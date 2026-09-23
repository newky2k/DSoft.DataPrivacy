using System;
using DSoft.EntityFrameworkCore.GDPR;
using DSoft.EntityFrameworkCore.GDPR.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Fluent configuration of personal data on a property. Equivalent to the property-level attributes.</summary>
public static class GdprPropertyBuilderExtensions
{
    /// <summary>Marks the property as personal data. Same as <see cref="PersonalDataAttribute"/>.</summary>
    /// <param name="builder">The property.</param>
    /// <param name="categories">What kind of personal data it is.</param>
    /// <param name="anonymisation">How it is anonymised when its record is kept but the person erased.</param>
    /// <param name="dataClass">The data class, when it differs from the entity's.</param>
    /// <param name="anonymiser">The registered anonymiser, for <see cref="AnonymisationMethod.Custom"/>.</param>
    /// <param name="description">A human description.</param>
    public static PropertyBuilder IsPersonalData(
        this PropertyBuilder builder,
        PersonalDataCategory categories,
        AnonymisationMethod anonymisation = AnonymisationMethod.Default,
        string? dataClass = null,
        string? anonymiser = null,
        string? description = null)
    {
        if (categories == PersonalDataCategory.None)
            throw new ArgumentException("Name at least one category, or use IsNotPersonalData.", nameof(categories));
        if (anonymisation == AnonymisationMethod.Custom && string.IsNullOrWhiteSpace(anonymiser))
            throw new ArgumentException("A custom anonymisation needs the anonymiser's name.", nameof(anonymiser));

        var metadata = builder.Metadata;
        metadata.RemoveAnnotation(GdprAnnotationNames.NotPersonalData);
        metadata.SetAnnotation(GdprAnnotationNames.Categories, (long)categories);
        SetOrRemove(builder, GdprAnnotationNames.Anonymisation, anonymisation == AnonymisationMethod.Default ? null : (int)anonymisation);
        SetOrRemove(builder, GdprAnnotationNames.DataClass, dataClass);
        SetOrRemove(builder, GdprAnnotationNames.Anonymiser, anonymiser);
        SetOrRemove(builder, GdprAnnotationNames.Description, description);
        return builder;
    }

    /// <inheritdoc cref="IsPersonalData(PropertyBuilder, PersonalDataCategory, AnonymisationMethod, string?, string?, string?)"/>
    public static PropertyBuilder<TProperty> IsPersonalData<TProperty>(
        this PropertyBuilder<TProperty> builder,
        PersonalDataCategory categories,
        AnonymisationMethod anonymisation = AnonymisationMethod.Default,
        string? dataClass = null,
        string? anonymiser = null,
        string? description = null)
        => (PropertyBuilder<TProperty>)IsPersonalData((PropertyBuilder)builder, categories, anonymisation, dataClass, anonymiser, description);

    /// <summary>
    /// Keeps this value when its record is anonymised on erasure, for example a copy of a clinician's name on a
    /// clinical entry. The property must also be marked as personal data.
    /// </summary>
    public static PropertyBuilder RetainOnErasure(this PropertyBuilder builder, ErasureExemption exemption)
    {
        if (exemption == ErasureExemption.None)
            throw new ArgumentException("Retaining data after an erasure request needs an Article 17(3) exemption.", nameof(exemption));

        builder.HasAnnotation(GdprAnnotationNames.Erasure, (int)ErasureAction.Retain);
        builder.HasAnnotation(GdprAnnotationNames.Exemption, (int)exemption);
        return builder;
    }

    /// <inheritdoc cref="RetainOnErasure(PropertyBuilder, ErasureExemption)"/>
    public static PropertyBuilder<TProperty> RetainOnErasure<TProperty>(this PropertyBuilder<TProperty> builder, ErasureExemption exemption)
        => (PropertyBuilder<TProperty>)RetainOnErasure((PropertyBuilder)builder, exemption);

    /// <summary>Records a decision that the property is not personal data. Same as <see cref="NotPersonalDataAttribute"/>.</summary>
    public static PropertyBuilder IsNotPersonalData(this PropertyBuilder builder, string? reason = null)
    {
        var metadata = builder.Metadata;
        metadata.RemoveAnnotation(GdprAnnotationNames.Categories);
        metadata.RemoveAnnotation(GdprAnnotationNames.Anonymisation);
        metadata.RemoveAnnotation(GdprAnnotationNames.Anonymiser);
        metadata.SetAnnotation(GdprAnnotationNames.NotPersonalData, reason ?? string.Empty);
        return builder;
    }

    /// <inheritdoc cref="IsNotPersonalData(PropertyBuilder, string?)"/>
    public static PropertyBuilder<TProperty> IsNotPersonalData<TProperty>(this PropertyBuilder<TProperty> builder, string? reason = null)
        => (PropertyBuilder<TProperty>)IsNotPersonalData((PropertyBuilder)builder, reason);

    /// <summary>
    /// Declares that this foreign key points at a data subject, and how the record relates to them.
    /// Same as <see cref="DataSubjectKeyAttribute"/>. Use <see cref="DataSubjectLinkKind.None"/> to stop a
    /// relationship being treated as a link to the person.
    /// </summary>
    public static PropertyBuilder IsDataSubjectKey(this PropertyBuilder builder, DataSubjectLinkKind kind = DataSubjectLinkKind.Owner)
        => builder.HasAnnotation(GdprAnnotationNames.SubjectLinkKind, (int)kind);

    /// <inheritdoc cref="IsDataSubjectKey(PropertyBuilder, DataSubjectLinkKind)"/>
    public static PropertyBuilder<TProperty> IsDataSubjectKey<TProperty>(this PropertyBuilder<TProperty> builder, DataSubjectLinkKind kind = DataSubjectLinkKind.Owner)
        => (PropertyBuilder<TProperty>)IsDataSubjectKey((PropertyBuilder)builder, kind);

    /// <summary>Marks the date a retention period is counted from. Same as <see cref="RetentionTriggerAttribute"/>.</summary>
    public static PropertyBuilder IsRetentionTrigger(this PropertyBuilder builder, RetentionTrigger trigger = RetentionTrigger.CreatedAt)
        => builder.HasAnnotation(GdprAnnotationNames.RetentionTrigger, (int)trigger);

    /// <inheritdoc cref="IsRetentionTrigger(PropertyBuilder, RetentionTrigger)"/>
    public static PropertyBuilder<TProperty> IsRetentionTrigger<TProperty>(this PropertyBuilder<TProperty> builder, RetentionTrigger trigger = RetentionTrigger.CreatedAt)
        => (PropertyBuilder<TProperty>)IsRetentionTrigger((PropertyBuilder)builder, trigger);

    /// <summary>Marks the nullable date set when the record is anonymised. Same as <see cref="AnonymisedAtAttribute"/>.</summary>
    public static PropertyBuilder IsAnonymisedAt(this PropertyBuilder builder)
        => builder.HasAnnotation(GdprAnnotationNames.AnonymisedAt, true);

    /// <inheritdoc cref="IsAnonymisedAt(PropertyBuilder)"/>
    public static PropertyBuilder<TProperty> IsAnonymisedAt<TProperty>(this PropertyBuilder<TProperty> builder)
        => (PropertyBuilder<TProperty>)IsAnonymisedAt((PropertyBuilder)builder);

    private static void SetOrRemove(PropertyBuilder builder, string name, object? value)
    {
        if (value == null)
            builder.Metadata.RemoveAnnotation(name);
        else
            builder.Metadata.SetAnnotation(name, value);
    }
}
