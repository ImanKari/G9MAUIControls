using G9MAUIControls.Localization;
using G9MAUIControls.Hosting;
using G9MAUIControls.BottomSheet;
using G9MAUIControls.Theming;
using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

/// <summary>
///     What <see cref="G9SelectionSheet.ShowForResultAsync" /> resolves to: the selection, plus HOW the
///     sheet ended — which the bare list returned by <see cref="G9SelectionSheet.ShowAsync" /> cannot say.
///     <para>
///         A bare list has one value, the empty list, for three different endings: the user cleared
///         the selection, the user dismissed a sheet that had nothing selected, and the sheet was
///         closed before its deferred content was ever built. <c>G9ComboBox</c> read all three as
///         "cleared" and wrote <c>null</c> into the bound value — a recorded data-loss bug in the
///         consuming app. And a dismissed single-select sheet returned the PREVIOUS item, which
///         <c>G9Picker</c> could not tell from a pick, so it raised <c>ItemSelected</c> for a cancel.
///     </para>
/// </summary>
public sealed class G9SelectionSheetResult
{
    internal G9SelectionSheetResult(bool wasAccepted, bool selectionChanged, IReadOnlyList<G9SelectionItem> items)
    {
        WasAccepted = wasAccepted;
        SelectionChanged = selectionChanged;
        Items = items;
    }

    /// <summary>
    ///     <see langword="true" /> when the user committed explicitly: picked a row on a
    ///     close-on-select sheet, pressed Done, or pressed Reset. <see langword="false" /> for every
    ///     dismissal — back, swipe-down, tap outside, the header close button, or a close that beat
    ///     the deferred content.
    /// </summary>
    public bool WasAccepted { get; }

    /// <summary>
    ///     <see langword="true" /> when <see cref="Items" /> is a different selection from the one the
    ///     sheet was opened with. A sheet that was dismissed untouched — or never got as far as
    ///     building its rows — reports <see langword="false" />.
    /// </summary>
    public bool SelectionChanged { get; }

    /// <summary>
    ///     The selection when the sheet ended. Never loses an item the caller passed in as selected
    ///     unless the user deselected it: an item that is disabled, or missing from the candidate
    ///     list, stays selected. An empty list therefore means the user really left nothing selected.
    /// </summary>
    public IReadOnlyList<G9SelectionItem> Items { get; }
}

/// <summary>
///     Bottom-sheet picker rendering a single column of <see cref="G9SelectionItem" />.
///     Used by <see cref="G9Picker" /> and <see cref="G9ComboBox" />.
///     // TODO (palette step): row / check colors will move to G9Palette.
/// </summary>
public sealed class G9SelectionSheet : Grid, IG9BottomSheetAwareView, IDeferredContentReadiness
{
    /// <summary>Pause after the last keystroke before the list is filtered.</summary>
    private const int SearchDebounceMs = 150;

    /// <summary>Dim applied to a row whose item is not enabled.</summary>
    private const double DisabledRowOpacity = 0.45;

    private readonly IReadOnlyList<G9SelectionItem> _allItems;

    /// <summary>The selection the sheet was opened with — what <see cref="CollectSelection" /> falls back on for items that are not in the list.</summary>
    private readonly IReadOnlyList<G9SelectionItem> _initialSelection;

    /// <summary>Labels shared by more than one key-less item; see <see cref="KeyOf" />.</summary>
    private readonly HashSet<string> _ambiguousTexts;
    private readonly HashSet<object> _selectedKeys;
    private readonly HashSet<object> _initialKeys;

    /// <summary>Every row, built once. Searching toggles <see cref="VisualElement.IsVisible" /> on these.</summary>
    private readonly List<(G9SelectionItem Item, Border Row)> _rows = [];
    private Label? _emptyLabel;
    private IDispatcherTimer? _searchTimer;
    private readonly bool _allowMultiple;
    private readonly bool _closeOnSingleSelection;
    private readonly bool _showSearch;
    private readonly string _emptyText;
    private readonly FlowDirection _itemFlowDirection;
    private readonly VerticalStackLayout _itemsHost;
    private readonly ScrollView _itemsScroll;
    private readonly DeferredContentReadinessSignal _readiness = new();
    // Multi-select only: the live selected-count pill lives in the SHARED bottom-sheet header's
    // trailing slot (built in ShowAsync and passed into the ctor) so multi-select uses the SAME
    // header as every other sheet. Null for single-select. Updated in UpdateSelectedCount.
    private readonly Label? _selectedCountLabel;
    private readonly Border? _selectedCountBorder;
    private readonly G9TextEntry? _searchEntry;
    private bool _isCompleted;

    /// <summary>A close-on-select pick is waiting out its 120 ms before closing; further taps are ignored.</summary>
    private bool _pickInFlight;
    private string _query = string.Empty;

    public G9SelectionSheet(
        string title,
        IEnumerable<G9SelectionItem> items,
        IEnumerable<G9SelectionItem>? selected,
        bool allowMultiple,
        bool closeOnSingleSelection,
        bool showSearch,
        string? emptyText = null,
        G9TextInputDirection itemTextDirection = G9TextInputDirection.MatchParent,
        Label? countLabel = null,
        Border? countBadge = null)
    {
        _allItems = items.ToList();
        _initialSelection = selected?.ToList() ?? [];
        _ambiguousTexts = FindAmbiguousTexts(_allItems);
        _initialKeys = _initialSelection.Select(KeyOf).ToHashSet();
        _selectedKeys = [.. _initialKeys];
        _allowMultiple = allowMultiple;
        _closeOnSingleSelection = closeOnSingleSelection;
        _showSearch = showSearch;
        _emptyText = string.IsNullOrWhiteSpace(emptyText) ? G9Strings.Get(G9StringKey.NoResults) : emptyText;
        _itemFlowDirection = ResolveItemFlowDirection(itemTextDirection);

        RowDefinitions =
        [
            new RowDefinition(GridLength.Auto),
            new RowDefinition(GridLength.Auto),
            // The items scroll row hugs its content (Auto) and scrolls only when the content
            // exceeds the ScrollView's MaximumHeightRequest. A Star row here expanded to fill
            // the sheet's allocated height, leaving a large empty gap below the last item on
            // short lists (theme / GPS / language pickers).
            new RowDefinition(GridLength.Auto)
        ];

        BackgroundColor = G9Palette.Current.Surface;
        Padding = new Thickness(0, 0, 0, 4);

        // Fit-to-content height: hug the actual content instead of forcing a tall floor.
        // Shared with ShowAsync so the deferred loading-spinner placeholder opens at exactly
        // this height — the spinner and the swapped-in content share one height, so there's no
        // resize "jump" on content load.
        MinimumHeightRequest = EstimateContentHeight(_allItems.Count, showSearch);

        // The selected-count pill (multi-select) is built in ShowAsync and lives in the SHARED
        // header's trailing slot; the sheet just keeps references to update its text/visibility.
        // Single-select gets null (no badge). BOTH modes now use the shared bottom-sheet header,
        // so this view no longer draws any inline header of its own.
        _selectedCountLabel = countLabel;
        _selectedCountBorder = countBadge;

        if (showSearch)
        {
            _searchEntry = new G9TextEntry
            {
                Placeholder = G9Strings.Get(G9StringKey.Search),
                LeadingIcon = G9Glyphs.Search,
                Margin = new Thickness(12, 8, 12, 8)
            };
            _searchEntry.InnerEntry.TextChanged += (_, e) =>
            {
                _query = e.NewTextValue ?? string.Empty;
                ScheduleFilter();
            };
            Grid.SetRow(_searchEntry, 1);
            Children.Add(_searchEntry);
        }

        // Flat, full-bleed rows: no inter-row spacing and no side padding on the host — the
        // side gap lives inside each row (SelectionRowHorizontalPadding) so the pressed / selected
        // fill spans edge-to-edge like a standard list, not an inset card.
        _itemsHost = new VerticalStackLayout { Spacing = 0, Padding = new Thickness(0, 4, 0, 4) };
        // The scroll cap MUST track the same max the sheet height is clamped to in
        // EstimateContentHeight (SelectionMaxHeight minus the same chrome), otherwise a long list
        // capped the scroll well below the taller sheet height and left a large empty gap below
        // the last visible row (the "Product picker weird gap"). Deriving both from the same
        // chrome keeps the scroll filling exactly up to the sheet's clamped height.
        var scrollMaxHeight = G9Metrics.SelectionMaxHeight - ChromeAllowance(showSearch);
        _itemsScroll = new ScrollView { Content = _itemsHost, MaximumHeightRequest = scrollMaxHeight };
        Grid.SetRow(_itemsScroll, 2);
        Children.Add(_itemsScroll);

        BuildRows();
        UpdateSelectedCount();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public IG9BottomSheetHandle G9BottomSheetHandle { get; set; } = G9BottomSheetHelper.InitG9BottomSheet();
    public event EventHandler<IReadOnlyList<G9SelectionItem>>? Completed;

    /// <summary>
    ///     Set before <see cref="Completed" /> is raised: <see langword="true" /> when the user
    ///     committed (row pick on a close-on-select sheet, Done), <see langword="false" /> when the
    ///     sheet was merely dismissed. See <see cref="G9SelectionSheetResult.WasAccepted" />.
    /// </summary>
    public bool WasAccepted { get; private set; }

    /// <summary>True when the current selection differs from the one the sheet was opened with.</summary>
    public bool HasSelectionChanged => !_selectedKeys.SetEquals(_initialKeys);

    bool IDeferredContentReadiness.IsContentReady => _readiness.IsReady;

    event EventHandler? IDeferredContentReadiness.ContentReady
    {
        add => _readiness.Ready += value;
        remove => _readiness.Ready -= value;
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        try
        {
            await ScrollInitialSelectionIntoViewAsync().ConfigureAwait(true);
        }
        finally
        {
            _readiness.MarkReady();
        }
    }

    private async Task ScrollInitialSelectionIntoViewAsync()
    {
        if (_selectedKeys.Count == 0)
        {
            return;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var selectedRow = FindFirstSelectedRow();
            if (selectedRow is not null)
            {
                try
                {
                    await _itemsScroll.ScrollToAsync(selectedRow, ScrollToPosition.Center, false).ConfigureAwait(true);
                }
                catch (ObjectDisposedException)
                {
                }
                catch (InvalidOperationException)
                {
                }

                return;
            }

            await Task.Delay(16).ConfigureAwait(true);
        }
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        // A pending debounce tick must not fire into (or keep alive) a closed sheet.
        _searchTimer?.Stop();
    }

    private View? FindFirstSelectedRow()
    {
        foreach (var (item, row) in _rows)
        {
            if (row.IsVisible && _selectedKeys.Contains(KeyOf(item)))
            {
                return row;
            }
        }

        return null;
    }

    /// <summary>
    ///     The identity a row is selected by: the item's <see cref="G9SelectionItem.Key" /> when it
    ///     has one, otherwise its text — unless that text is shared by another key-less item, in
    ///     which case the INSTANCE is the identity.
    ///     <para>
    ///         <see cref="G9SelectionItem.SelectionIdentity" /> falls back to the text alone, so two
    ///         key-less rows with the same label were one selection: tapping either checked both,
    ///         and both came back in the result. Text is still used while it is unambiguous, because
    ///         that is what keeps a selection alive when the caller rebuilds its item instances.
    ///         Keys and texts live in separate namespaces so a key "A" never matches a label "A".
    ///     </para>
    /// </summary>
    private object KeyOf(G9SelectionItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.Key))
        {
            return (true, item.Key);
        }

        if (_ambiguousTexts.Contains(item.Text))
        {
            return item;
        }

        return (false, item.Text);
    }

    private static HashSet<string> FindAmbiguousTexts(IReadOnlyList<G9SelectionItem> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ambiguous = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in items)
        {
            if (string.IsNullOrWhiteSpace(item.Key) && !seen.Add(item.Text))
            {
                ambiguous.Add(item.Text);
            }
        }

        return ambiguous;
    }

    /// <summary>
    ///     Predicts the sheet's fit-to-content height from the item count up-front (rows ×
    ///     rowHeight + chrome), clamped to a two-row floor and <see cref="G9Metrics.SelectionMaxHeight" />.
    ///     Used by the constructor for its own <c>MinimumHeightRequest</c> AND by <see cref="ShowAsync" />
    ///     for the deferred loading-placeholder height, so the sheet opens at its final size.
    /// </summary>
    private static double EstimateContentHeight(int itemCount, bool showSearch)
    {
        var chromeAllowance = ChromeAllowance(showSearch);
        var contentHeight = (itemCount * G9Metrics.SelectionRowHeight) + chromeAllowance;
        return Math.Clamp(
            contentHeight,
            (G9Metrics.SelectionRowHeight * 2) + chromeAllowance,
            G9Metrics.SelectionMaxHeight);
    }

    /// <summary>
    ///     Non-row vertical space the sheet's CONTENT reserves: the optional search field + padding.
    ///     The header is now the SHARED bottom-sheet header (drawn ABOVE this view by the helper for
    ///     BOTH single- and multi-select), so it costs the content nothing here. Shared by
    ///     <see cref="EstimateContentHeight" /> (sheet height) and the inner scroll cap so the two
    ///     never disagree (which would leave a gap).
    /// </summary>
    private static double ChromeAllowance(bool showSearch)
    {
        return (showSearch ? 64 : 0) + 18;
    }

    /// <summary>Completes as a DISMISSAL with the selection as it stands (<see cref="WasAccepted" /> = false).</summary>
    public void CompleteFromClose() => Complete(CollectSelection(), accepted: false);

    /// <summary>
    ///     Marks the sheet as already resolved so the close handler does not overwrite a result the
    ///     caller has just produced by other means (the header RESET action completes with its own
    ///     selection, then closes).
    /// </summary>
    public void MarkCompleted() => Complete([], accepted: true);

    /// <summary>
    ///     Shows the sheet and resolves to the selection when it ends.
    ///     <para>
    ///         The list cannot say whether the user committed or dismissed — prefer
    ///         <see cref="ShowForResultAsync" /> wherever the result is written back into a bound
    ///         value. Kept for source compatibility; one thing did change for the better: a sheet
    ///         closed before its deferred content was built now returns the selection it was opened
    ///         with instead of an empty list (which read as "the user cleared it").
    ///     </para>
    /// </summary>
    public static async Task<IReadOnlyList<G9SelectionItem>> ShowAsync(
        string title,
        IEnumerable<G9SelectionItem> items,
        IEnumerable<G9SelectionItem>? selectedItems,
        bool allowMultiple,
        bool closeOnSingleSelection,
        bool showSearch,
        string? emptyText = null,
        G9TextInputDirection itemTextDirection = G9TextInputDirection.MatchParent,
        IEnumerable<G9SelectionItem>? resetSelection = null)
    {
        var result = await ShowForResultAsync(
            title, items, selectedItems, allowMultiple, closeOnSingleSelection, showSearch,
            emptyText, itemTextDirection, resetSelection).ConfigureAwait(false);
        return result.Items;
    }

    /// <summary>
    ///     Shows the sheet and resolves to a <see cref="G9SelectionSheetResult" />, which — unlike the
    ///     list from <see cref="ShowAsync" /> — distinguishes "committed" from "dismissed" and
    ///     "changed" from "untouched". This is what <see cref="G9ComboBox" /> and
    ///     <see cref="G9Picker" /> use.
    /// </summary>
    public static Task<G9SelectionSheetResult> ShowForResultAsync(
        string title,
        IEnumerable<G9SelectionItem> items,
        IEnumerable<G9SelectionItem>? selectedItems,
        bool allowMultiple,
        bool closeOnSingleSelection,
        bool showSearch,
        string? emptyText = null,
        G9TextInputDirection itemTextDirection = G9TextInputDirection.MatchParent,
        IEnumerable<G9SelectionItem>? resetSelection = null)
    {
        var tcs = new TaskCompletionSource<G9SelectionSheetResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        G9SelectionSheet? sheet = null;

        // Materialize the items list once — the factory may be invoked on a deferred
        // dispatcher tick so we can't rely on the IEnumerable still being safe to
        // re-enumerate. Same for selected.
        var materializedItems = items.ToList();
        var materializedSelected = selectedItems?.ToList();

        void OnCompleted(object? sender, IReadOnlyList<G9SelectionItem> result)
        {
            if (sheet is null) return;
            sheet.Completed -= OnCompleted;
            tcs.TrySetResult(new G9SelectionSheetResult(sheet.WasAccepted, sheet.HasSelectionChanged, result));
        }

        // Use the factory + DeferContent path so the heavy view-tree construction
        // (each row is a Border + Grid + 3 children, plus optional G9IconView vector)
        // runs AFTER the sheet host is laid out and the open animation has started.
        // Without this, building 10+ rows synchronously before the sheet appears
        // produced a 1-3s perceptible delay between tap and sheet animation start.
        // The DeferredContentView shows a centered spinner for ~370ms then swaps in
        // the real content — by which time the sheet open animation has finished, so
        // the user sees the sheet appear instantly with a brief spinner that
        // becomes the picker. Measured on emulator: tap → ShowAsync 80ms → factory
        // invoked +492ms (after open animation) → factory done +24ms = 516ms total
        // perceived time vs the previous 1-3s synchronous lag.
        // Multi-select carries a live selected-count pill + a Done button. They now live in the
        // SHARED header's trailing slot (so multi-select uses the SAME header as every other sheet).
        // The header is built from `options` BEFORE the deferred sheet instance exists, so we build
        // these views here and: (a) hand them to `options.HeaderTrailingView`; (b) pass the pill's
        // label/border into the sheet ctor so it can update the live count; (c) wire Done to the
        // captured `sheet` closure (assigned in the factory before the user can tap it).
        Label? countLabel = null;
        Border? countBadge = null;
        View? headerTrailingView = null;
        if (allowMultiple)
        {
            countLabel = new Label
            {
                FontSize = 11,
                FontAttributes = FontAttributes.Bold,
                // The culture's own face, like every other number the suite prints: with a Persian
                // font the count reads in Persian digits instead of Latin ones in a Persian sheet.
                FontFamily = G9Culture.ResolveFontFamily(),
                TextColor = G9Palette.Current.OnPrimaryContainer,
                Padding = new Thickness(10, 4),
                VerticalTextAlignment = TextAlignment.Center
            };
            countBadge = new Border
            {
                StrokeThickness = 0,
                StrokeShape = G9Colors.Round(12),
                BackgroundColor = G9Palette.Current.PrimaryContainer,
                Content = countLabel,
                VerticalOptions = LayoutOptions.Center,
                IsVisible = false
            };
            var doneButton = new G9Button
            {
                Text = G9Strings.Get(G9StringKey.Done),
                Variant = G9ButtonVariant.Text,
                Size = G9ControlSize.Small,
                VerticalOptions = LayoutOptions.Center,
                Command = new Command(() =>
                {
                    // Done is a COMMIT, not a dismissal — the distinction the result carries.
                    sheet?.Complete(sheet.CollectSelection(), accepted: true);
                    sheet?.G9BottomSheetHandle.Close();
                })
            };
            headerTrailingView = new HorizontalStackLayout
            {
                Spacing = 8,
                VerticalOptions = LayoutOptions.Center,
                Children = { countBadge, doneButton }
            };
        }

        // Optional RESET, built eagerly next to the count pill so the header never re-lays-out when
        // the deferred content arrives. It completes with the caller's reset selection and closes —
        // the same outcome as picking that row, in one tap, which is what "reset the filter" means on
        // a close-on-select sheet. Callers that want reset-in-place should own their own sheet.
        var resetItems = resetSelection?.ToList();
        IReadOnlyList<ToolbarItem>? toolbarItems = resetItems is { Count: > 0 }
            ?
            [
                new G9BottomSheetToolbarItem
                {
                    Text = G9Strings.Get(G9StringKey.Reset),
                    Icon = G9Glyphs.Refresh,
                    AsyncAction = () =>
                    {
                        tcs.TrySetResult(new G9SelectionSheetResult(
                            wasAccepted: true, selectionChanged: true, resetItems));
                        sheet?.MarkCompleted();
                        sheet?.G9BottomSheetHandle.Close();
                        return Task.CompletedTask;
                    }
                }
            ]
            : null;

        var options = G9BottomSheetOptions.FitToContentOptions() with
        {
            DeferContent = true,
            // Open the loading placeholder at the list's known final BODY height so the swap-in
            // doesn't resize the sheet (no jump). The helper adds its own chrome (the shared
            // header band) on top and clamps to the 75% cap for long lists.
            DeferredLoadingPlaceholderHeight = EstimateContentHeight(materializedItems.Count, showSearch),
            // Skeleton rows instead of a spinner while the list builds — the row count matches
            // the real list so the placeholder reads as the content taking shape.
            LoadingSkeleton = G9BottomSheetLoadingSkeleton.ListRows,
            LoadingSkeletonRowCount = materializedItems.Count,
            BackgroundColor = G9Palette.Current.Surface,
            // BOTH single- and multi-select now use the SHARED standard bottom-sheet header (back
            // arrow + title beside it, NearBack — the app-wide title standard). Multi-select adds
            // its count pill + Done as the header's trailing view; single-select leaves it empty.
            ShowToolbar = true,
            ShowCloseButton = true,
            Title = title,
            HeaderTitlePlacement = G9BottomSheetHeaderTitlePlacement.NearBack,
            HeaderTrailingView = headerTrailingView,
            ToolbarItems = toolbarItems,
            ClosedCommand = new Command(() =>
            {
                // Reached for EVERY close. After a pick / Done / Reset the result is already set and
                // both calls below are no-ops; otherwise this is a dismissal.
                sheet?.CompleteFromClose();

                // Closed before the deferred factory ever ran: nothing was shown, so nothing can
                // have changed. This used to resolve to an EMPTY list, which a caller cannot tell
                // from "the user cleared the selection".
                IReadOnlyList<G9SelectionItem> untouched = materializedSelected ?? [];
                tcs.TrySetResult(new G9SelectionSheetResult(
                    wasAccepted: false,
                    selectionChanged: sheet?.HasSelectionChanged ?? false,
                    sheet?.CollectSelection() ?? untouched));
            })
        };

        G9SelectionSheet BuildSheet()
        {
            var built = new G9SelectionSheet(title, materializedItems, materializedSelected, allowMultiple, closeOnSingleSelection, showSearch, emptyText, itemTextDirection, countLabel, countBadge);
            built.Completed += OnCompleted;
            return built;
        }

        // ⛔ A SHORT list is built up front and shown as a PRE-BUILT body: it goes through
        // stage-before-show, so it opens at its exact height with its rows already in it — no
        // skeleton, no swap. The factory + skeleton path above was written for the case where
        // building the rows is the expensive part (the "1-3 s" note), and that is a property of LONG
        // lists: the rows are not virtualized, so cost scales with the count. A language or theme
        // picker has two or three rows, all already in memory; putting a loading placeholder in
        // front of those made the fastest sheets in the app the ones that visibly "loaded".
        // Views must be built on the UI thread — so a call that arrives OFF it (after a
        // ConfigureAwait(false), typically) goes there, rather than falling back to the skeleton
        // path it was the whole point to avoid.
        if (materializedItems.Count <= EagerBuildItemLimit)
        {
            void ShowPrebuilt()
            {
                try
                {
                    sheet = BuildSheet();
                    G9BottomSheetHelper.ShowG9BottomSheet(sheet, options);
                }
                catch (Exception exception)
                {
                    // Nothing was shown, so nothing will ever resolve the caller's task but this.
                    tcs.TrySetException(exception);
                }
            }

            if (MainThread.IsMainThread)
            {
                ShowPrebuilt();
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(ShowPrebuilt);
            }

            return tcs.Task;
        }

        G9BottomSheetHelper.ShowG9BottomSheet(() =>
        {
            sheet = BuildSheet();
            return sheet;
        }, options);
        return tcs.Task;
    }

    /// <summary>
    ///     Lists up to this many rows are built eagerly and open with their content in place; longer
    ///     ones keep the open-then-build path behind a skeleton (the rows are not virtualized, so a
    ///     very long list is genuinely expensive to construct). Default <c>40</c> — roughly the point
    ///     where building the rows stops being cheaper than the open animation it would delay. A
    ///     tuning knob for a consumer whose devices put that point somewhere else.
    /// </summary>
    public static int EagerBuildItemLimit { get; set; } = 40;

    private static FlowDirection ResolveItemFlowDirection(G9TextInputDirection direction)
    {
        return direction switch
        {
            G9TextInputDirection.LeftToRight => FlowDirection.LeftToRight,
            G9TextInputDirection.RightToLeft => FlowDirection.RightToLeft,
            _ => G9Visuals.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
    }

    /// <summary>
    ///     Builds every row ONCE. The list is not virtualized, and a row is a Border + Grid + Label
    ///     + icon + gesture recognizer; the old code tore all of them down and rebuilt them on every
    ///     search keystroke. Searching now only toggles visibility (<see cref="ApplyFilter" />).
    /// </summary>
    private void BuildRows()
    {
        foreach (var item in _allItems)
        {
            var row = CreateRow(item);
            _rows.Add((item, row));
            _itemsHost.Children.Add(row);
        }

        ApplyFilter();
    }

    /// <summary>
    ///     Debounces the search box: the filter runs <see cref="SearchDebounceMs" /> after the last
    ///     keystroke, so typing a word relays the list out once, not once per letter. Clearing the
    ///     box is applied at once — waiting to show the full list again only reads as lag.
    /// </summary>
    private void ScheduleFilter()
    {
        if (string.IsNullOrWhiteSpace(_query))
        {
            _searchTimer?.Stop();
            ApplyFilter();
            return;
        }

        if (_searchTimer is null)
        {
            _searchTimer = Dispatcher.CreateTimer();
            _searchTimer.Interval = TimeSpan.FromMilliseconds(SearchDebounceMs);
            _searchTimer.IsRepeating = false;
            _searchTimer.Tick += (_, _) => ApplyFilter();
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private void ApplyFilter()
    {
        var hasQuery = !string.IsNullOrWhiteSpace(_query);
        var anyVisible = false;

        foreach (var (item, row) in _rows)
        {
            var matches = !hasQuery || item.Text.Contains(_query, StringComparison.CurrentCultureIgnoreCase);
            if (row.IsVisible != matches) row.IsVisible = matches;
            anyVisible |= matches;
        }

        if (anyVisible)
        {
            if (_emptyLabel is not null) _emptyLabel.IsVisible = false;
            return;
        }

        if (_emptyLabel is null)
        {
            _emptyLabel = new Label
            {
                Text = _emptyText,
                TextColor = G9Palette.Current.TextTertiary,
                FontSize = 13,
                HorizontalTextAlignment = TextAlignment.Center,
                Padding = new Thickness(16, 28)
            };
            _itemsHost.Children.Add(_emptyLabel);
        }

        _emptyLabel.IsVisible = true;
    }

    private Border CreateRow(G9SelectionItem item)
    {
        var palette = G9Palette.Current;
        var selected = _selectedKeys.Contains(KeyOf(item));

        var row = new Grid
        {
            FlowDirection = _itemFlowDirection,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12,
            Padding = new Thickness(G9Metrics.SelectionRowHorizontalPadding, 0),
            VerticalOptions = LayoutOptions.Fill,
            // A disabled item had NO visual state — it looked tappable, flashed when pressed, and
            // then did nothing. Dimming the content (not the Border: a selected-but-disabled row
            // keeps its full-strength selection tint) is state-independent, so UpdateRowVisuals
            // never has to know about it.
            Opacity = item.IsEnabled ? 1 : DisabledRowOpacity,
            BindingContext = item
        };

        // Leading icon (emoji / icon-font glyph / image), or a colour swatch when the
        // item only carries stored colors (0 → none, 1 → solid circle, 2 → split circle). This is
        // what makes the combo / picker dropdown LIST show a variety's colours, matching the
        // selected-value swatch the trigger already shows.
        var hasIcon = G9IconFactory.HasIcon(item.Emoji, item.Icon, item.IconPath, item.IconSource);
        if (hasIcon)
        {
            // ⛔ item.IconTintColor WINS in both states, exactly as it does on the ComboBox / Picker
            // trigger. An item that carries its own colour carries it BECAUSE the colour is the
            // meaning — a soil type, a health state, an attribute option the office coloured on
            // purpose — so repainting it Primary when selected, or TextSecondary when not, throws
            // that meaning away. This row ignored the tint until 2026-09, which is why a coloured
            // option list rendered grey here and then snapped to its real colour the moment it was
            // chosen: the LIST was the only surface in the suite not honouring the field.
            row.Add(G9IconFactory.Create(
                item.Emoji, item.Icon, item.IconPath, item.IconSource,
                ResolveRowIconColor(item, selected, palette),
                G9Metrics.SelectionIconSize), 0);
        }
        else if (G9Visuals.HasSwatch(item.SwatchFirstColor, item.SwatchSecondColor))
        {
            row.Add(G9Visuals.CreateSwatch(
                item.SwatchFirstColor, item.SwatchSecondColor, G9Metrics.SelectionIconSize), 0);
        }

        // Text. HorizontalOptions/HorizontalTextAlignment are pinned to START rather than left to
        // their defaults: the label owns a Star column, so "Fill + whatever the default alignment is"
        // decides which EDGE of that column the text lands on, and the icon sits at the row's start.
        // Stating Start keeps the glyph and its label together as one block on the reading edge —
        // right in RTL, left in LTR — instead of the text drifting away from its own icon.
        row.Add(new Label
        {
            Text = item.Text,
            FontSize = G9Metrics.SelectionRowFontSize,
            FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None,
            TextColor = selected ? palette.Primary : palette.TextPrimary,
            HorizontalOptions = LayoutOptions.Start,
            HorizontalTextAlignment = TextAlignment.Start,
            VerticalTextAlignment = TextAlignment.Center,
            FlowDirection = _itemFlowDirection,
            LineBreakMode = LineBreakMode.TailTruncation
        }, 1);

        // Trailing: check icon always present (hidden via Opacity to avoid rebuild flash)
        var checkIcon = new G9IconView {
            Icon = G9Glyphs.Check,
            Color = palette.Primary,
            Size = 18,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center,
            Opacity = selected ? 1 : 0
        };
        row.Add(checkIcon, 2);

        // Flat, borderless row (design: bottom-sheet selection lists are flat, not framed cards).
        // No stroke and no rounded card shape. Selection is conveyed by a subtle primary-tint fill
        // (no border), bold primary text, the primary leading-icon tint, and the trailing check;
        // a tap plays a subtle press flash for feedback.
        var rowBorder = new Border
        {
            FlowDirection = _itemFlowDirection,
            StrokeThickness = 0,
            Stroke = null,
            BackgroundColor = selected ? SelectedRowTint(palette) : Colors.Transparent,
            MinimumHeightRequest = G9Metrics.SelectionRowHeight,
            Content = row
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += async (_, _) =>
        {
            // A disabled row neither acts nor pretends to; and once a close-on-select pick is on its
            // way out, a second tap must not re-point the selection the first one already returned.
            if (!item.IsEnabled || _isCompleted || _pickInFlight) return;

            // ACT FIRST. This used to await the 110 ms press flash before touching the selection,
            // so every tap felt late. The selection (and its row styling) is applied synchronously
            // inside SelectItemAsync; the flash is feedback only and never gates the action.
            var selection = SelectItemAsync(item);
            _ = PlayRowPressAsync(rowBorder, item);
            await selection.ConfigureAwait(true);
        };
        rowBorder.GestureRecognizers.Add(tap);
        return rowBorder;
    }

    /// <summary>
    ///     The colour ONE row's leading icon is drawn in. The single source of truth for it — both the
    ///     build (<see cref="CreateRow" />) and the re-style (<see cref="UpdateRowVisuals" />) call this.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b><see cref="G9SelectionItem.IconTintColor" /> wins in BOTH states</b>, exactly as it does
    ///         on the ComboBox / Picker trigger. An item that carries its own colour carries it BECAUSE
    ///         the colour is the meaning — a soil type, a health state, an option a back office coloured
    ///         on purpose — so repainting it Primary when selected, or TextSecondary when not, throws
    ///         that meaning away.
    ///     </para>
    ///     <para>
    ///         ⛔ Do not inline this back into either caller. The field was ignored here until 2026-09,
    ///         and the first attempt to fix it changed only <see cref="CreateRow" /> — which had no
    ///         visible effect at all, because <see cref="UpdateRowVisuals" /> runs afterwards and
    ///         overwrote the colour it had just set. A value with two writers has no owner.
    ///     </para>
    /// </remarks>
    private static Color ResolveRowIconColor(G9SelectionItem item, bool selected, G9Palette palette) =>
        item.IconTintColor ?? (selected ? palette.Primary : palette.TextSecondary);

    /// <summary>Subtle primary-tint fill marking a selected row (no border).</summary>
    private static Color SelectedRowTint(G9Palette palette) => palette.Primary.WithAlpha(0.10f);

    /// <summary>
    ///     Subtle press feedback for a flat row: briefly wash the row with a low-alpha neutral
    ///     (theme-aware via <c>OnSurface</c>) then restore its resting fill. Replaces the removed
    ///     card border/background as the row's "pressed" cue.
    /// </summary>
    private async Task PlayRowPressAsync(Border row, G9SelectionItem item)
    {
        row.BackgroundColor = G9Palette.Current.OnSurface.WithAlpha(0.06f);
        try
        {
            await Task.Delay(110).ConfigureAwait(true);
        }
        catch
        {
        }

        // Resolved NOW, not captured before the flash: the flash no longer blocks the action, so
        // the row's selection may have flipped (again) while it was showing.
        row.BackgroundColor = _selectedKeys.Contains(KeyOf(item))
            ? SelectedRowTint(G9Palette.Current)
            : Colors.Transparent;
    }

    private async Task SelectItemAsync(G9SelectionItem item)
    {
        if (!item.IsEnabled) return;

        var key = KeyOf(item);
        if (_allowMultiple)
        {
            if (!_selectedKeys.Add(key)) _selectedKeys.Remove(key);
            UpdateRowVisuals();
            UpdateSelectedCount();
            return;
        }

        _selectedKeys.Clear();
        _selectedKeys.Add(key);
        UpdateRowVisuals();

        if (_closeOnSingleSelection)
        {
            _pickInFlight = true;
            try
            {
                await Task.Delay(120).ConfigureAwait(true);
            }
            catch
            {
            }

            Complete([item], accepted: true);
            G9BottomSheetHandle.Close();
        }
    }

    /// <summary>
    ///     Updates the visual state (background, border, check opacity, text style) of existing
    ///     rows without destroying and recreating them. This prevents the "blink" that occurs
    ///     when all rows are rebuilt on every selection toggle.
    /// </summary>
    private void UpdateRowVisuals()
    {
        var palette = G9Palette.Current;

        foreach (var child in _itemsHost.Children)
        {
            if (child is not Border border || border.Content is not Grid row) continue;

            // Retrieve the item identity stored in the row's BindingContext.
            if (row.BindingContext is not G9SelectionItem item) continue;

            var selected = _selectedKeys.Contains(KeyOf(item));

            // Flat row: no stroke. Selection is a subtle primary-tint fill only.
            border.BackgroundColor = selected ? SelectedRowTint(palette) : Colors.Transparent;

            // Update text label (column 1)
            foreach (var element in row.Children)
            {
                if (Grid.GetColumn((BindableObject)element) == 1 && element is Label textLabel)
                {
                    textLabel.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
                    textLabel.TextColor = selected ? palette.Primary : palette.TextPrimary;
                }
                else if (Grid.GetColumn((BindableObject)element) == 2 && element is G9IconView checkIcon)
                {
                    checkIcon.Opacity = selected ? 1 : 0;
                }
                else if (Grid.GetColumn((BindableObject)element) == 0 && element is View iconView)
                {
                    // Update icon color for leading icon
                    if (iconView is Label emojiLabel)
                    {
                        // Emoji labels don't need color change
                    }
                    else if (iconView is G9IconView mauiIcon)
                    {
                        // ⛔ Through the SAME resolver CreateRow uses. This line is why fixing CreateRow
                        // alone changed nothing: rows are built once and then re-styled here on every
                        // selection change (and after the first filter pass), so whatever this writes is
                        // what the operator sees. Two writers for one colour is the bug; the shared
                        // resolver is the fix.
                        mauiIcon.Color = ResolveRowIconColor(item, selected, palette);
                    }
                }
            }
        }
    }

    private void UpdateSelectedCount()
    {
        // The count pill lives in the shared header (multi-select only); null for single-select.
        if (_selectedCountLabel is not null)
        {
            _selectedCountLabel.Text = string.Format(
                G9Culture.CurrentCulture, "{0} {1}", _selectedKeys.Count, G9Strings.Get(G9StringKey.Selected));
        }

        if (_selectedCountBorder is not null)
        {
            _selectedCountBorder.IsVisible = _allowMultiple && _selectedKeys.Count > 0;
        }
    }

    /// <summary>
    ///     The current selection: the listed items that are selected, plus every item the sheet was
    ///     opened with that is still selected but is NOT in the list.
    ///     <para>
    ///         The second half is the point. This used to be the intersection with the list alone,
    ///         so a selected item the caller had filtered out of the candidates (disabled, or no
    ///         longer in its source) silently fell out of the result — opening the sheet and
    ///         dismissing it was enough to null a bound value. The user can only deselect what they
    ///         can see, so anything they cannot see stays selected.
    ///     </para>
    /// </summary>
    private IReadOnlyList<G9SelectionItem> CollectSelection()
    {
        var result = new List<G9SelectionItem>();
        var listed = new HashSet<object>();

        foreach (var item in _allItems)
        {
            var key = KeyOf(item);
            if (!_selectedKeys.Contains(key)) continue;

            listed.Add(key);
            result.Add(item);
        }

        foreach (var item in _initialSelection)
        {
            var key = KeyOf(item);
            if (_selectedKeys.Contains(key) && listed.Add(key))
            {
                result.Add(item);
            }
        }

        return result;
    }

    private void Complete(IReadOnlyList<G9SelectionItem> selection, bool accepted)
    {
        if (_isCompleted) return;
        _isCompleted = true;
        WasAccepted = accepted;
        Completed?.Invoke(this, selection);
    }
}
