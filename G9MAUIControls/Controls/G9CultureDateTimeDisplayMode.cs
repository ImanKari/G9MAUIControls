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
    Relative = 3
}
