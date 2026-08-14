using G9MAUIControls.BottomSheet;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;

namespace G9MAUIControls.Controls;

internal sealed class G9TimeSpanPickerSheet : Grid, IG9BottomSheetAwareView
{
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

        var totalYears = _selected.Days / 365;
        var remainingMonths = _selected.Days % 365 / 30;
        var remainingDays = _selected.Days % 30;

        var showDays = mode >= G9TimeSpanPickerMode.YearsMonthsDays;

        var columnCount = 1;
        if (showDays) columnCount++;

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
            _daysColumn.SetItems(BuildDays(), remainingDays);
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

        _selected = new TimeSpan(years * 365 + months * 30 + days, 0, 0, 0);
    }

    private static IEnumerable<G9DrumItem> BuildYears()
    {
        for (var year = 0; year <= 100; year++)
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
        for (var month = 0; month <= 11; month++)
        {
            yield return new G9DrumItem
            {
                Value = month,
                Text = month.ToString("D2", G9Culture.CurrentCulture)
            };
        }
    }

    private static IEnumerable<G9DrumItem> BuildDays()
    {
        for (var day = 0; day <= 30; day++)
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
