using System;
using DSoft.DataPrivacy;
using DSoft.DataPrivacy.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Microsoft.EntityFrameworkCore;

/// <summary>Fluent configuration of how an entity holds personal data. Equivalent to the class-level attributes.</summary>
public static class DataPrivacyEntityTypeBuilderExtensions
{
    /// <summary>Marks the entity as a data subject: the person other personal data resolves to. Same as <see cref="DataSubjectAttribute"/>.</summary>
    public static EntityTypeBuilder IsDataSubject(this EntityTypeBuilder builder)
        => builder.HasAnnotation(PrivacyAnnotationNames.IsDataSubject, true);

    /// <inheritdoc cref="IsDataSubject(EntityTypeBuilder)"/>
    public static EntityTypeBuilder<TEntity> IsDataSubject<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)IsDataSubject((EntityTypeBuilder)builder);

    /// <summary>Sets the data class the entity's records belong to, for retention and the record of processing.</summary>
    public static EntityTypeBuilder HasDataClass(this EntityTypeBuilder builder, string dataClass)
    {
        if (string.IsNullOrWhiteSpace(dataClass))
            throw new ArgumentException("A data class needs a name.", nameof(dataClass));

        return builder.HasAnnotation(PrivacyAnnotationNames.DataClass, dataClass);
    }

    /// <inheritdoc cref="HasDataClass(EntityTypeBuilder, string)"/>
    public static EntityTypeBuilder<TEntity> HasDataClass<TEntity>(this EntityTypeBuilder<TEntity> builder, string dataClass)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasDataClass((EntityTypeBuilder)builder, dataClass);

    /// <summary>Sets whether the entity's records are deleted or anonymised when the person they belong to is erased.</summary>
    public static EntityTypeBuilder OnErasure(this EntityTypeBuilder builder, ErasureAction action)
    {
        if (action == ErasureAction.Retain)
            throw new ArgumentException("Use RetainOnErasure, which records the retention ground.", nameof(action));

        return builder.HasAnnotation(PrivacyAnnotationNames.Erasure, (int)action);
    }

    /// <inheritdoc cref="OnErasure(EntityTypeBuilder, ErasureAction)"/>
    public static EntityTypeBuilder<TEntity> OnErasure<TEntity>(this EntityTypeBuilder<TEntity> builder, ErasureAction action)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)OnErasure((EntityTypeBuilder)builder, action);

    /// <summary>Keeps the entity's records when the person is erased, under a retention ground. Same as <see cref="RetainOnErasureAttribute"/>.</summary>
    public static EntityTypeBuilder RetainOnErasure(this EntityTypeBuilder builder, RetentionGround ground, string? reason = null)
    {
        if (ground == RetentionGround.None)
            throw new ArgumentException("Retaining data after an erasure request needs a retention ground.", nameof(ground));

        builder.HasAnnotation(PrivacyAnnotationNames.Erasure, (int)ErasureAction.Retain);
        builder.HasAnnotation(PrivacyAnnotationNames.RetentionGround, (int)ground);
        if (reason != null)
            builder.HasAnnotation(PrivacyAnnotationNames.RetentionReason, reason);
        return builder;
    }

    /// <inheritdoc cref="RetainOnErasure(EntityTypeBuilder, RetentionGround, string?)"/>
    public static EntityTypeBuilder<TEntity> RetainOnErasure<TEntity>(this EntityTypeBuilder<TEntity> builder, RetentionGround ground, string? reason = null)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)RetainOnErasure((EntityTypeBuilder)builder, ground, reason);

    /// <summary>Describes what the entity's records hold, for inventories and the record of processing.</summary>
    public static EntityTypeBuilder HasPersonalDataDescription(this EntityTypeBuilder builder, string description)
        => builder.HasAnnotation(PrivacyAnnotationNames.Description, description);

    /// <inheritdoc cref="HasPersonalDataDescription(EntityTypeBuilder, string)"/>
    public static EntityTypeBuilder<TEntity> HasPersonalDataDescription<TEntity>(this EntityTypeBuilder<TEntity> builder, string description)
        where TEntity : class
        => (EntityTypeBuilder<TEntity>)HasPersonalDataDescription((EntityTypeBuilder)builder, description);
}
