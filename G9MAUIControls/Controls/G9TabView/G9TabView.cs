using G9MAUIControls.Helpers;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

/// <summary>
///     Modern segmented tab view with two layout modes:
///     <list type="bullet">
///         <item><b>Fixed</b> — tabs distribute equally across the full bar width. Best
///         for 2–4 tabs with short labels. Material 3 "primary tabs" pattern.</item>
///         <item><b>Scrollable</b> — tabs auto-size and the bar scrolls horizontally
///         when total width exceeds the viewport. Edge fades hint at off-screen tabs.
///         Material 3 "secondary tabs" pattern, also matches iOS scrollable segmented
///         controls.</item>
///     </list>
///     <para>
///         <b>Visual:</b> a single rounded "pill" container (the bar) holds an inner row
///         of tab cells. A floating sliding pill sits behind the active cell — it
///         interpolates BOTH X and width together when selection changes, so the
///         transition glides smoothly even when adjacent tabs have different widths.
///         The bar is visually self-contained; the content panel below it has its own
///         border so the two read as connected segments of the same control.
///     </para>
///     <para>
///         <b>Animation architecture (destruction-free):</b> like G9ChipGroup, the cell
///         widgets (Label + icon View + badge) are built once and never recreated. Color
///         changes happen by mutating <see cref="Label.TextColor" /> /
///         <see cref="G9IconView.Color" /> on existing instances. The pill animates by
///         driving <see cref="VisualElement.TranslationX" /> + <see cref="VisualElement.WidthRequest" />
///         in lockstep. No platform-handler re-init flicker, no brush type swaps.
///     </para>
///     <para>
///         <b>RTL:</b> the tab bar is locked to LTR FlowDirection so pill X always means
///         "pixels from physical left." Visual order is mirrored by reversing the items
///         iteration order when populating cells. Cell content (HorizontalStackLayout)
///         inherits FlowDirection from the page so emoji + text + badge render in the
///         correct reading order inside each cell.
///     </para>
///     // TODO (palette step): pill / inactive / badge colors will move to G9Palette.
/// </summary>
[ContentProperty(nameof(Items))]
public partial class G9TabView : G9ControlBase
{
    private readonly Grid _root;
    private readonly Border _barFrame;
    private readonly Grid _barInner;
    private readonly ScrollView _barScroll;
    private readonly Grid _cellsAndPillHost;
    private readonly Border _pill;
    /// <summary>
    ///     Top horizontal rule painted across the bar in <c>G9TabStyle.Underlined</c>.
    ///     Hidden in <c>Pill</c> style (the rounded bar Border draws its own outline instead).
    /// </summary>
    private readonly BoxView _topRule;
    /// <summary>
    ///     Bottom horizontal rule painted across the bar in <c>G9TabStyle.Underlined</c>.
    ///     The animated underline (<see cref="_pill"/> reshaped to a thin bar) sits on top of
    ///     this for the active cell's segment.
    /// </summary>
    private readonly BoxView _bottomRule;
    private readonly GraphicsView _leadingFade;
    private readonly GraphicsView _trailingFade;
    private readonly TabFadeDrawable _leadingFadeDrawable = new();
    private readonly TabFadeDrawable _trailingFadeDrawable = new();
    private readonly Border _contentBorder;
    private readonly Grid _contentHost;
    private ObservableCollection<G9TabItem>? _attachedItems;
    private readonly List<TabCell> _cells = [];
    /// <summary>
    ///     Stable BoxView instances for the vertical "|" separators between consecutive cells
    ///     in <c>G9TabStyle.Underlined</c>. Rebuilt on every <see cref="RebuildAll"/> alongside
    ///     the cells; hidden / removed in <c>Pill</c> style.
    /// </summary>
    private readonly List<BoxView> _separators = [];
    private int _previousVisualIndex = -1;
    private double _animatedPillX;
    private double _animatedPillWidth;
    private bool _pillAnimating;

    [AutoBindable(OnChanged = nameof(OnItemsChanged))]
    private ObservableCollection<G9TabItem>? _items;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnSelectedIndexChanged))]
    private int _selectedIndex;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _tabHeight;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _tabBarBackground;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _indicatorColor;
    [AutoBindable(OnChanged = nameof(OnLayoutModeChanged))] private G9TabMode _mode;
    [AutoBindable(OnChanged = nameof(OnBarPositionChanged))] private G9TabBarPosition _barPosition;

    /// <summary>
    ///     Visual treatment of the bar and selection indicator. Default is
    ///     <see cref="G9TabStyle.Underlined"/> — flat tabs with a primary-coloured
    ///     bottom underline that animates between cells, separated by a 1 dp <c>|</c>,
    ///     between two thin horizontal rules. Set to <see cref="G9TabStyle.Pill"/>
    ///     for the legacy rounded segmented-control look.
    ///     <para>
    ///         Switching styles also changes the visual side of the active text colour
    ///         contract: <c>Underlined</c> uses the regular <c>OnSurface</c> token for
    ///         active text (with the underline carrying the highlight colour), while
    ///         <c>Pill</c> uses <c>OnPrimaryContainer</c> to read against the active
    ///         pill fill. The <see cref="IndicatorColor"/> override still applies — it
    ///         tints the underline in <c>Underlined</c> and the active text/icon in
    ///         <c>Pill</c>.
    ///     </para>
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnStyleChanged))] private G9TabStyle _style;

    /// <summary>
    ///     When true the tab view renders ONLY the bar — no content panel, no spacer
    ///     row. Use this when the bar drives an external content host (a sibling
    ///     ScrollView, a CarouselView, etc.) and the consumer reacts to
    ///     <see cref="SelectionChanged" /> to update that host. The tab items'
    ///     <see cref="G9TabItem.TabContent" /> values are ignored in this mode.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnHeaderOnlyChanged))] private bool _headerOnly;

    /// <summary>
    ///     When false the content area paints no border / background — useful for full
    ///     screen tab hosts. The bar's own segmented style still renders.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _showFrame;

    /// <summary>
    ///     Corner radius of the content panel. Independent from the bar radius so the
    ///     two can be tuned separately.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _frameCornerRadius;

    /// <summary>
    ///     Padding applied inside the content panel (around the active tab's content).
    ///     Defaults to <c>20</c> on every edge — the standard inset for framed,
    ///     card-style tab content. Set to <c>0</c> for full-bleed content (e.g. a map
    ///     that should fill the panel edge-to-edge); the hosted content then supplies
    ///     its own padding/margins. Bind a <see cref="Thickness" /> for asymmetric insets.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnContentPaddingChanged))] private Thickness _contentPadding;

    /// <summary>
    ///     When <c>false</c> (the default) each tab's <see cref="G9TabItem.TabContent" /> is
    ///     realized <b>lazily</b>: it is only attached to the visual tree the first time its tab
    ///     becomes active, with a brief spinner shown across a one-frame yield so the heavy native
    ///     view-tree realization happens OFF the open / tab-switch animation frame (no 1–2s freeze
    ///     on sheets with virtualized lists / charts). After first activation the content stays
    ///     attached and tab switches just toggle visibility, so scroll / focus state is preserved.
    ///     Set <c>true</c> to build every tab eagerly up front (legacy behaviour) for the rare
    ///     screen that needs all tabs measured / loaded immediately.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnEagerContentChanged))] private bool _eagerContent;

    private Microsoft.Maui.Controls.ActivityIndicator? _contentSpinner;
    private int _pendingLazyRealizeIndex = -1;

    public G9TabView()
    {
        // ── Sliding pill (sits behind the active cell). Stable instance — we mutate
        // its TranslationX / WidthRequest per animation frame, never replace it.
        // Stroke is a stable instance too; its colors are themed in OnApplyVisuals.
        // No Shadow — the app is shadow-free by policy (see G9Controls.md); the
        // active state is carried by the PrimaryContainer fill + the primary-tinted ring.
        _pill = new Border
        {
            HeightRequest = TabHeight - (G9Metrics.TabBarInnerPadding * 2) - (G9Metrics.TabPillVerticalInset * 2),
            StrokeThickness = 1,
            StrokeShape = G9Colors.Round(G9Metrics.TabPillRadius),
            BackgroundColor = Colors.Transparent,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start,
            InputTransparent = true
        };

        // ── Inner cells host. Holds the cells AND the pill in a single Grid so the
        // pill's TranslationX is in the same coordinate space as cell.X.
        _cellsAndPillHost = new Grid
        {
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            ColumnSpacing = 0
        };

        // ── ScrollView for Scrollable mode. In Fixed mode we set HorizontalScrollBar
        // and bar layout to disable scrolling.
        _barScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            FlowDirection = FlowDirection.LeftToRight,
            Content = _cellsAndPillHost
        };
        _barScroll.Scrolled += (_, _) => UpdateFadeOverlays();

        // ── Edge fade overlays (Scrollable mode only). InputTransparent so they don't
        // block taps.
        _leadingFadeDrawable.IsLeading = true;
        _trailingFadeDrawable.IsLeading = false;
        _leadingFade = new GraphicsView
        {
            Drawable = _leadingFadeDrawable,
            WidthRequest = TabFadeWidth,
            HorizontalOptions = LayoutOptions.Start,
            InputTransparent = true,
            IsVisible = false
        };
        _trailingFade = new GraphicsView
        {
            Drawable = _trailingFadeDrawable,
            WidthRequest = TabFadeWidth,
            HorizontalOptions = LayoutOptions.End,
            InputTransparent = true,
            IsVisible = false
        };

        // ── Inner Grid: holds the scroll OR direct row depending on mode, plus the
        // edge fade overlays painted on top.
        // Top + bottom horizontal rules sit ABOVE the cells host so the underline
        // indicator (the reshaped _pill) can paint OVER the bottom rule for the
        // active cell. Both rules are visible only in `G9TabStyle.Underlined`;
        // they're hidden in `Pill` so the rounded outer frame draws the chrome.
        _topRule = new BoxView
        {
            HeightRequest = G9Metrics.TabBarEdgeLineThickness,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            InputTransparent = true,
            IsVisible = false
        };
        _bottomRule = new BoxView
        {
            HeightRequest = G9Metrics.TabBarEdgeLineThickness,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.End,
            InputTransparent = true,
            IsVisible = false
        };

        // ── Inner Grid: holds the scroll OR direct row depending on mode, plus the
        // edge fade overlays painted on top.
        // Z-order matters: the rules are added FIRST so they paint underneath
        // _barScroll, which means the underline indicator (_pill, inside the scroll)
        // can paint OVER the bottom rule for the active cell's segment. Cells
        // themselves are background-less, so they don't visually cover either rule.
        _barInner = new Grid
        {
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Thickness(G9Metrics.TabBarInnerPadding),
            Children = { _topRule, _bottomRule, _barScroll, _leadingFade, _trailingFade }
        };

        // Bar frame — the outer rounded "segmented control" container.
        _barFrame = new Border
        {
            StrokeThickness = 1,
            StrokeShape = G9Colors.Round(G9Metrics.TabBarRadius),
            HeightRequest = G9Metrics.TabBarHeight,
            Content = _barInner,
            FlowDirection = FlowDirection.LeftToRight
        };

        // Place the pill INSIDE the cells host so its TranslationX shares a coordinate
        // space with cell.X. Pill goes in first so it renders behind the cells (z-order
        // = visual-tree order in MAUI when no ZIndex is set).
        _cellsAndPillHost.Children.Add(_pill);

        // ── Content panel (below the bar).
        _contentHost = new Grid();
        _contentBorder = new Border
        {
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(0) },
            Padding = new Thickness(G9Metrics.TabContentDefaultPadding),
            Content = _contentHost
        };

        _root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(new GridLength(G9Metrics.TabBarToContentGap, GridUnitType.Absolute)),
                new RowDefinition(GridLength.Star)
            }
        };
        _root.Add(_barFrame, 0, 0);
        _root.Add(_contentBorder, 0, 2);

        Content = _root;

        Items = [];
        TabHeight = G9Metrics.TabBarHeight;
        // Default visual is the new flat / underlined look (the app designer's preferred
        // tab style for every screen). The content panel has no frame and no rounding,
        // and uses the app-wide page-edge body margin (`G9LayoutMetrics.BodyElementMargin`
        // = 10/10/10/0) as its default padding so list cards / form fields don't touch
        // the screen edges and the first row sits just below the bottom rule. The bottom
        // is intentionally 0 so scrollable lists / CollectionViews reach the end of the
        // page naturally — consumers add bottom padding (or rely on a footer/safe-area)
        // when they need it. Set `ContentPadding="0"` to opt into full-bleed (map tabs).
        // Consumers who want the legacy rounded "pill" segmented control opt in by
        // setting `Style = G9TabStyle.Pill` and (typically) the matching
        // ShowFrame=true / FrameCornerRadius=16 / ContentPadding=20 trio.
        Style = G9TabStyle.Underlined;
        ShowFrame = false;
        FrameCornerRadius = 0;
        ContentPadding = G9LayoutMetrics.BodyElementMargin;
        // Default to Fixed (equal full-width tabs). App convention: tab views are full-width
        // unless a screen explicitly opts into the auto-width Scrollable mode for 5+ tabs.
        Mode = G9TabMode.Fixed;
        BarPosition = G9TabBarPosition.Top;
        HeaderOnly = false;
        // Parent the cells host for the resolved mode NOW. `Mode` was just assigned its own
        // default value, so its OnChanged callback did not fire (same reason `Style` needs the
        // explicit ApplyBarToContentSpacer call below) — without this the first RebuildAll would
        // measure the cells inside the scroll view and hand the pill a stale width.
        ApplyLayoutMode();
        // The underline is sized from the active cell, so it must be re-snapped whenever the bar
        // is re-laid-out at a different width (sheet resize, rotation, a late reparent). Skipped
        // while the indicator is animating — the animation owns the pill for those 240 ms and
        // finishes on the correct target itself.
        _cellsAndPillHost.SizeChanged += OnCellsHostSizeChanged;
        ApplyBarPosition();
        // Resolve the bar→content spacer for the current style. Setting `Style` to its
        // already-default value (Underlined) doesn't fire the OnChanged callback, so the
        // root grid's initial 8 dp spacer (constructed before Style is assigned) wouldn't
        // be reset to 0 for the Underlined default. Call the helper directly so the
        // spacer matches the resolved style on first render.
        ApplyBarToContentSpacer();
    }

    private const double TabFadeWidth = 24;
    private const string PillAnimationName = "AppTabPill";

    // Delay before a lazily-realized tab's heavy content is attached, so the host's resize /
    // pill animation settles first and the one-frame native realization doesn't blink the resize.
    private const int LazyContentRealizeDelayMs = 240;

    public event EventHandler<int>? SelectionChanged;

    // ─── Lifecycle plumbing ──────────────────────────────────────────────────────────

    private void OnVisualChanged() => RequestVisualUpdate();
    private void OnLayoutModeChanged() { ApplyLayoutMode(); RebuildAll(); }
    private void OnBarPositionChanged() => ApplyBarPosition();
    private void OnHeaderOnlyChanged() => ApplyHeaderOnly();
    private void OnEagerContentChanged() => RebuildAll();

    /// <summary>
    ///     Style flips change every visual layer of the bar (background, outer border,
    ///     inner padding, indicator shape/position, separator visibility, top/bottom
    ///     rules) AND reorganise the inner cells host (separators are interleaved
    ///     columns in <c>Underlined</c>, absent in <c>Pill</c>). A full
    ///     <see cref="RebuildAll"/> rebuilds the cells + columns + separators in one
    ///     pass; <see cref="OnVisualChanged"/> re-runs <see cref="OnApplyVisuals"/>
    ///     so the per-style colour / shape / shadow contract is reapplied.
    /// </summary>
    private void OnStyleChanged()
    {
        ApplyBarToContentSpacer();
        RebuildAll();
        OnVisualChanged();
    }

    /// <summary>
    ///     Pushes <see cref="ContentPadding" /> onto the content panel. Guarded against
    ///     the bindable setter firing before <c>_contentBorder</c> is constructed.
    /// </summary>
    private void OnContentPaddingChanged()
    {
        if (_contentBorder is null) return;
        _contentBorder.Padding = ContentPadding;
    }

    /// <summary>
    ///     Toggles the visibility of the content panel. When <see cref="HeaderOnly" />
    ///     is true the content row is collapsed (Height=0) and the content border is
    ///     hidden — only the bar renders. The consumer then drives an external content
    ///     host via <see cref="SelectionChanged" />.
    /// </summary>
    private void ApplyHeaderOnly()
    {
        if (HeaderOnly)
        {
            // Collapse the spacer + content rows so the tab view's measured height is
            // just the bar. Putting the bar in row 0 (Auto) regardless of BarPosition
            // makes sense in header-only mode — there's no content to be "below."
            _root.RowDefinitions[0].Height = GridLength.Auto;
            _root.RowDefinitions[1].Height = new GridLength(0);
            _root.RowDefinitions[2].Height = new GridLength(0);
            Grid.SetRow(_barFrame, 0);
            Grid.SetRow(_contentBorder, 2);
            _contentBorder.IsVisible = false;
        }
        else
        {
            ApplyBarToContentSpacer();
            _contentBorder.IsVisible = true;
            ApplyBarPosition();
        }
    }

    /// <summary>
    ///     Resolves the spacer row between the bar and the content panel based on the
    ///     current <see cref="Style"/>. The <c>Pill</c> bar is a self-contained
    ///     segmented control with its own rounded outline, so a small visual gap
    ///     (<see cref="G9Metrics.TabBarToContentGap"/> = 8 dp) separates it from
    ///     the framed content panel beneath. The <c>Underlined</c> bar instead has a
    ///     bottom rule + animated underline that should sit directly above the content
    ///     — any spacer there reads as a "broken" separator. So in <c>Underlined</c>
    ///     the spacer row collapses to 0; in <c>Pill</c> it stays at the legacy 8 dp.
    ///     The user reported this as a "small gap between the bottom green border and
    ///     the body" on the Map tab; this method is the fix and must NOT be collapsed
    ///     back to a hard-coded <c>TabBarToContentGap</c> assignment.
    /// </summary>
    private void ApplyBarToContentSpacer()
    {
        if (HeaderOnly)
        {
            // Header-only mode owns the spacer (collapses everything to bar height).
            return;
        }

        var spacer = Style == G9TabStyle.Underlined
            ? new GridLength(0)
            : new GridLength(G9Metrics.TabBarToContentGap, GridUnitType.Absolute);

        if (_root.RowDefinitions[1].Height != spacer)
        {
            _root.RowDefinitions[1].Height = spacer;
        }
    }

    /// <summary>
    ///     Re-arranges the root grid so the bar sits above (default) or below the
    ///     content panel.
    ///     <para>
    ///         The fix here is to flip the row <b>sizes</b> too — not just which child
    ///         sits in which row. Row 0 should always be Auto (the bar is Auto-height,
    ///         the content is Star). If we just swap Grid.Row, the bar lands in the
    ///         Star-sized row and gets stretched to the full available height,
    ///         producing the long gap between the panel and the bar that you saw.
    ///     </para>
    /// </summary>
    private void ApplyBarPosition()
    {
        if (BarPosition == G9TabBarPosition.Bottom)
        {
            _root.RowDefinitions[0].Height = GridLength.Star;
            _root.RowDefinitions[2].Height = GridLength.Auto;
            Grid.SetRow(_contentBorder, 0);
            Grid.SetRow(_barFrame, 2);
        }
        else
        {
            _root.RowDefinitions[0].Height = GridLength.Auto;
            _root.RowDefinitions[2].Height = GridLength.Star;
            Grid.SetRow(_barFrame, 0);
            Grid.SetRow(_contentBorder, 2);
        }
    }

    private void OnItemsChanged()
    {
        DetachItems();
        _attachedItems = Items;
        if (_attachedItems is not null)
        {
            _attachedItems.CollectionChanged += OnItemsCollectionChanged;
            foreach (var item in _attachedItems)
            {
                item.VisualChanged += OnItemVisualChanged;
            }
        }

        RebuildAll();
    }

    private void DetachItems()
    {
        if (_attachedItems is null) return;
        _attachedItems.CollectionChanged -= OnItemsCollectionChanged;
        foreach (var item in _attachedItems) item.VisualChanged -= OnItemVisualChanged;
        _attachedItems = null;
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null) foreach (G9TabItem item in e.OldItems) item.VisualChanged -= OnItemVisualChanged;
        if (e.NewItems is not null) foreach (G9TabItem item in e.NewItems) item.VisualChanged += OnItemVisualChanged;
        RebuildAll();
    }

    /// <summary>
    ///     A property changed on an existing item (text, badge count, icon). Update the
    ///     existing widgets in-place rather than rebuilding the whole tab bar — a
    ///     full rebuild on every text-update would tear down all platform handlers and
    ///     cause flicker.
    /// </summary>
    private void OnItemVisualChanged(object? sender, EventArgs e)
    {
        if (sender is not G9TabItem changed) return;

        // Same thread contract as RebuildAll — this reads _cells and then mutates the visual tree.
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.Dispatch(() => OnItemVisualChanged(sender, e));
            return;
        }

        // `c is not null` is not redundant: see RebuildAll for how a concurrent Clear() can expose a
        // nulled slot to an in-flight enumerator. Defence in depth behind the thread guard above.
        var matchingCell = _cells.FirstOrDefault(c => c is not null && ReferenceEquals(c.Item, changed));
        if (matchingCell is null)
        {
            // Cell doesn't exist yet (item hasn't been added to the bar). RebuildAll
            // will pick it up.
            RebuildAll();
            return;
        }
        UpdateCellContent(matchingCell);
        ApplyTabStyles();
    }

    // ─── Layout mode switching ───────────────────────────────────────────────────────

    private void ApplyLayoutMode()
    {
        if (Mode == G9TabMode.Scrollable)
        {
            // Cells auto-size, bar scrolls if total > viewport.
            AttachCellsHost(scrollable: true);
            _cellsAndPillHost.HorizontalOptions = LayoutOptions.Start;
            _leadingFade.IsVisible = false;
            _trailingFade.IsVisible = false;
            UpdateFadeOverlays();
        }
        else
        {
            // Fixed: cells fill the bar width equally.
            AttachCellsHost(scrollable: false);
            _cellsAndPillHost.HorizontalOptions = LayoutOptions.Fill;
            _leadingFade.IsVisible = false;
            _trailingFade.IsVisible = false;
        }
    }

    /// <summary>
    ///     Parents the cells host for the current <see cref="Mode" />.
    ///     <para>
    ///         In <c>Fixed</c> mode the host is placed DIRECTLY in the bar. Routing it through
    ///         <see cref="_barScroll" /> costs ~10 dp of usable width on Android (the platform scroll
    ///         view reserves a scrollbar gutter even with the bars disabled), so every cell — and with
    ///         it the underline, which is sized from the active cell — came out 10 dp short while the
    ///         bar's own rules still spanned the full width. In LTR that hides under the first tab
    ///         (left-flush, the 10 dp missing off the far right edge); in RTL the FIRST tab is the
    ///         RIGHTMOST cell, so the same shortfall reads as "the underline sits a little left and
    ///         leaves a gap at the right edge" — measured at exactly 30 px / 10 dp on a 1344 px
    ///         (3× density) screen. Fixed mode never scrolls, so the scroll view has nothing to offer
    ///         there and is simply left out of the tree.
    ///     </para>
    ///     Idempotent: it runs on every visual apply, and reparenting an already-correct host would
    ///     drop native handlers (and the pill's measured position) for a frame.
    /// </summary>
    private void AttachCellsHost(bool scrollable)
    {
        if (scrollable)
        {
            _barInner.Children.Remove(_cellsAndPillHost);
            if (!ReferenceEquals(_barScroll.Content, _cellsAndPillHost))
            {
                _barScroll.Content = _cellsAndPillHost;
            }

            _barScroll.IsVisible = true;
            return;
        }

        if (ReferenceEquals(_barScroll.Content, _cellsAndPillHost))
        {
            _barScroll.Content = null;
        }

        _barScroll.IsVisible = false;

        if (!_barInner.Children.Contains(_cellsAndPillHost))
        {
            // Sits directly above the two rules and below the edge fades — the same z-position the
            // scroll view held, so the underline still paints over the bottom rule.
            var insertAt = Math.Clamp(_barInner.Children.IndexOf(_barScroll) + 1, 0, _barInner.Children.Count);
            _barInner.Children.Insert(insertAt, _cellsAndPillHost);
        }
    }

    // ─── Selection handling ──────────────────────────────────────────────────────────

    private void OnSelectedIndexChanged()
    {
        ApplyTabStyles();
        ShowSelectedContent();
        AnimatePillToSelected();
        ScrollSelectedIntoView(animate: true);
        SelectionChanged?.Invoke(this, SelectedIndex);
    }

    protected override void OnApplyVisuals()
    {
        ApplyVisualsCore(skipTabStyles: false);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Pure palette flips on G9TabView only need bar / pill / fade colours
    ///     refreshed plus the tab cell text colours. The heavy
    ///     <c>ApplyTabStyles</c> is still required (cell colours track the active
    ///     tab) but <c>ApplyLayoutMode</c> and other size-dependent reconciliation
    ///     can be skipped — palette doesn't move tabs around.
    /// </remarks>
    protected override void OnPaletteChanged()
    {
        ApplyVisualsCore(skipTabStyles: false);
    }

    private void ApplyVisualsCore(bool skipTabStyles)
    {
        var palette = G9Palette.Current;

        // Resolve the bar background once: explicit override > style-default. The
        // Underlined style defaults to the page background (transparent) so the
        // top/bottom rules read as standalone hairlines; Pill keeps the
        // SurfaceContainerLow tint that the legacy rounded chrome was designed for.
        var styleDefaultBarBg = Style == G9TabStyle.Underlined
            ? Colors.Transparent
            : palette.SurfaceContainerLow;
        _barFrame.BackgroundColor = TabBarBackground ?? styleDefaultBarBg;
        _barFrame.HeightRequest = TabHeight;

        if (Style == G9TabStyle.Underlined)
        {
            // Flat bar: no rounded outline, no outer stroke. The horizontal rules ARE
            // the chrome. Padding 0 on all sides so the rules touch the bar edges and
            // the separator/indicator math is in the bar's natural coordinate space.
            _barFrame.Stroke = null;
            _barFrame.StrokeThickness = 0;
            _barFrame.StrokeShape = G9Colors.Round(0);
            if (_barInner.Padding.Left != 0 || _barInner.Padding.Top != 0
                || _barInner.Padding.Right != 0 || _barInner.Padding.Bottom != 0)
            {
                _barInner.Padding = new Thickness(0);
            }

            var ruleColor = palette.OutlineVariant.WithAlpha(0.5f);
            _topRule.IsVisible = true;
            _bottomRule.IsVisible = true;
            _topRule.HeightRequest = G9Metrics.TabBarEdgeLineThickness;
            _bottomRule.HeightRequest = G9Metrics.TabBarEdgeLineThickness;
            _topRule.Color = ruleColor;
            _bottomRule.Color = ruleColor;

            // Reshape the existing pill Border into a thin underline. Same instance,
            // same animation pipeline (TranslationX + WidthRequest in lockstep) — only
            // the appearance changes. Heights flip from ~40dp pill to ~2.5dp underline.
            _pill.HeightRequest = G9Metrics.TabUnderlineThickness;
            _pill.VerticalOptions = LayoutOptions.End;
            _pill.StrokeThickness = 0;
            _pill.StrokeShape = G9Colors.Round(0);
            _pill.Stroke = null;
            _pill.BackgroundColor = IndicatorColor ?? palette.Primary;

            // Update separator tint in-place (created in RebuildAll).
            foreach (var sep in _separators)
            {
                sep.Color = ruleColor;
            }
        }
        else
        {
            // Legacy Pill (rounded segmented control). Restore the original
            // 4dp inner padding + rounded outer Border + colored shadow halo.
            _barFrame.Stroke = new SolidColorBrush(palette.OutlineVariant.WithAlpha(0.5f));
            _barFrame.StrokeThickness = 1;
            _barFrame.StrokeShape = G9Colors.Round(G9Metrics.TabBarRadius);
            var pillInnerPadding = new Thickness(G9Metrics.TabBarInnerPadding);
            if (_barInner.Padding != pillInnerPadding)
            {
                _barInner.Padding = pillInnerPadding;
            }

            _topRule.IsVisible = false;
            _bottomRule.IsVisible = false;

            // Active pill — strong tint on a recognizable accent. We use PrimaryContainer
            // (a tonal lighter primary recipe) for the background so the pill clearly stands
            // out against the SurfaceContainerLow bar without screaming the way solid
            // Primary would. Stroke is a 35%-alpha primary tint that wraps the pill in a
            // thin colored ring — with shadows removed app-wide, that ring is the contrast
            // cue that makes the active state pop on busy backgrounds.
            var pillBg = palette.PrimaryContainer;
            var pillStroke = palette.Primary.WithAlpha(0.35f);
            _pill.HeightRequest = TabHeight - (G9Metrics.TabBarInnerPadding * 2) - (G9Metrics.TabPillVerticalInset * 2);
            _pill.VerticalOptions = LayoutOptions.Center;
            _pill.BackgroundColor = pillBg;
            _pill.StrokeThickness = 1;
            _pill.StrokeShape = G9Colors.Round(G9Metrics.TabPillRadius);
            _pill.Stroke = new SolidColorBrush(pillStroke);
        }

        // Fade overlays use the bar's own resolved background as their base so the fade blends
        // seamlessly — picks up the explicit override OR the style default.
        var fadeBase = TabBarBackground ?? styleDefaultBarBg;
        _leadingFadeDrawable.BaseColor = fadeBase;
        _trailingFadeDrawable.BaseColor = fadeBase;
        _leadingFade.Invalidate();
        _trailingFade.Invalidate();

        if (ShowFrame)
        {
            _contentBorder.StrokeThickness = 1;
            _contentBorder.StrokeShape = G9Colors.Round(FrameCornerRadius);
            _contentBorder.BackgroundColor = palette.Surface;
            _contentBorder.Stroke = new SolidColorBrush(palette.OutlineVariant);
        }
        else
        {
            _contentBorder.StrokeThickness = 0;
            _contentBorder.StrokeShape = G9Colors.Round(FrameCornerRadius);
            _contentBorder.BackgroundColor = Colors.Transparent;
            _contentBorder.Stroke = null;
        }

        ApplyLayoutMode();
        if (!skipTabStyles)
        {
            ApplyTabStyles();
        }
        UpdateFadeOverlays();
    }

    // ─── Cell building ───────────────────────────────────────────────────────────────

    /// <summary>
    ///     Stable cell widget set. Built once in <see cref="BuildCell" /> and never
    ///     recreated — we mutate <see cref="Label.TextColor" /> / icon color on these
    ///     instances rather than swapping the View.
    /// </summary>
    private sealed class TabCell
    {
        public required G9TabItem Item { get; init; }
        public required int LogicalIndex { get; init; }
        public required Grid Container { get; init; }
        public required HorizontalStackLayout Row { get; init; }
        public required Label TextLabel { get; init; }
        public View? IconView { get; set; }
        public View? BadgeView { get; set; }
    }

    /// <summary>
    ///     Finds the cell for a logical index without enumerating <c>_cells</c> live.
    /// </summary>
    /// <remarks>
    ///     Indexed rather than LINQ, and null-tolerant, on purpose. <c>List&lt;T&gt;.Clear()</c> nulls
    ///     the backing array slots for a reference element type, so a reader that has already captured
    ///     the old count can walk into a slot that is now null and dereference it. That is the field
    ///     crash this guards: a <c>NullReferenceException</c> raised inside
    ///     <c>_cells.FirstOrDefault(c =&gt; c.LogicalIndex == effective)</c>, where <c>c</c> is the only
    ///     thing that can be null.
    ///     <para>
    ///         <b>The thread guard in <see cref="RebuildAll" /> is the actual fix</b>; this is defence
    ///         in depth, and it also covers the ordinary case where a queued dispatch runs after the
    ///         control has been rebuilt or torn down. Every lookup goes through here so the four call
    ///         sites cannot drift apart again.
    ///     </para>
    /// </remarks>
    private TabCell? FindCell(int logicalIndex)
    {
        for (var i = 0; i < _cells.Count; i++)
        {
            var candidate = _cells[i];
            if (candidate is not null && candidate.LogicalIndex == logicalIndex)
            {
                return candidate;
            }
        }

        return null;
    }

    private void RebuildAll()
    {
        // MUST run on the UI thread, and not only because MAUI forbids touching the visual tree off
        // it: this method calls _cells.Clear(), and List<T>.Clear() nulls the backing array slots for
        // a reference element type. A reader already enumerating _cells on the UI thread — the
        // dispatched PositionPillNow below is exactly that — has captured the OLD count, so it walks
        // into a slot that is now null and dereferences it.
        //
        // Observed in the field as a NullReferenceException inside
        // `_cells.FirstOrDefault(c => c.LogicalIndex == effective)`, where the only thing that can be
        // null is `c` itself. Reachable because RebuildAll runs on whatever thread raised the
        // trigger: OnItemsCollectionChanged fires on the mutating thread, so an ObservableCollection
        // updated from a background worker (data load, teardown) rebuilds off-thread.
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.Dispatch(RebuildAll);
            return;
        }

        // Clear cells AND separators (keep the pill — it's a stable instance we never
        // recreate). The pill's shape/size has already been re-applied for the current
        // Style by ApplyVisualsCore. Separators are always rebuilt because the column
        // structure depends on Style: in Underlined we interleave separator columns
        // between cells; in Pill we don't add them at all.
        for (var i = _cellsAndPillHost.Children.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_cellsAndPillHost.Children[i], _pill))
            {
                _cellsAndPillHost.Children.RemoveAt(i);
            }
        }
        _cellsAndPillHost.ColumnDefinitions.Clear();
        _cells.Clear();
        _separators.Clear();
        _contentHost.Children.Clear();

        var items = Items;
        if (items is null || items.Count == 0)
        {
            _pill.IsVisible = false;
            UpdateFadeOverlays();
            return;
        }

        _pill.IsVisible = true;

        // Build column definitions per layout mode + style.
        //  - Cell column unit:
        //    Fixed      -> *  (cells split the bar width equally)
        //    Scrollable -> Auto (cells size to content; the bar's natural width is
        //                  the sum of cell widths + separators, and the bar scrolls).
        //  - Underlined style additionally inserts a separator column (Auto, ~1 dp)
        //    between every consecutive cell pair so the visual reads as
        //    `cell | cell | cell` (no leading or trailing separator).
        var cellColumnLength = Mode == G9TabMode.Fixed
            ? new GridLength(1, GridUnitType.Star)
            : GridLength.Auto;

        // Visual order: LTR straight, RTL reversed (since the bar is locked to LTR
        // FlowDirection). The pill positioning then uses the same physical-X coordinate
        // system regardless of culture. Separators interleave correctly in both
        // directions because they sit between consecutive PHYSICAL columns, which
        // already match the visual order after the reverse.
        var isRtl = G9Culture.IsRtl;
        var orderedItems = isRtl
            ? items.Select((it, i) => (it, i)).Reverse().ToList()
            : items.Select((it, i) => (it, i)).ToList();

        var useSeparators = Style == G9TabStyle.Underlined;
        var palette = G9Palette.Current;
        var separatorColor = palette.OutlineVariant.WithAlpha(0.5f);
        var separatorHeight = Math.Max(0, TabHeight - (2 * G9Metrics.TabSeparatorVerticalInset));

        // Build the column structure first so cell columns sit at known indices.
        var totalColumns = useSeparators ? Math.Max(0, (orderedItems.Count * 2) - 1) : orderedItems.Count;
        for (var c = 0; c < totalColumns; c++)
        {
            var isCellColumn = !useSeparators || (c % 2 == 0);
            _cellsAndPillHost.ColumnDefinitions.Add(new ColumnDefinition(
                isCellColumn ? cellColumnLength : GridLength.Auto));
        }

        // Pill spans every column so its TranslationX + WidthRequest can land it on
        // any cell regardless of the separator interleave.
        Grid.SetColumnSpan(_pill, totalColumns);
        Grid.SetColumn(_pill, 0);

        for (var i = 0; i < orderedItems.Count; i++)
        {
            var (item, logicalIndex) = orderedItems[i];
            var cell = BuildCell(item, logicalIndex);
            var cellColumnIndex = useSeparators ? i * 2 : i;
            Grid.SetColumn(cell.Container, cellColumnIndex);
            _cellsAndPillHost.Children.Add(cell.Container);
            _cells.Add(cell);

            // Insert a vertical "|" separator BETWEEN cells (not before the first, not
            // after the last). The separator goes in the odd column at index
            // (cellColumnIndex + 1) and renders as a 1 dp box vertically inset so it
            // doesn't touch the top / bottom rules.
            if (useSeparators && i < orderedItems.Count - 1)
            {
                var separator = new BoxView
                {
                    WidthRequest = G9Metrics.TabSeparatorThickness,
                    HeightRequest = separatorHeight,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center,
                    Color = separatorColor,
                    InputTransparent = true
                };
                Grid.SetColumn(separator, cellColumnIndex + 1);
                _cellsAndPillHost.Children.Add(separator);
                _separators.Add(separator);
            }

            // Wire up content panel. Eager mode parents every tab up front (legacy). Lazy mode
            // (default) defers parenting to first activation — see ShowSelectedContent — so the
            // heavy native realization doesn't run on the open frame.
            var content = item.TabContent;
            if (content is not null && EagerContent)
            {
                content.IsVisible = false;
                if (content.Parent is null)
                {
                    _contentHost.Children.Add(content);
                }
            }
        }

        ApplyTabStyles();
        ShowSelectedContent();

        // Defer pill positioning until cells are measured.
        _previousVisualIndex = -1;
        Dispatcher.Dispatch(() =>
        {
            PositionPillNow();
            UpdateFadeOverlays();
            ScrollSelectedIntoView(animate: false);
        });
    }

    private TabCell BuildCell(G9TabItem item, int logicalIndex)
    {
        var palette = G9Palette.Current;

        var label = new Label
        {
            Text = item.Text ?? string.Empty,
            FontSize = G9Metrics.TabFontSize,
            FontAttributes = FontAttributes.Bold,
            TextColor = palette.OnSurfaceVariant,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            InputTransparent = true
        };

        var row = new HorizontalStackLayout
        {
            Spacing = 6,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            FlowDirection = FlowDirection.MatchParent,
            InputTransparent = true
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SelectedIndex = logicalIndex;

        // Cell container fills the inner area of the bar — this matches the
        // indicator's vertical extent so the cell content (icon + label + badge) is
        // visually centred on the chrome, not on a wider gap. The "inner area" depends
        // on style: in Pill the bar has a 4 dp inner padding around the rounded chrome,
        // so the cell sits inside that (TabHeight − 2 × TabBarInnerPadding); in
        // Underlined the bar has zero inner padding (the rules ARE the chrome) so the
        // cell fills the full bar height.
        var cellContainerHeight = Style == G9TabStyle.Underlined
            ? TabHeight
            : TabHeight - (G9Metrics.TabBarInnerPadding * 2);
        var container = new Grid
        {
            Padding = new Thickness(G9Metrics.TabCellHorizontalPadding, 0),
            HeightRequest = cellContainerHeight,
            FlowDirection = FlowDirection.MatchParent,
            Children = { row },
            GestureRecognizers = { tap }
        };

        var cell = new TabCell
        {
            Item = item,
            LogicalIndex = logicalIndex,
            Container = container,
            Row = row,
            TextLabel = label,
            IconView = null,
            BadgeView = null
        };

        UpdateCellContent(cell);
        return cell;
    }

    /// <summary>
    ///     Repopulates a cell's icon / label / badge from its <see cref="G9TabItem" />.
    ///     Called from BuildCell on first build, and again from
    ///     <see cref="OnItemVisualChanged" /> when an item property changes (text,
    ///     badge count, icon). The label instance is reused — only its Text / TextColor
    ///     change. Icon and badge views are recreated when the underlying icon /
    ///     badge type changes (cheap because they're leaves).
    /// </summary>
    private void UpdateCellContent(TabCell cell)
    {
        cell.Row.Children.Clear();
        cell.IconView = null;
        cell.BadgeView = null;

        var palette = G9Palette.Current;

        if (G9IconFactory.HasIcon(cell.Item.Emoji, cell.Item.Icon, cell.Item.ImagePath, cell.Item.ImageSource))
        {
            var icon = G9IconFactory.Create(
                cell.Item.Emoji, cell.Item.Icon, cell.Item.ImagePath, cell.Item.ImageSource,
                palette.OnSurfaceVariant, G9Metrics.TabIconSize);
            cell.IconView = icon;
            cell.Row.Children.Add(icon);
        }

        cell.TextLabel.Text = cell.Item.Text ?? string.Empty;
        cell.Row.Children.Add(cell.TextLabel);

        if (!string.IsNullOrWhiteSpace(cell.Item.BadgeText) || cell.Item.BadgeCount > 0)
        {
            var badge = CreateBadge(string.IsNullOrWhiteSpace(cell.Item.BadgeText)
                ? cell.Item.BadgeCount.ToString("N0")
                : cell.Item.BadgeText!, cell.Item.BadgeColor);
            cell.BadgeView = badge;
            cell.Row.Children.Add(badge);
        }
        else if (cell.Item.BadgeDot)
        {
            var dot = new BoxView
            {
                WidthRequest = 7,
                HeightRequest = 7,
                CornerRadius = 4,
                Color = palette.Error,
                VerticalOptions = LayoutOptions.Center
            };
            cell.BadgeView = dot;
            cell.Row.Children.Add(dot);
        }
    }

    // ─── Tab style application (in-place color updates, no re-creation) ─────────────

    private void ApplyTabStyles()
    {
        var palette = G9Palette.Current;
        var effective = EffectiveIndex;

        // Active text/icon colour depends on the visual style:
        //   - Underlined: active uses the regular OnSurface text token (high-contrast
        //     against any page background) and the underline below carries the
        //     highlight colour. The IndicatorColor override, when set, tints the
        //     underline rather than the text — keeping the active label legible
        //     instead of forcing it onto a possibly-low-contrast accent.
        //   - Pill: active uses OnPrimaryContainer (the matching contrast token to
        //     read against the active pill fill). IndicatorColor here overrides the
        //     active text colour because in this style the pill IS the indicator.
        Color activeColor;
        if (Style == G9TabStyle.Underlined)
        {
            activeColor = palette.OnSurface;
        }
        else
        {
            activeColor = IndicatorColor ?? palette.OnPrimaryContainer;
        }
        var inactiveColor = palette.OnSurfaceVariant;

        foreach (var cell in _cells)
        {
            // Null-tolerant for the same reason FindCell is — see RebuildAll.
            if (cell is null) continue;

            var selected = cell.LogicalIndex == effective;
            var color = selected ? activeColor : inactiveColor;

            cell.TextLabel.TextColor = color;

            // Update icon color in-place using the same destruction-free pattern as
            // G9ChipGroup. Recreating the icon here would cause the brief platform
            // default-color flash we fought hard to eliminate in the chip group.
            switch (cell.IconView)
            {
                case Label emojiLabel:
                    emojiLabel.TextColor = color;
                    break;
                case G9IconView mauiIcon:
                    mauiIcon.Color = color;
                    break;
            }
        }
    }

    private static View CreateBadge(string text, Color? badgeColor = null)
    {
        var palette = G9Palette.Current;
        return new Border
        {
            MinimumWidthRequest = G9Metrics.TabBadgeHeight,
            HeightRequest = G9Metrics.TabBadgeHeight,
            Padding = new Thickness(5, 0),
            StrokeThickness = 0,
            StrokeShape = G9Colors.Round(G9Metrics.RadiusPill),
            BackgroundColor = badgeColor ?? palette.Primary,
            VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = text,
                FontSize = 10,
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.NoWrap
            },
            InputTransparent = true
        };
    }

    // ─── Pill animation ──────────────────────────────────────────────────────────────

    /// <summary>
    ///     Drives the floating pill from its current position / width to the active
    ///     cell's bounds. Both interpolators run inside a single
    ///     <see cref="Animation" /> so X and width stay in lockstep — no visible
    ///     "stretch then slide" decomposition that a simpler TranslateToAsync would
    ///     produce when adjacent cells differ in width.
    /// </summary>
    private void AnimatePillToSelected()
    {
        var effective = EffectiveIndex;
        if (effective < 0 || _cells.Count == 0) return;

        var cell = FindCell(effective);
        if (cell is null) return;

        var targetX = cell.Container.X;
        var targetWidth = cell.Container.Width;
        if (targetWidth <= 0)
        {
            // Cell hasn't been measured yet. Wait for the cell's SizeChanged so we
            // pick up the real width once layout completes, instead of busy-looping
            // through the dispatcher.
            //
            // The previous implementation called <c>Dispatcher.Dispatch(AnimatePillToSelected)</c>
            // here. On WinUI that produced a permanent ~100%-of-one-core spin on an
            // otherwise idle page: the dispatcher re-queues AnimatePillToSelected
            // every pump, but the HeaderOnly tab bar's cell can report Width 0 long
            // enough that the re-dispatch loop never settles, so the UI thread never
            // goes idle. SizeChanged is a one-shot subscription tied to the cell's
            // container; the handler self-unsubscribes after the first non-zero
            // width so a chain of SizeChanged events (initial 0 → measured width)
            // doesn't re-enter <see cref="AnimatePillToSelected" /> for every
            // intermediate value. If the container is disposed before SizeChanged
            // fires the closure simply never runs — no leak, weak via the
            // container's own lifetime.
            EventHandler? handler = null;
            handler = (_, _) =>
            {
                if (cell.Container.Width <= 0) return;
                cell.Container.SizeChanged -= handler;
                AnimatePillToSelected();
            };
            cell.Container.SizeChanged += handler;
            return;
        }

        var fromX = _animatedPillX;
        var fromWidth = _animatedPillWidth;
        if (fromWidth <= 0)
        {
            // First time positioning — snap, no animation (user hasn't seen the
            // initial state yet).
            CommitPill(targetX, targetWidth);
            _previousVisualIndex = effective;
            return;
        }

        _pill.AbortAnimation(PillAnimationName);
        _pillAnimating = true;

        new Animation(t =>
        {
            var x = fromX + ((targetX - fromX) * t);
            var width = fromWidth + ((targetWidth - fromWidth) * t);
            CommitPill(x, width);
        }, 0, 1, Easing.CubicOut)
        .Commit(_pill, PillAnimationName, 16, G9Metrics.TabIndicatorDurationMs, finished: (_, cancelled) =>
        {
            _pillAnimating = false;
            if (cancelled) return;
            CommitPill(targetX, targetWidth);
        });

        _previousVisualIndex = effective;
    }

    /// <summary>
    ///     Re-snaps the underline after the bar has been re-laid-out at a new width. Deferred one
    ///     dispatch so the cells have been arranged inside the resized host before their bounds are
    ///     read back.
    /// </summary>
    private void OnCellsHostSizeChanged(object? sender, EventArgs e)
    {
        if (_pillAnimating) return;
        Dispatcher.Dispatch(PositionPillNow);
    }

    private void CommitPill(double x, double width)
    {
        _pill.WidthRequest = width;
        _pill.TranslationX = x;
        _animatedPillX = x;
        _animatedPillWidth = width;
    }

    /// <summary>
    ///     Snaps the pill to the active cell with no animation. Used on initial layout
    ///     and after rebuild when the previous position is meaningless.
    /// </summary>
    private void PositionPillNow()
    {
        var effective = EffectiveIndex;
        if (effective < 0 || _cells.Count == 0) return;

        var cell = FindCell(effective);
        if (cell is null) return;
        if (cell.Container.Width <= 0)
        {
            // Cell still unmeasured — try once more after a layout pass.
            cell.Container.SizeChanged += OnFirstSize;
            return;
        }

        CommitPill(cell.Container.X, cell.Container.Width);

        void OnFirstSize(object? s, EventArgs e)
        {
            cell.Container.SizeChanged -= OnFirstSize;
            if (cell.Container.Width > 0)
            {
                CommitPill(cell.Container.X, cell.Container.Width);
            }
        }
    }

    // ─── Content visibility + transition ────────────────────────────────────────────

    /// <summary>
    ///     Reveals the content view for the active tab. Uses a directional cross-fade:
    ///     the new content slides in from the side opposite the previous tab so the
    ///     motion feels coherent with the pill's direction. Only 6dp of travel — enough
    ///     to feel alive, not enough to read as a "page swipe."
    /// </summary>
    private void ShowSelectedContent()
    {
        var items = Items;
        if (items is null) return;

        var effective = EffectiveIndex;
        var prev = _previousVisualIndex;

        // Hide every non-active tab that is already realized (parented). Inactive lazy tabs that
        // were never visited stay unparented and cost nothing.
        for (var i = 0; i < items.Count; i++)
        {
            if (i == effective) continue;
            var content = items[i].TabContent;
            if (content is null || content.Parent is null || !content.IsVisible) continue;

            UnfocusDescendantInputs(content);
            content.IsVisible = false;
            content.AbortAnimation("AppTabContent");
            content.TranslationX = 0;
            content.Opacity = 1;
        }

        if (effective < 0 || effective >= items.Count) return;
        var active = items[effective].TabContent;
        if (active is null)
        {
            ShowContentSpinner(false);
            return;
        }

        if (active.Parent is not null)
        {
            // Already realized — reveal immediately with the slide/fade transition.
            ShowContentSpinner(false);
            RevealActiveContent(active, prev, effective);
            return;
        }

        // Lazy first activation: show a spinner now, then realize (parent → native handlers build)
        // AFTER a short delay so the pill move + the host's fit-to-content RESIZE animation finish
        // first — otherwise the heavy one-frame realization runs on top of the resize and the user
        // sees the sheet "jump/blink" while growing. The spinner masks the delay + the build. A
        // pending-index token guards against rapid switches so only the latest selection realizes.
        ShowContentSpinner(true);
        _pendingLazyRealizeIndex = effective;
        _ = RealizeLazyContentAfterDelayAsync(effective, prev);
    }

    private async Task RealizeLazyContentAfterDelayAsync(int effective, int prev)
    {
        // ~ the resize/transition window; long enough for the host resize to settle, short enough
        // that the content doesn't feel slow to appear.
        await Task.Delay(LazyContentRealizeDelayMs).ConfigureAwait(true);

        if (_pendingLazyRealizeIndex != effective) return; // superseded by a newer tap
        _pendingLazyRealizeIndex = -1;

        if (EffectiveIndex != effective) return;
        var stillActive = (Items is { } it && effective < it.Count) ? it[effective].TabContent : null;
        if (stillActive is null) { ShowContentSpinner(false); return; }

        if (stillActive.Parent is null)
        {
            _contentHost.Children.Add(stillActive);
        }

        ShowContentSpinner(false);
        RevealActiveContent(stillActive, prev, effective);
    }

    /// <summary>Reveals an already-parented tab content with the directional slide + fade.</summary>
    private void RevealActiveContent(View content, int prev, int effective)
    {
        ResetScrollAndUnfocus(content);
        content.IsVisible = true;

        // Slide direction: forward (prev < new) arrives from the trailing edge; reverse from the
        // leading edge. RTL mirrors the sign so motion always reads as "next from the reading end".
        var slideFromForward = prev < 0 || effective > prev;
        var direction = slideFromForward ? 1 : -1;
        if (G9Culture.IsRtl) direction = -direction;
        var startX = 6.0 * direction;

        content.AbortAnimation("AppTabContent");
        content.TranslationX = startX;
        content.Opacity = 0;

        new Animation(t =>
        {
            content.TranslationX = startX * (1 - t);
            content.Opacity = t;
        }, 0, 1, Easing.CubicOut)
        .Commit(content, "AppTabContent", 16, G9Metrics.TabContentTransitionMs, finished: (_, cancelled) =>
        {
            if (cancelled) return;
            content.TranslationX = 0;
            content.Opacity = 1;
        });
    }

    /// <summary>Shows/hides a lightweight spinner in the content host during lazy realization.</summary>
    private void ShowContentSpinner(bool show)
    {
        if (!show)
        {
            if (_contentSpinner is not null)
            {
                _contentSpinner.IsRunning = false;
                _contentSpinner.IsVisible = false;
            }
            return;
        }

        _contentSpinner ??= new Microsoft.Maui.Controls.ActivityIndicator
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            HeightRequest = 28,
            WidthRequest = 28,
            Color = G9Palette.Current.Primary
        };

        if (_contentSpinner.Parent is null)
        {
            _contentHost.Children.Add(_contentSpinner);
        }

        _contentSpinner.IsVisible = true;
        _contentSpinner.IsRunning = true;
    }

    private static void ResetScrollAndUnfocus(View root)
    {
        if (root is ScrollView sv)
        {
            try { _ = sv.ScrollToAsync(0, 0, false); } catch { }
        }
        UnfocusDescendantInputs(root);
    }

    private static void UnfocusDescendantInputs(View root)
    {
        if (root is Microsoft.Maui.Controls.IElementController controller)
        {
            foreach (var child in controller.LogicalChildren)
            {
                if (child is InputView input && input.IsFocused)
                {
                    try { input.Unfocus(); } catch { /* ignore */ }
                }
                else if (child is View childView)
                {
                    UnfocusDescendantInputs(childView);
                }
            }
        }
    }

    // ─── Scroll into view (Scrollable mode) ─────────────────────────────────────────

    /// <summary>
    ///     Scrolls the bar so the selected cell sits comfortably in view with two
    ///     extra cell-widths of breathing room past the cell on the trailing side
    ///     (or leading side if selecting toward the start). The user perceives this
    ///     as "the bar advances" when they tap a tab — the selected cell moves
    ///     toward the center, and the next 1–2 unselected tabs become visible.
    ///     <para>
    ///         When the selected cell is the first or last in the list the scroll
    ///         simply snaps to the corresponding edge (0 or the max scroll position),
    ///         so the user never has to drag the bar past the available range.
    ///     </para>
    /// </summary>
    private void ScrollSelectedIntoView(bool animate)
    {
        if (Mode != G9TabMode.Scrollable) return;

        var effective = EffectiveIndex;
        if (effective < 0 || _cells.Count == 0) return;

        var cell = FindCell(effective);
        if (cell?.Container.Width <= 0) return;
        if (cell is null) return;

        var contentWidth = _cellsAndPillHost.Width;
        var viewportWidth = _barScroll.Width;
        if (contentWidth <= 0 || viewportWidth <= 0) return;

        var maxScrollX = Math.Max(0, contentWidth - viewportWidth);

        // Find the cell's visual neighbours (in the scroll-bar order, NOT logical
        // index — RTL reverses the order). _cells is populated in visual order in
        // RebuildAll so its index is the visual position.
        var visualIndex = _cells.IndexOf(cell);

        var cellWidth = cell.Container.Width;
        var cellLeft = cell.Container.X;
        var cellRight = cellLeft + cellWidth;

        // Pre-roll buffer: aim to reveal 2 cell-widths of content past the selected
        // cell. We pick the side based on where the cell sits relative to the
        // current visible window, so taps near the trailing edge advance the scroll
        // forward and taps near the leading edge pull it back.
        var visibleLeft = _barScroll.ScrollX;
        var visibleRight = visibleLeft + viewportWidth;
        var visibleCenter = visibleLeft + (viewportWidth / 2);
        var cellCenter = cellLeft + (cellWidth / 2);
        var movingForward = cellCenter >= visibleCenter;

        var buffer = cellWidth * 2;

        double targetX;
        if (visualIndex == 0)
        {
            // First tab — always snap to the start so the leading edge is visible.
            targetX = 0;
        }
        else if (visualIndex == _cells.Count - 1)
        {
            // Last tab — always snap to the end so the trailing edge is visible.
            targetX = maxScrollX;
        }
        else if (movingForward)
        {
            // Reveal 2 cell-widths of trailing content.
            // ScrollX such that (cellRight + buffer) is at the right of the viewport
            // → ScrollX = cellRight + buffer − viewportWidth.
            targetX = cellRight + buffer - viewportWidth;
        }
        else
        {
            // Reveal 2 cell-widths of leading content.
            // ScrollX such that (cellLeft − buffer) is at the left of the viewport.
            targetX = cellLeft - buffer;
        }

        // Clamp to scroll range (no rubber-banding into invalid territory).
        targetX = Math.Clamp(targetX, 0, maxScrollX);

        // Skip if we're already there (avoids a 0-distance ScrollToAsync that some
        // platforms still spend a frame animating).
        if (Math.Abs(targetX - visibleLeft) < 0.5) return;

        try { _ = _barScroll.ScrollToAsync(targetX, 0, animate); } catch { }
    }

    // ─── Edge fade overlays ─────────────────────────────────────────────────────────

    private void UpdateFadeOverlays()
    {
        if (Mode != G9TabMode.Scrollable)
        {
            _leadingFade.IsVisible = false;
            _trailingFade.IsVisible = false;
            return;
        }

        if (_barScroll.ContentSize.Width <= _barScroll.Width + 1)
        {
            _leadingFade.IsVisible = false;
            _trailingFade.IsVisible = false;
            return;
        }

        var scrollX = _barScroll.ScrollX;
        var maxScrollX = Math.Max(0, _barScroll.ContentSize.Width - _barScroll.Width);

        const double fadeRange = TabFadeWidth;
        var leadingOpacity = Math.Clamp(scrollX / fadeRange, 0, 1);
        var trailingOpacity = Math.Clamp((maxScrollX - scrollX) / fadeRange, 0, 1);

        _leadingFade.IsVisible = leadingOpacity > 0.01;
        _trailingFade.IsVisible = trailingOpacity > 0.01;
        _leadingFade.Opacity = leadingOpacity;
        _trailingFade.Opacity = trailingOpacity;
    }

    /// <summary>
    ///     Edge-fade gradient. Fades from the bar's background color to transparent so
    ///     off-screen tabs visually "dissolve" into the bar edge.
    /// </summary>
    private sealed class TabFadeDrawable : IDrawable
    {
        public bool IsLeading { get; set; }
        public Color BaseColor { get; set; } = Colors.White;

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var gradient = new LinearGradientPaint
            {
                StartPoint = IsLeading ? new Point(0, 0) : new Point(1, 0),
                EndPoint = IsLeading ? new Point(1, 0) : new Point(0, 0),
                GradientStops =
                [
                    new PaintGradientStop(0f, BaseColor),
                    new PaintGradientStop(1f, BaseColor.WithAlpha(0))
                ]
            };
            canvas.SetFillPaint(gradient, dirtyRect);
            canvas.FillRectangle(dirtyRect);
        }
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────────

    private int EffectiveIndex
    {
        get
        {
            var count = Items?.Count ?? 0;
            if (count == 0) return -1;
            if (SelectedIndex < 0) return 0;
            if (SelectedIndex >= count) return count - 1;
            return SelectedIndex;
        }
    }
}
