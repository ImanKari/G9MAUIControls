using G9MAUIControls.Controls;
using G9MAUIControls.Helpers;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using System.ComponentModel;

namespace G9MAUIControls.Hosting;

/// <summary>
///     Base class for the former tab pages now hosted as content views inside
///     <c>MainPage</c>. Provides:
///     <list type="bullet">
///         <item>One-way mirrored safe-area properties from the owning <c>G9PageBase</c>.</item>
///         <item>Activate / deactivate lifecycle hooks driven by <c>MainPage</c>.</item>
///         <item>Per-activation <see cref="CancellationToken" /> for guarding async work.</item>
///         <item>Optional hardware-back hook routed by the host page.</item>
///     </list>
///     Designed to keep the visual tree alive across tab switches; <see cref="OnDeactivated" /> never
///     unloads the view, only cancels its activation token.
/// </summary>
public abstract partial class G9ContentViewBase : ContentView
{
    #region Fields and properties

    private CancellationTokenSource? _activationCts;
    private G9PageBase? _hostPage;
    private bool _hasFiredFirstActivation;
    private bool _cultureSubscribed;

    /// <summary>True between an activation and the next deactivation.</summary>
    public bool IsActive { get; private set; }

    /// <summary>
    ///     When true, this content view asks the app to pause its background work for as long as the
    ///     view is active — because the view itself exposes a user-initiated version of that work and
    ///     the two must not race.
    ///     <para>
    ///         The canonical case is a screen with a "Sync now" button: a silent automatic sync
    ///         starting underneath the user's own sync is confusing at best and conflicting at worst.
    ///         The view says <i>that it wants a pause</i>; what a pause means is
    ///         <see cref="BackgroundWorkSuppressionFactory" />'s business.
    ///     </para>
    /// </summary>
    protected virtual bool SuppressesBackgroundWork => false;

    /// <summary>
    ///     Supplies the pause for views that set <see cref="SuppressesBackgroundWork" />. Called on
    ///     activation; the returned <see cref="IDisposable" /> is disposed on deactivation, so a
    ///     scope object is the natural shape.
    ///     <para>
    ///         Left null (the default) nothing happens, which is correct for an app with no
    ///         background work to pause. Register once at startup:
    ///     </para>
    ///     <example>
    ///         <code>
    ///         G9ContentViewBase.BackgroundWorkSuppressionFactory = () => syncScheduler.Pause();
    ///         </code>
    ///     </example>
    /// </summary>
    public static Func<IDisposable?>? BackgroundWorkSuppressionFactory { get; set; }

    // Holds the suppression between Activate and Deactivate (null when not held).
    private IDisposable? _backgroundWorkSuppression;

    // Mirrored from the owning G9PageBase. Setters are protected internal so the host MainPage
    // can push values when its own insets change. Defaults are 0 — a content view that is not
    // hosted by an G9PageBase still renders without crashing.
    [AutoBindable] private double _topSafeAreaInset;
    [AutoBindable] private double _bottomSafeAreaInset;
    [AutoBindable] private double _leftSafeAreaInset;
    [AutoBindable] private double _rightSafeAreaInset;
    [AutoBindable] private double _bottomSafeAreaWithTabBar;

    #endregion

    #region Construction

    protected G9ContentViewBase()
    {
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        SafeAreaEdges = SafeAreaEdges.None;

        // Default themed background. G9PageTemplate paints the area behind ContentHost
        // black to support the bottom-sheet recede effect, so every content view must own
        // an opaque background or that black bleeds through.
        ApplyThemedBackground();
        G9Palette.Current.PropertyChanged += OnG9PaletteChanged;
    }

    /// <inheritdoc />
    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);

        if (args.NewHandler is null)
        {
            G9Palette.Current.PropertyChanged -= OnG9PaletteChanged;
        }
    }

    private void OnG9PaletteChanged(object? sender, PropertyChangedEventArgs e)
    {
        // A theme switch flushes a SINGLE PropertyChanged(string.Empty) "everything changed"
        // event (G9Palette.EndBatchUpdate), NOT a per-property "Background" event. Treat an
        // empty/null name as a refresh trigger too — otherwise the content-view background is
        // painted once at construction and is NEVER re-applied on a live Light<->Dark switch, so
        // the main page background stays on the old theme while cards/markup DO update (the
        // {themeManager:ThemeColor} markup already handles the empty-name flush). The targeted
        // "Background" name is still honored for the rare single-property change outside a batch.
        if (!string.IsNullOrEmpty(e.PropertyName) && e.PropertyName != nameof(G9Palette.Background))
        {
            return;
        }

        if (MainThread.IsMainThread)
        {
            ApplyThemedBackground();
            return;
        }

        MainThread.BeginInvokeOnMainThread(ApplyThemedBackground);
    }

    private Color? _lastAppliedThemedBackground;

    private void ApplyThemedBackground()
    {
        var paletteColor = G9Palette.Current.Background;

        // Only push the palette color when the subclass hasn't overridden BackgroundColor with
        // its own value. We compare the current BackgroundColor against the last value we
        // wrote — if they match, the subclass hasn't touched it and a palette update is safe.
        // If they differ, the subclass set its own color and we leave it alone going forward.
        if (_lastAppliedThemedBackground is not null &&
            !ColorsEqual(BackgroundColor, _lastAppliedThemedBackground))
        {
            return;
        }

        BackgroundColor = paletteColor;
        _lastAppliedThemedBackground = paletteColor;
    }

    private static bool ColorsEqual(Color? a, Color? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        return a.Red == b.Red && a.Green == b.Green && a.Blue == b.Blue && a.Alpha == b.Alpha;
    }

    #endregion

    #region Host wiring

    /// <inheritdoc />
    protected override void OnParentSet()
    {
        base.OnParentSet();

        if (Parent is null)
        {
            DetachHostPage();
            return;
        }

        AttachHostPage(FindHostPage());
    }

    private G9PageBase? FindHostPage()
    {
        Element? element = Parent;
        while (element is not null)
        {
            if (element is G9PageBase appPage)
            {
                return appPage;
            }

            element = element.Parent;
        }

        return null;
    }

    private void AttachHostPage(G9PageBase? page)
    {
        if (ReferenceEquals(_hostPage, page))
        {
            return;
        }

        DetachHostPage();
        _hostPage = page;

        if (page is null)
        {
            return;
        }

        // Initial copy.
        TopSafeAreaInset = page.TopSafeAreaInset;
        BottomSafeAreaInset = page.BottomSafeAreaInset;
        LeftSafeAreaInset = page.LeftSafeAreaInset;
        RightSafeAreaInset = page.RightSafeAreaInset;
        BottomSafeAreaWithTabBar = page.BottomSafeAreaWithTabBar;

        page.PropertyChanged += OnHostPagePropertyChanged;
    }

    private void DetachHostPage()
    {
        if (_hostPage is null)
        {
            return;
        }

        _hostPage.PropertyChanged -= OnHostPagePropertyChanged;
        _hostPage = null;
    }

    private void OnHostPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not G9PageBase page)
        {
            return;
        }

        switch (e.PropertyName)
        {
            case nameof(G9PageBase.TopSafeAreaInset):
                TopSafeAreaInset = page.TopSafeAreaInset;
                break;
            case nameof(G9PageBase.BottomSafeAreaInset):
                BottomSafeAreaInset = page.BottomSafeAreaInset;
                break;
            case nameof(G9PageBase.LeftSafeAreaInset):
                LeftSafeAreaInset = page.LeftSafeAreaInset;
                break;
            case nameof(G9PageBase.RightSafeAreaInset):
                RightSafeAreaInset = page.RightSafeAreaInset;
                break;
            case nameof(G9PageBase.BottomSafeAreaWithTabBar):
                BottomSafeAreaWithTabBar = page.BottomSafeAreaWithTabBar;
                break;
        }
    }

    /// <summary>
    ///     Releases the host page's loading overlay, if any. A content view that owns the page's
    ///     "first visible" moment — e.g. the Map tab once its first frame is ready — calls this so
    ///     <c>G9PageBase</c> can dismiss the splash overlay at exactly the right time.
    ///     No-op when this view has no host page or the overlay is already gone.
    /// </summary>
    protected void ReleaseHostPageLoadingOverlay()
    {
        _hostPage?.ReleasePageLoadingOverlay();
    }

    #endregion

    #region Lifecycle (driven by MainPage)

    /// <summary>
    ///     Called by the hosting page when this content view becomes the active tab.
    ///     First activation triggers <see cref="OnFirstActivatedAsync" />; every activation
    ///     triggers <see cref="OnActivatedAsync" />. Both run under a fresh cancellation token
    ///     that is cancelled in <see cref="Deactivate" />.
    ///     <para>
    ///         <b>Public because the HOST drives it, and the host lives in the consumer's app.</b> In a
    ///         single-page architecture one page owns a tab bar and a set of content views and calls
    ///         Activate/Deactivate as the selection moves. These were <c>internal</c> until a consumer tried
    ///         to build exactly that and found the lifecycle unreachable from outside the assembly — the same
    ///         defect shape as LES-0011: a public base class whose driving methods are internal advertises an
    ///         extension point that cannot be extended. Idempotent, so a double call is harmless.
    ///     </para>
    /// </summary>
    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        IsVisible = true;

        if (SuppressesBackgroundWork && _backgroundWorkSuppression is null)
        {
            _backgroundWorkSuppression = BackgroundWorkSuppressionFactory?.Invoke();
        }
        SubscribeCultureChanged();
        ApplyFlowDirectionFromCulture();

        _activationCts?.Dispose();
        _activationCts = new CancellationTokenSource();
        var token = _activationCts.Token;

        G9SafeCommand.RunSafe(
            () => RunActivationAsync(token),
            new G9SafeCommandOptions
            {
                Source = GetType().Name,
                EnableThrottle = false,
                ShowErrorG9Popup = true,
                PreventConcurrentExecution = false,
                RunActionOnMainThread = true,
                ThrottleKey = $"{GetType().Name}.Activate"
            });
    }

    private async Task RunActivationAsync(CancellationToken token)
    {
        if (!_hasFiredFirstActivation)
        {
            _hasFiredFirstActivation = true;
            await OnFirstActivatedAsync(token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
            {
                return;
            }
        }

        await OnActivatedAsync(token).ConfigureAwait(true);
    }

    /// <summary>
    ///     Called by the hosting page when another tab takes over. Cancels the activation token,
    ///     hides the view, and unsubscribes culture-change events.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;

        _backgroundWorkSuppression?.Dispose();
        _backgroundWorkSuppression = null;

        UnsubscribeCultureChanged();

        var cts = _activationCts;
        _activationCts = null;
        if (cts is not null)
        {
            try
            {
                cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already disposed
            }

            cts.Dispose();
        }

        G9SafeCommand.Run(
            OnDeactivated,
            new G9SafeCommandOptions
            {
                Source = GetType().Name,
                EnableThrottle = false,
                ShowErrorG9Popup = true,
                PreventConcurrentExecution = false,
                ThrottleKey = $"{GetType().Name}.Deactivate"
            });

        IsVisible = false;
    }

    /// <summary>
    ///     Forwarded from <c>MainPage</c> when the OS back button is pressed
    ///     and no bottom sheet handled it. Return <c>true</c> to consume the event.
    /// </summary>
    public virtual bool OnHardwareBackPressed()
    {
        return false;
    }

    /// <summary>
    ///     Runs the first time this content view is activated. Use for one-shot heavy
    ///     initialization (DB-driven view-model setup, map controller hookup, etc).
    ///     Default does nothing.
    /// </summary>
    protected virtual Task OnFirstActivatedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Runs every time this content view becomes the active tab. Use for refreshing
    ///     state that depends on what other tabs/modals may have done while away.
    ///     Default does nothing.
    /// </summary>
    protected virtual Task OnActivatedAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    ///     Runs when this content view is no longer the active tab. The activation token
    ///     has already been cancelled. Default does nothing.
    /// </summary>
    protected virtual void OnDeactivated()
    {
    }

    #endregion

    #region Culture

    private void SubscribeCultureChanged()
    {
        if (_cultureSubscribed)
        {
            return;
        }

        G9Culture.CultureChanged += OnCultureChanged;
        _cultureSubscribed = true;
    }

    private void UnsubscribeCultureChanged()
    {
        if (!_cultureSubscribed)
        {
            return;
        }

        G9Culture.CultureChanged -= OnCultureChanged;
        _cultureSubscribed = false;
    }

    private void OnCultureChanged(object? sender, G9CultureEventArgs e)
    {
        FlowDirection = e.Culture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    }

    private void ApplyFlowDirectionFromCulture()
    {
        FlowDirection = G9Culture.IsRtl
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    }

    #endregion
}
