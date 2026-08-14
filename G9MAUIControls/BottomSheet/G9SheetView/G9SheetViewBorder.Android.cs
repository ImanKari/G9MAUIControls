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

    public G9SheetViewBorderPlatformView(Context context, G9SheetViewBorder border)
        : base(context)
    {
        _borderRef = new WeakReference<G9SheetViewBorder>(border);
        SetClipChildren(true);
        _touchSlop = ViewConfiguration.Get(context) is { } vc ? vc.ScaledTouchSlop : 8;
        Clickable = true;
        Focusable = true;
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
                _lastX = ev.GetX();
                _lastY = ev.GetY();
                _scrollableUnderFinger = FindScrollableUnder(this, (int)ev.GetX(), (int)ev.GetY());
                _insideScrollable = _scrollableUnderFinger is not null;
                _gestureForwarded = false;
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
                if (!_insideScrollable || _scrollableUnderFinger is null)
                {
                    return false;
                }

                var curX = ev.GetX();
                var curY = ev.GetY();
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
                if (CanChildScrollVertically(_scrollableUnderFinger, dir))
                {
                    DisallowParentIntercept(true);
                    _lastX = curX;
                    _lastY = curY;
                    return false;
                }

                // Edge reached — take over and forward to the bottom sheet. Android sends
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

        var density = Resources?.DisplayMetrics?.Density ?? 1f;
        var dpPoint = new Microsoft.Maui.Graphics.Point(ev.GetX() / density, ev.GetY() / density);
        border.ForwardTouch(G9SheetViewTouchAction.Pressed, dpPoint);
        border.ForwardTouch(G9SheetViewTouchAction.Moved, dpPoint);
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

        var density = Resources?.DisplayMetrics?.Density ?? 1f;
        var dpPoint = new Microsoft.Maui.Graphics.Point(ev.GetX() / density, ev.GetY() / density);

        switch (ev.ActionMasked)
        {
            case MotionEventActions.Down:
                // Down here means scenario 1 above — synthesise the Pressed the sheet needs
                // before any Move forwarding can move the body.
                border.ForwardTouch(G9SheetViewTouchAction.Pressed, dpPoint);
                _gestureForwarded = true;
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
                    border.ForwardTouch(G9SheetViewTouchAction.Released, dpPoint);
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
        var p = Parent;
        while (p is not null)
        {
            p.RequestDisallowInterceptTouchEvent(disallow);
            p = p.Parent;
        }
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
    }
}
#endif
