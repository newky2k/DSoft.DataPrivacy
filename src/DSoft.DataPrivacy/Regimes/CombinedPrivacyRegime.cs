using System;
using System.Collections.Generic;
using System.Linq;

namespace DSoft.DataPrivacy.Regimes;

/// <summary>
/// Several laws that apply at once, for example POPIA for a South African organisation plus the GDPR for its UK
/// and EU customers. Every answer is the stricter of the laws combined, so meeting it meets each of them:
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>A retention ground, lawful basis or special data condition is only recognised when every law recognises it.</item>
/// <item>Data is special when any law treats it as special.</item>
/// <item>A right exists when any law gives it.</item>
/// <item>A request is due on the earliest deadline any law sets.</item>
/// <item>A breach must be notified within the shortest fixed window any law sets.</item>
/// </list>
/// Citations name each law, such as <c>GDPR Art 17(3)(c); POPIA s14(1)(a)</c>. Create one with
/// <see cref="PrivacyRegimes.Combine(PrivacyRegime[])"/>.
/// </remarks>
public sealed class CombinedPrivacyRegime : PrivacyRegime
{
    internal CombinedPrivacyRegime(IReadOnlyList<PrivacyRegime> regimes)
        : base(
            string.Join("+", regimes.Select(r => r.Id)),
            string.Join(" + ", regimes.Select(r => r.Name)),
            string.Join("; ", regimes.Select(r => r.Legislation)))
    {
        Regimes = regimes;
    }

    /// <summary>The laws combined.</summary>
    public IReadOnlyList<PrivacyRegime> Regimes { get; }

    /// <inheritdoc />
    public override string ControllerTerm => JoinDistinct(r => r.ControllerTerm);

    /// <inheritdoc />
    public override string ProcessorTerm => JoinDistinct(r => r.ProcessorTerm);

    /// <inheritdoc />
    public override string SpecialDataTerm => JoinDistinct(r => r.SpecialDataTerm);

    /// <summary>Every category any of the laws treats as special.</summary>
    public override PersonalDataCategory SpecialCategories
        => Regimes.Aggregate(PersonalDataCategory.None, (all, r) => all | r.SpecialCategories);

    /// <summary>
    /// The shortest fixed window. A law that only says "as soon as reasonably possible" does not lengthen it, and
    /// the result is <c>null</c> only when none of the laws sets a fixed window.
    /// </summary>
    public override TimeSpan? BreachNotificationWindow
        => Regimes.Select(r => r.BreachNotificationWindow).Where(w => w.HasValue).OrderBy(w => w).FirstOrDefault();

    /// <inheritdoc />
    public override string BreachNotificationRule
        => string.Join(" ", Regimes.Select(r => $"{r.Name}: {r.BreachNotificationRule}"));

    /// <summary>The citations under every law, or <c>null</c> when any law does not recognise the ground.</summary>
    public override string? Cite(RetentionGround ground) => CiteAll(r => r.Cite(ground));

    /// <summary>The citations under every law, or <c>null</c> when any law does not recognise the basis.</summary>
    public override string? Cite(LawfulBasis basis) => CiteAll(r => r.Cite(basis));

    /// <summary>The citations under every law, or <c>null</c> when any law does not recognise the condition.</summary>
    public override string? Cite(SpecialDataCondition condition) => CiteAll(r => r.Cite(condition));

    /// <summary>The citations under the laws that give the right, or <c>null</c> when none does.</summary>
    public override string? Cite(DataSubjectRequestType type)
    {
        var citations = Regimes.Select(r => (r.Name, Citation: r.Cite(type))).Where(c => c.Citation != null).ToList();
        return citations.Count == 0 ? null : string.Join("; ", citations.Select(c => $"{c.Name} {c.Citation}"));
    }

    /// <summary>The earliest deadline any of the laws sets, or <c>null</c> when none sets a fixed period.</summary>
    public override DateTime? RequestDeadline(DataSubjectRequestType type, DateTime receivedOn, bool extended = false, Func<DateTime, bool>? isNonWorkingDay = null)
        => Regimes.Select(r => r.RequestDeadline(type, receivedOn, extended, isNonWorkingDay)).Where(d => d.HasValue).OrderBy(d => d).FirstOrDefault();

    private string? CiteAll(Func<PrivacyRegime, string?> cite)
    {
        var citations = Regimes.Select(r => (r.Name, Citation: cite(r))).ToList();
        return citations.Any(c => c.Citation == null) ? null : string.Join("; ", citations.Select(c => $"{c.Name} {c.Citation}"));
    }

    private string JoinDistinct(Func<PrivacyRegime, string> term)
        => string.Join(" / ", Regimes.Select(term).Distinct(StringComparer.Ordinal));
}
