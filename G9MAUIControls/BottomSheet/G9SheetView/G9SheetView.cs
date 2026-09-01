using Microsoft.Maui.Controls.Shapes;

namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Cross-platform bottom-sheet control that powers <see cref="G9BottomSheetHelper" />.
///     Replaces the previously-vendored Syncfusion <c>SfG9BottomSheet</c> with a hand-rolled
///     implementation that uses only public MAUI primitives (<see cref="Grid" />,
///     <see cref="Border" />, <see cref="Microsoft.Maui.Controls.Animation" />). The helper
///     consumes the exact same property / event surface (<see cref="State" />,
///     <see cref="AllowedState" />, <see cref="IsOpen" />, <see cref="StateChanged" />,
///     <see cref="PositionChanged" />, <see cref="AnimationDurationProvider" />) so the
///     migration was source-only — no behavior changes.
/// </summary>
[ContentProperty(nameof(Content))]
public partial class G9SheetView : Grid
{
    #region Constants

    private const double MinimizedHeight = 100;
    private const double DefaultGrabberAreaHeight = 30;
    private const double DefaultGrabberHeight = 4;
    private const double DefaultGrabberWidth = 32;
    private const double DefaultGrabberCornerRadius = 12;
    private const double MinHalfExpandedRatio = 0.1;
    private const double MaxHalfExpandedRatio = 0.9;
    private const double MinFullExpandedRatio = 0.1;
    private const double MaxFullExpandedRatio = 1;
    private const double DefaultHalfExpandedRatio = 0.5;
    private const double DefaultFullExpandedRatio = 1;
    private const double DefaultOverlayOpacity = 0.5;
    /// <summary>
    ///     How close (dp) the body has to be to a detent to count as resting AT it. Deliberately
    ///     larger than the 2 dp settle overshoot <see cref="AnimateG9BottomSheet" /> adds, so a sheet
    ///     that finished its open animation reads as "at the detent" rather than 2 dp short of it.
    /// </summary>
    private const double DetentTolerance = 4;

    private const string SheetAnimationName = "G9SheetViewMotion";
    private const string OverlayAnimationName = "G9SheetViewOverlay";

    #endregion

    #region Fields

    private readonly Grid _overlayGrid;
    private readonly G9SheetViewBorder _bottomSheet;
    private readonly Grid _bottomSheetContent;
    private readonly Border _contentBorder;
    private readonly Border _grabber;
    private readonly Grid _grabberGrid;
    private readonly RoundRectangle _grabberStrokeShape;
    private readonly RoundRectangle _bottomSheetStrokeShape;
    private readonly G9SheetViewStateChangedEventArgs _stateChangedArgs = new();

    private bool _isHalfExpanded = true;
    private bool _isSheetOpen;
    private bool _isPointerPressed;
    private bool _isOverlayAdded;
    private bool _pendingShow;
    private double _initialTouchY;

    /// <summary>Raw finger travel over the current gesture — drives the drag-to-CLOSE decision.</summary>
    private double _dragTravelY;

    /// <summary>How far the sheet body actually moved over the current gesture — drives the SNAP decision.</summary>
    private double _sheetTravelY;

    #endregion

    #region Bindable Properties

    public static readonly BindableProperty ContentProperty = BindableProperty.Create(
        nameof(Content), typeof(View), typeof(G9SheetView), null,
        propertyChanged: (b, _, _) => ((G9SheetView)b).RebuildChildren());

    public static readonly BindableProperty G9BottomSheetContentProperty = BindableProperty.Create(
        nameof(G9BottomSheetContent), typeof(View), typeof(G9SheetView), null,
        propertyChanged: (b, _, n) => ((G9SheetView)b).SetG9BottomSheetContent(n as View));

    public static readonly BindableProperty StateProperty = BindableProperty.Create(
        nameof(State), typeof(G9SheetViewState), typeof(G9SheetView),
        G9SheetViewState.Hidden,
        propertyChanged: (b, o, n) => ((G9SheetView)b).OnStateChangedInternal((G9SheetViewState)o, (G9SheetViewState)n));

    public static readonly BindableProperty HalfExpandedRatioProperty = BindableProperty.Create(
        nameof(HalfExpandedRatio), typeof(double), typeof(G9SheetView),
        DefaultHalfExpandedRatio,
        propertyChanged: (b, o, n) => ((G9SheetView)b).UpdateHalfExpandedRatio((double)n));

    public static readonly BindableProperty FullExpandedRatioProperty = BindableProperty.Create(
        nameof(FullExpandedRatio), typeof(double), typeof(G9SheetView),
        DefaultFullExpandedRatio,
        propertyChanged: (b, o, n) => ((G9SheetView)b).UpdateFullExpandedRatio((double)n));

    public static readonly BindableProperty CollapsedHeightProperty = BindableProperty.Create(
        nameof(CollapsedHeight), typeof(double), typeof(G9SheetView),
        MinimizedHeight,
        propertyChanged: (b, o, n) => ((G9SheetView)b).UpdateCollapsedHeight((double)n));

    public static readonly BindableProperty AllowedStateProperty = BindableProperty.Create(
        nameof(AllowedState), typeof(G9SheetViewAllowedState), typeof(G9SheetView),
        G9SheetViewAllowedState.All,
        propertyChanged: (b, _, _) => ((G9SheetView)b).RecomputeAllowedState());

    public static readonly BindableProperty IsModalProperty = BindableProperty.Create(
        nameof(IsModal), typeof(bool), typeof(G9SheetView), true,
        propertyChanged: (b, _, n) => ((G9SheetView)b).OnIsModalChanged((bool)n));

    public static readonly BindableProperty ShowGrabberProperty = BindableProperty.Create(
        nameof(ShowGrabber), typeof(bool), typeof(G9SheetView), true,
        propertyChanged: (b, _, n) => ((G9SheetView)b).OnShowGrabberChanged((bool)n));

    public static readonly BindableProperty IsOpenProperty = BindableProperty.Create(
        nameof(IsOpen), typeof(bool), typeof(G9SheetView), false,
        BindingMode.TwoWay,
        propertyChanged: (b, o, n) =>
        {
            if (!o.Equals(n))
            {
                ((G9SheetView)b).UpdateOpenStateFromBinding((bool)n);
            }
        });

    public static readonly BindableProperty GrabberBackgroundProperty = BindableProperty.Create(
        nameof(GrabberBackground), typeof(Brush), typeof(G9SheetView),
        new SolidColorBrush(Color.FromArgb("#CAC4D0")),
        propertyChanged: (b, _, n) => ((G9SheetView)b)._grabber.Background = (Brush)n);

    public new static readonly BindableProperty BackgroundProperty = BindableProperty.Create(
        nameof(Background), typeof(Brush), typeof(G9SheetView),
        new SolidColorBrush(Color.FromArgb("#F7F2FB")),
        propertyChanged: (b, _, n) => ((G9SheetView)b).UpdateBackground((Brush)n));

    public static readonly BindableProperty CornerRadiusProperty = BindableProperty.Create(
        nameof(CornerRadius), typeof(CornerRadius), typeof(G9SheetView),
        new CornerRadius(0),
        propertyChanged: (b, _, n) => ((G9SheetView)b).UpdateCornerRadius((CornerRadius)n));

    public static readonly BindableProperty ContentPaddingProperty = BindableProperty.Create(
        nameof(ContentPadding), typeof(Thickness), typeof(G9SheetView),
        new Thickness(5),
        propertyChanged: (b, _, n) => ((G9SheetView)b)._bottomSheet.Padding = (Thickness)n);

    public static readonly BindableProperty EnableSwipingProperty = BindableProperty.Create(
        nameof(EnableSwiping), typeof(bool), typeof(G9SheetView), true);

    public static readonly BindableProperty GrabberAreaHeightProperty = BindableProperty.Create(
        nameof(GrabberAreaHeight), typeof(double), typeof(G9SheetView),
        DefaultGrabberAreaHeight,
        propertyChanged: (b, _, _) => ((G9SheetView)b).UpdateGrabberRowHeight());

    public static readonly BindableProperty AnimationDurationProperty = BindableProperty.Create(
        nameof(AnimationDuration), typeof(double), typeof(G9SheetView), 300d);

    // Width-mode is kept for API parity with the original control. The helper sets it but
    // doesn't drive any layout from it — width is always controlled by the host grid.
    public static readonly BindableProperty ContentWidthModeProperty = BindableProperty.Create(
        nameof(ContentWidthMode), typeof(G9SheetViewContentWidthMode), typeof(G9SheetView),
        G9SheetViewContentWidthMode.Full,
        propertyChanged: (b, _, _) => ((G9SheetView)b).UpdateContentWidth());

    public static readonly BindableProperty G9BottomSheetContentWidthProperty = BindableProperty.Create(
        nameof(G9BottomSheetContentWidth), typeof(double), typeof(G9SheetView), 300d,
        propertyChanged: (b, _, _) => ((G9SheetView)b).UpdateContentWidth());

    public static readonly BindableProperty CollapseOnOverlayTapProperty = BindableProperty.Create(
        nameof(CollapseOnOverlayTap), typeof(bool), typeof(G9SheetView), false);

    /// <summary>
    ///     When <c>true</c> (default), drag-to-close gestures from the body raise
    ///     <see cref="BackRequested" />. Set to <c>false</c> to suppress the gesture entirely
    ///     for sheets that must be closed only through their toolbar back button (e.g. modal
    ///     forms with unsaved-changes guards). Note: even when <c>false</c>, the sheet still
    ///     animates back to its resting position after a drag — only the close request is
    ///     suppressed.
    /// </summary>
    public static readonly BindableProperty IsCancelableProperty = BindableProperty.Create(
        nameof(IsCancelable), typeof(bool), typeof(G9SheetView), true);

    /// <summary>
    ///     Pixels of downward drag on release that triggers a close request. Default 72 — the
    ///     same threshold the previous helper-side <c>SwipeGestureRecognizer</c> used.
    /// </summary>
    public static readonly BindableProperty DragCloseThresholdProperty = BindableProperty.Create(
        nameof(DragCloseThreshold), typeof(double), typeof(G9SheetView), 72d);

    /// <summary>
    ///     Whether <see cref="G9SheetViewState.Collapsed" /> is one of the resting DETENTS this
    ///     sheet may snap to, as opposed to merely the position it happens to open at.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <see cref="AllowedState" /> only says which of the two LARGE detents exists; it cannot
    ///         express "this sheet has a peek step as well". Without that distinction a two-detent
    ///         peek→medium sheet was indistinguishable from a fixed single-state one, so a downward
    ///         drag from its medium step raised a CLOSE request instead of stepping back down to the
    ///         peek (<see cref="HasSingleAllowedState" />).
    ///     </para>
    ///     <para>
    ///         Default <c>false</c>, which keeps every fixed sheet (full-screen, single-state,
    ///         fit-to-content) behaving exactly as before. <c>G9BottomSheetHelper</c> sets it true
    ///         only when the caller declared more than one state and one of them is Peek.
    ///     </para>
    /// </remarks>
    public static readonly BindableProperty AllowCollapsedStateProperty = BindableProperty.Create(
        nameof(AllowCollapsedState), typeof(bool), typeof(G9SheetView), false);

    /// <summary>
    ///     Whether a drag that starts on a SCROLLABLE part of the body expands the sheet to its
    ///     next detent before the content is allowed to scroll (default <c>true</c>).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This is the cross-platform equivalent of UIKit's
    ///         <c>UISheetPresentationController.prefersScrollingExpandsWhenScrolledToEdge</c> (whose
    ///         default is likewise <c>true</c>) and of the Material
    ///         <c>BottomSheetBehavior</c> + nested-scroll contract on Android: while the sheet is
    ///         below its largest detent the drag belongs to the SHEET, and the inner scroller only
    ///         takes over once there is nowhere further to expand.
    ///     </para>
    ///     <para>
    ///         Turning it off restores the "inner scroller always wins" behaviour — the content
    ///         scrolls at every detent and the sheet is resized only from the grabber / non-scrolling
    ///         chrome.
    ///     </para>
    ///     <para>
    ///         <b>It is a no-op for a single-detent sheet by construction</b> (full-screen,
    ///         single-state and fit-to-content sheets are always AT their maximum detent), which is
    ///         why the default can be <c>true</c> without changing any fixed sheet's behaviour.
    ///     </para>
    /// </remarks>
    public static readonly BindableProperty ScrollingExpandsSheetProperty = BindableProperty.Create(
        nameof(ScrollingExpandsSheet), typeof(bool), typeof(G9SheetView), true);

    #endregion

    #region Properties

    public View Content
    {
        get => (View)GetValue(ContentProperty);
        set => SetValue(ContentProperty, value);
    }

    public View G9BottomSheetContent
    {
        get => (View)GetValue(G9BottomSheetContentProperty);
        set => SetValue(G9BottomSheetContentProperty, value);
    }

    public G9SheetViewState State
    {
        get => (G9SheetViewState)GetValue(StateProperty);
        set => SetValue(StateProperty, value);
    }

    public double HalfExpandedRatio
    {
        get => (double)GetValue(HalfExpandedRatioProperty);
        set => SetValue(HalfExpandedRatioProperty, value);
    }

    public double FullExpandedRatio
    {
        get => (double)GetValue(FullExpandedRatioProperty);
        set => SetValue(FullExpandedRatioProperty, value);
    }

    public double CollapsedHeight
    {
        get => (double)GetValue(CollapsedHeightProperty);
        set => SetValue(CollapsedHeightProperty, value);
    }

    public G9SheetViewAllowedState AllowedState
    {
        get => (G9SheetViewAllowedState)GetValue(AllowedStateProperty);
        set => SetValue(AllowedStateProperty, value);
    }

    /// <inheritdoc cref="AllowCollapsedStateProperty" />
    public bool AllowCollapsedState
    {
        get => (bool)GetValue(AllowCollapsedStateProperty);
        set => SetValue(AllowCollapsedStateProperty, value);
    }

    /// <inheritdoc cref="ScrollingExpandsSheetProperty" />
    public bool ScrollingExpandsSheet
    {
        get => (bool)GetValue(ScrollingExpandsSheetProperty);
        set => SetValue(ScrollingExpandsSheetProperty, value);
    }

    public bool IsModal
    {
        get => (bool)GetValue(IsModalProperty);
        set => SetValue(IsModalProperty, value);
    }

    public bool ShowGrabber
    {
        get => (bool)GetValue(ShowGrabberProperty);
        set => SetValue(ShowGrabberProperty, value);
    }

    public bool IsOpen
    {
        get => (bool)GetValue(IsOpenProperty);
        set => SetValue(IsOpenProperty, value);
    }

    public new Brush Background
    {
        get => (Brush)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    public CornerRadius CornerRadius
    {
        get => (CornerRadius)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public Thickness ContentPadding
    {
        get => (Thickness)GetValue(ContentPaddingProperty);
        set => SetValue(ContentPaddingProperty, value);
    }

    public Brush GrabberBackground
    {
        get => (Brush)GetValue(GrabberBackgroundProperty);
        set => SetValue(GrabberBackgroundProperty, value);
    }

    public bool EnableSwiping
    {
        get => (bool)GetValue(EnableSwipingProperty);
        set => SetValue(EnableSwipingProperty, value);
    }

    public double GrabberAreaHeight
    {
        get => (double)GetValue(GrabberAreaHeightProperty);
        set => SetValue(GrabberAreaHeightProperty, value);
    }

    public double AnimationDuration
    {
        get => (double)GetValue(AnimationDurationProperty);
        set => SetValue(AnimationDurationProperty, value);
    }

    public G9SheetViewContentWidthMode ContentWidthMode
    {
        get => (G9SheetViewContentWidthMode)GetValue(ContentWidthModeProperty);
        set => SetValue(ContentWidthModeProperty, value);
    }

    public double G9BottomSheetContentWidth
    {
        get => (double)GetValue(G9BottomSheetContentWidthProperty);
        set => SetValue(G9BottomSheetContentWidthProperty, value);
    }

    public bool CollapseOnOverlayTap
    {
        get => (bool)GetValue(CollapseOnOverlayTapProperty);
        set => SetValue(CollapseOnOverlayTapProperty, value);
    }

    /// <summary>
    ///     When <c>true</c> (default), the control raises <see cref="BackRequested" /> if the
    ///     user drags the body past <see cref="DragCloseThreshold" /> downward and releases.
    ///     Set to <c>false</c> to suppress drag-to-close.
    /// </summary>
    public bool IsCancelable
    {
        get => (bool)GetValue(IsCancelableProperty);
        set => SetValue(IsCancelableProperty, value);
    }

    /// <summary>Pixels of downward drag on release that triggers <see cref="BackRequested" />.</summary>
    public double DragCloseThreshold
    {
        get => (double)GetValue(DragCloseThresholdProperty);
        set => SetValue(DragCloseThresholdProperty, value);
    }

    /// <summary>
    ///     Optional hook called once per sheet motion (open / close / drag-release snap /
    ///     programmatic <c>State =</c>) to compute the duration. Signature is
    ///     <c>(currentTranslationY, targetTranslationY, height) =&gt; durationMs</c>. When
    ///     <c>null</c>, the static <see cref="AnimationDuration" /> is used.
    /// </summary>
    public Func<double, double, double, int>? AnimationDurationProvider { get; set; }

    #endregion

    #region Events

    /// <summary>Raised whenever <see cref="State" /> transitions to a new value.</summary>
    public event EventHandler<G9SheetViewStateChangedEventArgs>? StateChanged;

    /// <summary>Raised every frame the sheet's visible height changes.</summary>
    public event EventHandler<G9SheetViewPositionChangedEventArgs>? PositionChanged;

    /// <summary>
    ///     Raised when a body drag-to-close gesture passes the threshold and the user releases.
    ///     Subscribers are expected to either call <see cref="Close" /> (or the helper's
    ///     <c>HandleBackRequest</c>) or do nothing — the control does NOT close itself in
    ///     response. This separation lets <see cref="G9BottomSheetHelper" /> route the close
    ///     through its existing <c>OnBackRequested</c> callback / <c>IsCancelable</c> rules.
    /// </summary>
    public event EventHandler<G9SheetViewBackRequestedEventArgs>? BackRequested;

    #endregion

    #region Construction

    public G9SheetView()
    {
        // ----------------------------------------------------------------------------
        // Hit-testing contract for the bottom sheet container
        // ----------------------------------------------------------------------------
        // The bottom sheet sits inside `OverlayHost` (G9PageTemplate.xaml) and fills
        // the entire screen even when its body is hidden or only the peek/half portion
        // is visible. Without these two flags the empty area of this Grid would still
        // capture taps on every supported platform — Android (`Clickable=true` is the
        // default for ContentViewGroup once children are added), iOS / Mac Catalyst
        // (UIView default is UserInteractionEnabled=true), and Windows (Panel reports
        // a hit-test rectangle). The result is the symptom the user reported: the
        // login page text boxes / login button can be focused via Tab but not tapped
        // because the sheet host swallows the touch first.
        //
        // The fix is the standard MAUI pattern for an overlay container: declare the
        // host itself input-transparent and tell children to opt-out of the cascade
        // so they keep their normal hit-testing. Now:
        //
        //   * the empty area of the sheet host falls through to OverlayHost (which is
        //     already InputTransparent=True / CascadeInputTransparent=False) and from
        //     there to ContentHost — the page content stays interactive while the
        //     sheet is closed, in peek state, or even fully expanded but non-modal;
        //   * the sheet body (`_bottomSheet`) and the internal modal overlay grid
        //     (`_overlayGrid`) keep `InputTransparent=False` (their default), so they
        //     each capture touches over their own bounds when visible.
        //
        // The previous Syncfusion vendored control achieved the same effect by setting
        // `UserInteractionEnabled = false` on the iOS platform view in `WireEvents()`.
        // That fix never crossed over to Android / Windows because the upstream code
        // assumed `IsModal = true` would always block the page underneath. Our helper
        // explicitly disables `IsModal` (it renders its own page-level overlay sibling
        // in OverlayHost), so the bottom sheet host can never assume the page is
        // already covered. Setting the cross-platform `InputTransparent` flags below
        // makes the sheet host behave the same on every target.
        InputTransparent = true;
        CascadeInputTransparent = false;

        _overlayGrid = new Grid
        {
            BackgroundColor = Color.FromArgb("#80000000"),
            Opacity = DefaultOverlayOpacity,
            IsVisible = true,
            InputTransparent = false
        };
        var overlayTap = new TapGestureRecognizer();
        overlayTap.Tapped += OnOverlayTapped;
        _overlayGrid.GestureRecognizers.Add(overlayTap);

        _grabberStrokeShape = new RoundRectangle { CornerRadius = DefaultGrabberCornerRadius };
        _grabber = new Border
        {
            Background = GrabberBackground,
            Stroke = Colors.Transparent,
            HeightRequest = DefaultGrabberHeight,
            WidthRequest = DefaultGrabberWidth,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = _grabberStrokeShape
        };

        _grabberGrid = new Grid { IsClippedToBounds = true };
        _grabberGrid.Children.Add(_grabber);

        _bottomSheetContent = new Grid
        {
            Background = Background,
            RowDefinitions =
            {
                new RowDefinition { Height = new GridLength(DefaultGrabberAreaHeight) },
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }
            }
        };
#if IOS || MACCATALYST
        // The newer `SafeAreaEdges` API per-edge replacement is internal in MAUI 10 (the
        // `SafeAreaElement.IgnoreSafeArea` attached property is not public yet). We keep the
        // deprecated single-flag API so the inner content grid can extend under the iOS notch
        // / camera cutout when the host opted in via `UseTopSafeAreaPadding = false`. Switch
        // to `SafeAreaEdges = SafeAreaEdges.None` once Microsoft makes that property public.
#pragma warning disable CS0618 // Layout.IgnoreSafeArea is obsolete
        _bottomSheetContent.IgnoreSafeArea = true;
#pragma warning restore CS0618
#endif

        Grid.SetRow(_grabberGrid, 0);
        _bottomSheetContent.Children.Add(_grabberGrid);

        _contentBorder = new Border { StrokeThickness = 0 };

        _bottomSheetStrokeShape = new RoundRectangle { CornerRadius = CornerRadius };
        _bottomSheet = new G9SheetViewBorder(this)
        {
            Background = Background,
            StrokeThickness = 0,
            VerticalOptions = LayoutOptions.Start,
            HorizontalOptions = LayoutOptions.Fill,
            HeightRequest = 0,
            IsVisible = false,
            StrokeShape = _bottomSheetStrokeShape,
            Content = _bottomSheetContent,
            Padding = ContentPadding
        };

        Children.Add(_bottomSheet);
        SizeChanged += OnSizeChanged;
    }

    #endregion

    #region Public API

    /// <summary>
    ///     Raised when the open motion started by <see cref="Show" /> reaches its resting position.
    ///     Unlike a fixed timer, this fires at the ACTUAL end of the (size-scaled) open animation,
    ///     so a short fit-to-content open completes in ~80 ms rather than the full configured
    ///     duration. <c>G9BottomSheetHelper</c> uses it to run <c>OpenedCommand</c> and deferred
    ///     "open then fill" loads as early as possible; its full-duration timer remains only as a
    ///     fallback for opens that never animate (e.g. the <c>IsOpen = true</c> fallback path).
    /// </summary>
    public event EventHandler? OpenMotionCompleted;

    /// <summary>Show the sheet, animating from its current resting position.</summary>
    public void Show()
    {
        _bottomSheet.IsVisible = true;

        if (Height <= 0 || double.IsPositiveInfinity(Height))
        {
            // Defer until we have a real allocated size — OnSizeChanged will retry.
            // real open is deferred to OnSizeChanged; this is a common source of "opened small".
            _pendingShow = true;
            return;
        }

        _pendingShow = false;
        SetupForShow();
        var target = GetTargetPosition();
        AnimateG9BottomSheet(target, onFinish: () => OpenMotionCompleted?.Invoke(this, EventArgs.Empty));
        IsOpen = true;
    }

    /// <summary>Animate the sheet off-screen.</summary>
    public void Close()
    {
        AnimateG9BottomSheet(Height, onFinish: () =>
        {
            _bottomSheet.IsVisible = false;
            RemoveOverlayFromView();
        });

        if (_isSheetOpen)
        {
            _isSheetOpen = false;
            IsOpen = false;
            State = G9SheetViewState.Hidden;
        }
    }

    /// <summary>
    ///     Receive a single touch event from the per-platform border handler. Public so the
    ///     handlers in matching <c>.{platform}.cs</c> files can call into the state machine.
    /// </summary>
    internal void OnHandleTouch(G9SheetViewTouchAction action, Point point)
    {
        if (!EnableSwiping || !_isSheetOpen)
        {
            return;
        }

        var touchY = AdjustTouchY(point);

        switch (action)
        {
            case G9SheetViewTouchAction.Pressed:
                _initialTouchY = touchY;
                _isPointerPressed = true;
                _dragTravelY = 0;
                _sheetTravelY = 0;
                return;

            case G9SheetViewTouchAction.Moved:
                HandleTouchMoved(touchY);
                return;

            case G9SheetViewTouchAction.Released:
            case G9SheetViewTouchAction.Cancelled:
            case G9SheetViewTouchAction.Exited:
                HandleTouchReleased();
                return;
        }
    }

    #endregion

    #region Layout / children

    private void RebuildChildren()
    {
        // Children order:
        //   1. user-supplied Content (renders behind the sheet)
        //   2. overlay grid (inserted before the sheet at Show())
        //   3. _bottomSheet (always last, drawn on top)
        Children.Clear();
        _isOverlayAdded = false;

        if (Content is not null)
        {
            Children.Add(Content);
        }

        Children.Add(_bottomSheet);
    }

    private void AddOverlayToView()
    {
        if (!IsModal || _isOverlayAdded)
        {
            return;
        }

        if (!Children.Contains(_overlayGrid))
        {
            // Insert just before the sheet so overlay sits beneath the body but above content.
            Children.Insert(Children.Count - 1, _overlayGrid);
        }

        _isOverlayAdded = true;
    }

    private void RemoveOverlayFromView()
    {
        if (!_isOverlayAdded)
        {
            return;
        }

        if (Children.Contains(_overlayGrid))
        {
            Children.Remove(_overlayGrid);
        }

        _isOverlayAdded = false;
    }

    private void SetG9BottomSheetContent(View? content)
    {
        if (_bottomSheetContent.Children.Count > 1)
        {
            _bottomSheetContent.Children.RemoveAt(1);
        }

        if (content is null)
        {
            return;
        }

        _contentBorder.Content = content;
        _bottomSheetContent.Children.Add(_contentBorder);
        Grid.SetRow(_contentBorder, 1);
    }

    private void UpdateGrabberRowHeight()
    {
        if (_bottomSheetContent.RowDefinitions.Count > 0)
        {
            _bottomSheetContent.RowDefinitions[0].Height = ShowGrabber
                ? new GridLength(GrabberAreaHeight)
                : new GridLength(0);
        }

        _grabber.IsVisible = ShowGrabber && GrabberAreaHeight > 0;
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        if (Height <= 0 || double.IsPositiveInfinity(Height))
        {
            return;
        }

        if (_pendingShow)
        {
            // Show() was called before we had a measured size — retry now.
            // for the host to allocate a height.
            Show();
            return;
        }

        if (_bottomSheet.IsVisible && _isSheetOpen)
        {
            // Re-snap the body to the right resting position for the new host size (rotation,
            // window resize on Windows / Mac Catalyst, virtual keyboard show/hide, etc.).
            ApplyBodyHeightForState(Height);
        }
    }

    private void UpdateBackground(Brush brush)
    {
        var resolved = brush ?? (Brush)BackgroundProperty.DefaultValue;
        _bottomSheet.Background = resolved;
        _bottomSheetContent.Background = resolved;
    }

    private void UpdateCornerRadius(CornerRadius cornerRadius)
    {
        if (cornerRadius.TopLeft < 0 || cornerRadius.TopRight < 0 ||
            cornerRadius.BottomLeft < 0 || cornerRadius.BottomRight < 0)
        {
            cornerRadius = (CornerRadius)CornerRadiusProperty.DefaultValue;
        }

        _bottomSheetStrokeShape.CornerRadius = cornerRadius;
        _bottomSheet.StrokeShape = _bottomSheetStrokeShape;
    }

    private void UpdateContentWidth()
    {
        if (ContentWidthMode == G9SheetViewContentWidthMode.Full)
        {
            _bottomSheet.ClearValue(WidthRequestProperty);
            _bottomSheet.HorizontalOptions = LayoutOptions.Fill;
            return;
        }

        if (G9BottomSheetContentWidth > 0)
        {
            _bottomSheet.WidthRequest = G9BottomSheetContentWidth;
            _bottomSheet.HorizontalOptions = LayoutOptions.Center;
        }
    }

    private void UpdateHalfExpandedRatio(double newValue)
    {
        var clamped = Math.Clamp(newValue, MinHalfExpandedRatio, MaxHalfExpandedRatio);
        if (Math.Abs(clamped - newValue) > 0.0001)
        {
            HalfExpandedRatio = clamped;
            return;
        }

        if (State == G9SheetViewState.HalfExpanded)
        {
            ApplyBodyHeightForState(Height);
            // Notify position subscribers so the modal-overlay fade and backdrop card recede
            // track the resize even when the helper drives the change without animating
            // through Show()/AnimateG9BottomSheet (e.g. fit-to-content tween).
            RaisePositionChanged();
        }
    }

    private void UpdateFullExpandedRatio(double newValue)
    {
        var clamped = Math.Clamp(newValue, MinFullExpandedRatio, MaxFullExpandedRatio);
        if (Math.Abs(clamped - newValue) > 0.0001)
        {
            FullExpandedRatio = clamped;
            return;
        }

        if (State == G9SheetViewState.FullExpanded)
        {
            ApplyBodyHeightForState(Height);
            RaisePositionChanged();
        }
    }

    private void UpdateCollapsedHeight(double newValue)
    {
        if (newValue <= 0)
        {
            return;
        }

        if (State == G9SheetViewState.Collapsed)
        {
            ApplyBodyHeightForState(Height);
            RaisePositionChanged();
        }
    }

    private void RecomputeAllowedState()
    {
        var (newState, isHalfExpanded) = AllowedState switch
        {
            G9SheetViewAllowedState.HalfExpanded => (
                _isSheetOpen ? G9SheetViewState.HalfExpanded : State, true),
            G9SheetViewAllowedState.FullExpanded => (
                _isSheetOpen ? G9SheetViewState.FullExpanded : State, false),
            G9SheetViewAllowedState.All => (State, _isHalfExpanded),
            _ => (!_isSheetOpen ? G9SheetViewState.Hidden : State, true)
        };

        if (!newState.Equals(State))
        {
            SetValue(StateProperty, newState);
        }

        _isHalfExpanded = isHalfExpanded;
    }

    private void OnIsModalChanged(bool isModal)
    {
        if (isModal && _isSheetOpen && State is G9SheetViewState.FullExpanded or G9SheetViewState.HalfExpanded)
        {
            AddOverlayToView();
            AnimateOverlay(150);
            return;
        }

        if (!isModal)
        {
            RemoveOverlayFromView();
        }
    }

    private void OnShowGrabberChanged(bool _) => UpdateGrabberRowHeight();

    private void UpdateOpenStateFromBinding(bool isOpen)
    {
        if (isOpen && !_isSheetOpen)
        {
            Show();
        }
        else if (!isOpen && _isSheetOpen)
        {
            Close();
        }
    }

    #endregion

    #region State machine

    private void OnStateChangedInternal(G9SheetViewState oldValue, G9SheetViewState newValue)
    {
        // Clamp the requested state to what AllowedState permits — same priority as the
        // original Syncfusion control so the helper-driven flows behave identically.
        if ((AllowedState == G9SheetViewAllowedState.HalfExpanded && newValue == G9SheetViewState.FullExpanded) ||
            (AllowedState == G9SheetViewAllowedState.FullExpanded && newValue == G9SheetViewState.HalfExpanded))
        {
            State = _isSheetOpen
                ? AllowedState == G9SheetViewAllowedState.HalfExpanded
                    ? G9SheetViewState.HalfExpanded
                    : G9SheetViewState.FullExpanded
                : G9SheetViewState.Hidden;
            return;
        }

        // Skip the Show()/Close() round-trip when the state didn't actually move. The MAUI
        // bindable system still invokes property-changed handlers on `value == oldValue`
        // assignments (e.g. the helper's fit-to-content tween writes `State = Collapsed` on
        // every animation tick to refresh the snap target) — without this short-circuit each
        // tick would abort and restart the sheet animation, defeating the smooth resize the
        // helper is trying to drive.
        if (oldValue == newValue)
        {
            return;
        }

        if (newValue == G9SheetViewState.Hidden)
        {
            _isHalfExpanded = AllowedState != G9SheetViewAllowedState.FullExpanded;
            if (_isSheetOpen)
            {
                _isSheetOpen = false;
                Close();
            }
        }
        else if (newValue == G9SheetViewState.Collapsed)
        {
            _isHalfExpanded = true;
            if (_isSheetOpen)
            {
                Show();
            }
        }
        else
        {
            _isHalfExpanded = newValue == G9SheetViewState.HalfExpanded;
            if (_isSheetOpen)
            {
                Show();
            }
        }

        RaiseStateChanged(oldValue, newValue);
    }

    private void RaiseStateChanged(G9SheetViewState oldValue, G9SheetViewState newValue)
    {
        if (oldValue.Equals(newValue))
        {
            return;
        }

        _stateChangedArgs.OldState = oldValue;
        _stateChangedArgs.NewState = newValue;
        _stateChangedArgs.AnimationDurationMs = AnimationDuration;
        StateChanged?.Invoke(this, _stateChangedArgs);
    }

    private void OnOverlayTapped(object? sender, EventArgs e)
    {
        if (!_isSheetOpen)
        {
            return;
        }

        if (CollapseOnOverlayTap)
        {
            State = G9SheetViewState.Collapsed;
            return;
        }

        // The helper renders its own modal overlay sibling (in OverlayHost) and listens to
        // BackRequested through that. This control-internal overlay path is rarely active
        // (helper sets IsModal = false on every sheet) but stays wired for completeness so a
        // standalone-control caller still gets a close request without subclassing.
        BackRequested?.Invoke(
            this,
            new G9SheetViewBackRequestedEventArgs(G9SheetViewBackRequestReason.OverlayTap));
    }

    #endregion

    #region Sizing

    private void SetupForShow()
    {
        if (_isSheetOpen)
        {
            return;
        }

        _bottomSheet.TranslationY = Height;
        _bottomSheet.IsVisible = true;

        if (IsModal)
        {
            AddOverlayToView();
        }
    }

    private double GetTargetPosition()
    {
        if ((State == G9SheetViewState.FullExpanded || !_isHalfExpanded)
            && State != G9SheetViewState.Collapsed
            && AllowedState != G9SheetViewAllowedState.HalfExpanded)
        {
            return GetFullExpandedPosition();
        }

        if (State == G9SheetViewState.Collapsed)
        {
            return GetCollapsedPosition();
        }

        return GetHalfExpandedPosition();
    }

    private double GetFullExpandedPosition()
    {
        if (State == G9SheetViewState.Hidden || State != G9SheetViewState.FullExpanded)
        {
            State = G9SheetViewState.FullExpanded;
        }

        var target = Math.Abs(Height * (1 - FullExpandedRatio));
        _bottomSheet.HeightRequest = Height * FullExpandedRatio;
        return target;
    }

    private double GetCollapsedPosition() => Height - CollapsedHeight;

    private double GetHalfExpandedPosition()
    {
        var target = Height * (1 - HalfExpandedRatio);
        if (!_isSheetOpen || _bottomSheet.TranslationY > target)
        {
            State = G9SheetViewState.HalfExpanded;
            _bottomSheet.HeightRequest = Height * HalfExpandedRatio;
        }

        return target;
    }

    private void ApplyBodyHeightForState(double height)
    {
        if (height <= 0 || double.IsPositiveInfinity(height))
        {
            return;
        }

        switch (State)
        {
            case G9SheetViewState.Hidden:
                _bottomSheet.HeightRequest = 0;
                break;
            case G9SheetViewState.Collapsed:
                _bottomSheet.TranslationY = height - CollapsedHeight;
                _bottomSheet.HeightRequest = CollapsedHeight;
                break;
            case G9SheetViewState.HalfExpanded:
                _bottomSheet.TranslationY = height * (1 - HalfExpandedRatio);
                _bottomSheet.HeightRequest = height * HalfExpandedRatio;
                break;
            case G9SheetViewState.FullExpanded:
                _bottomSheet.TranslationY = Math.Abs(height * (1 - FullExpandedRatio));
                _bottomSheet.HeightRequest = height * FullExpandedRatio;
                break;
        }
    }

    private void UpdateBodyHeightForOpenState()
    {
        if (!_isSheetOpen)
        {
            return;
        }

        switch (State)
        {
            case G9SheetViewState.HalfExpanded:
                _bottomSheet.HeightRequest = Height * HalfExpandedRatio;
                break;
            case G9SheetViewState.Collapsed:
                _bottomSheet.HeightRequest = CollapsedHeight;
                break;
            case G9SheetViewState.FullExpanded:
                _bottomSheet.HeightRequest = Height * FullExpandedRatio;
                break;
        }
    }

    #endregion

    #region Animation

    private void AnimateG9BottomSheet(double targetPosition, Action? onFinish = null)
    {
        if (_bottomSheet.AnimationIsRunning(SheetAnimationName))
        {
            _bottomSheet.AbortAnimation(SheetAnimationName);
        }

        var current = _bottomSheet.TranslationY;
        var duration = AnimationDurationProvider is not null
            ? Math.Max(0, AnimationDurationProvider(current, targetPosition, Height))
            : (int)Math.Max(0, AnimationDuration);

        // AnimationDurationProvider resolved for this specific motion.

        const double topPadding = 2;
        _isSheetOpen = true;

        var animation = new Animation(
            value =>
            {
                _bottomSheet.TranslationY = value;
                RaisePositionChanged();
            },
            current,
            targetPosition + topPadding);

        _bottomSheet.Animate(
            SheetAnimationName,
            animation,
            length: (uint)duration,
            easing: Easing.CubicOut,
            finished: (_, _) =>
            {
                UpdateBodyHeightForOpenState();
                RaisePositionChanged();
                onFinish?.Invoke();
            });

        AnimateOverlay(duration);
    }

    private void AnimateOverlay(int durationMs)
    {
        if (!IsModal)
        {
            return;
        }

        var shouldShow = State is not (G9SheetViewState.Collapsed or G9SheetViewState.Hidden);
        if (shouldShow)
        {
            AddOverlayToView();
        }

        if (_overlayGrid.AnimationIsRunning(OverlayAnimationName))
        {
            _overlayGrid.AbortAnimation(OverlayAnimationName);
        }

        var startValue = _overlayGrid.Opacity;
        var endValue = shouldShow ? DefaultOverlayOpacity : 0;

        var animation = new Animation(value =>
        {
            if (!double.IsNaN(value))
            {
                _overlayGrid.Opacity = value;
            }
        }, startValue, endValue);

        _overlayGrid.Animate(
            OverlayAnimationName,
            animation,
            length: (uint)durationMs,
            easing: Easing.CubicOut,
            finished: (_, _) =>
            {
                if (!shouldShow)
                {
                    RemoveOverlayFromView();
                }
            });
    }

    private void RaisePositionChanged()
    {
        if (PositionChanged is null)
        {
            return;
        }

        var fullHeight = Height > 0 ? Height : _bottomSheet.HeightRequest;
        var visibleHeight = Math.Max(0, fullHeight - _bottomSheet.TranslationY);
        PositionChanged.Invoke(this, new G9SheetViewPositionChangedEventArgs(visibleHeight, fullHeight));
    }

    #endregion

    #region Drag handling

    private double AdjustTouchY(Point point)
    {
        // All three platform handlers (Android ContentViewGroup, iOS UIPanGestureRecognizer,
        // Windows pointer events) forward touches in dp coordinates relative to the rendered
        // body view. We convert that to host-space Y by adding the body's current TranslationY
        // — that gives a stable absolute Y the state-snap code can compare against host height
        // ratios without "jumping" as the body moves under the finger. Important: when reading
        // TranslationY the body has already moved by previous frames in this gesture, so the
        // resulting Y stays anchored to the original finger position on the screen.
        return point.Y + (_bottomSheet?.TranslationY ?? 0);
    }

    private void HandleTouchMoved(double touchY)
    {
        if (!_isPointerPressed)
        {
            return;
        }

        var diffY = touchY - _initialTouchY;

        // Two accumulators, because the two release decisions ask different questions:
        //  * _dragTravelY  — how far the FINGER went. A gesture that is fully clamped (the sheet is
        //    already at a detent it may not pass) still travels, and that travel is what a
        //    drag-to-close has to be judged on. Measuring the close by SHEET movement is why a
        //    fit-to-content sheet could never be dismissed by dragging it down: it cannot move.
        //  * _sheetTravelY — how far the SHEET went, which is what the snap-to-next-detent rules
        //    have always been written against. Keeping it separate means a clamped drag no longer
        //    also reads as "a big swipe" to the snap logic.
        _dragTravelY += diffY;
        _initialTouchY = touchY;

        var desired = _bottomSheet.TranslationY + diffY;
        var target = ClampDragTranslation(desired);
        var applied = target - _bottomSheet.TranslationY;
        if (Math.Abs(applied) < 0.01)
        {
            return;
        }

        _sheetTravelY += applied;
        _bottomSheet.TranslationY = target;

        // Below the smallest detent the body SLIDES OFF instead of shrinking: the sheet is being
        // dismissed, not resized, and re-laying the content out on every frame while it leaves the
        // screen costs a layout pass per frame and reads as the content collapsing in on itself.
        // Above it the height tracks the position as before, so the body's bottom edge stays welded
        // to the screen bottom.
        var minRestingHeight = ResolveMinimumDetentHeight();
        var visibleHeight = Math.Max(0, Height - target);
        _bottomSheet.HeightRequest = Math.Max(visibleHeight, Math.Min(minRestingHeight, Height));

        RaisePositionChanged();

        // Overlay alpha follows the VISIBLE height (not the height request, which is pinned during
        // a dismiss drag), so the scrim keeps fading out as the sheet slides away.
        var shouldShow = IsModal && visibleHeight > CollapsedHeight;
        if (shouldShow)
        {
            AddOverlayToView();
            _overlayGrid.Opacity = ComputeDragOverlayOpacity(visibleHeight);
        }
        else
        {
            RemoveOverlayFromView();
        }
    }

    /// <summary>
    ///     Clamps a proposed body position to the range this sheet may be dragged through: never
    ///     above its LARGEST allowed detent, and never below its smallest one unless the sheet is
    ///     cancelable — in which case the extra travel is the dismiss gesture.
    /// </summary>
    /// <remarks>
    ///     ⛔ The upper bound is the largest ALLOWED detent, not the host height. The rule this
    ///     replaces derived its limit from the CURRENT state, so a sheet resting at Peek had no
    ///     upper limit beyond the window itself: a two-detent peek→medium sheet could be dragged to
    ///     the very top of the screen and was then snapped back down on release, which is the
    ///     "it over-drags, then leaves a band of empty background" defect.
    /// </remarks>
    private double ClampDragTranslation(double desired)
    {
        if (Height <= 0 || double.IsPositiveInfinity(Height))
        {
            return Math.Max(0, desired);
        }

        var maxHeight = ResolveMaximumDetentHeight();
        var minHeight = IsCancelable ? 0 : ResolveMinimumDetentHeight();

        var minTranslation = Math.Max(0, Height - maxHeight);
        var maxTranslation = Math.Max(minTranslation, Height - minHeight);

        return Math.Clamp(desired, minTranslation, maxTranslation);
    }

    /// <summary>
    ///     Height (dp) of the largest detent this sheet may rest at, given
    ///     <see cref="AllowedState" /> and the current ratios.
    /// </summary>
    private double ResolveMaximumDetentHeight()
    {
        if (Height <= 0 || double.IsPositiveInfinity(Height))
        {
            return 0;
        }

        var maxHeight = AllowedState switch
        {
            G9SheetViewAllowedState.HalfExpanded => Height * HalfExpandedRatio,
            G9SheetViewAllowedState.FullExpanded => Height * FullExpandedRatio,
            _ => Math.Max(Height * FullExpandedRatio, Height * HalfExpandedRatio)
        };

        // A collapsed height taller than the large detent is not a contradiction — it is exactly
        // how a fit-to-content sheet is expressed (CollapsedHeight = the measured content height) —
        // so the peek participates in the maximum instead of being overridden by it.
        return Math.Clamp(Math.Max(maxHeight, CollapsedHeight), 0, Height);
    }

    /// <summary>Height (dp) of the smallest detent this sheet may rest at.</summary>
    private double ResolveMinimumDetentHeight()
    {
        if (Height <= 0 || double.IsPositiveInfinity(Height))
        {
            return 0;
        }

        if (AllowCollapsedState || AllowedState == G9SheetViewAllowedState.All)
        {
            return Math.Clamp(CollapsedHeight, 0, Height);
        }

        return AllowedState switch
        {
            G9SheetViewAllowedState.HalfExpanded => Math.Clamp(Height * HalfExpandedRatio, 0, Height),
            G9SheetViewAllowedState.FullExpanded => Math.Clamp(Height * FullExpandedRatio, 0, Height),
            _ => Math.Clamp(CollapsedHeight, 0, Height)
        };
    }

    /// <summary>
    ///     <c>true</c> while the body rests at (or past) its largest allowed detent — i.e. there is
    ///     nothing left to expand into. Consumed by the per-platform border handlers to decide
    ///     whether an inner scroller may take a drag; see <see cref="ScrollingExpandsSheetProperty" />.
    /// </summary>
    internal bool IsAtMaximumDetent
    {
        get
        {
            if (Height <= 0 || double.IsPositiveInfinity(Height))
            {
                return true;
            }

            var maxHeight = ResolveMaximumDetentHeight();
            if (maxHeight <= 0)
            {
                return true;
            }

            return Height - _bottomSheet.TranslationY >= maxHeight - DetentTolerance;
        }
    }

    /// <summary>
    ///     Whether a scrollable child under the finger may consume the drag, or whether the drag
    ///     belongs to the sheet because there is still a larger detent to reach.
    /// </summary>
    internal bool ShouldInnerScrollerConsumeDrag()
    {
        return !ScrollingExpandsSheet || IsAtMaximumDetent;
    }

    private double ComputeDragOverlayOpacity(double currentHeight)
    {
        const double maxOpacity = DefaultOverlayOpacity;
        var range = (HalfExpandedRatio * Height) - CollapsedHeight;
        if (range <= 0)
        {
            return maxOpacity;
        }

        var progress = Math.Clamp((currentHeight - CollapsedHeight) / range, 0, 1);
        return progress * maxOpacity;
    }

    private void HandleTouchReleased()
    {
        _initialTouchY = 0;
        _isPointerPressed = false;
        UpdateAfterRelease();
    }

    private void UpdateAfterRelease()
    {
        const double SwipeThreshold = 100;
        const double DoubleSwipeThreshold = SwipeThreshold * 2;
        var swipeDistance = _sheetTravelY;

        // whether it snaps to another state or becomes a close request.

        // Fixed-state sheets (FitToContent or only one allowed detent) can't snap to a smaller
        // state — a downward drag past DragCloseThreshold is a close request instead. The
        // helper layer turns that into either a real close or an OnBackRequested round-trip.
        // Judged on FINGER travel: such a sheet is clamped at its detent and may not have moved at
        // all, which is precisely the case the gesture exists for.
        if (HasSingleAllowedState() &&
            IsCancelable &&
            _dragTravelY > DragCloseThreshold)
        {
            Show();
            BackRequested?.Invoke(
                this,
                new G9SheetViewBackRequestedEventArgs(G9SheetViewBackRequestReason.DragToClose));
            return;
        }

        switch (State)
        {
            case G9SheetViewState.FullExpanded:
                HandleReleaseFromFullExpanded(swipeDistance, SwipeThreshold, DoubleSwipeThreshold);
                break;
            case G9SheetViewState.HalfExpanded:
                HandleReleaseFromHalfExpanded(swipeDistance, SwipeThreshold);
                break;
            case G9SheetViewState.Collapsed:
                HandleReleaseFromCollapsed(swipeDistance, SwipeThreshold);
                break;
        }
    }

    private bool HasSingleAllowedState()
    {
        // The helper sets AllowedState to FullExpanded for fixed full-screen modal sheets. For
        // multi-state sheets it's left as All so the drag-snap path runs normally. AllowCollapsedState
        // is what separates a genuine two-detent peek→medium sheet (which must step DOWN a detent
        // rather than close) from a single-state sheet that happens to carry the same AllowedState.
        return AllowedState != G9SheetViewAllowedState.All && !AllowCollapsedState;
    }

    private void HandleReleaseFromFullExpanded(double swipeDistance, double swipeThreshold, double doubleSwipeThreshold)
    {
        if (swipeDistance > swipeThreshold && AllowedState != G9SheetViewAllowedState.FullExpanded)
        {
            UpdateStateBasedOnNearestPoint();
        }
        else if (swipeDistance > doubleSwipeThreshold)
        {
            State = G9SheetViewState.Collapsed;
        }
        else
        {
            Show();
        }
    }

    private void HandleReleaseFromHalfExpanded(double swipeDistance, double swipeThreshold)
    {
        if (-swipeDistance > swipeThreshold && AllowedState != G9SheetViewAllowedState.HalfExpanded)
        {
            State = G9SheetViewState.FullExpanded;
        }
        else if (swipeDistance > swipeThreshold && IsDetentAllowed(G9SheetViewState.Collapsed))
        {
            State = G9SheetViewState.Collapsed;
        }
        else
        {
            Show();
        }
    }

    private void HandleReleaseFromCollapsed(double swipeDistance, double swipeThreshold)
    {
        if (-swipeDistance > swipeThreshold)
        {
            if (AllowedState == G9SheetViewAllowedState.HalfExpanded)
            {
                State = G9SheetViewState.HalfExpanded;
            }
            else if (AllowedState == G9SheetViewAllowedState.FullExpanded)
            {
                State = G9SheetViewState.FullExpanded;
            }
            else
            {
                UpdateStateBasedOnNearestPoint();
            }

            return;
        }

        // The smallest detent is where a downward drag means "dismiss" rather than "step down".
        // Without this a sheet resting at its lowest detent would spring back from the gesture every
        // platform reads as a dismissal.
        if (IsCancelable && _dragTravelY > DragCloseThreshold)
        {
            Show();
            BackRequested?.Invoke(
                this,
                new G9SheetViewBackRequestedEventArgs(G9SheetViewBackRequestReason.DragToClose));
            return;
        }

        Show();
    }

    /// <summary>
    ///     Snaps to the allowed detent nearest to where the body was actually released.
    /// </summary>
    /// <remarks>
    ///     Ties go to the CURRENT state, which matters more than it looks: a fit-to-content sheet
    ///     expresses all three detents at the SAME position (collapsed height == both ratios == the
    ///     measured content height), so a nearest-wins scan would arbitrarily promote it to
    ///     <see cref="G9SheetViewState.FullExpanded" /> — no visual change, but every state-driven
    ///     consumer (overlay opacity, top safe-area padding, the helper's state handlers) would then
    ///     act on a state the sheet never really entered.
    /// </remarks>
    private void UpdateStateBasedOnNearestPoint()
    {
        if (Height <= 0 || double.IsPositiveInfinity(Height))
        {
            Show();
            return;
        }

        var released = _bottomSheet.TranslationY;
        var candidates = new[]
        {
            (State: G9SheetViewState.FullExpanded, Position: Height * (1 - FullExpandedRatio)),
            (State: G9SheetViewState.HalfExpanded, Position: Height * (1 - HalfExpandedRatio)),
            (State: G9SheetViewState.Collapsed, Position: Height - CollapsedHeight)
        };

        var bestState = State;
        var bestDistance = double.MaxValue;

        foreach (var candidate in candidates)
        {
            if (!IsDetentAllowed(candidate.State))
            {
                continue;
            }

            var distance = Math.Abs(candidate.Position - released);

            // Strictly closer wins; an equal distance leaves the incumbent in place, which is what
            // keeps coincident detents from reclassifying the sheet.
            if (distance < bestDistance - 0.5 ||
                (candidate.State == State && distance <= bestDistance + 0.5))
            {
                bestDistance = distance;
                bestState = candidate.State;
            }
        }

        if (bestState != State)
        {
            State = bestState;
            return;
        }

        Show();
    }

    private bool IsDetentAllowed(G9SheetViewState candidate)
    {
        return candidate switch
        {
            G9SheetViewState.FullExpanded => AllowedState is G9SheetViewAllowedState.All
                or G9SheetViewAllowedState.FullExpanded,
            G9SheetViewState.HalfExpanded => AllowedState is G9SheetViewAllowedState.All
                or G9SheetViewAllowedState.HalfExpanded,
            G9SheetViewState.Collapsed => AllowCollapsedState || AllowedState == G9SheetViewAllowedState.All,
            _ => false
        };
    }

    #endregion

    #region Layout overrides

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if (height <= 0 || double.IsPositiveInfinity(height))
        {
            return;
        }

        ApplyBodyHeightForState(height);
    }

    #endregion
}
