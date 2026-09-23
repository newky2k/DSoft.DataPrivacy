using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DSoft.DataPrivacy;

/// <summary>
/// Keyed hashing (HMAC-SHA256) of personal data. The same value and key always give the same hash, so a
/// suppression list can recognise an erased email address, or two erased records can still be matched, without
/// the value being kept. Keep the key secret: anyone holding it can test guesses against the hashes.
/// </summary>
public sealed class PersonalDataHasher
{
    private readonly byte[] _key;

    /// <summary>Creates a hasher with a secret key of at least 32 bytes.</summary>
    public PersonalDataHasher(byte[] key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));
        if (key.Length < 32)
            throw new ArgumentException("The hashing key must be at least 32 bytes.", nameof(key));

        _key = (byte[])key.Clone();
    }

    /// <summary>
    /// Hashes a value to 64 lower case hex characters. Text is trimmed and lower-cased first, so
    /// <c>" Jo@Example.com"</c> and <c>"jo@example.com"</c> hash the same.
    /// </summary>
    public string Hash(object value)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        var bytes = value switch
        {
            byte[] raw => raw,
            string text => Encoding.UTF8.GetBytes(Normalise(text)),
            IFormattable formattable => Encoding.UTF8.GetBytes(formattable.ToString(null, CultureInfo.InvariantCulture)),
            _ => Encoding.UTF8.GetBytes(Normalise(value.ToString() ?? string.Empty)),
        };

        using var hmac = new HMACSHA256(_key);
        var hash = hmac.ComputeHash(bytes);

        var builder = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));

        return builder.ToString();
    }

    private static string Normalise(string text) => text.Trim().ToLowerInvariant();
}
