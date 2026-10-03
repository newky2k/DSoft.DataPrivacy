using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Compliance.Classification;

namespace DSoft.DataPrivacy.Redaction;

/// <summary>
/// Maps <see cref="PersonalDataCategory"/> onto <see cref="DataClassification"/>, so the categories used to describe
/// the database also drive log redaction.
/// </summary>
public static class PersonalDataTaxonomy
{
    /// <summary>The taxonomy name used for every classification.</summary>
    public const string TaxonomyName = "PersonalData";

    // Most sensitive first: a value in several categories is redacted as its most sensitive one.
    private static readonly PersonalDataCategory[] SensitivityOrder =
    {
        PersonalDataCategory.Health,
        PersonalDataCategory.Genetic,
        PersonalDataCategory.Biometric,
        PersonalDataCategory.SexLifeOrOrientation,
        PersonalDataCategory.RacialOrEthnicOrigin,
        PersonalDataCategory.ReligiousOrPhilosophicalBelief,
        PersonalDataCategory.PoliticalOpinion,
        PersonalDataCategory.TradeUnionMembership,
        PersonalDataCategory.CriminalOffence,
        PersonalDataCategory.Credential,
        PersonalDataCategory.Child,
        PersonalDataCategory.Financial,
        PersonalDataCategory.FreeText,
        PersonalDataCategory.Image,
        PersonalDataCategory.DirectIdentifier,
        PersonalDataCategory.Contact,
        PersonalDataCategory.Location,
        PersonalDataCategory.OnlineIdentifier,
        PersonalDataCategory.Employment,
        PersonalDataCategory.IndirectIdentifier,
        PersonalDataCategory.Telemetry,
        PersonalDataCategory.AuditCopy,
    };

    /// <summary>Categories whose values are erased from logs rather than hashed.</summary>
    public const PersonalDataCategory ErasedCategories = PersonalDataCategory.SpecialCategory | PersonalDataCategory.CriminalOffence
        | PersonalDataCategory.Credential | PersonalDataCategory.FreeText | PersonalDataCategory.Child | PersonalDataCategory.Financial;

    /// <summary>The classification for a single category, or for the most sensitive of several.</summary>
    public static DataClassification For(PersonalDataCategory categories)
    {
        var category = MostSensitive(categories);
        return category == PersonalDataCategory.None
            ? DataClassification.None
            : new DataClassification(TaxonomyName, category.ToString());
    }

    /// <summary>The most sensitive single category among <paramref name="categories"/>.</summary>
    public static PersonalDataCategory MostSensitive(PersonalDataCategory categories)
        => SensitivityOrder.FirstOrDefault(c => categories.HasAny(c));

    /// <summary>
    /// One set holding the classification of every category in <paramref name="categories"/>. It only matches a
    /// value that carries all of them: to map a redactor to each category, use <see cref="SetsFor"/>.
    /// </summary>
    public static DataClassificationSet SetFor(PersonalDataCategory categories)
        => new(categories.Flags().Select(c => new DataClassification(TaxonomyName, c.ToString())));

    /// <summary>
    /// One set per category in <paramref name="categories"/>. A redactor is chosen by the exact set a value carries,
    /// and a value classified with this taxonomy carries one classification, so redactors are mapped to these.
    /// </summary>
    public static DataClassificationSet[] SetsFor(PersonalDataCategory categories)
        => categories.Flags().Select(c => new DataClassificationSet(new DataClassification(TaxonomyName, c.ToString()))).ToArray();

    /// <summary>
    /// The classifications erased from logs, as one set. It does not match a value with a single classification:
    /// map redactors to <see cref="ErasedSets"/>.
    /// </summary>
    public static DataClassificationSet Erased => SetFor(ErasedCategories);

    /// <summary>
    /// The classifications that identify a person without being high risk, as one set. It does not match a value
    /// with a single classification: map redactors to <see cref="IdentifyingSets"/>.
    /// </summary>
    public static DataClassificationSet Identifying => SetFor(IdentifyingCategories);

    /// <summary>Categories that identify a person without being high risk; hashed in logs when a key is configured.</summary>
    public static PersonalDataCategory IdentifyingCategories => All & ~ErasedCategories;

    /// <summary>One set per classification erased from logs.</summary>
    public static DataClassificationSet[] ErasedSets => SetsFor(ErasedCategories);

    /// <summary>One set per classification that identifies a person without being high risk; hashed when a key is configured.</summary>
    public static DataClassificationSet[] IdentifyingSets => SetsFor(IdentifyingCategories);

    private static PersonalDataCategory All => SensitivityOrder.Aggregate(PersonalDataCategory.None, (all, c) => all | c);
}

/// <summary>
/// Classifies a logging parameter or property as personal data, so the redaction configured by
/// <see cref="PrivacyRedactionBuilderExtensions.AddPersonalDataRedactors"/> applies to it.
/// </summary>
/// <remarks>
/// The <c>[LoggerMessage]</c> source generator creates the attribute with no arguments, so it cannot use this
/// type directly. Use the attribute of a single category, such as <see cref="ContactDataAttribute"/>, or derive
/// a parameterless attribute of your own for a combination.
/// </remarks>
/// <example>
/// <code>
/// public sealed class CustomerEmailAttribute : PersonalDataClassificationAttribute
/// {
///     public CustomerEmailAttribute()
///         : base(PersonalDataCategory.DirectIdentifier | PersonalDataCategory.Contact)
///     {
///     }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.ReturnValue, AllowMultiple = false)]
public class PersonalDataClassificationAttribute : DataClassificationAttribute
{
    /// <summary>Classifies the value as <paramref name="categories"/>; several categories are treated as the most sensitive.</summary>
    public PersonalDataClassificationAttribute(PersonalDataCategory categories)
        : base(PersonalDataTaxonomy.For(categories))
    {
        Categories = categories;
    }

    /// <summary>The categories given.</summary>
    public PersonalDataCategory Categories { get; }
}
