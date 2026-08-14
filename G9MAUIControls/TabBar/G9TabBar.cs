using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using Microsoft.Maui.Controls.Shapes;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows.Input;
using static G9MAUIControls.TabBar.G9TabBarColorResolver;
using static G9MAUIControls.TabBar.G9TabBarMetrics;
using G9MAUIControls.Icons;

using G9MAUIControls.Controls;

namespace G9MAUIControls.TabBar;

public partial class G9TabBar : ContentView
{
    #region Constructor

    public G9TabBar()
    {
        HeightRequest = CompactControlHeight;
        MinimumHeightRequest = CompactControlHeight;
        IsClippedToBounds = false;
        BackgroundColor = Colors.Transparent;
        VerticalOptions = LayoutOptions.End;

        _themeChangedHandler = (_, _) => MainThread.BeginInvokeOnMainThread(ApplyTheme);

        _root = new Grid
        {
            IsClippedToBounds = false,
            HeightRequest = CompactControlHeight,
            MinimumHeightRequest = CompactControlHeight,
            BackgroundColor = Colors.Transparent,
            FlowDirection = FlowDirection.LeftToRight
        };

        _chromeView = new GraphicsView
        {
            Drawable = _chromeDrawable,
            InputTransparent = true,
            HeightRequest = CompactControlHeight,
            FlowDirection = FlowDirection.LeftToRight
        };

        // SkiaSharp drop shadow — drawn BEHIND the chrome so it casts the bar's notched
        // silhouette reliably on every platform (native / Sharpnado shadows can't follow the
        // concave FAB notch). Negative margin (inside the view) lets the blur bleed past the
        // bar into the page gutter. See G9TabBarShadowView.
        _shadowView = new G9TabBarShadowView
        {
            HeightRequest = CompactControlHeight,
            FlowDirection = FlowDirection.LeftToRight
        };

        _hitLayer = new AbsoluteLayout
        {
            IsClippedToBounds = false,
            InputTransparent = false,
            HeightRequest = CompactControlHeight,
            FlowDirection = FlowDirection.LeftToRight
        };

        _backdrop = CreateBackdrop();
        _hitLayer.Children.Add(_backdrop);

        _indicator = CreateIndicatorView();
        _hitLayer.Children.Add(_indicator);

        (_fabButton, _fabOuterSurface, _fabInnerSurface, _fabIconView) = CreateFabButton();

        // Shadow first (bottom of the z-stack), then chrome, then hit layer.
        _root.Children.Add(_shadowView);
        _root.Children.Add(_chromeView);
        _root.Children.Add(_hitLayer);
        Content = _root;

        ResetCenterOnMenuSelection = true;
        Items = CreateDefaultItems();
        SubMenuItems = CreateDefaultSubMenuItems();

        _activeSelectedIndex = SelectedIndex;
        _centerProgress = IsCenterFloating ? 1d : 0d;
        _chromeNotchProgress = _centerProgress;
        _openProgress = IsFabOpen ? 1d : 0d;
        _overflowProgress = IsOverflowOpen ? 1d : 0d;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;

        ApplyTheme();
        UpdateAllVisuals(false);
    }

    #endregion

    #region Fields And Properties

    private readonly Grid _root;
    private readonly GraphicsView _chromeView;
    private readonly G9TabBarShadowView _shadowView;
    private readonly AbsoluteLayout _hitLayer;
    private readonly G9TabBarChromeDrawable _chromeDrawable = new();
    private readonly G9FabPlusIconDrawable _fabPlusDrawable = new();
    private readonly List<MenuButtonParts> _bottomButtons = [];
    private readonly List<SubMenuButtonParts> _subMenuButtons = [];
    private readonly List<OverflowItemParts> _overflowItems = [];
    private readonly Border _indicator;
    private readonly BoxView _backdrop;
    private readonly Grid _fabButton;
    private readonly Border _fabOuterSurface;
    private readonly Border _fabInnerSurface;
    private readonly GraphicsView _fabIconView;
    private readonly PropertyChangedEventHandler _themeChangedHandler;

    private bool _themeHandlerAttached;
    private double _centerProgress;
    /// <summary>
    ///     Bouncy companion to <see cref="_centerProgress" /> driving the chrome notch only.
    ///     Lets the notch dip past its resting depth (and slightly under-shoot back) without
    ///     forcing the FAB body to ride the same overshoot — the FAB rides the smooth
    ///     <see cref="_centerProgress" /> so it pops up cleanly without colliding with itself.
    /// </summary>
    private double _chromeNotchProgress;
    private double _openProgress;
    private double _overflowProgress;
    private double _reservedHeight = CompactControlHeight;
    private double _lastSubMenuVisualSize = -1d;
    private double _bottomSelectionRevealProgress = 1d;
    private int _reservedHeightChangeVersion;
    private int _startupRevealVersion;
    private int _activeSelectedIndex;
    private int _revealingSelectedIndex = -1;
    private bool _applyingDefaultSelectedIndex;
    private bool _startupSelectionRevealQueued;
    private bool _indicatorPositioned;
    private double _indicatorTargetX;
    private double _indicatorTargetY;
    /// <summary>
    ///     Slot index whose <see cref="VisualElement.TranslationY" /> is currently being held
    ///     at <see cref="SelectedIndicatorDownNudgeY" />. Used to avoid re-firing the slide
    ///     animation on every visual refresh — only animate when the highlighted slot changes.
    /// </summary>
    private int _highlightedSlotIndex = -1;
    private ObservableCollection<G9TabBarItem>? _attachedItems;
    private ObservableCollection<G9TabBarItem>? _attachedSubMenuItems;

    public event EventHandler<G9TabBarSelectionChangedEventArgs>? ItemSelected;

    [AutoBindable(OnChanged = nameof(OnItemsChanged))]
    private ObservableCollection<G9TabBarItem>? _items;

    [AutoBindable(OnChanged = nameof(OnSubMenuItemsChanged))]
    private ObservableCollection<G9TabBarItem>? _subMenuItems;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnSelectedIndexChanged))]
    private int _selectedIndex;

    [AutoBindable(OnChanged = nameof(OnDefaultSelectedIndexChanged))]
    private int _defaultSelectedIndex;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnIsCenterFloatingChanged))]
    private bool _isCenterFloating;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnIsFabOpenChanged))]
    private bool _isFabOpen;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnIsOverflowOpenChanged))]
    private bool _isOverflowOpen;

    [AutoBindable] private bool _resetCenterOnMenuSelection;

    /// <summary>
    ///     Label shown on the overflow trigger ("More") slot. Defaults to the localized
    ///     <c>More</c> string from <c>AppDictionary</c>. Set this explicitly when you
    ///     need a different word, or to force a static label that ignores culture.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnOverflowTextChanged))]
    private string? _overflowText;

    /// <summary>
    ///     Index of the item that acts as the FAB (floating action button).
    ///     Set to <c>-1</c> to disable the FAB entirely — the bar becomes a plain tab bar.
    ///     Defaults to <see cref="DefaultFabIndex" /> (center slot of a 5-item menu).
    /// </summary>
    [AutoBindable(DefaultValue = "2", OnChanged = nameof(OnFabIndexChanged))]
    private int _fabIndex;

    /// <summary>True when there are more bar items than fit in the visible slots and the overflow trigger is shown.</summary>
    private bool HasOverflow => (Items?.Count ?? 0) > MaxVisibleBottomItems;

    /// <summary>Visual slot index occupied by the overflow trigger button (last visible slot).</summary>
    private int OverflowTriggerSlotIndex => MaxVisibleBottomItems - 1;

    /// <summary>
    ///     The FAB index actually honoured at runtime. When overflow is active the
    ///     trigger slot replaces any FAB that would have been there, so we collapse
    ///     conflicting / out-of-range FAB indices to <c>-1</c>.
    /// </summary>
    private int ResolvedFabIndex
    {
        get
        {
            var raw = FabIndex;
            if (raw < 0)
            {
                return -1;
            }

            if (HasOverflow && raw >= OverflowTriggerSlotIndex)
            {
                return -1;
            }

            return raw;
        }
    }

    /// <summary>True when a FAB slot is configured and reachable in the current layout.</summary>
    private bool HasFab => ResolvedFabIndex >= 0;

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == nameof(FlowDirection))
        {
            LayoutElements();
        }
    }

    #endregion

    #region Property change handlers

    private void OnItemsChanged()
    {
        if (_attachedItems is not null)
        {
            _attachedItems.CollectionChanged -= OnItemsCollectionChanged;
        }

        _attachedItems = Items;

        if (_attachedItems is not null)
        {
            _attachedItems.CollectionChanged += OnItemsCollectionChanged;
        }

        RebuildBottomButtons();
    }

    private void OnSubMenuItemsChanged()
    {
        if (_attachedSubMenuItems is not null)
        {
            _attachedSubMenuItems.CollectionChanged -= OnSubMenuItemsCollectionChanged;
        }

        _attachedSubMenuItems = SubMenuItems;

        if (_attachedSubMenuItems is not null)
        {
            _attachedSubMenuItems.CollectionChanged += OnSubMenuItemsCollectionChanged;
        }

        RebuildSubMenuButtons();
    }

    private void OnSelectedIndexChanged()
    {
        ApplySelectedIndex(SelectedIndex, !_applyingDefaultSelectedIndex, false);
    }

    private void OnDefaultSelectedIndexChanged()
    {
        ApplyDefaultSelectedIndex();
    }

    private void OnIsCenterFloatingChanged()
    {
        AnimateCenterState(IsCenterFloating);
    }

    private void OnIsFabOpenChanged()
    {
        AnimateFabOpen(IsFabOpen);
    }

    private void OnIsOverflowOpenChanged()
    {
        AnimateOverflowOpen(IsOverflowOpen);
    }

    private void OnOverflowTextChanged()
    {
        if (HasOverflow)
        {
            UpdateBottomButtonVisuals();
        }
    }

    private void OnFabIndexChanged()
    {
        // When the FAB slot changes the chrome notch and FAB button must move.
        // A full rebuild is the safest path because item-type (fab vs regular) changes.
        RebuildBottomButtons();
        UpdateAllVisuals(false);
    }

    private void OnItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildBottomButtons();
    }

    private void OnSubMenuItemsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildSubMenuButtons();
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

        ApplyTheme();
        LayoutElements();
        QueueStartupSelectionReveal();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (_themeHandlerAttached)
        {
            G9Palette.Current.PropertyChanged -= _themeChangedHandler;
            _themeHandlerAttached = false;
        }

        this.AbortAnimation(CenterStateAnimationName);
        this.AbortAnimation(NotchBounceAnimationName);
        this.AbortAnimation(OpenAnimationName);
        this.AbortAnimation(OverflowAnimationName);
        this.AbortAnimation(BottomSelectionRevealAnimationName);
        _startupRevealVersion++;
        _indicator.AbortAnimation(IndicatorAnimationName);
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        LayoutElements();
        UpdateIndicator(false);
    }

    #endregion

    #region Visual builders

    private void RebuildBottomButtons()
    {
        for (var i = 0; i < _bottomButtons.Count; i++)
        {
            _hitLayer.Children.Remove(_bottomButtons[i].Root);
        }

        _bottomButtons.Clear();

        for (var i = 0; i < _overflowItems.Count; i++)
        {
            _hitLayer.Children.Remove(_overflowItems[i].Root);
        }

        _overflowItems.Clear();

        var items = Items;
        if (items is null)
        {
            UpdateAllVisuals(false);
            return;
        }

        var totalCount = items.Count;
        var hasOverflow = totalCount > MaxVisibleBottomItems;
        var visibleCount = hasOverflow ? MaxVisibleBottomItems : totalCount;
        var triggerSlot = hasOverflow ? OverflowTriggerSlotIndex : -1;

        for (var i = 0; i < visibleCount; i++)
        {
            if (i == triggerSlot)
            {
                _bottomButtons.Add(CreateOverflowTriggerButton(i));
            }
            else
            {
                _bottomButtons.Add(CreateBottomButton(items[i], i));
            }

            _hitLayer.Children.Add(_bottomButtons[i].Root);
        }

        if (hasOverflow)
        {
            // Items folded into the overflow column: from the trigger slot index up to the end.
            for (var sourceIndex = triggerSlot; sourceIndex < totalCount; sourceIndex++)
            {
                var parts = CreateOverflowItem(items[sourceIndex], sourceIndex);
                _overflowItems.Add(parts);
                _hitLayer.Children.Add(parts.Root);
            }
        }
        else if (IsOverflowOpen)
        {
            // Collection shrunk under the threshold while open — collapse silently.
            IsOverflowOpen = false;
            _overflowProgress = 0d;
        }

        if (!_hitLayer.Children.Contains(_fabButton))
        {
            _hitLayer.Children.Add(_fabButton);
        }

        // Newly-instantiated overflow / overflow-trigger surfaces are unstyled; re-apply theme
        // so a re-assigned Items collection (e.g. on culture change) doesn't lose the glass.
        ApplyOverflowTheme(G9Palette.Current);
        _indicatorPositioned = false;
        // New buttons start at TranslationY=0; force the highlighted-slot animation to re-evaluate.
        _highlightedSlotIndex = -1;
        UpdateAllVisuals(false);
    }

    private void RebuildSubMenuButtons()
    {
        for (var i = 0; i < _subMenuButtons.Count; i++)
        {
            _hitLayer.Children.Remove(_subMenuButtons[i].Root);
        }

        _subMenuButtons.Clear();
        _lastSubMenuVisualSize = -1d;

        var items = SubMenuItems;
        if (items is null)
        {
            UpdateAllVisuals(false);
            return;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var parts = CreateSubMenuButton(items[i], i);
            _subMenuButtons.Add(parts);
            _hitLayer.Children.Add(parts.Root);
        }

        if (_hitLayer.Children.Contains(_fabButton))
        {
            _hitLayer.Children.Remove(_fabButton);
        }

        _hitLayer.Children.Add(_fabButton);
        ApplySubMenuTheme(G9Palette.Current);
        UpdateAllVisuals(false);
    }

    private MenuButtonParts CreateBottomButton(G9TabBarItem item, int index)
    {
        var icon = new G9IconView {
            Icon = item.Icon,
            Size = BottomIconSize,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        var label = new Label
        {
            Text = item.Text,
            FontSize = BottomLabelFontSize,
            FontFamily = ResolveCulturalFont(),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            FontAutoScalingEnabled = false,
            InputTransparent = true
        };

        var content = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            Children = { icon, label }
        };

        var root = new Grid { BackgroundColor = Colors.Transparent, IsClippedToBounds = false, ZIndex = 4 };
        root.Add(content);

        if (!string.IsNullOrWhiteSpace(item.AutomationId))
        {
            root.AutomationId = item.AutomationId;
        }

        SemanticProperties.SetDescription(root, item.Text);
        root.Behaviors.Add(G9PressFeedbackBehavior.For(new Command(() => OnBottomItemTapped(index))));

        return new MenuButtonParts(root, icon, label, item);
    }

    private SubMenuButtonParts CreateSubMenuButton(G9TabBarItem item, int index)
    {
        var icon = new G9IconView {
            Icon = item.Icon,
            Size = SubMenuRowIconSizeMax,
            Color = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        // Inner green accent circle — holds only the icon, primary gradient applied in ApplySubMenuTheme.
        var innerSurface = new Border
        {
            WidthRequest = SubMenuRowItemInnerSize,
            HeightRequest = SubMenuRowItemInnerSize,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = SubMenuRowItemInnerSize / 2 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = icon,
            InputTransparent = true
        };

        var label = new Label
        {
            Text = item.Text,
            FontSize = SubMenuRowFontSizeMax,
            FontAttributes = FontAttributes.Bold,
            FontFamily = ResolveCulturalFont(),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            FontAutoScalingEnabled = false,
            InputTransparent = true,
            HeightRequest = SubMenuRowLabelHeight
        };

        // Inner stack: green circle + label, both centered inside the outer glass shell.
        var stack = new VerticalStackLayout
        {
            Spacing = SubMenuRowLabelGap,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
            Children = { innerSurface, label }
        };

        // Outer glass shell — translucent rounded rectangle that mirrors the bar.
        var outerSurface = new Border
        {
            WidthRequest = SubMenuRowItemSize,
            HeightRequest = SubMenuRowCellHeight,
            StrokeThickness = BarStrokeSize,
            StrokeShape = new RoundRectangle { CornerRadius = SubMenuRowOuterCornerRadius },
            Padding = new Thickness(0, SubMenuRowTopPadding, 0, SubMenuRowBottomPadding),
            Content = stack,
            InputTransparent = true
        };

        var root = new Grid
        {
            WidthRequest = SubMenuRowItemSize,
            HeightRequest = SubMenuRowCellHeight,
            IsClippedToBounds = false,
            Opacity = 0,
            InputTransparent = true,
            ZIndex = 8
        };
        root.Add(outerSurface);

        if (!string.IsNullOrWhiteSpace(item.AutomationId))
        {
            root.AutomationId = item.AutomationId;
        }

        SemanticProperties.SetDescription(root, item.Text);
        root.GestureRecognizers.Add(CreateTapGesture(() => OnSubMenuItemTapped(index)));

        return new SubMenuButtonParts(root, outerSurface, innerSurface, icon, label, item);
    }

    private (Grid root, Border outerSurface, Border innerSurface, GraphicsView iconView) CreateFabButton()
    {
        var iconView = new GraphicsView
        {
            Drawable = _fabPlusDrawable,
            WidthRequest = FabIconSize,
            HeightRequest = FabIconSize,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        // Inner accent ring (carries the primary gradient + the +/× glyph).
        var innerSurface = new Border
        {
            WidthRequest = FabInnerSize,
            HeightRequest = FabInnerSize,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = FabInnerSize / 2 },
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Content = iconView,
            InputTransparent = true
        };

        // Outer FAB surface — translucent glass that mirrors the main bar.
        var outerSurface = new Border
        {
            WidthRequest = FabSize,
            HeightRequest = FabSize,
            StrokeThickness = BarStrokeSize,
            StrokeShape = new RoundRectangle { CornerRadius = FabSize / 2 },
            Content = innerSurface,
            InputTransparent = true
        };

        var root = new Grid
        {
            WidthRequest = FabSize,
            HeightRequest = FabSize,
            IsClippedToBounds = false,
            Opacity = 1,
            ZIndex = 12
        };
        root.Add(outerSurface);
        root.GestureRecognizers.Add(CreateTapGesture(OnFabTapped));
        SemanticProperties.SetDescription(root, "Create");

        return (root, outerSurface, innerSurface, iconView);
    }

    private MenuButtonParts CreateOverflowTriggerButton(int slotIndex)
    {
        var triggerItem = new G9TabBarItem(ResolveOverflowLabelText(), G9Glyph.Menu)
        {
            AutomationId = "G9TabBar_Overflow_Trigger"
        };
        var icon = new G9IconView {
            Icon = triggerItem.Icon,
            Size = BottomIconSize,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        var label = new Label
        {
            Text = triggerItem.Text,
            FontSize = BottomLabelFontSize,
            FontFamily = ResolveCulturalFont(),
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            FontAutoScalingEnabled = false,
            InputTransparent = true
        };

        var content = new VerticalStackLayout
        {
            Spacing = 3,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            Children = { icon, label }
        };

        var root = new Grid { BackgroundColor = Colors.Transparent, IsClippedToBounds = false, ZIndex = 4 };
        root.Add(content);
        root.AutomationId = triggerItem.AutomationId!;
        SemanticProperties.SetDescription(root, triggerItem.Text);
        root.Behaviors.Add(G9PressFeedbackBehavior.For(new Command(OnOverflowTriggerTapped)));

        return new MenuButtonParts(root, icon, label, triggerItem);
    }

    private OverflowItemParts CreateOverflowItem(G9TabBarItem item, int sourceIndex)
    {
        var icon = new G9IconView {
            Icon = item.Icon,
            Size = OverflowItemIconSize,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        var label = new Label
        {
            Text = item.Text,
            FontSize = OverflowItemFontSize,
            FontFamily = ResolveCulturalFont(),
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            FontAutoScalingEnabled = false,
            InputTransparent = true
        };

        var content = new VerticalStackLayout
        {
            Padding = new Thickness(4, 0),
            Spacing = 2,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
            Children = { icon, label }
        };

        // Glass cell. UpdateOverflowVisuals swaps the Background between glass (idle) and the
        // FAB primary gradient (selected) so the active overflow entry reads as the bar's pick.
        var surface = new Border
        {
            WidthRequest = OverflowItemSize,
            HeightRequest = OverflowItemSize,
            StrokeThickness = BarStrokeSize,
            StrokeShape = new RoundRectangle { CornerRadius = OverflowItemSize / 2 },
            Content = content,
            InputTransparent = true
        };

        var root = new Grid
        {
            WidthRequest = OverflowItemSize,
            HeightRequest = OverflowItemSize,
            IsClippedToBounds = false,
            Opacity = 0,
            InputTransparent = true,
            ZIndex = 9
        };
        root.Add(surface);

        if (!string.IsNullOrWhiteSpace(item.AutomationId))
        {
            root.AutomationId = item.AutomationId;
        }

        SemanticProperties.SetDescription(root, item.Text);
        root.GestureRecognizers.Add(CreateTapGesture(() => OnOverflowItemTapped(sourceIndex)));

        return new OverflowItemParts(root, surface, icon, label, item);
    }

    private static Border CreateIndicatorView()
    {
        return new Border
        {
            WidthRequest = IndicatorWidth,
            HeightRequest = IndicatorHeight,
            StrokeThickness = 0,
            StrokeShape = new RoundRectangle { CornerRadius = IndicatorCornerRadius },
            BackgroundColor = Colors.Transparent,
            Opacity = 0,
            InputTransparent = true,
            ZIndex = 2
        };
    }

    /// <summary>
    ///     Invisible tap-catcher that fills the *expanded* portion of the control while either
    ///     menu is open. Children buttons sit on top with higher ZIndex so they consume their own
    ///     taps; anything that misses a button hits the backdrop and closes the menus.
    /// </summary>
    private BoxView CreateBackdrop()
    {
        var backdrop = new BoxView
        {
            BackgroundColor = G9TabBarColors.BackdropFill(G9Palette.Current),
            Color = G9TabBarColors.BackdropFill(G9Palette.Current),
            IsVisible = false,
            InputTransparent = true,
            ZIndex = BackdropZIndex
        };
        backdrop.GestureRecognizers.Add(CreateTapGesture(OnBackdropTapped));

        // Drag (or any pointer move) on the empty zone also closes the open menus — gives the
        // user the same dismiss behavior they'd expect from a modal scrim.
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, args) =>
        {
            if (args.StatusType == GestureStatus.Started)
            {
                OnBackdropTapped();
            }
        };
        backdrop.GestureRecognizers.Add(pan);

        return backdrop;
    }

    private void OnBackdropTapped()
    {
        // Close everything that's open. Selection state is preserved.
        if (IsFabOpen)
        {
            IsFabOpen = false;
        }

        if (IsOverflowOpen)
        {
            IsOverflowOpen = false;
        }

        if (IsCenterFloating && ResetCenterOnMenuSelection && !HasFab)
        {
            IsCenterFloating = false;
        }
    }

    private static TapGestureRecognizer CreateTapGesture(Action handler)
    {
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => handler();
        return tap;
    }

    #endregion

    #region Tap handlers

    private void OnBottomItemTapped(int index)
    {
        CancelStartupSelectionReveal();

        var items = Items;
        if (items is null || index < 0 || index >= items.Count)
        {
            return;
        }

        // Selecting any regular bar item collapses an open overflow column.
        if (IsOverflowOpen)
        {
            IsOverflowOpen = false;
        }

        var tappedItem = items[index];

        var fabIndex = ResolvedFabIndex;
        if (HasFab && index == fabIndex)
        {
            ResetBottomSelectionReveal();
            SelectedIndex = index;

            if (!IsCenterFloating)
            {
                IsCenterFloating = true;
                IsFabOpen = false;
            }
            else
            {
                IsFabOpen = !IsFabOpen;
            }

            ExecuteItem(tappedItem, index, false);
            ItemSelected?.Invoke(this, new G9TabBarSelectionChangedEventArgs(index, tappedItem, false));
            return;
        }

        var revealSelectionFromCenter = ResetCenterOnMenuSelection && IsCenterFloating;
        if (revealSelectionFromCenter)
        {
            PrepareBottomSelectionReveal(index);
        }
        else
        {
            ResetBottomSelectionReveal();
        }

        SelectedIndex = index;
        IsFabOpen = false;

        if (ResetCenterOnMenuSelection)
        {
            IsCenterFloating = false;
        }

        if (revealSelectionFromCenter)
        {
            StartBottomSelectionReveal(index);
        }

        ExecuteItem(tappedItem, index, false);
        ItemSelected?.Invoke(this, new G9TabBarSelectionChangedEventArgs(index, tappedItem, false));
    }

    private void OnFabTapped()
    {
        CancelStartupSelectionReveal();

        // Opening the FAB closes the overflow column to keep only one cluster expanded at a time.
        if (IsOverflowOpen)
        {
            IsOverflowOpen = false;
        }

        var fabIndex = ResolvedFabIndex;
        if (HasFab && Items?.Count > fabIndex)
        {
            SelectedIndex = fabIndex;
        }

        if (!IsCenterFloating)
        {
            IsCenterFloating = true;
            IsFabOpen = false;
            return;
        }

        IsFabOpen = !IsFabOpen;
    }

    private void OnSubMenuItemTapped(int index)
    {
        CancelStartupSelectionReveal();

        var items = SubMenuItems;
        if (items is null || index < 0 || index >= items.Count)
        {
            return;
        }

        var item = items[index];
        ExecuteItem(item, index, true);
        IsFabOpen = false;
        ItemSelected?.Invoke(this, new G9TabBarSelectionChangedEventArgs(index, item, true));
    }

    private void OnOverflowTriggerTapped()
    {
        CancelStartupSelectionReveal();

        if (!HasOverflow)
        {
            return;
        }

        // Mutually exclusive with the FAB sub-menu.
        if (IsFabOpen)
        {
            IsFabOpen = false;
        }

        IsOverflowOpen = !IsOverflowOpen;
    }

    private void OnOverflowItemTapped(int sourceIndex)
    {
        CancelStartupSelectionReveal();

        var items = Items;
        if (items is null || sourceIndex < 0 || sourceIndex >= items.Count)
        {
            return;
        }

        var item = items[sourceIndex];

        // Mirror OnBottomItemTapped's regular-tab flow so an overflow selection collapses
        // every other open menu and returns the bar to its default state — no orphan FAB
        // sub-menu, no orphan floating FAB.
        var revealSelectionFromCenter = ResetCenterOnMenuSelection && IsCenterFloating;
        if (revealSelectionFromCenter)
        {
            // Reveal the indicator from where the FAB pinned it — effectively the More slot,
            // since that's where the indicator anchors when selection lives in overflow.
            PrepareBottomSelectionReveal(OverflowTriggerSlotIndex);
        }
        else
        {
            ResetBottomSelectionReveal();
        }

        SelectedIndex = sourceIndex;
        IsOverflowOpen = false;
        IsFabOpen = false;
        if (ResetCenterOnMenuSelection)
        {
            IsCenterFloating = false;
        }

        if (revealSelectionFromCenter)
        {
            StartBottomSelectionReveal(OverflowTriggerSlotIndex);
        }

        ExecuteItem(item, sourceIndex, false);
        ItemSelected?.Invoke(this, new G9TabBarSelectionChangedEventArgs(sourceIndex, item, false));
    }

    private static void ExecuteItem(G9TabBarItem item, int index, bool isSubMenuItem)
    {
        item.Clicked?.Invoke(new G9TabBarClickContext(index, item, isSubMenuItem));

        if (item.Command?.CanExecute(item.CommandParameter) == true)
        {
            item.Command.Execute(item.CommandParameter);
        }
    }

    #endregion

    #region Selection / animation orchestration

    private void ApplySelectedIndex(int requestedIndex, bool animate, bool raiseEvent)
    {
        var items = Items;
        if (items is null || items.Count == 0)
        {
            _activeSelectedIndex = -1;
            return;
        }

        var index = Math.Clamp(requestedIndex, 0, items.Count - 1);

        if (SelectedIndex != index)
        {
            SelectedIndex = index;
            return;
        }

        var changed = _activeSelectedIndex != index;
        _activeSelectedIndex = index;
        UpdateAllVisuals(animate);

        if (raiseEvent && changed)
        {
            ItemSelected?.Invoke(this, new G9TabBarSelectionChangedEventArgs(index, items[index], false));
        }
    }

    private void ApplyDefaultSelectedIndex()
    {
        var items = Items;
        if (items is null || items.Count == 0)
        {
            return;
        }

        var index = Math.Clamp(DefaultSelectedIndex, 0, items.Count - 1);
        ResetBottomSelectionReveal();

        _applyingDefaultSelectedIndex = true;
        try
        {
            SelectedIndex = index;
        }
        finally
        {
            _applyingDefaultSelectedIndex = false;
        }

        ApplySelectedIndex(index, false, false);
        IsFabOpen = false;

        if (HasFab && index == ResolvedFabIndex)
        {
            IsCenterFloating = true;
            return;
        }

        if (ResetCenterOnMenuSelection)
        {
            IsCenterFloating = false;
        }
    }

    private void QueueStartupSelectionReveal()
    {
        if (!StartupSelectionRevealEnabled || _startupSelectionRevealQueued)
        {
            return;
        }

        var items = Items;
        var index = _activeSelectedIndex;
        if (items is null || index < 0 || index >= items.Count)
        {
            return;
        }

        _startupSelectionRevealQueued = true;
        var version = ++_startupRevealVersion;

        if (HasFab && index == ResolvedFabIndex)
        {
            IsFabOpen = false;
            if (!IsCenterFloating)
            {
                IsCenterFloating = true;
            }

            this.AbortAnimation(CenterStateAnimationName);
            _centerProgress = 0d;
            _openProgress = 0d;
            _chromeDrawable.CenterProgress = 0f;
            _chromeDrawable.OpenProgress = 0f;
            SetReservedHeight(FloatingControlHeight);
        }
        else
        {
            PrepareBottomSelectionReveal(index);
        }

        UpdateAllVisuals(false);
        _ = RunStartupSelectionRevealAsync(index, version);
    }

    private async Task RunStartupSelectionRevealAsync(int index, int version)
    {
        try
        {
            await Task.Delay(StartupSelectionRevealDelayMs);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (version != _startupRevealVersion || Handler is null || index != _activeSelectedIndex)
                {
                    return;
                }

                if (HasFab && index == ResolvedFabIndex)
                {
                    if (!IsCenterFloating)
                    {
                        IsCenterFloating = true;
                    }

                    AnimateCenterState(true);
                    return;
                }

                StartBottomSelectionReveal(index);
            });
        }
        catch
        {
            // Startup reveal is decorative; lifecycle changes must not surface as unobserved task exceptions.
        }
    }

    private void CancelStartupSelectionReveal()
    {
        if (_startupSelectionRevealQueued)
        {
            _startupRevealVersion++;
        }
    }

    private void AnimateCenterState(bool isFloating)
    {
        this.AbortAnimation(CenterStateAnimationName);
        this.AbortAnimation(NotchBounceAnimationName);

        if (isFloating)
        {
            SetReservedHeight(IsFabOpen || _openProgress > 0.001d
                ? ComputeOpenControlHeight(SubMenuItems?.Count ?? 0)
                : FloatingControlHeight);
        }

        var target = isFloating ? 1d : 0d;
        if (Math.Abs(_centerProgress - target) < 0.001d)
        {
            _centerProgress = target;
            _chromeNotchProgress = target;
            UpdateAllVisuals(false);
            if (!isFloating)
            {
                DelayReservedHeightChange(ResolveTargetReservedHeight());
            }

            return;
        }

        // Two parallel single-pass animations driven by the same easeOutBack curve. Both ride
        // through their resting target, overshoot once, and settle smoothly — the FAB pop and
        // the notch dip share a beat (no multi-stage stitching, no jitter). The notch uses a
        // larger "back" coefficient so the hole grows visibly deeper than the FAB body pops.
        var fabStart = _centerProgress;
        var notchStart = _chromeNotchProgress;

        var fabAnimation = new Animation(value =>
        {
            _centerProgress = value;
            UpdateAllVisuals(false);
        }, fabStart, target, isFloating ? FabPopEasing : Easing.CubicIn);

        fabAnimation.Commit(
            this,
            CenterStateAnimationName,
            AnimationFrameRate,
            isFloating ? CenterStateBouncyDurationMs : CenterStateDurationMs,
            finished: (_, cancelled) =>
            {
                if (cancelled)
                {
                    return;
                }

                _centerProgress = target;

                if (!isFloating)
                {
                    DelayReservedHeightChange(ResolveTargetReservedHeight());
                }

                UpdateAllVisuals(false);
            });

        var notchAnimation = new Animation(v =>
        {
            _chromeNotchProgress = v;
            _chromeDrawable.CenterProgress = (float)v;
            _chromeView.Invalidate();
            SyncShadow();
        }, notchStart, target, isFloating ? NotchPopEasing : Easing.CubicIn);

        notchAnimation.Commit(
            this,
            NotchBounceAnimationName,
            AnimationFrameRate,
            isFloating ? CenterStateBouncyDurationMs : CenterStateDurationMs,
            finished: (_, cancelled) =>
            {
                if (cancelled)
                {
                    return;
                }

                _chromeNotchProgress = target;
                _chromeDrawable.CenterProgress = (float)target;
                _chromeView.Invalidate();
                SyncShadow();
            });
    }

    /// <summary>
    ///     Standard easeOutBack curve. <paramref name="back"/> controls how far past 1 the curve
    ///     overshoots before settling — same shape used by Material spring components.
    /// </summary>
    private static double EaseOutBackCurve(double t, double back)
    {
        var p = t - 1d;
        return p * p * ((back + 1d) * p + back) + 1d;
    }

    private static readonly Easing FabPopEasing =
        new(t => EaseOutBackCurve(t, FabPopBackCoefficient));

    private static readonly Easing NotchPopEasing =
        new(t => EaseOutBackCurve(t, NotchPopBackCoefficient));

    private void AnimateFabOpen(bool isOpen)
    {
        this.AbortAnimation(OpenAnimationName);

        if (isOpen && !IsCenterFloating)
        {
            IsCenterFloating = true;
        }

        if (isOpen)
        {
            SetReservedHeight(ComputeOpenControlHeight(SubMenuItems?.Count ?? 0));
            UpdateAllVisuals(false);
        }

        var target = isOpen ? 1d : 0d;
        if (Math.Abs(_openProgress - target) < 0.001d)
        {
            _openProgress = target;
            UpdateAllVisuals(false);
            if (!isOpen)
            {
                DelayReservedHeightChange(ResolveTargetReservedHeight());
            }

            return;
        }

        var start = _openProgress;
        var animation = new Animation(value =>
        {
            _openProgress = value;
            _chromeDrawable.OpenProgress = (float)value;
            UpdateFabOpenVisuals();
        }, start, target, Easing.CubicOut);

        animation.Commit(
            this,
            OpenAnimationName,
            AnimationFrameRate,
            OpenStateDurationMs,
            finished: (_, cancelled) =>
            {
                if (!cancelled && !isOpen)
                {
                    DelayReservedHeightChange(ResolveTargetReservedHeight());
                }
            });
    }

    private void AnimateOverflowOpen(bool isOpen)
    {
        this.AbortAnimation(OverflowAnimationName);

        if (isOpen)
        {
            SetReservedHeight(ComputeOverflowControlHeight(_overflowItems.Count));
            UpdateAllVisuals(false);
        }

        var target = isOpen ? 1d : 0d;
        if (Math.Abs(_overflowProgress - target) < 0.001d)
        {
            _overflowProgress = target;
            UpdateOverflowVisuals();
            if (!isOpen)
            {
                DelayReservedHeightChange(ResolveTargetReservedHeight());
            }

            return;
        }

        var start = _overflowProgress;
        var animation = new Animation(value =>
        {
            _overflowProgress = value;
            UpdateOverflowVisuals();
        }, start, target, Easing.CubicOut);

        animation.Commit(
            this,
            OverflowAnimationName,
            AnimationFrameRate,
            OverflowStateDurationMs,
            finished: (_, cancelled) =>
            {
                if (!cancelled && !isOpen)
                {
                    DelayReservedHeightChange(ResolveTargetReservedHeight());
                }
            });
    }

    private void PrepareBottomSelectionReveal(int index)
    {
        this.AbortAnimation(BottomSelectionRevealAnimationName);
        _revealingSelectedIndex = index;
        _bottomSelectionRevealProgress = 0d;
    }

    private void StartBottomSelectionReveal(int index)
    {
        PrepareBottomSelectionReveal(index);
        UpdateIndicator(true);

        var animation = new Animation(value =>
        {
            _bottomSelectionRevealProgress = value;
            UpdateBottomButtonVisuals();
        }, 0d, 1d, Easing.CubicOut);

        animation.Commit(
            this,
            BottomSelectionRevealAnimationName,
            AnimationFrameRate,
            SelectedReturnRevealDurationMs,
            finished: (_, cancelled) =>
            {
                if (cancelled || _revealingSelectedIndex != index)
                {
                    return;
                }

                _bottomSelectionRevealProgress = 1d;
                _revealingSelectedIndex = -1;
                UpdateBottomButtonVisuals();
            });
    }

    private void ResetBottomSelectionReveal()
    {
        this.AbortAnimation(BottomSelectionRevealAnimationName);
        _bottomSelectionRevealProgress = 1d;
        _revealingSelectedIndex = -1;
    }

    #endregion

    #region Visual updates

    private void UpdateAllVisuals(bool animate, bool invalidateChrome = true)
    {
        _chromeDrawable.CenterProgress = (float)_chromeNotchProgress;
        _chromeDrawable.OpenProgress = (float)_openProgress;
        UpdateBottomButtonVisuals();
        UpdateSubMenuButtonVisuals();
        UpdateOverflowVisuals();
        UpdateBackdropState();
        LayoutElements(invalidateChrome);
        UpdateIndicator(animate);
    }

    /// <summary>
    ///     Toggles the invisible backdrop on/off based on whether either menu is currently open.
    ///     The backdrop is what intercepts taps in the empty reserved zone.
    /// </summary>
    private void UpdateBackdropState()
    {
        var anyMenuOpen = IsFabOpen || _openProgress > HiddenVisibilityThreshold ||
                          IsOverflowOpen || _overflowProgress > HiddenVisibilityThreshold;
        _backdrop.IsVisible = anyMenuOpen;
        _backdrop.InputTransparent = !anyMenuOpen;
    }

    private void UpdateFabOpenVisuals()
    {
        _fabIconView.Rotation = FabRotationOpenDegrees * _openProgress;
        UpdateSubMenuButtonVisuals();

        var width = Width > 0 ? Width : 420d;
        var height = _reservedHeight;
        var barTop = Math.Max(0d, height - BarHeight - BarBottomGap);
        var fabCenterX = ResolveFabCenterX(width);

        LayoutFabButton(width, barTop, fabCenterX);
        LayoutSubMenuButtons(fabCenterX, barTop, width);
    }

    private void UpdateBottomButtonVisuals()
    {
        var theme = G9Palette.Current;
        var selectedColor = ResolveSelectedContentColor(theme);
        var unselectedColor = ResolveInactiveMenuColor(theme);
        var centerInlineRevealProgress = IsCenterFloating ? 0d : EaseOutCubic(1d - _centerProgress);
        var fabIndex = HasFab ? ResolvedFabIndex : -1;
        var triggerSlot = HasOverflow ? OverflowTriggerSlotIndex : -1;
        var fontFamily = ResolveCulturalFont();
        var overflowLabel = ResolveOverflowLabelText();

        // The More trigger only inherits the "selected" styling once an overflow item is actually
        // committed as the active selection — *opening* the overflow column does NOT highlight
        // More. Until the user picks something from the popup, the previously-selected slot
        // keeps its pill, bold label, and downward nudge intact.
        var activeSelectionInOverflow = HasOverflow && _activeSelectedIndex >= triggerSlot;

        for (var i = 0; i < _bottomButtons.Count; i++)
        {
            var parts = _bottomButtons[i];
            var isFab = i == fabIndex;
            var isOverflowTrigger = i == triggerSlot;
            var isSelected = !isOverflowTrigger && i == _activeSelectedIndex;
            var isHighlighted = isSelected || (isOverflowTrigger && activeSelectionInOverflow);
            // The trigger button's underlying G9TabBarItem holds the localized label; refreshing
            // it on each pass picks up culture changes without forcing a full Items reassignment.
            if (isOverflowTrigger && parts.Item.Text != overflowLabel)
            {
                parts.Item.Text = overflowLabel;
            }

            // Setting a BindableProperty re-runs equality already, but assigning the same icon enum still
            // pumps property-changed events; cache to avoid layout invalidation thrash during animation.
            if (!Equals(parts.Icon.Icon, parts.Item.Icon)) parts.Icon.Icon = parts.Item.Icon;
            if (parts.Label.Text != parts.Item.Text) parts.Label.Text = parts.Item.Text;
            if (parts.Label.FontFamily != fontFamily) parts.Label.FontFamily = fontFamily;
            parts.Icon.Color = isHighlighted ? selectedColor : unselectedColor;
            parts.Label.TextColor = isHighlighted ? selectedColor : unselectedColor;
            parts.Label.FontAttributes = isHighlighted ? FontAttributes.Bold : FontAttributes.None;

            if (isFab)
            {
                parts.Root.IsVisible = !IsCenterFloating || centerInlineRevealProgress > HiddenVisibilityThreshold;
                parts.Root.Opacity = centerInlineRevealProgress;
                parts.Root.InputTransparent = IsCenterFloating || _centerProgress > CenterItemFadeInputThreshold;
                parts.Root.Scale = 1d - (CenterItemHiddenScaleAmount * (1d - centerInlineRevealProgress));
                parts.Root.TranslationY = CenterItemHiddenTranslationY * (1d - centerInlineRevealProgress);
            }
            else
            {
                // Selected-slot Y nudge is animated separately by RefreshHighlightedSlotTranslation
                // so the icon + label slide smoothly with the pill instead of snapping. We only
                // touch the non-Y properties here to avoid fighting that running animation.
                parts.Root.IsVisible = true;
                parts.Root.Opacity = 1d;
                parts.Root.InputTransparent = false;
                parts.Root.Scale = 1d;
            }
        }

        RefreshHighlightedSlotTranslation();

        var fabVisibilityProgress = ResolveFabVisibilityProgress();
        _fabButton.IsVisible = HasFab && fabVisibilityProgress > HiddenVisibilityThreshold;
        // Bouncy open can push the underlying progress past 1.0 — clamp opacity so MAUI doesn't
        // see values out of [0, 1] and the FAB doesn't visibly fade past full while the notch
        // overshoots. Scale (computed in LayoutFabButton) is allowed to ride the overshoot for
        // the pop effect since it works fine in either direction.
        _fabButton.Opacity = Math.Clamp(fabVisibilityProgress, 0d, 1d);
        _fabButton.InputTransparent = !HasFab || fabVisibilityProgress < 0.6d;
        _fabIconView.Rotation = FabRotationOpenDegrees * _openProgress;
    }

    private void UpdateSubMenuButtonVisuals()
    {
        if (!HasFab)
        {
            return;
        }

        var count = _subMenuButtons.Count;
        var fontFamily = ResolveCulturalFont();
        var labelColor = ResolveSubMenuContentColor(G9Palette.Current);

        // Compute which visual index is the center of the row (for stagger direction).
        // Items animate outward from center, so the center item has the shortest stagger.
        var centerI = (count - 1) / 2.0;

        for (var i = 0; i < _subMenuButtons.Count; i++)
        {
            // Center-outward stagger: item closest to center appears first.
            var distanceFromCenter = Math.Abs(i - centerI);
            var itemProgress = ResolveItemOpenProgressWithDelay(distanceFromCenter);
            var parts = _subMenuButtons[i];

            if (!Equals(parts.Icon.Icon, parts.Item.Icon)) parts.Icon.Icon = parts.Item.Icon;
            parts.Icon.Color = Colors.White;
            if (parts.Label.Text != parts.Item.Text) parts.Label.Text = parts.Item.Text;
            if (parts.Label.FontFamily != fontFamily) parts.Label.FontFamily = fontFamily;
            parts.Label.TextColor = labelColor;

            // Each item slides outward from the FAB center on X, and rises slightly on Y.
            // Direction: items to the left of center slide left, right ones slide right.
            var direction = i < centerI ? -1d : (i > centerI ? 1d : 0d);
            var eased = EaseOutCubic(itemProgress);
            var easedBack = EaseOutBackLite(itemProgress);

            parts.Root.IsVisible = itemProgress > HiddenVisibilityThreshold;
            parts.Root.Opacity = itemProgress;
            // Slide from FAB center outward (X), and upward from a slight offset (Y)
            parts.Root.TranslationX = SubMenuRowRevealTravelX * direction * (1d - eased);
            parts.Root.TranslationY = SubMenuRowRevealTravelY * (1d - eased);
            parts.Root.Scale = 0.55d + (0.45d * easedBack);
            parts.Root.InputTransparent = itemProgress < SubMenuRowActivationThreshold;
        }
    }

    /// <summary>
    ///     Computes which bottom slot is currently "highlighted" (selected, or the More
    ///     trigger when overflow is open / when the active selection lives in overflow) and
    ///     animates that slot's Y down to <see cref="SelectedIndicatorDownNudgeY" />, while
    ///     animating the previously-highlighted slot back to 0. Called from
    ///     <see cref="UpdateBottomButtonVisuals" />; the <see cref="_highlightedSlotIndex" />
    ///     guard prevents the animation from re-firing on every visual refresh.
    /// </summary>
    private void RefreshHighlightedSlotTranslation()
    {
        if (_bottomButtons.Count == 0)
        {
            _highlightedSlotIndex = -1;
            return;
        }

        var fabIndex = HasFab ? ResolvedFabIndex : -1;
        var triggerSlot = HasOverflow ? OverflowTriggerSlotIndex : -1;
        var activeSelectionInOverflow = HasOverflow && _activeSelectedIndex >= triggerSlot;

        // Mirrors the highlighted formula in UpdateBottomButtonVisuals: opening the overflow
        // column does NOT change the highlighted slot — only an actual overflow selection
        // (activeSelectionInOverflow) promotes the More trigger.
        int highlighted;
        if (triggerSlot >= 0 && activeSelectionInOverflow)
        {
            highlighted = triggerSlot;
        }
        else if (_activeSelectedIndex >= 0
                 && _activeSelectedIndex < _bottomButtons.Count
                 && _activeSelectedIndex != fabIndex)
        {
            highlighted = _activeSelectedIndex;
        }
        else
        {
            highlighted = -1;
        }

        if (highlighted == _highlightedSlotIndex)
        {
            return;
        }

        var previous = _highlightedSlotIndex;
        _highlightedSlotIndex = highlighted;

        if (previous >= 0 && previous < _bottomButtons.Count)
        {
            AnimateSlotTranslateY(previous, 0d);
        }

        if (highlighted >= 0 && highlighted < _bottomButtons.Count)
        {
            AnimateSlotTranslateY(highlighted, SelectedIndicatorDownNudgeY);
        }
    }

    /// <summary>
    ///     Slides a single bottom slot's <see cref="VisualElement.TranslationY" /> to the
    ///     requested target. Uses an explicit per-slot named animation so a fast selection
    ///     change cleanly aborts the previous run and starts a new one from the current Y —
    ///     no dropped frames, no stuck-mid-air slots.
    /// </summary>
    private void AnimateSlotTranslateY(int index, double target)
    {
        if (index < 0 || index >= _bottomButtons.Count)
        {
            return;
        }

        var slot = _bottomButtons[index].Root;
        var animName = $"G9TabBar.SlotTranslation_{index}";

        // Always abort the previous run on this slot before starting a new one. Without this
        // the old animation can keep ticking and overwrite the new target value mid-flight.
        slot.AbortAnimation(animName);

        var start = slot.TranslationY;
        if (Math.Abs(start - target) < 0.01d)
        {
            slot.TranslationY = target;
            return;
        }

        new Animation(v => slot.TranslationY = v, start, target, Easing.CubicInOut)
            .Commit(slot, animName, AnimationFrameRate, IndicatorMoveDurationMs,
                finished: (_, cancelled) =>
                {
                    if (!cancelled)
                    {
                        slot.TranslationY = target;
                    }
                });
    }

    private void UpdateOverflowVisuals()
    {
        if (_overflowItems.Count == 0)
        {
            return;
        }

        var fontFamily = ResolveCulturalFont();
        var theme = G9Palette.Current;
        var inactiveColor = G9TabBarColors.InactiveBottomItem(theme);
        var glassFill = new SolidColorBrush(G9TabBarColors.FabSurface(theme));
        var glassStroke = G9TabBarColors.FabSurfaceStroke(theme);
        var selectedFill = G9TabBarColors.OverflowSelectedFill(theme);
        var selectedStroke = G9TabBarColors.OverflowSelectedStroke(theme);
        var selectedIconColor = G9TabBarColors.OverflowSelectedIconColor(theme);
        var selectedLabelColor = G9TabBarColors.OverflowSelectedLabelColor(theme);
        var triggerSlot = OverflowTriggerSlotIndex;

        // Bottom-up stagger: items closest to the trigger appear first.
        for (var i = 0; i < _overflowItems.Count; i++)
        {
            var parts = _overflowItems[i];
            var itemProgress = ResolveOverflowItemProgress(i);
            var sourceIndex = triggerSlot + i;
            var isSelected = sourceIndex == _activeSelectedIndex;

            if (!Equals(parts.Icon.Icon, parts.Item.Icon)) parts.Icon.Icon = parts.Item.Icon;
            if (parts.Label.Text != parts.Item.Text) parts.Label.Text = parts.Item.Text;
            if (parts.Label.FontFamily != fontFamily) parts.Label.FontFamily = fontFamily;

            // Selected = whole circle painted with the FAB primary gradient (matches the bar's
            // selection pill color), icon + label flip to OnPrimary so they read against the
            // green fill. Idle = translucent glass shell + inactive content color.
            if (isSelected)
            {
                parts.Surface.Background = selectedFill;
                parts.Surface.Stroke = selectedStroke;
                parts.Icon.Color = selectedIconColor;
                parts.Label.TextColor = selectedLabelColor;
            }
            else
            {
                parts.Surface.Background = glassFill;
                parts.Surface.Stroke = glassStroke;
                parts.Icon.Color = inactiveColor;
                parts.Label.TextColor = inactiveColor;
            }
            parts.Label.FontAttributes = FontAttributes.Bold;

            var eased = EaseOutCubic(itemProgress);
            var easedBack = EaseOutBackLite(itemProgress);

            parts.Root.IsVisible = itemProgress > HiddenVisibilityThreshold;
            parts.Root.Opacity = itemProgress;
            // Slide upward from the trigger as the column reveals.
            parts.Root.TranslationY = OverflowRevealTravelY * (1d - eased);
            parts.Root.TranslationX = 0d;
            parts.Root.Scale = 0.6d + (0.4d * easedBack);
            parts.Root.InputTransparent = itemProgress < OverflowActivationThreshold;
        }
    }

    private double ResolveOverflowItemProgress(int index)
    {
        if (_overflowProgress <= 0d)
        {
            return 0d;
        }

        // Items closer to the trigger (lower index) appear first.
        var delay = Math.Min(OverflowRevealStaggerCap, index * OverflowRevealStaggerStep);
        return Math.Clamp((_overflowProgress - delay) / Math.Max(0.01d, 1d - delay), 0d, 1d);
    }

    private void ApplyTheme()
    {
        var theme = G9Palette.Current;

        _chromeDrawable.BarColor = G9TabBarColors.BarBackground(theme);
        _chromeDrawable.BarStrokeColor = G9TabBarColors.BarStroke(theme);
        _chromeDrawable.BarTopHighlightColor = G9TabBarColors.BarTopHighlight(theme);
        // Shadow ink for the two Skia-drawn shadows (chrome drawable + G9TabBarShadowView).
        // These are NOT MAUI `Shadow` objects — they are painted on the Skia render thread, which
        // is why they survived the app-wide shadow ban. Black is what the old `theme.Shadow`
        // token resolved to in BOTH the light and dark dictionaries, so this is a like-for-like
        // substitution after that token was removed.
        _chromeDrawable.ShadowColor = Colors.Black;
        _chromeDrawable.CenterProgress = (float)_chromeNotchProgress;
        _chromeDrawable.OpenProgress = (float)_openProgress;

        // SkiaSharp drop shadow colour — shadow ink at a soft opacity. Pushed to the shadow
        // view and repainted via SyncShadow at the end of this method.
        _shadowView.ShadowColor = new SkiaSharp.SKColor(0, 0, 0, (byte)(0.5f * 255f));

        // Outer FAB shell — translucent glass that mirrors the main bar.
        // NO MAUI Shadow here: the FAB's drop shadow is a Skia circle in G9TabBarShadowView
        // (mirrored from LayoutFabButton), because the old Shadow { Offset=(0,8), Radius=18 }
        // rendered as a hard dark crescent UNDER the FAB on some devices (MAUI Android shadow
        // rendering is device-dependent — open upstream dotnet/maui #15565 / #16311). Do not
        // re-add a Shadow to this Border.
        _fabOuterSurface.BackgroundColor = Colors.Transparent;
        _fabOuterSurface.Background = new SolidColorBrush(G9TabBarColors.FabSurface(theme));
        _fabOuterSurface.Stroke = G9TabBarColors.FabSurfaceStroke(theme);

        // Inner accent ring — keeps the awesome primary gradient that holds the +.
        _fabInnerSurface.BackgroundColor = Colors.Transparent;
        _fabInnerSurface.Background = G9TabBarColors.FabInnerBackground(theme);
        _fabInnerSurface.Stroke = G9TabBarColors.FabInnerBorder(theme);

        _fabPlusDrawable.Color = theme.OnPrimary;
        _fabIconView.Invalidate();

        _indicator.BackgroundColor = G9TabBarColors.SelectedIndicator(theme);

        // Backdrop stays transparent; we just refresh in case something else painted it.
        _backdrop.BackgroundColor = G9TabBarColors.BackdropFill(theme);
        _backdrop.Color = G9TabBarColors.BackdropFill(theme);

        ApplySubMenuTheme(theme);
        ApplyOverflowTheme(theme);
        UpdateBottomButtonVisuals();
        UpdateSubMenuButtonVisuals();
        UpdateOverflowVisuals();
        _chromeView.Invalidate();
        SyncShadow();
    }

    private void ApplySubMenuTheme(G9Palette theme)
    {
        var labelColor = G9TabBarColors.SubMenuLabel(theme);
        for (var i = 0; i < _subMenuButtons.Count; i++)
        {
            var parts = _subMenuButtons[i];

            // Outer glass shell — mirrors the bar.
            parts.OuterSurface.BackgroundColor = Colors.Transparent;
            parts.OuterSurface.Background = new SolidColorBrush(G9TabBarColors.FabSurface(theme));
            parts.OuterSurface.Stroke = G9TabBarColors.FabSurfaceStroke(theme);
            // NO MAUI Shadow — the app is shadow-free by policy (design guide §12b). The glass
            // fill + stroke carry the sub-menu button's separation from the page behind it.

            // Inner green accent ring — primary gradient with the icon.
            parts.InnerSurface.BackgroundColor = Colors.Transparent;
            parts.InnerSurface.Background = G9TabBarColors.FabInnerBackground(theme);
            parts.InnerSurface.Stroke = G9TabBarColors.FabInnerBorder(theme);

            parts.Label.TextColor = labelColor;
        }
    }

    private void ApplyOverflowTheme(G9Palette theme)
    {
        var inactiveColor = G9TabBarColors.InactiveBottomItem(theme);
        for (var i = 0; i < _overflowItems.Count; i++)
        {
            var parts = _overflowItems[i];

            // Default = idle glass; UpdateOverflowVisuals will swap to the primary gradient
            // for whichever item is the active selection.
            parts.Surface.BackgroundColor = Colors.Transparent;
            parts.Surface.Background = new SolidColorBrush(G9TabBarColors.FabSurface(theme));
            parts.Surface.Stroke = G9TabBarColors.FabSurfaceStroke(theme);
            // NO MAUI Shadow — see ApplySubMenuTheme.

            parts.Icon.Color = inactiveColor;
            parts.Label.TextColor = inactiveColor;
        }
    }

    #endregion

    #region Indicator pill animation (selection)

    private void UpdateIndicator(bool animate)
    {
        if (_bottomButtons.Count == 0)
        {
            _indicator.Opacity = 0;
            _indicator.InputTransparent = true;
            return;
        }

        var index = _activeSelectedIndex;
        if (index < 0)
        {
            _indicator.Opacity = 0;
            _indicator.InputTransparent = true;
            return;
        }

        // When the active selection lives inside the overflow column the source index is past
        // the visible button count, but the pill should still render — pinned on the More
        // trigger slot so the user can see *that's where my selection lives*.
        if (index >= _bottomButtons.Count)
        {
            if (!HasOverflow)
            {
                _indicator.Opacity = 0;
                _indicator.InputTransparent = true;
                return;
            }

            // Anchor anything in the overflow column on the trigger slot.
            index = OverflowTriggerSlotIndex;
        }

        var (targetX, targetY) = ResolveIndicatorTarget(index);
        var hideForFab = HasFab && index == ResolvedFabIndex;
        var targetOpacity = hideForFab && (IsCenterFloating || _centerProgress > HiddenVisibilityThreshold)
            ? 0d
            : 1d;
        var revealIndicatorFromCenter = ShouldRevealIndicatorFromCenter(index, targetOpacity);

        if (!_indicatorPositioned)
        {
            _indicator.AbortAnimation(IndicatorAnimationName);
            AbsoluteLayout.SetLayoutBounds(_indicator, new Rect(targetX, targetY, IndicatorWidth, IndicatorHeight));
            _indicator.TranslationX = 0;
            _indicator.TranslationY = 0;
            _indicator.ScaleX = revealIndicatorFromCenter
                ? EaseOutCubic(_bottomSelectionRevealProgress)
                : 1;
            _indicator.Opacity = targetOpacity;
            _indicatorTargetX = targetX;
            _indicatorTargetY = targetY;
            _indicatorPositioned = true;
            return;
        }

        if (!animate)
        {
            if (revealIndicatorFromCenter)
            {
                AbsoluteLayout.SetLayoutBounds(_indicator, new Rect(targetX, targetY, IndicatorWidth, IndicatorHeight));
                _indicatorTargetX = targetX;
                _indicatorTargetY = targetY;
                return;
            }

            _indicator.AbortAnimation(IndicatorAnimationName);
            AbsoluteLayout.SetLayoutBounds(_indicator, new Rect(targetX, targetY, IndicatorWidth, IndicatorHeight));
            _indicator.TranslationX = 0;
            _indicator.TranslationY = 0;
            _indicator.ScaleX = 1;
            _indicator.Opacity = targetOpacity;
            _indicatorTargetX = targetX;
            _indicatorTargetY = targetY;
            return;
        }

        var distance = Math.Abs(targetX - _indicatorTargetX);
        var movedToNewSlot = distance > 0.5d || Math.Abs(targetY - _indicatorTargetY) > 0.5d;

        if (!movedToNewSlot)
        {
            if (revealIndicatorFromCenter)
            {
                AnimateIndicatorTo(targetX, targetY, targetOpacity, true);
                return;
            }

            if (animate && Math.Abs(_indicator.Opacity - targetOpacity) > 0.01d)
            {
                AnimateIndicatorOpacityTo(targetOpacity);
            }
            else
            {
                _indicator.Opacity = targetOpacity;
            }

            return;
        }

        AnimateIndicatorTo(targetX, targetY, targetOpacity, revealIndicatorFromCenter);
    }

    private (double x, double y) ResolveIndicatorTarget(int index)
    {
        var width = Width > 0 ? Width : 420d;
        var itemCount = Math.Max(1, _bottomButtons.Count);
        // Match LayoutElements: slots live inside the horizontally-inset bar span.
        var barLeft = BarHorizontalGap;
        var barWidth = Math.Max(1d, width - 2d * BarHorizontalGap);
        var slotWidth = barWidth / itemCount;
        // Items folded into the overflow column anchor the indicator on the trigger slot.
        var anchorIndex = HasOverflow && index >= OverflowTriggerSlotIndex
            ? OverflowTriggerSlotIndex
            : Math.Min(index, itemCount - 1);
        var visualSlot = IsRtl() ? itemCount - 1 - anchorIndex : anchorIndex;
        var centerX = barLeft + (visualSlot + 0.5d) * slotWidth;
        var height = _reservedHeight;
        var barTop = Math.Max(0d, height - BarHeight - BarBottomGap);

        var x = centerX - (IndicatorWidth / 2d);
        // Nudge the pill downward so the green pill sits visually centered on the icon and
        // the selected slot doesn't read as "pushed up" against the top of the bar.
        var y = barTop + IndicatorTopOffset + SelectedIndicatorDownNudgeY;
        return (x, y);
    }

    private bool ShouldRevealIndicatorFromCenter(int index, double targetOpacity)
    {
        return targetOpacity > HiddenVisibilityThreshold &&
               index == _revealingSelectedIndex &&
               _bottomSelectionRevealProgress < 1d - HiddenVisibilityThreshold;
    }

    private void AnimateIndicatorTo(double targetX, double targetY, double targetOpacity, bool revealFromZeroWidth)
    {
        _indicator.AbortAnimation(IndicatorAnimationName);

        var prevX = _indicatorTargetX;
        var prevY = _indicatorTargetY;
        var deltaX = prevX - targetX;
        var deltaY = prevY - targetY;
        var startOpacity = revealFromZeroWidth ? 0d : _indicator.Opacity;

        AbsoluteLayout.SetLayoutBounds(_indicator, new Rect(targetX, targetY, IndicatorWidth, IndicatorHeight));
        _indicator.TranslationX = deltaX;
        _indicator.TranslationY = deltaY;
        _indicator.ScaleX = revealFromZeroWidth ? 0d : 1d;
        _indicator.Opacity = startOpacity;
        _indicatorTargetX = targetX;
        _indicatorTargetY = targetY;

        var stretchAmount = Math.Min(IndicatorMaxStretch, Math.Abs(deltaX) / IndicatorStretchDistanceDivisor);

        var animation = new Animation();
        animation.Add(0, 1, new Animation(v => _indicator.TranslationX = v, deltaX, 0, Easing.CubicInOut));
        if (Math.Abs(deltaY) > 0.1d)
        {
            animation.Add(0, 1, new Animation(v => _indicator.TranslationY = v, deltaY, 0, Easing.CubicInOut));
        }

        if (revealFromZeroWidth)
        {
            animation.Add(0, 0.82, new Animation(v => _indicator.ScaleX = v, 0d, 1d, Easing.CubicOut));
        }
        else if (stretchAmount > 0.01d)
        {
            animation.Add(0, 0.5, new Animation(v => _indicator.ScaleX = v, 1d, 1d + stretchAmount, Easing.CubicOut));
            animation.Add(0.5, 1, new Animation(v => _indicator.ScaleX = v, 1d + stretchAmount, 1d, Easing.CubicIn));
        }

        if (Math.Abs(startOpacity - targetOpacity) > 0.01d)
        {
            animation.Add(0, 0.72, new Animation(v => _indicator.Opacity = v, startOpacity, targetOpacity,
                targetOpacity > startOpacity ? Easing.CubicOut : Easing.CubicIn));
        }

        animation.Commit(
            _indicator,
            IndicatorAnimationName,
            AnimationFrameRate,
            revealFromZeroWidth ? SelectedReturnRevealDurationMs : IndicatorMoveDurationMs,
            finished: (_, cancelled) =>
            {
                if (cancelled)
                {
                    return;
                }

                _indicator.TranslationX = 0;
                _indicator.TranslationY = 0;
                _indicator.ScaleX = 1;
                _indicator.Opacity = targetOpacity;
            });
    }

    private void AnimateIndicatorOpacityTo(double targetOpacity)
    {
        _indicator.AbortAnimation(IndicatorAnimationName);

        var startOpacity = _indicator.Opacity;
        var animation = new Animation(
            value => _indicator.Opacity = value,
            startOpacity,
            targetOpacity,
            targetOpacity > startOpacity ? Easing.CubicOut : Easing.CubicIn);

        animation.Commit(_indicator, IndicatorAnimationName, AnimationFrameRate, IndicatorMoveDurationMs);
    }

    #endregion

    #region Layout

    private void SetReservedHeight(double reservedHeight)
    {
        _reservedHeightChangeVersion++;

        if (Math.Abs(_reservedHeight - reservedHeight) < 0.5d)
        {
            return;
        }

        _reservedHeight = reservedHeight;
        HeightRequest = reservedHeight;
        _root.HeightRequest = reservedHeight;
        _chromeView.HeightRequest = reservedHeight;
        _shadowView.HeightRequest = reservedHeight;
        _hitLayer.HeightRequest = reservedHeight;
        InvalidateMeasure();
    }

    private void DelayReservedHeightChange(double reservedHeight)
    {
        var version = ++_reservedHeightChangeVersion;
        _ = DelayReservedHeightChangeAsync(reservedHeight, version);
    }

    private async Task DelayReservedHeightChangeAsync(double reservedHeight, int version)
    {
        await Task.Delay(ReservedHeightShrinkDelayMs);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (version != _reservedHeightChangeVersion)
            {
                return;
            }

            if (Math.Abs(ResolveTargetReservedHeight() - reservedHeight) > 0.5d)
            {
                return;
            }

            SetReservedHeight(reservedHeight);
            LayoutElements();
        });
    }

    private double ResolveTargetReservedHeight()
    {
        // Overflow column expands the bar regardless of FAB state.
        if (IsOverflowOpen || _overflowProgress > 0.001d)
        {
            return ComputeOverflowControlHeight(_overflowItems.Count);
        }

        if (!HasFab)
        {
            return CompactControlHeight;
        }

        if (IsFabOpen || _openProgress > 0.001d)
        {
            return ComputeOpenControlHeight(SubMenuItems?.Count ?? 0);
        }

        if (IsCenterFloating || _centerProgress > 0.001d)
        {
            return FloatingControlHeight;
        }

        return CompactControlHeight;
    }

    private static double ComputeOverflowControlHeight(int overflowItemCount)
    {
        if (overflowItemCount <= 0)
        {
            return CompactControlHeight;
        }

        var stack = overflowItemCount * OverflowItemSize + (overflowItemCount - 1) * OverflowItemSpacing;
        return BarHeight + OverflowAboveBarGap + stack + 8d + ChromeShadowPadding + BarBottomGap;
    }

    /// <summary>
    ///     Pushes the chrome's current geometry (bar height, notch center, notch progress)
    ///     onto the SkiaSharp <see cref="_shadowView" /> and repaints it, so the drop shadow
    ///     always tracks the live bar silhouette (including the animating FAB notch). Called
    ///     anywhere the chrome itself is invalidated.
    /// </summary>
    private void SyncShadow()
    {
        _shadowView.LayoutHeightDip = _chromeDrawable.LayoutHeight;
        _shadowView.NotchCenterX = _chromeDrawable.NotchCenterX;
        _shadowView.CenterProgress = _chromeDrawable.CenterProgress;
        _shadowView.InvalidateSurface();
    }

    private void LayoutElements(bool invalidateChrome = true)
    {
        var width = Width > 0 ? Width : 420d;
        var height = _reservedHeight;
        var barTop = Math.Max(0d, height - BarHeight - BarBottomGap);
        // The bar is drawn inset from each horizontal edge of the control by
        // BarHorizontalGap so the SkiaSharp drop shadow's left/right halo renders
        // INSIDE the control bounds (Android clips negative-margin overflow). All
        // bottom items, the FAB, the indicator, and overflow are positioned within
        // [barLeft, barRight] so they sit ON the bar instead of past its edges.
        var barLeft = BarHorizontalGap;
        var barRight = width - BarHorizontalGap;
        var barWidth = Math.Max(1d, barRight - barLeft);
        var itemCount = Math.Max(1, _bottomButtons.Count);
        var slotWidth = barWidth / itemCount;
        var rtl = IsRtl();
        var fabCenterX = ResolveFabCenterX(width);

        _chromeDrawable.LayoutWidth = (float)width;
        _chromeDrawable.LayoutHeight = (float)height;
        _chromeDrawable.NotchCenterX = HasFab ? (float)fabCenterX : 0f;
        if (invalidateChrome)
        {
            _chromeView.Invalidate();
            SyncShadow();
        }

        // Backdrop covers the full hit-layer area so any tap that misses a button hits it.
        AbsoluteLayout.SetLayoutBounds(_backdrop,
            new Rect(0d, BackdropTopInset, width, Math.Max(0d, height - BackdropTopInset)));

        for (var i = 0; i < _bottomButtons.Count; i++)
        {
            var visualSlot = rtl ? itemCount - 1 - i : i;
            var centerX = barLeft + (visualSlot + 0.5d) * slotWidth;
            var itemWidth = Math.Min(SlotItemMaxWidth,
                Math.Max(SlotItemMinWidth, slotWidth - SlotItemHorizontalPadding));
            var y = barTop;

            AbsoluteLayout.SetLayoutBounds(
                _bottomButtons[i].Root,
                new Rect(centerX - (itemWidth / 2d), y, itemWidth, BottomItemHeight));
        }

        LayoutFabButton(width, barTop, fabCenterX);
        LayoutSubMenuButtons(fabCenterX, barTop, width);
        LayoutOverflowItems(width, barTop, slotWidth, rtl, itemCount, barLeft);
    }

    /// <summary>
    ///     Stacks overflow items in a vertical column above the trigger slot.
    ///     The bottom-most item sits closest to the trigger and the column rises upward.
    ///     Items are edge-guarded so none clips off-screen.
    /// </summary>
    private void LayoutOverflowItems(double width, double barTop, double slotWidth, bool rtl, int itemCount, double barLeft)
    {
        if (_overflowItems.Count == 0)
        {
            return;
        }

        var triggerLogicalSlot = OverflowTriggerSlotIndex;
        var triggerVisualSlot = rtl ? itemCount - 1 - triggerLogicalSlot : triggerLogicalSlot;
        var triggerCenterX = barLeft + (triggerVisualSlot + 0.5d) * slotWidth;

        // Edge-guard: keep the column inside the screen bounds.
        var halfItem = OverflowItemSize / 2d;
        var minCenter = OverflowEdgeGuard + halfItem;
        var maxCenter = width - OverflowEdgeGuard - halfItem;
        if (minCenter < maxCenter)
        {
            triggerCenterX = Math.Clamp(triggerCenterX, minCenter, maxCenter);
        }

        // The first overflow item (index 0) sits closest to the bar, then the next one above it, etc.
        var startY = barTop - OverflowAboveBarGap - OverflowItemSize;

        for (var i = 0; i < _overflowItems.Count; i++)
        {
            var itemY = startY - i * (OverflowItemSize + OverflowItemSpacing);
            AbsoluteLayout.SetLayoutBounds(
                _overflowItems[i].Root,
                new Rect(triggerCenterX - halfItem, itemY, OverflowItemSize, OverflowItemSize));
        }
    }

    /// <summary>
    ///     Returns the horizontal center of the FAB button in layout coordinates,
    ///     accounting for RTL and which item slot is the FAB.
    ///     Fixes: the notch/FAB always tracks the correct slot center, including edge slots.
    /// </summary>
    private double ResolveFabCenterX(double width)
    {
        if (!HasFab || _bottomButtons.Count == 0)
        {
            return width / 2d;
        }

        var itemCount = _bottomButtons.Count;
        // The bar lives inside [BarHorizontalGap, width - BarHorizontalGap]; slot positions
        // and the FAB notch are computed against that inner span so they line up with the
        // drawn bar instead of the control's full width.
        var barLeft = BarHorizontalGap;
        var barRight = width - BarHorizontalGap;
        var barWidth = Math.Max(1d, barRight - barLeft);
        var slotWidth = barWidth / itemCount;
        var rtl = IsRtl();
        var fabSlot = Math.Clamp(ResolvedFabIndex, 0, itemCount - 1);
        var visualSlot = rtl ? itemCount - 1 - fabSlot : fabSlot;
        var rawCenterX = barLeft + slotWidth * visualSlot + slotWidth * 0.5d;

        // Clamp so the semicircle notch (and FAB) always fits inside the bar.
        var notchR = FabSize / 2d + NotchGap;
        var minCX = barLeft + notchR + BarTopRadius + 2d;
        var maxCX = barRight - notchR - BarTopRadius - 2d;
        return Math.Clamp(rawCenterX, minCX, maxCX);
    }

    private void LayoutFabButton(double width, double barTop, double fabCenterX)
    {
        var positionProgress = IsCenterFloating ? _centerProgress : 1d;
        var fabY = barTop - (FabSize * FabFloatingOverlapRatio) +
                   ((1d - positionProgress) * FabIdleVerticalOffset);
        var fabVisibilityProgress = ResolveFabVisibilityProgress();
        // Scale is driven only by how far the FAB has risen — no change when open/closed.
        var fabScale = FabIdleScaleMin + (FabFloatingScaleBoost * fabVisibilityProgress);

        _fabButton.Scale = fabScale;
        AbsoluteLayout.SetLayoutBounds(_fabButton, new Rect(fabCenterX - (FabSize / 2d), fabY, FabSize, FabSize));

        // Mirror the FAB's final geometry into the Skia shadow view — the FAB's drop shadow is a
        // blurred circle drawn THERE (device-independent), not a MAUI Shadow on the Border (which
        // rendered as a hard bottom crescent on some devices — see G9TabBarShadowView.FabCenterX).
        // Every path that moves/fades the FAB funnels through this method, so the circle can never
        // drift from the button. The shadow view and the hit layer share _root's coordinate space,
        // so fabY needs no translation. Scale rides FabRadius (MAUI Scale is center-anchored, so
        // the center point itself is scale-invariant). The invalidate is coalesced by SKCanvasView
        // when LayoutElements already invalidated in the same frame.
        _shadowView.FabCenterX = (float)(HasFab ? fabCenterX : 0d);
        _shadowView.FabCenterY = (float)(fabY + (FabSize / 2d));
        _shadowView.FabRadius = (float)(FabSize / 2d * fabScale);
        _shadowView.FabVisibility = HasFab ? (float)Math.Clamp(fabVisibilityProgress, 0d, 1d) : 0f;
        _shadowView.InvalidateSurface();
    }

    /// <summary>
    ///     Lays out sub-menu items as a horizontal row centered above the FAB.
    ///     The row fans out symmetrically left and right from the FAB center:
    ///     - With an odd number of items the middle item sits directly above the FAB.
    ///     - With an even number the two middle items straddle the FAB center.
    ///     Items are edge-guarded so none clips off-screen.
    ///     The stagger order is center-outward so the closest item appears first.
    /// </summary>
    private void LayoutSubMenuButtons(double fabCenterX, double barTop, double width)
    {
        if (!HasFab || _subMenuButtons.Count == 0)
        {
            return;
        }

        var count = _subMenuButtons.Count;
        var itemSize = SubMenuRowItemSize;
        var spacing = SubMenuRowSpacing;
        var cellHeight = SubMenuRowCellHeight;

        // Position the row so the green circle sits just above the FAB top edge with
        // SubMenuRowAboveFabGap of clearance. The cell extends below the circle to host the
        // label without growing the visible spacing toward the FAB.
        var fabTopY = barTop - FabSize * FabFloatingOverlapRatio;
        var itemY = fabTopY - SubMenuRowAboveFabGap - itemSize;

        // Total row width so we can clamp it within screen bounds.
        var totalRowWidth = count * itemSize + (count - 1) * spacing;
        var rowLeft = fabCenterX - totalRowWidth / 2d;

        // Edge-guard: shift the whole row so no item clips the screen edge.
        var minLeft = SubMenuRowEdgeGuard;
        var maxRight = width - SubMenuRowEdgeGuard;
        if (rowLeft < minLeft)
        {
            rowLeft = minLeft;
        }
        else if (rowLeft + totalRowWidth > maxRight)
        {
            rowLeft = maxRight - totalRowWidth;
        }

        for (var i = 0; i < _subMenuButtons.Count; i++)
        {
            var itemX = rowLeft + i * (itemSize + spacing);
            AbsoluteLayout.SetLayoutBounds(
                _subMenuButtons[i].Root,
                new Rect(itemX, itemY, itemSize, cellHeight));
        }
    }

    private double ResolveItemOpenProgress(int index)
    {
        if (_openProgress <= 0d)
        {
            return 0d;
        }

        // Sequential stagger from left to right for the horizontal row.
        var delay = Math.Min(SubMenuRowStaggerCap, index * SubMenuRowStaggerStep);
        return Math.Clamp((_openProgress - delay) / Math.Max(0.01d, 1d - delay), 0d, 1d);
    }

    /// <summary>
    ///     Variant used by UpdateSubMenuButtonVisuals for center-outward stagger.
    ///     <paramref name="distanceFromCenter" /> is a fractional distance (0 = center).
    /// </summary>
    private double ResolveItemOpenProgressWithDelay(double distanceFromCenter)
    {
        if (_openProgress <= 0d)
        {
            return 0d;
        }

        var delay = Math.Min(SubMenuRowStaggerCap, distanceFromCenter * SubMenuRowStaggerStep * 2d);
        return Math.Clamp((_openProgress - delay) / Math.Max(0.01d, 1d - delay), 0d, 1d);
    }

    private double ResolveFabVisibilityProgress()
    {
        if (_centerProgress <= HiddenVisibilityThreshold)
        {
            return 0d;
        }

        if (IsCenterFloating)
        {
            return _centerProgress;
        }

        var hideRange = Math.Max(0.01d, 1d - FabHideInvisibleAtCenterProgress);
        return Math.Clamp((_centerProgress - FabHideInvisibleAtCenterProgress) / hideRange, 0d, 1d);
    }

    private bool IsRtl()
    {
        if (FlowDirection == FlowDirection.RightToLeft)
        {
            return true;
        }

        if (FlowDirection == FlowDirection.LeftToRight)
        {
            return false;
        }

        return G9Culture.IsRtl;
    }

    #endregion

    #region Misc helpers

    private static string ResolveCulturalFont()
    {
        return G9Culture.ResolveAppFont("CulturalFont", G9Culture.RtlFontFamily);
    }

    /// <summary>
    ///     Resolves the label shown on the overflow trigger. Honours an explicit
    ///     <see cref="OverflowText" /> override first, then falls back to the localized
    ///     <c>More</c> string from <c>AppDictionary</c>.
    /// </summary>
    private string ResolveOverflowLabelText()
    {
        if (!string.IsNullOrEmpty(OverflowText))
        {
            return OverflowText!;
        }

        return G9Strings.Resolve("More") ?? "More";
    }

    private static double EaseOutCubic(double value)
    {
        var t = Math.Clamp(value, 0d, 1d) - 1d;
        return (t * t * t) + 1d;
    }

    private static double EaseOutBackLite(double value)
    {
        var t = Math.Clamp(value, 0d, 1d) - 1d;
        return 1d + (t * t * ((1.35d * t) + 0.35d));
    }

    private static ObservableCollection<G9TabBarItem> CreateDefaultItems()
    {
        return
        [
            new G9TabBarItem("Dashboard", G9Glyph.Menu) { AutomationId = "G9TabBar_Dashboard" },
            new G9TabBarItem("Tasks", G9Glyph.Check) { AutomationId = "G9TabBar_Tasks" },
            new G9TabBarItem("Create", G9Glyphs.Plus) { AutomationId = "G9TabBar_Create" },
            new G9TabBarItem("Announcements", G9Glyph.Info)
            {
                AutomationId = "G9TabBar_Announcements"
            },
            new G9TabBarItem("Profile", G9Glyph.Info) { AutomationId = "G9TabBar_Profile" }
        ];
    }

    private static ObservableCollection<G9TabBarItem> CreateDefaultSubMenuItems()
    {
        return
        [
            new G9TabBarItem("Requests", G9Glyph.Plus)
            {
                AutomationId = "G9TabBar_Requests", AngleDegrees = -112d
            },
            new G9TabBarItem("Teams", G9Glyph.Info)
            {
                AutomationId = "G9TabBar_Teams", AngleDegrees = -68d
            },
            new G9TabBarItem("Reports", G9Glyph.Info)
            {
                AutomationId = "G9TabBar_Reports", AngleDegrees = -24d
            },
            new G9TabBarItem("Approvals", G9Glyph.Check)
            {
                AutomationId = "G9TabBar_Approvals", AngleDegrees = -156d
            }
        ];
    }

    #endregion
}
