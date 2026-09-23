using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DSoft.DataPrivacy.Rules;

/// <summary>
/// A retention period in calendar units. Years and months are counted on the calendar, not as a fixed number of
/// days, so "six years" ends on the same date six years later.
/// </summary>
public readonly struct RetentionPeriod : IEquatable<RetentionPeriod>
{
    private static readonly Regex IsoPattern = new(
        @"^P(?:(?<y>\d+)Y)?(?:(?<m>\d+)M)?(?:(?<w>\d+)W)?(?:(?<d>\d+)D)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Creates a period.</summary>
    public RetentionPeriod(int years = 0, int months = 0, int days = 0)
    {
        if (years < 0 || months < 0 || days < 0)
            throw new ArgumentOutOfRangeException(nameof(years), "A retention period cannot be negative.");

        Years = years;
        Months = months;
        Days = days;
    }

    /// <summary>Whole years.</summary>
    public int Years { get; }

    /// <summary>Whole months.</summary>
    public int Months { get; }

    /// <summary>Whole days.</summary>
    public int Days { get; }

    /// <summary>True for a zero period.</summary>
    public bool IsZero => Years == 0 && Months == 0 && Days == 0;

    /// <summary>A period of whole years.</summary>
    public static RetentionPeriod FromYears(int years) => new(years: years);

    /// <summary>A period of whole months.</summary>
    public static RetentionPeriod FromMonths(int months) => new(months: months);

    /// <summary>A period of whole days.</summary>
    public static RetentionPeriod FromDays(int days) => new(days: days);

    /// <summary>Parses an ISO 8601 date period such as <c>P6Y</c>, <c>P18M</c> or <c>P1Y6M</c>. Weeks are read as seven days.</summary>
    public static RetentionPeriod Parse(string value)
    {
        if (!TryParse(value, out var period))
            throw new FormatException($"'{value}' is not an ISO 8601 period such as P6Y, P18M or P30D.");

        return period;
    }

    /// <summary>Tries to parse an ISO 8601 date period.</summary>
    public static bool TryParse(string? value, out RetentionPeriod period)
    {
        period = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var match = IsoPattern.Match(value!.Trim());
        if (!match.Success || match.Value.Length < 3)
            return false;

        static int Read(Match m, string group) => m.Groups[group].Success ? int.Parse(m.Groups[group].Value, CultureInfo.InvariantCulture) : 0;

        period = new RetentionPeriod(Read(match, "y"), Read(match, "m"), Read(match, "d") + (7 * Read(match, "w")));
        return true;
    }

    /// <summary>The latest trigger date that is now due: anything on or before it has passed the period.</summary>
    public DateTimeOffset CutoffFrom(DateTimeOffset now) => now.AddYears(-Years).AddMonths(-Months).AddDays(-Days);

    /// <summary>The latest trigger date that is now due.</summary>
    public DateTime CutoffFrom(DateTime now) => now.AddYears(-Years).AddMonths(-Months).AddDays(-Days);

    /// <summary>True when a record triggered at <paramref name="triggeredAt"/> has passed the period. A record with no trigger date never has.</summary>
    public bool HasElapsed(DateTimeOffset? triggeredAt, DateTimeOffset now)
        => triggeredAt.HasValue && triggeredAt.Value <= CutoffFrom(now);

    /// <summary>The ISO 8601 form, such as <c>P6Y</c>.</summary>
    public override string ToString()
    {
        if (IsZero)
            return "P0D";

        var builder = new StringBuilder("P");
        if (Years > 0) builder.Append(Years.ToString(CultureInfo.InvariantCulture)).Append('Y');
        if (Months > 0) builder.Append(Months.ToString(CultureInfo.InvariantCulture)).Append('M');
        if (Days > 0) builder.Append(Days.ToString(CultureInfo.InvariantCulture)).Append('D');
        return builder.ToString();
    }

    /// <inheritdoc />
    public bool Equals(RetentionPeriod other) => Years == other.Years && Months == other.Months && Days == other.Days;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is RetentionPeriod other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => (Years * 397) ^ (Months * 31) ^ Days;

    /// <summary>Equality.</summary>
    public static bool operator ==(RetentionPeriod left, RetentionPeriod right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(RetentionPeriod left, RetentionPeriod right) => !left.Equals(right);
}
