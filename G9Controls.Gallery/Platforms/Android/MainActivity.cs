using Android.App;
using Android.Content.PM;
using Android.Content.Res;
using Android.OS;
using Android.Views;
using G9MAUIControls.Hosting;

namespace G9Controls.Gallery;

/// <summary>
///     The reference implementation of the four <see cref="G9AndroidHost" /> hooks.
///     <para>
///         The suite deliberately does not install an activity callback of its own — a library that reaches
///         into the host activity behind the app's back is impossible to reason about, and impossible to opt
///         out of. Instead the host publishes four call sites and the app makes them. Everything below is
///         required for correct behaviour on Android; each line names what breaks without it.
///     </para>
/// </summary>
[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize
                         | ConfigChanges.Orientation
                         | ConfigChanges.UiMode
                         | ConfigChanges.ScreenLayout
                         | ConfigChanges.SmallestScreenSize
                         | ConfigChanges.Density)]
public sealed class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Without this the suite cannot reach the input-method manager, so programmatic keyboard
        // dismissal (sheet close, popup submit, chip selection) silently does nothing.
        G9AndroidHost.CurrentActivity = this;
    }

    /// <summary>
    ///     Feeds the activity's touch stream to the suite. Without it, tapping outside a focused field does
    ///     not dismiss the keyboard — see the remarks on
    ///     <see cref="G9AndroidHost.TouchDispatched" /> for why MAUI's own
    ///     <c>HideSoftInputOnTapped</c> is not a substitute.
    /// </summary>
    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        G9AndroidHost.RaiseTouchDispatched(e);
        return base.DispatchTouchEvent(e);
    }

    /// <summary>
    ///     Without this, safe-area padding is computed once at first layout and never corrected — so after a
    ///     rotation a page's content sits under the display cutout or the navigation bar.
    /// </summary>
    public override void OnConfigurationChanged(Configuration newConfig)
    {
        base.OnConfigurationChanged(newConfig);
        G9AndroidHost.RaiseWindowEnvironmentChanged();
    }

    protected override void OnDestroy()
    {
        // Reference-equality guarded: with SingleTop plus a configuration change, a new activity can be
        // created before the old one is destroyed, and an unguarded clear would null out the LIVE activity.
        if (ReferenceEquals(G9AndroidHost.CurrentActivity, this))
        {
            G9AndroidHost.CurrentActivity = null;
        }

        base.OnDestroy();
    }
}
