using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;
using System.Collections.Specialized;
using System.ComponentModel;
using static G9MAUIControls.EdgePanel.G9EdgePanelMetrics;
using G9MAUIControls.Controls;

using G9MAUIControls.Icons;

namespace G9MAUIControls.EdgePanel;

/// <summary>
///     A modern animated edge peek panel — compact map-tools drawer style.
///     Slides from left or right screen edge with a morphing tab/close button.
///     Supports custom <see cref="View" /> content or a navigable list menu.
///     Follows the same component architecture as <c>G9TabBar</c>.
/// </summary>
public partial class G9EdgePanel : ContentView
{
    #region Constructor

    public G9EdgePanel()
    {
        IsClippedToBounds = false;
        BackgroundColor = Colors.Transparent;
        InputTransparent = true;
        CascadeInputTransparent = false;
        // Keep the outer ContentView always visible. Visual presence is driven entirely by the
        // inner backdrop/card/tab visibility flags. Toggling IsVisible on the wrapper across the
        // open/close lifecycle is unreliable on Android (GONE→VISIBLE flips in the same frame
        // can race the slide animation and leave the panel input-capturing but unrendered).
        VerticalOptions = LayoutOptions.Fill;
        HorizontalOptions = LayoutOptions.Fill;

        _themeChangedHandler = (_, _) => MainThread.BeginInvokeOnMainThread(() =>
        {
            ApplyTheme();
            ApplySideLayout(false);
        });

        ContentFlowDirection = G9EdgePanelContentDirection.MatchApplication;

        // Root overlay fills the parent but stays input-transparent. Only the active
        // backdrop, panel, and tab participate in hit testing.
        // Lock the root to LeftToRight so AbsoluteLayout positions are deterministic regardless
        // of the host page's RTL/LTR. The Side bindable property is the single source of truth
        // for which visual edge the panel attaches to. Text content inside the panel still
        // respects the user culture via _panelContentHost.FlowDirection.
        _root = new AbsoluteLayout
        {
            IsClippedToBounds = false,
            BackgroundColor = Colors.Transparent,
            InputTransparent = true,
            CascadeInputTransparent = false,
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };

        // Backdrop for outside tap to close.
        _backdrop = new BoxView
        {
            BackgroundColor = Colors.Transparent,
            IsVisible = false,
            InputTransparent = true,
            ZIndex = 0
        };
        _backdrop.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(OnBackdropTapped) });
        _root.Children.Add(_backdrop);
        AbsoluteLayout.SetLayoutFlags(_backdrop, AbsoluteLayoutFlags.All);
        AbsoluteLayout.SetLayoutBounds(_backdrop, new Rect(0, 0, 1, 1));

        // Panel card.
        _panelScrollView = new ScrollView
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Default,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalOptions = LayoutOptions.Start
        };

        // Grid host so menu navigation can briefly hold the outgoing and incoming list at the
        // same time and animate them. IsClippedToBounds clips the directional slide within the card.
        _panelContentHost = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            IsClippedToBounds = true,
            FlowDirection = FlowDirection.LeftToRight
        };
        _panelScrollView.Content = _panelContentHost;

        // Sticky header row — sits above the ScrollView so it never scrolls away.
        // Wrapped in a Border so the background color clips correctly and the bottom
        // separator line renders as a proper border edge.
        // Height = TabExpandedSize + TabTopInset so the header text sits at the same
        // vertical band as the × close button (which is inset TabTopInset from the panel top).
        // Horizontal alignment of the label and inner padding are driven by
        // <see cref="MenuHeaderAlignment"/> so the consumer can choose between Auto
        // (follows ContentFlowDirection), LeftToRight, RightToLeft, and Center. The
        // initial values are placeholders; ApplyMenuHeaderAlignment writes the real ones
        // once we know the resolved alignment + content flow direction.
        _panelStickyHeaderLabel = new Label
        {
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            InputTransparent = true
        };
        _panelStickyHeaderCustomView = null; // set when a CustomView header is used

        _panelStickyHeaderInner = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            // Padding is set by ApplyMenuHeaderAlignment based on MenuHeaderAlignment so
            // the close (×) button never collides with the title regardless of how the
            // user chose to align the text. Keep this row's natural top inset (TabTopInset)
            // so the header still vertically aligns with the close button band.
            Padding = new Thickness(14, TabTopInset, 14, 0),
            MinimumHeightRequest = TabExpandedSize + TabTopInset
        };
        _panelStickyHeaderInner.Children.Add(_panelStickyHeaderLabel);

        _panelStickyHeader = new Border
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsVisible = false,
            StrokeThickness = 0,
            Padding = new Thickness(0),
            Content = _panelStickyHeaderInner
        };

        // Spinner shown during the first open slide — hides render-heavy content until the panel arrives.
        _panelSpinner = new G9ActivityIndicator
        {
            IsRunning = false,
            IsVisible = false,
            HeightRequest = 36,
            WidthRequest = 36,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        _panelSpinnerContainer = new Grid
        {
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            MinimumHeightRequest = SpinnerMinHeightDp,
            Children = { _panelSpinner }
        };

        // Card content: sticky header (row 0) + divider (row 1) + scroll area / spinner overlay (row 2).
        _panelCardContent = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition(GridLength.Auto),  // row 0: sticky header
                new RowDefinition(GridLength.Auto),  // row 1: separator line
                new RowDefinition(GridLength.Star)   // row 2: scroll area
            }
        };
        _panelStickyHeaderDivider = new BoxView
        {
            HeightRequest = 1,
            HorizontalOptions = LayoutOptions.Fill,
            IsVisible = false,
            InputTransparent = true
        };
        Grid.SetRow(_panelStickyHeader, 0);
        Grid.SetRow(_panelStickyHeaderDivider, 1);
        Grid.SetRow(_panelScrollView, 2);
        Grid.SetRow(_panelSpinnerContainer, 2);
        _panelCardContent.Children.Add(_panelStickyHeader);
        _panelCardContent.Children.Add(_panelStickyHeaderDivider);
        _panelCardContent.Children.Add(_panelScrollView);
        _panelCardContent.Children.Add(_panelSpinnerContainer);

        _panelCard = new Border
        {
            StrokeThickness = PanelStrokeThickness,
            Padding = new Thickness(0),
            Content = _panelCardContent,
            VerticalOptions = LayoutOptions.Start,
            HorizontalOptions = LayoutOptions.Start,
            IsVisible = false,
            InputTransparent = true,
            ZIndex = 1
        };
        _panelCard.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(static () => { }) });

        _root.Children.Add(_panelCard);

        // Tab button.
        _tabIcon = new G9IconView {
            Size = TabIconSize,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        _tabBorder = new Border
        {
            StrokeThickness = 1,
            Padding = new Thickness(0),
            Content = _tabIcon,
            VerticalOptions = LayoutOptions.Start,
            HorizontalOptions = LayoutOptions.Start,
            IsVisible = false,
            InputTransparent = false,
            ZIndex = 2
        };
        _tabBorder.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(OnTabTapped) });

        _root.Children.Add(_tabBorder);

        Content = _root;

        // Defaults via constructor (AiGuide: set default values in constructor only).
        Side = G9EdgeSide.Left;
        WidthRatio = DefaultWidthRatio;
        TopGap = DefaultTopGap;
        MaxPanelHeight = 0;
        MaxPanelHeightRatio = DefaultMaxPanelHeightRatio;
        OpenAnimationDuration = DefaultOpenAnimationDurationMs;
        CloseAnimationDuration = DefaultCloseAnimationDurationMs;
        EnableOutsideTapToClose = true;
        UseBackdrop = true;
        ShowCollapsedTab = false;
        IsOpen = false;
        MenuHeaderAlignment = G9EdgeMenuHeaderAlignment.Auto;
        CloseButtonPlacement = G9EdgeCloseButtonPlacement.Inset;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;

        ApplyTheme();
        ApplyMenuHeaderAlignment();
        ApplySideLayout(false);
    }

    #endregion

    #region Fields

    private readonly AbsoluteLayout _root;
    private readonly BoxView _backdrop;
    private readonly Border _panelCard;
    private readonly ScrollView _panelScrollView;
    private readonly Grid _panelContentHost;
    private readonly Grid _panelCardContent;
    private readonly Border _panelStickyHeader;
    private readonly Grid _panelStickyHeaderInner;
    private readonly Label _panelStickyHeaderLabel;
    private View? _panelStickyHeaderCustomView;
    private readonly BoxView _panelStickyHeaderDivider;
    private readonly Grid _panelSpinnerContainer;
    private readonly ActivityIndicator _panelSpinner;
    private readonly Border _tabBorder;
    private readonly G9IconView _tabIcon;
    /// <summary>
    ///     Cached corner-radius keys for the tab so we only push a new <c>CornerRadius</c>
    ///     value into the live <see cref="RoundRectangle"/> when the rounded shape actually
    ///     changes by ≥1dp. Avoids redundant property-changed events while still giving a
    ///     buttery continuous morph between the chevron handle and the close circle.
    /// </summary>
    private double _lastTabRadiusTl = -1;
    private double _lastTabRadiusTr = -1;
    private double _lastTabRadiusBl = -1;
    private double _lastTabRadiusBr = -1;
    /// <summary>Live, mutable round-rectangle for the tab. Reused so the morph never allocates.</summary>
    private readonly RoundRectangle _tabShapeMorphing = new();
    /// <summary>Quantized "size key" so we only push WidthRequest/HeightRequest changes when ≥1dp moved.</summary>
    private int _lastTabSizeKey = -1;
    /// <summary>Cached panel card stroke shapes — one per side. Reused so corner-radius changes are free.</summary>
    private readonly RoundRectangle _panelShapeLeft = new() { CornerRadius = new CornerRadius(0, PanelCornerRadius, 0, PanelCornerRadius) };
    private readonly RoundRectangle _panelShapeRight = new() { CornerRadius = new CornerRadius(PanelCornerRadius, 0, PanelCornerRadius, 0) };
    private readonly PropertyChangedEventHandler _themeChangedHandler;

    private bool _themeHandlerAttached;
    private bool _cultureHandlerAttached;
    private double _animProgress; // 0 = collapsed, 1 = expanded.
    private double _parentWidth;
    private double _parentHeight;
    private bool _isAnimating;
    private bool _suppressIsOpenChanged;
    private int _animationVersion;
    private DateTimeOffset _lastInteractionAt = DateTimeOffset.MinValue;

    // ── Per-frame state cache. The slide animation calls ApplyAnimProgress on every frame
    //   (≈60 Hz). Tracking the last applied "mode" lets us skip the expensive layout-bounds /
    //   visibility / wrapper-sizing work when only the progress value changed — those mutations
    //   trigger MAUI layout passes (and Android invalidate calls), which is the dominant cost
    //   that makes the slide stutter on lower-end devices, especially at the end of close
    //   when easing pushes the panel at peak speed.
    private OuterMode _appliedOuterMode = OuterMode.None;
    private bool _appliedIsLeft;
    private bool _appliedCaptureBackdrop;
    /// <summary>Cached panel-edge X used to position the tab via TranslationX (lock-step with card).</summary>
    private double _panelExpandedX;
    private double _panelHiddenOffset;
    private double _tabRelCollapsed;
    private double _tabRelExpanded;
    /// <summary>Cached widths so size-dependent layout bounds only re-apply when geometry changes.</summary>
    private double _appliedPanelWidth = -1;
    /// <summary>0 → "morph icon to chevron", 1 → "morph icon to close". Lets us skip icon swaps when state didn't flip.</summary>
    private int _lastTabIconKey = -1;
    private double _appliedTabFadeOpacity = -1;

    // ── Menu navigation state ──
    private readonly Stack<MenuNavFrame> _menuStack = new();
    private IList<G9EdgeMenuItem>? _currentMenuList;
    private G9EdgeMenuHeader? _currentMenuHeader;
    /// <summary>The list currently subscribed to <see cref="INotifyCollectionChanged"/>.</summary>
    private IList<G9EdgeMenuItem>? _attachedMenuItems;

    private int _panelHeightAnimGeneration;
    private int _menuTransitionGeneration;
    private bool _tabEntranceConsumed;
    private bool _tabEntranceAnimating;
    private bool _hasOpenedOnce;
    /// <summary>
    ///     Set to true by <see cref="PreSizePanelForFirstOpen"/> and cleared when the first-open
    ///     spinner swap completes. While true, <see cref="ApplySideLayout"/> must not reset
    ///     <c>_panelCard.HeightRequest</c> to -1 — that would undo the pre-sized height and
    ///     collapse the card back to the spinner's 72dp minimum mid-slide.
    /// </summary>
    private bool _firstOpenPreSizeActive;

    private enum OuterMode { None, Hidden, TabOnly, Fill }

    private sealed record MenuNavFrame(IList<G9EdgeMenuItem> Items, G9EdgeMenuHeader? Header);

    #endregion

    #region Bindable properties

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnSideChanged))]
    private G9EdgeSide _side;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnWidthRatioChanged))]
    private double _widthRatio;

    [AutoBindable(OnChanged = nameof(OnTopGapChanged))]
    private double _topGap;

    [AutoBindable(OnChanged = nameof(OnMaxPanelHeightChanged))]
    private double _maxPanelHeight;

    [AutoBindable(OnChanged = nameof(OnMaxPanelHeightChanged))]
    private double _maxPanelHeightRatio;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnIsOpenChanged))]
    private bool _isOpen;

    [AutoBindable(OnChanged = nameof(OnPanelContentChanged))]
    private View? _panelContent;

    [AutoBindable(OnChanged = nameof(OnMenuItemsChanged))]
    private IList<G9EdgeMenuItem>? _menuItems;

    [AutoBindable] private Color? _panelBackgroundColor;
    [AutoBindable] private Color? _tabBackgroundColor;
    [AutoBindable(OnChanged = nameof(OnCollapsedTabIconChanged))]
    private G9IconSource? _collapsedTabIcon;
    [AutoBindable] private uint _openAnimationDuration;
    [AutoBindable] private uint _closeAnimationDuration;
    [AutoBindable(OnChanged = nameof(OnBackdropBehaviorChanged))]
    private bool _enableOutsideTapToClose;

    [AutoBindable(OnChanged = nameof(OnBackdropBehaviorChanged))]
    private bool _useBackdrop;

    [AutoBindable(OnChanged = nameof(OnShowCollapsedTabChanged))]
    private bool _showCollapsedTab;

    [AutoBindable(OnChanged = nameof(OnContentFlowDirectionChanged))]
    private G9EdgePanelContentDirection _contentFlowDirection;

    [AutoBindable(OnChanged = nameof(OnMenuHeaderChanged))]
    private G9EdgeMenuHeader? _menuHeader;

    /// <summary>
    ///     Horizontal alignment of the sticky header label / custom view. Default
    ///     <see cref="G9EdgeMenuHeaderAlignment.Auto"/> follows
    ///     <see cref="ContentFlowDirection"/> so the title sits on the leading edge in
    ///     both English and Persian. The other values pin the alignment regardless of
    ///     content direction (<see cref="G9EdgeMenuHeaderAlignment.LeftToRight"/>,
    ///     <see cref="G9EdgeMenuHeaderAlignment.RightToLeft"/>,
    ///     <see cref="G9EdgeMenuHeaderAlignment.Center"/>).
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnMenuHeaderAlignmentChanged))]
    private G9EdgeMenuHeaderAlignment _menuHeaderAlignment;

    /// <summary>
    ///     Where the expanded close (×) tab sits relative to the panel's inner corner.
    ///     Default <see cref="G9EdgeCloseButtonPlacement.Inset"/> keeps the legacy look
    ///     (close button INSIDE the panel near the corner). Switch to
    ///     <see cref="G9EdgeCloseButtonPlacement.OnCorner"/> to centre the close button
    ///     ON the inner corner border (half-in / half-out) — useful for full-takeover
    ///     overlays where the close affordance should read as a halo on the corner.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnCloseButtonPlacementChanged))]
    private G9EdgeCloseButtonPlacement _closeButtonPlacement;

    /// <summary>
    ///     Convenience: sets both <see cref="OpenAnimationDuration" /> and
    ///     <see cref="CloseAnimationDuration" />. Getter returns the open duration.
    /// </summary>
    public uint AnimationDuration
    {
        get => OpenAnimationDuration;
        set
        {
            OpenAnimationDuration = value;
            CloseAnimationDuration = value;
        }
    }

    #endregion

    #region Events

    public event EventHandler? Opened;
    public event EventHandler? Closing;
    public event EventHandler? Closed;

    #endregion

    #region Property change handlers

    private void OnSideChanged()
    {
        if (IsOpen)
        {
            IsOpen = false;
            _animProgress = 0;
        }

        this.AbortAnimation(TabEntranceAnimationName);
        _tabEntranceAnimating = false;
        _tabEntranceConsumed = false;

        // Side controls which inner edge hosts the close (×) tab; the side-aware padding
        // in ApplyMenuHeaderAlignment must follow it. (Left side → close tab on right →
        // right padding gets the wider inset, etc.)
        ApplyMenuHeaderAlignment();
        ApplySideLayout(false);
    }

    private void OnWidthRatioChanged()
    {
        WidthRatio = Math.Clamp(WidthRatio, 0, 1);
        ApplySideLayout(false);
    }

    private void OnTopGapChanged() => ApplySideLayout(false);
    private void OnMaxPanelHeightChanged() => ApplySideLayout(false);

    private void OnIsOpenChanged()
    {
        if (_suppressIsOpenChanged)
        {
            return;
        }

        AnimateToggle(IsOpen);
    }

    private void OnBackdropBehaviorChanged() => ApplyAnimProgress(_animProgress);

    private void OnShowCollapsedTabChanged()
    {
        if (!ShowCollapsedTab)
        {
            this.AbortAnimation(TabEntranceAnimationName);
            _tabEntranceAnimating = false;
        }
        else
        {
            _tabEntranceConsumed = false;
        }

        ApplyAnimProgress(_animProgress);
    }

    private void OnContentFlowDirectionChanged()
    {
        _panelContentHost.FlowDirection = ResolvePanelContentFlowDirection();
        _panelStickyHeader.FlowDirection = ResolvePanelContentFlowDirection();
        // Auto-aligned header tracks ContentFlowDirection — re-apply when it changes so
        // the title visually swaps leading/trailing edge alongside the menu rows.
        ApplyMenuHeaderAlignment();
        ApplySideLayout(false);
        if (_currentMenuList is not null)
        {
            ShowMenuList(_currentMenuList, isRoot: _menuStack.Count == 0, G9MenuTransitionDirection.None);
        }
    }

    private void OnMenuHeaderChanged()
    {
        if (MenuItems is not { Count: > 0 } || _menuStack.Count > 0)
        {
            return;
        }

        _currentMenuHeader = MenuHeader;
        ShowMenuList(MenuItems, isRoot: true, G9MenuTransitionDirection.None);
    }

    private void OnMenuHeaderAlignmentChanged()
    {
        ApplyMenuHeaderAlignment();
    }

    private void OnCloseButtonPlacementChanged()
    {
        // The placement only affects the expanded tab X (and via that, the header padding
        // because center mode reserves the close-tab footprint). Re-run side layout so
        // _tabRelExpanded is recomputed and ApplyAnimProgress repaints.
        ApplyMenuHeaderAlignment();
        ApplySideLayout(false);
    }

    /// <summary>
    ///     Resolves <see cref="MenuHeaderAlignment"/> against the current
    ///     <see cref="ContentFlowDirection"/> and writes the matching label alignment +
    ///     header-grid padding. Called from the constructor (initial state), the bindable
    ///     change handler, and <see cref="OnContentFlowDirectionChanged"/> so the auto
    ///     mode tracks the panel's content direction.
    /// </summary>
    private void ApplyMenuHeaderAlignment()
    {
        // Inner inset on the side that hosts the close (×) tab is the tab footprint so
        // the title can never overlap the close button. The opposite side gets a small
        // breathing margin (14dp) so text doesn't kiss the wall edge. Center mode mirrors
        // the close-tab inset on the opposite side too, but caps it so a narrow panel
        // (0.40 width ratio on a phone) still has visible room for the label — earlier
        // versions used the full 96dp inset on both sides and squeezed the label to 0dp.
        // CloseButtonPlacement = OnCorner moves the close button to straddle the panel's
        // inner corner (half outside the panel), so only HALF of the tab actually overlaps
        // the header — reserve 20dp + 8dp breathing room on that side instead of the full
        // 96dp Inset footprint.
        const double WallEdgePadding = 14;
        const double CenterMaxInsetDp = 32;
        var closeTabInset = CloseButtonPlacement == G9EdgeCloseButtonPlacement.OnCorner
            ? TabExpandedSize / 2.0 + 8
            : TabExpandedSize + ExpandedTabInset;
        var closeTabSide = IsLeftSide()
            ? CloseTabPanelSide.Right
            : CloseTabPanelSide.Left;

        // Resolve auto → physical: in Auto mode the title sits on the leading edge of
        // the resolved content flow direction.
        var resolved = MenuHeaderAlignment;
        if (resolved == G9EdgeMenuHeaderAlignment.Auto)
        {
            resolved = ResolvePanelContentFlowDirection() == FlowDirection.RightToLeft
                ? G9EdgeMenuHeaderAlignment.RightToLeft
                : G9EdgeMenuHeaderAlignment.LeftToRight;
        }

        // Map alignment to the underlying label / inner-grid padding. The grid's own
        // FlowDirection is forced LTR by the panel root so physical Left padding is
        // always the visual left side regardless of the page's culture.
        switch (resolved)
        {
            case G9EdgeMenuHeaderAlignment.Center:
                _panelStickyHeaderLabel.HorizontalOptions = LayoutOptions.Center;
                _panelStickyHeaderLabel.HorizontalTextAlignment = TextAlignment.Center;
                // The close tab lives on exactly ONE side; pad that side by the tab footprint
                // so the label cannot overlap it, and pad the opposite side by a smaller
                // capped inset so the label's centre lands a touch off-true to compensate.
                // Cap by CenterMaxInsetDp so a narrow panel still has horizontal room.
                _panelStickyHeaderInner.Padding = new Thickness(
                    closeTabSide == CloseTabPanelSide.Left ? closeTabInset : CenterMaxInsetDp,
                    TabTopInset,
                    closeTabSide == CloseTabPanelSide.Right ? closeTabInset : CenterMaxInsetDp,
                    0);
                break;

            case G9EdgeMenuHeaderAlignment.RightToLeft:
                _panelStickyHeaderLabel.HorizontalOptions = LayoutOptions.End;
                _panelStickyHeaderLabel.HorizontalTextAlignment = TextAlignment.End;
                _panelStickyHeaderInner.Padding = new Thickness(
                    WallEdgePadding,
                    TabTopInset,
                    closeTabSide == CloseTabPanelSide.Right ? closeTabInset : WallEdgePadding,
                    0);
                break;

            case G9EdgeMenuHeaderAlignment.LeftToRight:
            default:
                _panelStickyHeaderLabel.HorizontalOptions = LayoutOptions.Start;
                _panelStickyHeaderLabel.HorizontalTextAlignment = TextAlignment.Start;
                _panelStickyHeaderInner.Padding = new Thickness(
                    closeTabSide == CloseTabPanelSide.Left ? closeTabInset : WallEdgePadding,
                    TabTopInset,
                    WallEdgePadding,
                    0);
                break;
        }
    }

    private enum CloseTabPanelSide { Left, Right }

    private void OnCollapsedTabIconChanged()
    {
        // Force the icon to re-evaluate on the next animation frame.
        _lastTabIconKey = -1;
        ApplyAnimProgress(_animProgress);
    }

    private void OnPanelContentChanged()
    {
        _menuStack.Clear();
        _currentMenuList = null;
        _currentMenuHeader = null;
        // Custom content never uses the sticky header.
        _panelStickyHeader.IsVisible = false;
        _panelStickyHeaderDivider.IsVisible = false;
        var frozenRecorded = TryFreezePanelCardHeight(out var frozen) ? frozen : 0d;
        ReplaceContentHostChild(PanelContent);
        var hg = ++_panelHeightAnimGeneration;
        if (PanelContent is not null)
        {
            _ = RunPanelHeightAlignToContentAsync(frozenRecorded, PanelContent, hg);
        }
        else
        {
            _panelCard.HeightRequest = -1;
        }
    }

    private void OnMenuItemsChanged()
    {
        // Detach collection-changed listener from the old list.
        if (_attachedMenuItems is INotifyCollectionChanged oldNcc)
            oldNcc.CollectionChanged -= OnMenuItemsCollectionChanged;

        _attachedMenuItems = MenuItems;

        // Attach to the new list if it supports change notifications.
        if (_attachedMenuItems is INotifyCollectionChanged newNcc)
            newNcc.CollectionChanged += OnMenuItemsCollectionChanged;

        _menuStack.Clear();
        _currentMenuList = null;
        if (MenuItems is { Count: > 0 })
        {
            _currentMenuHeader = MenuHeader;
            ShowMenuList(MenuItems, isRoot: true, G9MenuTransitionDirection.None);
        }
        else
        {
            ReplaceContentHostChild(null);
        }
    }

    private void OnMenuItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Any structural change to the root list (add/remove/replace/reset) triggers a full
        // rebuild of the root level. Navigation state is preserved — if the user is inside a
        // sub-list the stack is kept; only the root list is refreshed in the background.
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (MenuItems is not { Count: > 0 }) return;
            if (_menuStack.Count == 0)
            {
                // Currently showing root — rebuild immediately.
                _currentMenuHeader = MenuHeader;
                ShowMenuList(MenuItems, isRoot: true, G9MenuTransitionDirection.None);
            }
            // If inside a sub-list, the root is stale but the user can't see it — it will be
            // rebuilt when they navigate back (OnMenuBack calls ShowMenuList with the stack frame).
        });
    }

    private void ReplaceContentHostChild(View? child)
    {
        _panelContentHost.Children.Clear();
        if (child is not null)
        {
            _panelContentHost.Children.Add(child);
        }
    }

    #endregion

    #region Lifecycle

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (!_themeHandlerAttached)
        {
            G9Palette.Current.PropertyChanged += _themeChangedHandler;
            _themeHandlerAttached = true;
        }

        if (!_cultureHandlerAttached)
        {
            G9Culture.CultureChanged += OnAppCultureChanged;
            _cultureHandlerAttached = true;
        }

        ApplyTheme();
        MeasureParent();
        ApplySideLayout(false);

        // Belt-and-suspenders for Android: if a transient unload-reload cycle aborted an
        // in-flight slide animation, the canceled finished callback returns without clearing
        // _isAnimating. Detect that state here and resume from the current progress so the
        // panel always reaches its target instead of getting stuck mid-slide.
        if (_isAnimating && !this.AnimationIsRunning(PanelAnimationName))
        {
            AnimateToggle(IsOpen);
        }
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        // On Android, a transient unload-reload cycle can fire during layout reflow even though
        // the panel is still in its parent's children collection. Only run final cleanup when
        // the panel has actually been removed from its parent — otherwise we would abort an
        // in-flight slide animation that is about to resume on the next OnLoaded.
        if (Parent is not null)
        {
            return;
        }

        if (_themeHandlerAttached)
        {
            G9Palette.Current.PropertyChanged -= _themeChangedHandler;
            _themeHandlerAttached = false;
        }

        if (_cultureHandlerAttached)
        {
            G9Culture.CultureChanged -= OnAppCultureChanged;
            _cultureHandlerAttached = false;
        }

        this.AbortAnimation(PanelAnimationName);
        this.AbortAnimation(TabMorphAnimationName);
        this.AbortAnimation(TabEntranceAnimationName);
        this.AbortAnimation(PanelHeightAnimationName);
        this.AbortAnimation(MenuContentAnimationName);
        this.AbortAnimation(PanelContentFadeAnimationName);
        _tabEntranceAnimating = false;
        _tabEntranceConsumed = false;

        if (_attachedMenuItems is INotifyCollectionChanged ncc)
            ncc.CollectionChanged -= OnMenuItemsCollectionChanged;
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        MeasureParent();
        ApplySideLayout(false);
    }

    private void OnAppCultureChanged(object? sender, G9CultureEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ApplyTheme();
            ApplySideLayout(false);
            if (_currentMenuList is not null)
            {
                ShowMenuList(_currentMenuList, _menuStack.Count == 0, G9MenuTransitionDirection.None);
            }
        });
    }

    private void MeasureParent()
    {
        var parent = Parent as VisualElement;
        _parentWidth = parent?.Width > 0 ? parent.Width : (Width > 0 ? Width : 400);
        _parentHeight = parent?.Height > 0 ? parent.Height : (Height > 0 ? Height : 800);
    }

    #endregion

    #region Theme

    private void ApplyTheme()
    {
        var theme = G9Palette.Current;

        // Panel card.
        _panelCard.BackgroundColor = PanelBackgroundColor ?? G9EdgePanelColors.PanelBackground(theme);
        _panelCard.Stroke = new SolidColorBrush(G9EdgePanelColors.PanelStroke(theme));

        // Tab.
        _tabBorder.BackgroundColor = TabBackgroundColor ?? G9EdgePanelColors.TabBackground(theme);
        _tabBorder.Stroke = new SolidColorBrush(G9EdgePanelColors.TabStroke(theme));

        _tabIcon.Color = G9EdgePanelColors.TabIcon(theme);
        _panelSpinner.Color = theme.Primary;
        _panelStickyHeaderDivider.BackgroundColor = G9EdgePanelColors.MenuItemDivider(theme);
        _panelStickyHeader.BackgroundColor = G9EdgePanelColors.StickyHeaderBackground(theme);
        _panelStickyHeaderLabel.TextColor = G9EdgePanelColors.TitleText(theme);

        // Backdrop.
        _backdrop.BackgroundColor = G9EdgePanelColors.Backdrop(theme);

        // Refresh current menu list if showing menu items.
        if (_currentMenuList is not null)
        {
            ShowMenuList(_currentMenuList, isRoot: _menuStack.Count == 0, G9MenuTransitionDirection.None);
        }
    }

    #endregion

    #region Layout

    private void ApplySideLayout(bool animated)
    {
        MeasureParent();
        var panelWidth = _parentWidth * Math.Clamp(WidthRatio, 0, 1);
        var maxPanelHeight = ResolveMaxPanelHeight();
        var isLeft = IsLeftSide();

        // ── Panel card shapes ──
        // Reuse cached RoundRectangle instances so the corner-radius change does not re-tessellate
        // on every layout pass. Side directly controls which corners are rounded.
        _panelCard.StrokeShape = isLeft ? _panelShapeLeft : _panelShapeRight;
        _panelCard.WidthRequest = panelWidth;
        // Auto-size height to content, capped at the configured maximum so the scroll view kicks
        // in when the content exceeds the cap. Guard: do NOT reset HeightRequest while the
        // first-open pre-size is active — that would collapse the card back to the spinner's
        // 72dp minimum mid-slide and undo the pre-measured height we set in PreSizePanelForFirstOpen.
        if (!_firstOpenPreSizeActive)
        {
            _panelCard.HeightRequest = -1;
        }
        else
        {
        }
        _panelCard.MaximumHeightRequest = maxPanelHeight;
        _panelScrollView.HeightRequest = -1;
        _panelScrollView.MaximumHeightRequest = maxPanelHeight;
        _panelCard.Margin = Thickness.Zero;

        // Panel content follows <see cref="ContentFlowDirection" /> (or app culture when matching).
        _panelContentHost.FlowDirection = ResolvePanelContentFlowDirection();
        _panelStickyHeader.FlowDirection = ResolvePanelContentFlowDirection();

        // Cache geometry derived from current side + panel width. ApplyAnimProgress consumes
        // these every frame so we never recompute them mid-slide.
        // CloseButtonPlacement = OnCorner moves the close-tab CENTRE onto the panel's inner
        // corner edge (half inside, half outside the panel) instead of inset by
        // ExpandedTabInset. The collapsed offset is unchanged because the collapsed tab still
        // hangs flush off the panel's outer edge — only the expanded position differs.
        var isCenteredOnCorner = CloseButtonPlacement == G9EdgeCloseButtonPlacement.OnCorner;
        var halfTab = TabExpandedSize / 2.0;
        if (isLeft)
        {
            _panelExpandedX = -WallOverlapDp;
            _panelHiddenOffset = -panelWidth - CollapsedExtraOffset;
            // Tab sits flush against the panel's right edge when collapsed (no extra offset).
            // The tab is on the OUTSIDE of the panel during close so it rides with the panel
            // edge — sharing the same TranslationX keeps them glued together every frame.
            _tabRelCollapsed = panelWidth;
            _tabRelExpanded = isCenteredOnCorner
                ? panelWidth - halfTab            // centre on inner edge → origin = edge - r
                : panelWidth - ExpandedTabInset;  // legacy inset look
        }
        else
        {
            _panelExpandedX = _parentWidth - panelWidth + WallOverlapDp;
            _panelHiddenOffset = panelWidth + CollapsedExtraOffset;
            // Tab sits flush against the panel's left edge when collapsed (no extra offset).
            _tabRelCollapsed = -TabCollapsedWidth;
            _tabRelExpanded = isCenteredOnCorner
                ? -halfTab                        // centre on inner edge → origin = -r
                : ExpandedTabInset - TabExpandedSize;
        }

        _appliedPanelWidth = panelWidth;

        // Geometry note (close-time stickiness):
        //   panel hidden offset uses CollapsedExtraOffset so the panel slides 10dp PAST the wall
        //     (ensures the rounded corner is fully out of sight at t=0).
        //   tab collapsed-relative offset DOES NOT add CollapsedExtraOffset on top — it sits
        //     exactly at the panel's outer edge. Both layouts share the same TranslationX, so
        //     the tab stays glued to the panel edge through the entire close. Adding the extra
        //     offset to the tab (as the original code did) caused the tab to sit 10dp away
        //     from the panel right edge in the last frames of close — a visible gap.

        // Force re-application of layout bounds and tab shape because side or width changed.
        _appliedOuterMode = OuterMode.None;
        _lastTabIconKey = -1;
        _appliedTabFadeOpacity = -1;
        _lastTabSizeKey = -1;

        // ── Position ──
        ApplyAnimProgress(_animProgress);
    }

    /// <summary>
    ///     Updates the tab's shape, size, and icon based on the current animation progress.
    ///     <para>
    ///         The corner-radius morph runs continuously through the slide so the shape eases
    ///         from a chevron handle (one rounded corner pair flush against the wall) into a
    ///         circular close button (all four corners equal). Size is interpolated 48×64 →
    ///         40×40 the same way. Both run on a single reused <see cref="RoundRectangle"/>
    ///         instance — the only cost per frame is updating its <c>CornerRadius</c> property,
    ///         which is much cheaper than allocating a new shape. Quantized-key cache further
    ///         skips the property write when the value hasn't moved by ≥1dp / ≥0.5dp.
    ///     </para>
    /// </summary>
    /// <returns>
    ///     True when the tab's outer size changed enough this frame that its layout bounds need
    ///     to be re-committed. The caller refreshes <see cref="AbsoluteLayout"/> bounds for the
    ///     tab when this is set.
    /// </returns>
    private bool UpdateTabShape()
    {
        var isLeft = IsLeftSide();
        var t = _animProgress;
        var expandedRadius = TabExpandedSize / 2;

        // Size morph 48×64 → 40×40. Quantized to integer dp so we don't push WidthRequest /
        // HeightRequest on every sub-pixel frame — that scheduled a layout pass on Android.
        var w = Lerp(TabCollapsedWidth, TabExpandedSize, t);
        var h = Lerp(TabCollapsedHeight, TabExpandedSize, t);
        var sizeKey = ((int)Math.Round(w) << 16) | (int)Math.Round(h);
        var sizeChanged = false;
        if (sizeKey != _lastTabSizeKey)
        {
            _lastTabSizeKey = sizeKey;
            _tabBorder.WidthRequest = w;
            _tabBorder.HeightRequest = h;
            sizeChanged = true;
        }

        // Corner radius morph. Side controls which corners start rounded:
        //   left  collapsed: TL=0, TR=18, BR=18, BL=0  → expanded: all 20 (full circle)
        //   right collapsed: TL=18, TR=0, BR=0, BL=18 → expanded: all 20
        // We push the new CornerRadius into the same RoundRectangle each frame (no allocation)
        // and skip the assignment when no corner moved by ≥0.5dp.
        double tl, tr, br, bl;
        if (isLeft)
        {
            tl = Lerp(0, expandedRadius, t);
            tr = Lerp(TabCornerRadius, expandedRadius, t);
            br = Lerp(TabCornerRadius, expandedRadius, t);
            bl = Lerp(0, expandedRadius, t);
        }
        else
        {
            tl = Lerp(TabCornerRadius, expandedRadius, t);
            tr = Lerp(0, expandedRadius, t);
            br = Lerp(0, expandedRadius, t);
            bl = Lerp(TabCornerRadius, expandedRadius, t);
        }

        if (Math.Abs(tl - _lastTabRadiusTl) >= 0.5 ||
            Math.Abs(tr - _lastTabRadiusTr) >= 0.5 ||
            Math.Abs(br - _lastTabRadiusBr) >= 0.5 ||
            Math.Abs(bl - _lastTabRadiusBl) >= 0.5)
        {
            _lastTabRadiusTl = tl;
            _lastTabRadiusTr = tr;
            _lastTabRadiusBr = br;
            _lastTabRadiusBl = bl;
            // CornerRadius is a struct; mutating it on the live shape pushes one property change.
            _tabShapeMorphing.CornerRadius = new CornerRadius(tl, tr, bl, br);
            // Make sure the live shape is the one the Border is rendering. Assigned only when
            // not already set (cheap reference equality) — no-op on every subsequent frame.
            if (!ReferenceEquals(_tabBorder.StrokeShape, _tabShapeMorphing))
            {
                _tabBorder.StrokeShape = _tabShapeMorphing;
            }
        }

        // Icon swap at midpoint. The icon enum has only two states (chevron / close) so we
        // snap it instead of trying to morph a glyph — caching the key avoids triggering a
        // property-changed pass on every animation frame even when the icon hasn't flipped.
        var iconKey = t > 0.5 ? 1 : 0;
        if (iconKey != _lastTabIconKey)
        {
            _lastTabIconKey = iconKey;
            if (iconKey == 1)
            {
                _tabIcon.Icon = G9Glyphs.Clear;
            }
            else
            {
                // Use the caller-supplied collapsed icon when set; fall back to the directional chevron.
                _tabIcon.Icon = CollapsedTabIcon
                    ?? (isLeft ? G9Glyphs.ChevronForward : G9Glyphs.ChevronBack);
            }
        }

        return sizeChanged;
    }

    private void ApplyAnimProgress(double t)
    {
        t = Math.Clamp(t, 0, 1);

        var isLeft = IsLeftSide();
        var isExpandedOrAnimating = IsOpen || _isAnimating || t > 0.01;
        var inTabOnlyMode = ShowCollapsedTab && !isExpandedOrAnimating;
        var shouldCaptureBackdropInput = UseBackdrop && isExpandedOrAnimating;
        var anyContentVisible = isExpandedOrAnimating || inTabOnlyMode;

        if (!anyContentVisible)
        {
            // Fully hidden — collapse the wrapper so the page below stays fully interactive.
            ApplyOuterAsTabOnly(isLeft, hidden: true);
            HideAllChildren();
            return;
        }

        if (inTabOnlyMode)
        {
            ApplyOuterAsTabOnly(isLeft, hidden: false);
            EnterTabOnlyChildState();
            var tabShapeChangedTabOnly = UpdateTabShape();
            if (tabShapeChangedTabOnly)
            {
                AbsoluteLayout.SetLayoutBounds(_tabBorder,
                    new Rect(0, 0, _tabBorder.WidthRequest, _tabBorder.HeightRequest));
            }
            TryBeginTabEntranceAnimation(isLeft);
            // Tab is positioned at (0,0) inside the small _root because the outer wrapper is
            // already offset to the tab location via Margin.
            if (!_tabEntranceAnimating)
            {
                _tabBorder.TranslationX = 0;
            }
            _tabBorder.TranslationY = 0;
            _tabBorder.Opacity = 1;
            _appliedTabFadeOpacity = 1;
            return;
        }

        // Expanded or animating: wrapper covers the whole parent so the backdrop/panel can
        // capture taps and the slide animation has room.
        ApplyOuterAsFill(shouldCaptureBackdropInput);

        // Update tab size/shape BEFORE EnterFillChildState — the latter sets the tab's layout
        // bounds using the current WidthRequest/HeightRequest, so we need those to be correct
        // for the current animation progress before bounds are committed.
        var tabShapeChanged = UpdateTabShape();

        EnterFillChildState();

        // If the tab just crossed the morph midpoint, its WidthRequest/HeightRequest changed —
        // refresh its layout bounds to the new size so the platform draws a tight box.
        if (tabShapeChanged && _appliedOuterMode == OuterMode.Fill)
        {
            var tabY = TopGap + TabTopInset;
            AbsoluteLayout.SetLayoutBounds(_tabBorder,
                new Rect(_panelExpandedX + _tabRelCollapsed, tabY,
                    _tabBorder.WidthRequest, _tabBorder.HeightRequest));
        }

        // ── Per-frame work: ONLY TranslationX and (occasionally) opacity. Layout bounds for
        // panel/tab were committed when we entered Fill mode — re-applying them every frame
        // is what made the previous version judder, because each SetLayoutBounds call schedules
        // a full layout pass. TranslationX is a GPU-side transform with no layout cost.
        // ── Panel slide. Layout origin is at panelExpandedX (committed in EnterFillChildState),
        //   so TranslationX = Lerp(panelHiddenOffset, 0, t) carries the card from off-screen
        //   to the wall, identical to the previous implementation but without per-frame layout.
        var panelTranslation = Lerp(_panelHiddenOffset, 0, t);
        _panelCard.TranslationX = panelTranslation;

        // ── Tab slide. Tab's layout origin is at (panelExpandedX + _tabRelCollapsed) — its
        //   position in world space when the panel is fully collapsed. We compose two
        //   contributions into the translation: the panel slide and the tab's morph along the
        //   panel's inner edge (relCollapsed → relExpanded). Using TranslationX for both keeps
        //   the tab perfectly locked to the panel edge every frame, which fixes the visible
        //   gap that appeared during close because the panel used TranslationX (GPU)
        //   while the tab used SetLayoutBounds (deferred layout pass).
        _tabBorder.TranslationX = panelTranslation + Lerp(0, _tabRelExpanded - _tabRelCollapsed, t);

        // ── Tab vertical morph. In Inset (default) placement the tab stays at its layout-slot
        //   Y (TopGap + TabTopInset) for both states, so TranslationY is 0. In OnCorner
        //   placement the EXPANDED close button must be centred on the panel's inner top
        //   corner, i.e. its centre lies on the corner point (panelInnerEdgeX, TopGap). The
        //   layout slot already sits at TopGap + TabTopInset, so we shift the tab UP by
        //   (TabTopInset + halfExpandedTab) when fully open, and lerp back to 0 in the
        //   collapsed state where the chevron tab still hangs at its natural inset Y.
        if (CloseButtonPlacement == G9EdgeCloseButtonPlacement.OnCorner)
        {
            const double halfExpanded = TabExpandedSize / 2.0;
            _tabBorder.TranslationY = Lerp(0, -(TabTopInset + halfExpanded), t);
        }
        else
        {
            _tabBorder.TranslationY = 0;
        }

        // Persistent collapsed tab stays solid; otherwise the tab fades in/out with the slide
        // so the arrow doesn't pop in suddenly when first attached.
        var tabOpacity = ShowCollapsedTab || TabFadeInProgressFraction <= 0
            ? 1
            : Math.Clamp(t / TabFadeInProgressFraction, 0, 1);
        if (Math.Abs(tabOpacity - _appliedTabFadeOpacity) > 0.005)
        {
            _tabBorder.Opacity = tabOpacity;
            _appliedTabFadeOpacity = tabOpacity;
        }

        // Backdrop opacity tweens with the slide too; this is a free GPU op.
        _backdrop.Opacity = t;

        // InputTransparent only flips at the very start/end of the slide — toggling it every
        // frame causes Android to recompute hit-testing. Only flip when the boolean changes.
        var panelInputTransparent = !IsOpen || t < 0.99;
        if (_panelCard.InputTransparent != panelInputTransparent)
        {
            _panelCard.InputTransparent = panelInputTransparent;
        }
    }

    /// <summary>
    ///     Hides every interactive child. Called when the panel is fully collapsed and the
    ///     collapsed tab is disabled — the outer wrapper has shrunk to a 0-size invisible point
    ///     so the page below stays fully interactive.
    /// </summary>
    private void HideAllChildren()
    {
        if (_backdrop.IsVisible) { _backdrop.IsVisible = false; _backdrop.InputTransparent = true; }
        if (_panelCard.IsVisible) { _panelCard.IsVisible = false; _panelCard.InputTransparent = true; }
        if (_tabBorder.IsVisible) { _tabBorder.IsVisible = false; _tabBorder.InputTransparent = true; }
    }

    /// <summary>Switch child visibility/input flags for tab-only mode (panel hidden, tab visible).</summary>
    private void EnterTabOnlyChildState()
    {
        if (_backdrop.IsVisible) { _backdrop.IsVisible = false; _backdrop.InputTransparent = true; }
        if (_panelCard.IsVisible) { _panelCard.IsVisible = false; _panelCard.InputTransparent = true; }

        // Pre-position the tab before making it visible. TryBeginTabEntranceAnimation sets
        // TranslationX to the off-screen start value, so the tab is never seen at position 0
        // for even one frame before the entrance animation begins.
        if (!_tabEntranceConsumed)
        {
            var isLeft = IsLeftSide();
            var w = _tabBorder.WidthRequest > 0 ? _tabBorder.WidthRequest : TabCollapsedWidth;
            _tabBorder.TranslationX = isLeft ? -(w + CollapsedExtraOffset) : w + CollapsedExtraOffset;
        }

        // Keep the tab hidden until the wrapper layout has settled. On the very first appearance
        // the outer ContentView transitions from Fill to a small Margin-positioned box — that
        // layout change takes one frame. If we set IsVisible = true in the same frame the tab
        // briefly renders at the wrapper's old (Fill) position, which is the center of the page.
        // TryBeginTabEntranceAnimation handles making the tab visible after pre-positioning it,
        // so we only set IsVisible here for subsequent appearances (entrance already consumed).
        if (!_tabBorder.IsVisible)
        {
            if (!_tabEntranceConsumed)
            {
                // First appearance: TryBeginTabEntranceAnimation will set IsVisible = true
                // after pre-positioning TranslationX. Leave it hidden here.
            }
            else
            {
                _tabBorder.IsVisible = true;
            }
        }
        if (_tabBorder.InputTransparent) _tabBorder.InputTransparent = false;

        // Tab layout bounds — set once on mode entry. The outer wrapper is already positioned at
        // the tab's screen location via Margin, so the tab sits at (0, 0) inside the small _root.
        AbsoluteLayout.SetLayoutBounds(_tabBorder, new Rect(0, 0, _tabBorder.WidthRequest, _tabBorder.HeightRequest));
    }

    /// <summary>Switch child visibility/input flags for fill mode (panel + tab + backdrop visible).</summary>
    private void EnterFillChildState()
    {
        if (!_panelCard.IsVisible) _panelCard.IsVisible = true;
        if (!_tabBorder.IsVisible) _tabBorder.IsVisible = true;
        if (_tabBorder.InputTransparent) _tabBorder.InputTransparent = false;

        var captureBackdrop = UseBackdrop;
        if (_backdrop.IsVisible != captureBackdrop) _backdrop.IsVisible = captureBackdrop;
        _backdrop.InputTransparent = !captureBackdrop;

        // Re-applying layout bounds is what makes the previous implementation judder during the
        // slide — each SetLayoutBounds call invalidates the layout pass on the next frame. We
        // commit them ONCE on mode entry, then drive the slide via TranslationX (GPU transform,
        // free per frame). The slide formula in ApplyAnimProgress matches these origins so the
        // composed (Layout + Translation) on-screen X is identical to the old per-frame version.
        if (_appliedOuterMode != OuterMode.Fill)
        {
            AbsoluteLayout.SetLayoutFlags(_panelCard, AbsoluteLayoutFlags.None);
            // When the first-open pre-size is active, use the pre-measured height in the layout
            // bounds instead of AutoSize. AutoSize (-1) lets the AbsoluteLayout measure the card
            // from its visible children — but at this point _panelScrollView is hidden (showing
            // the spinner), so AutoSize would collapse the card to the spinner's 72dp minimum,
            // undoing the pre-measured height we set in PreSizePanelForFirstOpen.
            var cardLayoutH = _firstOpenPreSizeActive && _panelCard.HeightRequest > 0
                ? _panelCard.HeightRequest
                : AbsoluteLayout.AutoSize;
            AbsoluteLayout.SetLayoutBounds(_panelCard,
                new Rect(_panelExpandedX, TopGap, _appliedPanelWidth, cardLayoutH));

            // Tab's layout origin = the world-space position the tab occupies when the panel is
            // collapsed (panelExpandedX + tabRelCollapsed). The slide formula in ApplyAnimProgress
            // adds (panelTranslation + (tabRelExpanded - tabRelCollapsed) * t), which carries
            // the tab from collapsed-relative position to expanded-relative position over the
            // course of the slide.
            var tabY = TopGap + TabTopInset;
            AbsoluteLayout.SetLayoutBounds(_tabBorder,
                new Rect(_panelExpandedX + _tabRelCollapsed, tabY,
                    _tabBorder.WidthRequest <= 0 ? TabCollapsedWidth : _tabBorder.WidthRequest,
                    _tabBorder.HeightRequest <= 0 ? TabCollapsedHeight : _tabBorder.HeightRequest));

            AbsoluteLayout.SetLayoutBounds(_backdrop, new Rect(0, 0, 1, 1));
            AbsoluteLayout.SetLayoutFlags(_backdrop, AbsoluteLayoutFlags.All);

            _appliedOuterMode = OuterMode.Fill;
        }
    }

    /// <summary>
    ///     Positions the outer ContentView and inner _root so they cover only the tab region.
    ///     The page below stays fully interactive because the wrapper is no longer a full-page
    ///     input-transparent overlay (which Android blocks taps through despite
    ///     CascadeInputTransparent=false).
    /// </summary>
    private void ApplyOuterAsTabOnly(bool isLeft, bool hidden)
    {
        var targetMode = hidden ? OuterMode.Hidden : OuterMode.TabOnly;
        // Mode is stable AND side hasn't flipped — nothing to apply. ApplyAnimProgress runs on
        // every frame so without this guard we'd re-set Margin/HorizontalOptions/etc. every
        // frame, each of which schedules a layout pass on Android.
        if (_appliedOuterMode == targetMode && _appliedIsLeft == isLeft) return;
        _appliedOuterMode = targetMode;
        _appliedIsLeft = isLeft;

        // In MAUI, LayoutOptions.Start/End respect the parent's FlowDirection: Start is the
        // logical leading edge (left in LTR, right in RTL). To make Side directly map to the
        // ABSOLUTE visual edge regardless of the page's RTL/LTR, flip Start↔End when the
        // resolved flow is right-to-left. Use <see cref="G9Culture" /> so runtime language
        // switches (which update <c>CurrentFlowDirection</c>) stay in sync — thread
        // <c>CurrentUICulture</c> may not update immediately on all platforms.
        var parentRtl = G9Culture.IsRtl;
        HorizontalOptions = (isLeft ^ parentRtl) ? LayoutOptions.Start : LayoutOptions.End;
        VerticalOptions = LayoutOptions.Start;
        // Extend the tab slightly past its wall so the flat wall-side stroke is hidden and the
        // tab appears truly flush. WallOverlapDp is applied to the physical wall side (left/right).
        Margin = isLeft
            ? new Thickness(-WallOverlapDp, TopGap + TabTopInset, 0, 0)
            : new Thickness(0, TopGap + TabTopInset, -WallOverlapDp, 0);
        WidthRequest = TabCollapsedWidth;
        HeightRequest = TabCollapsedHeight;
        InputTransparent = hidden;

        _root.WidthRequest = TabCollapsedWidth;
        _root.HeightRequest = TabCollapsedHeight;
        _root.InputTransparent = hidden;
    }

    /// <summary>
    ///     Restores the outer ContentView and inner _root to fill the parent so the slide
    ///     animation, panel card, and backdrop tap-to-close all have the room they need.
    /// </summary>
    private void ApplyOuterAsFill(bool captureBackdropInput)
    {
        // The wrapper-level InputTransparent flag flips on every backdrop-mode change (open vs
        // collapsing); we update that on every transition but only re-apply the heavy layout
        // properties when the mode itself changes.
        var modeChange = _appliedOuterMode != OuterMode.Fill;
        if (_appliedCaptureBackdrop != captureBackdropInput || modeChange)
        {
            _appliedCaptureBackdrop = captureBackdropInput;
            // Outer wrapper stays input-OPAQUE while in Fill mode regardless of whether the
            // backdrop is on. This is required for hit-testing on Windows MAUI: when the
            // outer ContentView is InputTransparent=true the native peer is marked
            // IsHitTestVisible=false, and the platform stops dispatching pointer events into
            // descendants even when CascadeInputTransparent=false says otherwise. The result
            // is that the panel card and close-tab become un-tappable when no backdrop is in
            // play (e.g. the map's edge-panel slot). We keep CascadeInputTransparent=false so
            // _root's own InputTransparent=true still controls the empty-area click-through.
            InputTransparent = false;
            // Inner _root only captures input when a backdrop is active. With UseBackdrop=false
            // _root stays input-transparent so taps in the empty area between the panel card
            // and the host edges fall through to the layer underneath (the map). The card and
            // tab remain hit-testable because they sit inside _root with CascadeInputTransparent=false.
            _root.InputTransparent = !captureBackdropInput;
        }

        if (!modeChange) return;

        // Mode is changing to Fill — set the heavy layout properties exactly once.
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        Margin = Thickness.Zero;
        WidthRequest = -1;
        HeightRequest = -1;
        _root.WidthRequest = -1;
        _root.HeightRequest = -1;
    }

    private double ResolveMaxPanelHeight()
    {
        var availableHeight = _parentHeight - TopGap - G9LayoutMetrics.EdgeSpacing;
        if (double.IsNaN(availableHeight) || double.IsInfinity(availableHeight) || availableHeight <= 0)
        {
            availableHeight = _parentHeight > 0 ? _parentHeight * DefaultMaxPanelHeightRatio : DefaultMaxPanelHeightAbsoluteDp;
        }

        var ratio = MaxPanelHeightRatio;
        if (ratio <= 0 || ratio > 1 || double.IsNaN(ratio))
        {
            ratio = DefaultMaxPanelHeightRatio;
        }

        var fromRatio = _parentHeight * ratio;
        var fromAbsolute = MaxPanelHeight > 0 ? MaxPanelHeight : double.PositiveInfinity;
        var cap = Math.Min(fromRatio, fromAbsolute);
        cap = Math.Min(cap, availableHeight);

        return Math.Max(TabCollapsedHeight, cap);
    }

    /// <summary>
    ///     One-shot slide-in for the collapsed tab (first time it appears in tab-only mode after
    ///     attach or after <see cref="ShowCollapsedTab" /> becomes true again).
    /// </summary>
    private void TryBeginTabEntranceAnimation(bool isLeft)
    {
        if (!ShowCollapsedTab || _tabEntranceConsumed)
        {
            return;
        }

        _tabEntranceConsumed = true;
        _tabEntranceAnimating = true;
        this.AbortAnimation(TabEntranceAnimationName);

        var w = _tabBorder.WidthRequest;
        if (w <= 0)
        {
            w = TabCollapsedWidth;
        }

        var overshoot = CollapsedExtraOffset;
        var fromX = isLeft ? -(w + overshoot) : w + overshoot;
        // Pre-position off-screen BEFORE the tab becomes visible so it never flashes
        // at its final position for even one frame before the entrance animation starts.
        _tabBorder.TranslationX = fromX;
        // Make visible NOW — after TranslationX is set — so the first rendered frame
        // already shows the tab at the off-screen start position, not at the center of the page.
        _tabBorder.IsVisible = true;
        _tabBorder.InputTransparent = false;
        var fromXCaptured = fromX;
        var anim = new Animation(tt => { _tabBorder.TranslationX = fromXCaptured * (1 - tt); }, 0, 1, Easing.CubicOut);
        anim.Commit(this, TabEntranceAnimationName, 16, TabEntranceDurationMs, finished: (_, canceled) =>
        {
            _tabEntranceAnimating = false;
            if (!canceled)
            {
                _tabBorder.TranslationX = 0;
            }
        });
    }

    #endregion

    #region Animation

    private void AnimateToggle(bool open)
    {
        var version = ++_animationVersion;
        this.AbortAnimation(PanelAnimationName);
        this.AbortAnimation(PanelHeightAnimationName);
        this.AbortAnimation(MenuContentAnimationName);
        this.AbortAnimation(TabEntranceAnimationName);
        this.AbortAnimation(PanelContentFadeAnimationName);
        _tabEntranceAnimating = false;

        // If closing while the first-open spinner is still active, reset to normal content display.
        if (!open && !_hasOpenedOnce && _panelSpinnerContainer.IsVisible)
        {
            _firstOpenPreSizeActive = false;
            _panelSpinner.IsRunning = false;
            _panelSpinner.IsVisible = false;
            _panelSpinnerContainer.IsVisible = false;
            _panelScrollView.IsVisible = true;
            _panelCard.HeightRequest = -1;
        }

        _isAnimating = true;

        var from = _animProgress;
        var to = open ? 1.0 : 0.0;
        var duration = open
            ? (OpenAnimationDuration > 0 ? OpenAnimationDuration : DefaultOpenAnimationDurationMs)
            : (CloseAnimationDuration > 0 ? CloseAnimationDuration : DefaultCloseAnimationDurationMs);

        if (!open && from > 0)
        {
            Closing?.Invoke(this, EventArgs.Empty);
        }

        // Reset content opacity. On open we fade content in over the first part of the slide.
        // On close we fade content out over the FIRST part — by the time the panel approaches
        // the wall (the most expensive frames) the content is already invisible and the panel
        // composites as a flat colored rectangle, eliminating the late-close stutter.
        this.AbortAnimation(PanelContentFadeAnimationName);
        if (_panelCardContent is not null)
        {
            _panelCardContent.Opacity = 1;
        }

        // Show spinner on first open — defers heavy content rendering until the slide completes.
        // We ALSO pre-measure the real content and pre-size the panel to that height so the
        // spinner sits at the panel's final on-screen size. Without this the panel would open
        // at the spinner's 72dp minimum and then grow when content is swapped in — a visible
        // height jump that read as a layout glitch on first open. Pre-sizing makes the open
        // arrive at the correct height in one motion.
        if (open && !_hasOpenedOnce)
        {
            PreSizePanelForFirstOpen();

            _panelScrollView.IsVisible = false;
            _panelSpinnerContainer.IsVisible = true;
            _panelSpinner.IsVisible = true;
            _panelSpinner.IsRunning = true;
        }

        // Close-time content fade. Run as a separate, named animation that finishes well
        // before the slide does (35% of the close duration by default). The slide can then run
        // with no visible internal content for its final 65% — that's where Easing.CubicIn
        // applies the most acceleration and where the previous version dropped frames most.
        if (!open && from > 0.05 && _panelCardContent is not null)
        {
            var fadeDuration = (uint)Math.Max(
                ContentFadeOnCloseMinMs,
                duration * ContentFadeOnCloseFraction);
            var startOpacity = _panelCardContent.Opacity;
            new Animation(v => _panelCardContent.Opacity = v, startOpacity, 0, Easing.CubicIn)
                .Commit(this, PanelContentFadeAnimationName, 16, fadeDuration);
        }

        var easing = open ? Easing.CubicOut : Easing.CubicIn;

        var anim = new Animation(v =>
        {
            _animProgress = v;
            ApplyAnimProgress(v);
        }, from, to, easing);

        ApplyAnimProgress(from);

        anim.Commit(this, PanelAnimationName, 16, duration, finished: (_, canceled) =>
        {
            if (canceled || version != _animationVersion)
            {
                return;
            }

            _isAnimating = false;
            _animProgress = to;
            ApplyAnimProgress(to);

            // Sync the IsOpen property without re-triggering animation.
            if (IsOpen != open)
            {
                _suppressIsOpenChanged = true;
                try
                {
                    IsOpen = open;
                }
                finally
                {
                    _suppressIsOpenChanged = false;
                }
            }

            if (open)
            {
                // Swap spinner for real content on first open. The panel was pre-sized to the
                // measured content height before the slide started (see PreSizePanelForFirstOpen),
                // so the swap is a 1:1 replacement at the same height — no growth animation
                // needed and no visible height jump. We just release HeightRequest to -1 so
                // future content changes (nested menu navigation) can resize naturally.
                if (!_hasOpenedOnce)
                {
                    _hasOpenedOnce = true;
                    _firstOpenPreSizeActive = false;
                    _panelSpinner.IsRunning = false;
                    _panelSpinner.IsVisible = false;
                    _panelSpinnerContainer.IsVisible = false;
                    _panelScrollView.IsVisible = true;
                    // Release the explicit height and switch the layout bounds back to AutoSize
                    // now that the real content is visible and can drive the card height naturally.
                    _panelCard.HeightRequest = -1;
                    AbsoluteLayout.SetLayoutFlags(_panelCard, AbsoluteLayoutFlags.None);
                    AbsoluteLayout.SetLayoutBounds(_panelCard,
                        new Rect(_panelExpandedX, TopGap, _appliedPanelWidth, AbsoluteLayout.AutoSize));
                }

                Opened?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                Closed?.Invoke(this, EventArgs.Empty);
            }
        });
    }

    #endregion

    #region Interaction

    private void OnTabTapped()
    {
        // Tab toggles open/closed, so re-entry during animation could close-then-reopen.
        // Guard with both the animating flag and the shared debounce window.
        if (_isAnimating || !TryAcceptInteraction())
        {
            return;
        }

        IsOpen = !IsOpen;
    }

    private void OnBackdropTapped()
    {
        // Backdrop tap only ever closes (no toggle), so animation re-entry is harmless and the
        // existing IsOpen check makes it idempotent. Skip the _isAnimating gate so the user
        // always has a way to dismiss the panel even if a previous animation didn't finalize.
        if (!TryAcceptInteraction())
        {
            return;
        }

        if (UseBackdrop && EnableOutsideTapToClose && IsOpen)
        {
            IsOpen = false;
        }
    }

    /// <summary>
    ///     Shared debounce gate for tab/backdrop taps. On Android the same logical tap can
    ///     surface on both recognizers (and gesture state can leak across mid-animation layout
    ///     shifts); rejecting taps within a short window of the last accepted interaction keeps
    ///     each user tap from triggering both handlers.
    /// </summary>
    private bool TryAcceptInteraction()
    {
        var now = DateTimeOffset.UtcNow;
        if ((now - _lastInteractionAt).TotalMilliseconds < InteractionDebounceMs)
        {
            return false;
        }

        _lastInteractionAt = now;
        return true;
    }

    #endregion

    #region Menu list rendering

    /// <summary>
    ///     Builds and displays a list of <see cref="G9EdgeMenuItem" /> inside the panel scroll area.
    ///     <paramref name="direction" /> drives the slide transition: Forward pushes a sub-list in
    ///     from the inactive side, Back pulls the parent list back from the opposite side, None
    ///     replaces the content immediately (used for the initial render and theme refreshes).
    /// </summary>
    internal void ShowMenuList(IList<G9EdgeMenuItem> items, bool isRoot, G9MenuTransitionDirection direction)
    {
        _currentMenuList = items;

        var theme = G9Palette.Current;
        var culturalFont = ResolveCulturalFont();

        // ── Sticky header (outside the ScrollView) ──────────────────────────────────────
        // The header is placed in _panelStickyHeader so it stays fixed at the top of the
        // card while the list items scroll beneath it. Height matches the close-button row
        // (TabExpandedSize + TabTopInset) so the header text aligns with the × button visually.
        _panelStickyHeaderInner.Children.Clear();

        if (_currentMenuHeader is { IsEmpty: false } header)
        {
            if (header.CustomView is not null)
            {
                // Detach from previous parent before re-adding (Android "child already has parent").
                DetachViewFromParent(header.CustomView);
                _panelStickyHeaderCustomView = header.CustomView;
                _panelStickyHeaderInner.Children.Add(header.CustomView);
                _panelStickyHeader.IsVisible = true;
                _panelStickyHeaderDivider.IsVisible = true;
            }
            else
            {
                var text = header.Text;
                if (string.IsNullOrEmpty(text) && header.LocalizationKey is not null)
                    text = ResolveLocalizedText(header.LocalizationKey);

                if (!string.IsNullOrEmpty(text))
                {
                    _panelStickyHeaderLabel.Text = text;
                    _panelStickyHeaderLabel.FontFamily = culturalFont;
                    _panelStickyHeaderInner.Children.Add(_panelStickyHeaderLabel);
                    _panelStickyHeader.IsVisible = true;
                    _panelStickyHeaderDivider.IsVisible = true;
                }
                else
                {
                    _panelStickyHeader.IsVisible = false;
                    _panelStickyHeaderDivider.IsVisible = false;
                }
            }
        }
        else
        {
            _panelStickyHeader.IsVisible = false;
            _panelStickyHeaderDivider.IsVisible = false;
        }

        // ── Scrollable list content ──────────────────────────────────────────────────────
        var stack = new VerticalStackLayout
        {
            Spacing = 0,
            Padding = new Thickness(6, 6, 6, 10)
        };

        if (!isRoot)
        {
            var backItem = BuildBackItem(theme, culturalFont);
            stack.Children.Add(backItem);
        }

        foreach (var item in items)
        {
            var row = BuildMenuItemRow(item, theme, culturalFont);
            stack.Children.Add(row);

            if (item.ShowDividerBelow)
            {
                stack.Children.Add(new BoxView
                {
                    HeightRequest = 1,
                    BackgroundColor = G9EdgePanelColors.MenuItemDivider(theme),
                    Margin = new Thickness(8, 2)
                });
            }
        }

        if (direction == G9MenuTransitionDirection.None || _panelContentHost.Children.Count == 0)
        {
            var frozenRecorded = TryFreezePanelCardHeight(out var frozen) ? frozen : 0d;
            ReplaceContentHostChild(stack);
            var hg = ++_panelHeightAnimGeneration;
            _ = RunPanelHeightAlignToContentAsync(frozenRecorded, stack, hg);
            return;
        }

        TransitionMenuContent(stack, direction);
    }

    private View? BuildMenuHeader(G9EdgeMenuHeader header, G9Palette theme, string font)
    {
        if (header.IsEmpty)
        {
            return null;
        }

        if (header.CustomView is not null)
        {
            // Same instance can be reused on G9EdgeMenuItem.SubMenuHeader; detach before re-adding
            // so Android never sees "child already has a parent" while the outgoing list still holds it.
            DetachViewFromParent(header.CustomView);
            return header.CustomView;
        }

        var text = header.Text;
        if (string.IsNullOrEmpty(text) && header.LocalizationKey is not null)
        {
            text = ResolveLocalizedText(header.LocalizationKey);
        }

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return new Label
        {
            Text = text,
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            FontFamily = font,
            TextColor = G9EdgePanelColors.TitleText(theme),
            Margin = new Thickness(0),
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            InputTransparent = true
        };
    }

    private async void TransitionMenuContent(View incoming, G9MenuTransitionDirection direction)
    {
        var gen = ++_menuTransitionGeneration;
        this.AbortAnimation(MenuContentAnimationName);
        this.AbortAnimation(PanelHeightAnimationName);

        // Remove stale layers from an interrupted transition (keep the topmost current list).
        while (_panelContentHost.Children.Count > 1)
            _panelContentHost.Children.RemoveAt(0);

        var outgoing = _panelContentHost.Children.OfType<View>().LastOrDefault();

        // Freeze the card at its current height BEFORE we touch the tree. This anchors the visual
        // size so the cross-fade can run without the card jumping. If we leave HeightRequest=-1
        // the card auto-sizes to max(outgoing, incoming) the moment incoming is added — which
        // would expand-then-shrink for shorter sub-lists, the exact "jumping" behaviour we're
        // trying to eliminate.
        var frozenRecorded = TryFreezePanelCardHeight(out var frozen) ? frozen : 0d;
        var startH = frozenRecorded > 1
            ? frozenRecorded
            : (_panelCard.Height > 1 ? _panelCard.Height : 0);

        // Attach incoming BEFORE measuring. On Android (and partially on iOS), a view that has
        // no parent yet has no platform handler and no font resolution context — Measure() on
        // such a view typically returns the height of just the layout chrome (close button row),
        // which is exactly what made the panel collapse to "just the close button" mid-transition
        // before snapping back to the real height when the layout pass corrected it.
        incoming.Opacity = 0;
        _panelContentHost.Children.Add(incoming);

        // Yield one dispatcher pass so the platform handlers / font resolution / measure tree
        // catch up with the new child. After this the Measure call returns the real size.
        await _panelContentHost.Dispatcher.DispatchAsync(static () => { });
        if (gen != _menuTransitionGeneration) return;

        var targetH = MeasureHostedContentHeight(incoming);
        if (targetH < TabCollapsedHeight) targetH = TabCollapsedHeight;
        if (startH < 1) startH = targetH;

        var expanded = IsOpen || _animProgress > 0.05;
        var hgParallel = ++_panelHeightAnimGeneration;

        if (expanded)
        {
            // Tween the card height in parallel with the cross-fade. We hold HeightRequest at
            // the tween's final value (targetH) when finished — releasing to -1 would let the
            // layout pass briefly snap if the natural measure differs by a sub-pixel, which on
            // Android draws as a 1-frame flicker. The next stable layout (after outgoing is
            // removed below) releases HeightRequest cleanly.
            if (Math.Abs(startH - targetH) >= 3)
            {
                _panelCard.HeightRequest = startH;
                var s = startH;
                var t0 = targetH;
                new Animation(t => _panelCard.HeightRequest = s + (t0 - s) * t, 0, 1, Easing.CubicOut)
                    .Commit(this, PanelHeightAnimationName, 16, PanelHeightAnimationDurationMs,
                        finished: (_, canceled) =>
                        {
                            if (canceled || hgParallel != _panelHeightAnimGeneration) return;
                            _panelCard.HeightRequest = t0;
                        });
            }
            else
            {
                _panelCard.HeightRequest = targetH;
            }
        }

        // One named slot drives both fade-out and fade-in — keeps them perfectly in sync on every
        // frame and ensures AbortAnimation stops both simultaneously (no fire-and-forget divergence).
        var inTcs = new TaskCompletionSource<bool>();
        new Animation(t =>
            {
                if (outgoing is not null) outgoing.Opacity = 1 - t;
                incoming.Opacity = t;
            }, 0, 1, Easing.CubicInOut)
            .Commit(this, MenuContentAnimationName, 16, MenuTransitionDurationMs,
                finished: (_, canceled) =>
                {
                    if (gen == _menuTransitionGeneration)
                    {
                        if (outgoing is not null) outgoing.Opacity = 0;
                        incoming.Opacity = 1;
                    }
                    inTcs.TrySetResult(!canceled);
                });

        await inTcs.Task;
        if (gen != _menuTransitionGeneration) return;

        if (outgoing is not null && ReferenceEquals(outgoing.Parent, _panelContentHost))
            _panelContentHost.Children.Remove(outgoing);

        incoming.Opacity = 1;

        // Now that outgoing is gone the card can release the explicit height back to auto-size.
        // The auto-size value matches targetH (we measured it after attach), so the release is
        // visually a no-op — but it lets later content changes resize naturally.
        if (hgParallel == _panelHeightAnimGeneration)
        {
            _panelCard.HeightRequest = -1;
        }

        if (!expanded)
        {
            var hgFallback = ++_panelHeightAnimGeneration;
            _ = RunPanelHeightAlignToContentAsync(0, incoming, hgFallback);
        }
    }

    /// <summary>
    ///     Locks <see cref="_panelCard" /> height to its current measured value before swapping or
    ///     removing menu layers so intermediate layout passes cannot shrink the card early.
    /// </summary>
    private bool TryFreezePanelCardHeight(out double frozen)
    {
        frozen = _panelCard.Height;
        if (frozen <= 1 || !(IsOpen || _animProgress > 0.01))
        {
            return false;
        }

        _panelCard.HeightRequest = frozen;
        return true;
    }

    /// <summary>
    ///     Measures hosted menu/content and tweens card height from a frozen start to the target.
    ///     Pass <paramref name="frozenStartHeightOrZero" /> = 0 when no freeze was applied.
    /// </summary>
    private async Task RunPanelHeightAlignToContentAsync(double frozenStartHeightOrZero, View contentRoot,
        int generation)
    {
        await Task.Delay(MenuHeightSettleDelayMs);
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (generation != _panelHeightAnimGeneration)
            {
                return;
            }

            if (contentRoot.Parent != _panelContentHost)
            {
                _panelCard.HeightRequest = -1;
                return;
            }

            var targetH = MeasureHostedContentHeight(contentRoot);
            var startH = frozenStartHeightOrZero > 1 ? frozenStartHeightOrZero : _panelCard.Height;
            if (startH <= 1)
            {
                startH = targetH;
            }

            var expanded = IsOpen || _animProgress > 0.05;
            if (!expanded)
            {
                _panelCard.HeightRequest = -1;
                return;
            }

            if (Math.Abs(startH - targetH) < 3)
            {
                _panelCard.HeightRequest = -1;
                return;
            }

            this.AbortAnimation(PanelHeightAnimationName);
            _panelCard.HeightRequest = startH;

            var anim = new Animation(t =>
            {
                _panelCard.HeightRequest = startH + (targetH - startH) * t;
            }, 0, 1, Easing.CubicOut);

            var tcs = new TaskCompletionSource();
            anim.Commit(this, PanelHeightAnimationName, 16, PanelHeightAnimationDurationMs,
                finished: (_, canceled) =>
                {
                    _panelCard.HeightRequest = -1;
                    if (canceled)
                    {
                        tcs.TrySetCanceled();
                    }
                    else
                    {
                        tcs.TrySetResult();
                    }
                });

            try
            {
                await tcs.Task.ConfigureAwait(true);
            }
            catch (TaskCanceledException)
            {
                // Aborted by a newer panel interaction — height is already reset in finished.
            }
        });
    }

    private double MeasureHostedContentHeight(View root)
    {
        var w = _panelCard.Width;
        if (w <= 0)
        {
            w = _parentWidth * Math.Clamp(WidthRatio, 0, 1);
        }

        if (w <= 0)
        {
            w = 200;
        }

        var measured = root.Measure(w, double.PositiveInfinity);
        var h = measured.Height;
        if (double.IsNaN(h) || h <= 0)
        {
            h = TabCollapsedHeight;
        }

        // Add the sticky header height. The header lives outside the ScrollView (in row 0 of
        // _panelCardContent) so it is not included in the scrollable content measurement.
        // Use the already-laid-out height when available; fall back to MinimumHeightRequest.
        if (_panelStickyHeader.IsVisible)
        {
            var headerH = _panelStickyHeader.Height > 1
                ? _panelStickyHeader.Height
                : _panelStickyHeader.MinimumHeightRequest;
            if (headerH > 0) h += headerH;
            // +1 for the separator line row
            h += 1;
        }

        return Math.Clamp(h, TabCollapsedHeight, ResolveMaxPanelHeight());
    }

    /// <summary>
    ///     Pre-measures the real content view that will be displayed after the first open and
    ///     pins the panel's <see cref="VisualElement.HeightRequest"/> to that value before the
    ///     slide animation starts. The spinner then occupies the panel at the panel's final
    ///     on-screen size, so when the spinner is swapped for the real content after the slide
    ///     there's no perceptible height change.
    ///     <para>
    ///         For list menus we use a <strong>deterministic estimate</strong> built from
    ///         <see cref="MenuItems"/> count plus header / back-item / divider sizes. The
    ///         alternative (calling the platform Measure pass) is unreliable on Android before
    ///         the panel has a Width and before all child handlers + font resolution are
    ///         ready — that path returned a tiny value that left the panel opening at the
    ///         spinner's 72dp minimum.
    ///     </para>
    ///     <para>
    ///         For custom <see cref="PanelContent"/> we keep the measure-based path because we
    ///         can't predict an arbitrary view's height — but custom content is typically
    ///         self-sized via WidthRequest/HeightRequest already, so Measure works.
    ///     </para>
    /// </summary>
    private void PreSizePanelForFirstOpen()
    {
        // Invalidate any in-flight RunPanelHeightAlignToContentAsync. When the panel was just
        // configured (MenuItems / PanelContent setter ran in the same frame as the helper
        // attaches the panel), that setter already started a 48ms-delayed height-align task.
        // Without this bump that task wakes up mid-slide, sees its early-return condition
        // (start ≈ target height), sets HeightRequest = -1, and the card collapses to the
        // spinner's 72dp because _panelScrollView.IsVisible was just set to false. The card
        // would visibly shrink for the rest of the open. Bumping the generation makes the
        // stale task's gen-check fail and return without touching HeightRequest.
        _panelHeightAnimGeneration++;
        this.AbortAnimation(PanelHeightAnimationName);

        double estimated = 0;
        var maxH = ResolveMaxPanelHeight();

        // ── List menu path: estimate from the items collection. ──
        if (MenuItems is { Count: > 0 } items)
        {
            // Stack outer Padding (6 + 10 = 16dp from the VerticalStackLayout in ShowMenuList).
            const double stackTopBottomPadding = 6 + 10;

            // Sticky header row: (TabExpandedSize + TabTopInset) tall (only when a header is set).
            if (MenuHeader is { IsEmpty: false })
            {
                estimated += TabExpandedSize + TabTopInset;
            }

            // Each item row.
            estimated += items.Count * MenuItemHeight;

            // Dividers (1dp height + 4dp margin) below items that opt-in.
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i].ShowDividerBelow) estimated += 1 + 4;
            }

            estimated += stackTopBottomPadding;
        }
        else if (PanelContent is not null)
        {
            // ── Custom content path: rely on Measure. The PanelContent was added to the host
            //   when the bindable was set, so the handler is attached and Measure should
            //   return real values for self-sized custom views.
            estimated = MeasureHostedContentHeight(PanelContent);
        }
        else
        {
            return;
        }

        if (estimated <= TabCollapsedHeight)
        {
            return;
        }

        // Cap at the panel's max height. Beyond that the inner ScrollView takes over.
        estimated = Math.Clamp(estimated, TabCollapsedHeight, maxH);
        _panelCard.HeightRequest = estimated;
        _firstOpenPreSizeActive = true;
    }

    private View BuildBackItem(G9Palette theme, string font)
    {
        var isRtl = ResolveIsContentRtl();
        var backIcon = new G9IconView {
            Icon = isRtl ? G9Glyphs.ChevronForward : G9Glyphs.ChevronBack,
            Size = MenuItemIconSize,
            Color = G9EdgePanelColors.MenuItemIcon(theme),
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        var backLabel = new Label
        {
            Text = ResolveLocalizedText("Back") ?? "Back",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            FontFamily = font,
            TextColor = G9EdgePanelColors.MenuItemText(theme),
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center,
            InputTransparent = true
        };

        var row = new HorizontalStackLayout
        {
            Spacing = MenuItemSpacing,
            Padding = new Thickness(10, 8),
            HeightRequest = BackItemHeight,
            Children = { backIcon, backLabel }
        };

        var container = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = MenuItemCornerRadius },
            Content = row
        };

        container.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(OnMenuBack)
        });

        return container;
    }

    private View BuildMenuItemRow(G9EdgeMenuItem item, G9Palette theme, string font)
    {
        var hasLeading = item.Icon.HasValue
                         || !string.IsNullOrEmpty(item.ImageSource)
                         || !string.IsNullOrEmpty(item.Emoji);
        var hasTrailing = item.NextList is { Count: > 0 };

        // Grid: [Auto leading] [* text] [Auto trailing]
        var colDefs = new ColumnDefinitionCollection();
        if (hasLeading)
            colDefs.Add(new ColumnDefinition(GridLength.Auto));
        colDefs.Add(new ColumnDefinition(GridLength.Star));
        if (hasTrailing)
            colDefs.Add(new ColumnDefinition(GridLength.Auto));

        var row = new Grid
        {
            ColumnDefinitions = colDefs,
            ColumnSpacing = MenuItemSpacing,
            Padding = new Thickness(10, 0),
            HeightRequest = MenuItemHeight,
            VerticalOptions = LayoutOptions.Center
        };

        var col = 0;

        // ── Leading visual (icon / image / emoji) ──
        var themeIcon = G9EdgePanelColors.MenuItemIcon(theme);
        var themeText = G9EdgePanelColors.MenuItemText(theme);

        if (item.Icon.HasValue)
        {
            var iconTint = item.IconColor ?? themeIcon;
            var icon = new G9IconView {
                Icon = item.Icon.Value,
                Size = MenuItemIconSize,
                Color = item.IsEnabled ? iconTint : iconTint.WithAlpha(0.38f),
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            };
            Grid.SetColumn(icon, col);
            row.Children.Add(icon);
            col++;
        }
        else if (!string.IsNullOrEmpty(item.ImageSource))
        {
            var img = G9ImageFactory.Create(ImageSource.FromFile(item.ImageSource!), G9EdgePanelMetrics.MenuItemIconSize);
            Grid.SetColumn(img, col);
            row.Children.Add(img);
            col++;
        }
        else if (!string.IsNullOrEmpty(item.Emoji))
        {
            var emoji = new Label
            {
                Text = item.Emoji,
                FontSize = MenuItemEmojiSize,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                WidthRequest = MenuItemIconSize + 4,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            };
            Grid.SetColumn(emoji, col);
            row.Children.Add(emoji);
            col++;
        }

        // ── Text ──
        var displayText = item.Text
                          ?? (item.LocalizedTextKey is not null ? ResolveLocalizedText(item.LocalizedTextKey) : null)
                          ?? string.Empty;
        var textTint = item.TextColor ?? themeText;
        var label = new Label
        {
            Text = displayText,
            FontSize = 14,
            FontFamily = font,
            TextColor = item.IsEnabled ? textTint : textTint.WithAlpha(0.38f),
            VerticalOptions = LayoutOptions.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            HorizontalOptions = LayoutOptions.Fill,
            InputTransparent = true
        };
        Grid.SetColumn(label, col);
        row.Children.Add(label);
        col++;

        // ── Trailing chevron for nested list ──
        if (hasTrailing)
        {
            var isRtl = ResolveIsContentRtl();
            var chevronTint = (item.IconColor ?? themeIcon).WithAlpha(0.5f);
            var chevron = new G9IconView {
                Icon = isRtl ? G9Glyphs.ChevronBack : G9Glyphs.ChevronForward,
                Size = 18,
                Color = chevronTint,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            };
            Grid.SetColumn(chevron, col);
            row.Children.Add(chevron);
        }

        var container = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = item.BackgroundColor ?? Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = MenuItemCornerRadius },
            Content = row,
            Opacity = item.IsEnabled ? 1 : 0.5
        };

        if (item.IsEnabled)
        {
            container.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => OnMenuItemTapped(item))
            });
        }

        return container;
    }

    private void OnMenuItemTapped(G9EdgeMenuItem item)
    {
        // Diagnostic: any tap reaching here is a confirmed gesture-recognizer hit. If a
        // consumer reports "menu rows do nothing" and this line never fires, the problem
        // is gesture dispatch (InputTransparent ancestor / disabled row), NOT the click
        // handler. See G9EdgePanel.md "Pitfall: menu item tap does nothing".
        System.Diagnostics.Debug.WriteLine(
            $"[G9EdgePanel] Tap: Text={item.Text ?? item.LocalizedTextKey ?? "<custom>"} "
            + $"HasNextList={item.NextList is { Count: > 0 }} "
            + $"HasClicked={item.Clicked is not null} "
            + $"HasCommand={item.Command is not null} "
            + $"CloseAfterClick={item.CloseAfterClick} "
            + $"IsEnabled={item.IsEnabled}");

        // Invoke callback.
        item.Clicked?.Invoke(item);
        if (item.Command?.CanExecute(item.CommandParameter) == true)
        {
            item.Command.Execute(item.CommandParameter);
        }

        // Navigate to sub-list if provided. CloseAfterClick is intentionally ignored when
        // a sub-list exists — drilling in is the visual feedback the user expects, and
        // closing here would short-circuit navigation entirely.
        if (item.NextList is { Count: > 0 })
        {
            if (_currentMenuList is not null)
            {
                _menuStack.Push(new MenuNavFrame(_currentMenuList, _currentMenuHeader));
            }

            _currentMenuHeader = item.SubMenuHeader;
            ShowMenuList(item.NextList, isRoot: false, G9MenuTransitionDirection.Forward);
            return;
        }

        // Leaf-action close: lets a "zoom to feature" / "select item" tap reveal the
        // host (map) underneath without the user having to manually collapse the panel.
        if (item.CloseAfterClick && IsOpen)
        {
            Close();
        }
    }

    private void OnMenuBack()
    {
        if (_menuStack.Count > 0)
        {
            var frame = _menuStack.Pop();
            _currentMenuHeader = frame.Header;
            ShowMenuList(frame.Items, isRoot: _menuStack.Count == 0, G9MenuTransitionDirection.Back);
        }
    }

    #endregion

    #region Public API

    /// <summary>Opens the panel with animation.</summary>
    public void Open()
    {
        IsVisible = true;
        if (IsOpen)
        {
            if (_animProgress < 1 || _isAnimating)
            {
                AnimateToggle(true);
            }

            return;
        }

        IsOpen = true;
    }

    /// <summary>Closes the panel with animation.</summary>
    public void Close()
    {
        if (!IsOpen)
        {
            if (_animProgress > 0 || _isAnimating)
            {
                AnimateToggle(false);
            }
            else
            {
                ApplyAnimProgress(0);
            }

            return;
        }

        IsOpen = false;
    }

    /// <summary>Toggles the panel open/closed.</summary>
    public void Toggle()
    {
        IsOpen = !IsOpen;
    }

    /// <summary>
    ///     Programmatically replaces the displayed menu list (pushes current as parent for back navigation).
    /// </summary>
    public void NavigateToSubList(IList<G9EdgeMenuItem> subList, G9EdgeMenuHeader? subMenuHeader = null)
    {
        if (_currentMenuList is not null)
        {
            _menuStack.Push(new MenuNavFrame(_currentMenuList, _currentMenuHeader));
        }

        _currentMenuHeader = subMenuHeader;
        ShowMenuList(subList, isRoot: false, G9MenuTransitionDirection.Forward);
    }

    /// <summary>Navigates the menu list back to the root level.</summary>
    public void NavigateToRoot()
    {
        _menuStack.Clear();
        if (MenuItems is { Count: > 0 })
        {
            _currentMenuHeader = MenuHeader;
            ShowMenuList(MenuItems, isRoot: true, G9MenuTransitionDirection.Back);
        }
    }

    /// <summary>
    ///     Forces a full rebuild of the currently visible menu level from the underlying
    ///     <see cref="MenuItems"/> collection. Call this after mutating individual
    ///     <see cref="G9EdgeMenuItem"/> properties (e.g. toggling <see cref="G9EdgeMenuItem.IsEnabled"/>,
    ///     changing <see cref="G9EdgeMenuItem.Text"/>, or updating icon/color) when the list
    ///     itself does not implement <see cref="System.Collections.Specialized.INotifyCollectionChanged"/>.
    ///     <para>
    ///         If the list is an <see cref="System.Collections.ObjectModel.ObservableCollection{T}"/>
    ///         (or any <see cref="System.Collections.Specialized.INotifyCollectionChanged"/> implementation),
    ///         structural changes (add/remove/replace/reset) are detected automatically and this
    ///         method is only needed for in-place property mutations on existing items.
    ///     </para>
    /// </summary>
    public void RefreshMenuItems()
    {
        if (_currentMenuList is null) return;
        ShowMenuList(_currentMenuList, isRoot: _menuStack.Count == 0, G9MenuTransitionDirection.None);
    }

    #endregion

    #region Helpers

    private static double Lerp(double a, double b, double t) => a + (b - a) * t;

    /// <summary>
    ///     The visual edge the panel attaches to. Direct mapping from <see cref="Side" /> —
    ///     RTL/LTR does not invert this because the panel's root is locked to LeftToRight, so
    ///     the consumer always knows what they get when they pass <c>Side</c>.
    /// </summary>
    private bool IsLeftSide() => Side == G9EdgeSide.Left;

    /// <summary>
    ///     Flow direction for panel content (text labels, menu rows, back arrow). Uses
    ///     <see cref="ContentFlowDirection" /> or matches <see cref="G9Culture" />.
    /// </summary>
    private FlowDirection ResolvePanelContentFlowDirection()
    {
        return ContentFlowDirection switch
        {
            G9EdgePanelContentDirection.LeftToRight => FlowDirection.LeftToRight,
            G9EdgePanelContentDirection.RightToLeft => FlowDirection.RightToLeft,
            _ => G9Culture.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
    }

    private bool ResolveIsContentRtl() => ResolvePanelContentFlowDirection() == FlowDirection.RightToLeft;

    private static string ResolveCulturalFont()
    {
        return G9Culture.ResolveFontFamily() ?? string.Empty;
    }

    private static string? ResolveLocalizedText(string key)
    {
        // A caller may supply a resource KEY instead of literal text (G9EdgeMenuItem.TextKey), which is
        // what lets one menu definition re-localize on a culture flip. The suite has no resource file
        // of its own, so the key is resolved through the consumer-supplied provider.
        return G9Strings.Resolve(key);
    }

    /// <summary>
    ///     Removes <paramref name="view" /> from its current parent so it can be inserted into a
    ///     new menu stack (required for reused <see cref="G9EdgeMenuHeader.CustomView" /> on Android).
    /// </summary>
    private static void DetachViewFromParent(View view)
    {
        switch (view.Parent)
        {
            case Layout layout:
                layout.Children.Remove(view);
                return;
            case ContentView cv when ReferenceEquals(cv.Content, view):
                cv.Content = null;
                return;
            case Border b when ReferenceEquals(b.Content, view):
                b.Content = null;
                return;
            case ScrollView sv when ReferenceEquals(sv.Content, view):
                sv.Content = null;
                return;
            case ContentPage page when ReferenceEquals(page.Content, view):
                page.Content = null;
                return;
        }
    }

    #endregion
}
