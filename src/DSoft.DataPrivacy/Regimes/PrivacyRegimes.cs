using System;
using System.Collections.Generic;
using System.Linq;

namespace DSoft.DataPrivacy.Regimes;

/// <summary>The regimes this library ships.</summary>
public static class PrivacyRegimes
{
    /// <summary>The EU GDPR and the UK GDPR.</summary>
    public static PrivacyRegime Gdpr { get; } = new GdprRegime();

    /// <summary>South Africa's POPIA.</summary>
    public static PrivacyRegime Popia { get; } = new PopiaRegime();

    /// <summary>Every shipped regime.</summary>
    public static IReadOnlyList<PrivacyRegime> All { get; } = new[] { Gdpr, Popia };

    /// <summary>Finds a regime by its <see cref="PrivacyRegime.Id"/>, ignoring case. Useful when the regime comes from configuration.</summary>
    public static PrivacyRegime? Find(string? id)
        => All.FirstOrDefault(r => string.Equals(r.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a regime by its identifier, or throws a message listing the known ones.</summary>
    public static PrivacyRegime Get(string id)
        => Find(id) ?? throw new ArgumentException($"'{id}' is not a known privacy regime. Known: {string.Join(", ", All.Select(r => r.Id))}.", nameof(id));
}
