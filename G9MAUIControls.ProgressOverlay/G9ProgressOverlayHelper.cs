using G9MAUIControls.Hosting;
using G9MAUIControls.BottomSheet;
using G9MAUIControls.ProgressOverlay.Views;
using G9MAUIControls.Controls;
using G9MAUIControls.Helpers;
using G9MAUIControls.Localization;
using CommunityToolkit.Mvvm.Messaging;
using G9MAUIControls.Icons;

using G9MAUIControls.Toast;

using G9MAUIControls.Theming;

namespace G9MAUIControls.ProgressOverlay;

/// <summary>
///     Owns the app's single sync-progress overlay. There is ALWAYS at most one overlay view in the
///     tree (the "session"): every <see cref="ShowAsync" /> caller takes a lightweight lease on that
///     one view instead of mounting its own. This is what fixes the "two sync toasts on one page"
///     bug — two overlapping syncs no longer each mount a view that both render the same broadcast
///     progress. A sync that is merely QUEUED behind the running one is shown as a small badge on the
///     running overlay (driven by <see cref="G9ProgressQueuedCount" />), never as a second overlay.
/// </summary>
public static class G9ProgressOverlayHelper
{
    private const double HorizontalGap = 16;
    private const double VerticalGap = 14;
    private const double MobileBottomInsetFallback = 0;
    private const double MobileBottomInsetExtraGap = 8;

    // ZIndex is no longer set on the overlay because the helper mounts it into the
    // dedicated ToastHost grid in BasePageTemplate, which already paints above OverlayHost
    // (popup + sheet) via document order. See the layer contract at the top of
    // BasePageTemplate.xaml.

    private static readonly Lock SessionGate = new();
    private static G9ProgressOverlaySession? _session;

    // Doubles as the messenger recipient token for the CancelRequested bridge, so the registration is
    // keyed to something that lives exactly as long as this class.
    private static readonly object CancelSubscriberLock = new();
    private static EventHandler<G9ProgressCancelRequested>? _cancelSubscribers;

    public static async Task<G9ProgressOverlayHandle> ShowAsync(
        string contextText,
        G9ProgressOverlayPosition position = G9ProgressOverlayPosition.Bottom)
    {
        // The visual tree is sometimes not yet attached when callers fire sync from a
        // page's appearing path or a bottom sheet's content factory: in those frames
        // ResolveHostContext can return null because Shell.CurrentPage is still the
        // previous page, the G9PageBase ModalHostRegistry hasn't activated yet, or the
        // sheet's per-instance overlay grid hasn't been registered. Without a retry the
        // overlay is silently dropped and the user sees no progress UI.
        //
        // We re-resolve a few times across UI frames before giving up. The window is
        // small (~3 frames) so a real "no host" case still returns Empty quickly.
        var host = await ResolveHostContextWithRetryAsync().ConfigureAwait(true);
        if (host is null)
        {
            return G9ProgressOverlayHandle.Empty;
        }

        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var (session, isNew) = AcquireSession(host.Value, position);
            var handle = session.AddLease(contextText);
            if (isNew)
            {
                await session.MountAsync().ConfigureAwait(true);
            }

            return handle;
        });
    }

    /// <summary>
    ///     Shows a one-shot persistent failure banner on the shared overlay. Used by the sync
    ///     feedback pipeline when there is no active progress overlay (e.g. background syncs, retry
    ///     replays) — replaces the previous transient toast fallback so the user can still see and
    ///     acknowledge the error.
    /// </summary>
    public static async Task<bool> ShowStandaloneFailureAsync(
        string reason,
        string retryText,
        Func<Task>? retryAction,
        G9ProgressOverlayPosition position = G9ProgressOverlayPosition.Bottom)
    {
        var host = await ResolveHostContextWithRetryAsync().ConfigureAwait(true);
        if (host is null)
        {
            return false;
        }

        return await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var (session, isNew) = AcquireSession(host.Value, position);
            var handle = session.AddLease(reason);
            if (isNew)
            {
                await session.MountAsync().ConfigureAwait(true);
            }

            // Show failure first, then dispose-on-acknowledge in the background. The lease's dispose
            // blocks on the user tapping retry or close, so the banner stays visible until acted on;
            // we don't await it here so the caller is not blocked.
            await session.ShowFailureAsync(reason, retryText, retryAction).ConfigureAwait(true);
            _ = SafelyDisposeAfterAcknowledgmentAsync(handle);
            return true;
        });
    }

    private static async Task SafelyDisposeAfterAcknowledgmentAsync(G9ProgressOverlayHandle handle)
    {
        try
        {
            await handle.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Best effort dismissal; nothing the user can do if the overlay tear-down fails.
        }
    }

    public static Task<bool> TryShowCurrentSuccessAsync(string? message, string? subtitle = null)
    {
        return TryUseActiveSessionAsync(session => session.ShowSuccessAsync(message, subtitle));
    }

    public static Task<bool> TryShowCurrentFailureAsync(
        string reason,
        string retryText,
        Func<Task>? retryAction)
    {
        return TryUseActiveSessionAsync(session => session.ShowFailureAsync(reason, retryText, retryAction));
    }

    /// <summary>
    ///     Pushes a progress update to whichever overlay is currently mounted.
    ///     <para>
    ///         Progress travels over <see cref="WeakReferenceMessenger" /> rather than through the handle,
    ///         because the overlay is <b>shared</b>: several concurrent operations each hold their own lease
    ///         on one visual, and the thing reporting progress (a repository, an HTTP handler, a BLE poller)
    ///         is usually nowhere near the code that opened the overlay. A broadcast lets the reporter stay
    ///         ignorant of the UI entirely.
    ///     </para>
    ///     <para>
    ///         This method exists so that broadcast is not itself the public contract. Consumers should not
    ///         have to know the message type or take a direct dependency on the messenger to drive the
    ///         overlay — if the channel ever changes, it changes behind this call.
    ///     </para>
    ///     <para>Safe to call when no overlay is mounted: the message is simply unobserved.</para>
    /// </summary>
    public static void Report(G9ProgressReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        WeakReferenceMessenger.Default.Send(report);
    }

    /// <inheritdoc cref="Report(G9ProgressReport)" />
    /// <param name="ratio">Completion in 0..1, or <see langword="null" /> for indeterminate.</param>
    /// <param name="stage">Already-localized stage caption. The package never localizes caller text.</param>
    /// <param name="detail">Optional secondary line (item name, counter, endpoint).</param>
    public static void Report(double? ratio, string? stage = null, string? detail = null)
    {
        WeakReferenceMessenger.Default.Send(new G9ProgressReport(ratio, stage, detail));
    }

    /// <summary>
    ///     Reports how many further operations are queued behind the current one, so the overlay can show
    ///     "+3 waiting" instead of stacking three more overlays. See <see cref="G9ProgressQueuedCount" />.
    /// </summary>
    public static void ReportQueued(int count)
    {
        WeakReferenceMessenger.Default.Send(new G9ProgressQueuedCount(count));
    }

    /// <summary>
    ///     Raised on the main thread when the user taps cancel on the overlay. See
    ///     <see cref="G9ProgressCancelRequested" /> for why cancellation is a broadcast rather than a
    ///     per-session delegate.
    ///     <para>
    ///         The overlay has <b>already</b> entered <see cref="G9ProgressOverlayState.Canceling" /> by the
    ///         time this fires, so a handler does not need to produce visual feedback — only to stop the
    ///         work. When the work finishes unwinding, close the overlay by disposing the handle.
    ///     </para>
    ///     <para>
    ///         Handlers are held <b>strongly</b>: unsubscribe from a page's teardown. A static event is the
    ///         right shape here despite that — cancellation must reach code that outlives any one page, which
    ///         is the whole reason it is not a delegate on the session.
    ///     </para>
    /// </summary>
    public static event EventHandler<G9ProgressCancelRequested>? CancelRequested
    {
        add
        {
            lock (CancelSubscriberLock)
            {
                if (value is null)
                {
                    return;
                }

                // Registered against the messenger only while somebody is listening, so an app that never
                // offers cancellation carries no subscription at all.
                if (_cancelSubscribers is null)
                {
                    WeakReferenceMessenger.Default.Register<G9ProgressCancelRequested>(
                        CancelSubscriberLock,
                        static (_, message) => RaiseCancelRequested(message));
                }

                _cancelSubscribers += value;
            }
        }
        remove
        {
            lock (CancelSubscriberLock)
            {
                _cancelSubscribers -= value;
                if (_cancelSubscribers is null)
                {
                    WeakReferenceMessenger.Default.Unregister<G9ProgressCancelRequested>(CancelSubscriberLock);
                }
            }
        }
    }

    private static void RaiseCancelRequested(G9ProgressCancelRequested message)
    {
        EventHandler<G9ProgressCancelRequested>? handlers;
        lock (CancelSubscriberLock)
        {
            handlers = _cancelSubscribers;
        }

        if (handlers is null)
        {
            return;
        }

        // The messenger delivers on the sending thread; the overlay sends from the main thread today, but a
        // handler that touches UI must not depend on that staying true.
        if (MainThread.IsMainThread)
        {
            handlers(null, message);
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(() => handlers(null, message));
        }
    }

    private static async Task<bool> TryUseActiveSessionAsync(Func<G9ProgressOverlaySession, Task> action)
    {
        G9ProgressOverlaySession? session;
        lock (SessionGate)
        {
            session = _session is { IsActive: true } ? _session : null;
        }

        if (session is null)
        {
            return false;
        }

        await action(session).ConfigureAwait(false);
        return true;
    }

    private static (G9ProgressOverlaySession Session, bool IsNew) AcquireSession(
        OverlayHostContext host,
        G9ProgressOverlayPosition position)
    {
        lock (SessionGate)
        {
            if (_session is { IsActive: true })
            {
                return (_session, false);
            }

            var session = new G9ProgressOverlaySession(host.Parent, host.Page, position);
            _session = session;
            return (session, true);
        }
    }

    // Called by a session when its last lease is released and it has fully torn down, so the next
    // ShowAsync starts a fresh overlay rather than re-attaching to a removed view.
    internal static void ClearSession(G9ProgressOverlaySession session)
    {
        lock (SessionGate)
        {
            if (ReferenceEquals(_session, session))
            {
                _session = null;
            }
        }
    }

    internal static void ApplyOverlayPosition(
        View overlay,
        G9PageBase? page,
        G9ProgressOverlayPosition position)
    {
        var topInset = ResolveTopInset(page);
        var bottomInset = ResolveBottomInset(page, position);

        overlay.HorizontalOptions = LayoutOptions.Fill;
        overlay.VerticalOptions = position == G9ProgressOverlayPosition.Top
            ? LayoutOptions.Start
            : LayoutOptions.End;

        overlay.Margin = position == G9ProgressOverlayPosition.Top
            ? new Thickness(HorizontalGap, topInset + VerticalGap, HorizontalGap, bottomInset)
            : new Thickness(HorizontalGap, topInset, HorizontalGap, bottomInset + VerticalGap);
    }

    internal static void PrepareOverlayPlacement(Layout parent, View overlay)
    {
        if (parent is not Grid grid)
        {
            return;
        }

        var rowSpan = Math.Max(1, grid.RowDefinitions.Count);
        var columnSpan = Math.Max(1, grid.ColumnDefinitions.Count);

        Grid.SetRow(overlay, 0);
        Grid.SetColumn(overlay, 0);
        Grid.SetRowSpan(overlay, rowSpan);
        Grid.SetColumnSpan(overlay, columnSpan);
    }

    private static double ResolveTopInset(G9PageBase? page)
    {
        if (!IsMobilePlatform())
        {
            return 0;
        }

        return Math.Max(0, page?.TopSafeAreaInset ?? 0);
    }

    private static double ResolveBottomInset(G9PageBase? page, G9ProgressOverlayPosition position)
    {
        var isBottom = position == G9ProgressOverlayPosition.Bottom;

        // Tab-bar clearance: a bottom-anchored sync overlay on MainPage must float ABOVE the
        // managed bottom tab bar, but ONLY while no bottom sheet is open (a sheet covers the tab
        // bar, so the overlay reverts to the normal safe-area gap). BottomSafeAreaWithTabBar adds
        // the reserved tab-bar band over BottomSafeAreaInset only on MainPage; the delta is a
        // no-op on every other host. Mirrors G9ToastHelper.ResolveBottomInset.
        var tabBarClearance = 0d;
        if (isBottom && page is not null && G9BottomSheetHelper.GetOpenSheetCount() == 0)
        {
            tabBarClearance = Math.Max(0, page.BottomSafeAreaWithTabBar - page.BottomSafeAreaInset);
        }

        if (!IsMobilePlatform())
        {
            return isBottom && tabBarClearance > 0 ? tabBarClearance + MobileBottomInsetExtraGap : 0;
        }

        var bottomInset = page?.BottomSafeAreaInset ?? 0;

        if (bottomInset <= 0)
        {
            bottomInset = MobileBottomInsetFallback;
        }

        bottomInset += tabBarClearance;

        return isBottom
            ? bottomInset + MobileBottomInsetExtraGap
            : bottomInset;
    }

    private static bool IsMobilePlatform()
    {
        return DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS;
    }

    internal readonly record struct OverlayHostContext(Layout Parent, G9PageBase? Page);

    private static async Task<OverlayHostContext?> ResolveHostContextWithRetryAsync()
    {
        const int maxAttempts = 4;
        const int delayMilliseconds = 50;

        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var host = ResolveHostContext();
            if (host is not null)
            {
                return host;
            }

            if (attempt == maxAttempts - 1)
            {
                break;
            }

            await Task.Delay(delayMilliseconds).ConfigureAwait(true);
        }

        return null;
    }

    private static OverlayHostContext? ResolveHostContext()
    {
        // Mount on the dedicated ToastHost grid in BasePageTemplate (above OverlayHost so the
        // sync overlay paints over any open popup or bottom sheet). ToastHost is part of the
        // control template — outlives every sheet / popup / page-content swap — so a sync-
        // progress overlay started inside a sheet keeps showing after the sheet closes.
        if (G9OverlayHosts.TryGetCurrent(out var host))
        {
            return new OverlayHostContext(host.ToastLayer, host.Page);
        }

        // Fallback path — only reachable during the brief startup window before
        // OnApplyTemplate runs on the active G9PageBase. No popup or sheet exists then.
        var page = ResolveVisiblePage(Application.Current?.Windows
            .Where(window => window.Page is not null)
            .Select(window => window.Page)
            .FirstOrDefault());

        if (page is ContentPage contentPage && contentPage.Content is Layout layout)
        {
            return new OverlayHostContext(layout, page as G9PageBase);
        }

        return null;
    }

    private static Page? ResolveVisiblePage(Page? page)
    {
        if (page is null)
        {
            return null;
        }

        if (page.Navigation?.ModalStack is { Count: > 0 } modalStack &&
            !ReferenceEquals(modalStack[^1], page))
        {
            return ResolveVisiblePage(modalStack[^1]);
        }

        return page;
    }
}

/// <summary>
///     The single live overlay instance: owns the one <see cref="G9ProgressOverlayView" />, its
///     message subscriptions, and the ordered list of leases that keep it alive. The front lease
///     (oldest) is treated as the "running" sync whose context the view shows; the rest are queued.
///     The view is torn down only when the last lease is released (after its terminal state is seen).
/// </summary>
internal sealed class G9ProgressOverlaySession
{
    private const int SuccessTerminalDurationMs = 2200;

    // How long the overlay stays in "Canceling…" before telling the user (via a toast) that the
    // current step can't be interrupted immediately — the cancel still completes, this just
    // explains the wait. Cleared the instant the run actually stops.
    private const int CancelWatchdogMs = 5000;

    private readonly Layout _parent;
    private readonly G9PageBase? _page;
    private readonly G9ProgressOverlayPosition _position;
    private readonly G9ProgressOverlayView _view;
    private readonly List<G9ProgressOverlayHandle> _leases = new();
    private readonly object _gate = new();

    private bool _isTornDown;
    private bool _hasTerminalState;
    private bool _isFailureTerminal;
    private bool _isFailureAcknowledged;
    private DateTime _terminalVisibleUntilUtc;
    private TaskCompletionSource? _failureAcknowledgmentTcs;
    private Task? _retryTask;

    // Set when the user taps cancel; drives the immediate teardown + "Sync canceled" toast and the
    // non-interruptible-step watchdog. Cleared if a new sync supersedes the overlay.
    private bool _isCanceling;
    private CancellationTokenSource? _cancelWatchdogCts;

    internal G9ProgressOverlaySession(
        Layout parent,
        G9PageBase? page,
        G9ProgressOverlayPosition position)
    {
        _parent = parent;
        _page = page;
        _position = position;

        // Created on the main thread (callers wrap AcquireSession in InvokeOnMainThreadAsync).
        _view = new G9ProgressOverlayView();
        _view.Reset();

        WeakReferenceMessenger.Default.Register<G9ProgressReport>(this, OnG9ProgressReport);
        WeakReferenceMessenger.Default.Register<G9ProgressQueuedCount>(this, OnG9ProgressQueuedCount);
        _view.SizeChanged += OnViewSizeChanged;
        _view.CancelRequested += OnViewCancelRequested;
    }

    internal bool IsActive
    {
        get
        {
            lock (_gate)
            {
                return !_isTornDown;
            }
        }
    }

    internal async Task MountAsync()
    {
        G9ProgressOverlayHelper.PrepareOverlayPlacement(_parent, _view);
        G9ProgressOverlayHelper.ApplyOverlayPosition(_view, _page, _position);
        _parent.Add(_view);
        await G9ToastHelper.ReflowInlineToastsForHostAsync(_parent).ConfigureAwait(true);
        await _view.AnimateAppearingAsync(_position).ConfigureAwait(true);
    }

    internal G9ProgressOverlayHandle AddLease(string contextText)
    {
        var handle = new G9ProgressOverlayHandle(this, contextText);
        TaskCompletionSource? ackToRelease = null;
        CancellationTokenSource? watchdogToCancel = null;

        lock (_gate)
        {
            var wasEmpty = _leases.Count == 0;
            _leases.Add(handle);

            // A new sync supersedes any pending cancel intent on the shared overlay: drop the
            // canceling flag (so its eventual teardown does NOT show a spurious "canceled" toast)
            // and stop the watchdog.
            if (_isCanceling)
            {
                _isCanceling = false;
                watchdogToCancel = _cancelWatchdogCts;
                _cancelWatchdogCts = null;
            }

            // A new sync re-uses an overlay that may currently be showing the previous sync's
            // terminal (success/failure). Reset it to the running state and adopt this sync's
            // context so its progress shows. Releasing any pending failure-acknowledgment wait lets
            // an in-flight teardown abort and hand the view to this new sync.
            if (wasEmpty)
            {
                _hasTerminalState = false;
                _isFailureTerminal = false;
                _isFailureAcknowledged = false;
                ackToRelease = _failureAcknowledgmentTcs;
                _failureAcknowledgmentTcs = null;
                _view.Reset();
                _view.SetContextText(contextText);
            }
        }

        ackToRelease?.TrySetResult();
        CancelWatchdog(watchdogToCancel);
        return handle;
    }

    internal async Task ReleaseLeaseAsync(G9ProgressOverlayHandle handle)
    {
        bool wasFront;
        bool isLast;
        string? newFrontContext = null;

        lock (_gate)
        {
            wasFront = _leases.Count > 0 && ReferenceEquals(_leases[0], handle);
            _leases.Remove(handle);
            isLast = _leases.Count == 0;
            if (!isLast && wasFront)
            {
                newFrontContext = _leases[0].ContextText;
            }
        }

        if (!isLast)
        {
            // Other syncs still own the overlay. If the running (front) sync ended, hand the view to
            // the next one; a queued-but-not-front lease ending leaves the running view untouched.
            if (wasFront)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    _view.Reset();
                    _view.SetContextText(newFrontContext);
                }).ConfigureAwait(false);
            }

            return;
        }

        // Last lease: let the user see the terminal state (success countdown / failure ack) before
        // removing — unless a new lease arrives meanwhile, in which case we abort and reuse the view.
        await WaitForTerminalStateAsync().ConfigureAwait(false);

        bool aborted;
        var wasCanceling = false;
        lock (_gate)
        {
            aborted = _leases.Count > 0;
            if (aborted)
            {
                newFrontContext = _leases[0].ContextText;
            }
            else
            {
                _isTornDown = true;
                wasCanceling = _isCanceling;
            }
        }

        if (aborted)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                _view.Reset();
                _view.SetContextText(newFrontContext);
            }).ConfigureAwait(false);
            return;
        }

        await TearDownAsync().ConfigureAwait(false);

        // A user cancel closes the overlay completely and surfaces a single neutral toast — never
        // an error terminal (the run rolled back cleanly via AllOrNothing; SyncService normalized
        // any wrapped cancellation to OperationCanceledException so no failure feedback fired).
        if (wasCanceling)
        {
            await ShowCanceledToastAsync().ConfigureAwait(false);
        }
    }

    internal Task ShowSuccessAsync(string? message, string? subtitle)
    {
        lock (_gate)
        {
            if (_isTornDown)
            {
                return Task.CompletedTask;
            }

            _hasTerminalState = true;
            _isFailureTerminal = false;
            _terminalVisibleUntilUtc = DateTime.UtcNow.AddMilliseconds(SuccessTerminalDurationMs);
        }

        return _view.ShowSuccessAsync(message, subtitle);
    }

    internal Task ShowFailureAsync(string reason, string retryText, Func<Task>? retryAction)
    {
        lock (_gate)
        {
            if (_isTornDown)
            {
                return Task.CompletedTask;
            }

            _hasTerminalState = true;
            _isFailureTerminal = true;
            _isFailureAcknowledged = false;
            _terminalVisibleUntilUtc = DateTime.MaxValue;
            // Arm the dispose-blocking gate: the teardown wait awaits this TCS, which is released on
            // user acknowledgment (retry/close) or when a new sync supersedes the failure.
            _failureAcknowledgmentTcs ??=
                new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        Func<Task>? wrappedRetry = retryAction is null
            ? null
            : () => StartRetryAsync(retryAction);
        Func<Task> wrappedClose = AcknowledgeFailureAsync;

        return _view.ShowFailureAsync(reason, retryText, wrappedRetry, wrappedClose);
    }

    private Task AcknowledgeFailureAsync()
    {
        TaskCompletionSource? ackSource;
        lock (_gate)
        {
            if (_isFailureAcknowledged)
            {
                return Task.CompletedTask;
            }

            _isFailureAcknowledged = true;
            _terminalVisibleUntilUtc = DateTime.UtcNow;
            ackSource = _failureAcknowledgmentTcs;
        }

        ackSource?.TrySetResult();
        return Task.CompletedTask;
    }

    private Task StartRetryAsync(Func<Task> retryAction)
    {
        lock (_gate)
        {
            if (_retryTask is { IsCompleted: false })
            {
                return _retryTask;
            }
        }

        var task = StartRetryCoreAsync(retryAction);
        TaskCompletionSource? staleAck;
        lock (_gate)
        {
            _retryTask = task;
            _hasTerminalState = false;
            _isFailureTerminal = false;
            _isFailureAcknowledged = false;
            _terminalVisibleUntilUtc = DateTime.UtcNow;
            // Capture the failure-ack TCS the dispose-wait may currently be awaiting. We MUST
            // complete it (not just null it): WaitForTerminalStateAsync is blocked on that exact
            // task, so abandoning it would strand the wait forever — the user's later Close would
            // complete a NEW TCS the loop is no longer watching, and the overlay would never tear
            // down (the "X doesn't close the failure toast after a retry" bug). Completing it lets
            // the wait loop re-evaluate and follow the in-progress retry task instead.
            staleAck = _failureAcknowledgmentTcs;
            _failureAcknowledgmentTcs = null;
        }

        staleAck?.TrySetResult();

        return task;
    }

    private async Task StartRetryCoreAsync(Func<Task> retryAction)
    {
        await MainThread.InvokeOnMainThreadAsync(_view.Reset).ConfigureAwait(false);

        try
        {
            await retryAction().ConfigureAwait(false);
        }
        catch
        {
            // The sync path owns user-facing error reporting; keep the overlay alive.
        }
    }

    private async Task WaitForTerminalStateAsync()
    {
        while (true)
        {
            Task? retryTask;
            Task? failureAckTask;
            TimeSpan remaining;
            bool isFailureTerminal;

            lock (_gate)
            {
                // A new lease arrived — abort the teardown wait so the view is handed to it.
                if (_leases.Count > 0)
                {
                    return;
                }

                retryTask = _retryTask is { IsCompleted: false } ? _retryTask : null;
                isFailureTerminal = _isFailureTerminal && !_isFailureAcknowledged;
                failureAckTask = isFailureTerminal ? _failureAcknowledgmentTcs?.Task : null;
                remaining = _hasTerminalState && !isFailureTerminal
                    ? _terminalVisibleUntilUtc - DateTime.UtcNow
                    : TimeSpan.Zero;
            }

            if (retryTask is not null)
            {
                await retryTask.ConfigureAwait(false);
                continue;
            }

            if (failureAckTask is not null)
            {
                // Failure terminal stays visible until the user taps retry or close (or a new sync
                // supersedes it). The countdown fill-bar is intentionally not animated for failures.
                await failureAckTask.ConfigureAwait(false);
                continue;
            }

            if (remaining <= TimeSpan.Zero)
            {
                return;
            }

            await _view.AnimateTerminalCountdownAsync(remaining).ConfigureAwait(false);
        }
    }

    private async Task TearDownAsync()
    {
        CancellationTokenSource? watchdog;
        lock (_gate)
        {
            watchdog = _cancelWatchdogCts;
            _cancelWatchdogCts = null;
        }

        CancelWatchdog(watchdog);

        WeakReferenceMessenger.Default.Unregister<G9ProgressReport>(this);
        WeakReferenceMessenger.Default.Unregister<G9ProgressQueuedCount>(this);
        _view.SizeChanged -= OnViewSizeChanged;
        _view.CancelRequested -= OnViewCancelRequested;
        G9ProgressOverlayHelper.ClearSession(this);

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            _view.PrepareForRemoval();

            if (_view.Parent is null)
            {
                return;
            }

            try
            {
                await _view.AnimateDisappearingAsync(_position).ConfigureAwait(true);
            }
            catch
            {
                // Best effort dismissal.
            }

            _parent.Remove(_view);
            await G9ToastHelper.ReflowInlineToastsForHostAsync(_parent).ConfigureAwait(true);
        }).ConfigureAwait(false);
    }

    private void OnG9ProgressReport(object recipient, G9ProgressReport message)
    {
        _view.ApplyProgress(message);
    }

    private void OnG9ProgressQueuedCount(object recipient, G9ProgressQueuedCount message)
    {
        _view.SetQueuedCount(message.Value);
    }

    private void OnViewCancelRequested(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_isTornDown || _isCanceling)
            {
                return;
            }

            _isCanceling = true;
        }

        // Instant local feedback (idempotent — the view already switched to "Canceling…" on the X
        // tap; this also covers any programmatic cancel path).
        try
        {
            _view.EnterCancelingState();
        }
        catch
        {
            // Visual-only; never let it break the cancel broadcast below.
        }

        // The overlay does not know WHAT is being cancelled and must not. It broadcasts a generic
        // request so a worker nowhere near this call site can react without a delegate threaded
        // through every layer between them. Harmless alongside the caller-supplied OnCancel:
        // cancellation is idempotent.
        WeakReferenceMessenger.Default.Send(new G9ProgressCancelRequested());

        StartCancelWatchdog();
    }

    private void StartCancelWatchdog()
    {
        var cts = new CancellationTokenSource();
        CancellationTokenSource? previous;
        lock (_gate)
        {
            previous = _cancelWatchdogCts;
            _cancelWatchdogCts = cts;
        }

        CancelWatchdog(previous);
        _ = RunCancelWatchdogAsync(cts);
    }

    private async Task RunCancelWatchdogAsync(CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(CancelWatchdogMs, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The run stopped (or a new sync took over) before the watchdog fired — nothing to say.
            return;
        }

        bool stillCanceling;
        lock (_gate)
        {
            stillCanceling = _isCanceling && !_isTornDown;
        }

        if (!stillCanceling)
        {
            return;
        }

        // The run hasn't stopped yet — a step (large apply / geo reconcile) can't be interrupted
        // mid-way. Tell the user it will stop shortly; the overlay stays in "Canceling…".
        await G9ToastHelper.ShowToastAsync(
            G9Strings.Get(G9StringKey.CancelFinishingStep),
            G9ToastType.Information,
            new G9ToastOptions { Icon = G9Glyph.Clock }).ConfigureAwait(false);
    }

    private static Task ShowCanceledToastAsync()
    {
        return G9ToastHelper.ShowToastAsync(
            G9Strings.Get(G9StringKey.Cancelled),
            G9ToastType.Information,
            new G9ToastOptions { Icon = G9Glyph.Info });
    }

    private static void CancelWatchdog(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed by a concurrent path.
        }

        cts.Dispose();
    }

    private void OnViewSizeChanged(object? sender, EventArgs e)
    {
        lock (_gate)
        {
            if (_isTornDown)
            {
                return;
            }
        }

        _ = G9ToastHelper.ReflowInlineToastsForHostAsync(_parent, animate: false);
    }
}

/// <summary>
///     A lease on the shared <see cref="G9ProgressOverlaySession" />. Disposing it releases the
///     overlay for this caller; the view is removed only when the last lease is disposed.
/// </summary>
public sealed class G9ProgressOverlayHandle : IAsyncDisposable
{
    private readonly G9ProgressOverlaySession? _session;
    private readonly bool _isEnabled;
    private bool _isDisposed;

    internal static G9ProgressOverlayHandle Empty { get; } = new();

    internal string ContextText { get; }

    private G9ProgressOverlayHandle()
    {
        _isEnabled = false;
        ContextText = string.Empty;
    }

    internal G9ProgressOverlayHandle(G9ProgressOverlaySession session, string contextText)
    {
        _session = session;
        ContextText = contextText ?? string.Empty;
        _isEnabled = true;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed || !_isEnabled || _session is null)
        {
            _isDisposed = true;
            return;
        }

        _isDisposed = true;
        await _session.ReleaseLeaseAsync(this).ConfigureAwait(false);
    }
}
