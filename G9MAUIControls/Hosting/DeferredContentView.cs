using G9MAUIControls.BottomSheet;
using G9MAUIControls.Controls;
using G9MAUIControls.Theming;

namespace G9MAUIControls.Hosting;

/// <summary>
///     A content wrapper that defers the display of its content until after the hosting
///     page/modal/bottom-sheet open animation finishes. While deferred, a centered spinner
///     placeholder fills the host so the user sees motion (not the host body) during the wait.
/// </summary>
/// <remarks>
///     <para>Two modes:</para>
///     <list type="bullet">
///         <item>
///             <b>Factory mode</b> (preferred): set <see cref="ContentFactory" />. The view is
///             not constructed until after the host's open animation, eliminating UI-thread
///             freeze from heavy <c>InitializeComponent()</c> calls during the animation.
///         </item>
///         <item>
///             <b>Pre-built mode</b>: set <see cref="DeferredContent" /> to an already-built view.
///             Construction has already happened; only the layout/measure pass is deferred.
///         </item>
///     </list>
/// </remarks>
public sealed class DeferredContentView : ContentView
{
    /// <summary>
    ///     Default delay (ms) between the wrapper being attached and the content being built.
    ///     Long enough to cover the host's open animation on Android (~250ms) plus enough
    ///     time for the spinner to be perceived by the user.
    /// </summary>
    public const int DefaultLoadDelayMs = G9LayoutMetrics.DeferredContentLoadDelayMs;

    private readonly Lock _lock = new();
    private bool _isContentLoaded;
    private CancellationTokenSource? _loadCts;
    private bool _loadedHandlerAttached;

    public DeferredContentView()
    {
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;
        SafeAreaEdges = SafeAreaEdges.None;
    }

    /// <summary>
    ///     Factory invoked on the UI thread to create the actual view after <see cref="LoadDelayMs" />.
    ///     Preferred over <see cref="DeferredContent" /> when construction is expensive.
    /// </summary>
    public Func<View>? ContentFactory { get; set; }

    /// <summary>
    ///     An already-constructed view to swap in after <see cref="LoadDelayMs" />.
    ///     Use <see cref="ContentFactory" /> instead when construction is expensive.
    /// </summary>
    public View? DeferredContent { get; set; }

    /// <summary>
    ///     Optional callback invoked on the UI thread immediately after the content is created
    ///     (factory mode) or just before it is swapped in (pre-built mode). Use this to wire
    ///     events, set data, or extract toolbar items from the newly-built view.
    /// </summary>
    public Action<View>? OnContentCreated { get; set; }

    /// <summary>
    ///     Optional custom loading placeholder. When null, a default centered-spinner view is used.
    /// </summary>
    public View? LoadingView { get; set; }

    /// <summary>
    ///     When <c>true</c> (default), <see cref="LoadContentAsync" /> is automatically triggered
    ///     on the <c>Loaded</c> event.
    /// </summary>
    public bool AutoLoad { get; set; } = true;

    /// <summary>
    ///     When <c>true</c>, the freshly-built content is revealed with a crossfade instead of an
    ///     instant swap: the loading spinner stays visible while the new tree lays out (hidden
    ///     behind <c>Opacity 0</c>), then the content fades in and the spinner fades out together.
    ///     This masks the piecemeal native realization of heavy view trees (e.g. the date-picker's
    ///     drum columns) so the content appears as one settled unit rather than popping in part by
    ///     part. Default <c>false</c> keeps the instant swap used everywhere else, so this is opt-in
    ///     and carries no behavioural change for existing consumers. Sizing is unaffected: a
    ///     transparent grid hosts both children and an <c>Opacity 0</c> child still measures at its
    ///     full size, so a fit-to-content host grows to the real content height during the fade.
    /// </summary>
    public bool FadeContentIn { get; set; }

    /// <summary>
    ///     Settle-window CAP (ms) — the window itself is <see cref="RevealSettleFrames" /> drawn frames
    ///     after the content's first layout, and this is the longest it may ever take. It is how long
    ///     at most the OPAQUE loading placeholder stays over the
    ///     already-parented content before fading away, so native realization AND icon-font
    ///     application finish while covered (the first-frame "tofu rectangle" race). Only used
    ///     when <see cref="FadeContentIn" /> is <c>true</c>.
    /// </summary>
    public int FadeRevealDelayMs { get; set; } = 220;

    /// <summary>
    ///     Drawn frames the covered content is given, after its first layout, before the placeholder
    ///     starts to fade. Bounded by <see cref="FadeRevealDelayMs" />, which is the hard cap on the
    ///     whole settle window. Only used when <see cref="FadeContentIn" /> is <c>true</c>.
    /// </summary>
    public int RevealSettleFrames { get; set; } = 2;

    /// <summary>
    ///     Delay (ms) between <c>Loaded</c> firing and the deferred content being built/swapped in.
    ///     Defaults to <see cref="DefaultLoadDelayMs" />. The delay must cover the host's open
    ///     animation; otherwise the heavy factory call blocks the animation and the spinner never paints.
    /// </summary>
    public int LoadDelayMs { get; set; } = DefaultLoadDelayMs;

    /// <summary>
    ///     Whether the deferred load has been STARTED/claimed. NOTE: this flips true at the
    ///     BEGINNING of <see cref="LoadContentAsync" /> (while the spinner is still showing) — it
    ///     is a re-entrancy latch, not a visual-state signal. For "is the final content actually
    ///     on screen" use <see cref="IsRevealSettled" />.
    /// </summary>
    public bool IsContentLoaded
    {
        get
        {
            lock (_lock)
            {
                return _isContentLoaded;
            }
        }
    }

    /// <summary>
    ///     True once the FINAL content is the sole visual (instant swap done, or the covered
    ///     reveal settled — spinner removed from the reveal host). While false, whatever measures
    ///     is the loading placeholder — or the placeholder stacked over the content — so sizing
    ///     engines must not treat measurements as the content's real size (see
    ///     <c>G9BottomSheetHelper.ContainsLoadingDeferredContent</c>).
    /// </summary>
    public bool IsRevealSettled { get; private set; }

    public event EventHandler? ContentLoaded;

    /// <summary>
    ///     Raised when <see cref="IsRevealSettled" /> flips true — the reveal is over and the
    ///     content is the sole visual. Hosts that HOLD their size through the loading window (the
    ///     bottom-sheet fit engine) re-measure on this signal: since the reveal no longer
    ///     re-parents the content (no native re-attach), settling produces no layout invalidation
    ///     of its own, so without this event the final corrective resize would never run.
    /// </summary>
    public event EventHandler? RevealSettled;

    /// <inheritdoc />
    protected override void OnParentSet()
    {
        base.OnParentSet();

        if (Parent is null)
        {
            CancelPendingLoad();
            DetachLoadedHandler();
            return;
        }

        if (!_isContentLoaded && Content is null)
        {
            Content = LoadingView ?? CreateDefaultLoadingView();
        }

        if (AutoLoad && !_loadedHandlerAttached)
        {
            _loadedHandlerAttached = true;
            Loaded += OnLoaded;
        }
    }

    /// <inheritdoc />
    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        // Propagate BindingContext to the pre-built deferred content ONLY when it doesn't
        // already own one. Pre-built views may carry their own ViewModel and we must not
        // clobber it with the wrapper's inherited context.
        if (DeferredContent is not null && DeferredContent.BindingContext is null)
        {
            DeferredContent.BindingContext = BindingContext;
        }
    }

    /// <summary>
    ///     Waits <see cref="LoadDelayMs" /> for the host's open animation to finish, then swaps
    ///     the placeholder for the real content (built from <see cref="ContentFactory" /> if set,
    ///     otherwise <see cref="DeferredContent" />).
    /// </summary>
    public async Task LoadContentAsync(CancellationToken ct = default)
    {
        CancellationTokenSource linkedCts;
        lock (_lock)
        {
            if (_isContentLoaded)
            {
                return;
            }

            _isContentLoaded = true;
            _loadCts = new CancellationTokenSource();
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_loadCts.Token, ct);
        }

        // A deferred build is the classic "the screen looks ready but is not" window: the sheet is
        // open, the spinner is up, and the real content has not been constructed yet. Reported as
        // ONE activity spanning build + reveal, because a host waiting for quiescence cares about
        // when the content is actually there, not about the internal phases.
        using var buildActivity = Helpers.G9Diagnostics.Activity("deferred:" + (AutomationId ?? GetType().Name));

        IsRevealSettled = false;


        try
        {
            if (LoadDelayMs > 0)
            {
                await Task.Delay(LoadDelayMs, linkedCts.Token).ConfigureAwait(true);
            }
            else
            {
                await Task.Yield();
            }

            if (linkedCts.Token.IsCancellationRequested)
            {
                // Same as the OperationCanceledException path below: a cancelled load must be
                // repeatable. Returning with the latch still set meant a view that was detached
                // during its delay and later re-attached never loaded at all.
                lock (_lock)
                {
                    _isContentLoaded = false;
                }

                return;
            }


            if (MainThread.IsMainThread)
            {
                SwapContent();
            }
            else
            {
                await MainThread.InvokeOnMainThreadAsync(SwapContent);
            }
        }
        catch (OperationCanceledException)
        {

            // View was detached or caller cancelled — allow a re-load if reattached.
            lock (_lock)
            {
                _isContentLoaded = false;
            }
        }
        catch (Exception)
        {
            // ⛔ The factory (or a created-callback) threw. This used to be caught by nothing: the
            // latch stayed set, so the body could never be built again, the placeholder stayed up
            // forever, and — because the load is fire-and-forget — the exception was never
            // observed, so nothing was logged either. A sheet that spins for ever with no trace is
            // the worst failure this view can have. Release the latch, stop claiming to be loading
            // (so a host holding its size for us lets go), and RETHROW: every caller runs this
            // through G9SafeCommand, which logs it and tells the user.
            lock (_lock)
            {
                _isContentLoaded = false;
            }

            IsRevealSettled = true;
            RevealSettled?.Invoke(this, EventArgs.Empty);
            throw;
        }
        finally
        {
            linkedCts.Dispose();
            lock (_lock)
            {
                _loadCts?.Dispose();
                _loadCts = null;
            }
        }
    }

    private void SwapContent()
    {
        // blocks the frame; factoryMs is the real cost of building this sheet's body.
        var newContent = ContentFactory is not null
            ? ContentFactory()
            : DeferredContent;

        if (newContent is not null)
        {
            // Only adopt the wrapper's inherited BindingContext when the content didn't bring
            // its own. Factory-built views (e.g. *PageContentView) typically assign a DI-resolved
            // ViewModel inside their constructor; overwriting it here breaks every binding.
            if (newContent.BindingContext is null)
            {
                newContent.BindingContext = BindingContext;
            }

            OnContentCreated?.Invoke(newContent);
        }

        if (FadeContentIn && newContent is not null)
        {
            RevealWithCrossfade(newContent);
            return;
        }

        Content = newContent;
        IsRevealSettled = true;
        ContentLoaded?.Invoke(this, EventArgs.Empty);
        RevealSettled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    ///     Keeps the spinner on screen while the heavy <paramref name="newContent" /> lays out
    ///     behind it (Opacity 0), then crossfades the content in and the spinner out so the user
    ///     never sees the tree realize piece by piece.
    /// </summary>
    private void RevealWithCrossfade(View newContent)
    {
        var spinner = Content; // the current loading view (OPAQUE background — it is the cover)

        // Detach the spinner from THIS view BEFORE re-parenting it into the reveal host. It is still
        // this.Content at this point, so adding it to the host while parented triggers MAUI's
        // "…is already a child of DeferredContentView…" reparent path — a warning plus a native
        // detach/re-attach of a live subtree mid-reveal, which on fragile OEM devices (Doogee S96Pro)
        // can destabilise the visual/camera tree. Clearing Content first makes the re-parent clean.
        Content = null;

        // The content goes in at FULL opacity, UNDER the still-opaque placeholder. It renders,
        // lays out, and its icon fonts apply completely while covered; the reveal then only
        // fades the placeholder away, exposing an already-finished tree. (The previous scheme
        // faded both in parallel, which let half-applied glyphs show through mid-fade.)
        var host = new DeferredRevealHost
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        host.Add(newContent);
        if (spinner is not null)
        {
            host.Add(spinner);
        }

        Content = host;

        // Fire AFTER the new tree is parented so hosts can react. Sizing engines must keep
        // treating the body as loading until IsRevealSettled (a naive measure of the host
        // returns max(content, placeholder) while both are parented).
        ContentLoaded?.Invoke(this, EventArgs.Empty);

        _ = CrossfadeAsync(host, newContent, spinner);
    }

    private async Task CrossfadeAsync(Grid host, View newContent, View? spinner)
    {
        try
        {
            // Settle window: the covered content finishes its first layout and draw — and any
            // asynchronously-loaded icon images land — before anything of it becomes visible.
            //
            // It is measured in FRAMES, with FadeRevealDelayMs as the cap. It used to be a flat
            // 220 ms delay: a number sized for the slowest device and then paid in full by every
            // other one, on every reveal. What the delay was standing in for is "the content has
            // been laid out and drawn", which takes a couple of frames (~33 ms at 60 Hz); the cap
            // keeps the worst case exactly where it was.
            if (FadeRevealDelayMs > 0)
            {
                var deadline = G9FrameAwaiter.Deadline.After(FadeRevealDelayMs);
                await G9FrameAwaiter.WaitUntilAsync(
                        () => newContent.Handler is null || (newContent.Width > 0 && newContent.Height > 0),
                        deadline)
                    .ConfigureAwait(true);
                await G9FrameAwaiter.WaitFramesAsync(RevealSettleFrames, deadline).ConfigureAwait(true);
            }

            // The content is parented under the opaque spinner here, so its Loaded has fired and any
            // async initialization (data fill, scroll-to-selection) is running. If it opts into the
            // readiness contract, keep the cover until it signals settled — otherwise that late
            // mutation would re-render in full view (the one-time "blink"). Bounded by a timeout so a
            // view that never signals still reveals.
            await WaitForContentReadinessAsync(newContent).ConfigureAwait(true);

            if (spinner is not null)
            {
                await spinner.FadeToAsync(0, 160, Easing.CubicIn).ConfigureAwait(true);
            }
        }
        catch
        {
            // Best-effort visual polish — settle regardless of any animation hiccup
            // (e.g. the sheet closed mid-fade).
        }
        finally
        {
            // Drop ONLY the spinner; the content STAYS inside the reveal host permanently.
            // Re-parenting the content out of the host (the old "flatten") detached and
            // re-attached its entire native tree — a visible full re-render ~0.4 s after the
            // reveal that re-ran the icon-font race (icons flashed back to tofu rectangles).
            // The lingering host is a transparent single-child grid; the sizing engine's
            // transparent-wrapper unwrapping sees straight through it.
            newContent.Opacity = 1;
            if (spinner is not null)
            {
                host.Remove(spinner);
            }

            IsRevealSettled = true;
            RevealSettled?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    ///     Safety cap on how long the covered reveal waits for <see cref="IDeferredContentReadiness" />.
    ///     A view whose async init is unusually slow (or that never signals) reveals anyway, so the
    ///     spinner can never hang. Comfortably above a normal local-data load.
    /// </summary>
    private const int ContentReadinessTimeoutMs = 5000;

    /// <summary>
    ///     Awaits the content's <see cref="IDeferredContentReadiness.ContentReady" /> (bounded by
    ///     <see cref="ContentReadinessTimeoutMs" />) so the covered reveal exposes an already-settled
    ///     tree. No-op for content that doesn't implement the contract or is already ready.
    /// </summary>
    private static async Task WaitForContentReadinessAsync(View? content)
    {
        if (content is not IDeferredContentReadiness readiness || readiness.IsContentReady)
        {
            return;
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnReady(object? sender, EventArgs e) => tcs.TrySetResult();

        readiness.ContentReady += OnReady;
        try
        {
            // Re-check after subscribing: the signal may have fired between the guard above and the
            // subscription (DeferredContentReadinessSignal also replays a late subscribe, so this is
            // belt-and-braces).
            if (readiness.IsContentReady)
            {
                return;
            }

            await Task.WhenAny(tcs.Task, Task.Delay(ContentReadinessTimeoutMs)).ConfigureAwait(true);
        }
        finally
        {
            readiness.ContentReady -= OnReady;
        }
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        DetachLoadedHandler();

        if (_isContentLoaded)
        {
            return;
        }

        Helpers.G9SafeCommand.RunSafe(
            () => LoadContentAsync(),
            new Helpers.G9SafeCommandOptions
            {
                Source = nameof(DeferredContentView),
                ThrottleKey = $"{nameof(DeferredContentView)}.{nameof(OnLoaded)}",
                EnableThrottle = false,
                PreventConcurrentExecution = false,
                RunActionOnMainThread = true
            });
    }

    private void DetachLoadedHandler()
    {
        if (!_loadedHandlerAttached)
        {
            return;
        }

        Loaded -= OnLoaded;
        _loadedHandlerAttached = false;
    }

    private void CancelPendingLoad()
    {
        lock (_lock)
        {
            _loadCts?.Cancel();
        }
    }

    /// <summary>
    ///     Host used by the covered reveal: the real content at the bottom, the loading
    ///     placeholder stacked over it until the reveal settles (then only the placeholder is
    ///     removed — the content stays in this host PERMANENTLY, because re-parenting it out
    ///     would detach/re-attach its native tree, visibly re-rendering it and re-running the
    ///     icon-font race). Transparent single-child wrapper after settle; the sizing engine's
    ///     unwrapping sees through it.
    /// </summary>
    internal sealed class DeferredRevealHost : Grid
    {
    }

    private static View CreateDefaultLoadingView()
    {
        // Solid full-fill background so a stacked sheet over visible content underneath
        // (e.g. another bottom sheet) doesn't leak through while loading.
        return new ContentView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            SafeAreaEdges = SafeAreaEdges.None,
            BackgroundColor = G9Palette.Current.Background,
            Content = new G9ActivityIndicator
            {
                IsRunning = true,
                Color = G9Palette.Current.Primary,
                HeightRequest = 42,
                WidthRequest = 42,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
    }
}
