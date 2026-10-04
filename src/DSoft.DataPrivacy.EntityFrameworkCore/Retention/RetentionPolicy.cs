using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using DSoft.DataPrivacy.EntityFrameworkCore.Erasure;
using DSoft.DataPrivacy.EntityFrameworkCore.Metadata;
using DSoft.DataPrivacy.Rules;
using Microsoft.EntityFrameworkCore;

namespace DSoft.DataPrivacy.EntityFrameworkCore.Retention;

/// <summary>
/// How long records of one data class are kept, and what happens to them afterwards (storage limitation: GDPR
/// Article 5(1)(e), POPIA section 14). Policies usually live in configuration or the database so a data
/// protection or information officer can change them.
/// </summary>
/// <example>
/// <code>
/// var policy = new RetentionPolicy("Marketing.Leads", RetentionPeriod.FromMonths(18), RetentionTrigger.LastActivity);
/// </code>
/// </example>
public sealed class RetentionPolicy
{
    /// <summary>Creates a policy.</summary>
    public RetentionPolicy(string dataClass, RetentionPeriod period, RetentionTrigger trigger = RetentionTrigger.CreatedAt, ErasureAction action = ErasureAction.Delete)
    {
        if (string.IsNullOrWhiteSpace(dataClass))
            throw new ArgumentException("A retention policy applies to a data class.", nameof(dataClass));
        if (period.IsZero)
            throw new ArgumentException("A retention period cannot be zero.", nameof(period));
        if (action is not (ErasureAction.Delete or ErasureAction.Anonymise))
            throw new ArgumentException("A retention policy deletes or anonymises.", nameof(action));

        DataClass = dataClass;
        Period = period;
        Trigger = trigger;
        Action = action;
    }

    /// <summary>The data class the policy applies to, matched against entity data classes.</summary>
    public string DataClass { get; }

    /// <summary>How long records are kept after <see cref="Trigger"/>.</summary>
    public RetentionPeriod Period { get; }

    /// <summary>The event the period is counted from. Each entity needs a date marked with this trigger.</summary>
    public RetentionTrigger Trigger { get; }

    /// <summary><see cref="ErasureAction.Delete"/> or <see cref="ErasureAction.Anonymise"/>.</summary>
    public ErasureAction Action { get; }

    /// <summary>
    /// Also apply to entities configured to be retained on erasure. Off by default: records kept under a legal
    /// duty are only touched by a policy written for them.
    /// </summary>
    public bool IncludeRetainedRecords { get; set; }

    /// <summary>The conditions added with <c>Where</c>, each built for the context a run uses.</summary>
    internal List<(Type EntityType, Func<DbContext, LambdaExpression> Build)> Conditions { get; } = new();

    /// <summary>
    /// Adds a condition records of <typeparamref name="TEntity"/> must also meet before the policy applies to
    /// them, for rules a period cannot express, such as "kept while the record it is about still exists". It is
    /// part of the query that selects due records, so a record that fails it is never loaded: it is not counted,
    /// not held and not listed in <see cref="RetentionRunResult.Skipped"/>. Several conditions for one type must
    /// all be met. Entities with no condition are selected by the period alone.
    /// </summary>
    /// <remarks>
    /// The condition must be translatable by the database provider. A query on another set inside it must use
    /// the context the policy runs on: build the policy for that context, or use the overload that is given it.
    /// A condition on a base type applies to its derived types. Put the condition on the base type when a
    /// derived type shares its data class, because those records are selected through the base type.
    /// </remarks>
    /// <example>
    /// <code>
    /// var policy = new RetentionPolicy("Trails", RetentionPeriod.FromYears(8))
    ///     .Where&lt;AccessEntry&gt;(e => !db.People.Any(p => p.Id == e.PersonId &amp;&amp; p.AnonymisedAt == null));
    /// </code>
    /// </example>
    public RetentionPolicy Where<TEntity>(Expression<Func<TEntity, bool>> condition)
        where TEntity : class
    {
        if (condition == null)
            throw new ArgumentNullException(nameof(condition));

        Conditions.Add((typeof(TEntity), _ => condition));
        return this;
    }

    /// <summary>
    /// As <see cref="Where{TEntity}(Expression{Func{TEntity, bool}})"/>, for a policy that outlives a context:
    /// the condition is built on each run from the context the run uses.
    /// </summary>
    /// <example>
    /// <code>
    /// policy.Where&lt;AccessEntry&gt;(db => e => !db.Set&lt;Person&gt;().Any(p => p.Id == e.PersonId &amp;&amp; p.AnonymisedAt == null));
    /// </code>
    /// </example>
    public RetentionPolicy Where<TEntity>(Func<DbContext, Expression<Func<TEntity, bool>>> condition)
        where TEntity : class
    {
        if (condition == null)
            throw new ArgumentNullException(nameof(condition));

        Conditions.Add((typeof(TEntity), context => condition(context)
            ?? throw new InvalidOperationException($"The retention condition for '{typeof(TEntity).Name}' returned no expression.")));
        return this;
    }
}

/// <summary>Options for one run of a retention policy.</summary>
public sealed class RetentionRunOptions
{
    /// <summary>The time the run is for. Defaults to now.</summary>
    public DateTimeOffset? Now { get; set; }

    /// <summary>The most records to process per entity in one call. Defaults to 500.</summary>
    public int BatchSize { get; set; } = 500;

    /// <summary>
    /// True when <see cref="DateTime"/> columns hold UTC, which is the default; false when they hold local time.
    /// </summary>
    public bool DateTimesAreUtc { get; set; } = true;

    /// <summary>Work out what would happen, without changing anything.</summary>
    public bool DryRun { get; set; }

    /// <summary>Called for each record before it is changed. Not called on a dry run.</summary>
    public Func<ErasureRecord, System.Threading.CancellationToken, System.Threading.Tasks.Task>? OnRecord { get; set; }

    /// <summary>
    /// Keep individual records because of an actual or expected legal claim, whatever the retention period says.
    /// Called for each record that is due, with its entity and instance. A held record is neither deleted nor
    /// anonymised, and is named in <see cref="RetentionRunResult.Skipped"/>, on a dry run too. It takes the same
    /// test as <see cref="ErasureOptions.IsOnLegalHold"/>, so one can serve both.
    /// </summary>
    /// <remarks>
    /// Held records do not use up the batch: the run reads past them, so the records behind them are still
    /// processed. They stay due, and are reported again on every run until the hold is released.
    /// </remarks>
    public Func<PersonalDataEntity, object, bool>? IsOnLegalHold { get; set; }
}

/// <summary>What one run of a retention policy did.</summary>
public sealed class RetentionRunResult
{
    /// <summary>The data class.</summary>
    public string DataClass { get; init; } = string.Empty;

    /// <summary>Records older than this were due.</summary>
    public DateTimeOffset Cutoff { get; init; }

    /// <summary>One entry per record processed.</summary>
    public IReadOnlyList<ErasureLogEntry> Entries { get; init; } = Array.Empty<ErasureLogEntry>();

    /// <summary>
    /// Entities in the data class the policy could not run on, and why, and each record left untouched because it
    /// is on legal hold, named by entity and key.
    /// </summary>
    public IReadOnlyList<string> Skipped { get; init; } = Array.Empty<string>();

    /// <summary>Records that were due but left untouched because <see cref="RetentionRunOptions.IsOnLegalHold"/> held them.</summary>
    public int Held { get; init; }

    /// <summary>
    /// True when a batch was full and made progress, so calling again will process more. Call until it is false,
    /// for example from a scheduled job.
    /// </summary>
    public bool HasMore { get; init; }

    /// <summary>Records deleted.</summary>
    public int Deleted => Entries.Count(e => e.Action == ErasureAction.Delete);

    /// <summary>Records anonymised.</summary>
    public int Anonymised => Entries.Count(e => e.Action == ErasureAction.Anonymise);
}
