using System;
using System.Collections.Generic;
using DSoft.DataPrivacy.EntityFrameworkCore.Auditing;
using DSoft.DataPrivacy.Regimes;
using DSoft.DataPrivacy.Rules;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DSoft.DataPrivacy.EntityFrameworkCore.Infrastructure;

/// <summary>What a custom anonymiser is given.</summary>
/// <param name="EntityType">The entity type the value belongs to.</param>
/// <param name="Property">The property being anonymised.</param>
/// <param name="Value">The current value, never <c>null</c>.</param>
public sealed record AnonymisationContext(IEntityType EntityType, IProperty Property, object Value);

/// <summary>The settings made through <c>UseDataPrivacy</c>.</summary>
public sealed class DataPrivacyOptions
{
    internal Dictionary<string, Func<AnonymisationContext, object?>> Anonymisers { get; } = new(StringComparer.Ordinal);

    internal List<IPersonalDataChangeObserver> ChangeObservers { get; } = new();

    /// <summary>
    /// The data protection law in force, which supplies citations and checks retention grounds. <c>null</c> until
    /// <see cref="DataPrivacyOptionsBuilder.UseRegime(PrivacyRegime)"/> is called.
    /// </summary>
    public PrivacyRegime? Regime { get; internal set; }

    /// <summary>The hasher used by <see cref="AnonymisationMethod.Hash"/>, when a key has been configured.</summary>
    public PersonalDataHasher? Hasher { get; internal set; }

    /// <summary>Controller-wide erasure rules.</summary>
    public ErasurePolicy ErasurePolicy { get; internal set; } = ErasurePolicy.Default;

    /// <summary>The text written by <see cref="AnonymisationMethod.Redact"/>.</summary>
    public string RedactedText { get; internal set; } = "[erased]";

    /// <summary>How many relationships deep to look for a path from a record to a data subject.</summary>
    public int MaxLinkDepth { get; internal set; } = 4;

    /// <summary>The names of the registered custom anonymisers.</summary>
    public IReadOnlyCollection<string> AnonymiserNames => Anonymisers.Keys;

    internal Func<AnonymisationContext, object?>? FindAnonymiser(string name)
        => Anonymisers.TryGetValue(name, out var anonymiser) ? anonymiser : null;
}

/// <summary>Configures data privacy support for a context.</summary>
public sealed class DataPrivacyOptionsBuilder
{
    internal DataPrivacyOptionsBuilder(DataPrivacyOptions options)
    {
        Options = options;
    }

    internal DataPrivacyOptions Options { get; }

    /// <summary>
    /// Sets the data protection law in force, such as <see cref="PrivacyRegimes.Gdpr"/> or
    /// <see cref="PrivacyRegimes.Popia"/>. Erasure logs then cite its provisions, and <c>Validate()</c> reports
    /// retention grounds the law does not recognise.
    /// </summary>
    public DataPrivacyOptionsBuilder UseRegime(PrivacyRegime regime)
    {
        Options.Regime = regime ?? throw new ArgumentNullException(nameof(regime));
        return this;
    }

    /// <summary>Sets the data protection law in force by its identifier, such as <c>gdpr</c> or <c>popia</c>, for example from configuration.</summary>
    public DataPrivacyOptionsBuilder UseRegime(string regimeId) => UseRegime(PrivacyRegimes.Get(regimeId));

    /// <summary>
    /// Sets the secret key for <see cref="AnonymisationMethod.Hash"/>. Use at least 32 random bytes, keep it out of
    /// source control, and keep it stable: a new key means old hashes no longer match.
    /// </summary>
    public DataPrivacyOptionsBuilder UseHashKey(byte[] key)
    {
        Options.Hasher = new PersonalDataHasher(key);
        return this;
    }

    /// <summary>Registers an anonymiser that properties can name with <see cref="AnonymisationMethod.Custom"/>.</summary>
    public DataPrivacyOptionsBuilder AddAnonymiser(string name, Func<AnonymisationContext, object?> anonymiser)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An anonymiser needs a name.", nameof(name));

        Options.Anonymisers[name] = anonymiser ?? throw new ArgumentNullException(nameof(anonymiser));
        return this;
    }

    /// <summary>Applies organisation-wide erasure rules, such as always keeping health data.</summary>
    public DataPrivacyOptionsBuilder UseErasurePolicy(ErasurePolicy policy)
    {
        Options.ErasurePolicy = policy ?? throw new ArgumentNullException(nameof(policy));
        return this;
    }

    /// <summary>Changes the text written by <see cref="AnonymisationMethod.Redact"/>. Defaults to <c>[erased]</c>.</summary>
    public DataPrivacyOptionsBuilder UseRedactedText(string text)
    {
        Options.RedactedText = text ?? throw new ArgumentNullException(nameof(text));
        return this;
    }

    /// <summary>Changes how many relationships deep a record may be from the data subject it belongs to. Defaults to 4.</summary>
    public DataPrivacyOptionsBuilder UseMaxLinkDepth(int depth)
    {
        if (depth < 1)
            throw new ArgumentOutOfRangeException(nameof(depth), "The depth must be at least 1.");

        Options.MaxLinkDepth = depth;
        return this;
    }

    /// <summary>
    /// Tells <paramref name="observer"/> which personal data was added, changed or deleted each time changes are
    /// saved, for a rectification log or an audit trail. Values are never passed, only which properties changed.
    /// </summary>
    public DataPrivacyOptionsBuilder ObserveChanges(IPersonalDataChangeObserver observer)
    {
        Options.ChangeObservers.Add(observer ?? throw new ArgumentNullException(nameof(observer)));
        return this;
    }
}
