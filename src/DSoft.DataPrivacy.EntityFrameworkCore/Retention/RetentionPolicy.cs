using System;
using System.Collections.Generic;
using System.Linq;
using DSoft.DataPrivacy.EntityFrameworkCore.Erasure;
using DSoft.DataPrivacy.Rules;

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

    /// <summary>Entities in the data class the policy could not run on, and why.</summary>
    public IReadOnlyList<string> Skipped { get; init; } = Array.Empty<string>();

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
