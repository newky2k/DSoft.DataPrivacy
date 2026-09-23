using System;

namespace DSoft.EntityFrameworkCore.GDPR;

/// <summary>
/// Marks a property as personal data and says what kind.
/// </summary>
/// <example>
/// <code>
/// [PersonalData(PersonalDataCategory.DirectIdentifier | PersonalDataCategory.Contact, Anonymisation = AnonymisationMethod.Hash)]
/// public string Email { get; set; }
/// </code>
/// </example>
/// <remarks>
/// ASP.NET Core Identity has an attribute with the same name in <c>Microsoft.AspNetCore.Identity</c>. A file that
/// imports both namespaces needs an alias. The Identity attribute is also recognised on its own, as a
/// <see cref="PersonalDataCategory.DirectIdentifier"/>.
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class PersonalDataAttribute : Attribute
{
    /// <summary>Marks the property as personal data of the given categories.</summary>
    public PersonalDataAttribute(PersonalDataCategory categories)
    {
        Categories = categories;
    }

    /// <summary>What kind of personal data the value is.</summary>
    public PersonalDataCategory Categories { get; }

    /// <summary>
    /// The data class the value belongs to, used to match retention policies and the record of processing.
    /// Overrides the entity's data class for this property.
    /// </summary>
    public string? DataClass { get; set; }

    /// <summary>How the value is anonymised when its record is kept but the person is erased.</summary>
    public AnonymisationMethod Anonymisation { get; set; }

    /// <summary>The registered anonymiser to use when <see cref="Anonymisation"/> is <see cref="AnonymisationMethod.Custom"/>.</summary>
    public string? Anonymiser { get; set; }

    /// <summary>
    /// Set to <see cref="ErasureAction.Retain"/> to keep this value when its record is anonymised, for example an
    /// audit copy of a clinician's name. Give the reason in <see cref="RetentionExemption"/>.
    /// </summary>
    public ErasureAction Erasure { get; set; }

    /// <summary>The Article 17(3) ground relied on when <see cref="Erasure"/> is <see cref="ErasureAction.Retain"/>.</summary>
    public ErasureExemption RetentionExemption { get; set; }

    /// <summary>A human description of the value, for inventories and the record of processing.</summary>
    public string? Description { get; set; }
}
