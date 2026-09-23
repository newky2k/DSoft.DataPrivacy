namespace DSoft.EntityFrameworkCore.GDPR.Metadata;

/// <summary>
/// The Entity Framework model annotations that carry GDPR classification. Values are primitives (bool, int,
/// long, string) so they survive into compiled models and migration snapshots without a type dependency.
/// </summary>
public static class GdprAnnotationNames
{
    /// <summary>The prefix shared by every annotation.</summary>
    public const string Prefix = "Gdpr:";

    /// <summary>Model: set when the attribute conventions have run.</summary>
    public const string ConventionsApplied = Prefix + "ConventionsApplied";

    /// <summary>Entity: the entity represents a data subject.</summary>
    public const string IsDataSubject = Prefix + "IsDataSubject";

    /// <summary>Entity or property: the data class.</summary>
    public const string DataClass = Prefix + "DataClass";

    /// <summary>Entity or property: the <see cref="ErasureAction"/>, as an int.</summary>
    public const string Erasure = Prefix + "Erasure";

    /// <summary>Entity or property: the <see cref="ErasureExemption"/>, as an int.</summary>
    public const string Exemption = Prefix + "Exemption";

    /// <summary>Entity: the reason given for retaining records.</summary>
    public const string ExemptionReason = Prefix + "ExemptionReason";

    /// <summary>Entity or property: a human description.</summary>
    public const string Description = Prefix + "Description";

    /// <summary>Property: the <see cref="PersonalDataCategory"/> flags, as a long.</summary>
    public const string Categories = Prefix + "Categories";

    /// <summary>Property: the <see cref="AnonymisationMethod"/>, as an int.</summary>
    public const string Anonymisation = Prefix + "Anonymisation";

    /// <summary>Property: the name of a custom anonymiser.</summary>
    public const string Anonymiser = Prefix + "Anonymiser";

    /// <summary>Property: recorded as not personal data.</summary>
    public const string NotPersonalData = Prefix + "NotPersonalData";

    /// <summary>Property: the <see cref="DataSubjectLinkKind"/> of the foreign key it belongs to, as an int.</summary>
    public const string SubjectLinkKind = Prefix + "SubjectLinkKind";

    /// <summary>Property: the <see cref="RetentionTrigger"/> the date records, as an int.</summary>
    public const string RetentionTrigger = Prefix + "RetentionTrigger";

    /// <summary>Property: the date set when the record is anonymised.</summary>
    public const string AnonymisedAt = Prefix + "AnonymisedAt";
}
