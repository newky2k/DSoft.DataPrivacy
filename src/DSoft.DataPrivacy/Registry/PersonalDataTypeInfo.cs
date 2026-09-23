using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DSoft.DataPrivacy;

/// <summary>Everything the attributes on one type say about the personal data it holds.</summary>
public sealed class PersonalDataTypeInfo
{
    internal PersonalDataTypeInfo(
        Type type,
        bool isDataSubject,
        string? dataClass,
        ErasureAction erasure,
        RetentionGround ground,
        string? retentionReason,
        string? description,
        IReadOnlyList<PersonalDataPropertyInfo> properties,
        IReadOnlyList<NotPersonalDataPropertyInfo> notPersonalData,
        IReadOnlyList<DataSubjectKeyInfo> subjectKeys,
        IReadOnlyList<RetentionTriggerInfo> retentionTriggers,
        PropertyInfo? anonymisedAt)
    {
        Type = type;
        IsDataSubject = isDataSubject;
        DataClass = dataClass;
        Erasure = erasure;
        RetentionGround = ground;
        RetentionReason = retentionReason;
        Description = description;
        Properties = properties;
        NotPersonalData = notPersonalData;
        SubjectKeys = subjectKeys;
        RetentionTriggers = retentionTriggers;
        AnonymisedAt = anonymisedAt;
    }

    /// <summary>The type described.</summary>
    public Type Type { get; }

    /// <summary>True when the type is marked <see cref="DataSubjectAttribute"/>.</summary>
    public bool IsDataSubject { get; }

    /// <summary>The data class from <see cref="PersonalDataEntityAttribute"/>.</summary>
    public string? DataClass { get; }

    /// <summary>What happens to a record on erasure; <see cref="ErasureAction.Retain"/> when <see cref="RetainOnErasureAttribute"/> is present.</summary>
    public ErasureAction Erasure { get; }

    /// <summary>The retention ground from <see cref="RetainOnErasureAttribute"/>.</summary>
    public RetentionGround RetentionGround { get; }

    /// <summary>The reason given on <see cref="RetainOnErasureAttribute"/>.</summary>
    public string? RetentionReason { get; }

    /// <summary>The description from <see cref="PersonalDataEntityAttribute"/>.</summary>
    public string? Description { get; }

    /// <summary>The classified properties.</summary>
    public IReadOnlyList<PersonalDataPropertyInfo> Properties { get; }

    /// <summary>The properties recorded as not personal data.</summary>
    public IReadOnlyList<NotPersonalDataPropertyInfo> NotPersonalData { get; }

    /// <summary>Foreign keys declared with <see cref="DataSubjectKeyAttribute"/>.</summary>
    public IReadOnlyList<DataSubjectKeyInfo> SubjectKeys { get; }

    /// <summary>Dates marked with <see cref="RetentionTriggerAttribute"/>.</summary>
    public IReadOnlyList<RetentionTriggerInfo> RetentionTriggers { get; }

    /// <summary>The property marked <see cref="AnonymisedAtAttribute"/>, if any.</summary>
    public PropertyInfo? AnonymisedAt { get; }

    /// <summary>The union of every classified property's categories.</summary>
    public PersonalDataCategory Categories => Properties.Aggregate(PersonalDataCategory.None, (all, p) => all | p.Categories);

    /// <summary>True when any data privacy attribute is present on the type or its properties.</summary>
    public bool IsDescribed => IsDataSubject || DataClass != null || Erasure != ErasureAction.Default || Description != null
        || Properties.Count > 0 || NotPersonalData.Count > 0 || SubjectKeys.Count > 0 || RetentionTriggers.Count > 0 || AnonymisedAt != null;

    /// <summary>Finds the classification for a property by name.</summary>
    public PersonalDataPropertyInfo? FindProperty(string name) => Properties.FirstOrDefault(p => p.Name == name);
}
