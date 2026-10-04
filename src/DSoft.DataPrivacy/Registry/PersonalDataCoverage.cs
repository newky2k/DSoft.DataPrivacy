using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DSoft.DataPrivacy;

/// <summary>A property that could hold personal data but carries neither <see cref="PersonalDataAttribute"/> nor <see cref="NotPersonalDataAttribute"/>.</summary>
public sealed record UnclassifiedProperty(Type Type, PropertyInfo Property)
{
    /// <inheritdoc />
    public override string ToString() => $"{Type.Name}.{Property.Name} ({Property.PropertyType.Name})";
}

/// <summary>Options for <see cref="PersonalDataCoverage"/>.</summary>
public sealed class PersonalDataCoverageOptions
{
    /// <summary>
    /// Which property types must be classified. By default: text, binary and date types, the ones that carry
    /// names, contact details, free text, files and dates of birth.
    /// </summary>
    public Func<Type, bool> IsCandidateType { get; set; } = DefaultCandidateType;

    /// <summary>Property names never asked about. Defaults to <c>Id</c>.</summary>
    public ISet<string> IgnoredPropertyNames { get; } = new HashSet<string>(StringComparer.Ordinal) { "Id" };

    /// <summary>Any further properties to skip.</summary>
    public Func<PropertyInfo, bool>? Ignore { get; set; }

    /// <summary>The default candidate test: <see cref="string"/>, <see cref="T:byte[]"/> and the date types, nullable or not.</summary>
    public static bool DefaultCandidateType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        return underlying == typeof(string)
            || underlying == typeof(byte[])
            || underlying == typeof(DateTime)
            || underlying == typeof(DateTimeOffset)
            || underlying.FullName == "System.DateOnly";
    }
}

/// <summary>
/// Finds properties that nobody has decided about. Run it in a unit test over every type that can hold data about
/// a person, so a new column cannot reach production without a classification.
/// </summary>
/// <example>
/// <code>
/// [Fact]
/// public void Every_personal_entity_is_classified()
/// {
///     var missing = PersonalDataCoverage.FindUnclassified(new[] { typeof(Customer), typeof(Order) });
///     Assert.True(missing.Count == 0, PersonalDataCoverage.Format(missing));
/// }
/// </code>
/// </example>
public static class PersonalDataCoverage
{
    /// <summary>Lists the candidate properties on <paramref name="types"/> that carry neither attribute.</summary>
    public static IReadOnlyList<UnclassifiedProperty> FindUnclassified(IEnumerable<Type> types, PersonalDataCoverageOptions? options = null)
    {
        if (types == null)
            throw new ArgumentNullException(nameof(types));

        options ??= new PersonalDataCoverageOptions();
        var result = new List<UnclassifiedProperty>();

        foreach (var type in types.Distinct().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            foreach (var property in PersonalDataAttributeReader.PublicInstanceProperties(type).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (!options.IsCandidateType(property.PropertyType)
                    || options.IgnoredPropertyNames.Contains(property.Name)
                    || options.Ignore?.Invoke(property) == true)
                {
                    continue;
                }

                if (PersonalDataAttributeReader.Classify(property) == null
                    && !PersonalDataAttributeReader.IsMarkedNotPersonalData(property, out _))
                {
                    result.Add(new UnclassifiedProperty(type, property));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Name fragments that suggest a property holds personal data, used by <see cref="LooksPersonal(string)"/>. They are a
    /// starting point: a name is a hint, not a classification.
    /// </summary>
    public static IReadOnlyList<string> PersonalNamePatterns { get; } = new[]
    {
        "Email", "Phone", "Mobile", "FirstName", "LastName", "MiddleName", "MaidenName", "FullName", "Surname",
        "Forename", "UserName", "Address", "IpAddress", "PostCode", "ZipCode", "DateOfBirth", "BirthDate", "Passport",
    };

    /// <summary>
    /// Words that suggest a text property names a person when the property name starts with one:
    /// <c>FromName</c>, <c>ToAddress</c>, <c>RecipientEmail</c>. The word must be the whole name or be followed by
    /// an upper-case letter or an underscore, so <c>Total</c> and <c>Token</c> do not match. On a date or a number
    /// the same words describe a range, such as <c>FromDate</c> and <c>ToDate</c>, so they only count for text.
    /// </summary>
    public static IReadOnlyList<string> PersonalTextNamePrefixes { get; } = new[] { "From", "To", "Recipient" };

    /// <summary>
    /// True when <paramref name="propertyName"/> contains one of <see cref="PersonalNamePatterns"/>, ignoring case
    /// and underscores, so <c>ContactEmail</c>, <c>post_code</c> and <c>IpAddress</c> all match, or starts with
    /// one of <see cref="PersonalTextNamePrefixes"/>.
    /// </summary>
    /// <remarks>
    /// With only a name, the prefixes cannot be limited to text, so <c>FromDate</c> matches. Use
    /// <see cref="LooksPersonal(string, Type)"/> when the property's type is known.
    /// </remarks>
    public static bool LooksPersonal(string propertyName)
    {
        if (propertyName == null)
            throw new ArgumentNullException(nameof(propertyName));

        return ContainsPersonalPattern(propertyName) || StartsWithPersonalPrefix(propertyName);
    }

    /// <summary>
    /// As <see cref="LooksPersonal(string)"/>, but <see cref="PersonalTextNamePrefixes"/> only count when
    /// <paramref name="propertyType"/> is text, so <c>FromName</c> matches as a string and <c>FromDate</c> does not
    /// match as a date.
    /// </summary>
    public static bool LooksPersonal(string propertyName, Type propertyType)
    {
        if (propertyName == null)
            throw new ArgumentNullException(nameof(propertyName));
        if (propertyType == null)
            throw new ArgumentNullException(nameof(propertyType));

        return ContainsPersonalPattern(propertyName) || (propertyType == typeof(string) && StartsWithPersonalPrefix(propertyName));
    }

    private static bool ContainsPersonalPattern(string propertyName)
    {
        var name = propertyName.Replace("_", string.Empty);
        return PersonalNamePatterns.Any(pattern => name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0);
    }

    private static bool StartsWithPersonalPrefix(string propertyName)
        => PersonalTextNamePrefixes.Any(prefix => propertyName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && (propertyName.Length == prefix.Length || propertyName[prefix.Length] == '_' || char.IsUpper(propertyName[prefix.Length])));

    /// <summary>A readable failure message listing each unclassified property on its own line.</summary>
    public static string Format(IEnumerable<UnclassifiedProperty> unclassified)
    {
        var list = unclassified.ToList();
        if (list.Count == 0)
            return "Every candidate property is classified.";

        var builder = new StringBuilder()
            .Append(list.Count)
            .AppendLine(" properties need [PersonalData] or [NotPersonalData]:");

        foreach (var item in list)
            builder.Append("  ").AppendLine(item.ToString());

        return builder.ToString();
    }
}
