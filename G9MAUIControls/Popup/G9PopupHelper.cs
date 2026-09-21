using G9MAUIControls.Hosting;
using G9MAUIControls.Controls;
using G9MAUIControls.Popup;
using G9MAUIControls.Helpers;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Shapes;
using System.Windows.Input;
using Keyboard = Microsoft.Maui.Keyboard;

// G9PopupHelper lives in the G9Popup component folder; its namespace follows the folder path
// (Components.G9Popup). The G9PopupView control + per-call open options are the cross-platform
// building blocks it renders.
using G9MAUIControls.Icons;

namespace G9MAUIControls.Popup;

/// <summary>
///     Manages popup queue, presentation, configuration, and styling using the in-house
///     <see cref="G9PopupView" /> control. Replaces the previous SfG9Popup-backed
///     <c>G9PopupHelper</c>; the public API is unchanged so callers are not affected.
/// </summary>
/// <remarks>
///     Lives in the <c>Common/Components/G9Popup</c> folder; its namespace follows the folder
///     path (<c>G9MAUIControls.Popup</c>).
/// </remarks>
public static class G9PopupHelper
{
    private static readonly SemaphoreSlim QueueGate = new(1, 1);

    // A linked list rather than a Queue because a popup requested from INSIDE a running button
    // callback has to go to the front — see SignalPumpIfBlockedOnUserCode.
    private static readonly LinkedList<G9PopupRequest> PendingRequests = new();
    private static G9PopupSettings _defaultSettings = G9PopupSettings.CreateDefault();
    private static bool _isProcessing;

    /// <summary>
    ///     The request the queue pump is presenting right now; <c>null</c> while the pump is idle or
    ///     after it let a request go (see <see cref="TryParkOrRearm" />). Read from any thread by
    ///     <see cref="SignalPumpIfBlockedOnUserCode" />.
    /// </summary>
    private static volatile G9PopupRequest? _pumpRequest;

    /// <summary>
    ///     The request whose content is mounted in the popup view. UI thread only. It is what stops a
    ///     button callback that finishes late from closing a DIFFERENT popup that has since taken
    ///     over the (single, shared) view.
    /// </summary>
    private static G9PopupRequest? _mountedRequest;

    /// <summary>Bumped by <see cref="DismissAllG9PopupsAsync" />; see <see cref="G9PopupRequest.ParkedAtDismissEpoch" />.</summary>
    private static int _dismissEpoch;

    // GetServiceNullable THROWS while G9ServiceProvider is uninitialized, and this getter is read
    // from catch blocks on the queue pump — where a second throw used to escape the pump and wedge
    // the queue for the life of the process. Logging is never worth that.
    private static ILogger? Logger
    {
        get
        {
            try
            {
                return G9ServiceProvider.GetServiceNullable<ILoggerFactory>()?.CreateLogger("G9PopupViewHelper");
            }
            catch
            {
                return null;
            }
        }
    }

    private const double HeaderTitleFontSize = 17;
    private const double BodyMessageFontSize = 15;
    private const double FooterButtonFontSize = 15;
    private const double InputMessageFontSize = 15;
    private const double InputFieldLabelFontSize = 14;
    private const double ValidationMessageFontSize = 13;
    private const double FooterButtonMinHeight = 48;

    /// <summary>
    ///     Updates the global defaults used for all popups (per-call settings still win).
    /// </summary>
    public static void ConfigureG9PopupDefaults(G9PopupSettings settings)
    {
        _defaultSettings = settings.WithDefaults(G9PopupSettings.CreateDefault());
    }

    /// <summary>Shows an information popup (overlay).</summary>
    public static Task<G9PopupResult> ShowG9PopupAsync(
        string message,
        string? title = null,
        IEnumerable<G9PopupButton>? buttons = null,
        G9PopupSettings? settings = null,
        G9PopupAnimationType animation = G9PopupAnimationType.SlideUp)
    {
        var descriptor = G9PopupDescriptor.ForPreset(G9PopupType.Information, message, title, buttons, settings, animation);
        return EnqueueAsync(descriptor);
    }

    /// <summary>Shows a success popup.</summary>
    public static Task<G9PopupResult> ShowSuccessG9PopupAsync(
        string message,
        string? title = null,
        IEnumerable<G9PopupButton>? buttons = null,
        G9PopupSettings? settings = null,
        G9PopupAnimationType animation = G9PopupAnimationType.SlideUp)
    {
        var descriptor = G9PopupDescriptor.ForPreset(G9PopupType.Success, message, title, buttons, settings, animation);
        return EnqueueAsync(descriptor);
    }

    /// <summary>Shows a warning popup.</summary>
    public static Task<G9PopupResult> ShowWarningG9PopupAsync(
        string message,
        string? title = null,
        IEnumerable<G9PopupButton>? buttons = null,
        G9PopupSettings? settings = null,
        G9PopupAnimationType animation = G9PopupAnimationType.SlideUp)
    {
        var descriptor = G9PopupDescriptor.ForPreset(G9PopupType.Warning, message, title, buttons, settings, animation);
        return EnqueueAsync(descriptor);
    }

    /// <summary>Shows an error popup.</summary>
    public static Task<G9PopupResult> ShowErrorG9PopupAsync(
        string message,
        string? title = null,
        IEnumerable<G9PopupButton>? buttons = null,
        G9PopupSettings? settings = null,
        G9PopupAnimationType animation = G9PopupAnimationType.SlideUp)
    {
        var descriptor = G9PopupDescriptor.ForPreset(G9PopupType.Error, message, title, buttons, settings, animation);
        return EnqueueAsync(descriptor);
    }

    public static Task<G9PopupResult> ShowCustomG9PopupAsync(
        View view,
        string? title = null,
        IEnumerable<G9PopupButton>? buttons = null,
        G9PopupSettings? settings = null,
        G9PopupAnimationType animation = G9PopupAnimationType.SlideUp)
    {
        var descriptor = G9PopupDescriptor.ForCustom(view, title, buttons, settings, animation);
        return EnqueueAsync(descriptor);
    }

    /// <summary>
    ///     Routes a hardware/system back press to the foreground popup, if any. Returns
    ///     <c>true</c> when a popup is open (the press is consumed): a cancelable popup
    ///     (<see cref="G9PopupView.ClosesOnBackButton" />) is closed, a non-cancelable one is kept
    ///     open but still swallows the press so it never falls through to the page underneath.
    ///     Returns <c>false</c> when no popup is open, so the caller can keep walking the back
    ///     chain (bottom sheet, page, exit prompt). Called by <c>AppBackCoordinator</c>.
    /// </summary>
    public static bool TryHandleHardwareBack()
    {
        if (!G9ModalHostRegistry.TryGetCurrentHost(out var host) || !host.G9Popup.IsOpen)
        {
            return false;
        }

        if (host.G9Popup.ClosesOnBackButton)
        {
            _ = host.G9Popup.CloseAsync();
        }

        return true;
    }

    /// <summary>
    ///     Closes the foreground popup if one is open. Best-effort, main-thread-safe. Used by the
    ///     device-clock gate to dismiss its non-cancelable blocking popup the moment the clock is
    ///     fixed (e.g. on app resume after the user enabled automatic time), since such a popup has
    ///     no user-facing close affordance of its own.
    /// </summary>
    public static Task CloseActiveG9PopupAsync()
    {
        if (!G9ModalHostRegistry.TryGetCurrentHost(out var host) || !host.G9Popup.IsOpen)
        {
            return Task.CompletedTask;
        }

        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (host.G9Popup.IsOpen)
            {
                await host.G9Popup.CloseAsync().ConfigureAwait(true);
            }
        });
    }

    /// <summary>Shows an input popup with one or more fields and returns entered values.</summary>
    public static async Task<G9PopupInputResult> ShowInputG9PopupAsync(G9PopupInputOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var fields = options.Fields
            .Where(field => !string.IsNullOrWhiteSpace(field.Key))
            .ToList();
        if (fields.Count == 0)
        {
            return G9PopupInputResult.Cancel();
        }

        var duplicateKey = fields
            .GroupBy(field => field.Key, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicateKey is not null)
        {
            throw new ArgumentException(
                $@"G9Popup input field key '{duplicateKey.Key}' is duplicated.",
                nameof(options));
        }

        foreach (var radioField in fields.Where(x => x.Type == G9PopupInputFieldType.RadioButton))
        {
            var optionCount = radioField.Items?.Count(option => !string.IsNullOrWhiteSpace(option.Value)) ?? 0;
            if (optionCount < 2)
            {
                throw new ArgumentException(
                    $@"RadioButton field '{radioField.Key}' must define at least two items.",
                    nameof(options));
            }
        }

        var runtimeFields = new List<G9PopupInputRuntimeField>(fields.Count);
        var formView = BuildInputG9PopupView(options, fields, runtimeFields);
        var finalResult = G9PopupInputResult.Cancel();

        var cancelBase = options.CancelButton
                         ?? new G9PopupButton { Text = options.CancelButtonText ?? ResolveCancelText() };

        var submitBase = options.SubmitButton
                         ?? new G9PopupButton { Text = options.SubmitButtonText ?? ResolveSubmitText() };

        var buttons = new[]
        {
            cancelBase with
            {
                IsPrimary = false,
                CallbackAsync = _ =>
                {
                    finalResult = G9PopupInputResult.Cancel();
                    return Task.FromResult(G9PopupResult.Close());
                }
            },
            submitBase with
            {
                IsPrimary = true,
                CallbackAsync = _ =>
                {
                    var (isValid, values, arrayValues, errors) = ValidateInputFields(runtimeFields);

                    finalResult = isValid
                        ? G9PopupInputResult.Submit(values, arrayValues)
                        : G9PopupInputResult.ValidationFailed(values, arrayValues, errors);

                    return Task.FromResult(isValid ? G9PopupResult.Close() : G9PopupResult.NoAction());
                }
            }
        };

        var settings = (options.Settings ?? new G9PopupSettings()).WithDefaults(new G9PopupSettings
        {
            ShowHeader = true,
            ShowFooter = true,
            ShowCloseButton = false,
            CloseOnBackgroundClick = false,
            AutoSizeMode = G9PopupViewAutoSizeMode.Height,
            Padding = new Thickness(20, 12, 20, 8)
        });

        var descriptor = G9PopupDescriptor.ForCustom(
            formView,
            options.Title,
            buttons,
            settings,
            options.Animation,
            options.Type);

        await EnqueueAsync(descriptor).ConfigureAwait(false);
        return finalResult;
    }

    /// <summary>Clears pending popups (the current one remains).</summary>
    public static async Task ClearG9PopupQueueAsync()
    {
        List<G9PopupRequest> dropped;
        await QueueGate.WaitAsync().ConfigureAwait(false);
        try
        {
            dropped = PendingRequests.ToList();
            PendingRequests.Clear();
        }
        finally
        {
            QueueGate.Release();
        }

        foreach (var pending in dropped)
        {
            pending.Completion.TrySetResult(G9PopupResult.Close());
        }
    }

    /// <summary>Dismisses the active popup (if any) and clears the queue.</summary>
    public static async Task DismissAllG9PopupsAsync()
    {
        // A popup whose button callback is still running while a nested popup is on screen is in
        // neither the queue nor the view, so nothing below can reach it. The epoch is how it learns
        // that it was dismissed and must not re-present itself (G9PopupRequest.ParkedAtDismissEpoch).
        Interlocked.Increment(ref _dismissEpoch);

        await ClearG9PopupQueueAsync().ConfigureAwait(false);

        if (!G9ModalHostRegistry.TryGetCurrentHost(out var host))
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (host.G9Popup.IsOpen)
            {
                await host.G9Popup.CloseAsync().ConfigureAwait(true);
            }
        });
    }

    /// <summary>
    ///     The shared two-button confirm.
    ///     <para>
    ///         <b>Cancel is NOT primary.</b> Both buttons used to be <c>IsPrimary = true</c>, so every
    ///         confirm in the app painted TWO solid accent buttons side by side — the exact shape the
    ///         design system forbids ("secondary / Cancel / Decline / Dismiss → Outline, never a
    ///         second primary", `08-UI-UX-Design-System.md` §4c). Cancel is now the outline button and
    ///         only OK carries the accent.
    ///     </para>
    ///     <para>
    ///         <paramref name="type" /> picks that accent (and the header icon). Default stays
    ///         <see cref="G9PopupType.Information" /> so existing call sites are unchanged; pass
    ///         <see cref="G9PopupType.Warning" /> when confirming means LOSING something the user would
    ///         miss — signing out, discarding work — so the popup reads as a caution rather than a
    ///         neutral notice.
    ///     </para>
    ///     <para>
    ///         <b>Returns <c>false</c> for every exit that is not the OK button</b> — Cancel, hardware
    ///         back, <see cref="DismissAllG9PopupsAsync" />, <see cref="ClearG9PopupQueueAsync" />,
    ///         <see cref="CloseActiveG9PopupAsync" />, no visible host, a presentation failure. It used
    ///         to await a flag that only the two buttons ever set, so any other dismissal suspended the
    ///         caller forever — and, under <c>G9SafeCommand</c>, held its concurrency key forever too,
    ///         which is how a button "stopped responding until the app restarts".
    ///     </para>
    /// </summary>
    public static async Task<bool> ShowConfirmAsync(
        string message,
        string? title = null,
        Func<CancellationToken, Task>? okCallback = null,
        Func<CancellationToken, Task>? cancelCallback = null,
        G9PopupType type = G9PopupType.Information)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var ok = G9Strings.Get(G9StringKey.Ok);
        var cancel = G9Strings.Get(G9StringKey.Cancel);

        var buttons = new[]
        {
            new G9PopupButton
            {
                Text = cancel,
                IsPrimary = false,
                CallbackAsync = async ct =>
                {
                    // Try*, not Set*: the answer is decided once, and a second delivery (a double
                    // tap that beats the footer's re-entrancy gate, a future caller) must be a
                    // no-op rather than an InvalidOperationException on the UI thread.
                    tcs.TrySetResult(false);
                    if (cancelCallback is not null)
                    {
                        await cancelCallback(ct);
                    }

                    return G9PopupResult.Close();
                }
            },
            new G9PopupButton
            {
                Text = ok,
                IsPrimary = true,
                CallbackAsync = async ct =>
                {
                    tcs.TrySetResult(true);
                    if (okCallback is not null)
                    {
                        await okCallback(ct);
                    }

                    return G9PopupResult.Close();
                }
            }
        };

        var descriptor = G9PopupDescriptor.ForPreset(type, message, title, buttons, null, G9PopupAnimationType.SlideUp);
        await EnqueueAsync(descriptor);

        // EnqueueAsync returns on EVERY way a popup can end, the buttons being only two of them. If
        // neither button answered by now, nothing ever will: the popup is gone. That is a "no".
        tcs.TrySetResult(false);

        return await tcs.Task;
    }


    /// <summary>
    ///     One queued / presented popup. A class (it was a positional record) because it now carries
    ///     the mutable hand-over state between the queue pump and the footer-button handler.
    /// </summary>
    private sealed class G9PopupRequest(
        G9PopupDescriptor descriptor,
        G9PopupSettings settings,
        TaskCompletionSource<G9PopupResult> completion)
    {
        private int _buttonGate;

        public G9PopupDescriptor Descriptor { get; } = descriptor;
        public G9PopupSettings Settings { get; } = settings;
        public TaskCompletionSource<G9PopupResult> Completion { get; } = completion;

        /// <summary>Guards the four members below. Never held across an await or while taking <c>QueueGate</c>.</summary>
        public Lock SyncRoot { get; } = new();

        /// <summary>
        ///     True while consumer code belonging to this popup is executing — a footer button's
        ///     <c>CallbackAsync</c>, or the <c>AfterCloseAsync</c> hook the pump is waiting on. That
        ///     code may itself request a popup and await it, so while this is set the pump must be
        ///     releasable (see <see cref="PreemptSignal" />).
        /// </summary>
        public bool IsUserCodeRunning { get; set; }

        /// <summary>
        ///     True once the pump stopped waiting on this request so that a nested popup could be
        ///     shown. From then on the button handler — not the pump — finishes the request.
        /// </summary>
        public bool IsParked { get; set; }

        /// <summary>
        ///     <c>_dismissEpoch</c> at the moment the request was parked. If it has moved by the time a
        ///     parked popup wants to come back, <c>DismissAllG9PopupsAsync</c> ran in between and the
        ///     popup must stay gone (a form re-appearing on the login page after sign-out).
        /// </summary>
        public int ParkedAtDismissEpoch { get; set; }

        /// <summary>Completed by an enqueue that finds this request's user code running. Wakes the pump.</summary>
        public TaskCompletionSource<bool> PreemptSignal { get; set; } = NewSignal();

        /// <summary>The view this request was mounted in. UI thread only.</summary>
        public G9PopupView? PresentedOn { get; set; }

        /// <summary>
        ///     Footer re-entrancy gate, per REQUEST rather than per button: a double tap must not run a
        ///     Delete callback twice, and OK-then-Cancel must not run both.
        /// </summary>
        public bool TryEnterButton() => Interlocked.CompareExchange(ref _buttonGate, 1, 0) == 0;

        public void ExitButton() => Interlocked.Exchange(ref _buttonGate, 0);

        public static TaskCompletionSource<bool> NewSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed record G9PopupInputRuntimeField(
        G9PopupInputField Descriptor,
        Func<string> ReadValue,
        Func<IReadOnlyList<string>>? ReadArrayValues,
        Action? FocusAction,
        Action<bool, string?>? SetValidation);

    #region Queue Processing

    // ─────────────────────────────────────────────────────────────────────────────────────────────
    // How the queue avoids deadlocking on itself
    //
    // One pump presents one popup at a time, and a popup is "done" only when a footer button's
    // CallbackAsync has RETURNED. That callback is consumer code, and consumer code asks for popups:
    //
    //     ShowConfirmAsync(okCallback: _ => G9SafeCommand.RunAsync(Delete))   // Delete throws
    //
    // The error popup used to queue behind the confirm, which was waiting for the callback, which
    // was waiting for the error popup. Nothing ever moved again. AfterCloseAsync had the same shape.
    //
    // The rule now: THE PUMP NEVER WAITS ON CONSUMER CODE IT CANNOT BE WOKEN FROM. While a callback
    // (or AfterCloseAsync) runs, the request is flagged IsUserCodeRunning, and any enqueue that sees
    // the flag (a) goes to the FRONT of the queue and (b) completes the request's PreemptSignal. The
    // pump wakes, "parks" the request — stops waiting on it, leaves its callback running — and
    // presents the nested popup next. When the parked callback finally returns, the button handler
    // finishes the request itself: completes it and runs its tail, or, if the answer was DoNothing
    // (keep the popup open), puts the request back at the front so the pump shows it again.
    //
    // The flag is deliberately GLOBAL, not an AsyncLocal "am I inside the callback?" test. An
    // AsyncLocal does not flow through a platform dispatcher hop (Android's Handler.Post does not
    // carry ExecutionContext), so a nested request made after such a hop would look unrelated and
    // deadlock exactly as before. The price is small: an unrelated popup that happens to arrive
    // while a callback is running is shown straight away instead of after it. Never a hang.
    // ─────────────────────────────────────────────────────────────────────────────────────────────

    private static async Task<G9PopupResult> EnqueueAsync(G9PopupDescriptor descriptor)
    {
        var request = CreateRequest(descriptor);
        await AddToQueueAsync(request, false).ConfigureAwait(false);
        return await request.Completion.Task.ConfigureAwait(false);
    }

    private static G9PopupRequest CreateRequest(G9PopupDescriptor descriptor)
    {
        var tcs = new TaskCompletionSource<G9PopupResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resolvedSettings = ResolveSettings(descriptor.Settings, descriptor.Animation);
        return new G9PopupRequest(descriptor, resolvedSettings, tcs);
    }

    private static async Task AddToQueueAsync(G9PopupRequest request, bool forceFront)
    {
        var shouldStartProcessing = false;

        await QueueGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Evaluated inside the gate so the woken pump cannot look at the queue before the
            // request it was woken for is actually in it.
            if (forceFront || SignalPumpIfBlockedOnUserCode())
            {
                PendingRequests.AddFirst(request);
            }
            else
            {
                PendingRequests.AddLast(request);
            }

            if (!_isProcessing)
            {
                _isProcessing = true;
                shouldStartProcessing = true;
            }
        }
        finally
        {
            QueueGate.Release();
        }

        if (shouldStartProcessing)
        {
            _ = ProcessQueueAsync();
        }
    }

    /// <summary>
    ///     If the pump is currently waiting on consumer code (a button callback / AfterCloseAsync of
    ///     the popup it is presenting), wakes it and returns <c>true</c> — the caller then queues its
    ///     request at the front. See the block comment at the top of this region.
    /// </summary>
    private static bool SignalPumpIfBlockedOnUserCode()
    {
        var blocking = _pumpRequest;
        if (blocking is null)
        {
            return false;
        }

        lock (blocking.SyncRoot)
        {
            if (!blocking.IsUserCodeRunning)
            {
                return false;
            }

            blocking.PreemptSignal.TrySetResult(true);
            return true;
        }
    }

    private static async Task ProcessQueueAsync()
    {
        // This task is fire-and-forget, so NOTHING may escape it: an exception here used to leave
        // _isProcessing == true with no pump running, and every later popup — including every error
        // popup — queued forever behind a pump that no longer existed.
        try
        {
            while (true)
            {
                G9PopupRequest? request = null;
                await QueueGate.WaitAsync().ConfigureAwait(false);
                try
                {
                    if (PendingRequests.First is { } node)
                    {
                        request = node.Value;
                        PendingRequests.RemoveFirst();
                    }
                    else
                    {
                        _isProcessing = false;
                    }
                }
                finally
                {
                    QueueGate.Release();
                }

                if (request == null)
                {
                    return;
                }

                try
                {
                    await PresentAsync(request).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // PresentAsync guards itself; this is for a fault in its own cleanup. One bad
                    // popup costs that popup, never the queue.
                    LogErrorSafe(ex, "G9PopupViewHelper: presenting a popup failed; the queue continues.");
                    request.Completion.TrySetResult(G9PopupResult.Close());
                }
            }
        }
        catch (Exception ex)
        {
            LogErrorSafe(ex, "G9PopupViewHelper: the popup queue pump failed; resetting the queue.");
            await ResetQueueAfterPumpFailureAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    ///     Last resort for a fault in the pump's own bookkeeping: release every waiting caller and
    ///     leave <c>_isProcessing</c> false so the next request starts a fresh pump. Never throws.
    /// </summary>
    private static async Task ResetQueueAfterPumpFailureAsync()
    {
        List<G9PopupRequest> dropped = [];
        try
        {
            await QueueGate.WaitAsync().ConfigureAwait(false);
            try
            {
                dropped = [.. PendingRequests];
                PendingRequests.Clear();
                _isProcessing = false;
            }
            finally
            {
                QueueGate.Release();
            }
        }
        catch
        {
            // The gate itself is what failed. An unguarded write is still better than a flag that
            // stays true forever.
            _isProcessing = false;
        }

        foreach (var pending in dropped)
        {
            pending.Completion.TrySetResult(G9PopupResult.Close());
        }
    }

    /// <summary>
    ///     Called by the pump when <see cref="G9PopupRequest.PreemptSignal" /> fired before the request
    ///     completed. Returns <c>true</c> when the request is now parked (the pump must move on).
    /// </summary>
    private static bool TryParkOrRearm(G9PopupRequest request)
    {
        lock (request.SyncRoot)
        {
            if (request.Completion.Task.IsCompleted)
            {
                return false;
            }

            if (request.IsUserCodeRunning)
            {
                request.IsParked = true;
                request.ParkedAtDismissEpoch = Volatile.Read(ref _dismissEpoch);
                return true;
            }

            // The callback that raised the signal has already returned DoNothing: the popup is still
            // up, still interactive, and nobody is waiting on the pump any more. Keep presenting it
            // (the nested request simply shows afterwards, as it always did) and arm a fresh signal
            // for the next tap.
            request.PreemptSignal = G9PopupRequest.NewSignal();
            return false;
        }
    }

    /// <summary>Puts a parked request whose callback answered DoNothing back in front of the pump.</summary>
    private static Task RequeueParkedAsync(G9PopupRequest request)
    {
        lock (request.SyncRoot)
        {
            request.IsParked = false;
            request.PreemptSignal = G9PopupRequest.NewSignal();
        }

        return AddToQueueAsync(request, true);
    }

    #endregion

    #region Presentation

    private static async Task PresentAsync(G9PopupRequest request)
    {
        G9PopupView? popup = null;
        EventHandler? backgroundTappedHandler = null;
        EventHandler? closedHandler = null;
        var parked = false;

        _pumpRequest = request;
        try
        {
            if (request.Completion.Task.IsCompleted)
            {
                // A re-queued request that was completed while it waited (ClearG9PopupQueueAsync).
                return;
            }

            if (!G9ModalHostRegistry.TryGetCurrentHost(out var host))
            {
                Logger?.LogWarning(
                    "G9PopupViewHelper: no visible {G9PageBase} host was found. G9Popup skipped.",
                    nameof(G9PageBase));
                request.Completion.TrySetResult(G9PopupResult.Close());
                return;
            }

            popup = host.G9Popup;
            var profile = G9PopupVisualProfile.Create(request.Descriptor, request.Settings);

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (popup.IsOpen)
                {
                    // Close the previous popup first so its content fully detaches before the
                    // new one mounts. This matches the SfG9Popup queue behavior the helper had
                    // before the migration.
                    await popup.CloseAsync().ConfigureAwait(true);
                }

                var contentRoot = BuildG9PopupContent(request, profile);
                popup.SetContent(contentRoot);

                backgroundTappedHandler = (_, _) =>
                {
                    if (request.Settings.CloseOnBackgroundClick == true)
                    {
                        request.Completion.TrySetResult(G9PopupResult.Close());
                        _ = popup!.CloseAsync();
                    }
                };
                popup.BackgroundTapped += backgroundTappedHandler;

                closedHandler = (_, _) =>
                {
                    if (!request.Completion.Task.IsCompleted)
                    {
                        request.Completion.TrySetResult(G9PopupResult.Close());
                    }
                };
                popup.Closed += closedHandler;

                request.PresentedOn = popup;
                _mountedRequest = request;
                popup.Open(BuildOpenOptions(request.Settings, profile));
            }).ConfigureAwait(false);

            // Wait for the popup to finish — or for a nested request to need the pump.
            while (true)
            {
                Task preempted;
                lock (request.SyncRoot)
                {
                    preempted = request.PreemptSignal.Task;
                }

                await Task.WhenAny(request.Completion.Task, preempted).ConfigureAwait(false);

                if (request.Completion.Task.IsCompleted)
                {
                    break;
                }

                if (TryParkOrRearm(request))
                {
                    parked = true;
                    break;
                }
            }

            if (parked)
            {
                // The view is NOT closed here: the next PresentAsync closes it before mounting the
                // nested popup, and the parked request's own handlers come off in the finally below
                // so that close cannot complete it behind its callback's back.
                return;
            }

            var final = await request.Completion.Task.ConfigureAwait(false);

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (popup.IsOpen)
                {
                    await popup.CloseAsync().ConfigureAwait(true);
                }
            }).ConfigureAwait(false);

            await RunTailAsync(request, final, true).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogErrorSafe(ex, "G9PopupViewHelper failed to show popup.");
            request.Completion.TrySetResult(G9PopupResult.Close());

            if (popup is not null)
            {
                // Not CloseAsync: whatever just threw is quite likely to throw again, and a second
                // throw from here is how the pump used to die. The view must end up hidden and
                // input-transparent no matter what — it covers the whole page.
                await ForceHideAsync(popup, request).ConfigureAwait(false);
            }
        }
        finally
        {
            _pumpRequest = null;

            if (popup is not null)
            {
                try
                {
                    await MainThread.InvokeOnMainThreadAsync(() =>
                    {
                        if (backgroundTappedHandler is not null)
                        {
                            popup.BackgroundTapped -= backgroundTappedHandler;
                        }

                        if (closedHandler is not null)
                        {
                            popup.Closed -= closedHandler;
                        }

                        // A parked request stays "mounted" until something replaces or closes it:
                        // its button handler still needs to know whether the view is its to close.
                        if (!parked && ReferenceEquals(_mountedRequest, request))
                        {
                            _mountedRequest = null;
                        }
                    }).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    LogErrorSafe(ex, "G9PopupViewHelper failed to detach popup handlers.");
                }
            }
        }
    }

    /// <summary>
    ///     What happens after a popup has completed and closed: enqueue <c>ShowNext</c>, run
    ///     <c>AfterCloseAsync</c>. Runs on the pump for an ordinary popup, and on the button handler
    ///     for a parked one (the pump has long moved on). Never throws.
    /// </summary>
    private static async Task RunTailAsync(G9PopupRequest request, G9PopupResult final, bool heldByPump)
    {
        try
        {
            if (final.Action == G9PopupResultAction.ShowNext && final.NextG9Popup != null)
            {
                // Awaited (the add, not the popup) so it is in the queue BEFORE AfterCloseAsync
                // starts — otherwise it could land while IsUserCodeRunning is set and be mistaken
                // for a nested request, releasing the pump early.
                await AddToQueueAsync(CreateRequest(final.NextG9Popup), false).ConfigureAwait(false);
            }

            if (final.AfterCloseAsync is null)
            {
                return;
            }

            if (!heldByPump)
            {
                await RunAfterCloseGuardedAsync(final.AfterCloseAsync).ConfigureAwait(false);
                return;
            }

            // The pump waits for AfterCloseAsync, as documented — navigation / cleanup should not
            // race the next popup's open animation — but only until someone asks for a popup. If
            // that someone is AfterCloseAsync itself, waiting on would be the same deadlock as a
            // nested request from a button callback. The flag goes up BEFORE the hook is invoked:
            // it may request its popup synchronously, before its first await.
            Task preempted;
            lock (request.SyncRoot)
            {
                request.IsUserCodeRunning = true;
                preempted = request.PreemptSignal.Task;
            }

            try
            {
                var afterClose = RunAfterCloseGuardedAsync(final.AfterCloseAsync);
                await Task.WhenAny(afterClose, preempted).ConfigureAwait(false);
            }
            finally
            {
                lock (request.SyncRoot)
                {
                    request.IsUserCodeRunning = false;
                }
            }
        }
        catch (Exception ex)
        {
            LogErrorSafe(ex, "G9PopupViewHelper failed after closing a popup.");
        }
    }

    /// <summary>
    ///     Runs the consumer's AfterCloseAsync so that it can be abandoned by the pump without ever
    ///     surfacing as an unobserved task exception.
    /// </summary>
    private static async Task RunAfterCloseGuardedAsync(Func<Task> afterCloseAsync)
    {
        try
        {
            await afterCloseAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogErrorSafe(ex, "G9PopupViewHelper AfterCloseAsync failed.");
        }
    }

    /// <summary>Failure-path hide. Never throws; see <see cref="G9PopupView.ForceClose" />.</summary>
    private static async Task ForceHideAsync(G9PopupView popup, G9PopupRequest request)
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                popup.ForceClose();
                if (ReferenceEquals(_mountedRequest, request))
                {
                    _mountedRequest = null;
                }
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            LogErrorSafe(ex, "G9PopupViewHelper failed to force-hide the popup view.");
        }
    }

    private static void LogErrorSafe(Exception ex, string message)
    {
        try
        {
            Logger?.LogError(ex, "{Message}", message);
        }
        catch
        {
            // A logging provider that throws must not take the popup queue down with it.
        }
    }

    #endregion

    #region Configuration

    private static G9PopupViewOpenOptions BuildOpenOptions(G9PopupSettings settings, G9PopupVisualProfile profile)
    {
        return new G9PopupViewOpenOptions
        {
            Width = settings.Width,
            Height = settings.Height,
            CornerRadius = settings.CornerRadius ?? profile.CornerRadius,
            Padding = settings.Padding ?? profile.Padding,
            AutoSizeMode = settings.AutoSizeMode ?? G9PopupViewAutoSizeMode.Height,
            CardBackground = settings.CardBackgroundColor ?? profile.CardBackground,
            BorderColor = settings.BorderColor ?? profile.BorderColor,
            OverlayMode = settings.OverlayMode ?? G9PopupViewOverlayMode.Transparent,
            BlurIntensity = settings.BlurIntensity ?? G9PopupViewBlurIntensity.None,
            OverlayColor = settings.OverlayColor ?? profile.OverlayColor,
            OverlayOpacity = settings.OverlayOpacity ?? 0.45,
            CloseOnBackgroundTap = settings.CloseOnBackgroundClick ?? false,
            CloseOnBackButton = settings.CloseOnBackButton ?? true,
            Animation = profile.Animation,
            AnimationEasing = settings.AnimationEasing ?? G9PopupViewAnimationEasing.SinOut,
            AnimationDuration = settings.AnimationDuration ?? profile.AnimationDuration,
            AutoCloseDuration = settings.AutoCloseDuration ?? 0
            // Note: G9PopupViewOpenOptions also exposes RelativeView / RelativePosition /
            // AbsoluteX / AbsoluteY for parity with the legacy SfG9Popup.ShowRelativeToView API.
            // We don't surface those at the G9PopupSettings layer because no caller uses them —
            // the centered card is the only positioning the app actually needs. If a future
            // flow needs anchored positioning, add the corresponding G9PopupSettings fields and
            // forward them here.
        };
    }

    /// <summary>
    ///     Builds the content view that mounts inside <see cref="G9PopupView" />. Mirrors the
    ///     legacy SfG9Popup layout: header (icon badge + title), body (caller content / message),
    ///     and footer (1 or 2 action buttons stacked horizontally).
    /// </summary>
    private static View BuildG9PopupContent(G9PopupRequest request, G9PopupVisualProfile profile)
    {
        var palette = G9Palette.Current;
        var culturalFont = ResolveCulturalFont();
        var hasHeader = request.Settings.ShowHeader ?? true;
        var hasFooter = request.Settings.ShowFooter ?? true;

        var rootRows = "";
        if (hasHeader)
        {
            rootRows += "Auto,";
        }

        rootRows += "*";
        if (hasFooter)
        {
            rootRows += ",Auto";
        }

        var root = new Grid
        {
            // The card is auto-height (G9PopupViewAutoSizeMode.Height clears HeightRequest), so a
            // Star row collapses to 0 px inside it — that's the symptom that crashes Win2D
            // (SfTextInputLayout's hint draws into a 0-rect on Windows). Use Auto for the body
            // row so the body grows to its content's natural size and the card hugs the total.
            // For oversized content, callers wrap the body in a ScrollView (the input popup
            // does this) and we cap the card via MaximumHeightRequest in ApplyOptions so the
            // ScrollView gets bounded space and scrolls.
            RowDefinitions = new RowDefinitionCollection
            {
                new() { Height = hasHeader ? GridLength.Auto : new GridLength(0, GridUnitType.Absolute) },
                new() { Height = GridLength.Auto },
                new() { Height = hasFooter ? GridLength.Auto : new GridLength(0, GridUnitType.Absolute) }
            },
            RowSpacing = 0,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Transparent
        };

        if (hasHeader)
        {
            var header = BuildG9PopupHeader(request, profile, palette, culturalFont);
            Grid.SetRow(header, 0);
            root.Children.Add(header);
        }

        var body = BuildG9PopupBody(request, profile, palette, culturalFont);
        Grid.SetRow(body, 1);
        root.Children.Add(body);

        if (hasFooter)
        {
            var footer = BuildG9PopupFooter(request, profile, palette);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
        }

        return root;
    }

    private static View BuildG9PopupHeader(
        G9PopupRequest request,
        G9PopupVisualProfile profile,
        G9Palette palette,
        string culturalFont)
    {
        var titleText = ResolveTitle(request.Descriptor);
        var iconBadge = new Border
        {
            WidthRequest = 30,
            HeightRequest = 30,
            StrokeThickness = 0,
            BackgroundColor = profile.IconColor.WithAlpha(0.18f),
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            VerticalOptions = LayoutOptions.Center,
            Content = new G9IconView {
                Icon = profile.Icon,
                Color = profile.IconColor,
                Size = 17,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };

        var titleLabel = new Label
        {
            Text = titleText,
            FontSize = HeaderTitleFontSize,
            FontFamily = culturalFont,
            FontAttributes = FontAttributes.Bold,
            TextColor = profile.TitleColor,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

        var headerGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = GridLength.Auto },
                new() { Width = GridLength.Star }
            },
            ColumnSpacing = 10,
            Padding = new Thickness(0, 0, 0, 10)
        };
        Grid.SetColumn(iconBadge, 0);
        headerGrid.Children.Add(iconBadge);
        Grid.SetColumn(titleLabel, 1);
        headerGrid.Children.Add(titleLabel);

        var divider = new BoxView
        {
            HeightRequest = 1,
            Color = palette.OutlineVariant.WithAlpha(0.5f),
            HorizontalOptions = LayoutOptions.Fill
        };

        var headerContainer = new Grid
        {
            RowDefinitions = new RowDefinitionCollection
            {
                new() { Height = GridLength.Auto },
                new() { Height = GridLength.Auto }
            }
        };
        Grid.SetRow(headerGrid, 0);
        headerContainer.Children.Add(headerGrid);
        Grid.SetRow(divider, 1);
        headerContainer.Children.Add(divider);

        return headerContainer;
    }

    private static View BuildG9PopupBody(
        G9PopupRequest request,
        G9PopupVisualProfile profile,
        G9Palette palette,
        string culturalFont)
    {
        var hasCustomView = request.Descriptor.CustomView is not null;

        if (hasCustomView)
        {
            var customView = request.Descriptor.CustomView!;
            DetachFromParent(customView);

            return new ContentView
            {
                Padding = new Thickness(0, 12, 0, 12),
                Content = customView
            };
        }

        var message = request.Descriptor.Message ?? string.Empty;
        var label = new Label
        {
            Text = message,
            FontSize = BodyMessageFontSize,
            FontFamily = culturalFont,
            TextColor = profile.MessageColor,
            LineBreakMode = LineBreakMode.WordWrap,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start
        };
        SemanticProperties.SetDescription(label, message);

        return new ContentView
        {
            Padding = new Thickness(0, 12, 0, 12),
            Content = label
        };
    }

    private static View BuildG9PopupFooter(
        G9PopupRequest request,
        G9PopupVisualProfile profile,
        G9Palette palette)
    {
        var buttons = NormalizeButtons(request.Descriptor.Buttons);
        if (buttons.Count == 0)
        {
            return new BoxView { IsVisible = false };
        }

        // Stacked layout: one full-width button per row, top-to-bottom in caller order. Used for
        // 3+ buttons or long labels (device-clock gate) where the equal-column row would cram the
        // text. No 3-button cap here — vertical stacking scales to any count.
        if ((request.Settings.FooterButtonLayout ?? G9PopupFooterButtonLayout.Row)
            == G9PopupFooterButtonLayout.Stacked)
        {
            var stack = new VerticalStackLayout
            {
                Spacing = 8,
                Padding = new Thickness(0, 8, 0, 0)
            };

            foreach (var button in buttons)
            {
                stack.Children.Add(CreateFooterButton(button, request, profile, palette));
            }

            return stack;
        }

        if (buttons.Count > 3)
        {
            Logger?.LogWarning(
                "G9PopupViewHelper received {Count} buttons, but the popup footer renders at most 3. Extra buttons are ignored.",
                buttons.Count);
            buttons = [.. buttons.Take(3)];
        }

        var grid = new Grid
        {
            ColumnSpacing = 8,
            Padding = new Thickness(0, 8, 0, 0)
        };

        for (var i = 0; i < buttons.Count; i++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }

        for (var i = 0; i < buttons.Count; i++)
        {
            var button = buttons[i];
            var rendered = CreateFooterButton(button, request, profile, palette);
            Grid.SetColumn(rendered, i);
            grid.Children.Add(rendered);
        }

        return grid;
    }

    private static View CreateFooterButton(
        G9PopupButton button,
        G9PopupRequest request,
        G9PopupVisualProfile profile,
        G9Palette palette)
    {
        // Match the G9DesignSystem G9PopupView spec: secondary buttons use the outline style
        // (visible 1.5 px border + transparent background); primary buttons use the type's
        // accent color.
        var isPrimary = button.IsPrimary;
        var background = button.BackgroundColor
                         ?? (isPrimary ? profile.ButtonBackground : Colors.Transparent);
        // ButtonTextColor is the accent's own On* token, not a hard-coded white — see
        // G9PopupVisualProfile. A per-button TextColor override still wins (the exit prompt's
        // destructive "Exit" paints itself Error / OnError that way).
        var foreground = button.TextColor
                         ?? (isPrimary ? profile.ButtonTextColor : palette.OnSurface);
        var stroke = isPrimary ? Colors.Transparent : palette.OutlineVariant.WithAlpha(0.85f);

        var buttonBorder = new Border
        {
            BackgroundColor = background,
            Stroke = stroke,
            StrokeThickness = isPrimary ? 0 : 1.5,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = new Thickness(0),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Center,
            MinimumHeightRequest = FooterButtonMinHeight,
            Content = new Label
            {
                Text = button.Text,
                FontSize = FooterButtonFontSize,
                FontAttributes = FontAttributes.Bold,
                FontFamily = ResolveCulturalFont(),
                TextColor = foreground,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            // Visual press feedback — instant scale-down + fade, then snap back when the
            // callback returns. Keeps the press tactile without needing a custom animation
            // controller per button.
            //
            // ExecuteG9PopupButtonAsync resolves the view from the REQUEST (the one it was mounted
            // in), not from "the current host": with more than one G9PageBase alive those can be two
            // different popups, and the tap belongs to the one the button is sitting in.
            _ = AnimateButtonPressAsync(buttonBorder, () => ExecuteG9PopupButtonAsync(button, request));
        };
        buttonBorder.GestureRecognizers.Add(tap);
        return buttonBorder;
    }

    private static async Task AnimateButtonPressAsync(VisualElement target, Func<Task> action)
    {
        try
        {
            await Task.WhenAll(
                target.ScaleToAsync(0.96, 80, Easing.SinIn),
                target.FadeToAsync(0.85, 80, Easing.SinIn));
        }
        catch
        {
            // Swallow — animation aborts during navigation.
        }

        await action().ConfigureAwait(true);

        try
        {
            await Task.WhenAll(
                target.ScaleToAsync(1.0, 120, Easing.SinOut),
                target.FadeToAsync(1.0, 120, Easing.SinOut));
        }
        catch
        {
            // Swallow — animation aborts during navigation.
        }
    }

    #endregion

    #region Button Commands

    /// <summary>
    ///     Runs a footer button. Always entered on the UI thread (it comes from a tap) and never
    ///     throws — it is awaited by a fire-and-forget press animation.
    /// </summary>
    private static async Task ExecuteG9PopupButtonAsync(G9PopupButton button, G9PopupRequest request)
    {
        // One button run per popup at a time. Without this a double tap ran a Delete callback twice,
        // and OK followed quickly by Cancel ran BOTH callbacks of the same confirm.
        if (!request.TryEnterButton())
        {
            return;
        }

        // Held for good once the popup has an answer; handed back only when it stays open
        // (DoNothing — e.g. the input form failed validation and the user must be able to retry).
        var releaseGate = true;

        try
        {
            // Stale content: this request is no longer what the view shows, or the view is already
            // closing. (Was: "is the current host's popup open?", which could be another page's.)
            if (!ReferenceEquals(_mountedRequest, request) || request.PresentedOn is not { IsOpen: true })
            {
                return;
            }

            G9PopupResult result;

            // Raised BEFORE the callback is invoked, not after its first await: it may request its
            // nested popup synchronously. See the block comment in "Queue Processing".
            lock (request.SyncRoot)
            {
                request.IsUserCodeRunning = true;
            }

            try
            {
                // ConfigureAwait(true): everything below touches views. This used to be (false),
                // and with Animation = None the CloseAsync that followed wrote IsVisible /
                // InputTransparent from a thread-pool thread.
                result = button.CallbackAsync != null
                    ? await button.CallbackAsync(CancellationToken.None).ConfigureAwait(true)
                    : G9PopupResult.Close();
            }
            catch (Exception ex)
            {
                LogErrorSafe(ex, "G9PopupViewHelper button callback failed.");
                result = G9PopupResult.Close();
            }

            var keepOpen = result.Action == G9PopupResultAction.DoNothing;
            bool parked;
            bool dismissedWhileParked;

            // One lock decides who finishes the request — the pump or this handler — so that its
            // tail (ShowNext / AfterCloseAsync) runs exactly once. TryParkOrRearm is the other side.
            lock (request.SyncRoot)
            {
                request.IsUserCodeRunning = false;
                parked = request.IsParked;
                dismissedWhileParked =
                    parked && request.ParkedAtDismissEpoch != Volatile.Read(ref _dismissEpoch);

                if (!keepOpen)
                {
                    request.Completion.TrySetResult(result);
                }
            }

            if (keepOpen)
            {
                if (!parked)
                {
                    return;
                }

                // The popup wants to stay open, but a nested popup has taken its place in the view
                // while the callback ran. Bring it back — unless everything was dismissed meanwhile.
                if (dismissedWhileParked)
                {
                    request.Completion.TrySetResult(G9PopupResult.Close());
                    return;
                }

                await RequeueParkedAsync(request).ConfigureAwait(true);
                return;
            }

            releaseGate = false;

            await CloseIfMountedAsync(request).ConfigureAwait(true);

            if (parked)
            {
                // The pump let this request go; nobody else will run its tail.
                await RunTailAsync(request, result, false).ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            LogErrorSafe(ex, "G9PopupViewHelper failed to complete a popup button.");
            releaseGate = false;
            request.Completion.TrySetResult(G9PopupResult.Close());

            if (request.PresentedOn is { } popup && ReferenceEquals(_mountedRequest, request))
            {
                await ForceHideAsync(popup, request).ConfigureAwait(true);
            }
        }
        finally
        {
            if (releaseGate)
            {
                request.ExitButton();
            }
        }
    }

    /// <summary>
    ///     Closes the popup view on behalf of <paramref name="request" /> — on the UI thread, and only
    ///     while that request's content is still what the view shows. A callback that finishes after a
    ///     nested popup took over the view must not close the nested popup.
    /// </summary>
    private static Task CloseIfMountedAsync(G9PopupRequest request)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (!ReferenceEquals(_mountedRequest, request))
            {
                return;
            }

            if (request.PresentedOn is { IsOpen: true } popup)
            {
                await popup.CloseAsync().ConfigureAwait(true);
            }

            if (ReferenceEquals(_mountedRequest, request))
            {
                _mountedRequest = null;
            }
        });
    }

    #endregion

    #region Input G9Popup

    private static View BuildInputG9PopupView(
        G9PopupInputOptions options,
        IReadOnlyList<G9PopupInputField> fields,
        ICollection<G9PopupInputRuntimeField> runtimeFields)
    {
        var theme = G9Palette.Current;
        var culturalFont = G9Culture.ResolveAppFont("CulturalFont", G9Culture.RtlFontFamily);
        var englishFont = G9Culture.ResolveAppFont("EnglishFont", G9Culture.LtrFontFamily);

        var root = new VerticalStackLayout { Spacing = 12, Padding = new Thickness(4, 2, 4, 4) };

        if (!string.IsNullOrWhiteSpace(options.Message))
        {
            root.Add(new Label
            {
                Text = options.Message,
                FontSize = InputMessageFontSize,
                FontFamily = culturalFont,
                TextColor = theme.TextSecondary,
                LineBreakMode = LineBreakMode.WordWrap
            });
        }

        var fieldsLayout = new VerticalStackLayout { Spacing = 10 };

        foreach (var field in fields)
        {
            var isSelectionField = field.Type is G9PopupInputFieldType.CheckBox or G9PopupInputFieldType.RadioButton;

            var (fieldContent, readValue, readArrayValues, focusAction, setValidation) =
                CreateFieldControl(field, culturalFont, englishFont, theme);

            var fieldLayout = new VerticalStackLayout { Spacing = 4 };

            if (isSelectionField)
            {
                var titleText = field.IsRequired ? $"{field.Label} *" : field.Label;
                fieldLayout.Add(new Label
                {
                    Text = titleText, FontSize = InputFieldLabelFontSize, FontFamily = culturalFont, TextColor = theme.TextPrimary
                });
            }

            fieldLayout.Add(fieldContent);
            fieldsLayout.Add(fieldLayout);

            runtimeFields.Add(new G9PopupInputRuntimeField(
                field,
                readValue,
                readArrayValues,
                focusAction,
                setValidation));
        }

        if (options.FocusFirstFieldOnOpen)
        {
            root.Loaded += (_, _) =>
            {
                var firstFocusable = runtimeFields.FirstOrDefault(x => x.FocusAction is not null);
                firstFocusable?.FocusAction?.Invoke();
            };
        }

        root.Add(fieldsLayout);
        return new ScrollView { Content = root, Orientation = ScrollOrientation.Vertical };
    }

    private static (
        View Content,
        Func<string> ReadValue,
        Func<IReadOnlyList<string>>? ReadArrayValues,
        Action? FocusAction,
        Action<bool, string?>? SetValidation)
        CreateFieldControl(
            G9PopupInputField field,
            string culturalFont,
            string englishFont,
            G9Palette theme)
    {
        switch (field.Type)
        {
            case G9PopupInputFieldType.TextArea or G9PopupInputFieldType.Multiline:
                {
                    var fontFamily = field.FontFamily
                                     ?? (field.FlowDirection == FlowDirection.LeftToRight ? englishFont : culturalFont);
                    var hintText = field.IsRequired ? $"{field.Label} *" : field.Label;
                    var editor = new G9Editor
                    {
                        Text = field.InitialValue,
                        Label = hintText,
                        // A popup form is the "hard top edge" case the outlined-field system warns
                        // about: the first field butts against the popup header, so its floated
                        // label overhangs the box and gets clipped. Reserving the clearance keeps
                        // the label inside the field's own bounds. See G9Controls.md §3 and
                        // 08-UI-UX-Design-System.md §4. These are full-width stacked fields, not a
                        // height-matched lane, so there is no lane to break.
                        ReserveFloatingLabelClearance = true,
                        CustomFont = fontFamily,
                        KeyboardType = ResolveG9Keyboard(field),
                        IsReadOnly = field.IsReadOnly,
                        IsTextPredictionEnabled = field.IsTextPredictionEnabled,
                        IsSpellCheckEnabled = field.IsSpellCheckEnabled,
                        AutoSize = EditorAutoSizeOption.TextChanges,
                        MinimumEditorHeight = 90,
                        InputTextDirection = ResolveG9InputDirection(field),
                        VoiceEnabled = field.EnableVoice
                    };

                    if (field.MaxLength is { } editorMaxLength)
                    {
                        editor.MaxLength = editorMaxLength;
                    }

                    Action<bool, string?> setValidation = (hasError, msg) =>
                    {
                        editor.HasError = hasError;
                        editor.ErrorText = hasError ? msg ?? string.Empty : string.Empty;
                    };

                    return (editor, () => editor.Text ?? string.Empty, null, () => editor.Focus(),
                        setValidation);
                }
            case G9PopupInputFieldType.CheckBox:
                {
                    var options = ResolveCheckBoxOptions(field);
                    var optionsLayout = new VerticalStackLayout { Spacing = 6 };
                    var checkBoxes = new List<(G9PopupInputOption Option, G9Switch Control)>(options.Count);

                    foreach (var option in options)
                    {
                        var checkBox = new G9Switch
                        {
                            Title = option.Text,
                            IsOn = option.IsSelected,
                            IsInFormRow = true
                        };

                        ApplyInputFlowDirection(checkBox, field);
                        checkBoxes.Add((option, checkBox));
                        optionsLayout.Add(checkBox);
                    }

                    IReadOnlyList<string> ReadSelectedCheckBoxValues()
                    {
                        return checkBoxes
                            .Where(x => x.Control.IsOn)
                            .Select(x => x.Option.Value)
                            .ToArray();
                    }

                    var errorLabel = new Label
                    {
                        FontSize = ValidationMessageFontSize, FontFamily = culturalFont, TextColor = theme.Error, IsVisible = false
                    };

                    var checkBoxBorder = CreateInputBorder(theme, optionsLayout);
                    var wrapper = new VerticalStackLayout { Spacing = 4 };
                    wrapper.Add(checkBoxBorder);
                    wrapper.Add(errorLabel);

                    Action<bool, string?> setValidation = (hasError, msg) =>
                    {
                        checkBoxBorder.Stroke = hasError ? theme.Error : ResolveInputBorderColor(theme);
                        errorLabel.IsVisible = hasError;
                        errorLabel.Text = hasError ? msg ?? string.Empty : string.Empty;
                    };

                    return (
                        wrapper,
                        () => string.Join(", ", ReadSelectedCheckBoxValues()),
                        ReadSelectedCheckBoxValues,
                        () => checkBoxes.FirstOrDefault().Control?.Focus(),
                        setValidation);
                }
            case G9PopupInputFieldType.RadioButton:
                {
                    var radioOptions =
                        field.Items?.Where(option => !string.IsNullOrWhiteSpace(option.Value)).ToList() ?? [];
                    var selectedValue = field.InitialValue ??
                                        radioOptions.FirstOrDefault(option => option.IsSelected)?.Value;

                    // A unique group key keeps these radios mutually exclusive via the
                    // G9Switch single-selection registry, replacing the old manual
                    // StateChanged uncheck loop.
                    var radioGroup = $"G9PopupRadio:{Guid.NewGuid():N}";

                    var optionsLayout = new VerticalStackLayout { Spacing = 6 };
                    var radios =
                        new List<(G9PopupInputOption Option, G9Switch Control)>(radioOptions.Count);

                    foreach (var option in radioOptions)
                    {
                        var radio = new G9Switch
                        {
                            Title = option.Text,
                            IsOn =
                                string.Equals(selectedValue, option.Value, StringComparison.OrdinalIgnoreCase),
                            IsInFormRow = true,
                            SelectionGroup = radioGroup
                        };

                        ApplyInputFlowDirection(radio, field);
                        radios.Add((option, radio));
                        optionsLayout.Add(radio);
                    }

                    var errorLabel = new Label
                    {
                        FontSize = ValidationMessageFontSize, FontFamily = culturalFont, TextColor = theme.Error, IsVisible = false
                    };

                    var radioBorder = CreateInputBorder(theme, optionsLayout);
                    var wrapper = new VerticalStackLayout { Spacing = 4 };
                    wrapper.Add(radioBorder);
                    wrapper.Add(errorLabel);

                    Action<bool, string?> setValidation = (hasError, msg) =>
                    {
                        radioBorder.Stroke = hasError ? theme.Error : ResolveInputBorderColor(theme);
                        errorLabel.IsVisible = hasError;
                        errorLabel.Text = hasError ? msg ?? string.Empty : string.Empty;
                    };

                    return (
                        wrapper,
                        () => radios.Where(r => r.Control.IsOn).Select(r => r.Option.Value).FirstOrDefault() ??
                              string.Empty,
                        null,
                        () => radios.FirstOrDefault().Control.Focus(),
                        setValidation);
                }
            default:
                {
                    var fontFamily = field.FontFamily
                                     ?? (field.FlowDirection == FlowDirection.LeftToRight ? englishFont : culturalFont);
                    var isPassword = field.Type == G9PopupInputFieldType.Password;
                    var hintText = field.IsRequired ? $"{field.Label} *" : field.Label;
                    var entry = new G9TextEntry
                    {
                        Text = field.InitialValue,
                        Label = hintText,
                        // See the G9Editor branch above: a popup form is the hard-top-edge
                        // case, so the floated label needs its clearance reserved or the first
                        // field's label is clipped by the popup header.
                        ReserveFloatingLabelClearance = true,
                        CustomFont = fontFamily,
                        KeyboardType = ResolveG9Keyboard(field),
                        IsReadOnly = field.IsReadOnly,
                        IsPassword = isPassword,
                        PasswordToggle = isPassword,
                        ClearButton = true,
                        InputTextDirection = ResolveG9InputDirection(field),
                        VoiceEnabled = field.EnableVoice && !isPassword
                    };

                    if (field.MaxLength is { } maxLength)
                    {
                        entry.MaxLength = maxLength;
                    }

                    Action<bool, string?> setValidation = (hasError, msg) =>
                    {
                        entry.HasError = hasError;
                        entry.ErrorText = hasError ? msg ?? string.Empty : string.Empty;
                    };

                    return (entry, () => entry.Text ?? string.Empty, null, () => entry.Focus(),
                        setValidation);
                }
        }
    }

    private static void ApplyInputFlowDirection(VisualElement element, G9PopupInputField field)
    {
        if (field.FlowDirection is { } flowDirection)
        {
            element.FlowDirection = flowDirection;
        }
    }

    private static Border CreateInputBorder(G9Palette theme, View content, bool isTextArea = false)
    {
        return new Border
        {
            Stroke = ResolveInputBorderColor(theme),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            BackgroundColor = theme.Surface.WithAlpha(0.6f),
            Padding = isTextArea ? new Thickness(12, 8) : new Thickness(12, 4),
            Content = content
        };
    }

    private static Color ResolveInputBorderColor(G9Palette theme)
    {
        return theme.OutlineBorder.WithAlpha(0.75f);
    }

    private static bool ResolveInitialChecked(G9PopupInputField field)
    {
        if (field.InitialChecked is { } initialChecked)
        {
            return initialChecked;
        }

        return bool.TryParse(field.InitialValue, out var parsed) && parsed;
    }

    private static IReadOnlyList<G9PopupInputOption> ResolveCheckBoxOptions(G9PopupInputField field)
    {
        var options = field.Items?
            .Where(option => !string.IsNullOrWhiteSpace(option.Value))
            .ToList();

        if (options is { Count: > 0 })
        {
            return options;
        }

        var fallbackText = string.IsNullOrWhiteSpace(field.Placeholder) ? field.Label : field.Placeholder;
        return
        [
            G9PopupInputOption.Create("true", fallbackText, ResolveInitialChecked(field))
        ];
    }

    private static (
        bool IsValid,
        Dictionary<string, string> Values,
        Dictionary<string, IReadOnlyList<string>> ArrayValues,
        Dictionary<string, string> Errors)
        ValidateInputFields(IEnumerable<G9PopupInputRuntimeField> runtimeFields)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var arrayValues = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        G9PopupInputRuntimeField? firstInvalid = null;

        foreach (var runtimeField in runtimeFields)
        {
            var rawValue = runtimeField.ReadValue();
            var value = NormalizeFieldValue(runtimeField.Descriptor, rawValue);
            var selectedValues = runtimeField.ReadArrayValues?.Invoke() ?? [];
            if (selectedValues.Count > 0)
            {
                arrayValues[runtimeField.Descriptor.Key] = selectedValues;
                values[runtimeField.Descriptor.Key] = string.Join(", ", selectedValues);
            }
            else
            {
                values[runtimeField.Descriptor.Key] = value;
            }

            var errorMessage = ValidateInputField(runtimeField.Descriptor, value, selectedValues);
            if (string.IsNullOrWhiteSpace(errorMessage))
            {
                runtimeField.SetValidation?.Invoke(false, null);
                continue;
            }

            runtimeField.SetValidation?.Invoke(true, errorMessage);
            errors[runtimeField.Descriptor.Key] = errorMessage;
            firstInvalid ??= runtimeField;
        }

        if (firstInvalid is not null)
        {
            firstInvalid.FocusAction?.Invoke();
        }

        return (errors.Count == 0, values, arrayValues, errors);
    }

    private static string NormalizeFieldValue(G9PopupInputField field, string rawValue)
    {
        if (field.Type is G9PopupInputFieldType.CheckBox or G9PopupInputFieldType.RadioButton)
        {
            return rawValue;
        }

        return field.TrimValue ? rawValue.Trim() : rawValue;
    }

    private static string? ValidateInputField(
        G9PopupInputField field,
        string value,
        IReadOnlyList<string> selectedValues)
    {
        if (field.IsRequired)
        {
            if (field.Type == G9PopupInputFieldType.CheckBox)
            {
                if (selectedValues.Count == 0)
                {
                    return field.RequiredMessage ?? $"{field.Label} {ResolveRequiredFieldSuffix()}";
                }
            }
            else if (string.IsNullOrWhiteSpace(value))
            {
                return field.RequiredMessage ?? $"{field.Label} {ResolveRequiredFieldSuffix()}";
            }
        }

        if (field.Type == G9PopupInputFieldType.Email &&
            !string.IsNullOrWhiteSpace(value) &&
            !IsEmailFormatValid(value))
        {
            return ResolveInvalidEmailText();
        }

        return field.Validator?.Invoke(value);
    }

    private static G9KeyboardType ResolveG9Keyboard(G9PopupInputField field)
    {
        return field.Type switch
        {
            G9PopupInputFieldType.Email => G9KeyboardType.Email,
            G9PopupInputFieldType.Phone => G9KeyboardType.Phone,
            G9PopupInputFieldType.Number => G9KeyboardType.Number,
            _ => G9KeyboardType.Default
        };
    }

    private static G9TextInputDirection ResolveG9InputDirection(G9PopupInputField field)
    {
        return field.FlowDirection switch
        {
            FlowDirection.LeftToRight => G9TextInputDirection.LeftToRight,
            FlowDirection.RightToLeft => G9TextInputDirection.RightToLeft,
            _ => G9TextInputDirection.MatchParent
        };
    }

    private static bool IsEmailFormatValid(string value)
    {
        var atIndex = value.IndexOf('@');
        if (atIndex <= 0 || atIndex != value.LastIndexOf('@'))
        {
            return false;
        }

        var dotIndex = value.LastIndexOf('.');
        return dotIndex > atIndex + 1 && dotIndex < value.Length - 1;
    }

    private static string ResolveCancelText() => G9Strings.Get(G9StringKey.Cancel);
    private static string ResolveSubmitText() => G9Strings.Get(G9StringKey.Save);
    private static string ResolveRequiredFieldSuffix() => G9Strings.Get(G9StringKey.RequiredSuffix);
    private static string ResolveInvalidEmailText() => G9Strings.Get(G9StringKey.InvalidEmail);

    #endregion

    #region Resolvers

    private static G9PopupSettings ResolveSettings(G9PopupSettings? overrides, G9PopupAnimationType animation)
    {
        var merged = overrides?.WithDefaults(_defaultSettings) ?? _defaultSettings;
        var effectiveAnimation = overrides?.Animation ?? animation;
        return merged with { Animation = effectiveAnimation };
    }

    private static string ResolveTitle(G9PopupDescriptor descriptor)
    {
        if (!string.IsNullOrWhiteSpace(descriptor.Title))
        {
            return descriptor.Title;
        }

        var key = descriptor.Type switch
        {
            G9PopupType.Information => G9StringKey.Information,
            G9PopupType.Success => G9StringKey.Success,
            G9PopupType.Warning => G9StringKey.Warning,
            G9PopupType.Error => G9StringKey.Error,
            _ => G9StringKey.Information
        };

        return G9Strings.Get(key);
    }

    private static string ResolveOkText() => G9Strings.Get(G9StringKey.Ok);

    private static string ResolveCulturalFont() => G9Culture.ResolveFontFamily() ?? string.Empty;

    private static IReadOnlyList<G9PopupButton> NormalizeButtons(IEnumerable<G9PopupButton>? buttons)
    {
        var list = buttons?.ToList() ?? new List<G9PopupButton>();
        if (list.Count == 0)
        {
            list.Add(G9PopupButton.CloseButton(ResolveOkText()));
        }

        return list;
    }

    private static void DetachFromParent(View view)
    {
        switch (view.Parent)
        {
            case Layout layout:
                layout.Remove(view);
                break;
            case ContentView contentView when ReferenceEquals(contentView.Content, view):
                contentView.Content = null;
                break;
            case Border border when ReferenceEquals(border.Content, view):
                border.Content = null;
                break;
            case ScrollView scrollView when ReferenceEquals(scrollView.Content, view):
                scrollView.Content = null;
                break;
            case ContentPage page when ReferenceEquals(page.Content, view):
                page.Content = null;
                break;
        }
    }

    #endregion
}
