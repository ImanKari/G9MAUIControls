using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using G9MAUIControls.Controls;
using G9MAUIControls.Hosting;
using G9MAUIControls.Icons;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;

namespace G9MAUIControls.BottomSheet;

/// <summary>
///     The shared "pick from a list" bottom-sheet body behind
///     <c>ShowListBottomSheetAsync</c>: an optional search box, a flat
///     borderless row list, single or multiple selection, and an Apply button that only appears when
///     multi-select makes one necessary.
///     <para>
///         Rows are deliberately <b>flat and borderless</b> — 52 dp tall, inset 16 dp, tint-only
///         selection, no divider — the same recipe <c>G9SelectionSheet</c> uses, so a picker looks
///         the same wherever it is opened from.
///     </para>
///     <para>
///         <b>Selection is matched by identity, not reference.</b> Filtering rebuilds the visible
///         collection on every keystroke; comparing items by reference would drop the user's
///         selection each time. <see cref="G9BottomSheetListItem.SelectionIdentity" /> is the key
///         (see <see cref="IG9SelectionIdentity" />), which is also what lets a caller pass
///         freshly-constructed items as the initial selection.
///     </para>
/// </summary>
public partial class G9BottomSheetListPickerModal : Grid, IG9BottomSheetAwareView, IDeferredContentReadiness
{
    #region Fields And Properties

    /// <summary>Row height and inset come from the shared selection-row metrics.</summary>
    private const double ListItemEstimatedHeight = 54;

    [AutoBindable] private string _applyButtonText = G9Strings.Get(G9StringKey.Save);
    [AutoBindable] private bool _showApplyButton;

    /// <summary>Whether the search row is shown. Driven by the caller passing a placeholder.</summary>
    [AutoBindable] private bool _showSearch;

    /// <summary>Placeholder for the optional search box.</summary>
    [AutoBindable] private string _searchPlaceholder = string.Empty;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay))]
    private G9BottomSheetListItem? _selectedItem;

    private readonly HashSet<object?> _initialSelectionIdentities;
    private readonly ObservableCollection<G9BottomSheetListItem> _items;

    // The FULL list, kept separate from the bound collection. Filtering mutates `_items` in place so
    // the CollectionView keeps its scroll offset and recycled rows instead of rebuilding, and
    // re-widening a search has to restore from something the filter never touched.
    private readonly IReadOnlyList<G9BottomSheetListItem> _allItems;
    private readonly bool _allowMultipleSelection;
    private readonly bool _closeOnSingleSelection;
    private bool _isSingleSelectionClosing;
    private bool _isApplyingInitialSelection;
    private bool _isCompleted;

    // Holds the covered reveal until the list is populated, so the picker appears filled rather
    // than blinking from empty to filled.
    private readonly DeferredContentReadinessSignal _readiness = new();

    #endregion

    /// <inheritdoc />
    bool IDeferredContentReadiness.IsContentReady => _readiness.IsReady;

    /// <inheritdoc />
    event EventHandler? IDeferredContentReadiness.ContentReady
    {
        add => _readiness.Ready += value;
        remove => _readiness.Ready -= value;
    }

    #region Methods

    /// <summary>Builds the picker body.</summary>
    /// <param name="title">
    ///     Unused by the body itself — the shared sheet header renders it. Kept in the signature
    ///     because the helper passes it and callers read the parameter as "the picker's title".
    /// </param>
    /// <param name="items">Every selectable row.</param>
    /// <param name="selectedItems">Rows to pre-select, matched by identity.</param>
    /// <param name="allowMultipleSelection">Multi-select, which also reveals the Apply button.</param>
    /// <param name="closeOnSingleSelection">
    ///     Single-select only: close the sheet as soon as a row is tapped, instead of waiting for a
    ///     confirm. Ignored when <paramref name="allowMultipleSelection" /> is true.
    /// </param>
    /// <param name="searchPlaceholder">
    ///     Non-empty to show the search box. Opt-in on purpose: a short list does not need one.
    /// </param>
    public G9BottomSheetListPickerModal(
        string title,
        IEnumerable<G9BottomSheetListItem> items,
        IEnumerable<G9BottomSheetListItem>? selectedItems,
        bool allowMultipleSelection,
        bool closeOnSingleSelection,
        string? searchPlaceholder = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        _ = title;

        InitializeComponent();
        BindingContext = this;

        ShowApplyButton = allowMultipleSelection;

        _allowMultipleSelection = allowMultipleSelection;
        _closeOnSingleSelection = !allowMultipleSelection && closeOnSingleSelection;
        _allItems = items.ToList();
        _items = new ObservableCollection<G9BottomSheetListItem>(_allItems);

        SearchPlaceholder = searchPlaceholder ?? string.Empty;
        ShowSearch = !string.IsNullOrWhiteSpace(searchPlaceholder);
        _initialSelectionIdentities = selectedItems is null
            ? []
            : selectedItems.Select(static item => item.SelectionIdentity).ToHashSet();

        PickerList.ItemTemplate = CreateItemTemplate();
        PickerList.SelectionMode = allowMultipleSelection ? SelectionMode.Multiple : SelectionMode.Single;
        PickerList.ItemsSource = _items;
        PickerList.SelectionChanged += OnSelectionChanged;

        // Wired in code, not XAML: G9SearchEntry exposes DebouncedTextChanged (a 250 ms-debounced
        // EventHandler<string?>), NOT a plain TextChanged. Bound in XAML that only fails at
        // XamlC/publish time and never in Debug. The entry is our own child, so its lifetime is
        // ours and no unsubscribe is needed.
        PickerSearchEntry.DebouncedTextChanged += OnSearchTextChanged;

        Loaded += OnLoaded;
    }

    /// <inheritdoc />
    public IG9BottomSheetHandle G9BottomSheetHandle { get; set; } = G9BottomSheetHelper.InitG9BottomSheet();

    /// <summary>
    ///     Raised once, with the final selection, when the picker finishes — whether by Apply, by
    ///     the header's close button, or by a single-select tap that auto-closes.
    /// </summary>
    public event EventHandler<IReadOnlyList<G9BottomSheetListItem>>? Completed;

    [RelayCommand]
    private void Close()
    {
        CompleteFromClose();
        G9BottomSheetHandle.Close();
    }

    [RelayCommand]
    private void Apply()
    {
        CompleteFromClose();
        G9BottomSheetHandle.Close();
    }

    /// <summary>
    ///     Publishes the current selection without closing. The helper calls this when the sheet is
    ///     dismissed by something other than the buttons (hardware back, overlay tap), so a
    ///     dismissal still returns what the user had picked.
    /// </summary>
    public void CompleteFromClose() => Complete(CollectSelectionSnapshot());

    private void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;

        // Applied after the first layout so the CollectionView already has a viewport and can place
        // a pre-selected row without a visible jump.
        ApplyInitialSelection();
        _readiness.MarkReady();
    }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isApplyingInitialSelection || _allowMultipleSelection)
        {
            return;
        }

        var item = PickerList.SelectedItem as G9BottomSheetListItem;
        SelectedItem = item;

        if (item is null || !_closeOnSingleSelection || _isSingleSelectionClosing)
        {
            return;
        }

        _isSingleSelectionClosing = true;

        // Let the selected-row tint become visible before the sheet starts closing; closing on the
        // same frame reads as "did my tap register?".
        await Task.Delay(120).ConfigureAwait(true);
        Complete([item]);
        G9BottomSheetHandle.Close();
    }

    /// <summary>
    ///     Filters the visible rows as the user types.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Mutates <c>_items</c> IN PLACE rather than assigning a new <c>ItemsSource</c>: the
    ///         CollectionView diffs an observable collection and keeps its scroll position and row
    ///         recycling, whereas re-binding rebuilds the whole list on every keystroke.
    ///     </para>
    ///     <para>
    ///         The match is a plain culture-aware case-insensitive contains on the display text — a
    ///         picker search is a "find the row whose name I already know" affordance, not a ranked
    ///         search.
    ///     </para>
    /// </remarks>
    private void OnSearchTextChanged(object? sender, string? text)
    {
        var query = (text ?? string.Empty).Trim();

        var matches = query.Length == 0
            ? _allItems
            : _allItems
                .Where(item => item.Text.Contains(query, StringComparison.CurrentCultureIgnoreCase))
                .ToList();

        // Rebuild only when the result actually differs, so holding a key down does not churn the
        // list (and does not drop selection for a keystroke that changed nothing).
        if (matches.Count == _items.Count && matches.Zip(_items).All(pair => ReferenceEquals(pair.First, pair.Second)))
        {
            return;
        }

        var previouslySelected = CollectSelectionSnapshot();

        _items.Clear();
        foreach (var item in matches)
        {
            _items.Add(item);
        }

        RestoreSelection(previouslySelected);
    }

    /// <summary>
    ///     One flat, borderless row: optional glyph, optional bitmap, label.
    ///     <para>
    ///         Selection is painted by a <see cref="VisualStateManager" /> tint on the row itself
    ///         rather than left to the platform, because the native CollectionView selection visual
    ///         differs on every target (a full-bleed accent fill on Windows, a ripple-coloured
    ///         highlight on Android, nothing at all in some iOS configurations). A tint we draw is
    ///         the only way the four platforms agree.
    ///     </para>
    /// </summary>
    private static DataTemplate CreateItemTemplate()
    {
        return new DataTemplate(() =>
        {
            var palette = G9Palette.Current;

            var glyph = new G9IconView
            {
                Color = palette.OnSurfaceVariant,
                Size = 20,
                VerticalOptions = LayoutOptions.Center
            };
            glyph.SetBinding(
                G9IconView.IconProperty,
                static (G9BottomSheetListItem item) => item.Icon);
            glyph.SetBinding(
                IsVisibleProperty,
                static (G9BottomSheetListItem item) => item.HasIcon);

            var image = new Image
            {
                HeightRequest = 20,
                WidthRequest = 20,
                VerticalOptions = LayoutOptions.Center
            };
            image.SetBinding(
                Image.SourceProperty,
                static (G9BottomSheetListItem item) => item.ResolvedIconSource);
            image.SetBinding(
                IsVisibleProperty,
                static (G9BottomSheetListItem item) => item.HasResolvedIconSource);

            var label = new Label
            {
                FontSize = 14,
                LineBreakMode = LineBreakMode.TailTruncation,
                MaxLines = 1,
                TextColor = palette.OnSurface,
                VerticalTextAlignment = TextAlignment.Center
            };
            label.SetBinding(
                Label.TextProperty,
                static (G9BottomSheetListItem item) => item.Text);

            var row = new Border
            {
                Padding = new Thickness(G9Metrics.SelectionRowHorizontalPadding, 0),
                MinimumHeightRequest = G9Metrics.SelectionRowHeight,
                BackgroundColor = Colors.Transparent,
                StrokeThickness = 0,
                Content = new HorizontalStackLayout
                {
                    Spacing = 10,
                    VerticalOptions = LayoutOptions.Center,
                    Children = { glyph, image, label }
                }
            };

            var states = new VisualStateGroupList
            {
                new VisualStateGroup
                {
                    Name = "CommonStates",
                    States =
                    {
                        new VisualState { Name = "Normal" },
                        new VisualState
                        {
                            Name = "Selected",
                            Setters =
                            {
                                new Setter
                                {
                                    Property = BackgroundColorProperty,
                                    Value = palette.PrimaryContainer
                                }
                            }
                        }
                    }
                }
            };
            VisualStateManager.SetVisualStateGroups(row, states);

            return row;
        });
    }

    private void ApplyInitialSelection()
    {
        if (_initialSelectionIdentities.Count == 0)
        {
            return;
        }

        RestoreSelection(_items.Where(IsInitiallySelected).ToList());
    }

    /// <summary>
    ///     Writes a selection onto the CollectionView without letting
    ///     <see cref="OnSelectionChanged" /> mistake it for a user tap — which, in
    ///     close-on-single-selection mode, would close the sheet the instant it opened.
    /// </summary>
    private void RestoreSelection(IReadOnlyList<G9BottomSheetListItem> selection)
    {
        _isApplyingInitialSelection = true;
        try
        {
            if (!_allowMultipleSelection)
            {
                var single = selection.FirstOrDefault(item => _items.Contains(item));
                PickerList.SelectedItem = single;
                SelectedItem = single;
                return;
            }

            // CollectionView.SelectedItems is a live collection the control owns; replace its
            // contents rather than the collection itself.
            PickerList.SelectedItems.Clear();
            foreach (var item in selection.Where(item => _items.Contains(item)))
            {
                PickerList.SelectedItems.Add(item);
            }
        }
        finally
        {
            _isApplyingInitialSelection = false;
        }
    }

    private bool IsInitiallySelected(G9BottomSheetListItem item) =>
        _initialSelectionIdentities.Contains(item.SelectionIdentity);

    private IReadOnlyList<G9BottomSheetListItem> CollectSelectionSnapshot()
    {
        if (_allowMultipleSelection)
        {
            return PickerList.SelectedItems
                .OfType<G9BottomSheetListItem>()
                .Distinct()
                .ToList();
        }

        var selected = SelectedItem ?? PickerList.SelectedItem as G9BottomSheetListItem;
        return selected is null ? [] : [selected];
    }

    private void Complete(IReadOnlyList<G9BottomSheetListItem> selectedItems)
    {
        if (_isCompleted)
        {
            return;
        }

        _isCompleted = true;
        Completed?.Invoke(this, selectedItems);
    }

    #endregion
}
