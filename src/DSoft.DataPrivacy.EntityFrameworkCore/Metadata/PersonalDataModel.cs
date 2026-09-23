using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DSoft.DataPrivacy.EntityFrameworkCore.Metadata;

/// <summary>
/// The personal data an Entity Framework model holds: which entities are data subjects, which properties are
/// classified, and how every record finds its way to the person it is about.
/// </summary>
/// <remarks>
/// Relationships are followed from each record towards a data subject through foreign keys. A relationship's kind
/// is taken from <see cref="DataSubjectKeyAttribute"/> or <c>IsDataSubjectKey</c> where declared; otherwise a
/// required relationship that cascades on delete is <see cref="DataSubjectLinkKind.Owner"/>, and anything else is
/// <see cref="DataSubjectLinkKind.Reference"/>. A path through several relationships is only an owner link when
/// every step is.
/// </remarks>
public sealed class PersonalDataModel
{
    private static readonly ConditionalWeakTable<IModel, Dictionary<int, PersonalDataModel>> Cache = new();

    private readonly Dictionary<IEntityType, PersonalDataEntity> _entities;

    private PersonalDataModel(IModel model, int maxLinkDepth)
    {
        Model = model;

        if (model.FindAnnotation(PrivacyAnnotationNames.ConventionsApplied) == null)
        {
            throw new InvalidOperationException(
                "The data privacy conventions have not run for this model. Call UseDataPrivacy() when configuring the DbContext.");
        }

        _entities = model.GetEntityTypes()
            .Where(e => !e.IsOwned())
            .ToDictionary(e => e, Describe);

        var subjects = _entities.Values.Where(e => e.IsDataSubject).Select(e => e.EntityType).ToList();
        var linkFinder = new LinkFinder(subjects, maxLinkDepth);
        foreach (var entity in _entities.Values)
            entity.Links = linkFinder.Find(entity.EntityType);

        Entities = _entities.Values.OrderBy(e => e.IsDataSubject ? 0 : 1).ThenBy(e => e.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>The Entity Framework model described.</summary>
    public IModel Model { get; }

    /// <summary>Every entity type that is not owned, data subjects first.</summary>
    public IReadOnlyList<PersonalDataEntity> Entities { get; }

    /// <summary>The data subjects.</summary>
    public IEnumerable<PersonalDataEntity> DataSubjects => Entities.Where(e => e.IsDataSubject);

    /// <summary>
    /// Entities that hold classified personal data but have no path to any data subject. A subject access
    /// request or erasure cannot find their records, so each needs a relationship or a decision.
    /// </summary>
    public IEnumerable<PersonalDataEntity> Unlinked => Entities.Where(e => !e.IsDataSubject && e.Properties.Count > 0 && e.Links.Count == 0);

    /// <summary>Describes a model, caching the result per model.</summary>
    public static PersonalDataModel For(IModel model, int maxLinkDepth = 4)
    {
        if (model == null)
            throw new ArgumentNullException(nameof(model));

        var byDepth = Cache.GetOrCreateValue(model);
        lock (byDepth)
        {
            if (!byDepth.TryGetValue(maxLinkDepth, out var result))
            {
                result = new PersonalDataModel(model, maxLinkDepth);
                byDepth[maxLinkDepth] = result;
            }

            return result;
        }
    }

    /// <summary>The description of an entity type, or <c>null</c> for an owned or unknown type.</summary>
    public PersonalDataEntity? Find(IEntityType entityType)
        => _entities.TryGetValue(entityType, out var entity) ? entity : null;

    /// <summary>The description of the entity type mapped to <paramref name="clrType"/>.</summary>
    public PersonalDataEntity? Find(Type clrType)
    {
        var entityType = Model.FindEntityType(clrType);
        return entityType == null ? null : Find(entityType);
    }

    /// <summary>The data subject mapped to <paramref name="clrType"/>, or an exception explaining why there is none.</summary>
    public PersonalDataEntity GetDataSubject(Type clrType)
    {
        var entity = Find(clrType) ?? throw new InvalidOperationException($"'{clrType.Name}' is not an entity type in this model.");
        if (!entity.IsDataSubject)
            throw new InvalidOperationException($"'{entity.Name}' is not a data subject. Mark it with [DataSubject] or IsDataSubject().");

        return entity;
    }

    /// <summary>Entities with at least one link to <paramref name="subject"/>, with those links.</summary>
    public IEnumerable<(PersonalDataEntity Entity, IReadOnlyList<DataSubjectLink> Links)> LinkedTo(IEntityType subject)
    {
        foreach (var entity in Entities)
        {
            var links = entity.LinksTo(subject).ToList();
            if (links.Count > 0)
                yield return (entity, links);
        }
    }

    /// <summary>
    /// One row per classified property, for a data inventory or record of processing. With a regime, special data
    /// follows that law's definition and retention grounds carry their citation.
    /// </summary>
    public PersonalDataInventory Inventory(Regimes.PrivacyRegime? regime = null) => PersonalDataInventory.Create(this, regime);

    /// <summary>
    /// Finds properties on entities that hold personal data which carry no classification and no decision that
    /// they are not personal data. Only entities that are data subjects or linked to one are checked.
    /// </summary>
    public IReadOnlyList<PersonalDataGap> FindUnclassified(PersonalDataCoverageOptions? options = null)
    {
        options ??= new PersonalDataCoverageOptions();
        var gaps = new List<PersonalDataGap>();

        foreach (var entity in Entities.Where(e => e.IsDataSubject || e.Links.Count > 0))
        {
            foreach (var (property, path) in AllProperties(entity.EntityType, Array.Empty<INavigation>()))
            {
                if (property.IsKey() || property.IsForeignKey() || property.IsShadowProperty() || property.IsConcurrencyToken)
                    continue;
                if (!options.IsCandidateType(property.ClrType) || options.IgnoredPropertyNames.Contains(property.Name))
                    continue;
                if (property.PropertyInfo != null && options.Ignore?.Invoke(property.PropertyInfo) == true)
                    continue;
                if (property.FindAnnotation(PrivacyAnnotationNames.Categories) != null || property.FindAnnotation(PrivacyAnnotationNames.NotPersonalData) != null)
                    continue;

                gaps.Add(new PersonalDataGap(entity, property, string.Join(".", path.Select(n => n.Name).Append(property.Name))));
            }
        }

        return gaps;
    }

    internal static IEnumerable<(IProperty Property, IReadOnlyList<INavigation> Path)> AllProperties(IEntityType entityType, IReadOnlyList<INavigation> path)
    {
        foreach (var property in entityType.GetProperties())
            yield return (property, path);

        foreach (var navigation in entityType.GetNavigations().Where(n => n.TargetEntityType.IsOwned() && n.ForeignKey.IsOwnership && !n.IsOnDependent))
        {
            var ownedPath = path.Append(navigation).ToList();
            foreach (var owned in AllProperties(navigation.TargetEntityType, ownedPath))
            {
                // The owner's key is repeated on the owned type; it is not data of its own.
                if (owned.Property.IsKey() && owned.Property.IsForeignKey())
                    continue;

                yield return owned;
            }
        }
    }

    private static PersonalDataEntity Describe(IEntityType entityType)
    {
        var entity = new PersonalDataEntity(entityType)
        {
            IsDataSubject = entityType.FindAnnotation(PrivacyAnnotationNames.IsDataSubject)?.Value is true,
            DataClass = entityType.FindAnnotation(PrivacyAnnotationNames.DataClass)?.Value as string,
            Erasure = (ErasureAction)(entityType.FindAnnotation(PrivacyAnnotationNames.Erasure)?.Value as int? ?? 0),
            RetentionGround = (RetentionGround)(entityType.FindAnnotation(PrivacyAnnotationNames.RetentionGround)?.Value as int? ?? 0),
            RetentionReason = entityType.FindAnnotation(PrivacyAnnotationNames.RetentionReason)?.Value as string,
            Description = entityType.FindAnnotation(PrivacyAnnotationNames.Description)?.Value as string,
        };

        var properties = new List<PersonalDataProperty>();
        var notPersonal = new List<IProperty>();
        var triggers = new Dictionary<RetentionTrigger, IProperty>();

        foreach (var (property, path) in AllProperties(entityType, Array.Empty<INavigation>()))
        {
            var classification = ReadClassification(property);
            if (classification != null)
                properties.Add(new PersonalDataProperty(property, path, classification));
            else if (property.FindAnnotation(PrivacyAnnotationNames.NotPersonalData) != null)
                notPersonal.Add(property);

            if (path.Count == 0)
            {
                if (property.FindAnnotation(PrivacyAnnotationNames.RetentionTrigger)?.Value is int trigger)
                    triggers[(RetentionTrigger)trigger] = property;
                if (property.FindAnnotation(PrivacyAnnotationNames.AnonymisedAt)?.Value is true)
                    entity.AnonymisedAt = property;
            }
        }

        entity.Properties = properties;
        entity.NotPersonalData = notPersonal;
        entity.RetentionTriggers = triggers;
        return entity;
    }

    internal static PersonalDataPropertyClassification? ReadClassification(IReadOnlyProperty property)
    {
        if (property.FindAnnotation(PrivacyAnnotationNames.Categories)?.Value is not long categories)
            return null;

        return new PersonalDataPropertyClassification
        {
            Categories = (PersonalDataCategory)categories,
            DataClass = property.FindAnnotation(PrivacyAnnotationNames.DataClass)?.Value as string,
            Anonymisation = (AnonymisationMethod)(property.FindAnnotation(PrivacyAnnotationNames.Anonymisation)?.Value as int? ?? 0),
            Anonymiser = property.FindAnnotation(PrivacyAnnotationNames.Anonymiser)?.Value as string,
            Erasure = (ErasureAction)(property.FindAnnotation(PrivacyAnnotationNames.Erasure)?.Value as int? ?? 0),
            RetentionGround = (RetentionGround)(property.FindAnnotation(PrivacyAnnotationNames.RetentionGround)?.Value as int? ?? 0),
            Description = property.FindAnnotation(PrivacyAnnotationNames.Description)?.Value as string,
            Source = property.PropertyInfo != null && PersonalDataAttributeReader.Classify(property.PropertyInfo) != null
                ? ClassificationSource.Attribute
                : ClassificationSource.Explicit,
        };
    }

    private sealed class LinkFinder
    {
        private readonly HashSet<IEntityType> _subjects;
        private readonly int _maxDepth;

        public LinkFinder(IEnumerable<IEntityType> subjects, int maxDepth)
        {
            _subjects = new HashSet<IEntityType>(subjects.SelectMany(s => s.GetDerivedTypesInclusive()));
            _maxDepth = maxDepth;
        }

        public IReadOnlyList<DataSubjectLink> Find(IEntityType entityType)
            => Find(entityType, new HashSet<IEntityType>(), _maxDepth);

        private IReadOnlyList<DataSubjectLink> Find(IEntityType entityType, HashSet<IEntityType> visiting, int depth)
        {
            var links = new List<DataSubjectLink>();
            if (depth == 0 || !visiting.Add(entityType))
                return links;

            // Only foreign keys declared on this type: a derived type's query already returns rows found through its base.
            foreach (var foreignKey in entityType.GetDeclaredForeignKeys())
            {
                if (foreignKey.IsOwnership)
                    continue;

                var declared = DeclaredKind(foreignKey);
                if (declared == DataSubjectLinkKind.None)
                    continue;

                var hop = declared != DataSubjectLinkKind.Default ? declared : InferredKind(foreignKey);
                var principal = foreignKey.PrincipalEntityType;

                if (_subjects.Contains(principal))
                {
                    links.Add(new DataSubjectLink(new[] { foreignKey }, hop));
                    continue;
                }

                if (principal.IsOwned())
                    continue;

                foreach (var onward in Find(principal, visiting, depth - 1))
                {
                    var kind = hop == DataSubjectLinkKind.Owner && onward.Kind == DataSubjectLinkKind.Owner
                        ? DataSubjectLinkKind.Owner
                        : DataSubjectLinkKind.Reference;
                    links.Add(new DataSubjectLink(new[] { foreignKey }.Concat(onward.Path).ToList(), kind));
                }
            }

            // Base-type relationships reach derived rows too.
            if (entityType.BaseType != null)
                links.AddRange(Find(entityType.BaseType, visiting, depth));

            visiting.Remove(entityType);
            return links;
        }

        private static DataSubjectLinkKind DeclaredKind(IForeignKey foreignKey)
        {
            foreach (var property in foreignKey.Properties)
            {
                if (property.FindAnnotation(PrivacyAnnotationNames.SubjectLinkKind)?.Value is int kind)
                    return (DataSubjectLinkKind)kind;
            }

            return DataSubjectLinkKind.Default;
        }

        private static DataSubjectLinkKind InferredKind(IForeignKey foreignKey)
            => foreignKey.IsRequired && foreignKey.DeleteBehavior is DeleteBehavior.Cascade or DeleteBehavior.ClientCascade
                ? DataSubjectLinkKind.Owner
                : DataSubjectLinkKind.Reference;
    }
}

/// <summary>A property on an entity that holds personal data, with no classification or decision.</summary>
public sealed record PersonalDataGap(PersonalDataEntity Entity, IProperty Property, string Name)
{
    /// <inheritdoc />
    public override string ToString() => $"{Entity.Name}.{Name} ({Property.ClrType.Name})";
}
