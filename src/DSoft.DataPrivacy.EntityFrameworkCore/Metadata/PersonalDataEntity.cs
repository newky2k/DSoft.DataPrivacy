using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DSoft.DataPrivacy.EntityFrameworkCore.Metadata;

/// <summary>A classified property, possibly on an owned type reached through <see cref="OwnerPath"/>.</summary>
public sealed class PersonalDataProperty
{
    internal PersonalDataProperty(IProperty property, IReadOnlyList<INavigation> ownerPath, PersonalDataPropertyClassification classification)
    {
        Property = property;
        OwnerPath = ownerPath;
        Classification = classification;
        Name = string.Join(".", ownerPath.Select(n => n.Name).Append(property.Name));
    }

    /// <summary>The Entity Framework property.</summary>
    public IProperty Property { get; }

    /// <summary>The owned navigations from the entity to the type declaring the property; empty for the entity's own properties.</summary>
    public IReadOnlyList<INavigation> OwnerPath { get; }

    /// <summary>The property's name, prefixed by any owned navigations, such as <c>HomeAddress.Postcode</c>.</summary>
    public string Name { get; }

    /// <summary>How the property is classified.</summary>
    public PersonalDataPropertyClassification Classification { get; }

    /// <summary>What kind of personal data the value is.</summary>
    public PersonalDataCategory Categories => Classification.Categories;

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({Categories.Describe()})";
}

/// <summary>A path of foreign keys from a record to a data subject.</summary>
public sealed class DataSubjectLink
{
    internal DataSubjectLink(IReadOnlyList<IForeignKey> path, DataSubjectLinkKind kind)
    {
        Path = path;
        Kind = kind;
    }

    /// <summary>The foreign keys to follow, starting on the record's entity and ending at the data subject.</summary>
    public IReadOnlyList<IForeignKey> Path { get; }

    /// <summary><see cref="DataSubjectLinkKind.Owner"/> or <see cref="DataSubjectLinkKind.Reference"/>.</summary>
    public DataSubjectLinkKind Kind { get; }

    /// <summary>The data subject the path ends at.</summary>
    public IEntityType Subject => Path[Path.Count - 1].PrincipalEntityType;

    /// <summary>The entity the path starts from.</summary>
    public IEntityType Dependent => Path[0].DeclaringEntityType;

    /// <summary>A readable form, such as <c>OrderLine.OrderId → Order.CustomerId → Customer</c>.</summary>
    public string Describe()
        => string.Join(" → ", Path.Select(fk => $"{fk.DeclaringEntityType.ShortName()}.{string.Join("+", fk.Properties.Select(p => p.Name))}"))
            + " → " + Subject.ShortName();

    /// <inheritdoc />
    public override string ToString() => $"{Describe()} ({Kind})";
}

/// <summary>What the model says about one entity type's personal data.</summary>
public sealed class PersonalDataEntity
{
    internal PersonalDataEntity(IEntityType entityType)
    {
        EntityType = entityType;
    }

    /// <summary>The entity type.</summary>
    public IEntityType EntityType { get; }

    /// <summary>The entity type's short display name.</summary>
    public string Name => EntityType.ShortName();

    /// <summary>The CLR type.</summary>
    public Type ClrType => EntityType.ClrType;

    /// <summary>The table the entity is mapped to, when it uses a relational provider.</summary>
    public string? Table => EntityType.FindAnnotation("Relational:TableName")?.Value as string
        ?? EntityType.GetRootType().FindAnnotation("Relational:TableName")?.Value as string;

    /// <summary>True when the entity is a data subject.</summary>
    public bool IsDataSubject { get; internal set; }

    /// <summary>The data class the entity's records belong to.</summary>
    public string? DataClass { get; internal set; }

    /// <summary>What happens to the entity's records on erasure.</summary>
    public ErasureAction Erasure { get; internal set; }

    /// <summary>The retention ground, when the records are retained.</summary>
    public RetentionGround RetentionGround { get; internal set; }

    /// <summary>Why the records are retained.</summary>
    public string? RetentionReason { get; internal set; }

    /// <summary>A human description of the records.</summary>
    public string? Description { get; internal set; }

    /// <summary>Classified properties, including those on owned types.</summary>
    public IReadOnlyList<PersonalDataProperty> Properties { get; internal set; } = Array.Empty<PersonalDataProperty>();

    /// <summary>Properties recorded as not personal data.</summary>
    public IReadOnlyList<IProperty> NotPersonalData { get; internal set; } = Array.Empty<IProperty>();

    /// <summary>Paths from this entity to a data subject. Empty for a data subject itself unless it refers to another.</summary>
    public IReadOnlyList<DataSubjectLink> Links { get; internal set; } = Array.Empty<DataSubjectLink>();

    /// <summary>Dates a retention period can be counted from.</summary>
    public IReadOnlyDictionary<RetentionTrigger, IProperty> RetentionTriggers { get; internal set; } = new Dictionary<RetentionTrigger, IProperty>();

    /// <summary>The date set when a record is anonymised, if the entity has one.</summary>
    public IProperty? AnonymisedAt { get; internal set; }

    /// <summary>The union of every classified property's categories.</summary>
    public PersonalDataCategory Categories => Properties.Aggregate(PersonalDataCategory.None, (all, p) => all | p.Categories);

    /// <summary>True when records hold classified data, or belong to a data subject.</summary>
    public bool HoldsPersonalData => IsDataSubject || Properties.Count > 0 || Links.Any(l => l.Kind == DataSubjectLinkKind.Owner);

    /// <summary>The links to <paramref name="subject"/>.</summary>
    public IEnumerable<DataSubjectLink> LinksTo(IEntityType subject) => Links.Where(l => l.Subject == subject);

    /// <inheritdoc />
    public override string ToString() => Name;
}
