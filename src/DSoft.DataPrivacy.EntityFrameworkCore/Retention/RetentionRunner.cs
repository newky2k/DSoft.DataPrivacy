using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using DSoft.DataPrivacy.EntityFrameworkCore.Erasure;
using DSoft.DataPrivacy.EntityFrameworkCore.Infrastructure;
using DSoft.DataPrivacy.EntityFrameworkCore.Metadata;
using DSoft.DataPrivacy.EntityFrameworkCore.Querying;
using DSoft.DataPrivacy.Rules;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DSoft.DataPrivacy.EntityFrameworkCore.Retention;

/// <summary>Runs a retention policy in batches.</summary>
internal sealed class RetentionRunner
{
    private readonly DbContext _context;
    private readonly PersonalDataModel _model;
    private readonly DataPrivacyOptions _options;

    public RetentionRunner(DbContext context, PersonalDataModel model, DataPrivacyOptions options)
    {
        _context = context;
        _model = model;
        _options = options;
    }

    public async Task<RetentionRunResult> RunAsync(RetentionPolicy policy, RetentionRunOptions options, CancellationToken cancellationToken)
    {
        if (options.BatchSize < 1)
            throw new ArgumentOutOfRangeException(nameof(options), "The batch size must be at least 1.");

        var now = options.Now ?? DateTimeOffset.UtcNow;
        var cutoff = policy.Period.CutoffFrom(now);
        var skipped = new List<string>();
        var plan = new ErasurePlan(_context, _model, _options);
        var hasMore = false;
        var held = 0;
        var fullBatches = new List<(PersonalDataEntity Entity, List<object> Rows)>();

        foreach (var entity in _model.Entities.Where(e => string.Equals(e.DataClass, policy.DataClass, StringComparison.Ordinal)))
        {
            // A derived type is processed through its base type's query.
            if (entity.EntityType.BaseType != null && _model.Find(entity.EntityType.BaseType)?.DataClass == policy.DataClass)
                continue;

            if (entity.Erasure == ErasureAction.Retain && !policy.IncludeRetainedRecords)
            {
                skipped.Add($"{entity.Name}: retained on erasure ({entity.RetentionGround}); the policy does not include retained records.");
                continue;
            }

            if (!entity.RetentionTriggers.TryGetValue(policy.Trigger, out var trigger))
            {
                skipped.Add($"{entity.Name}: no date is marked as the {policy.Trigger} retention trigger.");
                continue;
            }

            if (policy.Action == ErasureAction.Anonymise && entity.AnonymisedAt == null)
            {
                skipped.Add($"{entity.Name}: anonymising needs a date marked [AnonymisedAt] so anonymised records are not selected again.");
                continue;
            }

            var predicate = Due(entity, trigger, cutoff, options.DateTimesAreUtc);
            var (rows, heldRows) = await LoadDueAsync(entity, predicate, plan, options, cancellationToken).ConfigureAwait(false);

            // A held record stays as it is, whatever the period says. Only its entity and key are reported.
            foreach (var row in heldRows)
                skipped.Add($"{entity.Name} {plan.FormatKey(row, plan.Describe(row, entity.EntityType))}: on legal hold; left untouched.");
            held += heldRows.Count;

            foreach (var row in rows)
            {
                var outcome = policy.Action == ErasureAction.Anonymise
                    ? ErasureOutcome.Anonymise($"Retention period {policy.Period} after {policy.Trigger} has ended.")
                    : ErasureOutcome.Delete($"Retention period {policy.Period} after {policy.Trigger} has ended.");
                plan.Add(row, plan.Describe(row, entity.EntityType), outcome);
            }

            if (rows.Count == options.BatchSize)
                fullBatches.Add((entity, rows));
        }

        await plan.KeepRecordsOthersDependOnAsync(cancellationToken).ConfigureAwait(false);
        var entries = await plan.ApplyAsync(options.DryRun, options.OnRecord, now, cancellationToken).ConfigureAwait(false);

        if (!options.DryRun)
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // A full batch only means more work if it changed something the next query will not select again:
        // a deletion, or an anonymisation that set the marker.
        foreach (var (entity, _) in fullBatches)
        {
            if (options.DryRun)
                break;

            if (entries.Any(e => e.Entity == entity.Name && (e.Action == ErasureAction.Delete || (e.Action == ErasureAction.Anonymise && entity.AnonymisedAt != null))))
                hasMore = true;
        }

        var kept = entries.Count(e => e.Action == ErasureAction.Anonymise && _model.Entities.Any(m => m.Name == e.Entity && m.AnonymisedAt == null));
        if (kept > 0)
            skipped.Add($"{kept} records could not be deleted because retained records depend on them, and have no [AnonymisedAt] date, so they will be selected again.");

        return new RetentionRunResult
        {
            DataClass = policy.DataClass,
            Cutoff = cutoff,
            Entries = entries,
            Skipped = skipped,
            Held = held,
            HasMore = hasMore,
        };
    }

    /// <summary>
    /// Loads a batch of due records, without those on legal hold. Held records stay due, so they would fill every
    /// batch and nothing behind them would ever be reached: the query is widened by the number found until the
    /// batch is full of records that can be processed, or there are no more.
    /// </summary>
    private async Task<(List<object> Rows, List<object> Held)> LoadDueAsync(PersonalDataEntity entity, LambdaExpression predicate, ErasurePlan plan, RetentionRunOptions options, CancellationToken cancellationToken)
    {
        var take = options.BatchSize;

        while (true)
        {
            var rows = await SubjectQuery.LoadAsync(_context, entity.EntityType, predicate, tracking: true, cancellationToken, take).ConfigureAwait(false);
            if (options.IsOnLegalHold == null)
                return (rows, new List<object>());

            var held = rows.Where(row => options.IsOnLegalHold(plan.Describe(row, entity.EntityType), row)).ToList();
            var released = rows.Except(held).ToList();

            if (released.Count >= options.BatchSize || rows.Count < take)
                return (released.Take(options.BatchSize).ToList(), held);

            take = options.BatchSize + held.Count;
        }
    }

    private static LambdaExpression Due(PersonalDataEntity entity, IProperty trigger, DateTimeOffset cutoff, bool utc)
    {
        var parameter = Expression.Parameter(entity.ClrType, "e");
        var type = trigger.ClrType;
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        object value = underlying == typeof(DateTimeOffset) ? cutoff
            : underlying == typeof(DateTime) ? (utc ? cutoff.UtcDateTime : cutoff.LocalDateTime)
            : underlying == typeof(DateOnly) ? DateOnly.FromDateTime(utc ? cutoff.UtcDateTime : cutoff.LocalDateTime)
            : throw new InvalidOperationException($"'{entity.Name}.{trigger.Name}' is a retention trigger but is not a date.");

        var nullableType = typeof(Nullable<>).MakeGenericType(underlying);
        Expression body = Expression.LessThanOrEqual(
            Expression.Convert(SubjectQuery.PropertyAccess(parameter, trigger), nullableType),
            SubjectQuery.Parameter(value, nullableType));

        if (entity.AnonymisedAt != null)
        {
            var markerType = entity.AnonymisedAt.ClrType;
            if (markerType.IsValueType && Nullable.GetUnderlyingType(markerType) == null)
                throw new InvalidOperationException($"'{entity.Name}.{entity.AnonymisedAt.Name}' marks anonymised records, so it must be nullable.");

            var marker = SubjectQuery.PropertyAccess(parameter, entity.AnonymisedAt);
            body = Expression.AndAlso(body, Expression.Equal(marker, Expression.Constant(null, markerType)));
        }

        return Expression.Lambda(body, parameter);
    }
}
