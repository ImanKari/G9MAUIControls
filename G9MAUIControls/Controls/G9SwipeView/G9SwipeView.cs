using G9MAUIControls.Icons;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using NativeSwipeItemView = Microsoft.Maui.Controls.SwipeItemView;
using NativeSwipeItems = Microsoft.Maui.Controls.SwipeItems;
using NativeSwipeView = Microsoft.Maui.Controls.SwipeView;

namespace G9MAUIControls.Controls;

/// <summary>
///     Material-style swipe-to-reveal control. Wraps <see cref="NativeSwipeView" /> with
///     four polish layers the native control lacks:
///     <list type="bullet">
///         <item>
///             <b>Card-aware clipping.</b> The whole swipe surface is wrapped in an outer
///             rounded <see cref="Border" /> with auto-clip via <see cref="Border.StrokeShape" />.
///             The rectangular action panes that the native SwipeView paints are clipped to
///             the rounded shape, so the panes look like they live INSIDE the card —
///             no more rectangular overflow above and below the rounded corners (the bug
///             the native SwipeView shows when its content is a rounded Border).
///         </item>
///         <item>
///             <b>Font icons that actually render.</b> Action panes are
///             <see cref="NativeSwipeItemView" /> instances, and a font glyph placed in a
///             text-rendering view inside one paints as a tofu box on Android: the code point
///             reaches the platform, but the font file is not loaded for that particular TextView,
///             so Roboto is substituted. <see cref="BuildActionIcon" /> therefore routes font
///             glyphs through a <see cref="FontImageSource" />, which rasterizes them in the
///             platform's IMAGE pipeline before the pane renderer runs. Built-in vector glyphs go
///             in directly — they have no font to fail to resolve. This is the one place in the
///             suite that bypasses <see cref="G9IconFactory" />, and the reason is on
///             <see cref="BuildActionIcon" />.
///         </item>
///         <item>
///             <b>Theme-resolved colors with destructive shortcut.</b> When
///             <see cref="G9SwipeAction.Background" /> is null we resolve the theme
///             palette automatically (<c>Primary</c> normally, <c>Error</c> when
///             <see cref="G9SwipeAction.IsDestructive" /> is true).
///         </item>
///         <item>
///             <b>Culture-aware refresh that honors leading-edge semantics.</b> Native
///             SwipeView keys its <c>LeftItems</c> / <c>RightItems</c> to physical screen
///             edges and never inverts on a FlowDirection flip. We swap our
///             <see cref="LeftActions" /> / <see cref="RightActions" /> assignments under
///             RTL so the leading-edge action (the swipe-from edge a left-to-right reader
///             pulls from) stays on the user's leading edge — physical right in RTL,
///             physical left in LTR. A <see cref="G9Culture.CultureChanged" />
///             subscription rebuilds the panes (with translated labels) and re-applies
///             the swap so a Persian session and an English session look correct without
///             consumers touching the action collections.
///         </item>
///     </list>
/// </summary>
[ContentProperty(nameof(CardContent))]
public partial class G9SwipeView : ContentView
{
    /// <summary>
    ///     Default per-action width (dp). Matches the Material 3 spec for "icon + 1-line
    ///     label" swipe actions. Override per-action via <see cref="G9SwipeAction.WidthRequest" />.
    /// </summary>
    public const double DefaultActionWidth = 88;

    /// <summary>
    ///     Builds an action pane's icon. This is the ONE place in the suite that does not go
    ///     through <see cref="G9IconFactory" />, because the native
    ///     <see cref="NativeSwipeItemView" /> is hostile to font glyphs.
    ///     <para>
    ///         A font glyph placed in a text-rendering view inside a swipe pane paints as a tofu
    ///         box on Android: the code point reaches the platform, but the font file is not
    ///         loaded for that particular TextView, so Roboto is substituted. Routing it through a
    ///         <see cref="FontImageSource" /> makes the platform's IMAGE pipeline rasterize the
    ///         glyph against the explicit family BEFORE the pane renderer runs, so the pane
    ///         receives an already-drawn bitmap. That is the same path a working
    ///         <c>&lt;Image Source="{FontImage …}"/&gt;</c> takes anywhere else.
    ///     </para>
    ///     <para>
    ///         A <b>built-in</b> glyph has no font to fail to resolve — it is vector geometry on a
    ///         <see cref="GraphicsView" /> — so it is added directly. That is not a shortcut: a
    ///         <see cref="FontImageSource" /> cannot express it at all.
    ///     </para>
    /// </summary>
    private static View? BuildActionIcon(G9IconSource icon, Color foreground, double size)
    {
        if (icon.IsEmpty)
        {
            return null;
        }

        if (icon.IsBuiltIn)
        {
            return new G9IconView { Icon = icon, Color = foreground, Size = size };
        }

        return new Image
        {
            Source = new FontImageSource
            {
                Glyph = icon.Glyph,
                FontFamily = icon.FontFamily,
                Color = foreground,
                Size = size
            },
            WidthRequest = size,
            HeightRequest = size,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
    }

    private readonly Border _frame;
    private readonly NativeSwipeView _swipe;
    private readonly ContentView _userContentHost;

#if WINDOWS
    // Custom Windows drag-to-reveal swipe (the native SwipeControl crashes — see ctor —
    // and only supports touch). The card body (_userContentHost) sits on top of a
    // background actions layer and is translated horizontally by a PanGestureRecognizer:
    //   • TranslationX > 0 reveals the physical-LEFT pane (_winLeftPane)
    //   • TranslationX < 0 reveals the physical-RIGHT pane (_winRightPane)
    private readonly Grid _winSwipeHost = null!;
    private readonly HorizontalStackLayout _winLeftPane = null!;
    private readonly HorizontalStackLayout _winRightPane = null!;
    private readonly HashSet<G9SwipeAction> _winSubscribed = [];
    private double _winPanStart;
    private bool? _winSwipeLock;
    private double _winLeftWidth;
    private double _winRightWidth;
#endif

    private readonly ObservableCollection<G9SwipeAction> _leftActions = [];
    private readonly ObservableCollection<G9SwipeAction> _rightActions = [];

    private readonly Dictionary<G9SwipeAction, NativeSwipeItemView> _renderedItems = [];

    private EventHandler<G9CultureEventArgs>? _cultureHandler;

    /// <summary>
    ///     Outer corner radius of the card frame. The action panes that the native
    ///     SwipeView paints are clipped to this radius via <c>Border.IsClippedToBounds</c>.
    ///     Default <c>14</c> matches <see cref="G9Metrics.RadiusMd" />.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnCornerRadiusChanged))]
    private double _cornerRadius;

    /// <summary>
    ///     The user's content view (the visible card body). XAML content properties go
    ///     here directly — when consumers set <c>&lt;newControls:G9SwipeView&gt;...&lt;/&gt;</c>
    ///     the body is captured and pushed into the inner SwipeView's content slot.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnCardContentChanged))]
    private View? _cardContent;

    /// <summary>
    ///     Optional card background painted by the outer frame. When set, the wrapper owns
    ///     the card's fill so the inner content can be a flat (square, borderless) layout —
    ///     the whole assembly (card body + revealed action panes) then shares the single
    ///     rounded clip, giving the clean edge-to-edge seam the design system expects.
    ///     When null the frame stays transparent and the consumer's inner content supplies
    ///     its own background (legacy behaviour).
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnCardChromeChanged))]
    private Color? _cardBackground;

    /// <summary>Optional border color painted by the outer frame. Pairs with <see cref="CardStrokeThickness" />.</summary>
    [AutoBindable(OnChanged = nameof(OnCardChromeChanged))]
    private Color? _cardStroke;

    /// <summary>Border thickness for <see cref="CardStroke" />. Default <c>0</c> (no visible border).</summary>
    [AutoBindable(OnChanged = nameof(OnCardChromeChanged))]
    private double _cardStrokeThickness;

    public G9SwipeView()
    {
        _userContentHost = new ContentView { BackgroundColor = Colors.Transparent };

#if WINDOWS
        // Microsoft.Maui.Controls.SwipeView wraps Microsoft.UI.Xaml.Controls.SwipeControl
        // on WinUI 3, and SwipeControl reliably tears the process down with a stowed
        // exception from <c>Microsoft.UI.Xaml.dll</c> (status code <c>0xC000027B</c>,
        // STATUS_STOWED_EXCEPTION) within ~12-17 seconds of being instantiated even on
        // an idle page — no user interaction required, no managed exception that the
        // CLR can catch. It also only supports touch (not a mouse pointer) even when it
        // doesn't crash. So on Windows we do NOT use the native SwipeView at all.
        //
        // Instead we build a lightweight custom drag-to-reveal: an actions layer (two
        // edge-docked panes) sits behind the card body, and a PanGestureRecognizer
        // translates the body horizontally to reveal a pane. This works with a mouse
        // drag on desktop. See BuildWindowsSwipe / OnWinPan / the #if WINDOWS RebuildAll
        // branch below. Mobile (Android, iOS, MacCatalyst) keeps the native SwipeView.
        _swipe = null!;

        _winLeftPane = new HorizontalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Fill
        };
        _winRightPane = new HorizontalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Fill
        };

        // Actions layer (index 0, behind) + card body (index 1, on top and draggable).
        _winSwipeHost = new Grid
        {
            BackgroundColor = Colors.Transparent
        };
        _winSwipeHost.Children.Add(_winLeftPane);
        _winSwipeHost.Children.Add(_winRightPane);
        _winSwipeHost.Children.Add(_userContentHost);

        var winPan = new PanGestureRecognizer();
        winPan.PanUpdated += OnWinPan;
        _userContentHost.GestureRecognizers.Add(winPan);

        // A tap on the revealed body (while open) closes it — matches native behaviour.
        var winTap = new TapGestureRecognizer();
        winTap.Tapped += OnWinBodyTapped;
        _userContentHost.GestureRecognizers.Add(winTap);

        _frame = new Border
        {
            StrokeThickness = 0,
            Stroke = Colors.Transparent,
            Background = null,
            BackgroundColor = Colors.Transparent,
            StrokeShape = G9Colors.Round(G9Metrics.RadiusMd),
            Padding = 0,
            Content = _winSwipeHost
        };
#else
        _swipe = new NativeSwipeView
        {
            Content = _userContentHost,
            BackgroundColor = Colors.Transparent
        };

        _frame = new Border
        {
            StrokeThickness = 0,
            Stroke = Colors.Transparent,
            Background = null,
            BackgroundColor = Colors.Transparent,
            StrokeShape = G9Colors.Round(G9Metrics.RadiusMd),
            Padding = 0,
            Content = _swipe
        };
#endif

        Content = _frame;

        // Set the bindable AFTER _frame is built so the OnCornerRadiusChanged callback
        // (which mutates _frame.StrokeShape) doesn't NRE. The setter still re-applies
        // to the newly-constructed Border so initial state is consistent.
        CornerRadius = G9Metrics.RadiusMd;

        _leftActions.CollectionChanged += OnLeftActionsChanged;
        _rightActions.CollectionChanged += OnRightActionsChanged;
    }

    /// <summary>Actions revealed by swiping the content from the leading edge.</summary>
    public ObservableCollection<G9SwipeAction> LeftActions => _leftActions;

    /// <summary>Actions revealed by swiping the content from the trailing edge.</summary>
    public ObservableCollection<G9SwipeAction> RightActions => _rightActions;

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        if (Handler is null)
        {
            // View detached — release the culture subscription so the page can be
            // garbage-collected without leaking back to the static culture service.
            if (_cultureHandler is not null)
            {
                G9Culture.CultureChanged -= _cultureHandler;
                _cultureHandler = null;
            }
        }
        else
        {
            _cultureHandler ??= (_, _) => RebuildAll();
            G9Culture.CultureChanged -= _cultureHandler;
            G9Culture.CultureChanged += _cultureHandler;
        }
    }

    private void OnCornerRadiusChanged()
    {
        // Defensive null-check: the AutoBindable-generated setter may fire during the
        // base ContentView's bindable-property registration phase, before _frame has
        // been constructed. The constructor re-applies CornerRadius after _frame is
        // built so this null path is only hit during initialization and is safe to skip.
        if (_frame is null) return;
        _frame.StrokeShape = G9Colors.Round(CornerRadius);
    }

    private void OnCardContentChanged()
    {
        if (_userContentHost is null) return;
        _userContentHost.Content = CardContent;
    }

    /// <summary>
    ///     Applies the optional card chrome (background + border) onto the outer frame.
    ///     When the consumer sets <see cref="CardBackground" /> the frame owns the card
    ///     fill so the inner content can be flat — the whole card + revealed panes then
    ///     share the single rounded clip (no rounded-inner-card-vs-square-pane seam).
    /// </summary>
    private void OnCardChromeChanged()
    {
        if (_frame is null) return;

        _frame.BackgroundColor = CardBackground ?? Colors.Transparent;
        _frame.Stroke = CardStroke is not null ? new SolidColorBrush(CardStroke) : Colors.Transparent;
        _frame.StrokeThickness = CardStroke is not null ? CardStrokeThickness : 0;
    }

    private void OnLeftActionsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildAll();

    private void OnRightActionsChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        RebuildAll();

    /// <summary>
    ///     Native SwipeView keys its panes by **physical** screen edge — LeftItems always
    ///     stays on screen-left regardless of FlowDirection. Apps want the opposite:
    ///     <see cref="LeftActions" /> means *leading* (the swipe-from edge a left-to-right
    ///     reader pulls from) and should appear on the **right** edge in an RTL layout
    ///     (where reading flows right-to-left, so the leading edge is the right edge).
    ///     We swap the pane assignments under RTL so the user-facing semantics match
    ///     across both cultures with no XAML changes.
    /// </summary>
    private void RebuildAll()
    {
#if WINDOWS
        // Windows custom swipe: build the two edge-docked panes from the action
        // collections (with the same RTL leading-edge swap as mobile). The native
        // SwipeView doesn't exist on this platform — see ctor.
        RebuildWindowsPanes();
        return;
#else
        var rtl = G9Culture.IsRtl;
        var leftPane = rtl ? _rightActions : _leftActions;
        var rightPane = rtl ? _leftActions : _rightActions;

        _swipe.LeftItems = BuildItems(leftPane);
        _swipe.RightItems = BuildItems(rightPane);

        // Drop renderers no longer in use after a flip so a destroyed action stops
        // receiving property-change notifications.
        var alive = new HashSet<G9SwipeAction>();
        foreach (var a in leftPane) alive.Add(a);
        foreach (var a in rightPane) alive.Add(a);
        foreach (var stale in _renderedItems.Keys.Where(k => !alive.Contains(k)).ToList())
        {
            stale.PropertyChanged -= OnActionPropertyChanged;
            _renderedItems.Remove(stale);
        }
#endif
    }

    private NativeSwipeItems BuildItems(IList<G9SwipeAction> source)
    {
        var items = new NativeSwipeItems
        {
            Mode = SwipeMode.Reveal,
            SwipeBehaviorOnInvoked = SwipeBehaviorOnInvoked.Close
        };

        foreach (var action in source)
        {
            if (!_renderedItems.TryGetValue(action, out var swipeItem))
            {
                action.PropertyChanged += OnActionPropertyChanged;
                swipeItem = BuildSwipeItem(action);
                _renderedItems[action] = swipeItem;
            }
            ApplyActionVisuals(swipeItem, action);
            items.Add(swipeItem);
        }

        return items;
    }

    private NativeSwipeItemView BuildSwipeItem(G9SwipeAction action)
    {
        var item = new NativeSwipeItemView();
        item.Invoked += (_, _) =>
        {
            if (action.Command is { } cmd && cmd.CanExecute(action.CommandParameter))
            {
                cmd.Execute(action.CommandParameter);
            }
            action.RaiseInvoked();
        };
        return item;
    }

    private void OnActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not G9SwipeAction action) return;
        if (!_renderedItems.TryGetValue(action, out var item)) return;
        ApplyActionVisuals(item, action);
    }

    private static void ApplyActionVisuals(NativeSwipeItemView item, G9SwipeAction action)
    {
        var palette = G9Palette.Current;
        var background = action.Background ?? (action.IsDestructive ? palette.Error : palette.Primary);
        var foreground = action.Foreground ?? (action.IsDestructive ? palette.OnError : palette.OnPrimary);

        item.IsVisible = action.IsVisible;
        item.IsEnabled = action.IsEnabled;
        item.WidthRequest = action.WidthRequest > 0 ? action.WidthRequest : DefaultActionWidth;
        item.BackgroundColor = background;

        // Build the visual content: Image (icon) stacked above a Label.
        //
        // We use Image + FontImageSource rather than the G9IconView View used elsewhere
        // in the project because G9IconView's binding-driven font resolution does not
        // complete reliably inside SwipeItemView's renderer on Android — the glyph
        // codepoint reaches the platform but the font file isn't loaded for that
        // specific TextView, so the platform substitutes the Roboto font and the
        // codepoint shows as a tofu box. FontImageSource forces the platform image
        // pipeline to rasterize the glyph against the explicit font family BEFORE
        // the swipe-pane renderer sees it, so the bitmap arrives fully drawn and
        // renders identically to any Image in any other control.
        var stack = new VerticalStackLayout
        {
            Spacing = 4,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        var iconView = BuildActionIcon(action.Icon ?? G9IconSource.Empty, foreground, action.IconSize);
        if (iconView is not null)
        {
            stack.Children.Add(iconView);
        }

        if (!string.IsNullOrEmpty(action.Text))
        {
            stack.Children.Add(new Label
            {
                Text = action.Text,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = foreground,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.NoWrap,
                MaxLines = 1,
                InputTransparent = true
            });
        }

        // The Grid host sits inside the SwipeItemView. Its background paints the
        // ENTIRE pane area — that's what fills the rectangle rather than leaving the
        // glyph on a transparent area where the SwipeView's container background
        // (often dark / theme-default) shows through.
        var host = new Grid
        {
            BackgroundColor = background,
            Padding = new Thickness(8),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = false
        };
        host.Children.Add(stack);

        item.Content = host;
    }

#if WINDOWS
    // ── Windows custom drag-to-reveal swipe ─────────────────────────────────────
    // The native SwipeControl crashes on WinUI 3 and only supports touch (see ctor),
    // so on Windows we render the action panes ourselves behind the card body and pan
    // the body with a mouse drag.

    private const double WinOpenThresholdFraction = 0.5; // drag past 50% of pane width to open
    private const uint WinAnimMs = 160;

    /// <summary>
    ///     Rebuild the two physical edge panes from the action collections, applying the
    ///     same RTL leading-edge swap as the mobile path: <see cref="LeftActions" /> is
    ///     the leading edge (physical left in LTR, physical right in RTL).
    /// </summary>
    private void RebuildWindowsPanes()
    {
        if (_winLeftPane is null || _winRightPane is null) return;

        var rtl = G9Culture.IsRtl;
        var leftSource = rtl ? _rightActions : _leftActions;
        var rightSource = rtl ? _leftActions : _rightActions;

        BuildWindowsPane(_winLeftPane, leftSource);
        BuildWindowsPane(_winRightPane, rightSource);

        // Close any open state and recompute pane widths on the next layout pass.
        WinResetClosed();
    }

    private void BuildWindowsPane(HorizontalStackLayout pane, IList<G9SwipeAction> source)
    {
        pane.Children.Clear();

        foreach (var action in source)
        {
            if (_winSubscribed.Add(action))
            {
                action.PropertyChanged += OnWinActionPropertyChanged;
            }

            if (!action.IsVisible) continue;

            pane.Children.Add(BuildWindowsActionButton(action));
        }
    }

    private View BuildWindowsActionButton(G9SwipeAction action)
    {
        var palette = G9Palette.Current;
        var background = action.Background ?? (action.IsDestructive ? palette.Error : palette.Primary);
        var foreground = action.Foreground ?? (action.IsDestructive ? palette.OnError : palette.OnPrimary);
        var width = action.WidthRequest > 0 ? action.WidthRequest : DefaultActionWidth;

        var stack = new VerticalStackLayout
        {
            Spacing = 4,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        var iconView = BuildActionIcon(action.Icon ?? G9IconSource.Empty, foreground, action.IconSize);
        if (iconView is not null)
        {
            stack.Children.Add(iconView);
        }

        if (!string.IsNullOrEmpty(action.Text))
        {
            stack.Children.Add(new Label
            {
                Text = action.Text,
                FontSize = 12,
                FontAttributes = FontAttributes.Bold,
                TextColor = foreground,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                LineBreakMode = LineBreakMode.NoWrap,
                MaxLines = 1,
                InputTransparent = true
            });
        }

        var host = new Grid
        {
            WidthRequest = width,
            BackgroundColor = background,
            Padding = new Thickness(8),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        host.Children.Add(stack);

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            if (!action.IsEnabled) return;
            WinResetClosed();
            if (action.Command is { } cmd && cmd.CanExecute(action.CommandParameter))
            {
                cmd.Execute(action.CommandParameter);
            }
            action.RaiseInvoked();
        };
        host.GestureRecognizers.Add(tap);

        return host;
    }

    private void OnWinActionPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RebuildWindowsPanes();

    private void OnWinBodyTapped(object? sender, TappedEventArgs e)
    {
        // If a pane is open, a tap on the body closes it (and the tap is "consumed" by
        // the close). If already closed, do nothing — let inner content handle the tap.
        if (Math.Abs(_userContentHost.TranslationX) > 0.5)
        {
            WinResetClosed();
        }
    }

    private void OnWinPan(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _winPanStart = _userContentHost.TranslationX;
                _winSwipeLock = null;
                MeasureWinPaneWidths();
                break;

            case GestureStatus.Running:
            {
                var target = _winPanStart + e.TotalX;

                // Lock to a direction on first meaningful horizontal movement so a
                // mostly-vertical scroll drag doesn't fight the list.
                if (_winSwipeLock is null && Math.Abs(e.TotalX) > 6)
                {
                    _winSwipeLock = e.TotalX > 0; // true = opening the left pane
                }

                // Clamp to the available pane widths. Positive translation reveals the
                // left pane (max = left pane width); negative reveals the right pane.
                target = Math.Clamp(target, -_winRightWidth, _winLeftWidth);

                // Don't allow opening a side that has no actions.
                if (target > 0 && _winLeftWidth <= 0) target = 0;
                if (target < 0 && _winRightWidth <= 0) target = 0;

                _userContentHost.TranslationX = target;
                break;
            }

            case GestureStatus.Completed:
            case GestureStatus.Canceled:
            {
                var tx = _userContentHost.TranslationX;
                // Settle open or closed based on how far past the threshold the drag went.
                if (tx > 0 && _winLeftWidth > 0 && tx >= _winLeftWidth * WinOpenThresholdFraction)
                {
                    WinAnimateTo(_winLeftWidth);
                }
                else if (tx < 0 && _winRightWidth > 0 && -tx >= _winRightWidth * WinOpenThresholdFraction)
                {
                    WinAnimateTo(-_winRightWidth);
                }
                else
                {
                    WinAnimateTo(0);
                }
                _winSwipeLock = null;
                break;
            }
        }
    }

    private void MeasureWinPaneWidths()
    {
        _winLeftWidth = _winLeftPane.Width > 0 ? _winLeftPane.Width : SumActionWidths(_winLeftPane);
        _winRightWidth = _winRightPane.Width > 0 ? _winRightPane.Width : SumActionWidths(_winRightPane);
    }

    private static double SumActionWidths(Layout pane)
    {
        double sum = 0;
        foreach (var child in pane.Children)
        {
            if (child is VisualElement ve)
            {
                sum += ve.Width > 0 ? ve.Width : ve.WidthRequest;
            }
        }
        return sum;
    }

    private void WinAnimateTo(double translationX)
    {
        _ = _userContentHost.TranslateToAsync(translationX, 0, WinAnimMs, Easing.CubicOut);
    }

    private void WinResetClosed()
    {
        if (_userContentHost is null) return;
        if (Math.Abs(_userContentHost.TranslationX) > 0.5)
        {
            _ = _userContentHost.TranslateToAsync(0, 0, WinAnimMs, Easing.CubicOut);
        }
        else
        {
            _userContentHost.TranslationX = 0;
        }
    }
#endif
}
