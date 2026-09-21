using G9MAUIControls.Popup;
using G9MAUIControls.Toast;
using G9MAUIControls.Controls;
using G9MAUIControls.Localization;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

namespace G9MAUIControls.Helpers;

/// <summary>
///     Unified static helper that combines throttle / double-tap prevention,
///     busy-state management, structured trace logging, automatic error popup
///     with admin-diagnostics "More details" integration, and ILogger output.
///     Replaces the former CommandThrottlerHelper and UiBusyOperationHelper.
///     <para>
///         Every external callback, logger call, localization lookup, and popup invocation
///         is individually guarded so that no secondary failure can propagate and crash
///         the application on any MAUI platform (Android, iOS, Windows, macOS).
///     </para>
/// </summary>
public static class G9SafeCommand
{
    // Throttle state is kept in Environment.TickCount64 milliseconds — a MONOTONIC clock — not wall
    // time. It used to be DateTime.UtcNow, and this library runs in an app with a device-clock gate:
    // when a clock that was AHEAD gets corrected, every stored timestamp is suddenly in the future,
    // "now - last" goes negative, and every key that had ever been tapped was rejected — with no
    // ageing out, because the cleanup compared against the same broken clock. Buttons went dead until
    // the app restarted. A tick count cannot be set by the user and never runs backwards.
    private static readonly ConcurrentDictionary<string, long> ThrottleTimestamps = new();
    private static readonly TimeSpan DefaultThrottleInterval = TimeSpan.FromMilliseconds(369);
    private static readonly TimeSpan CleanupThreshold = TimeSpan.FromMinutes(1);
    private static readonly ConcurrentDictionary<string, byte> InFlightOperations = new();
    private static long _lastCleanupTick = Environment.TickCount64;

    #region RunAsync – with G9OperationTrace

    /// <summary>
    ///     Executes an async action with throttle guard, busy-state management,
    ///     automatic error popup (with admin-diagnostics), and structured trace logging.
    ///     <para>
    ///         Does NOT use ConfigureAwait(false) on the main path so that the caller's
    ///         synchronization context (UI thread) is preserved for the action, SetBusy,
    ///         and OnError callbacks.
    ///     </para>
    ///     <para>
    ///         Guaranteed never to propagate an unhandled exception unless
    ///         <see cref="G9SafeCommandOptions.RethrowException" /> is explicitly true.
    ///     </para>
    /// </summary>
    public static async Task RunAsync(
        Func<G9OperationTrace, Task> action,
        G9SafeCommandOptions? options = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "")
    {
        G9SafeCommandOptions opts;
        string key;
        string source;
        G9OperationTrace? trace = null;
        ILogger? traceLogger;
        var enteredConcurrentGuard = false;

        try
        {
            ArgumentNullException.ThrowIfNull(action);
            opts = options ?? G9SafeCommandOptions.Default;
            key = opts.ThrottleKey ?? SafeBuildAutoKey(callerFile, callerMember);
            source = opts.Source ?? SafeGetFileName(callerFile);

            if (opts.EnableThrottle && !TryThrottle(key, opts.ThrottleInterval))
            {
                LogSafe(LogLevel.Debug, null, "Operation skipped by throttle guard", source, key);
                return;
            }

            if (opts.PreventConcurrentExecution && !TryEnterConcurrentGuard(key))
            {
                LogSafe(LogLevel.Debug, null, "Operation skipped by concurrent guard", source, key);
                return;
            }

            enteredConcurrentGuard = opts.PreventConcurrentExecution;

            traceLogger = ResolveLogger();
            trace = new G9OperationTrace(key, source, traceLogger);
        }
        catch (Exception initEx)
        {
            LogSafe(LogLevel.Critical, initEx, "SafeCommand initialization failed");
            return;
        }

        var executionDelay = ResolveExecutionDelay(opts);
        if (executionDelay > TimeSpan.Zero)
        {
            try
            {
                await Task.Delay(executionDelay);
            }
            catch (Exception delayEx)
            {
                LogSafe(LogLevel.Warning, delayEx, "SafeCommand delay failed", source, key);
            }
        }

        // A failure is only RECORDED inside the guarded region below; everything that waits on the
        // user — OnError, the error popup — happens after the finally has released the busy flag
        // and the concurrency key. The popup used to be awaited inside the guard, so the key was
        // held until the user dismissed it. Auto keys are File.Member, shared by every instance of a
        // view model: a tap on row B was silently dropped while row A's error popup was still open,
        // and if that popup never completed the button was dead until the app restarted.
        OperationFailure? failure = null;

        try
        {
            SafeInvokeSetBusy(opts.SetBusy, true, source, key);

            LogTraceStarted(source, key);
            var operationStopwatch = Stopwatch.StartNew();
            try
            {
                await ExecuteOnConfiguredThreadAsync(() => action(trace), opts.RunActionOnMainThread);
            }
            finally
            {
                LogTraceCompleted(source, key, operationStopwatch.ElapsedMilliseconds);
            }

            if (trace.HasFailed)
            {
                // trace.Fail(...) already wrote its own log line; this is the popup / observer half.
                failure = new OperationFailure(
                    trace.UserErrorMessage!,
                    trace.UserErrorDiagnostics ?? SafeBuildReport(trace),
                    null);
            }
        }
        catch (OperationCanceledException)
        {
            LogSafe(LogLevel.Debug, null, "Operation cancelled", source, key);
        }
        catch (ObjectDisposedException disposedEx)
        {
            // Still swallowed — it is almost always a view torn down under its own operation — but
            // no longer at Debug and without the exception: a production log (Information and up)
            // showed nothing at all, which made a real use-after-dispose bug a fully silent failure.
            LogSafe(LogLevel.Warning, disposedEx, "Object disposed during operation", source, key);
        }
        catch (Exception ex)
        {
            LogUnhandledOnce(trace, traceLogger, ex, "Unhandled exception in action", source, key);

            failure = new OperationFailure(
                opts.ErrorMessage ?? SafeGetDefaultErrorMessage(),
                SafeBuildReport(trace, ex),
                ex);
        }
        finally
        {
            SafeInvokeSetBusy(opts.SetBusy, false, source, key);

            if (enteredConcurrentGuard)
            {
                ExitConcurrentGuard(key);
            }
        }

        if (failure is null)
        {
            return;
        }

        // Guard released. From here on nothing is held, so these may take as long as the user does.
        RaiseOperationFailed(failure, source, key);

        if (failure.Exception is not null)
        {
            await SafeInvokeOnErrorAsync(opts.OnError, failure.Exception, source, key);
        }

        if (opts.ShowErrorG9Popup)
        {
            await SafeShowErrorG9PopupAsync(failure.UserMessage, opts.ErrorTitle, failure.Diagnostics, source);
        }

        if (failure.Exception is not null && opts.RethrowException)
        {
            // Was a bare `throw;` inside the catch. Rethrown from out here so the guard is already
            // released; ExceptionDispatchInfo keeps the original stack trace, as `throw;` did.
            ExceptionDispatchInfo.Capture(failure.Exception).Throw();
        }
    }

    #endregion

    #region RunAsync<T> – with return value

    /// <summary>
    ///     Executes an async function that produces a result.
    ///     Returns <c>default(T)</c> when throttled or on unhandled exception.
    /// </summary>
    public static async Task<T?> RunAsync<T>(
        Func<G9OperationTrace, Task<T>> action,
        G9SafeCommandOptions? options = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "")
    {
        T? result = default;
        await RunAsync(
            async trace => { result = await action(trace); },
            options, callerMember, callerFile);
        return result;
    }

    #endregion

    #region Error G9Popup (reusable from anywhere)

    /// <summary>
    ///     Shows an error popup with the given message. When admin-debug mode is enabled,
    ///     an additional "More details" button opens the <c>AdminDiagnosticsModal</c>
    ///     with the full diagnostics text and exception info.
    ///     Safe to call from any page or service — never throws.
    /// </summary>
    public static async Task ShowOperationErrorAsync(
        string message,
        string? title = null,
        string? diagnosticsText = null,
        IReadOnlyList<Exception>? exceptions = null,
        string? source = null)
    {
        await SafeShowErrorG9PopupAsync(message, title, diagnosticsText, source, exceptions);
    }

    #endregion

    #region RunAsync – simple (no trace)

    /// <summary>
    ///     Executes an async action without step-by-step tracing.
    ///     Still provides throttle guard, busy-state, and error popup.
    /// </summary>
    public static Task RunAsync(
        Func<Task> action,
        G9SafeCommandOptions? options = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "")
    {
        return RunAsync(_ => action(), options, callerMember, callerFile);
    }

    #endregion

    #region Option factories

    /// <summary>
    ///     Builds the standard traced options for UI operations that should reject same-key overlap.
    /// </summary>
    public static G9SafeCommandOptions CreateUiOperationOptions(string source, string throttleKey)
    {
        return new G9SafeCommandOptions
        {
            Source = source,
            ThrottleKey = throttleKey,
            EnableThrottle = false,
            ShowErrorG9Popup = true,
            PreventConcurrentExecution = true
        };
    }

    /// <summary>
    ///     Builds silent traced options for nested/background work that must not block sibling operations.
    /// </summary>
    public static G9SafeCommandOptions CreateSilentOperationOptions(string source, string throttleKey)
    {
        return new G9SafeCommandOptions
        {
            Source = source,
            ThrottleKey = throttleKey,
            EnableThrottle = false,
            ShowErrorG9Popup = false,
            PreventConcurrentExecution = false
        };
    }

    /// <summary>
    ///     Builds fire-and-forget options for observed event callbacks.
    /// </summary>
    public static G9SafeCommandOptions CreateFireAndForgetOperationOptions(string source, string throttleKey)
    {
        return new G9SafeCommandOptions
        {
            Source = source,
            ThrottleKey = throttleKey,
            EnableThrottle = false,
            PreventConcurrentExecution = false,
            ShowErrorG9Popup = false
        };
    }

    #endregion

    #region Throttle

    /// <summary>
    ///     Returns true when the action identified by <paramref name="key" /> is allowed to run now.
    ///     Thread-safe, automatically cleans up stale entries.
    /// </summary>
    public static bool TryThrottle(string key, TimeSpan? minInterval = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new ArgumentException(@"Key cannot be empty", nameof(key));
        }

        var intervalMs = (long)(minInterval ?? DefaultThrottleInterval).TotalMilliseconds;
        var now = Environment.TickCount64;

        // The decision is taken INSIDE the lambdas and carried out through this local. It used to be
        // inferred afterwards as "stored value == now", which let two calls landing on the same tick
        // both through: the second one's update returned `last`, and `last` was equal to `now`.
        // AddOrUpdate may run a factory more than once under contention; the last run is the one
        // whose value was stored, and each run overwrites the flag, so the flag matches the store.
        var allowed = false;

        ThrottleTimestamps.AddOrUpdate(
            key,
            _ =>
            {
                allowed = true;
                return now;
            },
            (_, last) =>
            {
                allowed = now - last >= intervalMs;
                return allowed ? now : last;
            });

        if (now - Interlocked.Read(ref _lastCleanupTick) > (long)CleanupThreshold.TotalMilliseconds)
        {
            CleanupThrottleEntries(now);
        }

        return allowed;
    }

    /// <summary>Clears all throttle state (useful for testing / logout).</summary>
    public static void ResetThrottle()
    {
        ThrottleTimestamps.Clear();
        InFlightOperations.Clear();
    }

    #endregion

    #region RunAsync – thread handoff

    /// <summary>
    ///     Runs a background phase, then dispatches a dependent phase on the main UI thread.
    /// </summary>
    public static Task RunAsync<TBackground>(
        Func<Task<TBackground>> runOnBackgroundThread,
        Func<TBackground, Task> runOnMainThread,
        G9SafeCommandOptions? options = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "")
    {
        ArgumentNullException.ThrowIfNull(runOnBackgroundThread);
        ArgumentNullException.ThrowIfNull(runOnMainThread);

        return RunAsync(
            _ => runOnBackgroundThread(),
            (value, _) => runOnMainThread(value),
            options,
            callerMember,
            callerFile);
    }

    /// <summary>
    ///     Runs a background phase with tracing, then dispatches a dependent phase on the main UI thread.
    /// </summary>
    public static Task RunAsync<TBackground>(
        Func<G9OperationTrace, Task<TBackground>> runOnBackgroundThread,
        Func<TBackground, G9OperationTrace, Task> runOnMainThread,
        G9SafeCommandOptions? options = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "")
    {
        ArgumentNullException.ThrowIfNull(runOnBackgroundThread);
        ArgumentNullException.ThrowIfNull(runOnMainThread);

        return RunAsync(
            async trace =>
            {
                var backgroundResult = await Task.Run(() => runOnBackgroundThread(trace))
                    .ConfigureAwait(false);
                await ExecuteOnMainThreadAsync(() => runOnMainThread(backgroundResult, trace));
            },
            options,
            callerMember,
            callerFile);
    }

    /// <summary>
    ///     Runs a background phase and then a main-thread phase that returns a final result.
    /// </summary>
    public static Task<TResult?> RunAsync<TBackground, TResult>(
        Func<Task<TBackground>> runOnBackgroundThread,
        Func<TBackground, Task<TResult>> runOnMainThread,
        G9SafeCommandOptions? options = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "")
    {
        ArgumentNullException.ThrowIfNull(runOnBackgroundThread);
        ArgumentNullException.ThrowIfNull(runOnMainThread);

        return RunAsync(
            _ => runOnBackgroundThread(),
            (value, _) => runOnMainThread(value),
            options,
            callerMember,
            callerFile);
    }

    /// <summary>
    ///     Runs a background phase with tracing and then a main-thread phase that returns a final result.
    /// </summary>
    public static async Task<TResult?> RunAsync<TBackground, TResult>(
        Func<G9OperationTrace, Task<TBackground>> runOnBackgroundThread,
        Func<TBackground, G9OperationTrace, Task<TResult>> runOnMainThread,
        G9SafeCommandOptions? options = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "")
    {
        ArgumentNullException.ThrowIfNull(runOnBackgroundThread);
        ArgumentNullException.ThrowIfNull(runOnMainThread);

        TResult? result = default;

        await RunAsync(
            async trace =>
            {
                var backgroundResult = await Task.Run(() => runOnBackgroundThread(trace))
                    .ConfigureAwait(false);
                result = await ExecuteOnMainThreadAsync(() => runOnMainThread(backgroundResult, trace));
            },
            options,
            callerMember,
            callerFile);

        return result;
    }

    #endregion

    #region RunSafe

    /// <summary>
    ///     Fire-and-forget wrapper for async UI/event callbacks.
    ///     Uses <see cref="RunAsync(Func{Task},G9SafeCommandOptions,string,string)" /> and
    ///     observes failures to avoid unhandled exceptions.
    /// </summary>
    public static void RunSafe(
        Func<Task> action,
        G9SafeCommandOptions? options = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "")
    {
        SafeFireAndForget(RunAsync(action, options, callerMember, callerFile));
    }

    /// <summary>
    ///     Fire-and-forget wrapper for synchronous UI/event callbacks.
    /// </summary>
    public static void RunSafe(
        Action action,
        G9SafeCommandOptions? options = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "")
    {
        SafeFireAndForget(RunAsync(() =>
        {
            action();
            return Task.CompletedTask;
        }, options, callerMember, callerFile));
    }

    #endregion

    #region Run – synchronous

    /// <summary>Synchronous overload with G9OperationTrace.</summary>
    /// <remarks>
    ///     <b>This overload blocks its calling thread, by contract</b> — when it returns, the action has
    ///     run. Two options therefore cost more here than in <c>RunAsync</c> / <c>RunSafe</c>:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>
    ///                 <see cref="G9SafeCommandOptions.DelayBeforeExecution" /> /
    ///                 <see cref="G9SafeCommandOptions.BusyDelay" /> are served by a blocking wait. On the
    ///                 UI thread that freezes the app for the whole delay. Use <c>RunSafe</c> when you
    ///                 need a delay.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 <see cref="G9SafeCommandOptions.RunActionOnMainThread" />, when called from a
    ///                 background thread, blocks that thread until the UI thread has run the action. If
    ///                 the UI thread is itself waiting on the caller, that is a deadlock. Called ON the
    ///                 UI thread (the normal case) the action simply runs inline and nothing waits.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     Neither wait can be removed without breaking the "has run when it returns" guarantee that
    ///     synchronous callers rely on (raising an event, flipping a camera off before teardown), so
    ///     they are documented rather than changed.
    /// </remarks>
    public static void Run(
        Action<G9OperationTrace> action,
        G9SafeCommandOptions? options = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "")
    {
        G9SafeCommandOptions opts;
        string key;
        string source;
        G9OperationTrace? trace = null;
        ILogger? traceLogger;
        var enteredConcurrentGuard = false;

        try
        {
            ArgumentNullException.ThrowIfNull(action);
            opts = options ?? G9SafeCommandOptions.Default;
            key = opts.ThrottleKey ?? SafeBuildAutoKey(callerFile, callerMember);
            source = opts.Source ?? SafeGetFileName(callerFile);

            if (opts.EnableThrottle && !TryThrottle(key, opts.ThrottleInterval))
            {
                LogSafe(LogLevel.Debug, null, "Sync operation skipped by throttle guard", source, key);
                return;
            }

            if (opts.PreventConcurrentExecution && !TryEnterConcurrentGuard(key))
            {
                LogSafe(LogLevel.Debug, null, "Sync operation skipped by concurrent guard", source, key);
                return;
            }

            enteredConcurrentGuard = opts.PreventConcurrentExecution;

            traceLogger = ResolveLogger();
            trace = new G9OperationTrace(key, source, traceLogger);
        }
        catch (Exception initEx)
        {
            LogSafe(LogLevel.Critical, initEx, "SafeCommand sync initialization failed");
            return;
        }

        try
        {
            var executionDelay = ResolveExecutionDelay(opts);
            if (executionDelay > TimeSpan.Zero)
            {
                Task.Delay(executionDelay).GetAwaiter().GetResult();
            }

            SafeInvokeSetBusy(opts.SetBusy, true, source, key);
            LogTraceStarted(source, key);
            var operationStopwatch = Stopwatch.StartNew();
            try
            {
                ExecuteOnConfiguredThreadAsync(() =>
                {
                    action(trace);
                    return Task.CompletedTask;
                }, opts.RunActionOnMainThread).GetAwaiter().GetResult();
            }
            finally
            {
                LogTraceCompleted(source, key, operationStopwatch.ElapsedMilliseconds);
            }

            if (trace.HasFailed)
            {
                var failure = new OperationFailure(
                    trace.UserErrorMessage!,
                    trace.UserErrorDiagnostics ?? SafeBuildReport(trace),
                    null);
                RaiseOperationFailed(failure, source, key);

                // Fire-and-forget, so — unlike an awaited popup — it never holds the guard below.
                if (opts.ShowErrorG9Popup)
                {
                    SafeFireAndForget(SafeShowErrorG9PopupAsync(
                        failure.UserMessage,
                        opts.ErrorTitle,
                        failure.Diagnostics,
                        source));
                }
            }
        }
        catch (OperationCanceledException)
        {
            LogSafe(LogLevel.Debug, null, "Sync operation cancelled", source, key);
        }
        catch (ObjectDisposedException disposedEx)
        {
            // See RunAsync: swallowed as before, but visible in a production log.
            LogSafe(LogLevel.Warning, disposedEx, "Object disposed during sync operation", source, key);
        }
        catch (Exception ex)
        {
            LogUnhandledOnce(trace, traceLogger, ex, "Unhandled exception in sync action", source, key);

            var failure = new OperationFailure(
                opts.ErrorMessage ?? SafeGetDefaultErrorMessage(),
                SafeBuildReport(trace, ex),
                ex);
            RaiseOperationFailed(failure, source, key);

            if (opts.ShowErrorG9Popup)
            {
                SafeFireAndForget(SafeShowErrorG9PopupAsync(
                    failure.UserMessage,
                    opts.ErrorTitle,
                    failure.Diagnostics,
                    source));
            }

            if (opts.RethrowException)
            {
                throw;
            }
        }
        finally
        {
            SafeInvokeSetBusy(opts.SetBusy, false, source, key);

            if (enteredConcurrentGuard)
            {
                ExitConcurrentGuard(key);
            }
        }
    }

    /// <summary>Synchronous overload without trace.</summary>
    public static void Run(
        Action action,
        G9SafeCommandOptions? options = null,
        [CallerMemberName] string callerMember = "",
        [CallerFilePath] string callerFile = "")
    {
        Run(_ => action(), options, callerMember, callerFile);
    }

    #endregion

    #region Execution helpers

    private static TimeSpan ResolveExecutionDelay(G9SafeCommandOptions options)
    {
        var delay = options.DelayBeforeExecution > TimeSpan.Zero
            ? options.DelayBeforeExecution
            : options.BusyDelay;

        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static Task ExecuteOnConfiguredThreadAsync(Func<Task> action, bool runOnMainThread)
    {
        return runOnMainThread
            ? ExecuteOnMainThreadAsync(action)
            : action();
    }

    private static Task ExecuteOnMainThreadAsync(Func<Task> action)
    {
        if (MainThread.IsMainThread)
        {
            return action();
        }

        return MainThread.InvokeOnMainThreadAsync(action);
    }

    private static Task<T> ExecuteOnMainThreadAsync<T>(Func<Task<T>> action)
    {
        if (MainThread.IsMainThread)
        {
            return action();
        }

        return MainThread.InvokeOnMainThreadAsync(action);
    }

    #endregion

    #region Safe callback wrappers

    private static void SafeInvokeSetBusy(Action<bool>? setBusy, bool value, string source, string key)
    {
        if (setBusy is null)
        {
            return;
        }

        try
        {
            setBusy(value);
        }
        catch (Exception ex)
        {
            LogSafe(LogLevel.Error, ex, $"SetBusy({value}) callback failed", source, key);
        }
    }

    private static async Task SafeInvokeOnErrorAsync(
        Func<Exception, Task>? onError,
        Exception originalException,
        string source,
        string key)
    {
        if (onError is null)
        {
            return;
        }

        try
        {
            await onError(originalException);
        }
        catch (Exception cbEx)
        {
            LogSafe(LogLevel.Error, cbEx, "OnError callback failed", source, key);
        }
    }

    private static async Task SafeShowErrorG9PopupAsync(
        string? message,
        string? title,
        string? diagnosticsText,
        string? source,
        IReadOnlyList<Exception>? exceptions = null)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        try
        {
            var buttons = new List<G9PopupButton>();

            // A "More details" escape hatch onto the app's own diagnostics surface, when there is
            // one. The library has no diagnostics UI of its own and will not assume one, so this is
            // a hook the host opts into — see DiagnosticsHandler. With no handler registered the
            // popup shows a single OK, which is the right default for an end user.
            //
            // DiagnosticsAvailable is asked HERE, per popup, rather than once at registration:
            // whether the surface is reachable is usually a runtime setting (a developer-mode
            // toggle), and a button that is present but inert is worse than no button.
            var diagnostics = DiagnosticsHandler;
            if (diagnostics is not null && IsDiagnosticsAvailable())
            {
                buttons.Add(new G9PopupButton
                {
                    Text = G9Strings.Get(G9StringKey.MoreDetails),
                    IsPrimary = false,
                    CallbackAsync = _ => Task.FromResult(G9PopupResult.Close(async () =>
                        await SafeOpenDiagnosticsAsync(message, diagnosticsText, exceptions,
                            source ?? "Unknown")))
                });
            }

            buttons.Add(G9PopupButton.CloseButton(G9Strings.Get(G9StringKey.Ok)));

            await G9PopupHelper.ShowErrorG9PopupAsync(
                message!,
                title ?? G9Strings.Get(G9StringKey.Error),
                buttons);
        }
        catch (Exception ex)
        {
            LogSafe(LogLevel.Error, ex, "Failed to show error popup");
        }
    }

    /// <summary>
    ///     Opt-in hook that adds a <b>More details</b> button to the error popup this helper shows,
    ///     and receives everything it knows about the failure when the user taps it.
    ///     <para>
    ///         Register the app's diagnostics surface here — a log viewer, a bug-report sheet, a
    ///         support form. With no handler registered the error popup shows a single OK, which is
    ///         the correct default: a raw stack trace is not something to put in front of an end
    ///         user, and the library cannot know whether this build has somewhere better to send it.
    ///     </para>
    ///     <example>
    ///         <code>
    ///         G9SafeCommand.DiagnosticsHandler = async report =>
    ///         {
    ///             if (!settings.DeveloperMode) return;   // gate it however you like
    ///             await diagnosticsSheet.OpenAsync(report);
    ///         };
    ///         </code>
    ///     </example>
    /// </summary>
    public static Func<G9DiagnosticsReport, Task>? DiagnosticsHandler { get; set; }

    /// <summary>
    ///     Optional gate deciding whether the <b>More details</b> button is offered on a given error
    ///     popup. Return <c>false</c> and the popup shows only OK, exactly as if no
    ///     <see cref="DiagnosticsHandler" /> were registered.
    ///     <para>
    ///         Leave it <c>null</c> — the default — and the button appears whenever a handler is
    ///         registered.
    ///     </para>
    ///     <para>
    ///         <b>Why this is not just "clear the handler".</b> Diagnostics surfaces are typically
    ///         gated on a setting the user can flip while the app runs (a developer or support mode),
    ///         and the handler is registered once at startup by code that has no natural place to
    ///         re-register it later. Without this gate the only options are a button that is always
    ///         shown and silently does nothing when the setting is off, or nulling a static from
    ///         wherever the setting changes — a hidden coupling that breaks the moment a second thing
    ///         wants to register a handler.
    ///     </para>
    ///     <para>
    ///         Evaluated on every error popup, so keep it cheap and non-throwing; a fault here is
    ///         treated as "not available" rather than propagated into the popup queue.
    ///     </para>
    ///     <example>
    ///         <code>
    ///         G9SafeCommand.DiagnosticsHandler   = report => diagnosticsSheet.OpenAsync(report);
    ///         G9SafeCommand.DiagnosticsAvailable = () => settings.DeveloperMode;
    ///         </code>
    ///     </example>
    /// </summary>
    public static Func<bool>? DiagnosticsAvailable { get; set; }

    /// <summary>
    ///     Evaluates <see cref="DiagnosticsAvailable" />, treating both "not configured" and "threw" as
    ///     available / not available respectively.
    /// </summary>
    /// <remarks>
    ///     A throwing gate must not take the error popup down with it. The popup being built here is
    ///     already the app's response to a failure, so losing it would replace a visible error with
    ///     silence — the worst possible outcome of a diagnostics feature.
    /// </remarks>
    private static bool IsDiagnosticsAvailable()
    {
        var gate = DiagnosticsAvailable;
        if (gate is null)
        {
            return true;
        }

        try
        {
            return gate();
        }
        catch (Exception ex)
        {
            LogSafe(LogLevel.Warning, ex, "Diagnostics availability gate threw; hiding the More details button");
            return false;
        }
    }

    private static async Task SafeOpenDiagnosticsAsync(
        string? errorMessage,
        string? diagnosticsText,
        IReadOnlyList<Exception>? exceptions,
        string source)
    {
        var handler = DiagnosticsHandler;
        if (handler is null)
        {
            return;
        }

        try
        {
            await handler(new G9DiagnosticsReport(source, errorMessage, diagnosticsText, exceptions));
        }
        catch (Exception ex)
        {
            // The handler is consumer code running from inside a popup callback. A fault here must
            // not escape into the popup queue, or the queue stalls and no later popup can open.
            LogSafe(LogLevel.Error, ex, "Diagnostics handler threw");
        }
    }

    /// <summary>
    ///     Raised <b>exactly once for every operation that fails</b> under <c>RunAsync</c>,
    ///     <c>RunSafe</c> or <c>Run</c> — an unhandled exception, or a <c>trace.Fail(...)</c> — and
    ///     <b>regardless of <see cref="G9SafeCommandOptions.ShowErrorG9Popup" /></b>. It fires after the
    ///     busy flag and concurrency key have been released and before <c>OnError</c> / the popup.
    ///     <para>
    ///         <b>Why it exists when there is already an <c>ILogger</c>.</b> This helper logs every
    ///         failure under the category <c>"G9SafeCommand"</c> — and a host is entitled to filter its
    ///         log by category. One that keeps only its own namespace (a common, sensible setup) drops
    ///         every line written here, which is exactly how an app came to record "showed the popup and
    ///         wrote NOTHING to the runtime log", and how <c>ShowErrorG9Popup = false</c> became a fully
    ///         silent failure. A delegate cannot be filtered away by a category rule: subscribe and
    ///         write the report to whatever sink you actually read.
    ///     </para>
    ///     <para>
    ///         Not raised for a cancelled operation or for the swallowed
    ///         <see cref="ObjectDisposedException" /> teardown race — neither is reported to the user
    ///         either. Keep the handler cheap and non-blocking; it runs on the operation's thread. A
    ///         handler that throws is swallowed.
    ///     </para>
    ///     <example>
    ///         <code>
    ///         G9SafeCommand.OperationFailed = report =>
    ///             logger.LogError(report.Exceptions?.FirstOrDefault(),
    ///                 "{Source}: {Message} {Trace}", report.Source, report.UserMessage, report.DiagnosticsText);
    ///         </code>
    ///     </example>
    /// </summary>
    public static Action<G9DiagnosticsReport>? OperationFailed { get; set; }

    /// <summary>What a failed operation leaves behind for the code that runs once its guard is released.</summary>
    private sealed record OperationFailure(string UserMessage, string Diagnostics, Exception? Exception);

    private static void RaiseOperationFailed(OperationFailure failure, string source, string key)
    {
        var handler = OperationFailed;
        if (handler is null)
        {
            return;
        }

        try
        {
            handler(new G9DiagnosticsReport(
                $"{source}/{key}",
                failure.UserMessage,
                failure.Diagnostics,
                failure.Exception is null ? [] : [failure.Exception]));
        }
        catch (Exception ex)
        {
            // Same rule as G9Diagnostics: an observer must never break the operation it observes.
            LogSafe(LogLevel.Warning, ex, "OperationFailed observer threw", source, key);
        }
    }

    /// <summary>
    ///     Records an unhandled exception in the trace and writes it to the log <b>once</b>.
    ///     <para>
    ///         <c>trace.Error</c> logs through the trace's own logger, and the caller used to follow it
    ///         with a second <c>LogSafe(Error, ex)</c> — the same exception, twice, on every failure
    ///         (two entries in the log, two events in an error reporter). The direct write is now the
    ///         fallback only: when the trace has no logger, or recording into it threw.
    ///     </para>
    /// </summary>
    private static void LogUnhandledOnce(
        G9OperationTrace trace,
        ILogger? traceLogger,
        Exception exception,
        string message,
        string source,
        string key)
    {
        var logged = false;
        try
        {
            trace.Error("Unhandled exception", exception);
            logged = traceLogger is not null;
        }
        catch
        {
            // trace itself failed — fall through to the direct write
        }

        if (!logged)
        {
            LogSafe(LogLevel.Error, exception, message, source, key);
        }
    }

    /// <summary>
    ///     Observes a fire-and-forget Task so unhandled exceptions don't
    ///     surface as UnobservedTaskException (which can crash on some platforms).
    /// </summary>
    private static async void SafeFireAndForget(Task task)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            LogSafe(LogLevel.Error, ex, "Fire-and-forget task failed");
        }
    }

    #endregion

    #region Safe infrastructure helpers

    private static ILogger? ResolveLogger()
    {
        // Cache the resolved logger: G9SafeCommand is on the hot path of nearly every UI
        // command, so a per-call DI lookup (especially for the new Trace start/complete logs) would
        // add avoidable overhead. The factory + logger are process-lifetime singletons, so caching
        // the first non-null result is safe; the benign race just re-resolves to the same instance.
        var cached = _cachedLogger;
        if (cached is not null)
        {
            return cached;
        }

        try
        {
            var logger = G9ServiceProvider.GetServiceNullable<ILoggerFactory>()
                ?.CreateLogger("G9SafeCommand");
            if (logger is not null)
            {
                _cachedLogger = logger;
            }

            return logger;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    ///     High-performance Trace marker emitted when an operation starts. Free when Trace is off
    ///     (the generated delegate short-circuits on <see cref="ILogger.IsEnabled" />).
    /// </summary>
    private static void LogTraceStarted(string source, string key)
    {
        // The host's observation seam. Placed here rather than at the call sites because
        // this is the ONE place every safe-run path already funnels through, so a host that
        // wants to know when the app is busy does not have to wrap several hundred callers —
        // and cannot miss the next one somebody writes. Null unless a host opted in.
        G9Diagnostics.RaiseOperationStarted(source, key);

        try
        {
            var logger = ResolveLogger();
            if (logger is not null)
            {
                LogOperationStarted(logger, source, key, null);
            }
        }
        catch
        {
            // tracing must never break the operation
        }
    }

    /// <summary>High-performance Trace marker emitted when an operation completes, with duration.</summary>
    private static void LogTraceCompleted(string source, string key, long elapsedMs)
    {
        // Both call sites emit this from a finally, so a host counting in-flight operations
        // can never leak a count when an operation throws.
        G9Diagnostics.RaiseOperationCompleted(source, key, elapsedMs);

        try
        {
            var logger = ResolveLogger();
            if (logger is not null)
            {
                LogOperationCompleted(logger, source, key, elapsedMs, null);
            }
        }
        catch
        {
            // tracing must never break the operation
        }
    }

    private static readonly Action<ILogger, string, string, Exception?> LogOperationStarted =
        LoggerMessage.Define<string, string>(
            LogLevel.Trace,
            new EventId(5200, "SafeCommandStarted"),
            "Operation started [{Source}/{Key}]");

    private static readonly Action<ILogger, string, string, long, Exception?> LogOperationCompleted =
        LoggerMessage.Define<string, string, long>(
            LogLevel.Trace,
            new EventId(5201, "SafeCommandCompleted"),
            "Operation completed [{Source}/{Key}] in {ElapsedMs}ms");

    private static ILogger? _cachedLogger;

    private static void LogSafe(
        LogLevel level,
        Exception? ex,
        string message,
        string? source = null,
        string? key = null)
    {
        try
        {
            var logger = ResolveLogger();
            if (logger is null)
            {
                return;
            }

            var tag = source is not null && key is not null
                ? $"[{source}/{key}] {message}"
                : message;

            if (ex is not null)
            {
                logger.Log(level, ex, "{Message}", tag);
            }
            else
            {
                logger.Log(level, "{Message}", tag);
            }
        }
        catch
        {
            // Absolutely nothing can be done — swallow to protect the app.
        }
    }

    private static VisualElement? GetCurrentVisualElement()
    {
        // Single-page model: there is no Shell. Resolve from the active window root page;
        // ModalContentPage / bottom sheets surface through G9ModalHostRegistry when needed.
        return Application.Current?.Windows.FirstOrDefault()?.Page;
    }

    private static string SafeBuildAutoKey(string callerFile, string callerMember)
    {
        try
        {
            return $"{Path.GetFileNameWithoutExtension(callerFile)}.{callerMember}";
        }
        catch
        {
            return callerMember;
        }
    }

    private static string SafeGetFileName(string callerFile)
    {
        try
        {
            return Path.GetFileNameWithoutExtension(callerFile);
        }
        catch
        {
            return "Unknown";
        }
    }

    private static string SafeBuildReport(G9OperationTrace? trace, Exception? exception = null)
    {
        try
        {
            return trace?.BuildReport(exception) ?? exception?.ToString() ?? "No diagnostics available.";
        }
        catch
        {
            return exception?.ToString() ?? "Failed to build diagnostics report.";
        }
    }

    private static string SafeGetDefaultErrorMessage()
    {
        return G9Strings.Get(G9StringKey.UnexpectedError);
    }

    private static void CleanupThrottleEntries(long now)
    {
        try
        {
            Interlocked.Exchange(ref _lastCleanupTick, now);
            var threshold = now - (long)CleanupThreshold.TotalMilliseconds;
            foreach (var kvp in ThrottleTimestamps)
            {
                if (kvp.Value < threshold)
                {
                    // The pair overload: remove only if the value is still the stale one we looked
                    // at, so a tap recorded between the read and the remove is not forgotten.
                    ThrottleTimestamps.TryRemove(kvp);
                }
            }
        }
        catch
        {
            // Cleanup is best-effort; never crash for housekeeping.
        }
    }

    private static bool TryEnterConcurrentGuard(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return true;
        }

        return InFlightOperations.TryAdd(key, 0);
    }

    private static void ExitConcurrentGuard(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        InFlightOperations.TryRemove(key, out _);
    }

    #endregion
}
