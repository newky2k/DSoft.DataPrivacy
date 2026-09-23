using System;
using System.Collections.Generic;
using DSoft.EntityFrameworkCore.GDPR.Auditing;
using DSoft.EntityFrameworkCore.GDPR.Rules;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DSoft.EntityFrameworkCore.GDPR.Infrastructure;

/// <summary>What a custom anonymiser is given.</summary>
/// <param name="EntityType">The entity type the value belongs to.</param>
/// <param name="Property">The property being anonymised.</param>
/// <param name="Value">The current value, never <c>null</c>.</param>
public sealed record AnonymisationContext(IEntityType EntityType, IProperty Property, object Value);

/// <summary>The settings made through <c>UseGdpr</c>.</summary>
public sealed class GdprOptions
{
    internal Dictionary<string, Func<AnonymisationContext, object?>> Anonymisers { get; } = new(StringComparer.Ordinal);

    internal List<IPersonalDataChangeObserver> ChangeObservers { get; } = new();

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

/// <summary>Configures GDPR support for a context.</summary>
public sealed class GdprOptionsBuilder
{
    internal GdprOptionsBuilder(GdprOptions options)
    {
        Options = options;
    }

    internal GdprOptions Options { get; }

    /// <summary>
    /// Sets the secret key for <see cref="AnonymisationMethod.Hash"/>. Use at least 32 random bytes, keep it out of
    /// source control, and keep it stable: a new key means old hashes no longer match.
    /// </summary>
    public GdprOptionsBuilder UseHashKey(byte[] key)
    {
        Options.Hasher = new PersonalDataHasher(key);
        return this;
    }

    /// <summary>Registers an anonymiser that properties can name with <see cref="AnonymisationMethod.Custom"/>.</summary>
    public GdprOptionsBuilder AddAnonymiser(string name, Func<AnonymisationContext, object?> anonymiser)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("An anonymiser needs a name.", nameof(name));

        Options.Anonymisers[name] = anonymiser ?? throw new ArgumentNullException(nameof(anonymiser));
        return this;
    }

    /// <summary>Applies controller-wide erasure rules, such as always keeping health data.</summary>
    public GdprOptionsBuilder UseErasurePolicy(ErasurePolicy policy)
    {
        Options.ErasurePolicy = policy ?? throw new ArgumentNullException(nameof(policy));
        return this;
    }

    /// <summary>Changes the text written by <see cref="AnonymisationMethod.Redact"/>. Defaults to <c>[erased]</c>.</summary>
    public GdprOptionsBuilder UseRedactedText(string text)
    {
        Options.RedactedText = text ?? throw new ArgumentNullException(nameof(text));
        return this;
    }

    /// <summary>Changes how many relationships deep a record may be from the data subject it belongs to. Defaults to 4.</summary>
    public GdprOptionsBuilder UseMaxLinkDepth(int depth)
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
    public GdprOptionsBuilder ObserveChanges(IPersonalDataChangeObserver observer)
    {
        Options.ChangeObservers.Add(observer ?? throw new ArgumentNullException(nameof(observer)));
        return this;
    }
}
