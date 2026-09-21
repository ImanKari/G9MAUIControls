using G9MAUIControls.BottomSheet;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;

namespace G9MAUIControls.Controls;

internal sealed class G9TimeSpanPickerSheet : Grid, IG9BottomSheetAwareView
{
    /// <summary>Last row of the years drum.</summary>
    private const int MaxYears = 100;

    /// <summary>Last row of the days drum unless the opening value needs more (see the ctor).</summary>
    private const int DefaultMaxDays = 30;

    private readonly G9TimeSpanPickerMode _mode;
    private readonly G9TimeSpanPicker? _owner;
    private TimeSpan _selected;
    private bool _completed;
    private bool _suspendApply;

    private readonly G9DrumColumn? _yearsColumn;
    private readonly G9DrumColumn? _monthsColumn;
    private readonly G9DrumColumn? _daysColumn;

    public G9TimeSpanPickerSheet(
        string title,
        TimeSpan? selected,
        G9TimeSpanPickerMode mode,
        G9TimeSpanPicker? owner)
    {
        _mode = mode;
        _owner = owner;
        _selected = selected ?? TimeSpan.Zero;

        RowDefinitions =
        [
            new RowDefinition(GridLength.Auto),
            new RowDefinition(GridLength.Auto)
        ];
        BackgroundColor = G9Palette.Current.Surface;
        Padding = new Thickness(0, 0, 0, 10);

        var header = CreateHeader(title);
        Grid.SetRow(header, 0);
        Children.Add(header);

        var showDays = mode >= G9TimeSpanPickerMode.YearsMonthsDays;

        // ONE decomposition for the drums, the field text and the returned value (G9TimeSpanMath).
        // The sheet used to compute months from `Days % 365 / 30` but days from `Days % 30`, so 370
        // days opened as "1 year 10 days" and a Done without touching anything re-saved a different
        // number than the one on screen.
        var (totalYears, remainingMonths, remainingDays) = G9TimeSpanMath.Decompose(_selected.Days);

        // A value past the years drum would select a row that does not exist (the drum then shows
        // nothing selected and reports the out-of-range number). Pin it to the last row instead.
        totalYears = Math.Min(totalYears, MaxYears);
        if (!showDays) remainingDays = 0;

        // What Done returns must be what the drums show — including when the user changes nothing.
        _selected = ComposeSelection(totalYears, remainingMonths, remainingDays);

        // Years AND months always exist; days is the optional third drum. This used to be
        // `1 (+1 if days)`, one definition short, so the last two drums were stacked in one cell.
        var columnCount = showDays ? 3 : 2;

        var columns = new Grid
        {
            HeightRequest = G9Metrics.DrumColumnHeight + 30,
            Padding = new Thickness(8, 0),
            ColumnSpacing = 0
        };

        for (var i = 0; i < columnCount; i++)
            columns.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        var colIndex = 0;

        _yearsColumn = new G9DrumColumn(G9Strings.Get(G9StringKey.Year));
        _monthsColumn = new G9DrumColumn(G9Strings.Get(G9StringKey.Month));

        _suspendApply = true;
        _yearsColumn.SetItems(BuildYears(), totalYears);
        _monthsColumn.SetItems(BuildMonths(), remainingMonths);
        _yearsColumn.SelectedValueChanged += OnColumnChanged;
        _monthsColumn.SelectedValueChanged += OnColumnChanged;
        columns.Add(_yearsColumn, colIndex++);
        columns.Add(_monthsColumn, colIndex++);

        if (showDays)
        {
            _daysColumn = new G9DrumColumn(G9Strings.Get(G9StringKey.Day));
            // The last five days of a 365-day year decompose to 11 months + 30..34 days (see
            // G9TimeSpanMath.Decompose); grow the drum just enough to hold such a value.
            _daysColumn.SetItems(BuildDays(Math.Max(DefaultMaxDays, remainingDays)), remainingDays);
            _daysColumn.SelectedValueChanged += OnColumnChanged;
            columns.Add(_daysColumn, colIndex++);
        }

        _suspendApply = false;

        Grid.SetRow(columns, 1);
        Children.Add(columns);
    }

    public IG9BottomSheetHandle G9BottomSheetHandle { get; set; } = G9BottomSheetHelper.InitG9BottomSheet();
    public event EventHandler<TimeSpan?>? Completed;

    public void CompleteFromClose() => Complete(null);

    public static Task<TimeSpan?> ShowAsync(
        string title,
        TimeSpan? selected,
        G9TimeSpanPickerMode mode,
        G9TimeSpanPicker? owner)
    {
        var tcs = new TaskCompletionSource<TimeSpan?>(TaskCreationOptions.RunContinuationsAsynchronously);
        G9TimeSpanPickerSheet? sheet = null;

        G9BottomSheetHelper.ShowG9BottomSheet(
            () =>
            {
                sheet = new G9TimeSpanPickerSheet(title, selected, mode, owner);
                sheet.Completed += (_, value) => tcs.TrySetResult(value);
                return sheet;
            },
            G9BottomSheetOptions.FitToContentOptions() with
            {
                DeferContent = true,
                FadeDeferredContentIn = true,
                BackgroundColor = G9Palette.Current.Surface,
                ClosedCommand = new Command(() =>
                {
                    if (sheet is not null) sheet.CompleteFromClose();
                    else tcs.TrySetResult(null);
                })
            });

        return tcs.Task;
    }

    private Grid CreateHeader(string title)
    {
        var header = new Grid
        {
            Padding = new Thickness(16, 10),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };

        header.Add(new G9Button
        {
            Text = G9Strings.Get(G9StringKey.Cancel),
            Variant = G9ButtonVariant.Text,
            Size = G9ControlSize.Small,
            Command = new Command(() =>
            {
                Complete(null);
                G9BottomSheetHandle.Close();
            })
        }, 0);

        header.Add(new Label
        {
            Text = title,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = G9Palette.Current.TextPrimary,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        }, 1);

        header.Add(new G9Button
        {
            Text = G9Strings.Get(G9StringKey.Done),
            Variant = G9ButtonVariant.Primary,
            Size = G9ControlSize.Small,
            Command = new Command(() =>
            {
                Complete(_selected);
                G9BottomSheetHandle.Close();
            })
        }, 2);

        return header;
    }

    private void OnColumnChanged(object? sender, int e) => ApplySelectionFromColumns();

    private void ApplySelectionFromColumns()
    {
        if (_suspendApply) return;

        var years = _yearsColumn?.SelectedValue ?? 0;
        var months = _monthsColumn?.SelectedValue ?? 0;
        var days = _daysColumn?.SelectedValue ?? 0;

        _selected = ComposeSelection(years, months, days);
    }

    private static TimeSpan ComposeSelection(int years, int months, int days) =>
        new(G9TimeSpanMath.Compose(years, months, days), 0, 0, 0);

    private static IEnumerable<G9DrumItem> BuildYears()
    {
        for (var year = 0; year <= MaxYears; year++)
        {
            yield return new G9DrumItem
            {
                Value = year,
                Text = year.ToString("D2", G9Culture.CurrentCulture)
            };
        }
    }

    private static IEnumerable<G9DrumItem> BuildMonths()
    {
        for (var month = 0; month <= G9TimeSpanMath.MaxMonths; month++)
        {
            yield return new G9DrumItem
            {
                Value = month,
                Text = month.ToString("D2", G9Culture.CurrentCulture)
            };
        }
    }

    private static IEnumerable<G9DrumItem> BuildDays(int maxDay)
    {
        for (var day = 0; day <= maxDay; day++)
        {
            yield return new G9DrumItem
            {
                Value = day,
                Text = day.ToString("D2", G9Culture.CurrentCulture)
            };
        }
    }

    private void Complete(TimeSpan? value)
    {
        if (_completed) return;
        _completed = true;
        Completed?.Invoke(this, value);
    }
}
