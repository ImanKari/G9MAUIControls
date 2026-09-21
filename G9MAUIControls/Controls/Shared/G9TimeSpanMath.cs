namespace G9MAUIControls.Controls;

/// <summary>
///     The single years / months / days decomposition used by <c>G9TimeSpanPicker</c> and its sheet.
///     <para>
///         Convention: a year is 365 days, a month is 30 days. Before this helper the field and the
///         sheet each carried their own copy of the arithmetic, and the copies disagreed — months
///         came from <c>days % 365 / 30</c> while days came from <c>days % 30</c>, so 370 days read
///         "1 year 10 days" instead of "1 year 5 days" and the value the sheet returned was not the
///         value it had displayed. Everything that splits or joins a day count goes through here so
///         <see cref="Compose" /> of a <see cref="Decompose" /> result is always the input.
///     </para>
///     <para>
///         Deliberately free of any MAUI dependency so it can be file-linked into a plain
///         <c>net10.0</c> test project.
///     </para>
/// </summary>
public static class G9TimeSpanMath
{
    /// <summary>Days in one picker "year".</summary>
    public const int DaysPerYear = 365;

    /// <summary>Days in one picker "month".</summary>
    public const int DaysPerMonth = 30;

    /// <summary>Largest value the months drum can show (0–11).</summary>
    public const int MaxMonths = 11;

    /// <summary>
    ///     Splits a day count into years, months and days. Negative input is treated as zero.
    ///     <para>
    ///         365 is not a multiple of 30: the last five days of a year (remainder 360–364) would
    ///         compute as month 12, which the 0–11 months drum cannot show. Months are therefore
    ///         clamped to <see cref="MaxMonths" /> and the overflow stays in <c>Days</c>, so that
    ///         range yields 11 months and 30–34 days. <see cref="Compose" /> still round-trips it.
    ///     </para>
    /// </summary>
    public static (int Years, int Months, int Days) Decompose(int totalDays)
    {
        if (totalDays < 0)
            totalDays = 0;

        var years = totalDays / DaysPerYear;
        var remainder = totalDays % DaysPerYear;
        var months = remainder / DaysPerMonth;
        if (months > MaxMonths)
            months = MaxMonths;
        var days = remainder - months * DaysPerMonth;
        return (years, months, days);
    }

    /// <summary>
    ///     Joins years, months and days back into a day count using the same convention as
    ///     <see cref="Decompose" />. Negative parts are treated as zero; the result saturates at
    ///     <see cref="int.MaxValue" /> instead of overflowing.
    /// </summary>
    public static int Compose(int years, int months, int days)
    {
        var total = (long)Math.Max(0, years) * DaysPerYear
                    + (long)Math.Max(0, months) * DaysPerMonth
                    + Math.Max(0, days);
        return total > int.MaxValue ? int.MaxValue : (int)total;
    }
}
