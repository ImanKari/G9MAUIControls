#if ANDROID
using View = Android.Views.View;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views.InputMethods;
using AndroidMauiApplication = Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.Application;
using AndroidSoftInputModeAdjust = Microsoft.Maui.Controls.PlatformConfiguration.AndroidSpecific.WindowSoftInputModeAdjust;
#endif

#if IOS
using UIKit;
#endif

namespace G9MAUIControls.Helpers;

public class G9KeyboardHelper
{
#if ANDROID
    private static readonly Lock AndroidSoftInputModeLock = new();
    private static int _androidPanSoftInputModeUsers;
#endif

    /// <summary>
    ///     Method to hide the keyboard and clear focus from the specified VisualElement.
    /// </summary>
    /// <exception>All exception are ignored</exception>
    /// <param name="element">Specified the target element that has focus and keyboard is opened based on</param>
    public static void HideKeyboardAndClearFocus(VisualElement? element = null)
    {
        try
        {
#if ANDROID
            Activity? activity = Platform.CurrentActivity;
            if (activity == null)
            {
                return;
            }

            InputMethodManager? imm = activity.GetSystemService(Context.InputMethodService) as InputMethodManager;
            if (imm == null)
            {
                return;
            }

            IBinder? token = null;

            if (element?.Handler?.PlatformView is View nativeView)
            {
                token = nativeView.WindowToken;
            }

            token ??= activity.CurrentFocus?.WindowToken;

            token ??= activity.Window?.DecorView.WindowToken;

            if (token != null)
            {
                imm.HideSoftInputFromWindow(token, HideSoftInputFlags.None);
            }

            // Prevent re-open
            activity.Window?.DecorView.ClearFocus();
#elif IOS
#pragma warning disable CA1422
#pragma warning disable CA1416
            if (element?.Handler?.PlatformView is UIView nativeView)
            {
                nativeView.EndEditing(true);
            }
            else
            {
                // Fallback: active window in current scene
                UIWindow? window = UIApplication.SharedApplication
                    .ConnectedScenes
                    .OfType<UIWindowScene>()
                    .SelectMany(s => s.Windows)
                    .FirstOrDefault(w => w.IsKeyWindow);

                window?.EndEditing(true);
            }
#pragma warning restore CA1416
#pragma warning restore CA1422
#endif
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    ///     Temporarily switches Android to pan the focused input above the keyboard.
    ///     Returns an idempotent restore action; call it when the owning sheet/page closes.
    ///     On non-Android platforms the returned action is a no-op.
    /// </summary>
    public static Action UseAndroidPanSoftInputMode()
    {
#if ANDROID
        PushAndroidPanSoftInputMode();
        var restored = 0;
        return () =>
        {
            if (Interlocked.Exchange(ref restored, 1) == 0)
            {
                PopAndroidPanSoftInputMode();
            }
        };
#else
        return static () => { };
#endif
    }

#if ANDROID
    private static void PushAndroidPanSoftInputMode()
    {
        lock (AndroidSoftInputModeLock)
        {
            _androidPanSoftInputModeUsers++;
            if (_androidPanSoftInputModeUsers == 1)
            {
                SetAndroidSoftInputMode(AndroidSoftInputModeAdjust.Pan);
            }
        }
    }

    private static void PopAndroidPanSoftInputMode()
    {
        lock (AndroidSoftInputModeLock)
        {
            if (_androidPanSoftInputModeUsers <= 0)
            {
                _androidPanSoftInputModeUsers = 0;
                SetAndroidSoftInputMode(AndroidSoftInputModeAdjust.Resize);
                return;
            }

            _androidPanSoftInputModeUsers--;
            if (_androidPanSoftInputModeUsers == 0)
            {
                SetAndroidSoftInputMode(AndroidSoftInputModeAdjust.Resize);
            }
        }
    }

    private static void SetAndroidSoftInputMode(AndroidSoftInputModeAdjust mode)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => SetAndroidSoftInputMode(mode));
            return;
        }

        var app = Microsoft.Maui.Controls.Application.Current;
        if (app is null)
        {
            return;
        }

        // MainActivity owns the default Resize mode. Scoped overlays can ask Android to
        // pan the focused input into view and must restore Resize through the returned action.
        AndroidMauiApplication.SetWindowSoftInputModeAdjust(app, mode);
        foreach (var window in app.Windows)
        {
            AndroidMauiApplication.SetWindowSoftInputModeAdjust(window, mode);
        }
    }
#endif
}
