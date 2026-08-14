#if ANDROID
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AView = Android.Views.View;
#endif

#if IOS || MACCATALYST
using Foundation;
using ObjCRuntime;
using UIKit;
#endif

#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
#endif

using G9MAUIControls.Hosting;

namespace G9MAUIControls.Controls;

/// <summary>
///     Tap-outside-to-dismiss-keyboard for an entire ContentPage. Replaces MAUI's
///     built-in <c>ContentPage.HideSoftInputOnTapped</c> because that built-in
///     gates its registration on <c>NavigatedTo</c> firing (see
///     <c>HideSoftInputOnTappedChangedManager.Platform.cs</c> in dotnet/maui),
///     which Nalu's Shell-driven navigation doesn't raise for our pages.
///     <para>
///         The dismisser is page-scoped: <see cref="Attach" /> is called from
///         <c>OnAppearing</c> and the returned
///         <see cref="IDisposable" /> is disposed from
///         <c>OnDisappearing</c> so the platform
///         hooks live exactly as long as the page is on screen.
///     </para>
///     <para>
///         <b>Android.</b> Subscribes to
///         <c>TouchDispatched</c> — a
///         pre-existing event the activity raises from
///         <c>Activity.DispatchTouchEvent</c>, before any view in the hierarchy
///         claims the touch. On <c>ACTION_UP</c>, if the keyboard is showing and
///         the touch coordinates do NOT land on a focused / focusable
///         <c>EditText</c> (or any view whose ancestor up to
///         <c>ScrollView</c>/<c>HorizontalScrollView</c>
///         contains an EditText with focus), we hide the IME and clear focus.
///         This keeps Entry-to-Entry focus transfer working: a tap on a
///         different EditText is detected and the IME stays up.
///     </para>
///     <para>
///         <b>iOS / Mac Catalyst.</b> Adds a single
///         <c>UITapGestureRecognizer</c> to the key window. The recognizer's
///         <c>ShouldReceiveTouch</c> returns false when the touch lands on a
///         <c>UITextField</c>, <c>UITextView</c>, or any ancestor
///         that <c>CanBecomeFirstResponder</c> (so taps on Entry, buttons inside
///         the keyboard accessory, etc. don't trigger the dismiss). On a recognized
///         tap the gesture calls <c>EndEditing(true)</c> on the window, which
///         dismisses the keyboard and unfocuses the input.
///     </para>
///     <para>
///         <b>Windows.</b> Hooks <c>PointerPressed</c> on the WinUI window's content
///         root. When a press lands outside a <c>TextBox</c> while one is focused,
///         moves platform focus to the nearest <c>ScrollViewer</c>'s <c>IsTabStop</c>
///         <c>ContentPanel</c> via <c>FocusManager.TryFocusAsync</c>, which fires MAUI's
///         <c>Unfocused</c> event and clears the visual focus state. This is the
///         desktop equivalent of "click outside to blur" — MAUI's own
///         <c>HideSoftInputOnTapped</c> on Windows only hides the soft keyboard but
///         does not remove focus from the <c>Entry</c> (confirmed MAUI issue #21053,
///         closed as not planned).
///     </para>
/// </summary>
public static class TapOutsideKeyboardDismisser
{
    /// <summary>
    ///     Wires up the platform tap-outside detector for the given page. The
    ///     returned token must be disposed when the page disappears so the
    ///     subscription doesn't leak across pages.
    /// </summary>
    public static IDisposable Attach(Page page)
    {
        ArgumentNullException.ThrowIfNull(page);

#if ANDROID
        return new AndroidDismisser(page);
#elif IOS || MACCATALYST
        return new IosDismisser();
#elif WINDOWS
        return new WindowsDismisser(page);
#else
        return EmptyDisposable.Instance;
#endif
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }

#if ANDROID
    private sealed class AndroidDismisser : IDisposable
    {
        private readonly EventHandler<object?>? _handler;
        private readonly WeakReference<Page> _pageRef;

        public AndroidDismisser(Page page)
        {
            _pageRef = new WeakReference<Page>(page);
            _handler = OnTouchDispatched;
            G9AndroidHost.TouchDispatched += _handler;
        }

        public void Dispose()
        {
            if (_handler is not null)
            {
                G9AndroidHost.TouchDispatched -= _handler;
            }
        }

        private void OnTouchDispatched(object? sender, object? args)
        {
            // The host hook is typed EventHandler<object?> so G9AndroidHost stays usable from
            // shared code without a platform reference; cast back here.
            if (args is not MotionEvent e)
            {
                return;
            }

            // We only act on ACTION_UP — letting ACTION_DOWN / ACTION_MOVE flow
            // freely so child views can still claim the gesture (CollectionView
            // scroll, button presses, swipe rows). On UP we hit-test against
            // the currently-focused EditText.
            if (e.Action != MotionEventActions.Up) return;
            if (!_pageRef.TryGetTarget(out var page) || page.Handler is null) return;

            var activity = G9AndroidHost.CurrentActivity as Activity ?? Platform.CurrentActivity;
            if (activity?.Window?.DecorView is not AView decorView) return;

            // 1. Is there a currently-focused view that is (or contains) an EditText?
            var focused = activity.CurrentFocus;
            if (focused is not EditText focusedEdit) return;

            // 2. Is the IME actually showing? If not we have nothing to dismiss.
            if (!IsKeyboardVisible(activity)) return;

            // 3. Where did the touch land? Hit-test the focused EditText's
            //    on-screen bounds against the touch point. If the touch is
            //    inside, do nothing (the user tapped the focused field, e.g. to
            //    move the caret). If the touch landed on ANOTHER focusable
            //    EditText we also bail out — Android will move focus and the
            //    IME stays up.
            var loc = new int[2];
            focusedEdit.GetLocationOnScreen(loc);
            var x = (int)e.RawX;
            var y = (int)e.RawY;
            var insideFocused =
                x >= loc[0] &&
                x <= loc[0] + focusedEdit.Width &&
                y >= loc[1] &&
                y <= loc[1] + focusedEdit.Height;
            if (insideFocused) return;

            // Tapping a different EditText? Walk the decor view's hierarchy
            // looking for an EditText whose bounds contain the touch.
            if (FindEditTextAt(decorView, x, y) is not null) return;

            // 4. Tapped outside any EditText while the IME is up — dismiss.
            HideKeyboardAndClearFocus(activity, focusedEdit);
        }

        private static bool IsKeyboardVisible(Activity activity)
        {
            // The most reliable cross-API check: query the current insets on
            // API 30+ (R), fall back to a height-comparison heuristic on older
            // releases.
            var decor = activity.Window?.DecorView;
            if (decor is null) return false;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
#pragma warning disable CA1416
                var insets = decor.RootWindowInsets;
                if (insets is null) return false;
                return insets.IsVisible(WindowInsets.Type.Ime());
#pragma warning restore CA1416
            }

#pragma warning disable CA1422
            var rect = new Android.Graphics.Rect();
            decor.GetWindowVisibleDisplayFrame(rect);
            var screenHeight = decor.RootView?.Height ?? 0;
            var keypadHeight = screenHeight - rect.Bottom;
            // 15% of screen height is the conventional cutoff for "keyboard is up".
            return screenHeight > 0 && keypadHeight > screenHeight * 0.15;
#pragma warning restore CA1422
        }

        private static EditText? FindEditTextAt(AView root, int x, int y)
        {
            if (root is EditText et && et.IsShown)
            {
                var loc = new int[2];
                et.GetLocationOnScreen(loc);
                if (x >= loc[0] && x <= loc[0] + et.Width &&
                    y >= loc[1] && y <= loc[1] + et.Height)
                {
                    return et;
                }
            }

            if (root is ViewGroup vg)
            {
                for (var i = 0; i < vg.ChildCount; i++)
                {
                    var child = vg.GetChildAt(i);
                    if (child is null) continue;
                    var hit = FindEditTextAt(child, x, y);
                    if (hit is not null) return hit;
                }
            }

            return null;
        }

        private static void HideKeyboardAndClearFocus(Activity activity, AView focused)
        {
            try
            {
                if (activity.GetSystemService(Context.InputMethodService) is InputMethodManager imm)
                {
                    var token = focused.WindowToken
                                ?? activity.CurrentFocus?.WindowToken
                                ?? activity.Window?.DecorView.WindowToken;
                    if (token is not null)
                    {
                        imm.HideSoftInputFromWindow(token, HideSoftInputFlags.None);
                    }
                }

                focused.ClearFocus();
            }
            catch
            {
                // Defensive — IME calls can throw if the window is being torn
                // down concurrently with the touch dispatch. Swallowing here
                // is safe; the worst case is the keyboard staying up for one
                // extra frame.
            }
        }
    }
#endif

#if IOS || MACCATALYST
    private sealed class IosDismisser : IDisposable
    {
        private UITapGestureRecognizer? _recognizer;
        private OutsideTapDelegate? _gateDelegate;
        private UIWindow? _window;

        public IosDismisser()
        {
            _window = UIApplication.SharedApplication
                .ConnectedScenes
                .OfType<UIWindowScene>()
                .SelectMany(s => s.Windows)
                .FirstOrDefault(w => w.IsKeyWindow);

            if (_window is null) return;

            _gateDelegate = new OutsideTapDelegate();
            _recognizer = new UITapGestureRecognizer(OnWindowTap)
            {
                CancelsTouchesInView = false,
                DelaysTouchesBegan = false,
                DelaysTouchesEnded = false,
                Delegate = _gateDelegate
            };
            _window.AddGestureRecognizer(_recognizer);
        }

        public void Dispose()
        {
            if (_recognizer is not null && _window is not null)
            {
                _window.RemoveGestureRecognizer(_recognizer);
            }

            _recognizer?.Dispose();
            _gateDelegate?.Dispose();
            _recognizer = null;
            _gateDelegate = null;
            _window = null;
        }

        private void OnWindowTap()
        {
            _window?.EndEditing(true);
        }

        /// <summary>
        ///     Decides per-touch whether the recognizer should fire. We want to
        ///     suppress the dismiss when the touch lands on:
        ///       • a UITextField / UITextView (the user is interacting with the
        ///         input itself — caret placement, selection, paste menu)
        ///       • any ancestor view that CanBecomeFirstResponder (Entry-to-Entry
        ///         transfers, input-accessory toolbars, picker views)
        ///     Returning false from ShouldReceiveTouch lets the touch flow through
        ///     to the underlying view normally.
        /// </summary>
        private sealed class OutsideTapDelegate : UIGestureRecognizerDelegate
        {
            public override bool ShouldReceiveTouch(UIGestureRecognizer recognizer, UITouch touch)
            {
                for (var view = touch.View; view is not null; view = view.Superview)
                {
                    if (view is UITextField or UITextView) return false;
                    if (view.CanBecomeFirstResponder) return false;
                }
                return true;
            }

            public override bool ShouldRecognizeSimultaneously(
                UIGestureRecognizer gestureRecognizer,
                UIGestureRecognizer otherGestureRecognizer) => true;
        }
    }
#endif

#if WINDOWS
    /// <summary>
    ///     Windows implementation. Hooks <c>PointerPressed</c> on the WinUI window's
    ///     content root. When a press lands outside a <c>TextBox</c> while one is
    ///     focused, moves platform focus to a neutral focusable container so the
    ///     <c>TextBox</c> blurs, which fires MAUI's <c>Unfocused</c> event and clears the
    ///     visual focus state.
    ///     <para>
    ///         <b>Why <c>PointerPressed</c> on the content root?</b> WinUI routes
    ///         pointer events up the visual tree; subscribing at the root with
    ///         <c>handledEventsToo: true</c> means we see every press regardless of
    ///         whether a child already handled it. We only act when the currently-focused
    ///         element is a <c>TextBox</c> AND the press target is not a <c>TextBox</c>
    ///         (or a descendant of one), so Entry-to-Entry focus transfer is unaffected.
    ///     </para>
    ///     <para>
    ///         <b>How focus is moved.</b> <see cref="FindFocusTarget" /> walks up from the
    ///         focused <c>TextBox</c> to the nearest <c>ScrollViewer</c>'s
    ///         <c>IsTabStop</c> <c>ContentPanel</c> (made tab-stoppable by the
    ///         <c>ScrollViewHandler</c> mapper) and we call
    ///         <c>FocusManager.TryFocusAsync(target, FocusState.Pointer)</c>. Three other
    ///         approaches were tried and rejected: (1) MAUI's <c>HideSoftInputOnTapped</c>
    ///         only hides the soft keyboard, never removes focus (MAUI issue #21053,
    ///         closed not-planned); (2) <c>FocusManager.TryMoveFocus(Next)</c> walks the
    ///         tab order and focuses the NEXT input; (3) <c>TryFocusAsync</c> on
    ///         <c>XamlRoot.Content</c> returns <c>Succeeded=False</c> because the root
    ///         container is not itself focusable. Targeting a focusable container is the
    ///         only approach that blurs the <c>TextBox</c> without activating another input.
    ///     </para>
    /// </summary>
    private sealed class WindowsDismisser : IDisposable
    {
        private FrameworkElement? _root;
        private PointerEventHandler? _handler;

        public WindowsDismisser(Page page)
        {
            // Defer wiring until the page's platform view is available. If the
            // handler is already set we can wire immediately; otherwise we wait
            // for HandlerChanged.
            if (page.Handler?.PlatformView is FrameworkElement fe)
            {
                Wire(fe);
            }
            else
            {
                page.HandlerChanged += OnHandlerChanged;

                void OnHandlerChanged(object? s, EventArgs _)
                {
                    if (s is Page p)
                    {
                        p.HandlerChanged -= OnHandlerChanged;
                        if (p.Handler?.PlatformView is FrameworkElement root)
                        {
                            Wire(root);
                        }
                    }
                }
            }
        }

        private void Wire(FrameworkElement root)
        {
            _root = root;
            _handler = OnPointerPressed;
            // handledEventsToo=true so we see presses that child elements already
            // handled (e.g. a tap on a Button inside the page still reaches us).
            _root.AddHandler(
                UIElement.PointerPressedEvent,
                _handler,
                handledEventsToo: true);
        }

        public void Dispose()
        {
            if (_root is not null && _handler is not null)
            {
                _root.RemoveHandler(UIElement.PointerPressedEvent, _handler);
            }
            _root = null;
            _handler = null;
        }

        private static async void OnPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            try
            {
                var xamlRoot = (sender as UIElement)?.XamlRoot;

                // Only act when a TextBox currently holds focus — if nothing is
                // focused, or a non-text element is focused, there's nothing to blur.
                var focused = FocusManager.GetFocusedElement(xamlRoot) as DependencyObject;
                if (focused is not Microsoft.UI.Xaml.Controls.TextBox)
                {
                    return;
                }

                // Is the press target itself (or one of its ancestors) a TextBox?
                // If so the user is interacting with an input — don't steal focus.
                var original = e.OriginalSource as DependencyObject;
                for (var el = original; el is not null;
                     el = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(el))
                {
                    if (el is Microsoft.UI.Xaml.Controls.TextBox)
                    {
                        return;
                    }
                }

                // Press landed outside any TextBox. Move focus to a neutral, focusable
                // container so the TextBox loses focus WITHOUT focus jumping to the next
                // input.
                //
                // Targeting XamlRoot.Content (WindowRootViewContainer) fails —
                // TryFocusAsync returns Succeeded=False because that root container is
                // not itself a focusable element, so focus never leaves the TextBox.
                // Likewise TryMoveFocus(Next) walks the tab order and focuses the NEXT
                // input — the wrong behaviour. Instead FindFocusTarget walks up to the
                // nearest ScrollViewer's IsTabStop ContentPanel (made IsTabStop=true by
                // the ScrollViewHandler mapper), which IS focusable, so TryFocusAsync
                // succeeds and the TextBox blurs cleanly without activating another input.
                // This fires the TextBox's LostFocus and MAUI's Unfocused.
                var target = FindFocusTarget(focused);
                if (target is null)
                {
                    return;
                }

                await FocusManager.TryFocusAsync(target, FocusState.Pointer);
            }
            catch
            {
                // Defensive — FocusManager calls can throw during page teardown.
            }
        }

        /// <summary>
        ///     Walk up the visual tree from the focused <c>TextBox</c> to find a neutral,
        ///     focusable container to move focus to. Prefers the nearest
        ///     <c>ScrollViewer</c>'s content panel (our <c>ContentPanel</c>, made
        ///     <c>IsTabStop=true</c> by the <c>ScrollViewHandler</c> mapper), then the
        ///     <c>ScrollViewer</c> itself, then any <c>IsTabStop</c> ancestor. Returns
        ///     null when nothing suitable is found.
        /// </summary>
        private static DependencyObject? FindFocusTarget(DependencyObject focused)
        {
            Microsoft.UI.Xaml.Controls.ScrollViewer? scrollViewer = null;
            Microsoft.UI.Xaml.Controls.Control? tabStopAncestor = null;

            for (var el = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(focused);
                 el is not null;
                 el = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(el))
            {
                if (scrollViewer is null && el is Microsoft.UI.Xaml.Controls.ScrollViewer sv)
                {
                    scrollViewer = sv;
                }
                if (tabStopAncestor is null && el is Microsoft.UI.Xaml.Controls.Control { IsTabStop: true } ctrl)
                {
                    tabStopAncestor = ctrl;
                }
            }

            // Prefer the ScrollViewer's IsTabStop content panel if present.
            if (scrollViewer?.Content is Microsoft.Maui.Platform.ContentPanel { IsTabStop: true } panel)
            {
                return panel;
            }
            if (scrollViewer is not null)
            {
                return scrollViewer;
            }
            return tabStopAncestor;
        }
    }
#endif
}
