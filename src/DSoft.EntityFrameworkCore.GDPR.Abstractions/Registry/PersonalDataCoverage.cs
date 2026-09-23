using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;

namespace DSoft.EntityFrameworkCore.GDPR;

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
