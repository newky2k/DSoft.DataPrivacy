using System.Reflection;

namespace DSoft.DataPrivacy;

/// <summary>Where a classification came from.</summary>
public enum ClassificationSource
{
    /// <summary>A <see cref="PersonalDataAttribute"/> on the member.</summary>
    Attribute = 0,

    /// <summary>ASP.NET Core Identity's own <c>[PersonalData]</c> or <c>[ProtectedPersonalData]</c> attribute.</summary>
    IdentityAttribute = 1,

    /// <summary>Configured in code, for example through the Entity Framework fluent API.</summary>
    Explicit = 2,
}

/// <summary>How one property is classified.</summary>
public sealed record PersonalDataPropertyClassification
{
    /// <summary>What kind of personal data the value is.</summary>
    public PersonalDataCategory Categories { get; init; }

    /// <summary>The data class the value belongs to, when it differs from its entity's.</summary>
    public string? DataClass { get; init; }

    /// <summary>How the value is anonymised.</summary>
    public AnonymisationMethod Anonymisation { get; init; }

    /// <summary>The registered anonymiser, for <see cref="AnonymisationMethod.Custom"/>.</summary>
    public string? Anonymiser { get; init; }

    /// <summary><see cref="ErasureAction.Retain"/> keeps the value when its record is anonymised.</summary>
    public ErasureAction Erasure { get; init; }

    /// <summary>The retention ground for keeping the value.</summary>
    public RetentionGround RetentionGround { get; init; }

    /// <summary>A human description of the value.</summary>
    public string? Description { get; init; }

    /// <summary>Where the classification came from.</summary>
    public ClassificationSource Source { get; init; }
}

/// <summary>A property and how it is classified.</summary>
public sealed record PersonalDataPropertyInfo(PropertyInfo Property, PersonalDataPropertyClassification Classification)
{
    /// <summary>The property name.</summary>
    public string Name => Property.Name;

    /// <summary>What kind of personal data the value is.</summary>
    public PersonalDataCategory Categories => Classification.Categories;
}

/// <summary>A property recorded as not personal data.</summary>
public sealed record NotPersonalDataPropertyInfo(PropertyInfo Property, string? Reason);

/// <summary>A foreign key property declared to point at a data subject.</summary>
public sealed record DataSubjectKeyInfo(PropertyInfo Property, DataSubjectLinkKind Kind);

/// <summary>A date a retention period can be counted from.</summary>
public sealed record RetentionTriggerInfo(PropertyInfo Property, RetentionTrigger Trigger);
