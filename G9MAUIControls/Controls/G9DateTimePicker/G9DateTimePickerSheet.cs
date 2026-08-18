using G9MAUIControls.BottomSheet;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using System.Globalization;
using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

/// <summary>
///     Bottom-sheet that hosts the drum columns for <see cref="G9DateTimePicker" />.
///     Uses the Persian calendar in RTL mode and the Gregorian calendar in LTR mode.
///     The header always shows a live preview of the currently-selected date/time so the
///     user sees the value update as they spin the columns.
/// </summary>
internal sealed class G9DateTimePickerSheet : Grid, IG9BottomSheetAwareView
{
    private static readonly PersianCalendar PersianCalendar = new();
    private static readonly string[] PersianMonths =
    [
        "فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور",
        "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"
    ];

    private readonly G9DateTimePickerMode _mode;
    private readonly DateTime? _minDate;
    private readonly DateTime? _maxDate;
    private readonly bool _twentyFourHour;
    private readonly bool _isPersian;
    private readonly G9DateTimePicker? _owner;
    private readonly Label _previewLabel;
    private readonly G9DrumColumn? _dayColumn;
    private readonly G9DrumColumn? _monthColumn;
    private readonly G9DrumColumn? _yearColumn;
    private readonly G9DrumColumn? _hourColumn;
    private readonly G9DrumColumn? _minuteColumn;
    private DateTime _selected;
    private bool _completed;
    private bool _suspendApply;
    /// <summary>
    ///     Caches the day count the day column is currently populated for. The day column
    ///     only needs a rebuild when the day count actually changes (e.g. switching from
    ///     a 30-day month to a 31-day month, or to/from leap-Feb). For year-only changes
    ///     within the same month — which is the common Year drum gesture — the day count
    ///     stays the same and the labels (always "01".."31") don't depend on year, so
    ///     rebuilding 31 row Views is pure waste. The previous cache key of (year, month)
    ///     invalidated on any year change and forced ~500-1100ms rebuilds on the UI
    ///     thread inside the SelectedValueChanged handler, blocking the user's next swipe.
    /// </summary>
    private int _dayColumnBuiltForDayCount;
    private readonly bool _showTodayButton;

    public G9DateTimePickerSheet(
        string title,
        DateTime? selected,
        G9DateTimePickerMode mode,
        DateTime? minDate,
        DateTime? maxDate,
        bool twentyFourHour,
        G9DateTimePicker? owner,
        bool showTodayButton = true)
    {
        _mode = mode;
        _minDate = minDate;
        _maxDate = maxDate;
        _twentyFourHour = twentyFourHour;
        _isPersian = G9Culture.IsRtl;
        _owner = owner;
        _showTodayButton = showTodayButton;
        _selected = Clamp(selected ?? DateTime.Now);

        RowDefinitions =
        [
            new RowDefinition(GridLength.Auto),
            new RowDefinition(GridLength.Auto),
            new RowDefinition(GridLength.Auto),
            new RowDefinition(GridLength.Auto)
        ];
        BackgroundColor = G9Palette.Current.Surface;
        Padding = new Thickness(0, 0, 0, 10);
        FlowDirection = _isPersian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        var header = CreateHeader(title);
        Grid.SetRow(header, 0);
        Children.Add(header);

        _previewLabel = new Label
        {
            FontSize = 13,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            TextColor = G9Palette.Current.Primary,
            Padding = new Thickness(16, 4, 16, 8)
        };
        Grid.SetRow(_previewLabel, 1);
        Children.Add(_previewLabel);

        // Today/Now button — compact modern pill chip with icon, centered.
        if (_showTodayButton)
        {
            var todayChip = CreateTodayChip();
            Grid.SetRow(todayChip, 2);
            Children.Add(todayChip);
        }

        var columns = new Grid
        {
            HeightRequest = G9Metrics.DrumColumnHeight + 30,
            Padding = new Thickness(8, 0),
            ColumnSpacing = 0
        };

        if (mode is G9DateTimePickerMode.Date or G9DateTimePickerMode.DateTime)
        {
            columns.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            columns.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(1.7, GridUnitType.Star)));
            columns.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            _dayColumn = new G9DrumColumn(G9Strings.Get(G9StringKey.Day));
            _monthColumn = new G9DrumColumn(G9Strings.Get(G9StringKey.Month));
            _yearColumn = new G9DrumColumn(G9Strings.Get(G9StringKey.Year));

            columns.Add(_dayColumn, 0);
            columns.Add(_monthColumn, 1);
            columns.Add(_yearColumn, 2);

            _dayColumn.SelectedValueChanged += OnColumnChanged;
            _monthColumn.SelectedValueChanged += OnColumnChanged;
            _yearColumn.SelectedValueChanged += OnColumnChanged;
        }

        if (mode is G9DateTimePickerMode.Time or G9DateTimePickerMode.DateTime)
        {
            var offset = columns.ColumnDefinitions.Count;
            columns.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            columns.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

            _hourColumn = new G9DrumColumn(G9Strings.Get(G9StringKey.Hour));
            _minuteColumn = new G9DrumColumn(G9Strings.Get(G9StringKey.Minute));
            columns.Add(_hourColumn, offset);
            columns.Add(_minuteColumn, offset + 1);

            _hourColumn.SelectedValueChanged += OnColumnChanged;
            _minuteColumn.SelectedValueChanged += OnColumnChanged;
        }

        Grid.SetRow(columns, 3);
        Children.Add(columns);
        RebuildColumns();
        UpdatePreview();
    }

    public IG9BottomSheetHandle G9BottomSheetHandle { get; set; } = G9BottomSheetHelper.InitG9BottomSheet();
    public event EventHandler<DateTime?>? Completed;

    public void CompleteFromClose() => Complete(null);

    public static Task<DateTime?> ShowAsync(
        string title,
        DateTime? selected,
        G9DateTimePickerMode mode,
        DateTime? minDate,
        DateTime? maxDate,
        bool twentyFourHour,
        G9DateTimePicker? owner,
        bool showTodayButton = true)
    {
        var tcs = new TaskCompletionSource<DateTime?>(TaskCreationOptions.RunContinuationsAsynchronously);
        G9DateTimePickerSheet? sheet = null;

        // Use the factory + DeferContent path. The sheet constructor builds 4-5
        // drum columns each with up to ~100 rows (year column = ±50 years), every row
        // a Label + ContentView with bindings — that's hundreds of view-tree
        // allocations. Synchronous construction before the open animation produced a
        // perceptible 1-3s lag on tap. With DeferContent=true the sheet host is laid
        // out FIRST with a centered spinner, the open animation plays, and only then
        // is the heavy view tree built — by that point the user is already looking at
        // the sheet so the build cost is masked. Measured on emulator: tap →
        // ShowAsync 64ms → factory invoked +480ms (after open animation) → factory
        // body 336ms → total perceived 816ms vs the previous synchronous 1-3s lag.
        G9BottomSheetHelper.ShowG9BottomSheet(
            () =>
            {
                sheet = new G9DateTimePickerSheet(title, selected, mode, minDate, maxDate, twentyFourHour, owner, showTodayButton);
                sheet.Completed += (_, value) => tcs.TrySetResult(value);
                return sheet;
            },
            G9BottomSheetOptions.FitToContentOptions() with
            {
                DeferContent = true,
                // Drum columns realize row-by-row on first paint; crossfade the built tree in
                // (spinner held until laid out) so it appears as one unit, not piecemeal.
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

    private void RebuildColumns()
    {
        _suspendApply = true;
        try
        {
            if (_dayColumn is not null && _monthColumn is not null && _yearColumn is not null)
            {
                var (year, month, day) = GetDateParts(_selected);
                _yearColumn.SetItems(BuildYears(year), year);
                _monthColumn.SetItems(BuildMonths(), month);
                _dayColumn.SetItems(BuildDays(year, month), day);
                _dayColumnBuiltForDayCount = GetDaysInMonth(year, month);
            }

            if (_hourColumn is not null && _minuteColumn is not null)
            {
                _hourColumn.SetItems(BuildHours(), _selected.Hour);
                _minuteColumn.SetItems(BuildMinutes(), _selected.Minute);
            }
        }
        finally
        {
            _suspendApply = false;
        }
    }

    private void OnColumnChanged(object? sender, int e) => ApplySelectionFromColumns();

    private void ApplySelectionFromColumns()
    {
        if (_suspendApply) return;

        var hour = _hourColumn?.SelectedValue ?? _selected.Hour;
        var minute = _minuteColumn?.SelectedValue ?? _selected.Minute;

        DateTime next;
        if (_dayColumn is not null && _monthColumn is not null && _yearColumn is not null)
        {
            var year = _yearColumn.SelectedValue;
            var month = _monthColumn.SelectedValue;
            var day = Math.Min(_dayColumn.SelectedValue, GetDaysInMonth(year, month));
            next = CreateDate(year, month, day, hour, minute);

            // Adjust the day column ONLY when the day count actually changes (e.g.
            // March 31 → April 30, or to/from leap-Feb). Year-only changes within the
            // same month leave the count untouched and the day labels are just "01".."31"
            // with no year/month dependency, so an adjustment would be pure waste.
            //
            // When the count DOES change, we use TrimOrExtendItems instead of a full
            // SetItems rebuild. SetItems destroys all 30-31 row Views and runs a full
            // measure/arrange pass (~500-950ms on Android). TrimOrExtendItems just
            // adds or removes 1-3 rows at the end, keeping the rest of the view tree
            // intact — typically <5ms. Without this, every Month swipe that crossed a
            // day-count boundary (Jan→Feb, Mar→Apr, etc., which is most of them)
            // blocked the UI thread for ~700ms inside the SelectedValueChanged
            // handler and silently swallowed the user's next touch.
            var requiredDayCount = GetDaysInMonth(year, month);
            if (_dayColumnBuiltForDayCount != requiredDayCount)
            {
                _suspendApply = true;
                try
                {
                    var culture = G9Culture.CurrentCulture;
                    _dayColumn.TrimOrExtendItems(requiredDayCount, day, idx => new G9DrumItem
                    {
                        Value = idx + 1,
                        Text = (idx + 1).ToString("00", culture)
                    });
                    _dayColumnBuiltForDayCount = requiredDayCount;
                }
                finally
                {
                    _suspendApply = false;
                }
            }
        }
        else
        {
            next = new DateTime(_selected.Year, _selected.Month, _selected.Day, hour, minute, 0);
        }

        _selected = Clamp(next);
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        _previewLabel.Text = _owner?.FormatValue(_selected) ?? FormatFallback(_selected);
    }

    private string FormatFallback(DateTime value)
    {
        var culture = G9Culture.CurrentCulture;
        if (_isPersian)
        {
            var day = PersianCalendar.GetDayOfMonth(value).ToString("00", culture);
            var month = PersianMonths[PersianCalendar.GetMonth(value) - 1];
            var year = PersianCalendar.GetYear(value).ToString("0000", culture);
            var time = value.ToString(_twentyFourHour ? "HH:mm" : "hh:mm tt", culture);
            return _mode switch
            {
                G9DateTimePickerMode.Time => time,
                G9DateTimePickerMode.DateTime => $"{day} {month} {year}، {time}",
                _ => $"{day} {month} {year}"
            };
        }

        return _mode switch
        {
            G9DateTimePickerMode.Time => value.ToString(_twentyFourHour ? "HH:mm" : "hh:mm tt", culture),
            G9DateTimePickerMode.DateTime => value.ToString("d MMM yyyy, HH:mm", culture),
            _ => value.ToString("d MMM yyyy", culture)
        };
    }

    private (int Year, int Month, int Day) GetDateParts(DateTime date)
    {
        if (!_isPersian) return (date.Year, date.Month, date.Day);

        return (
            PersianCalendar.GetYear(date),
            PersianCalendar.GetMonth(date),
            PersianCalendar.GetDayOfMonth(date));
    }

    private IEnumerable<G9DrumItem> BuildYears(int selectedYear)
    {
        var minYear = _minDate.HasValue ? GetDateParts(_minDate.Value).Year : selectedYear - 50;
        var maxYear = _maxDate.HasValue ? GetDateParts(_maxDate.Value).Year : selectedYear + 50;

        for (var year = minYear; year <= maxYear; year++)
        {
            yield return new G9DrumItem
            {
                Value = year,
                Text = year.ToString("0000", G9Culture.CurrentCulture)
            };
        }
    }

    private IEnumerable<G9DrumItem> BuildMonths()
    {
        var culture = G9Culture.CurrentCulture;
        for (var month = 1; month <= 12; month++)
        {
            yield return new G9DrumItem
            {
                Value = month,
                Text = _isPersian
                    ? PersianMonths[month - 1]
                    : culture.DateTimeFormat.GetMonthName(month)
            };
        }
    }

    private IEnumerable<G9DrumItem> BuildDays(int year, int month)
    {
        var days = GetDaysInMonth(year, month);
        for (var day = 1; day <= days; day++)
        {
            yield return new G9DrumItem
            {
                Value = day,
                Text = day.ToString("00", G9Culture.CurrentCulture)
            };
        }
    }

    private IEnumerable<G9DrumItem> BuildHours()
    {
        for (var hour = 0; hour <= 23; hour++)
        {
            yield return new G9DrumItem
            {
                Value = hour,
                Text = _twentyFourHour
                    ? hour.ToString("00", G9Culture.CurrentCulture)
                    : DateTime.Today.AddHours(hour).ToString("hh tt", G9Culture.CurrentCulture)
            };
        }
    }

    private IEnumerable<G9DrumItem> BuildMinutes()
    {
        for (var minute = 0; minute < 60; minute++)
        {
            yield return new G9DrumItem
            {
                Value = minute,
                Text = minute.ToString("00", G9Culture.CurrentCulture)
            };
        }
    }

    private int GetDaysInMonth(int year, int month)
    {
        return _isPersian
            ? PersianCalendar.GetDaysInMonth(year, month)
            : DateTime.DaysInMonth(year, month);
    }

    private DateTime CreateDate(int year, int month, int day, int hour, int minute)
    {
        return _isPersian
            ? PersianCalendar.ToDateTime(year, month, day, hour, minute, 0, 0)
            : new DateTime(year, month, day, hour, minute, 0);
    }

    private DateTime Clamp(DateTime value)
    {
        if (_minDate.HasValue && value < _minDate.Value) value = _minDate.Value;
        if (_maxDate.HasValue && value > _maxDate.Value) value = _maxDate.Value;
        return value;
    }

    private void Complete(DateTime? value)
    {
        if (_completed) return;
        _completed = true;
        Completed?.Invoke(this, value);
    }

    private void SetToNow()
    {
        _ = SetToNowAsync();
    }

    /// <summary>
    ///     Smoothly transitions every drum column to the current date/time. Each column
    ///     animates in parallel using the slower <see cref="G9DrumColumn.RollDurationMs"/>
    ///     glide so the user can SEE the rolling motion (the post-drag snap is faster
    ///     because it commits a gesture; the Today button is a transition the user wants
    ///     to watch). Day count is incrementally adjusted via TrimOrExtendItems if it
    ///     changes — works the same in Gregorian and Persian (Shamsi) calendars.
    /// </summary>
    private async Task SetToNowAsync()
    {
        var now = Clamp(DateTime.Now);
        _selected = now;

        var (year, month, day) = GetDateParts(now);

        _suspendApply = true;
        try
        {
            // The year column was built once at sheet open with a window around the
            // initially-selected year (or constrained by min/max date if set). If the
            // user navigated outside that window AND today's year falls outside the
            // current column range, AnimateToValue would silently no-op. Rebuild the
            // year column to cover the new year — this is rare so the cost is fine.
            if (_yearColumn is not null && !YearInRange(year))
            {
                _yearColumn.SetItems(BuildYears(year), year);
            }

            // Day-count adjustment (Persian leap-Esfand, Gregorian leap-Feb, 30/31
            // alternating months) — same logic as ApplySelectionFromColumns.
            if (_dayColumn is not null)
            {
                var requiredDays = GetDaysInMonth(year, month);
                if (_dayColumnBuiltForDayCount != requiredDays)
                {
                    var culture = G9Culture.CurrentCulture;
                    _dayColumn.TrimOrExtendItems(requiredDays, day, idx => new G9DrumItem
                    {
                        Value = idx + 1,
                        Text = (idx + 1).ToString("00", culture)
                    });
                    _dayColumnBuiltForDayCount = requiredDays;
                }
            }
        }
        finally
        {
            _suspendApply = false;
        }

        // Animate columns in parallel using the slower roll duration so the user sees
        // the columns transition into the new values instead of snapping instantly.
        var tasks = new List<Task>();
        if (_yearColumn is not null) tasks.Add(_yearColumn.AnimateToValue(year));
        if (_monthColumn is not null) tasks.Add(_monthColumn.AnimateToValue(month));
        if (_dayColumn is not null) tasks.Add(_dayColumn.AnimateToValue(day));
        if (_hourColumn is not null) tasks.Add(_hourColumn.AnimateToValue(now.Hour));
        if (_minuteColumn is not null) tasks.Add(_minuteColumn.AnimateToValue(now.Minute));

        UpdatePreview();

        try { await Task.WhenAll(tasks).ConfigureAwait(true); }
        catch { }
    }

    private bool YearInRange(int year)
    {
        if (_yearColumn is null) return true;
        // The year column's items are built linearly from min to max. Use the items'
        // selected-value range as the "in-range" check by walking the column. We don't
        // expose the full list, so use SelectedValue as the proxy plus the count.
        // Simpler: try AnimateToValue and let it no-op silently is risky; explicitly
        // check by attempting a lookup. The cheapest path is to recompute the bounds
        // we'd have used in BuildYears.
        var selectedYear = GetDateParts(_selected).Year;
        var minYear = _minDate.HasValue ? GetDateParts(_minDate.Value).Year : selectedYear - 50;
        var maxYear = _maxDate.HasValue ? GetDateParts(_maxDate.Value).Year : selectedYear + 50;
        return year >= minYear && year <= maxYear;
    }

    private View CreateTodayChip()
    {
        var palette = G9Palette.Current;
        var text = _mode == G9DateTimePickerMode.Time ? G9Strings.Get(G9StringKey.Now) : G9Strings.Get(G9StringKey.Today);

        var icon = new G9IconView {
            Icon = G9Glyphs.CalendarToday,
            Size = 14,
            Color = palette.Primary,
            VerticalOptions = LayoutOptions.Center
        };

        var label = new Label
        {
            Text = text,
            FontSize = 12,
            FontAttributes = FontAttributes.Bold,
            TextColor = palette.Primary,
            VerticalTextAlignment = TextAlignment.Center
        };

        var row = new HorizontalStackLayout
        {
            Spacing = 6,
            Padding = new Thickness(12, 6),
            Children = { icon, label }
        };

        var chip = new Border
        {
            StrokeThickness = 1,
            Stroke = new SolidColorBrush(Color.FromRgba(palette.Primary.Red, palette.Primary.Green, palette.Primary.Blue, 0.30f)),
            StrokeShape = G9Colors.Round(16),
            BackgroundColor = Color.FromRgba(palette.Primary.Red, palette.Primary.Green, palette.Primary.Blue, 0.08f),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 2, 0, 6),
            Content = row
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            // Quick scale pulse for tactile feedback.
            try
            {
                await chip.ScaleToAsync(0.94, 60, Easing.CubicOut);
                await chip.ScaleToAsync(1.0, 80, Easing.CubicOut);
            }
            catch { }
            SetToNow();
        };
        chip.GestureRecognizers.Add(tap);

        return chip;
    }
}
