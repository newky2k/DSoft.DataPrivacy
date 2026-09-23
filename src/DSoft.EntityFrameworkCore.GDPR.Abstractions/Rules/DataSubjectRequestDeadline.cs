using System;

namespace DSoft.EntityFrameworkCore.GDPR.Rules;

/// <summary>
/// When a response to a data subject request is due under Article 12(3): one month from receipt, extendable by a
/// further two months for complex or numerous requests.
/// </summary>
/// <remarks>
/// Follows the UK regulator's published method: the day of receipt is day one, the deadline is the corresponding
/// calendar date in the following month, the last day of that month when there is no corresponding date, and the
/// next working day when the deadline is not a working day. The clock starts when the request is received, or
/// later when identity must first be confirmed or clarification sought; pass that date as the received date.
/// </remarks>
public static class DataSubjectRequestDeadline
{
    /// <summary>The number of months a deadline may be extended by.</summary>
    public const int MaximumExtensionMonths = 2;

    /// <summary>Calculates the due date.</summary>
    /// <param name="receivedOn">The date the clock started. Only the date part is used.</param>
    /// <param name="extended">True when the deadline has been extended by the further two months.</param>
    /// <param name="isNonWorkingDay">Says which days are not working days. Defaults to Saturdays and Sundays; add bank holidays as needed.</param>
    public static DateTime Calculate(DateTime receivedOn, bool extended = false, Func<DateTime, bool>? isNonWorkingDay = null)
    {
        isNonWorkingDay ??= IsWeekend;

        // DateTime.AddMonths already clamps 31 January to the last day of February.
        var due = receivedOn.Date.AddMonths(1 + (extended ? MaximumExtensionMonths : 0));

        var guard = 0;
        while (isNonWorkingDay(due))
        {
            due = due.AddDays(1);
            if (++guard > 31)
                throw new InvalidOperationException("No working day found within a month of the deadline; check isNonWorkingDay.");
        }

        return due;
    }

    /// <summary>True when the request is overdue on <paramref name="today"/>.</summary>
    public static bool IsOverdue(DateTime receivedOn, DateTime today, bool extended = false, Func<DateTime, bool>? isNonWorkingDay = null)
        => today.Date > Calculate(receivedOn, extended, isNonWorkingDay);

    private static bool IsWeekend(DateTime date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
}
