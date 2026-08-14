using G9MAUIControls.Popup;
using G9MAUIControls.Controls;
using G9MAUIControls.Helpers;
using G9MAUIControls.Localization;
using Maui.BindableProperty.Generator.Core;
#if ANDROID
using View = Android.Views.View;
using Android.OS;
using Android.Views;
#endif

#if IOS
using Microsoft.Maui.Controls.PlatformConfiguration;
using Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific;
using UIStatusBarAnimation = Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific.UIStatusBarAnimation;
using Application = Microsoft.Maui.Controls.Application;
// Microsoft.Maui.Controls.PlatformConfiguration.iOSSpecific also exposes a static
// VisualElement extensions class (VisualElement.SetIsHomeIndicatorAutoHidden etc.),
// which collides with Microsoft.Maui.Controls.VisualElement under iOS and makes the
// unqualified type ambiguous (CS0104). Alias VisualElement to the real control type
// so the field declaration + GetTemplateChild cast in the shared region resolve to
// the MAUI control on iOS just like they do on Android/Windows.
using VisualElement = Microsoft.Maui.Controls.VisualElement;
#endif

using G9MAUIControls.BottomSheet;
using G9MAUIControls.Theming;

namespace G9MAUIControls.Hosting;

public abstract partial class G9PageBase : ContentPage
{
    #region Fields And Properties

    private bool _templateReady;

    // G9BottomSheetHelper reads ContentHost to apply the "card recede" backdrop effect when
    // the primary bottom sheet expands past its configured threshold. Subclasses still get
    // protected read access; the helper consumes it through the internal-friendly setter.
    protected internal Grid ContentHost { get; private set; } = null!;

    private Grid _overlayHost = null!;
    private Grid _toastHost = null!;
    private Grid _devHost = null!;
    private G9PopupView _appG9Popup = null!;
    private G9SheetView _appG9BottomSheet = null!;

    // Page-loading overlay — captured from the ControlTemplate. Removed from the visual
    // tree (with a scale+fade animation) once the page's appearing work completes.
    // Typed as VisualElement (not View) to avoid the Android #if alias `View = Android.Views.View`
    // that shadows Microsoft.Maui.Controls.View in this file.
    private VisualElement? _pageLoadingOverlay;
    private bool _overlayDismissed;
    private CancellationTokenSource? _pageLoadingCts;

    // Set when the page reports it is visually ready — automatically when
    // OnAppearingAfterParentAsync completes, or manually when the page calls
    // ReleasePageLoadingOverlay. Re-created at the start of every appearing run. Completing it
    // lets the overlay dismiss (after the extra dismiss delay), bounded by the safety timeout.
    private TaskCompletionSource? _pageReadyTcs;

    // Completes once the page-loading overlay has fully animated out and been removed. Lets
    // callers defer work that must not run behind the splash overlay (e.g. MainPage gates its
    // startup-overlay tasks on this). One-shot — the overlay is never re-shown for a page.
    private readonly TaskCompletionSource _pageLoadingDismissedTcs =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Lifetime token for the tap-outside-to-dismiss-keyboard hook. Wired in
    // OnAppearing, disposed in OnDisappearing, so the platform listeners
    // (Android: MainActivity.TouchDispatched; iOS: UITapGestureRecognizer on the
    // key window) only live while the page is on screen.
    private IDisposable? _tapOutsideDismisser;

    // Safe Area Insets - Bindable properties for use in XAML
    [AutoBindable] private double _topSafeAreaInset;
    [AutoBindable] private double _bottomSafeAreaInset;
    [AutoBindable] private double _leftSafeAreaInset;
    [AutoBindable] private double _rightSafeAreaInset;

    // Computed property for bottom spacing including the page-owned tab bar (MainPage only).
    [AutoBindable] private double _bottomSafeAreaWithTabBar;

    #endregion

    #region Methods

    protected G9PageBase()
    {
        if (Application.Current?.Resources.TryGetValue("G9PageTemplate", out var template) == true &&
            template is ControlTemplate ct)
        {
            ControlTemplate = ct;
        }
        else
        {
            throw new ApplicationException(
                $"G9PageTemplate not found in resources or is of wrong type. Check {nameof(G9PageBase)} constructor.");
        }

        SafeAreaEdges = SafeAreaEdges.None;

        // Paint an opaque themed page background.
        //
        // NOT cosmetic. G9PageTemplate keeps an always-present BackdropHost BoxView directly beneath
        // ContentHost, painted G9BottomSheetSettings.BackdropCardColor (default BLACK) so the screen edges
        // stay dark while a bottom sheet recedes the page behind it. A page that leaves its own background
        // unset is TRANSPARENT over that, so the whole screen renders black regardless of theme — light
        // theme included, with dark body text on it and nothing wrong in any dictionary.
        //
        // In the source app every page content happened to be opaque, so this never surfaced. It surfaced
        // on the first consumer page built from scratch (LES-0019). Assigning it here means a consumer gets
        // the right thing by default and can still override — the palette push below respects that.
        ApplyThemedBackground();
        G9Palette.Current.PropertyChanged += OnPalettePropertyChanged;

        // Tap-outside-to-dismiss-keyboard. We bypass MAUI's built-in
        // ContentPage.HideSoftInputOnTapped because it gates registration on
        // the page's NavigatedTo event firing — and Nalu's navigation doesn't
        // raise that event for our Shell-managed pages, so the manager never
        // wires up the underlying gesture detector. The custom implementation
        // in TapOutsideKeyboardDismisser is hooked into our page lifecycle
        // (OnAppearing / OnDisappearing) and uses the platform touch hooks
        // we already control:
        //   • Android — MainActivity.TouchDispatched (Activity.DispatchTouchEvent)
        //   • iOS / Mac Catalyst — a UITapGestureRecognizer on the key window
        // No-op on Windows (WinUI), where users have a hardware keyboard.

#if ANDROID
        // Subscribe to Loaded event to apply Android insets when view is ready
        Loaded += OnPageLoaded;

        // Re-apply the safe-area insets whenever the Android window environment changes (cold start,
        // resume, window focus regain after a picker/camera intent, or a screen off→on). This keeps
        // the cutout-only insets correct across background/foreground and intent cycles. Unsubscribed
        // in OnHandlerChanging when the handler is torn down.
        G9AndroidHost.WindowEnvironmentChanged += OnAndroidWindowEnvironmentChanged;
#endif
    }

    /// <summary>
    ///     Captures template children, registers with <see cref="G9ModalHostRegistry" />, and paints the
    ///     bottom-sheet backdrop card color behind the page content.
    /// </summary>
    protected sealed override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        ContentHost = (GetTemplateChild("ContentHost") as Grid)!;
        _overlayHost = (GetTemplateChild("OverlayHost") as Grid)!;
        _toastHost = (GetTemplateChild("ToastHost") as Grid)!;
        _appG9Popup = (GetTemplateChild("G9PopupView") as G9PopupView)!;
        _appG9BottomSheet = (GetTemplateChild("G9SheetView") as G9SheetView)!;
        _pageLoadingOverlay = GetTemplateChild("PageLoadingHost") as VisualElement;

        // BackdropHost is a BoxView painted between the page background and ContentHost so the
        // bottom-sheet "card recede" effect doesn't expose the light theme background. The color
        // comes from G9BottomSheetSettings.BackdropCardColor; keep the lookup local — no consumer
        // outside this method needs the BoxView.
        if (GetTemplateChild("BackdropHost") is BoxView backdropHost)
        {
            backdropHost.Color = G9BottomSheetHelper.GetBackdropCardColor();
        }

        _devHost = (GetTemplateChild("DevHost") as Grid)!;

        // TWO registrations of the same page, on purpose.
        //
        // The internal one carries the popup and sheet CONTROL instances, which the suite's own helpers
        // need. The public one (G9OverlayHosts) exposes only a layer to mount into and the page for its
        // safe-area insets — enough for a satellite package or a consumer's own always-on overlay, and
        // nothing that lets external code reach into another overlay's control. See IG9OverlayHost for
        // why the boundary is drawn there rather than opened up with InternalsVisibleTo.
        //
        // Assigned together so the two views can never disagree about which page is current.
        G9ModalHostRegistry.Assign(this, _appG9Popup, _appG9BottomSheet, _overlayHost, _toastHost);
        G9OverlayHostRegistry.Set(this, _toastHost, _devHost, _overlayHost);

#if IOS || MACCATALYST
        ApplyAppleTemplateHostEdgeToEdge();
#endif

        G9SafeCommand.Run(
            OnApplyTemplateAfterParent,
            new G9SafeCommandOptions
            {
                Source = GetType().Name,
                EnableThrottle = false,
                ShowErrorG9Popup = true,
                PreventConcurrentExecution = false,
                ThrottleKey = $"{GetType().Name}.OnApplyTemplateAfterParent"
            });

        _templateReady = true;
    }

    /// <summary>
    ///     Hook for derived pages. Called after template children are captured.
    /// </summary>
    private Color? _lastAppliedThemedBackground;

    private void OnPalettePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (MainThread.IsMainThread)
        {
            ApplyThemedBackground();
            return;
        }

        MainThread.BeginInvokeOnMainThread(ApplyThemedBackground);
    }

    /// <summary>
    ///     Writes the palette's page background, unless the subclass has set its own.
    ///     <para>
    ///         Same override-detection as <see cref="G9ContentViewBase" />: compare the live value against
    ///         the last value THIS code wrote. Equal means nobody else touched it and a palette update is
    ///         safe; different means the subclass owns the property and we never write it again.
    ///     </para>
    /// </summary>
    private void ApplyThemedBackground()
    {
        var paletteColor = G9Palette.Current.Background;

        if (_lastAppliedThemedBackground is not null &&
            !ThemedBackgroundEquals(BackgroundColor, _lastAppliedThemedBackground))
        {
            return;
        }

        BackgroundColor = paletteColor;
        _lastAppliedThemedBackground = paletteColor;
    }

    private static bool ThemedBackgroundEquals(Color? a, Color? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return Math.Abs(a.Red - b.Red) < 0.001
            && Math.Abs(a.Green - b.Green) < 0.001
            && Math.Abs(a.Blue - b.Blue) < 0.001
            && Math.Abs(a.Alpha - b.Alpha) < 0.001;
    }

    protected virtual void OnApplyTemplateAfterParent()
    {
    }

    /// <summary>
    ///     Computes <see cref="BottomSafeAreaWithTabBar" /> from the platform-resolved insets.
    ///     Default returns just the platform bottom safe-area inset. Pages that own a managed
    ///     tab bar (currently only <c>MainPage</c>) override this to add the
    ///     reserved tab-bar height.
    /// </summary>
    protected virtual double ComputeBottomSafeAreaWithTabBar(double bottomInset)
    {
        return bottomInset;
    }

    /// <summary>
    ///     Invoked when the page is about to appear. Sets flow direction from the current culture
    ///     and subscribes to culture-change events; iOS additionally re-applies safe-area padding.
    /// </summary>
    protected sealed override void OnAppearing()
    {
        base.OnAppearing();

        FlowDirection = G9Culture.IsRtl
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        G9Culture.CultureChanged += G9CultureOnCultureChanged;

        // Wire the tap-outside detector. Idempotent — guards against
        // duplicate OnAppearing calls (modal push/pop scenarios).
        _tapOutsideDismisser?.Dispose();
        _tapOutsideDismisser = TapOutsideKeyboardDismisser.Attach(this);

#if IOS
        ApplyIOSPaddingAsync();
#endif

        G9SafeCommand.RunSafe(
            RunAppearingWithOverlayDismissAsync,
            new G9SafeCommandOptions
            {
                Source = GetType().Name,
                EnableThrottle = false,
                ShowErrorG9Popup = true,
                ThrottleKey = $"{GetType().Name}.OnAppearingAfterParent"
            });
    }

    private void G9CultureOnCultureChanged(object? sender, G9CultureEventArgs e)
    {
        FlowDirection = e.Culture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    }

    /// <summary>Hook for derived pages. Default is a no-op.</summary>
    protected virtual void OnAppearingAfterParent()
    {
    }

    /// <summary>
    ///     Async lifecycle hook for derived pages. Default delegates to
    ///     <see cref="OnAppearingAfterParent" />.
    /// </summary>
    protected virtual Task OnAppearingAfterParentAsync()
    {
        OnAppearingAfterParent();
        return Task.CompletedTask;
    }

    /// <summary>Unsubscribes from culture-change events and forwards to the async hook.</summary>
    protected sealed override void OnDisappearing()
    {
        G9Culture.CultureChanged -= G9CultureOnCultureChanged;

        // Tear down the tap-outside detector so the platform hook is unsubscribed
        // before the next page's OnAppearing wires up its own.
        _tapOutsideDismisser?.Dispose();
        _tapOutsideDismisser = null;

        // If the page navigates away while the overlay is still showing, cancel the
        // WaitAsync so the finally block in RunAppearingWithOverlayDismissAsync fires
        // and removes the overlay without playing the animation.
        _pageLoadingCts?.Cancel();

        G9SafeCommand.RunSafe(
            OnDisappearingAfterParentAsync,
            new G9SafeCommandOptions
            {
                Source = GetType().Name,
                EnableThrottle = false,
                ShowErrorG9Popup = true,
                ThrottleKey = $"{GetType().Name}.OnDisappearingAfterParent"
            });
        base.OnDisappearing();
    }

    /// <summary>Hook for derived pages. Default is a no-op.</summary>
    protected virtual void OnDisappearingAfterParent()
    {
    }

    /// <summary>
    ///     Async lifecycle hook for derived pages. Default delegates to
    ///     <see cref="OnDisappearingAfterParent" />.
    /// </summary>
    protected virtual Task OnDisappearingAfterParentAsync()
    {
        OnDisappearingAfterParent();
        return Task.CompletedTask;
    }

    /// <summary>
    ///     When <c>true</c>, a hardware / system back press is swallowed before any other handling
    ///     (popup, bottom sheet, in-app back). Default <c>false</c>.
    ///     <para>
    ///         Override on a page that must lock back for a while — a blocking startup overlay, a
    ///         biometric gate, a step the user cannot reverse out of.
    ///     </para>
    /// </summary>
    /// <remarks>
    ///     <b>Public, not internal, and both halves of that matter.</b> The suite has no back
    ///     dispatcher of its own — it cannot, because back arrives at a platform activity the library
    ///     cannot see — so the consuming app owns the dispatcher and this is the question it asks the
    ///     current page. An app therefore needs to both OVERRIDE it (from its own assembly) and CALL
    ///     it (from a coordinator that is not a page). <c>internal</c> allowed neither across the
    ///     package boundary, which made the whole back contract unreachable for the one kind of
    ///     consumer it was written for.
    /// </remarks>
    public virtual bool IsHardwareBackSuppressed => false;

    /// <summary>
    ///     In-app back handling for this page, evaluated after popups and bottom sheets have had
    ///     their turn. Return <c>true</c> to consume the press (the page navigated somewhere),
    ///     <c>false</c> to let the app's back coordinator fall through to its exit prompt. Default
    ///     <c>false</c> — a page with no internal back state is a dead-end.
    /// </summary>
    /// <remarks>Public for the same reason as <see cref="IsHardwareBackSuppressed" />.</remarks>
    public virtual bool TryHandleInAppBack() => false;

    /// <summary>
    ///     Detaches from the modal host registry when the handler is being torn down.
    /// </summary>
    protected sealed override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.NewHandler is null)
        {
            G9ModalHostRegistry.Remove(this);
            G9OverlayHostRegistry.Clear(this);
#if ANDROID
            G9AndroidHost.WindowEnvironmentChanged -= OnAndroidWindowEnvironmentChanged;
#endif
        }

        base.OnHandlerChanging(args);

        G9SafeCommand.Run(
            () => OnHandlerChangingAfterParent(args),
            new G9SafeCommandOptions
            {
                Source = GetType().Name,
                EnableThrottle = false,
                ShowErrorG9Popup = true,
                PreventConcurrentExecution = false,
                ThrottleKey = $"{GetType().Name}.OnHandlerChangingAfterParent"
            });
    }

    /// <summary>Hook for derived pages. Called after parent logic runs.</summary>
    protected virtual void OnHandlerChangingAfterParent(HandlerChangingEventArgs args)
    {
    }

    /// <summary>
    ///     Extra delay (ms) AFTER the page reports "ready" before the loading overlay animates
    ///     out, so the content painted behind it is visible the moment it is dismissed. Override
    ///     per page to tune the hand-off. Default 369.
    /// </summary>
    protected virtual int PageLoadingDismissDelayMs => 369;

    /// <summary>
    ///     Absolute cap (ms), measured from <c>OnAppearing</c>, after which the loading
    ///     overlay is dismissed no matter what — even if the page never reports ready (a manual
    ///     release that never fires, a hung await, …). Override to allow a slower first paint.
    ///     Default 5000.
    /// </summary>
    protected virtual int PageLoadingSafetyTimeoutMs => 5000;

    /// <summary>
    ///     When <c>false</c> (default) the overlay is released automatically as soon as
    ///     <see cref="OnAppearingAfterParentAsync" /> completes. When <c>true</c> the page owns
    ///     the timing and must call <see cref="ReleasePageLoadingOverlay" /> itself (still bounded
    ///     by <see cref="PageLoadingSafetyTimeoutMs" />). Use this when the visual "ready" moment
    ///     happens after the appearing hook returns — e.g. an async content view finishing its
    ///     first paint.
    /// </summary>
    protected virtual bool PageLoadingManualRelease => false;

    /// <summary>
    ///     Signals that the page is visually ready, so the loading overlay may dismiss (after
    ///     <see cref="PageLoadingDismissDelayMs" />). Thread-safe and idempotent: extra calls,
    ///     calls before the overlay exists, and calls after it is already gone are all no-ops.
    ///     Auto pages get this called for them; manual pages call it themselves. Also reachable
    ///     from a hosted <see cref="G9ContentViewBase" /> (via its
    ///     <c>ReleaseHostPageLoadingOverlay</c>) so the initial tab can own the moment.
    /// </summary>
    protected internal void ReleasePageLoadingOverlay()
    {
        _pageReadyTcs?.TrySetResult();
    }

    /// <summary>
    ///     Completes once the loading overlay has fully animated out and been removed from the
    ///     visual tree (or immediately, if it was never shown). Await this to defer work that must
    ///     not run while the splash overlay still covers the screen.
    /// </summary>
    protected Task PageLoadingOverlayDismissed => _pageLoadingDismissedTcs.Task;

    /// <summary>
    ///     Drives the page-loading overlay lifecycle around <see cref="OnAppearingAfterParentAsync" />:
    ///     run the appearing hook, wait for the page to become ready (auto or manual), apply the
    ///     extra dismiss delay, then dismiss. Every wait is bounded by
    ///     <see cref="PageLoadingSafetyTimeoutMs" /> so the overlay is ALWAYS removed.
    /// </summary>
    private async Task RunAppearingWithOverlayDismissAsync()
    {
        // The overlay is one-shot. On any later appearance (e.g. returning from a pushed modal)
        // just run the hook — there is no overlay left to dismiss.
        if (_overlayDismissed)
        {
            await OnAppearingAfterParentAsync().ConfigureAwait(true);
            return;
        }

        _pageReadyTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _pageLoadingCts = new CancellationTokenSource(
            TimeSpan.FromMilliseconds(Math.Max(0, PageLoadingSafetyTimeoutMs)));

        try
        {
            await OnAppearingAfterParentAsync()
                .WaitAsync(_pageLoadingCts.Token)
                .ConfigureAwait(true);

            // Auto pages: the appearing hook completing IS the ready signal. Manual pages release
            // themselves — here we only wait for that call (bounded by the safety timeout).
            if (!PageLoadingManualRelease)
            {
                ReleasePageLoadingOverlay();
            }

            await _pageReadyTcs.Task
                .WaitAsync(_pageLoadingCts.Token)
                .ConfigureAwait(true);

            await Task.Delay(Math.Max(0, PageLoadingDismissDelayMs), _pageLoadingCts.Token)
                .ConfigureAwait(true);
        }
        catch (System.OperationCanceledException)
        {
            // Either the safety timeout fired or OnDisappearing cancelled the token. Fall through
            // to the finally block — the overlay must always be dismissed.
            // Fully-qualified because Android imports Android.OS (via the #if ANDROID block at
            // the top of this file), which exposes Android.OS.OperationCanceledException and
            // makes the unqualified name ambiguous (CS0104) under the Android TFM.
        }
        finally
        {
            _pageLoadingCts?.Dispose();
            _pageLoadingCts = null;
            await DismissPageLoadingOverlayAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    ///     Plays a scale-and-fade exit animation on the overlay, then removes it completely from
    ///     the visual tree and completes <see cref="PageLoadingOverlayDismissed" />. One-shot:
    ///     subsequent calls are no-ops.
    /// </summary>
    private async Task DismissPageLoadingOverlayAsync()
    {
        if (_overlayDismissed)
        {
            return;
        }

        _overlayDismissed = true;

        var overlay = _pageLoadingOverlay;
        _pageLoadingOverlay = null;

        if (overlay is not null)
        {
            try
            {
                // Scale toward center + fade simultaneously for a polished "recede" exit.
                await Task.WhenAll(
                    overlay.ScaleToAsync(0.9, 200, Easing.CubicIn),
                    overlay.FadeToAsync(0, 200, Easing.CubicIn)).ConfigureAwait(true);
            }
            catch
            {
                // Animation may fail if the page handler is already being torn down — safe to ignore.
            }

            try
            {
                // Remove entirely from the visual tree so there is zero touch-passthrough risk.
                if (overlay.Parent is Grid parentGrid)
                {
                    parentGrid.Children.Remove(overlay);
                }
            }
            catch
            {
                // Parent may already be disconnected — safe to ignore.
            }
        }

        // Always release awaiters, even if the overlay was never captured from the template.
        _pageLoadingDismissedTcs.TrySetResult();
    }

    #endregion

    #region Platform-Specific Code

#if IOS || MACCATALYST
    /// <summary>
    ///     Opts every <see cref="G9PageTemplate" /> host container out of the platform safe area on
    ///     Apple platforms, so the page really is edge-to-edge.
    /// </summary>
    /// <remarks>
    ///     A G9-hosted page is full-screen by design on EVERY platform, and the safe area is applied
    ///     ONCE, manually: each page/content view self-insets through the bound <c>*SafeAreaInset</c>
    ///     properties published by <c>ApplyIOSPaddingAsync</c> (Android does the equivalent from the
    ///     activity, via <see cref="G9AndroidHost" />). <see cref="G9PageBase" /> already sets
    ///     <c>SafeAreaEdges</c> to <c>SafeAreaEdges.None</c> on the PAGE and on its <c>Content</c> —
    ///     but the template host grids sandwiched BETWEEN them
    ///     (<c>RootHost</c>, <c>ContentHost</c>, <c>OverlayHost</c>, <c>G9PopupHost</c>,
    ///     <c>ToastHost</c>, <c>DevHost</c>) are declared in <c>G9PageTemplate.xaml</c> with no
    ///     <c>SafeAreaEdges</c> at all, so they keep <c>SafeAreaRegions.Default</c> = "apply the
    ///     platform default", which on UIKit means inset for the notch / Dynamic Island / home
    ///     indicator. That produced two visible bugs on iOS that Android never had:
    ///     <list type="bullet">
    ///         <item>a white band across the top of every page (the page artwork stopped below the
    ///         status-bar area instead of painting under it), and</item>
    ///         <item>a double gap on full-screen bottom sheets — <c>G9BottomSheetHelper</c>
    ///         ALREADY reserves the top inset itself (<c>G9BottomSheetOptions.UseTopSafeAreaPadding</c>
    ///         → <c>ResolveTopSafeAreaInset</c> → <c>Page.TopSafeAreaInset</c>), so the platform inset
    ///         was being added on top of the manual one, at both the top AND the bottom.</item>
    ///     </list>
    ///     Neutralising the hosts restores the single-source-of-truth model. Apple-only on purpose:
    ///     Android/Windows resolve their insets through a completely different path (edge-to-edge
    ///     activity + consumed decor insets) and are known-good, so they are deliberately left
    ///     untouched. Verified on iOS; Mac Catalyst shares the same UIKit safe-area model and
    ///     template, so it is included, but it was not visually verified.
    /// </remarks>
    private void ApplyAppleTemplateHostEdgeToEdge()
    {
        // RootHost / G9PopupHost are not captured as fields by OnApplyTemplate, so look them up
        // here; the rest are already resolved above. Every one is a Grid in the template, but the
        // lookups stay defensive so a future template edit can't turn this into a hard crash.
        SetEdgeToEdge(GetTemplateChild("RootHost"));
        SetEdgeToEdge(ContentHost);
        SetEdgeToEdge(_overlayHost);
        SetEdgeToEdge(GetTemplateChild("G9PopupHost"));
        SetEdgeToEdge(_toastHost);
        SetEdgeToEdge(_devHost);

        static void SetEdgeToEdge(object? templateChild)
        {
            if (templateChild is Layout layout)
            {
                layout.SafeAreaEdges = SafeAreaEdges.None;
            }
        }
    }
#endif

#if IOS
    /// <summary>
    ///     Applies platform-specific padding and safe-area adjustments for iOS.
    /// </summary>
    private void ApplyIOSPaddingAsync()
    {
        // Also apply to root layout (very important for bottom sheet)
        if (Content is Layout layout)
        {
            layout.SafeAreaEdges = SafeAreaEdges.None;
        }

        G9SafeCommand.RunSafe(() =>
            {
                // Hide status bar text
                On<iOS>().SetPrefersStatusBarHidden(StatusBarHiddenMode.True);
                On<iOS>().SetPreferredStatusBarUpdateAnimation(UIStatusBarAnimation.Fade);

                var insets = On<iOS>().SafeAreaInsets();

                TopSafeAreaInset = insets.Top;
                BottomSafeAreaInset = insets.Bottom;
                LeftSafeAreaInset = insets.Left;
                RightSafeAreaInset = insets.Right;

                BottomSafeAreaWithTabBar = ComputeBottomSafeAreaWithTabBar(insets.Bottom);

                // No negative page padding. The page stays edge-to-edge (SafeAreaEdges.None) and
                // each content view self-insets via the bound *SafeAreaInset properties set above.
                // The map's intentional bottom bleed (edge-to-edge / hide the basemap's "Powered by
                // Esri" footer) is owned by ArcGisMapView on every platform — see
                // Common/Components/Map/AiGuide-MapComponent.md → "Bottom bleed". Do NOT reintroduce
                // a negative Padding/Margin here.
                Padding = new Thickness(0);
            },
            new G9SafeCommandOptions
            {
                Source = GetType().Name,
                ShowErrorG9Popup = true,
                EnableThrottle = false,
                PreventConcurrentExecution = false,
                DelayBeforeExecution = TimeSpan.FromMilliseconds(50),
                RunActionOnMainThread = true,
                ThrottleKey = $"{GetType().Name}.ApplyIOSPaddingAsync"
            });
    }
#endif

#if ANDROID
    /// <summary>
    ///     Handles the Loaded event on Android to apply safe-area insets after the handler is ready.
    /// </summary>
    private void OnPageLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnPageLoaded;
        ApplyAndroidSafeAreaInsets();
    }

    /// <summary>
    ///     Re-applies the safe-area insets when the Android window environment changes (resume /
    ///     window-focus regain after a picker/camera intent or a screen off→on), raised by
    ///     <see cref="G9AndroidHost.WindowEnvironmentChanged" />. Marshalled to the main thread;
    ///     <see cref="ApplyAndroidSafeAreaInsets" /> is itself fully guarded so a handler running
    ///     mid-teardown can never throw into the activity's immersive pass.
    /// </summary>
    private void OnAndroidWindowEnvironmentChanged(object? sender, EventArgs e)
    {
        if (MainThread.IsMainThread)
        {
            ApplyAndroidSafeAreaInsets();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(ApplyAndroidSafeAreaInsets);
        }
    }

    /// <summary>
    ///     Resolves the page safe-area insets from the DISPLAY CUTOUT ONLY (camera notch / punch-hole)
    ///     and publishes them to the bound <c>*SafeAreaInset</c> properties.
    ///     <para>
    ///         The app is permanently full-screen / immersive — the host's activity hides the
    ///         status and navigation bars — so the only real inset is the display cutout; the system
    ///         bars must NOT contribute. Reading the system-bar insets here is what produced the white
    ///         "bands": on some OEM ROMs (e.g. Doogee, API 29) the legacy <c>RootWindowInsets</c> keep
    ///         reporting the hidden status (~28dp) and navigation (~48dp) bars, so pages reserved space
    ///         for bars that are not on screen. Cutout-only keeps the app edge-to-edge by default; a
    ///         page opts into camera avoidance by binding <see cref="TopSafeAreaInset" /> (e.g. the Map
    ///         farm-selector row, the Tasks header row).
    ///     </para>
    ///     <para>
    ///         Runs on load AND on every resume / focus regain via
    ///         <see cref="OnAndroidWindowEnvironmentChanged" />. Fully guarded so it never throws, and
    ///         idempotent — writing an unchanged bindable inset raises no PropertyChanged — so the
    ///         re-runs are cheap and side-effect-free.
    ///     </para>
    /// </summary>
    private void ApplyAndroidSafeAreaInsets()
    {
        try
        {
            if (Handler?.PlatformView is not View handler) return;

            ClearAndroidNativeRootPadding(handler);
            InvalidateAndroidEdgeToEdgeLayout(handler);

            if (Content is Layout contentLayout)
            {
                contentLayout.SafeAreaEdges = SafeAreaEdges.None;
            }

            var windowInsets = handler.RootWindowInsets;
            if (windowInsets is null) return;

            var density = handler.Context?.Resources?.DisplayMetrics?.Density ?? 1;
            if (density <= 0) density = 1;

            double top = 0, bottom = 0, left = 0, right = 0;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.R)
            {
#pragma warning disable CA1416
                // Cutout only — never SystemBars(): the bars are hidden, only the camera is physical.
                var insets = windowInsets.GetInsets(WindowInsets.Type.DisplayCutout());
                top = insets.Top / density;
                bottom = insets.Bottom / density;
                left = insets.Left / density;
                right = insets.Right / density;
#pragma warning restore CA1416
            }
            else if (Build.VERSION.SdkInt >= BuildVersionCodes.P)
            {
                // API 28/29: DisplayCutout exists but GetInsets(type) does not. Read the cutout's safe
                // insets directly and IGNORE SystemWindowInset* (the phantom hidden-bar insets that
                // are the root cause of the bands on this OEM ROM).
#pragma warning disable CA1416
                var cutout = windowInsets.DisplayCutout;
                if (cutout is not null)
                {
                    top = cutout.SafeInsetTop / density;
                    bottom = cutout.SafeInsetBottom / density;
                    left = cutout.SafeInsetLeft / density;
                    right = cutout.SafeInsetRight / density;
                }
#pragma warning restore CA1416
            }
            // API < 28: no DisplayCutout API and no cutout hardware -> all zero (edge-to-edge).

            TopSafeAreaInset = top;
            BottomSafeAreaInset = bottom;
            LeftSafeAreaInset = left;
            RightSafeAreaInset = right;

            BottomSafeAreaWithTabBar = ComputeBottomSafeAreaWithTabBar(bottom);

            ClearAndroidNativeRootPadding(handler);
            InvalidateAndroidEdgeToEdgeLayout(handler);
        }
        catch
        {
            // Never let an inset read throw into the page lifecycle or the activity's immersive pass.
        }
    }

    /// <summary>
    ///     Clears native Android padding that MAUI can re-apply to the page/content root after a
    ///     child activity returns. The app publishes safe-area values through bindable properties;
    ///     the native page root itself must stay edge-to-edge.
    /// </summary>
    private void ClearAndroidNativeRootPadding(View pageHandler)
    {
        ClearAndroidNativePadding(pageHandler);

        if (Content?.Handler?.PlatformView is View contentView)
        {
            ClearAndroidNativePadding(contentView);
        }
    }

    private static void ClearAndroidNativePadding(View view)
    {
        if (view.PaddingTop == 0 &&
            view.PaddingBottom == 0 &&
            view.PaddingLeft == 0 &&
            view.PaddingRight == 0)
        {
            return;
        }

        view.SetPadding(0, 0, 0, 0);
        view.RequestLayout();
    }

    private void InvalidateAndroidEdgeToEdgeLayout(View pageHandler)
    {
        try
        {
            InvalidateMeasure();

            if (Content is VisualElement content)
            {
                content.InvalidateMeasure();
            }

            if (_templateReady)
            {
                ContentHost.InvalidateMeasure();
            }

            pageHandler.RequestLayout();
            RequestAndroidLayoutTree(pageHandler, maxDepth: 4);

            if (Content?.Handler?.PlatformView is View contentView)
            {
                RequestAndroidLayoutTree(contentView, maxDepth: 5);
            }

            if (_templateReady && ContentHost.Handler?.PlatformView is View contentHostView)
            {
                RequestAndroidLayoutTree(contentHostView, maxDepth: 5);
            }
        }
        catch
        {
            // Layout invalidation is best-effort during handler teardown.
        }
    }

    private static void RequestAndroidLayoutTree(View view, int maxDepth)
    {
        var visited = 0;
        RequestAndroidLayoutTree(view, 0, maxDepth, ref visited);
    }

    private static void RequestAndroidLayoutTree(View view, int depth, int maxDepth, ref int visited)
    {
        if (visited >= 80 || depth > maxDepth)
        {
            return;
        }

        visited++;
        view.RequestLayout();

        if (view is not ViewGroup group)
        {
            return;
        }

        for (var i = 0; i < group.ChildCount && visited < 80; i++)
        {
            if (group.GetChildAt(i) is { } child)
            {
                RequestAndroidLayoutTree(child, depth + 1, maxDepth, ref visited);
            }
        }
    }
#endif

    #endregion
}
