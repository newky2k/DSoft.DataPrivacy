using System;
using System.Collections.Generic;
using System.Linq;

namespace DSoft.DataPrivacy.Regimes;

/// <summary>
/// A data protection law: what it calls things, which provisions the neutral grounds, bases and conditions rest on,
/// which rights it gives, what counts as special data, and its deadlines. Classification stays neutral; the regime
/// in force turns it into citations and checks it.
/// </summary>
/// <remarks>
/// The citations are a guide to the provisions a decision relies on. They are not legal advice. Have the
/// organisation's data protection or information officer confirm them for the processing concerned.
/// </remarks>
public abstract class PrivacyRegime
{
    /// <summary>Creates a regime.</summary>
    protected PrivacyRegime(string id, string name, string legislation)
    {
        Id = id;
        Name = name;
        Legislation = legislation;
    }

    /// <summary>A short, stable identifier for configuration, such as <c>gdpr</c> or <c>popia</c>.</summary>
    public string Id { get; }

    /// <summary>The common name, such as <c>GDPR</c>.</summary>
    public string Name { get; }

    /// <summary>The legislation, in full.</summary>
    public string Legislation { get; }

    /// <summary>What the law calls the organisation that decides how data is processed.</summary>
    public abstract string ControllerTerm { get; }

    /// <summary>What the law calls an organisation that processes data on the controller's behalf.</summary>
    public abstract string ProcessorTerm { get; }

    /// <summary>What the law calls its special data.</summary>
    public abstract string SpecialDataTerm { get; }

    /// <summary>The categories the law treats as special data.</summary>
    public abstract PersonalDataCategory SpecialCategories { get; }

    /// <summary>
    /// How long the organisation has to notify the regulator of a breach, or <c>null</c> when the law says
    /// "as soon as reasonably possible" rather than a fixed period.
    /// </summary>
    public abstract TimeSpan? BreachNotificationWindow { get; }

    /// <summary>The breach notification rule in words.</summary>
    public abstract string BreachNotificationRule { get; }

    /// <summary>The rights the law gives a data subject.</summary>
    public IReadOnlyList<DataSubjectRequestType> RequestTypes
        => Enum.GetValues(typeof(DataSubjectRequestType)).Cast<DataSubjectRequestType>().Where(t => Cite(t) != null).ToList();

    /// <summary>True when values in <paramref name="categories"/> are special data under this law.</summary>
    public bool IsSpecial(PersonalDataCategory categories) => categories.HasAny(SpecialCategories);

    /// <summary>The provision a retention ground rests on, or <c>null</c> when it is not a ground under this law.</summary>
    public abstract string? Cite(RetentionGround ground);

    /// <summary>The provision a lawful basis rests on, or <c>null</c> when it is not a basis under this law.</summary>
    public abstract string? Cite(LawfulBasis basis);

    /// <summary>The provision a special data condition rests on, or <c>null</c> when it is not a condition under this law.</summary>
    public abstract string? Cite(SpecialDataCondition condition);

    /// <summary>The provision giving a right, or <c>null</c> when the law does not give it.</summary>
    public abstract string? Cite(DataSubjectRequestType type);

    /// <summary>True when <paramref name="ground"/> is a retention ground under this law.</summary>
    public bool Recognises(RetentionGround ground) => ground != RetentionGround.None && Cite(ground) != null;

    /// <summary>True when <paramref name="basis"/> is a lawful basis under this law.</summary>
    public bool Recognises(LawfulBasis basis) => basis != LawfulBasis.Undecided && Cite(basis) != null;

    /// <summary>True when <paramref name="condition"/> is a special data condition under this law.</summary>
    public bool Recognises(SpecialDataCondition condition) => condition != SpecialDataCondition.None && Cite(condition) != null;

    /// <summary>
    /// When a response to a request is due, or <c>null</c> when the law sets no fixed period for it (or does not
    /// give the right at all). The clock starts on <paramref name="receivedOn"/>: pass a later date when the law
    /// lets it start once identity is confirmed or a fee paid.
    /// </summary>
    /// <param name="type">The right exercised.</param>
    /// <param name="receivedOn">The date the clock started. Only the date part is used.</param>
    /// <param name="extended">True when the deadline has been extended as far as the law allows.</param>
    /// <param name="isNonWorkingDay">Which days are not working days. Defaults to Saturdays and Sundays; add public holidays.</param>
    public abstract DateTime? RequestDeadline(DataSubjectRequestType type, DateTime receivedOn, bool extended = false, Func<DateTime, bool>? isNonWorkingDay = null);

    /// <summary>
    /// True when a request is overdue on <paramref name="today"/>. A request with no fixed period is never
    /// reported overdue; track it against your own service level.
    /// </summary>
    public bool IsOverdue(DataSubjectRequestType type, DateTime receivedOn, DateTime today, bool extended = false, Func<DateTime, bool>? isNonWorkingDay = null)
        => RequestDeadline(type, receivedOn, extended, isNonWorkingDay) is DateTime due && today.Date > due;

    /// <inheritdoc />
    public override string ToString() => Name;

    /// <summary>Moves a date that is not a working day to the next working day.</summary>
    protected static DateTime NextWorkingDay(DateTime date, Func<DateTime, bool>? isNonWorkingDay)
    {
        isNonWorkingDay ??= d => d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

        var guard = 0;
        while (isNonWorkingDay(date))
        {
            date = date.AddDays(1);
            if (++guard > 31)
                throw new InvalidOperationException("No working day found within a month of the deadline; check isNonWorkingDay.");
        }

        return date;
    }
}
