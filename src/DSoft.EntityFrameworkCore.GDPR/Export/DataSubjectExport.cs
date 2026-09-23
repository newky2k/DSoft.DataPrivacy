using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DSoft.EntityFrameworkCore.GDPR.Export;

/// <summary>How much of a record that only mentions the person is included in an export.</summary>
public enum ReferenceDetail
{
    /// <summary>The record's key and how it refers to the person.</summary>
    KeysOnly = 0,

    /// <summary>Also the record's classified values. They may be about other people; review before release.</summary>
    ClassifiedFields = 1,

    /// <summary>Every value on the record. They may be about other people; review before release.</summary>
    AllFields = 2,
}

/// <summary>Options for a subject access export.</summary>
public sealed class DataSubjectExportOptions
{
    /// <summary>How much of records that only mention the person to include. Defaults to <see cref="ReferenceDetail.KeysOnly"/>.</summary>
    public ReferenceDetail ReferenceDetail { get; set; } = ReferenceDetail.KeysOnly;

    /// <summary>Include values classified as <see cref="PersonalDataCategory.Credential"/>. Off by default: they are withheld.</summary>
    public bool IncludeCredentials { get; set; }

    /// <summary>Include values that are not classified. On by default: a record about the person is their data as a whole.</summary>
    public bool IncludeUnclassifiedFields { get; set; } = true;

    /// <summary>A reference for the request, such as its case number, copied into the export.</summary>
    public string? RequestReference { get; set; }
}

/// <summary>
/// Everything held about one data subject, grouped by entity. Serialise it with <see cref="ToJson"/> for a
/// machine-readable answer to an access (Article 15) or portability (Article 20) request.
/// </summary>
public sealed class DataSubjectExport
{
    /// <summary>The data subject's entity.</summary>
    public string Subject { get; init; } = string.Empty;

    /// <summary>The data subject's key.</summary>
    public IReadOnlyDictionary<string, object?> SubjectKey { get; init; } = new Dictionary<string, object?>();

    /// <summary>The request reference, when one was given.</summary>
    public string? RequestReference { get; init; }

    /// <summary>When the export was produced.</summary>
    public DateTimeOffset GeneratedAt { get; init; }

    /// <summary>The records found, grouped by entity and relationship. The data subject's own record comes first.</summary>
    public List<DataSubjectExportSection> Sections { get; init; } = new();

    /// <summary>Things the export could not include, such as entity types it cannot query. Review them by hand.</summary>
    public List<string> Notes { get; init; } = new();

    /// <summary>True when a value holds free text, or a record only mentions the person, and should be reviewed for other people's data before release.</summary>
    [JsonInclude]
    public bool RequiresReview => Sections.Any(s => s.Kind == DataSubjectLinkKind.Reference && s.Records.Count > 0)
        || Sections.SelectMany(s => s.Records).SelectMany(r => r.Fields).Any(f => f.NeedsReview);

    /// <summary>The total number of records.</summary>
    [JsonInclude]
    public int RecordCount => Sections.Sum(s => s.Records.Count);

    /// <summary>The export as JSON, with camel-cased names and enum names as text.</summary>
    public string ToJson(bool indented = true)
        => JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            WriteIndented = indented,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Converters = { new JsonStringEnumConverter() },
        });
}

/// <summary>The records of one entity reached through one relationship.</summary>
public sealed class DataSubjectExportSection
{
    /// <summary>The entity.</summary>
    public string Entity { get; init; } = string.Empty;

    /// <summary>A human description of the entity, when configured.</summary>
    public string? Description { get; init; }

    /// <summary>How the records relate to the person, such as <c>Order.CustomerId → Customer</c>, or <c>(data subject)</c>.</summary>
    public string Relationship { get; init; } = string.Empty;

    /// <summary>Whether the records are about the person, or only mention them.</summary>
    public DataSubjectLinkKind Kind { get; init; }

    /// <summary>The records.</summary>
    public List<DataSubjectExportRecord> Records { get; init; } = new();
}

/// <summary>One record.</summary>
public sealed class DataSubjectExportRecord
{
    /// <summary>The record's primary key.</summary>
    public IReadOnlyDictionary<string, object?> Key { get; init; } = new Dictionary<string, object?>();

    /// <summary>The record's values.</summary>
    public List<DataSubjectExportField> Fields { get; init; } = new();
}

/// <summary>One value.</summary>
public sealed class DataSubjectExportField
{
    /// <summary>The property, prefixed by owned navigations.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>The value, or <c>null</c> when it is empty or withheld.</summary>
    public object? Value { get; init; }

    /// <summary>The value's categories, when it is classified.</summary>
    public string? Categories { get; init; }

    /// <summary>True when the value is free text that may hold other people's data.</summary>
    public bool NeedsReview { get; init; }

    /// <summary>True when the value was withheld, such as a credential.</summary>
    public bool Withheld { get; init; }
}
