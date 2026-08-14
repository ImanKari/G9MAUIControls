using G9MAUIControls.Controls;
using G9MAUIControls.Localization;
using G9MAUIControls.Helpers;
using G9MAUIControls.Theming;
using System.Globalization;

using G9MAUIControls.Icons;

using G9MAUIControls.ProgressOverlay;

namespace G9MAUIControls.ProgressOverlay.Views;

/// <summary>
///     The single sync-progress overlay view. Its visual is driven by ONE explicit state machine so
///     the behaviour is easy to reason about:
///     <list type="bullet">
///         <item><b>Running</b> — spinner + context + stage + % + fill bar + Cancel (X). Tapping the
///         card BODY minimizes; tapping X starts cancellation.</item>
///         <item><b>Canceling</b> — instant feedback after the X is tapped: "Canceling…" + spinner,
///         the X is hidden, progress updates are ignored. The session closes the overlay (and shows
///         a "canceled" toast) once the run actually stops; a watchdog shows a notice if a step
///         can't be interrupted immediately.</item>
///         <item><b>Success / Error</b> — terminal states (handled by <see cref="ShowSuccessAsync" /> /
///         <see cref="ShowFailureAsync" />). A terminal always forces the expanded card.</item>
///     </list>
///     <para><b>Minimized</b> is an orthogonal flag valid for Running/Error: the card collapses to a
///     72×72 bubble (just the %) that the user can DRAG anywhere on screen and TAP to restore.</para>
///     <para>Every gesture/animation handler is exception-safe: a failure here must never crash the
///     UI loop — this overlay is on screen during the most failure-prone moments (flaky sync).</para>
/// </summary>
public partial class G9ProgressOverlayView : ContentView
{
    private const string ProgressAnimationName = "SyncProgressToast.Progress";
    private const string TerminalCountdownAnimationName = "SyncProgressToast.TerminalCountdown";

    // A pan whose total travel stays under this is treated as a tap (restore), not a drag.
    private const double DragTapThresholdPixels = 12d;
    private const double DragEdgeInset = 8d;

    private double _displayedProgress;
    private bool _isMinimized;
    private bool _isPreparingForRemoval;
    private bool _isTransitioning;
    private G9ProgressOverlayState _state = G9ProgressOverlayState.Running;

    /// <summary>
    ///     Where the overlay is in its lifecycle. Surfaced so a consumer can tell "still working" from
    ///     "already finished" without tracking it in parallel — the overlay reaches a terminal state on its
    ///     own (a cancel tap, a linger expiring), so caller-side bookkeeping drifts.
    /// </summary>
    public G9ProgressOverlayState VisualState => _state;
    private string _lastContextText = string.Empty;
    private string _lastStageText;
    private CancellationTokenSource? _progressAnimationCts;
    private Func<Task>? _retryAction;
    private Func<Task>? _closeAction;

    // Drag state for the minimized bubble (the whole view is translated; clamped to the host).
    private double _dragStartTranslationX;
    private double _dragStartTranslationY;
    private double _dragTravel;
    private DateTime _lastDragEndUtc = DateTime.MinValue;

    public G9ProgressOverlayView()
    {
        InitializeComponent();
        ApplyFillLayerDirection();
        SetExpandedStateImmediate();
        SetContextText(G9Strings.Get(G9StringKey.Loading));
        _lastStageText = G9Strings.Get(G9StringKey.Loading);
        StageLabel.Text = _lastStageText;
        ApplyProgressVisual(0d);
    }

    /// <summary>Raised when the user taps the cancel (X) button while a sync is in progress.</summary>
    public event EventHandler? CancelRequested;

    public void SetContextText(string? text)
    {
        var contextText = string.IsNullOrWhiteSpace(text)
            ? G9Strings.Get(G9StringKey.Loading)
            : text.Trim();
        _lastContextText = contextText;
        ContextLabel.Text = contextText;
    }

    public void Reset()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(Reset);
            return;
        }

        CancelCurrentProgressAnimation();
        this.AbortAnimation(TerminalCountdownAnimationName);

        _isPreparingForRemoval = false;
        ApplyFillLayerDirection();
        ApplyRunningVisuals();
        _lastStageText = G9Strings.Get(G9StringKey.Loading);
        StageLabel.Text = _lastStageText;
        SetExpandedStateImmediate();
        ApplyProgressVisual(0d);
    }

    public void ApplyProgress(G9ProgressReport message)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => ApplyProgress(message));
            return;
        }

        // Progress only drives the Running state. Once canceling/terminal, the bar is frozen.
        if (_isPreparingForRemoval || _state != G9ProgressOverlayState.Running)
        {
            return;
        }

        // A queued sync (still blocked on the agent gate) must NOT drive the shared overlay — that
        // belongs to the sync currently running. Its presence is shown via the queued badge
        // (SetQueuedCount) instead, so dropping its waiting-phase progress keeps the bar from
        // jumping backwards over the running sync's real progress.
        if (message.IsIndeterminate)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(message.Stage))
        {
            ObserveAnimationTask(SetContextTextAsync(message.Stage));
        }

        var stageText = string.IsNullOrWhiteSpace(message.Detail)
            ? ResolveStageText(message)
            : message.Detail.Trim();
        ObserveAnimationTask(SetStageTextAsync(stageText));
        ObserveAnimationTask(AnimateProgressToAsync((message.ClampedRatio ?? 0d)));
    }

    /// <summary>
    ///     Updates the small "queued" badge that tells the user another sync is waiting behind the
    ///     one currently shown. <paramref name="queuedCount" /> is the number of runs blocked on the
    ///     sync agent gate; the badge hides at zero.
    /// </summary>
    public void SetQueuedCount(int queuedCount)
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(() => SetQueuedCount(queuedCount));
            return;
        }

        var count = Math.Max(0, queuedCount);
        if (count <= 0)
        {
            QueuedBadge.IsVisible = false;
            return;
        }

        QueuedBadgeLabel.Text = count.ToString(CultureInfo.CurrentCulture);
        QueuedBadge.IsVisible = true;
    }

    /// <summary>
    ///     Switches to the "Canceling…" state — the instant visual feedback when the user taps X.
    ///     Keeps the spinner, freezes the bar, hides the cancel button + percent pill, and ignores
    ///     further progress. Idempotent; only meaningful from <see cref="G9ProgressOverlayState.Running" />.
    ///     The session removes the overlay (and shows a toast) once the run actually stops.
    /// </summary>
    public void EnterCancelingState()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(EnterCancelingState);
            return;
        }

        if (_isPreparingForRemoval || _state != G9ProgressOverlayState.Running)
        {
            return;
        }

        _state = G9ProgressOverlayState.Canceling;

        // The X is only reachable while expanded, so we are normally expanded already; restore the
        // expanded card defensively in case a cancel was triggered while minimized.
        if (_isMinimized)
        {
            SetExpandedStateImmediate();
        }

        CancelCurrentProgressAnimation();

        ProgressSpinner.IsRunning = true;
        CancelButton.IsVisible = false;
        ExpandedPercentPill.IsVisible = false;
        QueuedBadge.IsVisible = false;

        _lastContextText = G9Strings.Get(G9StringKey.CancelFinishingStep);
        ContextLabel.Text = G9Strings.Get(G9StringKey.CancelFinishingStep);
        _lastStageText = string.Empty;
        StageLabel.Text = string.Empty;
    }

    public Task ShowSuccessAsync(string? message, string? subtitle = null)
    {
        var successMessage = string.IsNullOrWhiteSpace(message)
            ? G9Strings.Get(G9StringKey.Success)
            : message.Trim();
        var successSubtitle = string.IsNullOrWhiteSpace(subtitle) ? string.Empty : subtitle.Trim();

        return ShowTerminalAsync(
            G9ProgressOverlayState.Success,
            successMessage,
            successSubtitle,
            G9Palette.Current.Primary,
            G9Palette.Current.OnPrimary,
            G9Glyphs.Success,
            false,
            null,
            null);
    }

    public Task ShowFailureAsync(string reason, string retryText, Func<Task>? retryAction, Func<Task>? closeAction)
    {
        var failureReason = string.IsNullOrWhiteSpace(reason)
            ? G9Strings.Get(G9StringKey.Error)
            : reason.Trim();
        var retryMessage = string.IsNullOrWhiteSpace(retryText)
            ? G9Strings.Get(G9StringKey.Retry)
            : retryText.Trim();

        return ShowTerminalAsync(
            G9ProgressOverlayState.Error,
            failureReason,
            retryMessage,
            G9Palette.Current.Error,
            G9Palette.Current.OnError,
            G9Glyphs.Refresh,
            true,
            retryAction,
            closeAction);
    }

    public Task AnimateTerminalCountdownAsync(TimeSpan duration)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (!IsTerminalState || duration <= TimeSpan.Zero)
            {
                return;
            }

            ExpandedFillLayer.ScaleX = 0d;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var animation = new Animation(value => ExpandedFillLayer.ScaleX = value, 0d, 1d, Easing.Linear);
            animation.Commit(
                this,
                TerminalCountdownAnimationName,
                16,
                (uint)Math.Clamp(duration.TotalMilliseconds, 250d, 10000d),
                finished: (_, _) => completion.TrySetResult());

            await Task.WhenAny(
                    completion.Task,
                    Task.Delay(duration + TimeSpan.FromMilliseconds(160)))
                .ConfigureAwait(true);
        });
    }

    public Task AnimateAppearingAsync(G9ProgressOverlayPosition position)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var offset = position == G9ProgressOverlayPosition.Top ? -26 : 26;
            Opacity = 0;
            TranslationY = offset;

            await Task.WhenAll(
                    this.FadeToAsync(1, 220, Easing.CubicOut),
                    this.TranslateToAsync(0, 0, 220, Easing.CubicOut))
                .ConfigureAwait(true);
        });
    }

    public Task AnimateDisappearingAsync(G9ProgressOverlayPosition position)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            // When minimized the bubble may have been dragged anywhere — fade straight out from
            // wherever it sits instead of assuming the anchored position.
            if (_isMinimized)
            {
                await this.FadeToAsync(0, 160, Easing.CubicIn).ConfigureAwait(true);
                return;
            }

            var offset = position == G9ProgressOverlayPosition.Top ? -16 : 16;

            await Task.WhenAll(
                    this.FadeToAsync(0, 160, Easing.CubicIn),
                    this.TranslateToAsync(0, offset, 160, Easing.CubicIn))
                .ConfigureAwait(true);
        });
    }

    internal void PrepareForRemoval()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(PrepareForRemoval);
            return;
        }

        _isPreparingForRemoval = true;
        _retryAction = null;
        _closeAction = null;
        CancelCurrentProgressAnimation();
        this.AbortAnimation(TerminalCountdownAnimationName);

        ExpandedContainer.GestureRecognizers.Clear();
        CancelButton.GestureRecognizers.Clear();
        TerminalRetryButton.GestureRecognizers.Clear();
        TerminalCloseButton.GestureRecognizers.Clear();
        CompactContainer.GestureRecognizers.Clear();
    }

    #region Gesture handlers (exception-safe)

    // Tap on the card body → minimize, but only while a sync is actively running. No-op in the
    // canceling / success / error states (the X / retry / close buttons consume their own taps).
    private void OnBodyTapped(object? sender, TappedEventArgs e)
    {
        SafeRun(() =>
        {
            if (_isPreparingForRemoval || _isMinimized || _state != G9ProgressOverlayState.Running)
            {
                return;
            }

            ObserveAnimationTask(SetMinimizedAsync(true));
        });
    }

    // Tap on the minimized bubble → restore the full card. Suppressed for the brief window right
    // after a drag so the drag's release does not also trigger a restore.
    private void OnBubbleTapped(object? sender, TappedEventArgs e)
    {
        SafeRun(() =>
        {
            if (_isPreparingForRemoval || !_isMinimized)
            {
                return;
            }

            if ((DateTime.UtcNow - _lastDragEndUtc).TotalMilliseconds < 300d)
            {
                return;
            }

            ObserveAnimationTask(SetMinimizedAsync(false));
        });
    }

    // Drag the minimized bubble anywhere on screen. The whole view is translated and clamped so the
    // bubble can never leave the visible host area. Mirrors the pan pattern in MapDrawingToolbar
    // (TotalX/Y are cumulative from the gesture start on both Android and iOS).
    private void OnBubblePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        SafeRun(() =>
        {
            if (_isPreparingForRemoval || !_isMinimized)
            {
                return;
            }

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _dragStartTranslationX = TranslationX;
                    _dragStartTranslationY = TranslationY;
                    _dragTravel = 0d;
                    break;

                case GestureStatus.Running:
                    var (x, y) = ClampBubbleTranslation(
                        _dragStartTranslationX + e.TotalX,
                        _dragStartTranslationY + e.TotalY);
                    TranslationX = x;
                    TranslationY = y;
                    _dragTravel = Math.Max(_dragTravel, Math.Abs(e.TotalX) + Math.Abs(e.TotalY));
                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    if (_dragTravel > DragTapThresholdPixels)
                    {
                        // Real drag — remember when it ended so the trailing tap is ignored.
                        _lastDragEndUtc = DateTime.UtcNow;
                    }

                    break;
            }
        });
    }

    private void OnCancelTapped(object? sender, TappedEventArgs e)
    {
        SafeRun(() =>
        {
            if (_isPreparingForRemoval || _state != G9ProgressOverlayState.Running)
            {
                return;
            }

            // Instant local feedback; the session drives the actual cancellation + teardown.
            EnterCancelingState();
            CancelRequested?.Invoke(this, EventArgs.Empty);
        });
    }

    private async void OnTerminalRetryTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_isPreparingForRemoval)
            {
                return;
            }

            var retryAction = _retryAction;
            if (retryAction is null)
            {
                return;
            }

            await retryAction().ConfigureAwait(true);
        }
        catch
        {
            // The sync path owns user-facing error reporting; never throw out of a gesture handler.
        }
    }

    private async void OnTerminalCloseTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (_isPreparingForRemoval)
            {
                return;
            }

            var closeAction = _closeAction;
            if (closeAction is null)
            {
                return;
            }

            await closeAction().ConfigureAwait(true);
        }
        catch
        {
            // Never throw out of a gesture handler.
        }
    }

    #endregion

    #region State transitions

    private bool IsTerminalState => _state is G9ProgressOverlayState.Success or G9ProgressOverlayState.Error;

    private async Task SetMinimizedAsync(bool minimize)
    {
        if (!MainThread.IsMainThread)
        {
            await MainThread.InvokeOnMainThreadAsync(() => SetMinimizedAsync(minimize)).ConfigureAwait(false);
            return;
        }

        if (_isPreparingForRemoval || _isTransitioning || minimize == _isMinimized)
        {
            return;
        }

        _isTransitioning = true;
        try
        {
            if (minimize)
            {
                await MinimizeAsync().ConfigureAwait(true);
            }
            else
            {
                await ExpandAsync().ConfigureAwait(true);
            }

            _isMinimized = minimize;
        }
        finally
        {
            _isTransitioning = false;
        }
    }

    private async Task MinimizeAsync()
    {
        CompactContainer.IsVisible = true;
        CompactContainer.Opacity = 0;
        CompactContainer.Scale = 0.82;

        await Task.WhenAll(
                ExpandedContainer.FadeToAsync(0, 120, Easing.CubicIn),
                ExpandedContainer.ScaleToAsync(0.97, 120, Easing.CubicIn))
            .ConfigureAwait(true);

        ExpandedContainer.IsVisible = false;
        ExpandedContainer.Scale = 1;

        // Shrink the view to the bubble so it can be dragged freely — a Fill-width view cannot move
        // horizontally. The bubble was already anchored End, so it stays put visually.
        HorizontalOptions = LayoutOptions.End;

        await Task.WhenAll(
                CompactContainer.FadeToAsync(1, 170, Easing.CubicOut),
                CompactContainer.ScaleToAsync(1, 170, Easing.CubicOut))
            .ConfigureAwait(true);
    }

    private async Task ExpandAsync()
    {
        // Drop any drag offset and restore the full-width anchored placement.
        TranslationX = 0;
        TranslationY = 0;
        HorizontalOptions = LayoutOptions.Fill;

        ExpandedContainer.IsVisible = true;
        ExpandedContainer.Opacity = 0;
        ExpandedContainer.Scale = 0.97;

        await Task.WhenAll(
                CompactContainer.FadeToAsync(0, 110, Easing.CubicIn),
                CompactContainer.ScaleToAsync(0.84, 110, Easing.CubicIn))
            .ConfigureAwait(true);

        CompactContainer.IsVisible = false;
        CompactContainer.Scale = 0.84;

        await Task.WhenAll(
                ExpandedContainer.FadeToAsync(1, 180, Easing.CubicOut),
                ExpandedContainer.ScaleToAsync(1, 180, Easing.CubicOut))
            .ConfigureAwait(true);
    }

    private (double X, double Y) ClampBubbleTranslation(double translationX, double translationY)
    {
        if (Parent is not VisualElement host ||
            host.Width <= 0 || host.Height <= 0 ||
            Width <= 0 || Height <= 0)
        {
            return (translationX, translationY);
        }

        // X / Y are the laid-out position (without translation); keep the translated frame inside
        // the host with a small inset on every edge.
        var minX = DragEdgeInset - X;
        var maxX = host.Width - DragEdgeInset - Width - X;
        var minY = DragEdgeInset - Y;
        var maxY = host.Height - DragEdgeInset - Height - Y;

        if (minX > maxX)
        {
            (minX, maxX) = (0d, 0d);
        }

        if (minY > maxY)
        {
            (minY, maxY) = (0d, 0d);
        }

        return (Math.Clamp(translationX, minX, maxX), Math.Clamp(translationY, minY, maxY));
    }

    private Task ShowTerminalAsync(
        G9ProgressOverlayState terminalState,
        string title,
        string subtitle,
        Color background,
        Color foreground,
        G9IconSource icon,
        bool showIcon,
        Func<Task>? retryAction,
        Func<Task>? closeAction)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            CancelCurrentProgressAnimation();
            this.AbortAnimation(TerminalCountdownAnimationName);

            _state = terminalState;
            _retryAction = retryAction;
            _closeAction = closeAction;
            _isTransitioning = false;

            // A terminal always shows the full card — drop minimized + any drag offset.
            _isMinimized = false;
            TranslationX = 0;
            TranslationY = 0;
            HorizontalOptions = LayoutOptions.Fill;

            CompactContainer.IsVisible = false;
            CompactContainer.Opacity = 0;
            ExpandedContainer.IsVisible = true;
            ExpandedContainer.Opacity = 1;
            ExpandedContainer.Scale = 1;

            ExpandedContainer.BackgroundColor = background;
            ExpandedContainer.Stroke = foreground.WithAlpha(0.30f);
            ExpandedFillLayer.BackgroundColor = foreground.WithAlpha(0.16f);
            ExpandedFillLayer.ScaleX = 0d;

            ProgressSpinner.IsRunning = false;
            ProgressContent.IsVisible = false;
            ProgressContent.Opacity = 0;
            QueuedBadge.IsVisible = false;

            TerminalIcon.Icon = icon;
            TerminalIcon.Color = foreground;
            TerminalIcon.IsVisible = showIcon;
            TerminalTitleLabel.Text = title;
            TerminalTitleLabel.TextColor = foreground;
            TerminalSubtitleLabel.Text = subtitle;
            TerminalSubtitleLabel.TextColor = foreground.WithAlpha(0.88f);
            TerminalSubtitleLabel.IsVisible = !string.IsNullOrWhiteSpace(subtitle);

            // Action buttons are only shown for failure terminals (closeAction != null).
            // Success terminals stay action-free and rely on the auto-dismiss countdown.
            var showRetry = retryAction is not null;
            var showClose = closeAction is not null;
            TerminalRetryButton.IsVisible = showRetry;
            TerminalCloseButton.IsVisible = showClose;
            TerminalRetryIcon.Color = foreground;
            TerminalCloseIcon.Color = foreground;
            TerminalRetryButton.BackgroundColor = foreground.WithAlpha(0.16f);
            TerminalRetryButton.Stroke = foreground.WithAlpha(0.32f);
            TerminalCloseButton.BackgroundColor = foreground.WithAlpha(0.16f);
            TerminalCloseButton.Stroke = foreground.WithAlpha(0.32f);

            // Reserve text column 1; put right-side buttons in 2 (retry) and 3 (close).
            // When neither button is visible (success), let the text host span 1..3 so it
            // stays centered.
            Grid.SetColumn(TerminalTextHost, showIcon ? 1 : 0);
            Grid.SetColumnSpan(TerminalTextHost, (showRetry || showClose) ? 1 : (showIcon ? 3 : 4));

            TerminalContent.IsVisible = true;
            TerminalContent.Opacity = 0;
            await TerminalContent.FadeToAsync(1, 140, Easing.CubicOut).ConfigureAwait(true);
        });
    }

    private void ApplyRunningVisuals()
    {
        var theme = G9Palette.Current;
        _state = G9ProgressOverlayState.Running;
        _retryAction = null;
        _closeAction = null;

        // Restore controls that the canceling state hides.
        CancelButton.IsVisible = true;
        ExpandedPercentPill.IsVisible = true;

        ExpandedContainer.BackgroundColor = theme.SurfaceContainerHigh.WithAlpha(0.98f);
        ExpandedContainer.Stroke = theme.OutlineVariant.WithAlpha(0.52f);
        ExpandedFillLayer.BackgroundColor = theme.Primary.WithAlpha(0.34f);
        ExpandedFillLayer.ScaleX = 0d;

        ProgressSpinner.IsRunning = true;
        ProgressContent.IsVisible = true;
        ProgressContent.Opacity = 1;
        TerminalContent.IsVisible = false;
        TerminalContent.Opacity = 0;

        ExpandedPercentPill.BackgroundColor = theme.Primary.WithAlpha(0.98f);
    }

    private void SetExpandedStateImmediate()
    {
        _isMinimized = false;
        _isTransitioning = false;

        // Anchored, full-width, no drag offset.
        HorizontalOptions = LayoutOptions.Fill;
        TranslationX = 0;
        TranslationY = 0;

        ExpandedContainer.IsVisible = true;
        ExpandedContainer.Opacity = 1;
        ExpandedContainer.Scale = 1;

        CompactContainer.IsVisible = false;
        CompactContainer.Opacity = 0;
        CompactContainer.Scale = 0.84;
    }

    #endregion

    #region Progress animation

    private async Task SetContextTextAsync(string contextText)
    {
        if (!MainThread.IsMainThread)
        {
            await MainThread.InvokeOnMainThreadAsync(() => SetContextTextAsync(contextText)).ConfigureAwait(false);
            return;
        }

        if (string.Equals(_lastContextText, contextText, StringComparison.Ordinal))
        {
            return;
        }

        _lastContextText = contextText;

        await ContextLabel.FadeToAsync(0.35, 90, Easing.CubicIn).ConfigureAwait(true);
        ContextLabel.Text = contextText;
        await ContextLabel.FadeToAsync(1, 140, Easing.CubicOut).ConfigureAwait(true);
    }

    private async Task SetStageTextAsync(string stageText)
    {
        if (!MainThread.IsMainThread)
        {
            await MainThread.InvokeOnMainThreadAsync(() => SetStageTextAsync(stageText)).ConfigureAwait(false);
            return;
        }

        if (string.Equals(_lastStageText, stageText, StringComparison.Ordinal))
        {
            return;
        }

        _lastStageText = stageText;

        await StageLabel.FadeToAsync(0.35, 90, Easing.CubicIn).ConfigureAwait(true);
        StageLabel.Text = stageText;
        await StageLabel.FadeToAsync(1, 140, Easing.CubicOut).ConfigureAwait(true);
    }

    private async Task AnimateProgressToAsync(double targetProgress)
    {
        if (!MainThread.IsMainThread)
        {
            await MainThread.InvokeOnMainThreadAsync(() => AnimateProgressToAsync(targetProgress))
                .ConfigureAwait(false);
            return;
        }

        var clampedTarget = Math.Clamp(targetProgress, 0d, 1d);
        CancelCurrentProgressAnimation();

        var start = _displayedProgress;
        var delta = Math.Abs(clampedTarget - start);

        if (delta <= 0.001d)
        {
            ApplyProgressVisual(clampedTarget);
            return;
        }

        var duration = (uint)Math.Clamp(190 + (delta * 540), 190, 820);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        _progressAnimationCts = cts;

        var animation = new Animation(
            ApplyProgressVisual,
            start,
            clampedTarget,
            Easing.CubicOut);

        try
        {
            animation.Commit(
                this,
                ProgressAnimationName,
                16,
                duration,
                finished: (_, canceled) =>
                {
                    if (canceled || token.IsCancellationRequested)
                    {
                        completion.TrySetCanceled(token);
                        return;
                    }

                    completion.TrySetResult();
                });

            using var registration = token.Register(() => completion.TrySetCanceled(token));

            await completion.Task.ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        finally
        {
            if (ReferenceEquals(_progressAnimationCts, cts))
            {
                _progressAnimationCts = null;
            }
        }

        if (!token.IsCancellationRequested)
        {
            ApplyProgressVisual(clampedTarget);
        }
    }

    private void CancelCurrentProgressAnimation()
    {
        var cts = _progressAnimationCts;
        _progressAnimationCts = null;
        this.AbortAnimation(ProgressAnimationName);

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
            // A finishing animation task may already have disposed its own source.
        }
    }

    private void ApplyProgressVisual(double progress)
    {
        _displayedProgress = Math.Clamp(progress, 0d, 1d);

        ExpandedFillLayer.ScaleX = _displayedProgress;
        CompactFillLayer.ScaleX = _displayedProgress;

        var percent = (int)Math.Round(_displayedProgress * 100d, MidpointRounding.AwayFromZero);
        var percentText = percent.ToString(CultureInfo.CurrentCulture) + "%";
        ExpandedPercentLabel.Text = percentText;
        CompactPercentLabel.Text = percentText;
    }

    #endregion

    #region Helpers

    /// <summary>
    ///     The stage label the overlay should show right now.
    ///     <para>
    ///         The component this was extracted from switched over a sync-specific phase enum here and
    ///         looked each member up in the app's resource dictionary. A package cannot own that vocabulary
    ///         (see <see cref="G9ProgressReport" />), so the caller supplies an already-localized string and
    ///         this only supplies the fallback for a report that carries none.
    ///     </para>
    /// </summary>
    private static string ResolveStageText(G9ProgressReport report) =>
        !string.IsNullOrWhiteSpace(report.Stage)
            ? report.Stage!
            : G9Strings.Get(G9StringKey.Loading);

    protected override void OnParentSet()
    {
        base.OnParentSet();

        if (Parent is not null)
        {
            return;
        }

        CancelCurrentProgressAnimation();
        this.AbortAnimation(TerminalCountdownAnimationName);
    }

    /// <summary>
    ///     Sets AnchorX on fill layers so the progress bar animates in the correct direction:
    ///     LTR → AnchorX = 0 (left to right), RTL → AnchorX = 1 (right to left).
    /// </summary>
    private void ApplyFillLayerDirection()
    {
        var anchorX = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft ? 1d : 0d;
        ExpandedFillLayer.AnchorX = anchorX;
        CompactFillLayer.AnchorX = anchorX;
    }

    // Runs a synchronous gesture handler body without ever letting an exception escape into the
    // MAUI input loop (a throw from a gesture handler can tear down the page).
    private static void SafeRun(Action action)
    {
        try
        {
            action();
        }
        catch
        {
            // Swallow — the overlay is best-effort UI; a handler fault must never crash the app.
        }
    }

    private static void ObserveAnimationTask(Task task)
    {
        _ = task.ContinueWith(
            static completedTask => _ = completedTask.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    #endregion
}
