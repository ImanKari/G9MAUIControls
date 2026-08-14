using G9MAUIControls.Localization;
using System.Globalization;

namespace G9MAUIControls.Helpers;

/// <summary>
///     Formats a past <see cref="DateTime" /> as a localized relative "time ago" phrase
///     (seconds → minutes → hours → days → weeks → months → years), e.g. "۶ ساعت پیش" / "6 hours ago".
///     Numbers render in the current culture's digits (Persian digits under fa), matching how
///     <c>G9CultureDateTimeLabel</c> renders dates. Values are compared against the Tehran wall clock
///     (<see cref="DateTimeOffset.UtcNow" />) — the same clock history rows are stamped
///     with — so the phrase is consistent regardless of device time zone.
/// </summary>
public static class G9RelativeTimeFormatter
{
    /// <summary>Returns the relative phrase alone, e.g. "۲ روز پیش" / "2 days ago".</summary>
    public static string FormatAgo(DateTime value, CultureInfo? culture = null)
    {
        culture ??= G9Culture.CurrentCulture;

        var delta = DateTimeOffset.UtcNow - value;
        if (delta < TimeSpan.Zero)
        {
            delta = TimeSpan.Zero;
        }

        if (delta.TotalMinutes < 1)
        {
            return G9Strings.Get(G9StringKey.TimeAgoJustNow);
        }

        if (delta.TotalMinutes < 60)
        {
            return Format(G9Strings.Get(G9StringKey.TimeAgoMinutesFormat), (int)delta.TotalMinutes, culture);
        }

        if (delta.TotalHours < 24)
        {
            return Format(G9Strings.Get(G9StringKey.TimeAgoHoursFormat), (int)delta.TotalHours, culture);
        }

        if (delta.TotalDays < 7)
        {
            return Format(G9Strings.Get(G9StringKey.TimeAgoDaysFormat), (int)delta.TotalDays, culture);
        }

        if (delta.TotalDays < 30)
        {
            return Format(G9Strings.Get(G9StringKey.TimeAgoWeeksFormat), (int)(delta.TotalDays / 7), culture);
        }

        if (delta.TotalDays < 365)
        {
            return Format(G9Strings.Get(G9StringKey.TimeAgoMonthsFormat), (int)(delta.TotalDays / 30), culture);
        }

        return Format(G9Strings.Get(G9StringKey.TimeAgoYearsFormat), (int)(delta.TotalDays / 365), culture);
    }

    /// <summary>
    ///     Returns the relative phrase joined with the clock time, e.g. "۶ ساعت پیش - ۱۴:۱۹" /
    ///     "6 hours ago - 14:19" — the format the state-transition history timeline shows.
    /// </summary>
    public static string FormatAgoWithTime(DateTime value, CultureInfo? culture = null)
    {
        culture ??= G9Culture.CurrentCulture;
        var ago = FormatAgo(value, culture);
        var time = value.ToString("HH:mm", culture);
        return $"{ago} - {time}";
    }

    private static string Format(string? pattern, int quantity, CultureInfo culture)
    {
        return string.Format(culture, pattern ?? "{0}", Math.Max(quantity, 1));
    }
}
