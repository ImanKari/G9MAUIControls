using Microsoft.Maui.Controls.Shapes;
using G9MAUIControls.Popup;
using G9MAUIControls.Toast;

namespace G9MAUIControls.Popup;

/// <summary>
///     Cross-platform popup control built entirely from public MAUI primitives. Replaces
///     <c>SfG9Popup</c> with a hand-rolled overlay + card that uses a <see cref="Grid" /> root
///     so it can sit inside the existing <c>OverlayHost</c> in <c>G9PageTemplate.xaml</c>
///     without introducing any vendor-specific layout type.
///     <para>
///         The control is hit-test-transparent when closed — exactly like
///         <c>G9SheetView</c> — so the page content underneath stays interactive while no
///         popup is showing. When opened, the overlay <see cref="BoxView" /> blocks input over
///         the entire host and routes background taps to <see cref="BackgroundTapped" />, which
///         the helper translates into a close request honoring <c>CloseOnBackgroundTap</c>.
///     </para>
/// </summary>
public sealed class G9PopupView : Grid
{
    /// <summary>Animation key for the card's translate / scale tween.</summary>
    private const string CardAnimationName = "G9PopupViewCardMotion";

    /// <summary>Animation key for the overlay alpha tween.</summary>
    private const string OverlayAnimationName = "G9PopupViewOverlayMotion";

    private readonly BoxView _overlay;
    private readonly Grid _cardHost;
    private readonly Grid _cardContainer;
    private readonly Border _cardFrame;
    private readonly RoundRectangle _cardCornerShape = new() { CornerRadius = 16 };

    private View? _content;
    private G9PopupViewOpenOptions _options = new();
    private bool _isOpen;
    private CancellationTokenSource? _autoCloseCts;
    private TaskCompletionSource<bool>? _closeAnimationCompleted;

    // Drag-by-handle state (see G9PopupViewOpenOptions.IsDraggable / DragHandle).
    private PanGestureRecognizer? _dragRecognizer;
    private View? _attachedDragHandle;
    private double _dragStartX;
    private double _dragStartY;

    /// <summary>
    ///     Cap the card height to a fraction of the host height. A scrollable body inside
    ///     an Auto-sized card would otherwise grow to its natural content height and push
    ///     the card off-screen on long input forms. This cap gives the body a known
    ///     <c>availableHeight</c> during measurement so wrapped <c>ScrollView</c>s actually
    ///     scroll.
    /// </summary>
    private const double CardHeightFraction = 0.85;
    private const double DefaultCenteredWidthFraction = 0.90;
    private const double MinimumCardWidth = 160;
    private const double MinimumCompactCardWidth = 120;

    public G9PopupView()
    {
        // The control fills its host (OverlayHost). It is input-transparent while closed so
        // page content underneath stays tappable. While the popup is open, the overlay child
        // captures every tap that misses the card; we keep CascadeInputTransparent = false so
        // the children can opt back in.
        InputTransparent = true;
        CascadeInputTransparent = false;
        IsVisible = false;

        _overlay = new BoxView
        {
            Color = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = false,
            Opacity = 0
        };
        var overlayTap = new TapGestureRecognizer();
        overlayTap.Tapped += OnOverlayTapped;
        _overlay.GestureRecognizers.Add(overlayTap);

        _cardContainer = new Grid
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
            CascadeInputTransparent = false,
            ScaleX = 0.92,
            ScaleY = 0.92,
            Opacity = 0
        };

        _cardFrame = new Border
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            StrokeThickness = 1,
            Stroke = new SolidColorBrush(Colors.Transparent),
            BackgroundColor = Colors.White,
            StrokeShape = _cardCornerShape,
            Padding = new Thickness(20, 16, 20, 12),
            // The card is the actually-interactive surface — it captures taps so they don't
            // bubble to the overlay (which would close the popup). The outer container owns
            // the open / close animation.
            InputTransparent = false,
            Opacity = 1
        };
        _cardContainer.Children.Add(_cardFrame);

        _cardHost = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            CascadeInputTransparent = false
        };
        _cardHost.Children.Add(_cardContainer);

        Children.Add(_overlay);
        Children.Add(_cardHost);

        // Re-cap the card's MaximumHeightRequest whenever the host resizes. This is what
        // makes a long ScrollView-wrapped input form scroll instead of pushing the card off-
        // screen — the cap gives the body a finite measure constraint at layout time.
        SizeChanged += OnHostSizeChanged;
    }

    private void OnHostSizeChanged(object? sender, EventArgs e)
    {
        ApplyCardSizeConstraints(_options);
        ApplyDefaultCenteredWidthIfNeeded(_options);
    }

    /// <summary>Raised when the user taps outside the card. Helper layer decides whether to close.</summary>
    public event EventHandler? BackgroundTapped;

    /// <summary>Raised after the close animation finishes and the control is hidden.</summary>
    public event EventHandler? Closed;

    /// <summary>True while the popup is open (or in the middle of an open / close animation).</summary>
    public bool IsOpen => _isOpen;

    /// <summary>
    ///     Whether a hardware/system back press should close this popup — mirrors the
    ///     <see cref="G9PopupViewOpenOptions.CloseOnBackButton" /> of the options it was last opened
    ///     with. The back coordinator reads this (while <see cref="IsOpen" /> is <c>true</c>) to
    ///     decide whether back dismisses the popup or is merely swallowed (a non-cancelable popup
    ///     that must stay up).
    /// </summary>
    public bool ClosesOnBackButton => _options.CloseOnBackButton;

    /// <summary>Currently-mounted content view, or <c>null</c> when no popup is showing.</summary>
    public View? Content => _content;

    /// <summary>Replace the content view that sits inside the card.</summary>
    public void SetContent(View? content)
    {
        if (_cardFrame.Content is View existing && !ReferenceEquals(existing, content))
        {
            _cardFrame.Content = null;
        }

        _content = content;
        _cardFrame.Content = content;
    }

    /// <summary>
    ///     Open the popup with the given visual options. If a popup is already open the visuals
    ///     are reapplied in place (no close-then-open round-trip) so the helper can swap content
    ///     while the overlay is still showing.
    /// </summary>
    public void Open(G9PopupViewOpenOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        ApplyOptions(options);
        StartAutoCloseTimer(options.AutoCloseDuration);

        // Bring the host visible before the animation starts. We were hidden + scale 0.92 so
        // the very first frame of the open animation is the "starting from a smaller hidden
        // state" frame — matches how a native iOS UIAlert snaps in.
        IsVisible = true;
        // Modal popups (Transparent / Blur overlay) capture all input over the host so taps
        // outside the card hit the overlay (which translates to a close request). Non-modal
        // popups (OverlayMode.None) keep the host input-transparent so taps in empty areas pass
        // straight through to the page content underneath; only the card itself receives input.
        InputTransparent = options.OverlayMode == G9PopupViewOverlayMode.None;
        _isOpen = true;

        // Capture (and detach) any in-flight close TCS BEFORE aborting animations. AbortAnimation
        // fires the finished-callback synchronously; that callback now bails when it sees
        // _isOpen == true (Open is authoritative for state). Because the bail path doesn't
        // complete the close TCS, we complete it here so any caller awaiting popup.CloseAsync()
        // releases cleanly. This is what makes the helper queue (info → warn → success) work
        // when each popup's button-callback awaits popup.CloseAsync() before the next request
        // dequeues.
        var pendingClose = _closeAnimationCompleted;
        _closeAnimationCompleted = null;

        AbortRunningAnimations();
        AnimateOpen(options);

        pendingClose?.TrySetResult(true);
    }

    /// <summary>
    ///     Close the popup with the configured animation. Returns a task that completes once
    ///     the close animation finishes and the control is fully hidden.
    /// </summary>
    public Task CloseAsync()
    {
        if (!_isOpen)
        {
            return Task.CompletedTask;
        }

        _isOpen = false;
        CancelAutoCloseTimer();

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _closeAnimationCompleted = tcs;

        AbortRunningAnimations();
        AnimateClose(_options, () =>
        {
            // If a re-open superseded this close (e.g. G9PopupHelper queue advanced before
            // this animation completed), Open() flipped _isOpen back to true and is now
            // authoritative for the visual state. Bail without touching IsVisible /
            // InputTransparent / Closed event — the new popup is already showing. Open()
            // is responsible for completing the close TCS in that path.
            if (_isOpen)
            {
                return;
            }

            // Fully off-screen and non-interactive so taps fall through immediately. We
            // detach the content as part of the cleanup so the next Show() can mount a fresh
            // view without parent-already-set conflicts.
            InputTransparent = true;
            IsVisible = false;
            SetContent(null);
            Closed?.Invoke(this, EventArgs.Empty);
            tcs.TrySetResult(true);
        });

        return tcs.Task;
    }

    private void ApplyOptions(G9PopupViewOpenOptions options)
    {
        // Card frame visuals.
        _cardCornerShape.CornerRadius = new CornerRadius(options.CornerRadius);
        _cardFrame.StrokeShape = _cardCornerShape;
        _cardFrame.BackgroundColor = options.CardBackground ?? Colors.White;
        _cardFrame.Stroke = new SolidColorBrush(options.BorderColor ?? Colors.Transparent);
        _cardFrame.Padding = options.Padding;

        // Sizing — three independent flags keep parity with the SfG9Popup AutoSizeMode contract.
        switch (options.AutoSizeMode)
        {
            case G9PopupViewAutoSizeMode.Both:
                _cardFrame.ClearValue(WidthRequestProperty);
                _cardFrame.ClearValue(HeightRequestProperty);
                break;
            case G9PopupViewAutoSizeMode.Height:
                if (options.Width is { } w1)
                {
                    _cardFrame.WidthRequest = w1;
                }
                else if (ShouldUseDefaultCenteredWidth(options) && Width > 0)
                {
                    _cardFrame.WidthRequest = ResolveDefaultCenteredWidth();
                }
                else
                {
                    _cardFrame.ClearValue(WidthRequestProperty);
                }
                _cardFrame.ClearValue(HeightRequestProperty);
                break;
            case G9PopupViewAutoSizeMode.Width:
                if (options.Height is { } h1)
                {
                    _cardFrame.HeightRequest = h1;
                }
                else
                {
                    _cardFrame.ClearValue(HeightRequestProperty);
                }
                _cardFrame.ClearValue(WidthRequestProperty);
                break;
            case G9PopupViewAutoSizeMode.None:
                if (options.Width is { } w2)
                {
                    _cardFrame.WidthRequest = w2;
                }
                if (options.Height is { } h2)
                {
                    _cardFrame.HeightRequest = h2;
                }
                break;
        }

        ApplyCardSizeConstraints(options);

        // Card alignment — relative anchor / absolute coordinates support if requested. Most
        // callers leave both off and the card is centered by the cardHost LayoutOptions.Center.
        ApplyCardAlignment(options);

        // Overlay color setup. Real blur is delegated to the platform — see remarks in
        // G9PopupViewEnums.cs. We always paint a solid color first so the overlay is visible even
        // on platforms without a blur primitive; blur is layered on top via a property the
        // platforms can pick up if they support it. OverlayMode.None hides the overlay entirely
        // (non-modal floating popup — see G9PopupViewOpenOptions.IsDraggable for the typical use).
        var overlayColor = ResolveOverlayColor(options);
        _overlay.Color = overlayColor;
        _overlay.IsVisible = options.OverlayMode != G9PopupViewOverlayMode.None;
        _overlay.InputTransparent = options.OverlayMode == G9PopupViewOverlayMode.None;

        // Reset any drag offset accumulated by a prior open so a re-open always centers the
        // card. The open animation will re-set TranslationY (e.g. SlideUp starts at +40), so
        // resetting both here doesn't conflict with the animation start state.
        _cardContainer.TranslationX = 0;
        _cardContainer.TranslationY = 0;

        // Drag-by-handle (see G9PopupViewOpenOptions.IsDraggable / DragHandle).
        ApplyDragBehavior(options);
    }

    private void ApplyCardSizeConstraints(G9PopupViewOpenOptions options)
    {
        if (Height > 0)
        {
            if (options.Height is null)
            {
                _cardFrame.MaximumHeightRequest = Math.Max(120, Height * CardHeightFraction);
            }
            else
            {
                _cardFrame.ClearValue(MaximumHeightRequestProperty);
            }
        }

        if (Width > 0)
        {
            if (options.Width is null)
            {
                // Long content should never go edge-to-edge. Default centered popups also use
                // this exact width so their left and right screen gaps stay equal on mobile.
                _cardFrame.MaximumWidthRequest = ResolveDefaultCenteredWidth();
            }
            else
            {
                _cardFrame.ClearValue(MaximumWidthRequestProperty);
            }
        }
    }

    private void ApplyDefaultCenteredWidthIfNeeded(G9PopupViewOpenOptions options)
    {
        if (ShouldUseDefaultCenteredWidth(options) && Width > 0)
        {
            _cardFrame.WidthRequest = ResolveDefaultCenteredWidth();
        }
    }

    private bool ShouldUseDefaultCenteredWidth(G9PopupViewOpenOptions options) =>
        options.AutoSizeMode == G9PopupViewAutoSizeMode.Height
        && options.Width is null
        && options.AbsoluteX is null
        && options.AbsoluteY is null
        && options.RelativeView is null;

    private double ResolveDefaultCenteredWidth()
    {
        var edgeBoundedWidth = Math.Max(MinimumCompactCardWidth, Width);
        var preferredWidth = Math.Max(MinimumCardWidth, Width * DefaultCenteredWidthFraction);
        return Math.Min(Width, Math.Min(edgeBoundedWidth, preferredWidth));
    }

    private void ApplyCardAlignment(G9PopupViewOpenOptions options)
    {
        if (options.AbsoluteX is { } absX && options.AbsoluteY is { } absY)
        {
            _cardContainer.HorizontalOptions = LayoutOptions.Start;
            _cardContainer.VerticalOptions = LayoutOptions.Start;
            _cardContainer.Margin = new Thickness(absX, absY, 0, 0);
            return;
        }

        if (options.RelativeView is not null && options.RelativePosition is { } relPos)
        {
            ApplyRelativeAlignment(options, relPos);
            return;
        }

        _cardContainer.HorizontalOptions = LayoutOptions.Center;
        _cardContainer.VerticalOptions = LayoutOptions.Center;
        _cardContainer.Margin = Thickness.Zero;
    }

    private void ApplyRelativeAlignment(G9PopupViewOpenOptions options, G9PopupViewRelativePosition relPos)
    {
        // The legacy SfG9Popup.ShowRelativeToView was rarely used in this codebase — all five
        // call sites pass null. We support it minimally for parity: convert the anchor's
        // center-on-screen to host coordinates, then apply the requested side.
        var anchor = options.RelativeView!;
        if (anchor.Width <= 0 || anchor.Height <= 0)
        {
            // Anchor has not been laid out yet — fall back to centering and let the next
            // SizeChanged tick re-apply the alignment.
            _cardContainer.HorizontalOptions = LayoutOptions.Center;
            _cardContainer.VerticalOptions = LayoutOptions.Center;
            _cardContainer.Margin = Thickness.Zero;
            return;
        }

        var anchorPosition = TryGetAnchorOffsetWithinHost(anchor);
        var anchorRect = new Rect(
            anchorPosition.X,
            anchorPosition.Y,
            anchor.Width,
            anchor.Height);

        var (x, y, hOpt, vOpt) = relPos switch
        {
            G9PopupViewRelativePosition.AlignTopLeft => (anchorRect.X, anchorRect.Y, LayoutOptions.Start, LayoutOptions.Start),
            G9PopupViewRelativePosition.AlignTopRight => (anchorRect.Right, anchorRect.Y, LayoutOptions.End, LayoutOptions.Start),
            G9PopupViewRelativePosition.AlignBottomLeft => (anchorRect.X, anchorRect.Bottom, LayoutOptions.Start, LayoutOptions.End),
            G9PopupViewRelativePosition.AlignBottomRight => (anchorRect.Right, anchorRect.Bottom, LayoutOptions.End, LayoutOptions.End),
            G9PopupViewRelativePosition.AlignTop => (anchorRect.Center.X, anchorRect.Y, LayoutOptions.Center, LayoutOptions.Start),
            G9PopupViewRelativePosition.AlignBottom => (anchorRect.Center.X, anchorRect.Bottom, LayoutOptions.Center, LayoutOptions.End),
            G9PopupViewRelativePosition.AlignLeft => (anchorRect.X, anchorRect.Center.Y, LayoutOptions.Start, LayoutOptions.Center),
            G9PopupViewRelativePosition.AlignRight => (anchorRect.Right, anchorRect.Center.Y, LayoutOptions.End, LayoutOptions.Center),
            G9PopupViewRelativePosition.AlignToTopOf => (anchorRect.Center.X, anchorRect.Y, LayoutOptions.Center, LayoutOptions.End),
            G9PopupViewRelativePosition.AlignToBottomOf => (anchorRect.Center.X, anchorRect.Bottom, LayoutOptions.Center, LayoutOptions.Start),
            G9PopupViewRelativePosition.AlignToLeftOf => (anchorRect.X, anchorRect.Center.Y, LayoutOptions.End, LayoutOptions.Center),
            G9PopupViewRelativePosition.AlignToRightOf => (anchorRect.Right, anchorRect.Center.Y, LayoutOptions.Start, LayoutOptions.Center),
            _ => (anchorRect.Center.X, anchorRect.Center.Y, LayoutOptions.Center, LayoutOptions.Center)
        };

        _cardContainer.HorizontalOptions = hOpt;
        _cardContainer.VerticalOptions = vOpt;
        _cardContainer.Margin = new Thickness(
            x + options.RelativeAbsoluteX,
            y + options.RelativeAbsoluteY,
            0,
            0);
    }

    private Point TryGetAnchorOffsetWithinHost(VisualElement anchor)
    {
        // Walk up the parent chain accumulating X/Y until we hit ourselves. The result is the
        // anchor's top-left in our own coordinate space; if the anchor isn't a descendant of
        // our host (rare — usually relative anchors live in the same page), fall back to (0,0)
        // and the next layout pass corrects it through SizeChanged.
        double x = 0;
        double y = 0;
        VisualElement? cursor = anchor;
        while (cursor is not null && !ReferenceEquals(cursor, this))
        {
            x += cursor.X;
            y += cursor.Y;
            cursor = cursor.Parent as VisualElement;
        }

        return new Point(x, y);
    }

    private Color ResolveOverlayColor(G9PopupViewOpenOptions options)
    {
        // Non-modal popups don't paint a scrim — the overlay BoxView is also hidden + made
        // input-transparent in ApplyOptions so taps fall through to the page content.
        if (options.OverlayMode == G9PopupViewOverlayMode.None)
        {
            return Colors.Transparent;
        }

        var baseColor = options.OverlayColor ?? Colors.Black;
        var opacity = (float)Math.Clamp(options.OverlayOpacity, 0, 1);

        // Blur mode: we don't have a public MAUI blur primitive, so we fake it by darkening
        // the overlay slightly so the user perceives more visual separation. The actual GPU
        // blur is left to a future per-platform handler if the design system requires it.
        if (options.OverlayMode == G9PopupViewOverlayMode.Blur)
        {
            opacity = options.BlurIntensity switch
            {
                G9PopupViewBlurIntensity.None => opacity,
                G9PopupViewBlurIntensity.Light => Math.Min(1f, opacity + 0.05f),
                G9PopupViewBlurIntensity.ExtraLight => Math.Min(1f, opacity + 0.10f),
                G9PopupViewBlurIntensity.Dark => Math.Min(1f, opacity + 0.15f),
                G9PopupViewBlurIntensity.ExtraDark => Math.Min(1f, opacity + 0.20f),
                _ => opacity
            };
        }

        return baseColor.WithAlpha(opacity);
    }

    private void OnOverlayTapped(object? sender, EventArgs e)
    {
        if (!_isOpen)
        {
            return;
        }

        BackgroundTapped?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyDragBehavior(G9PopupViewOpenOptions options)
    {
        // Resolve which view should capture the pan gesture. When IsDraggable is on but no
        // handle is supplied we fall back to the entire card frame — note that this prevents
        // taps inside the body from working normally, so callers SHOULD pass a header bar.
        var newHandle = options.IsDraggable ? (options.DragHandle ?? (View)_cardFrame) : null;

        if (ReferenceEquals(newHandle, _attachedDragHandle))
        {
            return;
        }

        // Detach the recognizer from the previous handle (if any) so we never accumulate
        // duplicate gestures across re-opens with a different DragHandle.
        if (_attachedDragHandle is not null && _dragRecognizer is not null)
        {
            _attachedDragHandle.GestureRecognizers.Remove(_dragRecognizer);
        }

        _attachedDragHandle = newHandle;

        if (newHandle is null)
        {
            _dragRecognizer = null;
            return;
        }

        _dragRecognizer = new PanGestureRecognizer();
        _dragRecognizer.PanUpdated += OnCardDragUpdated;
        newHandle.GestureRecognizers.Add(_dragRecognizer);
    }

    private void OnCardDragUpdated(object? sender, PanUpdatedEventArgs e)
    {
        // Cumulative drag: capture the card's current translation on Started, then on every
        // Running tick set translation = start + total. This is more stable than applying
        // delta-per-tick because PanUpdated reports TotalX/Y as the total since gesture start.
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _dragStartX = _cardContainer.TranslationX;
                _dragStartY = _cardContainer.TranslationY;
                break;
            case GestureStatus.Running:
                _cardContainer.TranslationX = _dragStartX + e.TotalX;
                _cardContainer.TranslationY = _dragStartY + e.TotalY;
                break;
        }
    }

    private void StartAutoCloseTimer(int autoCloseDurationMs)
    {
        CancelAutoCloseTimer();
        if (autoCloseDurationMs <= 0)
        {
            return;
        }

        _autoCloseCts = new CancellationTokenSource();
        var token = _autoCloseCts.Token;
        _ = AutoCloseAfterAsync(autoCloseDurationMs, token);
    }

    private async Task AutoCloseAfterAsync(int autoCloseDurationMs, CancellationToken token)
    {
        try
        {
            await Task.Delay(autoCloseDurationMs, token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        if (!token.IsCancellationRequested && _isOpen)
        {
            await CloseAsync().ConfigureAwait(true);
        }
    }

    private void CancelAutoCloseTimer()
    {
        _autoCloseCts?.Cancel();
        _autoCloseCts?.Dispose();
        _autoCloseCts = null;
    }

    private void AbortRunningAnimations()
    {
        _cardContainer.AbortAnimation(CardAnimationName);
        _overlay.AbortAnimation(OverlayAnimationName);
    }

    private Easing ResolveEasing(G9PopupViewAnimationEasing easing)
    {
        return easing switch
        {
            G9PopupViewAnimationEasing.Linear => Easing.Linear,
            G9PopupViewAnimationEasing.SinIn => Easing.SinIn,
            G9PopupViewAnimationEasing.SinOut => Easing.SinOut,
            G9PopupViewAnimationEasing.SinInOut => Easing.SinInOut,
            G9PopupViewAnimationEasing.CubicOut => Easing.CubicOut,
            G9PopupViewAnimationEasing.BounceOut => Easing.BounceOut,
            _ => Easing.SinOut
        };
    }

    private void AnimateOpen(G9PopupViewOpenOptions options)
    {
        var easing = ResolveEasing(options.AnimationEasing);
        var duration = options.AnimationDuration;

        // Overlay fades in from 0 → 1 alpha. We always run this leg even when the card uses a
        // "None" animation so the scrim never pops in instantly.
        _overlay.Animate(
            OverlayAnimationName,
            v => _overlay.Opacity = v,
            _overlay.Opacity,
            1d,
            16,
            duration,
            easing);

        // Card animation depends on the requested kind. Each kind sets the START state (the
        // popup was just made visible — the user hasn't seen any frame yet) then animates to
        // the resting visible state.
        switch (options.Animation)
        {
            case G9PopupAnimationType.None:
                _cardContainer.Opacity = 1;
                _cardContainer.Scale = 1;
                _cardContainer.TranslationY = 0;
                break;
            case G9PopupAnimationType.FadeIn:
                _cardContainer.Scale = 1;
                _cardContainer.TranslationY = 0;
                _cardContainer.Animate(CardAnimationName, v => _cardContainer.Opacity = v, _cardContainer.Opacity, 1d, 16, duration, easing);
                break;
            case G9PopupAnimationType.SlideUp:
                _cardContainer.Scale = 1;
                _cardContainer.TranslationY = 40;
                _cardContainer.Opacity = 0;
                AnimateSimultaneous(
                    o => _cardContainer.Opacity = o,
                    t => _cardContainer.TranslationY = t,
                    fromOpacity: 0d,
                    toOpacity: 1d,
                    fromValue: 40d,
                    toValue: 0d,
                    duration,
                    easing);
                break;
            case G9PopupAnimationType.DropIn:
                _cardContainer.Scale = 1;
                _cardContainer.TranslationY = -40;
                _cardContainer.Opacity = 0;
                AnimateSimultaneous(
                    o => _cardContainer.Opacity = o,
                    t => _cardContainer.TranslationY = t,
                    fromOpacity: 0d,
                    toOpacity: 1d,
                    fromValue: -40d,
                    toValue: 0d,
                    duration,
                    easing);
                break;
            case G9PopupAnimationType.Bounce:
                _cardContainer.TranslationY = 0;
                _cardContainer.Opacity = 0;
                _cardContainer.Scale = 0.6;
                AnimateSimultaneous(
                    o => _cardContainer.Opacity = o,
                    s => _cardContainer.Scale = s,
                    fromOpacity: 0d,
                    toOpacity: 1d,
                    fromValue: 0.6d,
                    toValue: 1d,
                    duration,
                    Easing.BounceOut);
                break;
            case G9PopupAnimationType.ZoomIn:
            default:
                _cardContainer.TranslationY = 0;
                _cardContainer.Opacity = 0;
                _cardContainer.Scale = 0.92;
                AnimateSimultaneous(
                    o => _cardContainer.Opacity = o,
                    s => _cardContainer.Scale = s,
                    fromOpacity: 0d,
                    toOpacity: 1d,
                    fromValue: 0.92d,
                    toValue: 1d,
                    duration,
                    easing);
                break;
        }
    }

    private void AnimateClose(G9PopupViewOpenOptions options, Action onCompleted)
    {
        var easing = ResolveEasing(options.AnimationEasing);
        var duration = options.AnimationDuration;

        // Overlay fades back out independently. We don't gate the onCompleted callback on it
        // because the card animation is what defines "the popup is hidden" semantically.
        _overlay.Animate(
            OverlayAnimationName,
            v => _overlay.Opacity = v,
            _overlay.Opacity,
            0d,
            16,
            duration,
            easing);

        switch (options.Animation)
        {
            case G9PopupAnimationType.None:
                _cardContainer.Opacity = 0;
                onCompleted();
                return;
            case G9PopupAnimationType.SlideUp:
                AnimateSimultaneous(
                    o => _cardContainer.Opacity = o,
                    t => _cardContainer.TranslationY = t,
                    fromOpacity: _cardContainer.Opacity,
                    toOpacity: 0d,
                    fromValue: _cardContainer.TranslationY,
                    toValue: 40d,
                    duration,
                    easing,
                    onCompleted);
                return;
            case G9PopupAnimationType.DropIn:
                AnimateSimultaneous(
                    o => _cardContainer.Opacity = o,
                    t => _cardContainer.TranslationY = t,
                    fromOpacity: _cardContainer.Opacity,
                    toOpacity: 0d,
                    fromValue: _cardContainer.TranslationY,
                    toValue: -40d,
                    duration,
                    easing,
                    onCompleted);
                return;
            case G9PopupAnimationType.FadeIn:
                _cardContainer.Animate(
                    CardAnimationName,
                    v => _cardContainer.Opacity = v,
                    _cardContainer.Opacity,
                    0d,
                    16,
                    duration,
                    easing,
                    (_, _) => onCompleted());
                return;
            case G9PopupAnimationType.Bounce:
            case G9PopupAnimationType.ZoomIn:
            default:
                AnimateSimultaneous(
                    o => _cardContainer.Opacity = o,
                    s => _cardContainer.Scale = s,
                    fromOpacity: _cardContainer.Opacity,
                    toOpacity: 0d,
                    fromValue: _cardContainer.Scale,
                    toValue: 0.92d,
                    duration,
                    easing,
                    onCompleted);
                return;
        }
    }

    private void AnimateSimultaneous(
        Action<double> opacityCallback,
        Action<double> valueCallback,
        double fromOpacity,
        double toOpacity,
        double fromValue,
        double toValue,
        uint duration,
        Easing easing,
        Action? onCompleted = null)
    {
        // MAUI's Animation supports child animations that all run on the same animator. Driving
        // both opacity and the variable (translation or scale) from one Animate call keeps them
        // perfectly in lockstep — no fighting if the user closes mid-open.
        var compound = new Animation();
        compound.Add(0, 1, new Animation(opacityCallback, fromOpacity, toOpacity, easing));
        compound.Add(0, 1, new Animation(valueCallback, fromValue, toValue, easing));

        _cardContainer.Animate(
            CardAnimationName,
            compound,
            16,
            duration,
            easing,
            (_, _) => onCompleted?.Invoke());
    }
}
