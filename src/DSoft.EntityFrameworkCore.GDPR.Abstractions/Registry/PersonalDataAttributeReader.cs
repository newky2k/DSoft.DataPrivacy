using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DSoft.EntityFrameworkCore.GDPR;

/// <summary>
/// Reads the GDPR attributes on a type. This is the one place attributes are interpreted; the registry, the
/// coverage check and the Entity Framework conventions all go through it.
/// </summary>
public static class PersonalDataAttributeReader
{
    private const string IdentityPersonalDataAttribute = "Microsoft.AspNetCore.Identity.PersonalDataAttribute";

    private static readonly ConcurrentDictionary<Type, PersonalDataTypeInfo> Cache = new();

    /// <summary>Describes <paramref name="type"/> from its attributes. Results are cached.</summary>
    public static PersonalDataTypeInfo Describe(Type type)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        return Cache.GetOrAdd(type, Read);
    }

    /// <summary>Reads the classification on one property, or <c>null</c> when it has none.</summary>
    public static PersonalDataPropertyClassification? Classify(PropertyInfo property)
    {
        if (property == null)
            throw new ArgumentNullException(nameof(property));

        var attribute = property.GetCustomAttribute<PersonalDataAttribute>(inherit: true);
        if (attribute != null)
        {
            return new PersonalDataPropertyClassification
            {
                Categories = attribute.Categories,
                DataClass = attribute.DataClass,
                Anonymisation = attribute.Anonymisation,
                Anonymiser = attribute.Anonymiser,
                Erasure = attribute.Erasure,
                RetentionExemption = attribute.RetentionExemption,
                Description = attribute.Description,
                Source = ClassificationSource.Attribute,
            };
        }

        if (HasIdentityPersonalDataAttribute(property))
        {
            return new PersonalDataPropertyClassification
            {
                Categories = PersonalDataCategory.DirectIdentifier,
                Source = ClassificationSource.IdentityAttribute,
            };
        }

        return null;
    }

    /// <summary>True when the property carries <see cref="NotPersonalDataAttribute"/>.</summary>
    public static bool IsMarkedNotPersonalData(PropertyInfo property, out string? reason)
    {
        var attribute = property.GetCustomAttribute<NotPersonalDataAttribute>(inherit: true);
        reason = attribute?.Reason;
        return attribute != null;
    }

    private static PersonalDataTypeInfo Read(Type type)
    {
        var entity = type.GetCustomAttribute<PersonalDataEntityAttribute>(inherit: true);
        var retain = type.GetCustomAttribute<RetainOnErasureAttribute>(inherit: true);

        var properties = new List<PersonalDataPropertyInfo>();
        var notPersonal = new List<NotPersonalDataPropertyInfo>();
        var subjectKeys = new List<DataSubjectKeyInfo>();
        var triggers = new List<RetentionTriggerInfo>();
        PropertyInfo? anonymisedAt = null;

        foreach (var property in PublicInstanceProperties(type))
        {
            var classification = Classify(property);
            if (classification != null)
                properties.Add(new PersonalDataPropertyInfo(property, classification));

            if (IsMarkedNotPersonalData(property, out var reason))
            {
                if (classification != null)
                {
                    throw new InvalidOperationException(
                        $"{type.Name}.{property.Name} is marked both personal data and not personal data.");
                }

                notPersonal.Add(new NotPersonalDataPropertyInfo(property, reason));
            }

            var key = property.GetCustomAttribute<DataSubjectKeyAttribute>(inherit: true);
            if (key != null)
                subjectKeys.Add(new DataSubjectKeyInfo(property, key.Kind));

            foreach (var trigger in property.GetCustomAttributes<RetentionTriggerAttribute>(inherit: true))
                triggers.Add(new RetentionTriggerInfo(property, trigger.Trigger));

            if (property.GetCustomAttribute<AnonymisedAtAttribute>(inherit: true) != null)
                anonymisedAt = property;
        }

        var erasure = retain != null ? ErasureAction.Retain : entity?.Erasure ?? ErasureAction.Default;

        return new PersonalDataTypeInfo(
            type,
            isDataSubject: type.GetCustomAttribute<DataSubjectAttribute>(inherit: false) != null,
            dataClass: entity?.DataClass,
            erasure: erasure,
            exemption: retain?.Exemption ?? ErasureExemption.None,
            exemptionReason: retain?.Reason,
            description: entity?.Description,
            properties: properties,
            notPersonalData: notPersonal,
            subjectKeys: subjectKeys,
            retentionTriggers: triggers,
            anonymisedAt: anonymisedAt);
    }

    internal static IEnumerable<PropertyInfo> PublicInstanceProperties(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0)
            // A property redeclared with "new" appears twice; keep the most derived.
            .GroupBy(p => p.Name)
            .Select(g => g.OrderByDescending(p => Depth(p.DeclaringType)).First());

    private static int Depth(Type? type)
    {
        var depth = 0;
        for (var current = type; current != null; current = current.BaseType)
            depth++;
        return depth;
    }

    private static bool HasIdentityPersonalDataAttribute(PropertyInfo property)
    {
        foreach (var attribute in property.GetCustomAttributes(inherit: true))
        {
            for (var type = attribute.GetType(); type != null && type != typeof(Attribute); type = type.BaseType)
            {
                if (type.FullName == IdentityPersonalDataAttribute)
                    return true;
            }
        }

        return false;
    }
}
