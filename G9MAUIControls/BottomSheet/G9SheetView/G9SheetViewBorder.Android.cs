#if ANDROID
using Android.Content;
using Android.Views;
using Android.Widget;
using AndroidX.Core.View;
using AndroidX.Core.Widget;
using AndroidX.RecyclerView.Widget;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using AndroidView = Android.Views.View;
using AndroidScrollView = Android.Widget.ScrollView;

namespace G9MAUIControls.BottomSheet;

internal partial class G9SheetViewBorder
{
    private bool _isMotionLayerApplied;

    /// <summary>
    ///     While the body moves, composite it from a hardware layer: the GPU re-blits one cached
    ///     texture per frame instead of re-rasterizing every view in the sheet. A translation never
    ///     dirties the layer, which is exactly the property the translation-only motion is built to
    ///     exploit. Released the moment the motion ends — a permanent layer costs texture memory and
    ///     makes every later content invalidation MORE expensive, not less.
    /// </summary>
    partial void OnBeginMotionLayer()
    {
        if (!G9SheetView.UseHardwareLayerDuringMotion ||
            _isMotionLayerApplied ||
            Handler?.PlatformView is not AndroidView platformView)
        {
            return;
        }

        platformView.SetLayerType(LayerType.Hardware, null);
        _isMotionLayerApplied = true;
    }

    partial void OnEndMotionLayer()
    {
        if (!_isMotionLayerApplied)
        {
            return;
        }

        _isMotionLayerApplied = false;
        if (Handler?.PlatformView is AndroidView platformView)
        {
            platformView.SetLayerType(LayerType.None, null);
        }
    }
}

/// <summary>
///     Android handler that swaps in a custom <see cref="ContentViewGroup" /> subclass for the
///     border. The platform group intercepts vertical drags so inner scrollables (RecyclerView,
///     NestedScrollView, ScrollView, AbsListView) keep their natural scroll until they hit an
///     edge, at which point we hand off to the bottom sheet for state changes / drag-to-close.
///     This is a direct port of the original vendored Syncfusion logic — the only difference is
///     that we forward the locally-defined <see cref="G9SheetViewTouchAction" /> instead of
///     a Syncfusion <c>Internals.PointerActions</c>.
/// </summary>
internal sealed class G9SheetViewBorderHandler : BorderHandler
{
    protected override ContentViewGroup CreatePlatformView()
    {
        var view = VirtualView ?? throw new InvalidOperationException("VirtualView must be set.");
        if (view is not G9SheetViewBorder border)
        {
            throw new InvalidOperationException(
                $"Expected {typeof(G9SheetViewBorder).FullName}, got {view.GetType().FullName}.");
        }

        var group = new G9SheetViewBorderPlatformView(Context, border);
        group.SetClipChildren(true);
        return group;
    }

    public override void SetVirtualView(IView view) => base.SetVirtualView(view);
}

/// <summary>
///     Custom platform-side container for <see cref="G9SheetViewBorder" />. Intercepts
///     vertical drag once an inner scrollable hits its edge and forwards the gesture to the
///     bottom sheet for a smooth state change / close.
/// </summary>
internal sealed class G9SheetViewBorderPlatformView : ContentViewGroup
{
    private readonly WeakReference<G9SheetViewBorder> _borderRef;
    private readonly int _touchSlop;

    private AndroidView? _scrollableUnderFinger;
    private bool _insideScrollable;
    private bool _gestureForwarded;
    private float _lastX;
    private float _lastY;

    // Cached: Resources → DisplayMetrics → Density is three JNI hops, and this was being read on
    // every single touch event. Refreshed in OnConfigurationChanged, the only time it can change.
    private float _density;

    // The pointer that started the gesture. MotionEvent.GetX()/GetY() read pointer INDEX 0, which
    // becomes a different finger the moment the first one lifts — the body then jumps to it.
    private const int InvalidPointerId = -1;
    private int _activePointerId = InvalidPointerId;

    // Release speed for the sheet's fling decision. Fed with SCREEN coordinates: this view moves
    // with the finger while it is being dragged, so view-relative Y barely changes and a tracker
    // fed with it would report almost no velocity for the fastest flick.
    private VelocityTracker? _velocityTracker;
    private long _lastTrackedEventTime = -1;

    public G9SheetViewBorderPlatformView(Context context, G9SheetViewBorder border)
        : base(context)
    {
        _borderRef = new WeakReference<G9SheetViewBorder>(border);
        SetClipChildren(true);
        _touchSlop = ViewConfiguration.Get(context) is { } vc ? vc.ScaledTouchSlop : 8;
        _density = ReadDensity();
        Clickable = true;
        Focusable = true;
    }

    private float ReadDensity()
    {
        var density = Resources?.DisplayMetrics?.Density ?? 1f;
        return density > 0 ? density : 1f;
    }

    protected override void OnConfigurationChanged(Android.Content.Res.Configuration? newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        _density = ReadDensity();
    }

    protected override void OnDetachedFromWindow()
    {
        RecycleVelocityTracker();
        base.OnDetachedFromWindow();
    }

    public override bool OnInterceptTouchEvent(MotionEvent? ev)
    {
        if (ev is null)
        {
            return base.OnInterceptTouchEvent(ev);
        }

        switch (ev.ActionMasked)
        {
            case MotionEventActions.Down:
                _activePointerId = ev.GetPointerId(0);
                _lastX = ev.GetX();
                _lastY = ev.GetY();
                _scrollableUnderFinger = FindScrollableUnder(this, (int)ev.GetX(), (int)ev.GetY());
                _insideScrollable = _scrollableUnderFinger is not null;
                _gestureForwarded = false;
                BeginVelocityTracking(ev);
                DisallowParentIntercept(true);

                // Never intercept Down. We want children to get a chance at it (so taps on
                // buttons / scrolls inside lists work normally), and Android only re-checks
                // OnInterceptTouchEvent on subsequent moves when a child actually consumed
                // Down. If no child consumes (the very common case for our test sheets — only
                // labels and borders inside the body), the gesture stream falls through to
                // OUR OnTouchEvent below, which is where the body-drag forwarding happens.
                return false;

            case MotionEventActions.Move:
                // Only need intercept logic for the inside-scrollable edge-handoff case. For
                // a body that has no scrollable child under the finger, the children won't
                // consume Down and the gesture stream is handled in OnTouchEvent directly —
                // so we deliberately skip the previous "always-intercept on vertical drag"
                // path here to avoid a double-fire (OnInterceptTouchEvent(Move) forwarding
                // *and* OnTouchEvent(Move) forwarding the same event).
                TrackVelocity(ev);

                if (!_insideScrollable || _scrollableUnderFinger is null)
                {
                    return false;
                }

                var pointerIndex = ResolveActivePointerIndex(ev);
                var curX = ev.GetX(pointerIndex);
                var curY = ev.GetY(pointerIndex);
                var dx = curX - _lastX;
                var dy = curY - _lastY;

                if (Math.Abs(dx) < _touchSlop && Math.Abs(dy) < _touchSlop)
                {
                    return false;
                }

                // Mostly-horizontal swipes belong to inner CarouselView / swipe-to-delete /
                // tab gestures, so leave them alone.
                if (Math.Abs(dx) > Math.Abs(dy) * 1.2f)
                {
                    _lastX = curX;
                    _lastY = curY;
                    return false;
                }

                // dy > 0 (finger down)  -> child needs to scroll UP    -> dir = -1
                // dy < 0 (finger up)    -> child needs to scroll DOWN  -> dir = +1
                var dir = dy > 0 ? -1 : 1;

                // The sheet outranks the scroller while there is still a larger detent to reach
                // (G9SheetView.ScrollingExpandsSheet — the Material nested-scroll / UIKit
                // prefersScrollingExpandsWhenScrolledToEdge contract). Asked BEFORE the edge test,
                // because a scroller that CAN scroll is exactly the case being overridden. Reads
                // true — i.e. behaves as it always did — for any sheet already at its maximum.
                var innerMayConsume = !_borderRef.TryGetTarget(out var gateBorder) ||
                                      gateBorder.ShouldInnerScrollerConsumeDrag();

                if (innerMayConsume && CanChildScrollVertically(_scrollableUnderFinger, dir))
                {
                    // No DisallowParentIntercept here: the request made on Down holds for the whole
                    // gesture. Repeating it on every move was an ancestor-chain walk per frame.
                    _lastX = curX;
                    _lastY = curY;
                    return false;
                }

                // Edge reached (or the sheet claimed the drag) — take over and forward to the
                // bottom sheet. Android sends
                // ACTION_CANCEL to the inner scrollable for us when intercept switches to
                // true mid-stream, so the list cleanly hands off without a residual scroll.
                DisallowParentIntercept(true);
                ViewCompat.StopNestedScroll(_scrollableUnderFinger);

                if (_scrollableUnderFinger is RecyclerView rv)
                {
                    rv.StopScroll();
                }

                if (_scrollableUnderFinger is NestedScrollView nsv)
                {
                    nsv.StopNestedScroll();
                }

                ForwardSheetGestureStart(ev);
                _gestureForwarded = true;

                _lastX = curX;
                _lastY = curY;
                return true;

            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                ResetGestureState();
                break;
        }

        return base.OnInterceptTouchEvent(ev);
    }

    /// <summary>
    ///     Synthesise a Pressed at the current touch point so the bottom sheet has a valid
    ///     gesture start, then forward the in-flight Move. Called from the inside-scrollable
    ///     edge-handoff path in <see cref="OnInterceptTouchEvent" />.
    /// </summary>
    private void ForwardSheetGestureStart(MotionEvent ev)
    {
        if (!_borderRef.TryGetTarget(out var border))
        {
            return;
        }

        var dpPoint = ToDpPoint(ev);
        border.ForwardTouch(G9SheetViewTouchAction.Pressed, dpPoint);
        border.ForwardTouch(G9SheetViewTouchAction.Moved, dpPoint);
    }

    private Microsoft.Maui.Graphics.Point ToDpPoint(MotionEvent ev)
    {
        var index = ResolveActivePointerIndex(ev);
        return new Microsoft.Maui.Graphics.Point(ev.GetX(index) / _density, ev.GetY(index) / _density);
    }

    private int ResolveActivePointerIndex(MotionEvent ev)
    {
        if (_activePointerId == InvalidPointerId)
        {
            return 0;
        }

        var index = ev.FindPointerIndex(_activePointerId);
        return index >= 0 ? index : 0;
    }

    private void BeginVelocityTracking(MotionEvent ev)
    {
        RecycleVelocityTracker();
        _velocityTracker = VelocityTracker.Obtain();
        _lastTrackedEventTime = -1;
        TrackVelocity(ev);
    }

    private void TrackVelocity(MotionEvent ev)
    {
        // The same event can arrive through OnInterceptTouchEvent AND OnTouchEvent; feed it once.
        if (_velocityTracker is null || ev.EventTime == _lastTrackedEventTime)
        {
            return;
        }

        _lastTrackedEventTime = ev.EventTime;

        var screenEvent = MotionEvent.Obtain(ev);
        if (screenEvent is null)
        {
            return;
        }

        try
        {
            screenEvent.OffsetLocation(ev.RawX - ev.GetX(), ev.RawY - ev.GetY());
            _velocityTracker.AddMovement(screenEvent);
        }
        finally
        {
            screenEvent.Recycle();
        }
    }

    /// <summary>Vertical release speed in dp/s, positive downward; 0 when it cannot be measured.</summary>
    private double ResolveReleaseVelocityDp()
    {
        if (_velocityTracker is null)
        {
            return 0;
        }

        _velocityTracker.ComputeCurrentVelocity(1000);
        var pixelsPerSecond = _activePointerId == InvalidPointerId
            ? _velocityTracker.YVelocity
            : _velocityTracker.GetYVelocity(_activePointerId);
        return pixelsPerSecond / _density;
    }

    private void RecycleVelocityTracker()
    {
        _velocityTracker?.Recycle();
        _velocityTracker = null;
    }

    public override bool OnTouchEvent(MotionEvent? ev)
    {
        // OnTouchEvent receives the gesture stream in two scenarios:
        //   1. OnInterceptTouchEvent returned false on Down AND no child consumed Down — the
        //      common case for the default sheet body (labels / non-clickable borders). All
        //      events come straight here, and we forward them as-is so the sheet's state
        //      machine gets a clean Pressed → Move → Released stream.
        //   2. OnInterceptTouchEvent returned true mid-stream (inside-scrollable edge handoff).
        //      In that case ForwardSheetGestureStart already raised Pressed + Move, so we only
        //      need to keep forwarding subsequent Move / Up here without re-raising Pressed.
        if (ev is null || !_borderRef.TryGetTarget(out var border))
        {
            return base.OnTouchEvent(ev);
        }

        TrackVelocity(ev);
        var dpPoint = ToDpPoint(ev);

        switch (ev.ActionMasked)
        {
            case MotionEventActions.Down:
                // Down here means scenario 1 above — synthesise the Pressed the sheet needs
                // before any Move forwarding can move the body.
                border.ForwardTouch(G9SheetViewTouchAction.Pressed, dpPoint);
                _gestureForwarded = true;
                return true;

            case MotionEventActions.PointerUp:
                // The finger that owns the drag lifted while another is still down. Index 0 now
                // belongs to that other finger, so carrying on would teleport the body to it. End
                // the gesture cleanly; the remaining finger starts a new one on its next Down.
                if (ev.GetPointerId(ev.ActionIndex) == _activePointerId)
                {
                    if (_gestureForwarded)
                    {
                        border.ForwardTouch(
                            G9SheetViewTouchAction.Released,
                            dpPoint,
                            ResolveReleaseVelocityDp());
                    }

                    ResetGestureState();
                }

                return true;

            case MotionEventActions.Move:
                if (!_gestureForwarded)
                {
                    // Defensive — e.g. some custom child returned true from its onTouchEvent
                    // for Down but cancelled before Move; we then receive Move here without
                    // ever having seen Down. Synthesise a Pressed first so the sheet has a
                    // valid gesture start.
                    border.ForwardTouch(G9SheetViewTouchAction.Pressed, dpPoint);
                    _gestureForwarded = true;
                }

                border.ForwardTouch(G9SheetViewTouchAction.Moved, dpPoint);
                return true;

            case MotionEventActions.Up:
                if (_gestureForwarded)
                {
                    border.ForwardTouch(G9SheetViewTouchAction.Released, dpPoint, ResolveReleaseVelocityDp());
                }

                ResetGestureState();
                return true;

            case MotionEventActions.Cancel:
                if (_gestureForwarded)
                {
                    border.ForwardTouch(G9SheetViewTouchAction.Cancelled, dpPoint);
                }

                ResetGestureState();
                return true;
        }

        return base.OnTouchEvent(ev);
    }

    private void DisallowParentIntercept(bool disallow)
    {
        // ONE call. ViewGroup.requestDisallowInterceptTouchEvent forwards the request to its own
        // parent natively, all the way up — walking the chain here as well made it O(depth²) JNI
        // calls, and it used to run on every qualifying move.
        Parent?.RequestDisallowInterceptTouchEvent(disallow);
    }

    private static AndroidView? FindScrollableUnder(ViewGroup root, int x, int y)
    {
        for (var i = root.ChildCount - 1; i >= 0; i--)
        {
            var child = root.GetChildAt(i);
            if (child is null)
            {
                continue;
            }

            var left = child.Left;
            var top = child.Top;
            var right = child.Right;
            var bottom = child.Bottom;

            if (x < left || x > right || y < top || y > bottom)
            {
                continue;
            }

            if (IsScrollable(child))
            {
                return child;
            }

            if (child is ViewGroup vg)
            {
                var nested = FindScrollableUnder(vg, x - left, y - top);
                if (nested is not null)
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static bool CanChildScrollVertically(AndroidView v, int dir)
    {
        if (v is AndroidScrollView sv)
        {
            var child = sv.ChildCount > 0 ? sv.GetChildAt(0) : null;
            if (child is null)
            {
                return false;
            }

            var viewport = sv.Height - sv.PaddingTop - sv.PaddingBottom;
            var range = Math.Max(0, child.MeasuredHeight - viewport);
            return dir > 0 ? sv.ScrollY < range : sv.ScrollY > 0;
        }

        if (v is NestedScrollView nsv)
        {
            var child = nsv.ChildCount > 0 ? nsv.GetChildAt(0) : null;
            if (child is null)
            {
                return false;
            }

            var viewport = nsv.Height - nsv.PaddingTop - nsv.PaddingBottom;
            var range = Math.Max(0, child.MeasuredHeight - viewport);
            return dir > 0 ? nsv.ScrollY < range : nsv.ScrollY > 0;
        }

        if (v is RecyclerView rv)
        {
            var offset = rv.ComputeVerticalScrollOffset();
            var extent = rv.ComputeVerticalScrollExtent();
            var range = rv.ComputeVerticalScrollRange();
            var maxOffset = Math.Max(0, range - extent);
            return dir > 0 ? offset < maxOffset : offset > 0;
        }

        return v.CanScrollVertically(dir);
    }

    private static bool IsScrollable(AndroidView v)
    {
        return v is RecyclerView
            || v is AndroidScrollView
            || v is AbsListView
            || v is NestedScrollView
            || v.CanScrollVertically(1)
            || v.CanScrollVertically(-1);
    }

    private void ResetGestureState()
    {
        _insideScrollable = false;
        _scrollableUnderFinger = null;
        _gestureForwarded = false;
        _activePointerId = InvalidPointerId;
        RecycleVelocityTracker();
    }
}
#endif
