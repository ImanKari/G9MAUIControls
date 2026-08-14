#if IOS || MACCATALYST
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Handlers;
using UIKit;

namespace G9MAUIControls.BottomSheet;

/// <summary>
///     iOS / Mac Catalyst handler that attaches a custom <see cref="UIPanGestureRecognizer" />
///     to the rendered platform view of <see cref="G9SheetViewBorder" />. The recognizer
///     is configured to recognise simultaneously with native UIScrollView / UICollectionView
///     gestures, and only forwards drag events to the bottom sheet once the inner scroll view
///     reaches an edge — preserving the iOS-native scroll handoff feel.
/// </summary>
internal sealed class G9SheetViewBorderHandler : BorderHandler
{
    private G9SheetViewPanGestureRecognizer? _pan;

    protected override void ConnectHandler(Microsoft.Maui.Platform.ContentView platformView)
    {
        base.ConnectHandler(platformView);

        if (VirtualView is not G9SheetViewBorder border)
        {
            return;
        }

        _pan = new G9SheetViewPanGestureRecognizer(border, platformView)
        {
            CancelsTouchesInView = false,
            DelaysTouchesBegan = false,
            DelaysTouchesEnded = false,
            ShouldRecognizeSimultaneously = (_, _) => true
        };
        platformView.AddGestureRecognizer(_pan);
    }

    protected override void DisconnectHandler(Microsoft.Maui.Platform.ContentView platformView)
    {
        if (_pan is not null)
        {
            platformView.RemoveGestureRecognizer(_pan);
            _pan.Dispose();
            _pan = null;
        }

        base.DisconnectHandler(platformView);
    }
}

/// <summary>
///     Custom <see cref="UIPanGestureRecognizer" /> that watches the touch path for
///     UIScrollView / UICollectionView ancestors. While the inner scroller can still scroll in
///     the drag direction the recogniser stays passive (returning Failed for the recogniser
///     state machine so the inner gesture wins). Once the inner scroller reaches its edge — or
///     when the touch starts outside any scroller — the recogniser forwards Pressed → Moved →
///     Released into the <see cref="G9SheetViewBorder" />, which routes them to the owning
///     <see cref="G9SheetView" />'s touch state machine.
/// </summary>
internal sealed class G9SheetViewPanGestureRecognizer : UIPanGestureRecognizer
{
    private readonly WeakReference<G9SheetViewBorder> _borderRef;
    private readonly WeakReference<UIView> _platformViewRef;

    private UIScrollView? _activeScrollView;
    private bool _insideScrollable;
    private bool _handoff;
    private CGPoint _lastPoint;
    private static readonly nfloat EdgeEpsilon = 1f;

    public G9SheetViewPanGestureRecognizer(G9SheetViewBorder border, UIView platformView)
    {
        _borderRef = new WeakReference<G9SheetViewBorder>(border);
        _platformViewRef = new WeakReference<UIView>(platformView);

        AddTarget(() => OnPan());
    }

    public override void TouchesBegan(NSSet touches, UIEvent evt)
    {
        if (touches.AnyObject is not UITouch touch || !_platformViewRef.TryGetTarget(out var platformView))
        {
            base.TouchesBegan(touches, evt);
            return;
        }

        var location = touch.LocationInView(platformView);
        var hit = platformView.HitTest(location, evt);
        _activeScrollView = ResolveScrollableAncestor(hit);
        _insideScrollable = _activeScrollView is not null;
        _handoff = false;
        _lastPoint = location;

        // Don't synthesise a Pressed yet — if we're inside a scroller we want it to keep the
        // gesture until it hits an edge. For non-scrollable hits we still wait for the first
        // pan tick so a simple tap inside the body isn't misread as a drag-to-close.
        base.TouchesBegan(touches, evt);
    }

    public override void TouchesCancelled(NSSet touches, UIEvent evt)
    {
        ResetState();
        base.TouchesCancelled(touches, evt);
    }

    public override void TouchesEnded(NSSet touches, UIEvent evt)
    {
        ResetState();
        base.TouchesEnded(touches, evt);
    }

    private void OnPan()
    {
        if (!_borderRef.TryGetTarget(out var border) || !_platformViewRef.TryGetTarget(out var platformView))
        {
            return;
        }

        var location = LocationInView(platformView);
        var dpPoint = new Point(location.X, location.Y);

        switch (State)
        {
            case UIGestureRecognizerState.Began:
                if (!_insideScrollable)
                {
                    border.ForwardTouch(G9SheetViewTouchAction.Pressed, dpPoint);
                    _handoff = true;
                }

                _lastPoint = location;
                break;

            case UIGestureRecognizerState.Changed:
                var dy = location.Y - _lastPoint.Y;

                if (_insideScrollable && _activeScrollView is { } scroll)
                {
                    if (CanInnerScroll(scroll, dy))
                    {
                        // Inner scroll can still consume — stay passive.
                        _lastPoint = location;
                        return;
                    }

                    if (!_handoff)
                    {
                        border.ForwardTouch(G9SheetViewTouchAction.Pressed, dpPoint);
                        _handoff = true;
                    }
                }

                if (_handoff)
                {
                    border.ForwardTouch(G9SheetViewTouchAction.Moved, dpPoint);
                }

                _lastPoint = location;
                break;

            case UIGestureRecognizerState.Ended:
            case UIGestureRecognizerState.Cancelled:
            case UIGestureRecognizerState.Failed:
                if (_handoff)
                {
                    border.ForwardTouch(
                        State == UIGestureRecognizerState.Cancelled
                            ? G9SheetViewTouchAction.Cancelled
                            : G9SheetViewTouchAction.Released,
                        dpPoint);
                }

                ResetState();
                break;
        }
    }

    private void ResetState()
    {
        _activeScrollView = null;
        _insideScrollable = false;
        _handoff = false;
    }

    private static UIScrollView? ResolveScrollableAncestor(UIView? view)
    {
        var current = view;
        while (current is not null)
        {
            if (current is UIScrollView scroll)
            {
                return scroll;
            }

            current = current.Superview;
        }

        return null;
    }

    private static bool CanInnerScroll(UIScrollView scroll, nfloat dy)
    {
        var topInset = scroll.AdjustedContentInset.Top;
        var bottomInset = scroll.AdjustedContentInset.Bottom;
        var visible = scroll.Bounds.Height;
        var contentHeight = scroll.ContentSize.Height;

        if (contentHeight <= visible - (topInset + bottomInset))
        {
            return false;
        }

        var minOffsetY = -topInset;
        var maxOffsetY = (nfloat)Math.Max(0, contentHeight - visible + bottomInset);
        var y = scroll.ContentOffset.Y;

        if (dy < 0)
        {
            return y < (maxOffsetY - EdgeEpsilon);
        }

        if (dy > 0)
        {
            return y > (minOffsetY + EdgeEpsilon);
        }

        return false;
    }
}
#endif
