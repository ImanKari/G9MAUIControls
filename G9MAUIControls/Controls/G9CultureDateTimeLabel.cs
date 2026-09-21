using G9MAUIControls.Controls;
using G9MAUIControls.Helpers;
using G9MAUIControls.Localization;
using Maui.BindableProperty.Generator.Core;
using System.Globalization;

namespace G9MAUIControls.Controls;

/// <summary>
///     A <see cref="Label" /> that renders a <see cref="DateTime" /> in the app's active culture — the
///     Persian (Jalali) calendar under <c>fa</c>, the Gregorian one otherwise — and re-renders itself
///     when the language changes.
///     <para>
///         <b>For layout it is an ordinary <see cref="Label" />.</b> It inherits its parent's flow
///         direction, and <see cref="Label.HorizontalTextAlignment" /> / <see cref="View.HorizontalOptions" />
///         mean what they mean everywhere else: <c>Start</c> is the leading edge in the current reading
///         direction. Keeping a numeric date reading left-to-right inside a right-to-left screen is done
///         in the STRING, not by pinning the view's direction — see <see cref="EmbedLeftToRight" />.
///     </para>
/// </summary>
public partial class G9CultureDateTimeLabel : Label
{
    /// <summary>
    ///     Unicode LEFT-TO-RIGHT EMBEDDING / POP DIRECTIONAL FORMATTING. Wrapping the formatted value in
    ///     these keeps the whole numeric run left-to-right inside a right-to-left paragraph, which is what
    ///     the label used to buy by pinning its own <see cref="VisualElement.FlowDirection" /> — see
    ///     <see cref="EmbedLeftToRight" /> for why that pin had to go. Embedding (U+202A/U+202C) rather
    ///     than the newer isolates (U+2066/U+2069): the control is the whole paragraph, so isolation buys
    ///     nothing over embedding here, and embedding has been implemented by every bidi engine since
    ///     Unicode 2.0.
    /// </summary>
    private const string LeftToRightEmbedding = "\u202A";

    private const string PopDirectionalFormatting = "\u202C";

    private static readonly PersianCalendar PersianCalendar = new();

    private bool _isCultureChangedAttached;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.OneWay), OnChanged = nameof(OnDateTimeDisplayChanged))]
    private DateTime? _dateTimeValue;

    [AutoBindable(OnChanged = nameof(OnDateTimeDisplayChanged))]
    private G9CultureDateTimeDisplayMode _displayMode;

    [AutoBindable(OnChanged = nameof(OnDateTimeDisplayChanged))]
    private string? _emptyText;

    public G9CultureDateTimeLabel()
    {
        DisplayMode = G9CultureDateTimeDisplayMode.DateTime;
        EmptyText = string.Empty;

        // G9Culture.CultureChanged is STATIC, so a subscription roots this label — and through its
        // parent chain the whole page. It used to be held from "handler connected" to "handler
        // disconnected", but a page popped off the stack, a recycled list cell or a closed sheet
        // keeps its handlers long after it left the screen. The subscription now follows
        // Loaded / Unloaded; the handler-null path stays as the last line of defence.
        Loaded += OnLabelLoaded;
        Unloaded += OnLabelUnloaded;

        UpdateText();
    }

    private void OnLabelLoaded(object? sender, EventArgs e)
    {
        AttachCultureChanged();
        // Re-sync: a language switch that happened while the label was detached was not heard.
        UpdateText();
    }

    private void OnLabelUnloaded(object? sender, EventArgs e)
    {
        DetachCultureChanged();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null)
        {
            return;
        }

        // A handler re-created while the label is already in the live tree raises no new Loaded.
        if (IsLoaded)
        {
            AttachCultureChanged();
        }

        UpdateText();
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.NewHandler is null)
        {
            DetachCultureChanged();
        }

        base.OnHandlerChanging(args);
    }

    private void AttachCultureChanged()
    {
        if (_isCultureChangedAttached)
        {
            return;
        }

        G9Culture.CultureChanged += OnCultureChanged;
        _isCultureChangedAttached = true;
    }

    private void DetachCultureChanged()
    {
        if (!_isCultureChangedAttached)
        {
            return;
        }

        G9Culture.CultureChanged -= OnCultureChanged;
        _isCultureChangedAttached = false;
    }

    private void OnCultureChanged(object? sender, G9CultureEventArgs e)
    {
        UpdateText();
    }

    private void OnDateTimeDisplayChanged()
    {
        UpdateText();
    }

    private void UpdateText()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(UpdateText);
            return;
        }

        Text = DateTimeValue.HasValue
            ? EmbedLeftToRight(Format(DateTimeValue.Value, DisplayMode, G9Culture.CurrentCulture))
            : EmptyText ?? string.Empty;
    }

    /// <summary>
    ///     Keeps an absolute (numeric) value reading left-to-right under a right-to-left culture, by
    ///     wrapping it in an LTR embedding instead of by pinning the label's own
    ///     <see cref="VisualElement.FlowDirection" />.
    ///     <para>
    ///         <b>The pin was a defect, and the reason is worth keeping.</b> The label used to set
    ///         <c>FlowDirection = LeftToRight</c> for every absolute mode. That is a paragraph-direction
    ///         switch, so it fixed the ORDER of <c>1403/05/24 - 14:30</c> — and silently took the
    ///         label's ALIGNMENT with it, because <see cref="Label.HorizontalTextAlignment" /> and
    ///         <see cref="View.HorizontalOptions" /> resolve <c>Start</c>/<c>End</c> against the
    ///         view's own effective flow direction. A consumer asking for <c>Start</c> got the physical
    ///         LEFT edge in Persian too, so a date could not be aligned with the plain
    ///         <see cref="Label" /> beside it in both languages: whichever alignment was written, one of
    ///         the two languages was wrong. Every consumer paid for a decision about glyph ORDER.
    ///     </para>
    ///     <para>
    ///         The embedding expresses exactly the original intent — <i>this run of text is
    ///         left-to-right</i> — at the level it belongs to, the string, and leaves the label an
    ///         ordinary <see cref="Label" /> for layout: it inherits the parent's direction and its
    ///         logical alignment mirrors with the rest of the screen.
    ///     </para>
    ///     <para>
    ///         <b>Relative mode is deliberately untouched.</b> Its phrase is localized WORDS, which must
    ///         read in the culture's own direction; embedding them left-to-right is the bug this method
    ///         exists to prevent, inverted. Under an LTR culture the value is returned unchanged, so no
    ///         invisible character ever enters a string an LTR app might log, export or compare.
    ///     </para>
    /// </summary>
    private string EmbedLeftToRight(string value)
    {
        return DisplayMode == G9CultureDateTimeDisplayMode.Relative ||
               string.IsNullOrEmpty(value) ||
               !G9Culture.IsRtl
            ? value
            : LeftToRightEmbedding + value + PopDirectionalFormatting;
    }

    private static string Format(DateTime value, G9CultureDateTimeDisplayMode displayMode, CultureInfo culture)
    {
        if (displayMode == G9CultureDateTimeDisplayMode.Relative)
        {
            return G9RelativeTimeFormatter.FormatAgoWithTime(value, culture);
        }

        // Language decides the calendar (fa → Jalali); the rule and the range guard are shared
        // with G9DateTimePicker through G9Calendar so the three cannot drift apart again.
        return G9Calendar.IsPersianLanguage(culture)
            ? FormatPersian(value, displayMode, culture)
            : FormatGregorian(value, displayMode, culture);
    }

    private static string FormatPersian(DateTime value, G9CultureDateTimeDisplayMode displayMode, CultureInfo culture)
    {
        if (!G9Calendar.IsSupportedByPersianCalendar(value))
        {
            return FormatGregorian(value, displayMode, culture);
        }

        return displayMode switch
        {
            G9CultureDateTimeDisplayMode.Date => FormatPersianDate(value, culture),
            G9CultureDateTimeDisplayMode.Time => FormatTime(value, culture),
            _ => string.Format(
                culture,
                "{0} - {1}",
                FormatPersianDate(value, culture),
                FormatTime(value, culture))
        };
    }

    private static string FormatPersianDate(DateTime value, CultureInfo culture)
    {
        return string.Format(
            culture,
            "{0:0000}/{1:00}/{2:00}",
            PersianCalendar.GetYear(value),
            PersianCalendar.GetMonth(value),
            PersianCalendar.GetDayOfMonth(value));
    }

    private static string FormatGregorian(DateTime value, G9CultureDateTimeDisplayMode displayMode, CultureInfo culture)
    {
        // ToString formats in the CULTURE's calendar. Under fa-IR that is the Persian one, so this
        // method — the fallback for a date the Persian calendar cannot represent — threw the same
        // ArgumentOutOfRangeException it was reached to avoid. Swap in a Gregorian-calendar culture.
        culture = G9Calendar.GetGregorianFormatCulture(culture);

        return displayMode switch
        {
            G9CultureDateTimeDisplayMode.Date => value.ToString("yyyy/MM/dd", culture),
            G9CultureDateTimeDisplayMode.Time => FormatTime(value, culture),
            _ => string.Format(
                culture,
                "{0} - {1}",
                value.ToString("yyyy/MM/dd", culture),
                FormatTime(value, culture))
        };
    }

    private static string FormatTime(DateTime value, CultureInfo culture)
    {
        return value.ToString("HH:mm", culture);
    }
}
