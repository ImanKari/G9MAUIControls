using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using System.Globalization;

using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

/// <summary>
///     Outlined trigger that opens a bottom-sheet drum picker for date / time / date-and-time.
///     Calendar (Gregorian / Shamsi) is selected automatically by the active culture.
///     Inherits from <see cref="G9OutlinedFieldBase" /> so the outline / floating label /
///     icon padding all match the other input controls.
///     // TODO (palette step): outline / icon colors are inherited from the base.
/// </summary>
public partial class G9DateTimePicker : G9OutlinedFieldBase
{
    private static readonly PersianCalendar PersianCalendar = new();
    private static readonly string[] PersianMonths =
    [
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    ];

    private readonly Label _valueLabel;
    private bool _isOpening;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnVisualChanged))]
    private DateTime? _selectedDateTime;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9DateTimePickerMode _mode;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9DateTimeDisplayFormat _displayFormat;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _formattedDisplayText;

    [AutoBindable] private DateTime? _minDate;
    [AutoBindable] private DateTime? _maxDate;
    [AutoBindable] private bool _twentyFourHourDisplay;
    [AutoBindable] private bool _restoreOnCancel;
    [AutoBindable] private bool _showTodayButton;

    public G9DateTimePicker()
    {
        _valueLabel = new Label
        {
            FontSize = 15,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

        InnerContentHost.Content = _valueLabel;

        TrailingIcon = G9Glyphs.Calendar;

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        // Add to Box (the inner grid) so the tap isn't blocked by the outer ContentView
        // hit-test on Android. The base's boxTap calls FocusTarget which is null for
        // pickers, so it's a no-op — but it still consumes the event before it reaches
        // the outer ContentView's gesture recognizers.
        Box.GestureRecognizers.Add(tap);

        Mode = G9DateTimePickerMode.Date;
        DisplayFormat = G9DateTimeDisplayFormat.ShortDate;
        RestoreOnCancel = true;
        ShowTodayButton = true;
    }

    public event EventHandler<DateTime?>? DateTimeSelected;

    protected override View BuildInnerContent() => _valueLabel;

    protected override bool IsValueFloated => SelectedDateTime.HasValue;

    private void OnVisualChanged() => RequestVisualUpdate();

    protected override void OnRefresh()
    {
        if (_valueLabel is null) return;

        var palette = G9Palette.Current;
        var hasValue = SelectedDateTime.HasValue;

        if (hasValue)
        {
            _valueLabel.Text = FormatValue(SelectedDateTime!.Value);
            _valueLabel.TextColor = palette.TextPrimary;
        }
        else if (string.IsNullOrWhiteSpace(Label))
        {
            _valueLabel.Text = Placeholder ?? string.Empty;
            _valueLabel.TextColor = palette.TextTertiary;
        }
        else
        {
            _valueLabel.Text = string.Empty;
        }

        _valueLabel.FontFamily = G9Culture.IsRtl ? G9Culture.RtlFontFamily : G9Culture.LtrFontFamily;
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled || IsReadOnly || _isOpening) return;

        _isOpening = true;
        try
        {
            this.Unfocus();

            var previous = SelectedDateTime;
            var result = await G9DateTimePickerSheet.ShowAsync(
                ResolveSheetTitle(), previous, Mode, MinDate, MaxDate, TwentyFourHourDisplay,
                this, ShowTodayButton).ConfigureAwait(true);

            if (result.HasValue)
            {
                SelectedDateTime = result.Value;
                DateTimeSelected?.Invoke(this, result.Value);
            }
            else if (RestoreOnCancel)
            {
                SelectedDateTime = previous;
            }
        }
        finally
        {
            _isOpening = false;
        }
    }

    private string ResolveSheetTitle()
    {
        if (!string.IsNullOrWhiteSpace(Label)) return Label!;

        return Mode switch
        {
            G9DateTimePickerMode.Time => G9Strings.Get(G9StringKey.SelectTime),
            G9DateTimePickerMode.DateTime => G9Strings.Get(G9StringKey.SelectDateTime),
            _ => G9Strings.Get(G9StringKey.SelectDate)
        };
    }

    internal string FormatValue(DateTime value)
    {
        if (DisplayFormat == G9DateTimeDisplayFormat.Custom && !string.IsNullOrWhiteSpace(FormattedDisplayText))
        {
            return FormattedDisplayText!;
        }

        return G9Culture.IsRtl ? FormatPersian(value) : FormatGregorian(value);
    }

    private string FormatGregorian(DateTime value)
    {
        var culture = G9Culture.CurrentCulture;
        return DisplayFormat switch
        {
            G9DateTimeDisplayFormat.LongDate => value.ToString("MMMM d, yyyy", culture),
            G9DateTimeDisplayFormat.TimeOnly => value.ToString(TwentyFourHourDisplay ? "HH:mm" : "hh:mm tt", culture),
            G9DateTimeDisplayFormat.ShortDateTime => value.ToString("d MMM yyyy, HH:mm", culture),
            G9DateTimeDisplayFormat.LongDateTime => value.ToString("MMMM d, yyyy, hh:mm tt", culture),
            _ => value.ToString("d MMM yyyy", culture)
        };
    }

    private string FormatPersian(DateTime value)
    {
        var culture = G9Culture.CurrentCulture;
        var day = PersianCalendar.GetDayOfMonth(value).ToString("00", culture);
        var month = PersianMonths[PersianCalendar.GetMonth(value) - 1];
        var year = PersianCalendar.GetYear(value).ToString("0000", culture);
        var time = value.ToString(TwentyFourHourDisplay ? "HH:mm" : "hh:mm tt", culture);

        return DisplayFormat switch
        {
            G9DateTimeDisplayFormat.TimeOnly => time,
            G9DateTimeDisplayFormat.ShortDateTime or G9DateTimeDisplayFormat.LongDateTime => $"{day} {month} {year}، {time}",
            _ => $"{day} {month} {year}"
        };
    }
}
