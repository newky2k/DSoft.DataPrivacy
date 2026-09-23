using System;
using System.Linq;
using DSoft.DataPrivacy.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;

namespace DSoft.DataPrivacy.EntityFrameworkCore.Conventions;

/// <summary>
/// Copies the data privacy attributes on entity classes into the model as annotations. It runs as the model is finalised
/// and at data-annotation precedence, so anything configured with the fluent API wins.
/// </summary>
public sealed class PersonalDataAttributeConvention : IModelFinalizingConvention
{
    /// <inheritdoc />
    public void ProcessModelFinalizing(IConventionModelBuilder modelBuilder, IConventionContext<IConventionModelBuilder> context)
    {
        foreach (var entityType in modelBuilder.Metadata.GetEntityTypes().ToList())
        {
            if (entityType.HasSharedClrType && entityType.ClrType.IsGenericType)
                continue; // property bags have no attributes to read

            ApplyEntity(entityType);

            foreach (var property in entityType.GetDeclaredProperties())
                ApplyProperty(entityType, property);

            foreach (var navigation in entityType.GetDeclaredNavigations())
                ApplyNavigation(navigation);
        }

        modelBuilder.HasAnnotation(PrivacyAnnotationNames.ConventionsApplied, true, fromDataAnnotation: true);

        Validate(modelBuilder.Metadata);
    }

    private static void ApplyEntity(IConventionEntityType entityType)
    {
        // Entity-level attributes are inherited, so a derived entity type carries its base class's settings too.
        var info = PersonalDataAttributeReader.Describe(entityType.ClrType);
        var builder = entityType.Builder;
        if (info.IsDataSubject)
            builder.HasAnnotation(PrivacyAnnotationNames.IsDataSubject, true, fromDataAnnotation: true);
        if (info.DataClass != null)
            builder.HasAnnotation(PrivacyAnnotationNames.DataClass, info.DataClass, fromDataAnnotation: true);
        if (info.Erasure != ErasureAction.Default)
            builder.HasAnnotation(PrivacyAnnotationNames.Erasure, (int)info.Erasure, fromDataAnnotation: true);
        if (info.RetentionGround != RetentionGround.None)
            builder.HasAnnotation(PrivacyAnnotationNames.RetentionGround, (int)info.RetentionGround, fromDataAnnotation: true);
        if (info.RetentionReason != null)
            builder.HasAnnotation(PrivacyAnnotationNames.RetentionReason, info.RetentionReason, fromDataAnnotation: true);
        if (info.Description != null)
            builder.HasAnnotation(PrivacyAnnotationNames.Description, info.Description, fromDataAnnotation: true);
    }

    private static void ApplyProperty(IConventionEntityType entityType, IConventionProperty property)
    {
        var member = property.PropertyInfo;
        if (member == null)
            return;

        var builder = property.Builder;
        var classification = PersonalDataAttributeReader.Classify(member);
        if (classification != null && property.FindAnnotation(PrivacyAnnotationNames.NotPersonalData) == null)
        {
            builder.HasAnnotation(PrivacyAnnotationNames.Categories, (long)classification.Categories, fromDataAnnotation: true);
            if (classification.DataClass != null)
                builder.HasAnnotation(PrivacyAnnotationNames.DataClass, classification.DataClass, fromDataAnnotation: true);
            if (classification.Anonymisation != AnonymisationMethod.Default)
                builder.HasAnnotation(PrivacyAnnotationNames.Anonymisation, (int)classification.Anonymisation, fromDataAnnotation: true);
            if (classification.Anonymiser != null)
                builder.HasAnnotation(PrivacyAnnotationNames.Anonymiser, classification.Anonymiser, fromDataAnnotation: true);
            if (classification.Erasure != ErasureAction.Default)
                builder.HasAnnotation(PrivacyAnnotationNames.Erasure, (int)classification.Erasure, fromDataAnnotation: true);
            if (classification.RetentionGround != RetentionGround.None)
                builder.HasAnnotation(PrivacyAnnotationNames.RetentionGround, (int)classification.RetentionGround, fromDataAnnotation: true);
            if (classification.Description != null)
                builder.HasAnnotation(PrivacyAnnotationNames.Description, classification.Description, fromDataAnnotation: true);
        }

        if (PersonalDataAttributeReader.IsMarkedNotPersonalData(member, out var reason) && property.FindAnnotation(PrivacyAnnotationNames.Categories) == null)
            builder.HasAnnotation(PrivacyAnnotationNames.NotPersonalData, reason ?? string.Empty, fromDataAnnotation: true);

        var info = PersonalDataAttributeReader.Describe(entityType.ClrType);

        var key = info.SubjectKeys.FirstOrDefault(k => k.Property.Name == member.Name);
        if (key != null)
            builder.HasAnnotation(PrivacyAnnotationNames.SubjectLinkKind, (int)key.Kind, fromDataAnnotation: true);

        var trigger = info.RetentionTriggers.FirstOrDefault(t => t.Property.Name == member.Name);
        if (trigger != null)
            builder.HasAnnotation(PrivacyAnnotationNames.RetentionTrigger, (int)trigger.Trigger, fromDataAnnotation: true);

        if (info.AnonymisedAt?.Name == member.Name)
            builder.HasAnnotation(PrivacyAnnotationNames.AnonymisedAt, true, fromDataAnnotation: true);
    }

    private static void ApplyNavigation(IConventionNavigation navigation)
    {
        // [DataSubjectKey] on a navigation describes the foreign key behind it.
        var member = navigation.PropertyInfo;
        if (member == null || !navigation.IsOnDependent)
            return;

        var attribute = (DataSubjectKeyAttribute?)Attribute.GetCustomAttribute(member, typeof(DataSubjectKeyAttribute), inherit: true);
        if (attribute == null)
            return;

        foreach (var property in navigation.ForeignKey.Properties)
            property.Builder.HasAnnotation(PrivacyAnnotationNames.SubjectLinkKind, (int)attribute.Kind, fromDataAnnotation: true);
    }

    private static void Validate(IConventionModel model)
    {
        foreach (var entityType in model.GetEntityTypes())
        {
            if (entityType.FindAnnotation(PrivacyAnnotationNames.Erasure)?.Value is int erasure
                && erasure == (int)ErasureAction.Retain
                && entityType.FindAnnotation(PrivacyAnnotationNames.RetentionGround)?.Value is not int)
            {
                throw new InvalidOperationException(
                    $"Entity '{entityType.DisplayName()}' is retained on erasure but names no retention ground.");
            }

            foreach (var property in entityType.GetDeclaredProperties())
            {
                if (property.FindAnnotation(PrivacyAnnotationNames.Categories) != null
                    && property.FindAnnotation(PrivacyAnnotationNames.NotPersonalData) != null)
                {
                    throw new InvalidOperationException(
                        $"'{entityType.DisplayName()}.{property.Name}' is configured both as personal data and as not personal data.");
                }
            }
        }
    }
}
