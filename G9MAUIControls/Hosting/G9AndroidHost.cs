namespace G9MAUIControls.Hosting;

/// <summary>
///     The three things this suite needs from the host's Android <c>MainActivity</c>, and the ONE
///     place a consumer wires them up.
///     <para>
///         <b>Why a hook and not a base activity.</b> Two features genuinely cannot work from inside
///         a library: the raw touch stream (only the activity sees
///         <c>Activity.DispatchTouchEvent</c>) and window-environment changes (only the activity sees
///         its own configuration and insets). Shipping a <c>G9MauiAppCompatActivity</c> base class
///         would work but force every consumer to inherit from it — impossible for anyone already
///         inheriting something else, which is common. A small static hook the activity <i>reports
///         into</i> composes instead of competing.
///     </para>
///     <para>
///         <b>Nothing here is mandatory.</b> Wire up nothing and the suite works; you lose exactly
///         two conveniences, both named below. There is no crash, no warning, no silent breakage of
///         anything else.
///     </para>
///     <example>
///         Add these four overrides to <c>Platforms/Android/MainActivity.cs</c>:
///         <code>
///         public class MainActivity : MauiAppCompatActivity
///         {
///             protected override void OnCreate(Bundle? savedInstanceState)
///             {
///                 base.OnCreate(savedInstanceState);
///                 G9AndroidHost.CurrentActivity = this;
///             }
///
///             public override bool DispatchTouchEvent(MotionEvent? e)
///             {
///                 G9AndroidHost.RaiseTouchDispatched(e);   // tap-outside-to-dismiss-keyboard
///                 return base.DispatchTouchEvent(e);
///             }
///
///             public override void OnConfigurationChanged(Configuration newConfig)
///             {
///                 base.OnConfigurationChanged(newConfig);
///                 G9AndroidHost.RaiseWindowEnvironmentChanged();  // safe-area re-measure
///             }
///
///             protected override void OnDestroy()
///             {
///                 if (ReferenceEquals(G9AndroidHost.CurrentActivity, this))
///                 {
///                     G9AndroidHost.CurrentActivity = null;
///                 }
///
///                 base.OnDestroy();
///             }
///         }
///         </code>
///     </example>
/// </summary>
public static class G9AndroidHost
{
    /// <summary>
    ///     Raised for every touch the activity dispatches. Required for <b>tap-outside-to-dismiss
    ///     -keyboard</b> on Android.
    ///     <para>
    ///         MAUI's own <c>ContentPage.HideSoftInputOnTapped</c> is not an alternative: its manager
    ///         only registers a page after that page raises <c>NavigatedTo</c>, which Shell-routed
    ///         pages under some navigation libraries never do — so the property silently does
    ///         nothing. Reading the activity's touch stream is the only reliable source.
    ///     </para>
    ///     <para>
    ///         The argument is the platform <c>MotionEvent</c>, boxed as <see cref="object" /> so this
    ///         type stays usable from shared code. Handlers cast it. Never raised on other platforms.
    ///     </para>
    /// </summary>
    public static event EventHandler<object?>? TouchDispatched;

    /// <summary>
    ///     Raised when the activity's window environment changes — rotation, a configuration change,
    ///     a display-cutout or inset update. Drives a safe-area re-measure, so without it a page's
    ///     camera-cutout padding is computed once and never corrected after a rotation.
    /// </summary>
    public static event EventHandler? WindowEnvironmentChanged;

    /// <summary>
    ///     The live activity, or <c>null</c>. Used to reach the input-method manager when dismissing
    ///     the soft keyboard. Boxed as <see cref="object" /> so this type stays usable from shared
    ///     code; the Android-only consumers cast it.
    /// </summary>
    public static object? CurrentActivity { get; set; }

    /// <summary>Reports a dispatched touch. Call from <c>Activity.DispatchTouchEvent</c>.</summary>
    /// <param name="motionEvent">The platform <c>MotionEvent</c>.</param>
    public static void RaiseTouchDispatched(object? motionEvent) =>
        TouchDispatched?.Invoke(null, motionEvent);

    /// <summary>
    ///     Reports a window-environment change. Call from <c>OnConfigurationChanged</c>, and from any
    ///     inset listener the app installs.
    /// </summary>
    public static void RaiseWindowEnvironmentChanged() =>
        WindowEnvironmentChanged?.Invoke(null, EventArgs.Empty);

    /// <summary>Drops the activity reference and every subscriber. Intended for tests.</summary>
    public static void Reset()
    {
        CurrentActivity = null;
        TouchDispatched = null;
        WindowEnvironmentChanged = null;
    }
}
