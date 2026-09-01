#if WINDOWS
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Windows handler that wires <see cref="UIElement.PointerPressed" /> /
///     <see cref="UIElement.PointerMoved" /> / <see cref="UIElement.PointerReleased" /> on the
///     platform view of <see cref="G9SheetViewBorder" />. Walks <see cref="ScrollViewer" />
///     ancestors so an inner scroller keeps the gesture until it hits its edge, then hands off
///     the drag to the bottom sheet for a smooth state change / drag-to-close.
/// </summary>
internal sealed class G9SheetViewBorderHandler : BorderHandler
{
    private FrameworkElement? _platformView;
    private ScrollViewer? _activeScrollViewer;
    private bool _insideScrollable;
    private bool _handoff;
    private bool _isTouchHandled;
    private double _lastPointerY;

    protected override void ConnectHandler(ContentPanel platformView)
    {
        base.ConnectHandler(platformView);

        _platformView = platformView;
        platformView.ManipulationMode = ManipulationModes.All;
        platformView.PointerPressed += OnPointerPressed;
        platformView.PointerMoved += OnPointerMoved;
        platformView.PointerReleased += OnPointerReleased;
        platformView.PointerCanceled += OnPointerReleased;
        platformView.PointerCaptureLost += OnPointerCaptureLost;
    }

    protected override void DisconnectHandler(ContentPanel platformView)
    {
        platformView.PointerPressed -= OnPointerPressed;
        platformView.PointerMoved -= OnPointerMoved;
        platformView.PointerReleased -= OnPointerReleased;
        platformView.PointerCanceled -= OnPointerReleased;
        platformView.PointerCaptureLost -= OnPointerCaptureLost;
        _platformView = null;

        base.DisconnectHandler(platformView);
    }

    private G9SheetViewBorder? Border => VirtualView as G9SheetViewBorder;

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_platformView is null || Border is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(_platformView).Position;
        _activeScrollViewer = ResolveScrollViewerAncestor(e.OriginalSource as DependencyObject);
        _insideScrollable = _activeScrollViewer is not null;
        _handoff = false;
        _lastPointerY = point.Y;
        _isTouchHandled = e.Pointer.PointerDeviceType == PointerDeviceType.Touch;

        // Press outside any scroller — start the sheet gesture immediately.
        if (!_insideScrollable)
        {
            Border.ForwardTouch(G9SheetViewTouchAction.Pressed, new Point(point.X, point.Y));
            _handoff = true;
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_platformView is null || Border is null)
        {
            return;
        }

        // Suppress synthetic mouse moves on touch devices.
        if (e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && _isTouchHandled)
        {
            return;
        }

        var pp = e.GetCurrentPoint(_platformView);
        var props = pp.Properties;
        var pressed = e.Pointer.PointerDeviceType switch
        {
            PointerDeviceType.Mouse => props.IsLeftButtonPressed,
            PointerDeviceType.Touch => pp.IsInContact,
            _ => false
        };

        if (!pressed)
        {
            return;
        }

        var point = pp.Position;
        var dy = point.Y - _lastPointerY;

        if (_insideScrollable && _activeScrollViewer is { } scroll)
        {
            // The sheet outranks the scroller while there is still a larger detent to reach — see
            // G9SheetView.ScrollingExpandsSheet. Always true for a single-detent sheet, so the
            // classic edge-handoff behaviour is unchanged for every fixed sheet.
            if (Border.ShouldInnerScrollerConsumeDrag() && CanInnerScroll(scroll, dy))
            {
                _lastPointerY = point.Y;
                return;
            }

            if (!_handoff)
            {
                Border.ForwardTouch(G9SheetViewTouchAction.Pressed, new Point(point.X, point.Y));
                _handoff = true;
                _platformView.CapturePointer(e.Pointer);
            }

            e.Handled = true;
        }

        if (_handoff)
        {
            Border.ForwardTouch(G9SheetViewTouchAction.Moved, new Point(point.X, point.Y));
        }

        _lastPointerY = point.Y;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_platformView is null || Border is null)
        {
            return;
        }

        var point = e.GetCurrentPoint(_platformView).Position;

        if (_handoff)
        {
            Border.ForwardTouch(G9SheetViewTouchAction.Released, new Point(point.X, point.Y));
        }

        try
        {
            _platformView.ReleasePointerCaptures();
        }
        catch
        {
            // Swallow — capture was already released by the system.
        }

        ResetState();
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_handoff && Border is not null)
        {
            Border.ForwardTouch(G9SheetViewTouchAction.Cancelled, new Point(0, 0));
        }

        ResetState();
    }

    private void ResetState()
    {
        _activeScrollViewer = null;
        _insideScrollable = false;
        _handoff = false;
        _isTouchHandled = false;
    }

    /// <summary>
    ///     Nearest ancestor scroll viewer that can actually scroll VERTICALLY. Horizontal-only
    ///     scrollers are skipped so a side-scrolling row inside the body cannot swallow the body's
    ///     own vertical scrolling — see the iOS handler for the same rule.
    /// </summary>
    private static ScrollViewer? ResolveScrollViewerAncestor(DependencyObject? start)
    {
        var current = start;
        while (current is not null)
        {
            if (current is ScrollViewer sv && sv.ScrollableHeight > 0.5)
            {
                return sv;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private static bool CanInnerScroll(ScrollViewer sv, double dy)
    {
        if (dy < 0)
        {
            return sv.VerticalOffset < sv.ScrollableHeight - 0.5;
        }

        if (dy > 0)
        {
            return sv.VerticalOffset > 0.5;
        }

        return false;
    }
}
#endif
