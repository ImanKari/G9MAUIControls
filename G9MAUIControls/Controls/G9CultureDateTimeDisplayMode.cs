namespace G9MAUIControls.Controls;

public enum G9CultureDateTimeDisplayMode
{
    DateTime = 0,
    Date = 1,
    Time = 2,

    /// <summary>
    ///     Relative "time ago" phrase plus the clock time, e.g. "۶ ساعت پیش - ۱۴:۱۹" / "6 hours ago -
    ///     14:19". Localized via the <c>TimeAgo*</c> keys (see <c>RelativeTimeFormatter</c>).
    ///     Unlike the absolute modes, this one is NOT forced LTR — the phrase contains localized words
    ///     that must follow the current culture's flow direction.
    /// </summary>
    Relative = 3,

    /// <summary>
    ///     A due-date style: «امروز، ۱۸:۰۰» / "Today, 18:00" — and «فردا» / «دیروز» — when the value
    ///     falls on today, tomorrow or yesterday; otherwise exactly <see cref="DateTime" /> (a midnight
    ///     value drops its "00:00" either way, since it almost always means "a date with no time").
    ///     "Today" is measured against <see cref="G9CultureDateTimeLabel.RelativeDayNow" />. The day words
    ///     come from <c>G9Strings</c> (<c>Today</c> / <c>Tomorrow</c> / <c>Yesterday</c>).
    /// </summary>
    RelativeDay = 4
}
