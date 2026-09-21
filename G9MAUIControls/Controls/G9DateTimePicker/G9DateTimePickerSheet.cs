using G9MAUIControls.BottomSheet;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using System.Globalization;
using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

/// <summary>
///     Bottom-sheet that hosts the drum columns for <see cref="G9DateTimePicker" />.
///     Uses the Persian calendar when the active LANGUAGE is Persian and the Gregorian calendar
///     otherwise (see <see cref="G9Calendar" />); the layout direction still follows the culture.
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

    /// <summary>
    ///     Years built on each side of the selection. The year drum is NOT virtualized — every year
    ///     is a realized row — so it must never be sized from Min..Max: a <c>MinDate = 1900</c>
    ///     sentinel alone was ~175 rows, at a measured ~700 ms per 31 rows on Android.
    /// </summary>
    private const int YearWindowRadius = 50;

    /// <summary>Rows appended when the selection gets close to the END of the built window.</summary>
    private const int YearWindowStep = 25;

    /// <summary>How close (in rows) to a window edge the selection must be before the window grows.</summary>
    private const int YearWindowEdgeRows = 2;

    /// <summary>
    ///     Delay before the window is re-centred after the selection reached its START. Longer than
    ///     the drum's own post-settle snap, which would otherwise scroll the rebuilt column to the
    ///     old row's offset.
    /// </summary>
    private const int YearRecentreDelayMs = 260;

    /// <summary>Duration of the corrective roll after a value was clamped to Min/Max.</summary>
    private const int ClampSnapDurationMs = 220;

    private const int GregorianMaxYear = 9999;

    /// <summary>One short of the Persian calendar's last (partial) year, so every month/day the drums offer exists.</summary>
    private const int PersianMaxYear = 9377;

    private readonly G9DateTimePickerMode _mode;
    private readonly DateTime? _minDate;
    private readonly DateTime? _maxDate;
    private readonly bool _twentyFourHour;
    private readonly bool _isPersian;
    private readonly bool _isRtl;
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

    /// <summary>First / last year the year drum currently holds (see <see cref="BuildYears" />).</summary>
    private int _yearWindowMin;
    private int _yearWindowMax;
    private bool _yearRecentreScheduled;
    private bool _clampSnapScheduled;

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
        _twentyFourHour = twentyFourHour;
        // Calendar by LANGUAGE, layout by DIRECTION. Both used to come from IsRtl, which gave
        // Arabic / Hebrew apps the Jalali calendar.
        _isPersian = G9Calendar.IsPersianLanguage(G9Culture.CurrentCulture);
        _isRtl = G9Culture.IsRtl;
        _owner = owner;
        _showTodayButton = showTodayButton;

        // PersianCalendar throws for anything before 0622-03-22, and nothing above this sheet
        // catches it. Bounds are pulled into the supported range once, here, so every later
        // GetDateParts call is safe; a SELECTED value the calendar cannot represent (a bound
        // default(DateTime) is the usual one) means "no value yet" and opens on today.
        if (_isPersian)
        {
            minDate = minDate.HasValue ? G9Calendar.ClampToPersianRange(minDate.Value) : null;
            maxDate = maxDate.HasValue ? G9Calendar.ClampToPersianRange(maxDate.Value) : null;
            if (selected.HasValue && !G9Calendar.IsSupportedByPersianCalendar(selected.Value))
            {
                selected = null;
            }
        }

        _minDate = minDate;
        _maxDate = maxDate;
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
        FlowDirection = _isRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

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
            try
            {
                next = CreateDate(year, month, day, hour, minute);
            }
            catch (ArgumentOutOfRangeException)
            {
                // A drum combination the calendar cannot represent (its very first / last days).
                // This runs inside an event handler, where an exception is fatal — keep the last
                // good value and put the drums back on it.
                ScheduleSnapToSelected();
                return;
            }

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

        // Clamp used to change the VALUE only: the drums kept showing the out-of-range date while
        // the preview and the returned value said something else. Roll them onto the clamped one.
        if (_selected != next)
        {
            ScheduleSnapToSelected();
        }
        else if (_yearColumn is not null)
        {
            GrowYearWindowIfNeeded(GetDateParts(_selected).Year);
        }
    }

    /// <summary>
    ///     Rolls every drum onto <see cref="_selected" />. Deferred by one dispatcher tick on
    ///     purpose: this is reached from a drum's <c>SelectedValueChanged</c>, and the drum starts
    ///     its own snap animation right AFTER raising that event — a roll started from inside the
    ///     handler would be cancelled by it.
    /// </summary>
    private void ScheduleSnapToSelected()
    {
        if (_clampSnapScheduled) return;
        _clampSnapScheduled = true;

        Dispatcher.Dispatch(() =>
        {
            _clampSnapScheduled = false;
            if (_completed) return;
            _ = SnapColumnsToSelectedAsync();
        });
    }

    private async Task SnapColumnsToSelectedAsync()
    {
        var (year, month, day) = GetDateParts(_selected);

        _suspendApply = true;
        try
        {
            if (_yearColumn is not null && !YearInRange(year))
            {
                _yearColumn.SetItems(BuildYears(year), year);
            }

            SyncDayCount(year, month, day);
        }
        finally
        {
            _suspendApply = false;
        }

        var tasks = new List<Task>();
        if (_yearColumn is not null) tasks.Add(_yearColumn.AnimateToValue(year, ClampSnapDurationMs));
        if (_monthColumn is not null) tasks.Add(_monthColumn.AnimateToValue(month, ClampSnapDurationMs));
        if (_dayColumn is not null) tasks.Add(_dayColumn.AnimateToValue(day, ClampSnapDurationMs));
        if (_hourColumn is not null) tasks.Add(_hourColumn.AnimateToValue(_selected.Hour, ClampSnapDurationMs));
        if (_minuteColumn is not null) tasks.Add(_minuteColumn.AnimateToValue(_selected.Minute, ClampSnapDurationMs));

        try { await Task.WhenAll(tasks).ConfigureAwait(true); }
        catch { }
    }

    /// <summary>
    ///     Day-count adjustment (Persian leap-Esfand, Gregorian leap-Feb, 30/31 alternating months)
    ///     through the cheap trim/extend path — see <see cref="ApplySelectionFromColumns" />.
    /// </summary>
    private void SyncDayCount(int year, int month, int day)
    {
        if (_dayColumn is null) return;

        var requiredDays = GetDaysInMonth(year, month);
        if (_dayColumnBuiltForDayCount == requiredDays) return;

        var culture = G9Culture.CurrentCulture;
        _dayColumn.TrimOrExtendItems(requiredDays, day, idx => new G9DrumItem
        {
            Value = idx + 1,
            Text = (idx + 1).ToString("00", culture)
        });
        _dayColumnBuiltForDayCount = requiredDays;
    }

    /// <summary>
    ///     Keeps the capped year window usable: when the selection comes within
    ///     <see cref="YearWindowEdgeRows" /> of an edge that Min/Max do not actually impose, the
    ///     window grows in that direction.
    ///     <para>
    ///         The END grows in place — appending rows does not move the rows already there, so it
    ///         is cheap and cannot disturb the drum's scroll position. The START cannot (rows
    ///         inserted above would shift everything under the finger), so there the column is
    ///         rebuilt re-centred on the selection, once the drum has come to rest.
    ///     </para>
    /// </summary>
    private void GrowYearWindowIfNeeded(int year)
    {
        if (_yearColumn is null) return;

        var (allowedMin, allowedMax) = ResolveAllowedYears();

        if (year >= _yearWindowMax - YearWindowEdgeRows && _yearWindowMax < allowedMax)
        {
            var newMax = Math.Min(allowedMax, _yearWindowMax + YearWindowStep);
            var windowMin = _yearWindowMin;
            var culture = G9Culture.CurrentCulture;

            _suspendApply = true;
            try
            {
                _yearColumn.TrimOrExtendItems(newMax - windowMin + 1, year, idx => new G9DrumItem
                {
                    Value = windowMin + idx,
                    Text = (windowMin + idx).ToString("0000", culture)
                });
                _yearWindowMax = newMax;
            }
            finally
            {
                _suspendApply = false;
            }
        }

        if (year <= _yearWindowMin + YearWindowEdgeRows && _yearWindowMin > allowedMin && !_yearRecentreScheduled)
        {
            _yearRecentreScheduled = true;
            Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(YearRecentreDelayMs), RecentreYearWindow);
        }
    }

    private void RecentreYearWindow()
    {
        _yearRecentreScheduled = false;
        if (_completed || _yearColumn is null || Handler is null) return;

        // Still under the finger or still rolling: leave it. The next settle re-evaluates.
        if (!_yearColumn.IsIdle) return;

        var year = GetDateParts(_selected).Year;
        var (allowedMin, _) = ResolveAllowedYears();
        if (year > _yearWindowMin + YearWindowEdgeRows || _yearWindowMin <= allowedMin) return;

        _suspendApply = true;
        try
        {
            _yearColumn.SetItems(BuildYears(year), year);
        }
        finally
        {
            _suspendApply = false;
        }
    }

    private void UpdatePreview()
    {
        _previewLabel.Text = _owner?.FormatValue(_selected) ?? FormatFallback(_selected);
    }

    private string FormatFallback(DateTime value)
    {
        var culture = G9Culture.CurrentCulture;
        if (_isPersian && G9Calendar.IsSupportedByPersianCalendar(value))
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

        // Formats in the Gregorian calendar whatever the culture's own calendar is (G9Calendar).
        culture = G9Calendar.GetGregorianFormatCulture(culture);
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

        // Every caller passes a value already inside the Persian range (the ctor pulls Min / Max /
        // the selection into it, and "now" always is); the clamp makes that a guarantee, not a hope.
        date = G9Calendar.ClampToPersianRange(date);
        return (
            PersianCalendar.GetYear(date),
            PersianCalendar.GetMonth(date),
            PersianCalendar.GetDayOfMonth(date));
    }

    /// <summary>
    ///     Builds the year rows for a window of at most ±<see cref="YearWindowRadius" /> around
    ///     <paramref name="selectedYear" />, inside Min/Max, and records that window. Not lazy on
    ///     purpose: <see cref="YearInRange" /> and <see cref="GrowYearWindowIfNeeded" /> read the
    ///     recorded bounds, which an iterator would only set once somebody enumerated it.
    /// </summary>
    private List<G9DrumItem> BuildYears(int selectedYear)
    {
        var (allowedMin, allowedMax) = ResolveAllowedYears();

        var minYear = Math.Max(allowedMin, selectedYear - YearWindowRadius);
        var maxYear = Math.Min(allowedMax, selectedYear + YearWindowRadius);
        if (minYear > maxYear)
        {
            // Inverted Min/Max from the consumer. One row beats an empty, unselectable drum.
            minYear = maxYear = selectedYear;
        }

        _yearWindowMin = minYear;
        _yearWindowMax = maxYear;

        var culture = G9Culture.CurrentCulture;
        var items = new List<G9DrumItem>(maxYear - minYear + 1);
        for (var year = minYear; year <= maxYear; year++)
        {
            items.Add(new G9DrumItem
            {
                Value = year,
                Text = year.ToString("0000", culture)
            });
        }

        return items;
    }

    /// <summary>The years Min/Max (or, without them, the calendar itself) allow.</summary>
    private (int Min, int Max) ResolveAllowedYears()
    {
        var calendarMax = _isPersian ? PersianMaxYear : GregorianMaxYear;
        var min = _minDate.HasValue ? GetDateParts(_minDate.Value).Year : 1;
        var max = _maxDate.HasValue ? GetDateParts(_maxDate.Value).Year : calendarMax;
        return (Math.Clamp(min, 1, calendarMax), Math.Clamp(max, 1, calendarMax));
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
            // The year column holds a window around the year it was last built for. If today's
            // year falls outside it, AnimateToValue would silently no-op, so rebuild the column
            // around today — rare, so the cost is fine. YearInRange tests the window that was
            // ACTUALLY built: it used to recompute one around `_selected`, which the line above
            // had just overwritten with today, so it always answered "in range" and Today did
            // nothing whenever the value was more than 50 years away.
            if (_yearColumn is not null && !YearInRange(year))
            {
                _yearColumn.SetItems(BuildYears(year), year);
            }

            SyncDayCount(year, month, day);
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

    /// <summary>Whether the year drum, as currently built, holds a row for <paramref name="year" />.</summary>
    private bool YearInRange(int year)
    {
        if (_yearColumn is null) return true;
        return year >= _yearWindowMin && year <= _yearWindowMax;
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
