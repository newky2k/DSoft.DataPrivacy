using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DSoft.DataPrivacy;

/// <summary>
/// The personal data described by attributes across a set of types, without Entity Framework. Use it where the
/// database is not available: documentation, a record of processing, log redaction, or a unit test.
/// </summary>
/// <example>
/// <code>
/// var registry = PersonalDataRegistry.FromAssemblies(typeof(Customer).Assembly);
/// foreach (var type in registry.Types)
///     Console.WriteLine($"{type.Type.Name}: {type.Categories.Describe()}");
/// </code>
/// </example>
public sealed class PersonalDataRegistry
{
    private readonly Dictionary<Type, PersonalDataTypeInfo> _types;

    private PersonalDataRegistry(IEnumerable<PersonalDataTypeInfo> types)
    {
        _types = types.ToDictionary(t => t.Type);
        Types = _types.Values.OrderBy(t => t.Type.FullName, StringComparer.Ordinal).ToList();
    }

    /// <summary>Every described type, ordered by name.</summary>
    public IReadOnlyList<PersonalDataTypeInfo> Types { get; }

    /// <summary>The types marked <see cref="DataSubjectAttribute"/>.</summary>
    public IEnumerable<PersonalDataTypeInfo> DataSubjects => Types.Where(t => t.IsDataSubject);

    /// <summary>Scans the given types and keeps those that carry any data privacy attribute.</summary>
    public static PersonalDataRegistry FromTypes(IEnumerable<Type> types)
    {
        if (types == null)
            throw new ArgumentNullException(nameof(types));

        return new PersonalDataRegistry(types
            .Where(t => t.IsClass && !t.IsGenericTypeDefinition)
            .Distinct()
            .Select(PersonalDataAttributeReader.Describe)
            .Where(t => t.IsDescribed));
    }

    /// <summary>Scans every public class in the given assemblies.</summary>
    public static PersonalDataRegistry FromAssemblies(params Assembly[] assemblies)
    {
        if (assemblies == null)
            throw new ArgumentNullException(nameof(assemblies));

        return FromTypes(assemblies.SelectMany(a => a.GetExportedTypes()));
    }

    /// <summary>The description of <paramref name="type"/>, or <c>null</c> when it carries no data privacy attributes.</summary>
    public PersonalDataTypeInfo? Find(Type type) => _types.TryGetValue(type, out var info) ? info : null;

    /// <summary>The types that belong to a data class, either as a whole or through one of their properties.</summary>
    public IEnumerable<PersonalDataTypeInfo> InDataClass(string dataClass)
        => Types.Where(t => string.Equals(t.DataClass, dataClass, StringComparison.Ordinal)
            || t.Properties.Any(p => string.Equals(p.Classification.DataClass, dataClass, StringComparison.Ordinal)));

    /// <summary>Every data class named on a type or property.</summary>
    public IReadOnlyList<string> DataClasses()
        => Types.Select(t => t.DataClass)
            .Concat(Types.SelectMany(t => t.Properties).Select(p => p.Classification.DataClass))
            .Where(c => !string.IsNullOrEmpty(c))
            .Select(c => c!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();
}
