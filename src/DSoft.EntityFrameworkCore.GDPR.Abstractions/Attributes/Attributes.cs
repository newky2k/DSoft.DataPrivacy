using System;

namespace DSoft.EntityFrameworkCore.GDPR;

/// <summary>
/// Records a decision that a property is not personal data, so coverage checks stop asking about it.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class NotPersonalDataAttribute : Attribute
{
    /// <summary>Marks the property as not personal data.</summary>
    public NotPersonalDataAttribute()
    {
    }

    /// <summary>Marks the property as not personal data, with the reason.</summary>
    public NotPersonalDataAttribute(string reason)
    {
        Reason = reason;
    }

    /// <summary>Why the value is not personal data.</summary>
    public string? Reason { get; }
}

/// <summary>
/// Marks the entity that represents a person, the anchor every other piece of personal data resolves to.
/// Subject access, erasure and restriction all start from a data subject.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class DataSubjectAttribute : Attribute
{
}

/// <summary>
/// Declares that a foreign key, or the navigation that owns it, points at a data subject, and how the record
/// relates to that person. Without it the relationship is still discovered, and its kind is inferred.
/// </summary>
/// <example>
/// <code>
/// [DataSubjectKey]                                  // this address is the person's own
/// public int PersonId { get; set; }
///
/// [DataSubjectKey(DataSubjectLinkKind.Reference)]   // the ticket only mentions who raised it
/// public int RaisedById { get; set; }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class DataSubjectKeyAttribute : Attribute
{
    /// <summary>The record is owned by the data subject the key points at.</summary>
    public DataSubjectKeyAttribute()
        : this(DataSubjectLinkKind.Owner)
    {
    }

    /// <summary>The record relates to the data subject the key points at as <paramref name="kind"/> says.</summary>
    public DataSubjectKeyAttribute(DataSubjectLinkKind kind)
    {
        Kind = kind;
    }

    /// <summary>How the record relates to the person.</summary>
    public DataSubjectLinkKind Kind { get; }
}

/// <summary>Describes an entity that holds personal data as a whole.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PersonalDataEntityAttribute : Attribute
{
    /// <summary>
    /// The data class the entity's records belong to, such as <c>"Marketing"</c> or <c>"HR.Absence"</c>. Retention
    /// policies and the record of processing are keyed by data class.
    /// </summary>
    public string? DataClass { get; set; }

    /// <summary>What happens to the record when the person it belongs to is erased.</summary>
    public ErasureAction Erasure { get; set; }

    /// <summary>A human description of what the records hold.</summary>
    public string? Description { get; set; }
}

/// <summary>
/// Keeps an entity's records, and their personal data, when the person they belong to is erased, relying on an
/// Article 17(3) exemption. Use for records the controller has a legal duty to keep: health records, accounting
/// entries, anything under a legal hold.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RetainOnErasureAttribute : Attribute
{
    /// <summary>Keep the records under <paramref name="exemption"/>.</summary>
    public RetainOnErasureAttribute(ErasureExemption exemption)
    {
        if (exemption == ErasureExemption.None)
            throw new ArgumentException("Retaining data after an erasure request needs an Article 17(3) exemption.", nameof(exemption));

        Exemption = exemption;
    }

    /// <summary>The Article 17(3) ground relied on.</summary>
    public ErasureExemption Exemption { get; }

    /// <summary>The duty that requires the records to be kept, in words a data subject could be given.</summary>
    public string? Reason { get; set; }
}

/// <summary>Marks the date a retention period is counted from.</summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = true, Inherited = true)]
public sealed class RetentionTriggerAttribute : Attribute
{
    /// <summary>The date is the <paramref name="trigger"/> event.</summary>
    public RetentionTriggerAttribute(RetentionTrigger trigger = RetentionTrigger.CreatedAt)
    {
        Trigger = trigger;
    }

    /// <summary>The event the date records.</summary>
    public RetentionTrigger Trigger { get; }
}

/// <summary>
/// Marks a nullable date that is set when a record is anonymised. Retention skips records that already carry
/// it, which lets an anonymising policy make progress in batches.
/// </summary>
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false, Inherited = true)]
public sealed class AnonymisedAtAttribute : Attribute
{
}
