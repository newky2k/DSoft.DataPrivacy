using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSoft.DataPrivacy.EntityFrameworkCore.Erasure;
using DSoft.DataPrivacy.EntityFrameworkCore.Export;
using DSoft.DataPrivacy.EntityFrameworkCore.Infrastructure;
using DSoft.DataPrivacy.EntityFrameworkCore.Metadata;
using DSoft.DataPrivacy.EntityFrameworkCore.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace DSoft.DataPrivacy;

/// <summary>How serious a <see cref="PersonalDataIssue"/> is.</summary>
public enum PersonalDataIssueSeverity
{
    /// <summary>Worth a look.</summary>
    Warning = 0,

    /// <summary>An operation will fail or miss data.</summary>
    Error = 1,
}

/// <summary>A problem with how the model describes personal data.</summary>
/// <param name="Severity">How serious it is.</param>
/// <param name="Entity">The entity it concerns.</param>
/// <param name="Message">What is wrong and what to do.</param>
public sealed record PersonalDataIssue(PersonalDataIssueSeverity Severity, string Entity, string Message)
{
    /// <inheritdoc />
    public override string ToString() => $"{Severity}: {Entity}: {Message}";
}

/// <summary>
/// The personal data operations for one context: the model, subject access export, erasure and retention.
/// Get one with <c>context.PersonalData()</c>.
/// </summary>
public sealed class PersonalDataOperations
{
    private readonly DbContext _context;

    internal PersonalDataOperations(DbContext context)
    {
        _context = context;

        var extension = context.GetService<IDbContextOptions>().FindExtension<DataPrivacyOptionsExtension>()
            ?? throw new InvalidOperationException("Data privacy support is not enabled for this context. Call UseDataPrivacy() when configuring it.");

        Options = extension.Options;
        Model = PersonalDataModel.For(context.Model, Options.MaxLinkDepth);
    }

    /// <summary>The options configured with <c>UseDataPrivacy</c>.</summary>
    public DataPrivacyOptions Options { get; }

    /// <summary>The personal data the model holds.</summary>
    public PersonalDataModel Model { get; }

    /// <summary>
    /// Collects everything held about a data subject, for an access (Article 15) or portability (Article 20)
    /// request. Returns <c>null</c> when there is no such subject. Nothing is tracked or changed.
    /// </summary>
    public Task<DataSubjectExport?> ExportAsync<TSubject>(object key, DataSubjectExportOptions? options = null, CancellationToken cancellationToken = default)
        where TSubject : class
        => ExportAsync(typeof(TSubject), KeyValues(key), options, cancellationToken);

    /// <inheritdoc cref="ExportAsync{TSubject}(object, DataSubjectExportOptions?, CancellationToken)"/>
    public Task<DataSubjectExport?> ExportAsync(Type subjectType, object?[] key, DataSubjectExportOptions? options = null, CancellationToken cancellationToken = default)
        => new DataSubjectExporter(_context, Model).ExportAsync(subjectType, key, options ?? new DataSubjectExportOptions(), cancellationToken);

    /// <summary>
    /// Erases a data subject (Article 17): deletes the records that belong to them, anonymises records that must
    /// stay because others depend on them, and keeps records retained under an exemption. The result is a log
    /// with no personal values in it.
    /// </summary>
    public Task<ErasureResult> EraseAsync<TSubject>(object key, ErasureOptions? options = null, CancellationToken cancellationToken = default)
        where TSubject : class
        => EraseAsync(typeof(TSubject), KeyValues(key), options, cancellationToken);

    /// <inheritdoc cref="EraseAsync{TSubject}(object, ErasureOptions?, CancellationToken)"/>
    public Task<ErasureResult> EraseAsync(Type subjectType, object?[] key, ErasureOptions? options = null, CancellationToken cancellationToken = default)
        => new DataSubjectEraser(_context, Model, Options).EraseAsync(subjectType, key, options ?? new ErasureOptions(), cancellationToken);

    /// <summary>
    /// Runs one batch of a retention policy: deletes or anonymises records of its data class whose retention
    /// period has ended. Call again while <see cref="RetentionRunResult.HasMore"/> is true.
    /// </summary>
    public Task<RetentionRunResult> ApplyRetentionAsync(RetentionPolicy policy, RetentionRunOptions? options = null, CancellationToken cancellationToken = default)
        => new RetentionRunner(_context, Model, Options).RunAsync(policy ?? throw new ArgumentNullException(nameof(policy)), options ?? new RetentionRunOptions(), cancellationToken);

    /// <summary>
    /// Checks the model for problems that would make an export incomplete or an erasure fail, such as personal
    /// data with no path to a data subject, or anonymisers that are not registered. Run it in a unit test.
    /// </summary>
    public IReadOnlyList<PersonalDataIssue> Validate()
    {
        var issues = new List<PersonalDataIssue>();

        if (!Model.DataSubjects.Any())
            issues.Add(new PersonalDataIssue(PersonalDataIssueSeverity.Error, "(model)", "No entity is marked as a data subject, so nothing can be exported or erased."));

        foreach (var entity in Model.Unlinked)
        {
            issues.Add(new PersonalDataIssue(PersonalDataIssueSeverity.Warning, entity.Name,
                "Holds personal data but has no relationship to a data subject, so exports and erasures cannot find its records."));
        }

        foreach (var entity in Model.Entities)
        {
            var subjects = entity.Links.Where(l => l.Kind == DataSubjectLinkKind.Owner && l.Dependent == entity.EntityType)
                .Select(l => l.Path[0]).Distinct().ToList();
            if (subjects.Count > 1)
            {
                issues.Add(new PersonalDataIssue(PersonalDataIssueSeverity.Warning, entity.Name,
                    $"Belongs to a data subject through {subjects.Count} relationships ({string.Join(", ", subjects.Select(fk => string.Join("+", fk.Properties.Select(p => p.Name))))}); erasing either person deletes the record. Declare the others as references with [DataSubjectKey(DataSubjectLinkKind.Reference)] if that is wrong."));
            }

            foreach (var property in entity.Properties)
            {
                var classification = property.Classification;
                var type = Nullable.GetUnderlyingType(property.Property.ClrType) ?? property.Property.ClrType;

                if (classification.Anonymisation == AnonymisationMethod.Hash)
                {
                    if (type != typeof(string))
                        issues.Add(new PersonalDataIssue(PersonalDataIssueSeverity.Error, entity.Name, $"'{property.Name}' is hashed on erasure but is not text."));
                    else if (Options.Hasher == null)
                        issues.Add(new PersonalDataIssue(PersonalDataIssueSeverity.Error, entity.Name, $"'{property.Name}' is hashed on erasure but no hash key is configured. Call UseHashKey."));
                    else if (property.Property.GetMaxLength() is int length && length < 64)
                        issues.Add(new PersonalDataIssue(PersonalDataIssueSeverity.Warning, entity.Name, $"'{property.Name}' holds {length} characters; hashes are 64 and will be cut short, making collisions more likely."));
                }

                if (classification.Anonymisation == AnonymisationMethod.Custom
                    && (classification.Anonymiser == null || Options.FindAnonymiser(classification.Anonymiser) == null))
                {
                    issues.Add(new PersonalDataIssue(PersonalDataIssueSeverity.Error, entity.Name, $"'{property.Name}' uses the anonymiser '{classification.Anonymiser}', which is not registered. Call AddAnonymiser."));
                }

                if (classification.Erasure == ErasureAction.Retain && classification.RetentionExemption == ErasureExemption.None)
                    issues.Add(new PersonalDataIssue(PersonalDataIssueSeverity.Error, entity.Name, $"'{property.Name}' is retained on erasure but names no Article 17(3) exemption."));

                if (property.Property.IsKey())
                    issues.Add(new PersonalDataIssue(PersonalDataIssueSeverity.Warning, entity.Name, $"'{property.Name}' is personal data used as a key; it cannot be anonymised, only deleted with its record."));
            }

            if (entity.AnonymisedAt != null && !entity.AnonymisedAt.IsNullable)
                issues.Add(new PersonalDataIssue(PersonalDataIssueSeverity.Error, entity.Name, $"'{entity.AnonymisedAt.Name}' marks anonymised records, so it must be nullable."));
        }

        return issues;
    }

    private object?[] KeyValues(object key) => key switch
    {
        null => throw new ArgumentNullException(nameof(key)),
        object?[] values => values,
        _ => new[] { key },
    };
}
