using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DSoft.DataPrivacy.EntityFrameworkCore.Metadata;
using DSoft.DataPrivacy.EntityFrameworkCore.Querying;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DSoft.DataPrivacy.EntityFrameworkCore.Export;

/// <summary>Collects everything held about a data subject.</summary>
internal sealed class DataSubjectExporter
{
    private readonly DbContext _context;
    private readonly PersonalDataModel _model;

    private readonly Regimes.PrivacyRegime? _regime;

    public DataSubjectExporter(DbContext context, PersonalDataModel model, Regimes.PrivacyRegime? regime)
    {
        _context = context;
        _model = model;
        _regime = regime;
    }

    public async Task<DataSubjectExport?> ExportAsync(Type subjectType, object?[] key, DataSubjectExportOptions options, CancellationToken cancellationToken)
    {
        var subject = _model.GetDataSubject(subjectType);
        var subjectRow = await EntityValues.FindAsync(_context, subject.EntityType, key, tracking: false, cancellationToken).ConfigureAwait(false);
        if (subjectRow == null)
            return null;

        var export = new DataSubjectExport
        {
            Subject = subject.Name,
            Regime = _regime?.Name,
            SubjectKey = EntityValues.KeyMap(_context, subjectRow, subject.EntityType),
            RequestReference = options.RequestReference,
            GeneratedAt = DateTimeOffset.UtcNow,
        };

        var subjectSection = new DataSubjectExportSection
        {
            Entity = subject.Name,
            Description = subject.Description,
            Relationship = "(data subject)",
            Kind = DataSubjectLinkKind.Owner,
        };
        subjectSection.Records.Add(Record(subjectRow, subject.EntityType, DataSubjectLinkKind.Owner, options));
        export.Sections.Add(subjectSection);

        foreach (var (entity, links) in _model.LinkedTo(subject.EntityType))
        {
            if (!SubjectQuery.CanQuery(entity.EntityType))
            {
                if (links.Any(l => l.Dependent == entity.EntityType))
                    export.Notes.Add($"'{entity.Name}' cannot be queried automatically; collect its records by hand.");
                continue;
            }

            foreach (var link in links.Where(l => l.Dependent == entity.EntityType))
            {
                var principalKey = EntityValues.GetKey(_context, subjectRow, link.Path[link.Path.Count - 1].PrincipalKey.Properties);
                var rows = await SubjectQuery.LoadAsync(_context, entity.EntityType, link, principalKey, tracking: false, cancellationToken).ConfigureAwait(false);
                if (rows.Count == 0)
                    continue;

                var section = new DataSubjectExportSection
                {
                    Entity = entity.Name,
                    Description = entity.Description,
                    Relationship = link.Describe(),
                    Kind = link.Kind,
                };

                foreach (var row in rows)
                    section.Records.Add(Record(row, entity.EntityType, link.Kind, options));

                export.Sections.Add(section);
            }
        }

        return export;
    }

    private DataSubjectExportRecord Record(object row, IEntityType queried, DataSubjectLinkKind kind, DataSubjectExportOptions options)
    {
        var entityType = EntityValues.ResolveType(_context, row, queried);
        var record = new DataSubjectExportRecord { Key = EntityValues.KeyMap(_context, row, entityType) };

        var detail = kind == DataSubjectLinkKind.Owner ? ReferenceDetail.AllFields : options.ReferenceDetail;
        if (detail == ReferenceDetail.KeysOnly)
            return record;

        AddFields(record, row, entityType, prefix: string.Empty, detail, options);
        return record;
    }

    private void AddFields(DataSubjectExportRecord record, object instance, IEntityType entityType, string prefix, ReferenceDetail detail, DataSubjectExportOptions options)
    {
        foreach (var property in entityType.GetProperties())
        {
            if ((property.IsShadowProperty() && !property.IsIndexerProperty()) || (prefix.Length > 0 && property.IsKey() && property.IsForeignKey()))
                continue;
            if (property.FindAnnotation(PrivacyAnnotationNames.NotPersonalData) != null)
                continue;

            var classification = PersonalDataModel.ReadClassification(property);
            if (classification == null && (detail != ReferenceDetail.AllFields || !options.IncludeUnclassifiedFields))
                continue;

            if (!EntityValues.TryGet(_context, instance, property, out var value))
                continue;

            var categories = classification?.Categories ?? PersonalDataCategory.None;
            var withheld = categories.HasAny(PersonalDataCategory.Credential) && !options.IncludeCredentials;

            record.Fields.Add(new DataSubjectExportField
            {
                Name = prefix + property.Name,
                Value = withheld ? null : value,
                Categories = classification == null ? null : categories.Describe(),
                NeedsReview = categories.HasAny(PersonalDataCategory.FreeText) && value != null,
                Withheld = withheld && value != null,
            });
        }

        foreach (var navigation in entityType.GetNavigations().Where(n => n.TargetEntityType.IsOwned() && n.ForeignKey.IsOwnership && !n.IsOnDependent))
        {
            if (!EntityValues.TryGet(_context, instance, navigation, out var owned) || owned == null)
                continue;

            if (navigation.IsCollection)
            {
                var index = 0;
                foreach (var item in (IEnumerable)owned)
                    AddFields(record, item, navigation.TargetEntityType, $"{prefix}{navigation.Name}[{index++}].", detail, options);
            }
            else
            {
                AddFields(record, owned, navigation.TargetEntityType, prefix + navigation.Name + ".", detail, options);
            }
        }
    }
}
