using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSoft.DataPrivacy.EntityFrameworkCore.Infrastructure;
using DSoft.DataPrivacy.EntityFrameworkCore.Metadata;
using DSoft.DataPrivacy.EntityFrameworkCore.Querying;
using DSoft.DataPrivacy.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DSoft.DataPrivacy.EntityFrameworkCore.Erasure;

/// <summary>
/// The records an erasure or retention run will touch and what happens to each. It makes sure nothing is deleted
/// while a record that is kept still depends on it, then applies the outcomes to tracked entities.
/// </summary>
internal sealed class ErasurePlan
{
    private readonly DbContext _context;
    private readonly PersonalDataModel _model;
    private readonly DataPrivacyOptions _options;
    private readonly Dictionary<object, Planned> _planned = new(ReferenceEqualityComparer.Instance);
    private readonly List<Planned> _order = new();

    public ErasurePlan(DbContext context, PersonalDataModel model, DataPrivacyOptions options)
    {
        _context = context;
        _model = model;
        _options = options;
    }

    public bool Contains(object row) => _planned.ContainsKey(row);

    public int Count => _order.Count;

    public void Add(object row, PersonalDataEntity entity, ErasureOutcome outcome)
    {
        if (_planned.ContainsKey(row))
            return;

        var planned = new Planned(row, entity, outcome);
        _planned.Add(row, planned);
        _order.Add(planned);
    }

    /// <summary>The entity description for a tracked row.</summary>
    public PersonalDataEntity Describe(object row, IEntityType queried)
    {
        var entityType = EntityValues.ResolveType(_context, row, queried);
        return _model.Find(entityType) ?? throw new InvalidOperationException($"'{entityType.DisplayName()}' is not described by the personal data model.");
    }

    /// <summary>
    /// Keeps, anonymised, any record planned for deletion that another kept record depends on. Repeats until no
    /// more change, because keeping one record can mean keeping the record it depends on.
    /// </summary>
    public async Task KeepRecordsOthersDependOnAsync(CancellationToken cancellationToken)
    {
        var dependentsCache = new Dictionary<(object, IForeignKey), List<object>>();
        bool changed;

        do
        {
            changed = false;

            foreach (var planned in _order.Where(p => p.Outcome.Action == ErasureAction.Delete).ToList())
            {
                foreach (var foreignKey in planned.Entity.EntityType.GetReferencingForeignKeys())
                {
                    if (foreignKey.IsOwnership)
                        continue;

                    // An optional relationship that sets null on delete survives the principal going.
                    if (!foreignKey.IsRequired && foreignKey.DeleteBehavior is DeleteBehavior.SetNull or DeleteBehavior.ClientSetNull)
                        continue;

                    if (!dependentsCache.TryGetValue((planned.Row, foreignKey), out var dependents))
                    {
                        var principalKey = EntityValues.GetKey(_context, planned.Row, foreignKey.PrincipalKey.Properties);
                        dependents = principalKey.Any(v => v == null)
                            ? new List<object>()
                            : await SubjectQuery.LoadDependentsAsync(_context, foreignKey.DeclaringEntityType, foreignKey, principalKey, cancellationToken).ConfigureAwait(false);
                        dependentsCache[(planned.Row, foreignKey)] = dependents;
                    }

                    if (dependents.Any(d => !ReferenceEquals(d, planned.Row) && !(_planned.TryGetValue(d, out var p) && p.Outcome.Action == ErasureAction.Delete)))
                    {
                        planned.Outcome = ErasureDecision.KeepForDependents();
                        changed = true;
                        break;
                    }
                }
            }
        }
        while (changed);
    }

    /// <summary>Applies every outcome to the tracked entities, or only describes it on a dry run.</summary>
    public async Task<List<ErasureLogEntry>> ApplyAsync(bool dryRun, Func<ErasureRecord, CancellationToken, Task>? onRecord, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var log = new List<ErasureLogEntry>();

        foreach (var planned in _order)
        {
            if (!dryRun && onRecord != null)
                await onRecord(new ErasureRecord(planned.Entity, planned.Row, planned.Outcome), cancellationToken).ConfigureAwait(false);

            var anonymised = new List<string>();
            var retained = new List<string>();

            switch (planned.Outcome.Action)
            {
                case ErasureAction.Delete:
                    if (!dryRun)
                        _context.Remove(planned.Row);
                    break;

                case ErasureAction.Anonymise:
                    Anonymise(planned, dryRun, now, anonymised, retained);
                    break;
            }

            log.Add(new ErasureLogEntry
            {
                Entity = planned.Entity.Name,
                Key = FormatKey(planned),
                Action = planned.Outcome.Action,
                Exemption = planned.Outcome.Exemption,
                Reason = planned.Outcome.Reason,
                Anonymised = anonymised,
                Retained = retained,
            });
        }

        return log;
    }

    private void Anonymise(Planned planned, bool dryRun, DateTimeOffset now, List<string> anonymised, List<string> retained)
    {
        foreach (var property in planned.Entity.Properties)
        {
            var scalar = property.Property;
            if (scalar.IsKey() || scalar.IsForeignKey())
                continue;

            var decision = ErasureDecision.DecideField(new FieldErasureFacts
            {
                Categories = property.Categories,
                Erasure = property.Classification.Erasure,
                RetentionExemption = property.Classification.RetentionExemption,
                Method = property.Classification.Anonymisation,
                ValueType = Nullable.GetUnderlyingType(scalar.ClrType) ?? scalar.ClrType,
                IsNullable = scalar.IsNullable,
            });

            if (decision.Action == ErasureAction.Retain)
            {
                retained.Add($"{property.Name} ({decision.Exemption})");
                continue;
            }

            var changed = false;
            foreach (var instance in Instances(planned.Row, property.OwnerPath))
            {
                var entry = _context.Entry(instance);
                var current = entry.Property(scalar.Name).CurrentValue;
                var replacement = ValueAnonymiser.Anonymise(decision.Method, planned.Entity.EntityType, scalar, current, _options, property.Classification.Anonymiser);
                if (Equals(current, replacement))
                    continue;

                changed = true;
                if (!dryRun)
                    entry.Property(scalar.Name).CurrentValue = replacement;
            }

            if (changed)
                anonymised.Add(property.Name);
        }

        var marker = planned.Entity.AnonymisedAt;
        if (marker != null && !dryRun)
        {
            var type = Nullable.GetUnderlyingType(marker.ClrType) ?? marker.ClrType;
            // Cast each branch: a plain conditional would convert the DateTime to a DateTimeOffset.
            var value = type == typeof(DateTimeOffset) ? (object)now : now.UtcDateTime;
            _context.Entry(planned.Row).Property(marker.Name).CurrentValue = value;
        }
    }

    private static IEnumerable<object> Instances(object row, IReadOnlyList<INavigation> ownerPath)
    {
        IEnumerable<object> current = new[] { row };
        foreach (var navigation in ownerPath)
        {
            current = current.SelectMany(instance =>
            {
                var value = navigation.PropertyInfo?.GetValue(instance) ?? navigation.FieldInfo?.GetValue(instance);
                return value switch
                {
                    null => Enumerable.Empty<object>(),
                    IEnumerable items when navigation.IsCollection => items.Cast<object>(),
                    _ => new[] { value },
                };
            }).ToList();
        }

        return current;
    }

    private string FormatKey(Planned planned)
    {
        var entityType = planned.Entity.EntityType;
        var key = entityType.FindPrimaryKey();
        if (key == null)
            return string.Empty;

        if (key.Properties.Any(p => PersonalDataModel.ReadClassification(p) != null))
        {
            if (_options.Hasher == null)
                return "[classified key]";

            var values = EntityValues.GetKey(_context, planned.Row, key.Properties);
            return "hash:" + _options.Hasher.Hash(string.Join("|", values));
        }

        return EntityValues.FormatKey(EntityValues.KeyMap(_context, planned.Row, entityType));
    }

    private sealed class Planned
    {
        public Planned(object row, PersonalDataEntity entity, ErasureOutcome outcome)
        {
            Row = row;
            Entity = entity;
            Outcome = outcome;
        }

        public object Row { get; }

        public PersonalDataEntity Entity { get; }

        public ErasureOutcome Outcome { get; set; }
    }
}
