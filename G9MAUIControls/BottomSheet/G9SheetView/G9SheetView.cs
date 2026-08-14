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
    private double _startTouchY;
    private double _endTouchY;

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
                _startTouchY = touchY;
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
        var newTranslation = Math.Max(0, _bottomSheet.TranslationY + diffY);

        if (ShouldRestrictMovement(newTranslation, diffY))
        {
            return;
        }

        _bottomSheet.TranslationY = newTranslation;
        _initialTouchY = touchY;
        _bottomSheet.HeightRequest = Math.Max(0, Height - newTranslation);
        RaisePositionChanged();

        var shouldShow = IsModal && _bottomSheet.HeightRequest > CollapsedHeight;
        if (shouldShow)
        {
            AddOverlayToView();
            _overlayGrid.Opacity = ComputeDragOverlayOpacity(_bottomSheet.HeightRequest);
        }
        else
        {
            RemoveOverlayFromView();
        }
    }

    private bool ShouldRestrictMovement(double newTranslation, double diffY)
    {
        _ = diffY;

        var endPosition = State switch
        {
            G9SheetViewState.FullExpanded => Height * FullExpandedRatio,
            G9SheetViewState.HalfExpanded => Height * HalfExpandedRatio,
            G9SheetViewState.Collapsed => CollapsedHeight,
            _ => 0d
        };

        var updatedHeight = Height - newTranslation;

        var halfRestricted = State == G9SheetViewState.HalfExpanded
                             && AllowedState == G9SheetViewAllowedState.HalfExpanded
                             && updatedHeight > endPosition;

        var collapsedDown = State == G9SheetViewState.Collapsed && updatedHeight < endPosition;

        var fullRestricted = State == G9SheetViewState.FullExpanded && updatedHeight > endPosition;

        var beyondHost = newTranslation > Height - CollapsedHeight ||
                         newTranslation < Height * (1 - FullExpandedRatio);

        return halfRestricted || collapsedDown || fullRestricted || beyondHost;
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
        _endTouchY = _initialTouchY;
        _initialTouchY = 0;
        _isPointerPressed = false;
        UpdateAfterRelease();
    }

    private void UpdateAfterRelease()
    {
        const double SwipeThreshold = 100;
        const double DoubleSwipeThreshold = SwipeThreshold * 2;
        var swipeDistance = _endTouchY - _startTouchY;

        // whether it snaps to another state or becomes a close request.

        // Fixed-state sheets (FitToContent or only one allowed state) can't snap to a smaller
        // state — a downward drag past DragCloseThreshold is a close request instead. The
        // helper layer turns that into either a real close or an OnBackRequested round-trip.
        if (HasSingleAllowedState() &&
            IsCancelable &&
            swipeDistance > DragCloseThreshold)
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
        // multi-state sheets it's left as All so the drag-snap path runs normally.
        return AllowedState != G9SheetViewAllowedState.All;
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
        else if (swipeDistance > swipeThreshold)
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
        }
        else
        {
            Show();
        }
    }

    private void UpdateStateBasedOnNearestPoint()
    {
        var fullExpandedY = Height * (1 - FullExpandedRatio);
        var halfExpandedY = Height * (1 - HalfExpandedRatio);
        var collapsedY = Height - CollapsedHeight;
        var points = new[] { fullExpandedY, halfExpandedY, collapsedY };
        var nearest = points.OrderBy(p => Math.Abs(p - _endTouchY)).First();

        if (Math.Abs(nearest - fullExpandedY) < 0.001)
        {
            if (State != G9SheetViewState.FullExpanded)
            {
                State = G9SheetViewState.FullExpanded;
            }
            else
            {
                Show();
            }
        }
        else if (Math.Abs(nearest - halfExpandedY) < 0.001)
        {
            State = G9SheetViewState.HalfExpanded;
        }
        else
        {
            if (State != G9SheetViewState.Collapsed)
            {
                State = G9SheetViewState.Collapsed;
            }
            else
            {
                Show();
            }
        }
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
