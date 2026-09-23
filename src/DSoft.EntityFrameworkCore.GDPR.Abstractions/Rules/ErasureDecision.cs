using System;
using System.Collections.Generic;

namespace DSoft.EntityFrameworkCore.GDPR.Rules;

/// <summary>What is known about a record when deciding how to erase it.</summary>
public sealed record RecordErasureFacts
{
    /// <summary>The union of the categories of the record's classified values.</summary>
    public PersonalDataCategory Categories { get; init; }

    /// <summary>What the entity is configured to do on erasure.</summary>
    public ErasureAction EntityErasure { get; init; }

    /// <summary>The Article 17(3) ground configured on the entity, when it is retained.</summary>
    public ErasureExemption EntityExemption { get; init; }

    /// <summary>The reason configured with <see cref="EntityExemption"/>.</summary>
    public string? ExemptionReason { get; init; }

    /// <summary>True when data about the person must be kept for an actual or expected legal claim.</summary>
    public bool LegalHold { get; init; }
}

/// <summary>What is known about a single value on a record that is being anonymised.</summary>
public sealed record FieldErasureFacts
{
    /// <summary>What kind of personal data the value is.</summary>
    public PersonalDataCategory Categories { get; init; }

    /// <summary><see cref="ErasureAction.Retain"/> keeps the value.</summary>
    public ErasureAction Erasure { get; init; }

    /// <summary>The Article 17(3) ground for keeping the value.</summary>
    public ErasureExemption RetentionExemption { get; init; }

    /// <summary>The configured method; <see cref="AnonymisationMethod.Default"/> lets the rule choose.</summary>
    public AnonymisationMethod Method { get; init; }

    /// <summary>The value's type, without <see cref="Nullable{T}"/>.</summary>
    public Type ValueType { get; init; } = typeof(string);

    /// <summary>True when the column accepts <c>null</c>.</summary>
    public bool IsNullable { get; init; }
}

/// <summary>The decision for a record or a value.</summary>
public sealed record ErasureOutcome
{
    private ErasureOutcome(ErasureAction action, ErasureExemption exemption, AnonymisationMethod method, string reason)
    {
        Action = action;
        Exemption = exemption;
        Method = method;
        Reason = reason;
    }

    /// <summary><see cref="ErasureAction.Delete"/>, <see cref="ErasureAction.Anonymise"/> or <see cref="ErasureAction.Retain"/>.</summary>
    public ErasureAction Action { get; }

    /// <summary>The Article 17(3) ground, when retained.</summary>
    public ErasureExemption Exemption { get; }

    /// <summary>The method to use, when a value is anonymised.</summary>
    public AnonymisationMethod Method { get; }

    /// <summary>Why, in words that can go in an erasure log.</summary>
    public string Reason { get; }

    /// <summary>Delete the record.</summary>
    public static ErasureOutcome Delete(string reason) => new(ErasureAction.Delete, ErasureExemption.None, AnonymisationMethod.Default, reason);

    /// <summary>Keep the record and anonymise its personal data, or anonymise a value with <paramref name="method"/>.</summary>
    public static ErasureOutcome Anonymise(string reason, AnonymisationMethod method = AnonymisationMethod.Default)
        => new(ErasureAction.Anonymise, ErasureExemption.None, method, reason);

    /// <summary>Keep the record or value under <paramref name="exemption"/>.</summary>
    public static ErasureOutcome Retain(ErasureExemption exemption, string reason)
    {
        if (exemption == ErasureExemption.None)
            throw new ArgumentException("Retaining data after an erasure request needs an Article 17(3) exemption.", nameof(exemption));

        return new(ErasureAction.Retain, exemption, AnonymisationMethod.Default, reason);
    }
}

/// <summary>A category of data kept on every erasure, and the ground for keeping it.</summary>
public sealed record CategoryRetention(PersonalDataCategory Categories, ErasureExemption Exemption, string Reason);

/// <summary>
/// Controller-wide erasure rules that apply on top of how each entity is configured.
/// </summary>
/// <example>
/// <code>
/// // A health provider keeps clinical data whatever entity it is found on.
/// var policy = new ErasurePolicy()
///     .RetainCategory(PersonalDataCategory.Health, ErasureExemption.PublicHealth, "Health records retention schedule");
/// </code>
/// </example>
public sealed class ErasurePolicy
{
    private readonly List<CategoryRetention> _retainedCategories = new();

    /// <summary>A policy with no controller-wide rules: every entity is erased as it is configured.</summary>
    public static ErasurePolicy Default { get; } = new();

    /// <summary>Categories whose records are always kept.</summary>
    public IReadOnlyList<CategoryRetention> RetainedCategories => _retainedCategories;

    /// <summary>Keeps every record holding any of <paramref name="categories"/>, under <paramref name="exemption"/>.</summary>
    public ErasurePolicy RetainCategory(PersonalDataCategory categories, ErasureExemption exemption, string reason)
    {
        if (ReferenceEquals(this, Default))
            throw new InvalidOperationException("The default policy cannot be changed; create a new ErasurePolicy.");
        if (categories == PersonalDataCategory.None)
            throw new ArgumentException("Name at least one category.", nameof(categories));
        if (exemption == ErasureExemption.None)
            throw new ArgumentException("Retaining data after an erasure request needs an Article 17(3) exemption.", nameof(exemption));

        _retainedCategories.Add(new CategoryRetention(categories, exemption, reason));
        return this;
    }
}

/// <summary>
/// Decides, for each record and value, whether an erasure deletes it, anonymises it or keeps it on a stated
/// Article 17(3) ground. It is pure: the engine gathers the facts, this rule decides, and a test can cover every
/// branch without a database.
/// </summary>
public static class ErasureDecision
{
    /// <summary>
    /// Decides what happens to a record, in this order: a legal hold keeps it; an entity configured to be retained
    /// keeps it; a category the policy retains keeps it; an entity configured to be anonymised is anonymised;
    /// anything else is deleted.
    /// </summary>
    /// <remarks>
    /// A record that is deleted here can still be kept by the engine when other records that are kept depend on it.
    /// See <see cref="KeepForDependents"/>.
    /// </remarks>
    public static ErasureOutcome DecideRecord(RecordErasureFacts facts, ErasurePolicy? policy = null)
    {
        if (facts == null)
            throw new ArgumentNullException(nameof(facts));

        policy ??= ErasurePolicy.Default;

        if (facts.LegalHold)
            return ErasureOutcome.Retain(ErasureExemption.LegalClaims, "Held for a legal claim.");

        if (facts.EntityErasure == ErasureAction.Retain)
        {
            if (facts.EntityExemption == ErasureExemption.None)
                throw new InvalidOperationException("An entity retained on erasure must name its Article 17(3) exemption.");

            return ErasureOutcome.Retain(facts.EntityExemption, facts.ExemptionReason ?? "Retained under a duty to keep the record.");
        }

        foreach (var rule in policy.RetainedCategories)
        {
            if (facts.Categories.HasAny(rule.Categories))
                return ErasureOutcome.Retain(rule.Exemption, rule.Reason);
        }

        if (facts.EntityErasure == ErasureAction.Anonymise)
            return ErasureOutcome.Anonymise("The record is kept without the person's data.");

        return ErasureOutcome.Delete("Erased at the person's request.");
    }

    /// <summary>
    /// The outcome for a record that would be deleted but must stay because records that are kept point at it.
    /// Its personal data is anonymised so the references remain valid.
    /// </summary>
    public static ErasureOutcome KeepForDependents()
        => ErasureOutcome.Anonymise("Kept without the person's data because retained records depend on it.");

    /// <summary>
    /// Decides how a value on an anonymised record is treated: kept if configured to be retained, otherwise
    /// anonymised with its configured method, or with a method chosen from its type (see <see cref="AnonymisationMethod.Default"/>).
    /// </summary>
    public static ErasureOutcome DecideField(FieldErasureFacts facts)
    {
        if (facts == null)
            throw new ArgumentNullException(nameof(facts));

        if (facts.Erasure == ErasureAction.Retain)
        {
            if (facts.RetentionExemption == ErasureExemption.None)
                throw new InvalidOperationException("A value retained on erasure must name its Article 17(3) exemption.");

            return ErasureOutcome.Retain(facts.RetentionExemption, "Value retained with its record.");
        }

        var method = facts.Method != AnonymisationMethod.Default ? facts.Method : DefaultMethod(facts);
        return ErasureOutcome.Anonymise("Value anonymised.", method);
    }

    private static AnonymisationMethod DefaultMethod(FieldErasureFacts facts)
    {
        if (facts.IsNullable)
            return AnonymisationMethod.Null;

        var type = facts.ValueType;
        if (type == typeof(string) || type == typeof(byte[]))
            return AnonymisationMethod.Redact;

        if (type == typeof(DateTime) || type == typeof(DateTimeOffset) || type.FullName == "System.DateOnly")
            return AnonymisationMethod.Generalise;

        return AnonymisationMethod.Null;
    }
}
