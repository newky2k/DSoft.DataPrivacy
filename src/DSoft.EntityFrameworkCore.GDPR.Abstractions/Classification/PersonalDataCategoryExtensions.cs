using System.Collections.Generic;

namespace DSoft.EntityFrameworkCore.GDPR;

/// <summary>Questions commonly asked of a <see cref="PersonalDataCategory"/>.</summary>
public static class PersonalDataCategoryExtensions
{
    /// <summary>True when any of <paramref name="other"/>'s flags are set.</summary>
    public static bool HasAny(this PersonalDataCategory categories, PersonalDataCategory other) => (categories & other) != 0;

    /// <summary>True for Article 9 special category data.</summary>
    public static bool IsSpecialCategory(this PersonalDataCategory categories) => categories.HasAny(PersonalDataCategory.SpecialCategory);

    /// <summary>True for Article 10 criminal offence data.</summary>
    public static bool IsCriminalOffence(this PersonalDataCategory categories) => categories.HasAny(PersonalDataCategory.CriminalOffence);

    /// <summary>
    /// True for data whose disclosure causes most harm: special categories, criminal offence data and credentials.
    /// Logs should erase these rather than hash them.
    /// </summary>
    public static bool IsHighRisk(this PersonalDataCategory categories)
        => categories.HasAny(PersonalDataCategory.SpecialCategory | PersonalDataCategory.CriminalOffence | PersonalDataCategory.Credential);

    /// <summary>The individual flags set, lowest first, without the composite masks.</summary>
    public static IReadOnlyList<PersonalDataCategory> Flags(this PersonalDataCategory categories)
    {
        var result = new List<PersonalDataCategory>();
        for (var bit = 0; bit < 63; bit++)
        {
            var flag = (PersonalDataCategory)(1L << bit);
            if ((categories & flag) != 0)
                result.Add(flag);
        }
        return result;
    }

    /// <summary>A stable, comma separated form such as <c>DirectIdentifier, Contact</c>.</summary>
    public static string Describe(this PersonalDataCategory categories)
        => categories == PersonalDataCategory.None ? nameof(PersonalDataCategory.None) : string.Join(", ", Flags(categories));
}
