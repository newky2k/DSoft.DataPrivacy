using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSoft.DataPrivacy.EntityFrameworkCore.Infrastructure;
using DSoft.DataPrivacy.EntityFrameworkCore.Metadata;
using DSoft.DataPrivacy.EntityFrameworkCore.Querying;
using DSoft.DataPrivacy.Rules;
using Microsoft.EntityFrameworkCore;

namespace DSoft.DataPrivacy.EntityFrameworkCore.Erasure;

/// <summary>Carries out an erasure request across every record that belongs to a data subject.</summary>
internal sealed class DataSubjectEraser
{
    private readonly DbContext _context;
    private readonly PersonalDataModel _model;
    private readonly DataPrivacyOptions _options;

    public DataSubjectEraser(DbContext context, PersonalDataModel model, DataPrivacyOptions options)
    {
        _context = context;
        _model = model;
        _options = options;
    }

    public async Task<ErasureResult> EraseAsync(Type subjectType, object?[] key, ErasureOptions options, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var subject = _model.GetDataSubject(subjectType);
        var subjectRow = await EntityValues.FindAsync(_context, subject.EntityType, key, tracking: true, cancellationToken).ConfigureAwait(false);
        if (subjectRow == null)
            return new ErasureResult { Subject = subject.Name, Regime = _options.Regime?.Name, Found = false, DryRun = options.DryRun, At = now };

        var policy = options.Policy ?? _options.ErasurePolicy;
        var plan = new ErasurePlan(_context, _model, _options);

        var subjectEntity = plan.Describe(subjectRow, subject.EntityType);
        plan.Add(subjectRow, subjectEntity, Decide(subjectEntity, subjectRow, options, policy));

        // Records that belong to the person. Records that only mention them are left alone; the person they
        // point at is anonymised in place if they depend on it.
        foreach (var (entity, links) in _model.LinkedTo(subject.EntityType))
        {
            foreach (var link in links.Where(l => l.Kind == DataSubjectLinkKind.Owner && l.Dependent == entity.EntityType))
            {
                var principalKey = EntityValues.GetKey(_context, subjectRow, link.Path[link.Path.Count - 1].PrincipalKey.Properties);
                var rows = await SubjectQuery.LoadAsync(_context, entity.EntityType, link, principalKey, tracking: true, cancellationToken).ConfigureAwait(false);

                foreach (var row in rows.Where(r => !plan.Contains(r)))
                {
                    var described = plan.Describe(row, entity.EntityType);
                    plan.Add(row, described, Decide(described, row, options, policy));
                }
            }
        }

        await plan.KeepRecordsOthersDependOnAsync(cancellationToken).ConfigureAwait(false);

        var entries = await plan.ApplyAsync(options.DryRun, options.OnRecord, now, cancellationToken).ConfigureAwait(false);

        if (!options.DryRun && options.SaveChanges)
            await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return new ErasureResult
        {
            Subject = subject.Name,
            Regime = _options.Regime?.Name,
            Found = true,
            DryRun = options.DryRun,
            At = now,
            Entries = entries,
        };
    }

    private static ErasureOutcome Decide(PersonalDataEntity entity, object row, ErasureOptions options, ErasurePolicy policy)
        => ErasureDecision.DecideRecord(
            new RecordErasureFacts
            {
                Categories = entity.Categories,
                EntityErasure = entity.Erasure,
                EntityGround = entity.RetentionGround,
                RetentionReason = entity.RetentionReason,
                LegalHold = options.LegalHold || options.IsOnLegalHold?.Invoke(entity, row) == true,
            },
            policy);
}
