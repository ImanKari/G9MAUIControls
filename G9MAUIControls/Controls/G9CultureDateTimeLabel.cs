using G9MAUIControls.Controls;
using G9MAUIControls.Helpers;
using G9MAUIControls.Localization;
using Maui.BindableProperty.Generator.Core;
using System.Globalization;

namespace G9MAUIControls.Controls;

public partial class G9CultureDateTimeLabel : Label
{
    /// <summary>
    ///     The language this label formats with the Persian (Jalali) calendar. It is a constant
    ///     rather than a comparison against the ambient culture: the label's whole purpose is to
    ///     render Persian dates in the Persian calendar even when the thread culture is something
    ///     else, so "is the current culture Persian" is the wrong question to ask.
    /// </summary>
    private const string PersianLanguageCode = "fa";
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
        FlowDirection = FlowDirection.LeftToRight;

        UpdateText();
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null)
        {
            return;
        }

        AttachCultureChanged();
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

        // The absolute modes render numeric dates/times that read left-to-right; the Relative mode's
        // phrase contains localized words that must follow the culture's flow direction (RTL under fa).
        FlowDirection = DisplayMode == G9CultureDateTimeDisplayMode.Relative
            ? FlowDirection.MatchParent
            : FlowDirection.LeftToRight;

        Text = DateTimeValue.HasValue
            ? Format(DateTimeValue.Value, DisplayMode, G9Culture.CurrentCulture)
            : EmptyText ?? string.Empty;
    }

    private static string Format(DateTime value, G9CultureDateTimeDisplayMode displayMode, CultureInfo culture)
    {
        if (displayMode == G9CultureDateTimeDisplayMode.Relative)
        {
            return G9RelativeTimeFormatter.FormatAgoWithTime(value, culture);
        }

        return IsPersianCulture(culture)
            ? FormatPersian(value, displayMode, culture)
            : FormatGregorian(value, displayMode, culture);
    }

    private static bool IsPersianCulture(CultureInfo culture)
    {
        return culture.TwoLetterISOLanguageName.Equals(
            PersianLanguageCode,
            StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPersian(DateTime value, G9CultureDateTimeDisplayMode displayMode, CultureInfo culture)
    {
        if (value < PersianCalendar.MinSupportedDateTime || value > PersianCalendar.MaxSupportedDateTime)
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
