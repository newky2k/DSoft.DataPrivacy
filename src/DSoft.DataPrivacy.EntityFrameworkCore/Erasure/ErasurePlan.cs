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
using Microsoft.EntityFrameworkCore.ChangeTracking;
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

            // Deletions are applied together at the end, in an order of their own. The log keeps the plan's order.
            if (planned.Outcome.Action == ErasureAction.Anonymise)
                Anonymise(planned, dryRun, now, anonymised, retained);

            log.Add(new ErasureLogEntry
            {
                Entity = planned.Entity.Name,
                Key = FormatKey(planned.Row, planned.Entity),
                Action = planned.Outcome.Action,
                RetentionGround = planned.Outcome.RetentionGround,
                Citation = planned.Outcome.RetentionGround == RetentionGround.None ? null : _options.Regime?.Cite(planned.Outcome.RetentionGround),
                Reason = planned.Outcome.Reason,
                Anonymised = anonymised,
                Retained = retained,
            });
        }

        if (!dryRun)
            RemoveDeleted();

        return log;
    }

    /// <summary>
    /// Removes the records planned for deletion, each one after the planned records that point at it. Removing a
    /// record while one that requires it is still tracked and not yet deleted makes EF Core report the
    /// relationship as severed, although both are meant to go.
    /// </summary>
    private void RemoveDeleted()
    {
        var deleted = _order.Where(p => p.Outcome.Action == ErasureAction.Delete).ToList();
        if (deleted.Count == 0)
            return;

        var ordered = DependantsFirst(deleted, out var beforeDependant);
        var timing = _context.ChangeTracker.CascadeDeleteTiming;

        try
        {
            foreach (var planned in ordered)
            {
                // In a cycle some record has to go before one that requires it. Its dependants are then looked
                // at when the changes are saved, by which time every record in the cycle is marked as deleted.
                _context.ChangeTracker.CascadeDeleteTiming = beforeDependant.Contains(planned) ? CascadeTiming.OnSaveChanges : timing;
                _context.Remove(planned.Row);
            }
        }
        finally
        {
            _context.ChangeTracker.CascadeDeleteTiming = timing;
        }
    }

    /// <summary>
    /// Orders records so that each comes after the records in <paramref name="deleted"/> that point at it by
    /// foreign key, and otherwise in the order planned. A record that points at itself, or closes a cycle, cannot
    /// be ordered that way: it is reported in <paramref name="beforeDependant"/>.
    /// </summary>
    private List<Planned> DependantsFirst(List<Planned> deleted, out HashSet<Planned> beforeDependant)
    {
        var dependants = deleted.ToDictionary(p => p, _ => new List<Planned>());
        var principals = new Dictionary<IKey, Dictionary<object?[], Planned>>();

        foreach (var planned in deleted)
        {
            foreach (var foreignKey in planned.Entity.EntityType.GetForeignKeys())
            {
                if (foreignKey.IsOwnership)
                    continue;

                var values = EntityValues.GetKey(_context, planned.Row, foreignKey.Properties);
                if (values.Any(v => v == null))
                    continue;

                var key = foreignKey.PrincipalKey;
                if (!principals.TryGetValue(key, out var byKey))
                {
                    byKey = new Dictionary<object?[], Planned>(KeyValuesComparer.Instance);
                    foreach (var candidate in deleted.Where(p => key.DeclaringEntityType.IsAssignableFrom(p.Entity.EntityType)))
                        byKey[EntityValues.GetKey(_context, candidate.Row, key.Properties)] = candidate;
                    principals.Add(key, byKey);
                }

                if (byKey.TryGetValue(values, out var principal))
                    dependants[principal].Add(planned);
            }
        }

        // Depth first, without recursion: a record is placed once everything that points at it has been.
        var ordered = new List<Planned>(deleted.Count);
        var placed = new Dictionary<Planned, bool>();
        var path = new List<(Planned Record, int Next)>();
        beforeDependant = new HashSet<Planned>();

        foreach (var start in deleted)
        {
            if (placed.ContainsKey(start))
                continue;

            placed[start] = false;
            path.Add((start, 0));

            while (path.Count > 0)
            {
                var (record, next) = path[path.Count - 1];
                var pointingAtIt = dependants[record];

                if (next == pointingAtIt.Count)
                {
                    path.RemoveAt(path.Count - 1);
                    placed[record] = true;
                    ordered.Add(record);
                    continue;
                }

                path[path.Count - 1] = (record, next + 1);
                var dependant = pointingAtIt[next];

                if (!placed.TryGetValue(dependant, out var done))
                {
                    placed[dependant] = false;
                    path.Add((dependant, 0));
                }
                else if (!done)
                {
                    // The dependant is still waiting for this record, or is this record: a cycle.
                    beforeDependant.Add(record);
                }
            }
        }

        return ordered;
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
                RetentionGround = property.Classification.RetentionGround,
                Method = property.Classification.Anonymisation,
                ValueType = Nullable.GetUnderlyingType(scalar.ClrType) ?? scalar.ClrType,
                IsNullable = scalar.IsNullable,
            });

            if (decision.Action == ErasureAction.Retain)
            {
                var citation = _options.Regime?.Cite(decision.RetentionGround);
                retained.Add(citation == null ? $"{property.Name} ({decision.RetentionGround})" : $"{property.Name} ({decision.RetentionGround}, {citation})");
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

    /// <summary>A record's key for a log: hashed, or withheld, when the key is itself personal data.</summary>
    public string FormatKey(object row, PersonalDataEntity entity)
    {
        var entityType = entity.EntityType;
        var key = entityType.FindPrimaryKey();
        if (key == null)
            return string.Empty;

        if (key.Properties.Any(p => PersonalDataModel.ReadClassification(p) != null))
        {
            if (_options.Hasher == null)
                return "[classified key]";

            var values = EntityValues.GetKey(_context, row, key.Properties);
            return "hash:" + _options.Hasher.Hash(string.Join("|", values));
        }

        return EntityValues.FormatKey(EntityValues.KeyMap(_context, row, entityType));
    }

    /// <summary>Compares key values one by one, so two reads of the same key match.</summary>
    private sealed class KeyValuesComparer : IEqualityComparer<object?[]>
    {
        public static readonly KeyValuesComparer Instance = new();

        public bool Equals(object?[]? x, object?[]? y) => StructuralComparisons.StructuralEqualityComparer.Equals(x, y);

        public int GetHashCode(object?[] values) => StructuralComparisons.StructuralEqualityComparer.GetHashCode(values);
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
