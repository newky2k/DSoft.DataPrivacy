using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSoft.DataPrivacy.EntityFrameworkCore.Metadata;
using DSoft.DataPrivacy.Rules;

namespace DSoft.DataPrivacy.EntityFrameworkCore.Erasure;

/// <summary>Options for erasing a data subject.</summary>
public sealed class ErasureOptions
{
    /// <summary>Work out and return what would happen, without changing anything.</summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// Save the changes before returning. On by default. Turn it off to add your own records, such as a request
    /// log entry, and save them in the same transaction.
    /// </summary>
    public bool SaveChanges { get; set; } = true;

    /// <summary>Keep everything about the person because of an actual or expected legal claim.</summary>
    public bool LegalHold { get; set; }

    /// <summary>Keep individual records because of a legal claim. Called for each record found.</summary>
    public Func<PersonalDataEntity, object, bool>? IsOnLegalHold { get; set; }

    /// <summary>Controller-wide rules for this erasure, replacing the ones configured in <c>UseDataPrivacy</c>.</summary>
    public ErasurePolicy? Policy { get; set; }

    /// <summary>
    /// Called for each record before it is changed, with the entity instance, so data held outside the database
    /// (files, blobs, search indexes) can be removed too. Not called on a dry run.
    /// </summary>
    public Func<ErasureRecord, CancellationToken, Task>? OnRecord { get; set; }
}

/// <summary>A record about to be erased, passed to <see cref="ErasureOptions.OnRecord"/>.</summary>
/// <param name="Entity">The record's entity.</param>
/// <param name="Instance">The entity instance.</param>
/// <param name="Outcome">What will happen to it.</param>
public sealed record ErasureRecord(PersonalDataEntity Entity, object Instance, ErasureOutcome Outcome);

/// <summary>
/// One line of an erasure log. It names the record and what was done, but never holds a personal value, so it
/// can be kept as evidence of the erasure, and replayed after a backup is restored.
/// </summary>
public sealed record ErasureLogEntry
{
    /// <summary>The entity.</summary>
    public string Entity { get; init; } = string.Empty;

    /// <summary>The record's primary key, or <c>[classified key]</c> when the key itself is personal data.</summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>What was done.</summary>
    public ErasureAction Action { get; init; }

    /// <summary>The Article 17(3) ground, when the record or a value was retained.</summary>
    public ErasureExemption Exemption { get; init; }

    /// <summary>Why.</summary>
    public string Reason { get; init; } = string.Empty;

    /// <summary>The names of the values anonymised.</summary>
    public IReadOnlyList<string> Anonymised { get; init; } = Array.Empty<string>();

    /// <summary>The names of values kept on an anonymised record, with their ground.</summary>
    public IReadOnlyList<string> Retained { get; init; } = Array.Empty<string>();
}

/// <summary>What an erasure did, or would do on a dry run.</summary>
public sealed class ErasureResult
{
    /// <summary>The data subject's entity.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>True when the data subject was found.</summary>
    public bool Found { get; init; }

    /// <summary>True when nothing was changed.</summary>
    public bool DryRun { get; init; }

    /// <summary>When the erasure ran.</summary>
    public DateTimeOffset At { get; init; }

    /// <summary>One entry per record.</summary>
    public IReadOnlyList<ErasureLogEntry> Entries { get; init; } = Array.Empty<ErasureLogEntry>();

    /// <summary>Records deleted.</summary>
    public int Deleted => Entries.Count(e => e.Action == ErasureAction.Delete);

    /// <summary>Records anonymised.</summary>
    public int Anonymised => Entries.Count(e => e.Action == ErasureAction.Anonymise);

    /// <summary>Records retained.</summary>
    public int Retained => Entries.Count(e => e.Action == ErasureAction.Retain);
}
