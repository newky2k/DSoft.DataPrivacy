using System;
using System.Collections.Generic;
using System.Linq;

namespace DSoft.DataPrivacy.Regimes;

/// <summary>The regimes this library ships, and ways to find and combine them.</summary>
public static class PrivacyRegimes
{
    private static readonly char[] Separators = { ',', '+', ';' };

    /// <summary>The EU GDPR and the UK GDPR.</summary>
    public static PrivacyRegime Gdpr { get; } = new GdprRegime();

    /// <summary>South Africa's POPIA.</summary>
    public static PrivacyRegime Popia { get; } = new PopiaRegime();

    /// <summary>Every shipped regime.</summary>
    public static IReadOnlyList<PrivacyRegime> All { get; } = new[] { Gdpr, Popia };

    /// <summary>
    /// Combines laws that apply at once into one regime that gives the stricter answer every time. One regime is
    /// returned as it is; combined regimes are flattened and duplicates removed.
    /// </summary>
    public static PrivacyRegime Combine(params PrivacyRegime[] regimes)
    {
        if (regimes == null)
            throw new ArgumentNullException(nameof(regimes));

        var flat = regimes
            .SelectMany(r => r is CombinedPrivacyRegime combined ? combined.Regimes : new[] { r ?? throw new ArgumentNullException(nameof(regimes)) })
            .Distinct()
            .ToList();

        return flat.Count switch
        {
            0 => throw new ArgumentException("Name at least one regime.", nameof(regimes)),
            1 => flat[0],
            _ => new CombinedPrivacyRegime(flat),
        };
    }

    /// <summary>
    /// Finds a regime by its <see cref="PrivacyRegime.Id"/>, ignoring case. Several identifiers separated by
    /// <c>,</c>, <c>+</c> or <c>;</c> (such as <c>gdpr,popia</c>) give their combination. Returns <c>null</c> when
    /// any identifier is unknown. Useful when the regime comes from configuration.
    /// </summary>
    public static PrivacyRegime? Find(string? id)
    {
        var ids = (id ?? string.Empty).Split(Separators, StringSplitOptions.RemoveEmptyEntries).Select(i => i.Trim()).Where(i => i.Length > 0).ToList();
        if (ids.Count == 0)
            return null;

        var found = ids.Select(i => All.FirstOrDefault(r => string.Equals(r.Id, i, StringComparison.OrdinalIgnoreCase))).ToList();
        return found.Any(r => r == null) ? null : Combine(found.ToArray()!);
    }

    /// <summary>Finds a regime, or a combination, by identifier, or throws a message listing the known ones.</summary>
    public static PrivacyRegime Get(string id)
        => Find(id) ?? throw new ArgumentException($"'{id}' is not a known privacy regime. Known: {string.Join(", ", All.Select(r => r.Id))}.", nameof(id));
}
