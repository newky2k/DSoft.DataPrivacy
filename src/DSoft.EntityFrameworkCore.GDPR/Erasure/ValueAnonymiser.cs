using System;
using DSoft.EntityFrameworkCore.GDPR.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DSoft.EntityFrameworkCore.GDPR.Erasure;

/// <summary>Works out the anonymised replacement for a value.</summary>
internal static class ValueAnonymiser
{
    public static object? Anonymise(AnonymisationMethod method, IEntityType entityType, IProperty property, object? value, GdprOptions options, string? anonymiser)
    {
        if (value == null)
            return null;

        var type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;

        switch (method)
        {
            case AnonymisationMethod.Null:
                return NullOrDefault(property);

            case AnonymisationMethod.Redact:
                if (type == typeof(string))
                    return Truncate(options.RedactedText, property);
                if (type == typeof(byte[]))
                    return Array.Empty<byte>();
                return NullOrDefault(property);

            case AnonymisationMethod.Hash:
                if (type != typeof(string))
                    throw new InvalidOperationException($"'{entityType.ShortName()}.{property.Name}' is hashed on erasure, but only text properties can hold a hash.");
                var hasher = options.Hasher
                    ?? throw new InvalidOperationException($"'{entityType.ShortName()}.{property.Name}' is hashed on erasure, but no key is configured. Call UseHashKey in UseGdpr.");
                return Truncate(hasher.Hash(value), property);

            case AnonymisationMethod.Generalise:
                return Generalise(value) ?? NullOrDefault(property);

            case AnonymisationMethod.Custom:
                var custom = anonymiser == null ? null : options.FindAnonymiser(anonymiser);
                if (custom == null)
                    throw new InvalidOperationException($"'{entityType.ShortName()}.{property.Name}' uses the anonymiser '{anonymiser}', which is not registered. Call AddAnonymiser in UseGdpr.");
                return custom(new AnonymisationContext(entityType, property, value));

            default:
                throw new ArgumentOutOfRangeException(nameof(method), method, "The method must be resolved before anonymising.");
        }
    }

    private static object? NullOrDefault(IProperty property)
    {
        if (property.IsNullable)
            return null;

        var type = property.ClrType;
        if (type == typeof(string))
            return string.Empty;
        if (type == typeof(byte[]))
            return Array.Empty<byte>();

        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private static object? Generalise(object value) => value switch
    {
        DateTime date => new DateTime(date.Year, 1, 1, 0, 0, 0, date.Kind),
        DateTimeOffset date => new DateTimeOffset(date.Year, 1, 1, 0, 0, 0, date.Offset),
        DateOnly date => new DateOnly(date.Year, 1, 1),
        _ => null,
    };

    private static string Truncate(string text, IProperty property)
    {
        var maxLength = property.GetMaxLength();
        return maxLength.HasValue && text.Length > maxLength.Value ? text.Substring(0, maxLength.Value) : text;
    }
}
