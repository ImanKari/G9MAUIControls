using G9MAUIControls.BottomSheet;

using G9MAUIControls.Controls;

using G9MAUIControls.Popup;

namespace G9MAUIControls.Hosting;

/// <summary>
///     Base for bottom-sheet content that follows the "open then fill" pattern: the sheet opens
///     immediately showing this view's own loading/preview state, then <see cref="G9BottomSheetHelper" />
///     drives the deferred data load after the open animation completes (see
///     <see cref="IDeferredSheetLoad" />). Heavy data fetches therefore never block the tap → open
///     path, which also keeps the helper's single-key <c>ShowBottomSheet</c> throttle effective
///     against rapid double-taps.
///     <para>
///         This non-generic base is the <b>XAML-friendly</b> root: a view can declare
///         <c>&lt;bases:LoadableSheetContentView x:Class="…"&gt;</c> without <c>x:TypeArguments</c>.
///         It owns the cross-cutting plumbing shared by every loadable sheet body — the
///         <see cref="IsLoading" /> bindable, the closed-guard (<see cref="IsClosed" /> /
///         <see cref="MarkClosed" />), the per-open <see cref="CancellationToken" /> (cancelled on
///         sheet close <em>and</em> on view detach), and the bottom-sheet handle injection.
///         Subclasses implement <see cref="RunDeferredLoadAsync" />. For the common
///         "load a TData then apply it" shape, derive from
///         <see cref="LoadableSheetContentView{TData}" /> instead, which fills that method in.
///     </para>
/// </summary>
public abstract class LoadableSheetContentView : ContentView, IG9BottomSheetAwareView, IDeferredSheetLoad,
    IStagedSheetLoad, IDeferredContentReadiness
{
    /// <summary>
    ///     True while the deferred data load is in flight. Bind sheet content visibility to this
    ///     (spinner when <c>true</c>, populated body when <c>false</c>). Starts <c>true</c> so the
    ///     view paints its loading state from the very first frame the sheet shows.
    /// </summary>
    public static readonly BindableProperty IsLoadingProperty =
        BindableProperty.Create(
            nameof(IsLoading),
            typeof(bool),
            typeof(LoadableSheetContentView),
            true,
            propertyChanged: static (bindable, _, newValue) =>
            {
                // Leaving the loading state IS the readiness signal for a load that ran while the
                // sheet was staged (see LoadWhileStaged). Not the end of RunDeferredLoadAsync: that
                // routinely carries on into a background server refresh long after the body is
                // showing what the local store had.
                if (newValue is false)
                {
                    ((LoadableSheetContentView)bindable)._readiness.MarkReady();
                }
            });

    private readonly DeferredContentReadinessSignal _readiness = new();

    private readonly Lock _loadLock = new();
    private CancellationTokenSource? _loadCts;
    private int _isClosed;
    private bool _loadStarted;

    public bool IsLoading
    {
        get => (bool)GetValue(IsLoadingProperty);
        protected set => SetValue(IsLoadingProperty, value);
    }

    /// <summary>Injected automatically by <see cref="G9BottomSheetHelper" /> when the view is shown.</summary>
    public IG9BottomSheetHandle G9BottomSheetHandle { get; set; } = G9BottomSheetHelper.InitG9BottomSheet();

    /// <summary>
    ///     True once the sheet has begun closing (or the view detached). After this, no loaded
    ///     data is applied — guards against touching a torn-down sheet from a late-completing load.
    /// </summary>
    public bool IsClosed => Volatile.Read(ref _isClosed) != 0;

    /// <summary>
    ///     Opt-in (default <c>false</c>): run <see cref="RunDeferredLoadAsync" /> WHILE THE SHEET IS
    ///     STAGED off-screen, and hold the open — briefly — until the body has left its loading state,
    ///     so the sheet arrives already filled instead of opening onto a spinner.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Turn it on for a body whose FIRST render needs only data that is already on the
    ///         device</b> — a local-database read, a value passed in. "Open then fill" was written
    ///         for loads that are slow; applied to a read that takes a few milliseconds it makes the
    ///         fastest sheets in the app the ones that visibly load: the sheet opens on a spinner,
    ///         the read finishes almost at once, the body waits out a glyph-settle window, fades in,
    ///         and the sheet resizes to it. Staged, all of that happens below the screen edge.
    ///     </para>
    ///     <para>
    ///         <b>Leave it off for a body whose first render waits on the network.</b> The hold is
    ///         bounded by <c>G9BottomSheetSettings.PreOpenMaxHoldMs</c>, so such a sheet still opens —
    ///         on its loading state, exactly as today — but only after sitting out that bound with
    ///         nothing on screen, which is worse than opening at once.
    ///     </para>
    ///     <para>
    ///         A background refresh AFTER the first render is fine and is the expected shape: the
    ///         open waits for <see cref="IsLoading" /> to clear, not for
    ///         <see cref="RunDeferredLoadAsync" /> to return. Nothing else changes for the subclass —
    ///         it still renders and then calls <see cref="RevealLoadedContentAsync" />, which skips
    ///         its cover and fade while the sheet is staged (there is nobody to hide the swap from).
    ///     </para>
    /// </remarks>
    protected virtual bool LoadWhileStaged => false;

    bool IStagedSheetLoad.LoadsWhileStaged => LoadWhileStaged;

    bool IDeferredContentReadiness.IsContentReady => !LoadWhileStaged || !IsLoading || _readiness.IsReady;

    event EventHandler? IDeferredContentReadiness.ContentReady
    {
        add => _readiness.Ready += value;
        remove => _readiness.Ready -= value;
    }

    /// <summary>
    ///     Drawn frames the loaded body is given, after its first layout, before it fades in. Bounded
    ///     by the <c>glyphSettleDelayMs</c> argument of <see cref="RevealLoadedContentAsync" />, which
    ///     is the hard cap on the whole window.
    /// </summary>
    protected virtual int RevealSettleFrames => 2;

    /// <summary>
    ///     Reveals the loaded body with a glyph-settle window instead of a same-frame swap: the
    ///     body becomes part of layout immediately (hidden at <c>Opacity 0</c>) while the view's
    ///     <see cref="IsLoading" /> visual stays on screen, waits <paramref name="glyphSettleDelayMs" />
    ///     for native realization + icon-font application to finish, then clears
    ///     <see cref="IsLoading" /> and fades the body in. This is the loadable-body equivalent of
    ///     <see cref="DeferredContentView" />'s covered crossfade — without it, icons flash as
    ///     empty "tofu" rectangles for the first frames after the spinner disappears. Call on the
    ///     MAIN thread from the data-apply step, INSTEAD of setting <c>IsVisible</c>/<c>IsLoading</c>
    ///     directly. Keeping <see cref="IsLoading" /> true through the window also keeps the
    ///     bottom-sheet height memo/placeholder window open, so the sheet resizes once, after the
    ///     reveal.
    /// </summary>
    protected async Task RevealLoadedContentAsync(View contentRoot, int glyphSettleDelayMs = 220)
    {
        ArgumentNullException.ThrowIfNull(contentRoot);

        var hostSheet = FindHostSheet();

        // STAGED: the sheet is still parked below the screen edge, so there is nothing to cover and
        // nothing to fade — the body simply becomes the content. The staging pipeline measures it,
        // lays it out and holds its settle frames before the open motion starts, which is the same
        // protection the cover below provides, without the wait being visible.
        if (hostSheet is { IsStaged: true })
        {
            contentRoot.Opacity = 1;
            contentRoot.IsVisible = true;
            IsLoading = false;
            return;
        }

        contentRoot.Opacity = 0;
        contentRoot.IsVisible = true;

        // A load that was started while staged but outlived the hold lands here with the open motion
        // possibly still running. Swapping the body in mid-motion is a layout pass inside the
        // animation; wait the motion out first (bounded — a lost completion must not strand the body
        // behind its spinner).
        if (hostSheet is { IsMotionRunning: true })
        {
            await BottomSheet.G9FrameAwaiter.WaitUntilAsync(
                    () => IsClosed || !hostSheet.IsMotionRunning,
                    BottomSheet.G9FrameAwaiter.Deadline.After(800))
                .ConfigureAwait(true);
        }

        // The settle window is measured in FRAMES, with glyphSettleDelayMs as its CAP. It used to be
        // a flat delay of that length — sized for the slowest device and paid in full by every
        // other one. What it was standing in for is "the body has been laid out and drawn", which
        // is a couple of frames; the cap keeps the worst case exactly where it was.
        if (glyphSettleDelayMs > 0)
        {
            var deadline = BottomSheet.G9FrameAwaiter.Deadline.After(glyphSettleDelayMs);
            await BottomSheet.G9FrameAwaiter.WaitUntilAsync(
                    () => IsClosed || contentRoot.Handler is null || (contentRoot.Width > 0 && contentRoot.Height > 0),
                    deadline)
                .ConfigureAwait(true);
            await BottomSheet.G9FrameAwaiter.WaitFramesAsync(RevealSettleFrames, deadline).ConfigureAwait(true);
        }

        if (IsClosed)
        {
            contentRoot.Opacity = 1;
            IsLoading = false;
            return;
        }

        // Fade the body in FIRST (it renders on top of the loading visual), and only then clear
        // IsLoading. Collapsing the loading visual mid-reveal changes the body layout while the
        // user is watching — the "content jumps down and back" glitch — so the spinner leaves
        // the tree only after the content is fully opaque over it. This also keeps the sheet's
        // height window open until the reveal is done.
        await contentRoot.FadeToAsync(1, 140, Easing.CubicOut).ConfigureAwait(true);
        contentRoot.Opacity = 1;
        IsLoading = false;
    }

    /// <summary>
    ///     Marks the content closed and cancels any in-flight load. Wire this from the sheet's
    ///     <c>ClosedCommand</c>. Idempotent.
    /// </summary>
    public void MarkClosed()
    {
        if (Interlocked.Exchange(ref _isClosed, 1) != 0)
        {
            return;
        }

        CancelLoad();
        OnClosed();
    }

    /// <inheritdoc />
    Task IDeferredSheetLoad.LoadDeferredAsync(CancellationToken cancellationToken)
    {
        if (IsClosed)
        {
            return Task.CompletedTask;
        }

        CancellationTokenSource linkedCts;
        lock (_loadLock)
        {
            if (_loadStarted)
            {
                return Task.CompletedTask;
            }

            _loadStarted = true;
            _loadCts = new CancellationTokenSource();
            linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_loadCts.Token, cancellationToken);
        }

        return RunGuardedAsync(linkedCts);
    }

    /// <inheritdoc />
    protected override void OnParentSet()
    {
        base.OnParentSet();

        // Detaching from the visual tree means the sheet is gone — cancel any pending load so a
        // late completion never applies onto a dead view.
        if (Parent is null)
        {
            CancelLoad();
        }
    }

    /// <summary>
    ///     Runs the actual deferred load (and applies the result) for the supplied token. Called
    ///     once per open, on the UI thread, by <see cref="G9BottomSheetHelper" />. The token is
    ///     cancelled on sheet close / view detach.
    /// </summary>
    protected abstract Task RunDeferredLoadAsync(CancellationToken cancellationToken);

    /// <summary>Sets <see cref="IsLoading" /> safely from any thread.</summary>
    protected void SetLoading(bool value)
    {
        if (MainThread.IsMainThread)
        {
            IsLoading = value;
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => IsLoading = value);
    }

    /// <summary>
    ///     Called once when the sheet begins closing (via <see cref="MarkClosed" />). Override to
    ///     release subscriptions or forward the close to child content. Default does nothing.
    /// </summary>
    protected virtual void OnClosed()
    {
    }

    private async Task RunGuardedAsync(CancellationTokenSource linkedCts)
    {
        try
        {
            await RunDeferredLoadAsync(linkedCts.Token).ConfigureAwait(false);
        }
        finally
        {
            // Whatever happened — finished, cancelled, threw — the staged hold has nothing further
            // to wait for. (A normal load already signalled when IsLoading cleared.)
            if (MainThread.IsMainThread)
            {
                _readiness.MarkReady();
            }
            else
            {
                MainThread.BeginInvokeOnMainThread(_readiness.MarkReady);
            }

            linkedCts.Dispose();
            lock (_loadLock)
            {
                _loadCts?.Dispose();
                _loadCts = null;
            }
        }
    }

    private G9SheetView? FindHostSheet()
    {
        for (Element? element = Parent; element is not null; element = element.Parent)
        {
            if (element is G9SheetView sheet)
            {
                return sheet;
            }
        }

        return null;
    }

    private void CancelLoad()
    {
        lock (_loadLock)
        {
            try
            {
                _loadCts?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // already disposed — nothing to cancel.
            }
        }
    }
}

/// <summary>
///     Base for "open then fill" content with the common "load a <typeparamref name="TData" />,
///     then apply it" shape. Subclasses paint a loading/preview state on construction (binding to
///     <see cref="LoadableSheetContentView.IsLoading" />),     pass a <c>loader</c> to the
///     base constructor, and implement <see cref="ApplyLoadedData" /> to render the result.
///     <para>
///         The load is started by <see cref="G9BottomSheetHelper" /> after the sheet is visible, runs
///         off the UI thread, and the apply step is marshalled back to the UI thread. Cancellation
///         (sheet close / view detach) is handled by the base, so <see cref="ApplyLoadedData" /> is
///         never called after the sheet starts closing. A <c>null</c> loader result means "nothing
///         to apply" (cancelled / target gone); the loading flag is still cleared. Override
///         <see cref="OnLoadReturnedNull" /> to react.
///     </para>
///     <para>
///         Because the base is generic, deriving views constructed from C# (no XAML root) are the
///         natural fit. A XAML view should instead derive from the non-generic
///         <see cref="LoadableSheetContentView" /> and implement
///         <see cref="LoadableSheetContentView.RunDeferredLoadAsync" /> directly, to avoid an
///         <c>x:TypeArguments</c> generic XAML root.
///     </para>
/// </summary>
/// <typeparam name="TData">The data shape produced by the loader and consumed by the view.</typeparam>
public abstract class LoadableSheetContentView<TData> : LoadableSheetContentView
{
    private readonly Func<CancellationToken, Task<TData?>> _loader;

    protected LoadableSheetContentView(Func<CancellationToken, Task<TData?>> loader)
    {
        ArgumentNullException.ThrowIfNull(loader);
        _loader = loader;
        IsLoading = true;
    }

    /// <inheritdoc />
    protected sealed override async Task RunDeferredLoadAsync(CancellationToken cancellationToken)
    {
        TData? data;
        try
        {
            data = await _loader(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            // ⛔ A failing loader used to propagate with IsLoading still TRUE, and the helper runs
            // this load fire-and-forget with its error popup off — so the sheet kept its loading
            // skeleton for ever and nothing told the user why. Leave the loading state, give the
            // view a chance to show something, and rethrow so the failure is still logged.
            if (!IsClosed)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (IsClosed)
                    {
                        return;
                    }

                    OnLoadFailed(exception);
                    IsLoading = false;
                }).ConfigureAwait(false);
            }

            throw;
        }

        if (IsClosed || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (IsClosed || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (data is null)
            {
                OnLoadReturnedNull();
            }
            else
            {
                ApplyLoadedData(data);
            }

            IsLoading = false;
        }).ConfigureAwait(false);
    }

    /// <summary>
    ///     Renders the loaded data. Always called on the UI thread, and never after the sheet has
    ///     started closing.
    /// </summary>
    protected abstract void ApplyLoadedData(TData data);

    /// <summary>
    ///     Called on the UI thread when the loader returned <c>null</c> (cancelled / target gone).
    ///     Default does nothing.
    /// </summary>
    protected virtual void OnLoadReturnedNull()
    {
    }

    /// <summary>
    ///     Called on the UI thread when the loader THREW, just before <see cref="LoadableSheetContentView.IsLoading" />
    ///     is cleared. Override to show an error state or close the sheet. The exception is rethrown
    ///     afterwards, so it is still logged by whoever started the load. Default does nothing.
    /// </summary>
    protected virtual void OnLoadFailed(Exception exception)
    {
    }
}
