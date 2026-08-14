using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

public enum G9TimeSpanPickerMode
{
    YearsMonths,
    YearsMonthsDays,
    YearsMonthsDaysHours,
    YearsMonthsDaysHoursMinutes,
    YearsMonthsDaysHoursMinutesSeconds
}

public partial class G9TimeSpanPicker : G9OutlinedFieldBase
{
    private readonly Label _valueLabel;
    private bool _isOpening;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnVisualChanged))]
    private TimeSpan? _selectedTimeSpan;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))]
    private G9TimeSpanPickerMode _mode = G9TimeSpanPickerMode.YearsMonthsDays;

    public G9TimeSpanPicker()
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
        Box.GestureRecognizers.Add(tap);
    }

    public event EventHandler<TimeSpan?>? TimeSpanSelected;

    protected override View BuildInnerContent() => _valueLabel;

    protected override bool IsValueFloated => SelectedTimeSpan.HasValue;

    private void OnVisualChanged() => RequestVisualUpdate();

    protected override void OnRefresh()
    {
        if (_valueLabel is null) return;

        var palette = G9Palette.Current;
        var hasValue = SelectedTimeSpan.HasValue;

        if (hasValue)
        {
            _valueLabel.Text = FormatValue(SelectedTimeSpan!.Value);
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

            var previous = SelectedTimeSpan;
            var result = await G9TimeSpanPickerSheet.ShowAsync(
                ResolveSheetTitle(), previous, Mode, this).ConfigureAwait(true);

            if (result.HasValue)
            {
                SelectedTimeSpan = result.Value;
                TimeSpanSelected?.Invoke(this, result.Value);
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
        return G9Strings.Get(G9StringKey.SelectDuration);
    }

    internal string FormatValue(TimeSpan value)
    {
        var parts = new System.Collections.Generic.List<string>();
        var culture = G9Culture.CurrentCulture;

        if (value.Days / 365 > 0)
            parts.Add(string.Format(culture, G9Strings.Get(G9StringKey.TimeSpanYearsFormat), value.Days / 365));
        if (value.Days % 365 / 30 > 0 || (value.Days / 365 == 0 && value.Days % 365 / 30 > 0))
            parts.Add(string.Format(culture, G9Strings.Get(G9StringKey.TimeSpanMonthsFormat), value.Days % 365 / 30));
        if (Mode >= G9TimeSpanPickerMode.YearsMonthsDays && value.Days % 30 > 0)
            parts.Add(string.Format(culture, G9Strings.Get(G9StringKey.TimeSpanDaysFormat), value.Days % 30));

        if (parts.Count == 0)
            return string.Format(culture, G9Strings.Get(G9StringKey.TimeSpanDaysFormat), 0);

        return string.Join(" ", parts);
    }
}
