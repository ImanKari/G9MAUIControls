using G9MAUIControls.Popup;
using G9MAUIControls.Toast;
using G9MAUIControls.Controls;
using G9MAUIControls.Localization;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;

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
    private static readonly ConcurrentDictionary<string, DateTime> ThrottleTimestamps = new();
    private static readonly TimeSpan DefaultThrottleInterval = TimeSpan.FromMilliseconds(369);
    private static readonly TimeSpan CleanupThreshold = TimeSpan.FromMinutes(1);
    private static readonly ConcurrentDictionary<string, byte> InFlightOperations = new();
    private static DateTime _lastCleanupUtc = DateTime.UtcNow;

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
        var isRethrowing = false;
        G9SafeCommandOptions opts;
        string key;
        string source;
        G9OperationTrace? trace = null;
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

            trace = new G9OperationTrace(key, source, ResolveLogger());
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

            if (trace.HasFailed && opts.ShowErrorG9Popup)
            {
                await SafeShowErrorG9PopupAsync(
                    trace.UserErrorMessage!,
                    opts.ErrorTitle,
                    trace.UserErrorDiagnostics ?? SafeBuildReport(trace),
                    source);
            }
        }
        catch (OperationCanceledException)
        {
            LogSafe(LogLevel.Debug, null, "Operation cancelled", source, key);
        }
        catch (ObjectDisposedException)
        {
            LogSafe(LogLevel.Debug, null, "Object disposed during operation", source, key);
        }
        catch (Exception ex)
        {
            try
            {
                trace.Error("Unhandled exception", ex);
            }
            catch
            {
                // trace itself failed — nothing we can do
            }

            LogSafe(LogLevel.Error, ex, "Unhandled exception in action", source, key);

            await SafeInvokeOnErrorAsync(opts.OnError, ex, source, key);

            if (opts.ShowErrorG9Popup)
            {
                await SafeShowErrorG9PopupAsync(
                    opts.ErrorMessage ?? SafeGetDefaultErrorMessage(),
                    opts.ErrorTitle,
                    SafeBuildReport(trace, ex),
                    source);
            }

            if (opts.RethrowException)
            {
                isRethrowing = true;
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

        // This intentionally lives outside the try/finally above.
        // If isRethrowing is true the throw already left; we never reach here.
        // If some truly unexpected infrastructure exception escaped (shouldn't
        // happen given the guards above), let the outer caller decide.
        _ = isRethrowing; // suppress unused warning
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

        var interval = minInterval ?? DefaultThrottleInterval;
        var now = DateTime.UtcNow;

        var canExecute = ThrottleTimestamps.AddOrUpdate(
            key,
            _ => now,
            (_, last) => now - last >= interval ? now : last);

        var allowed = canExecute == now;

        if (now - _lastCleanupUtc > CleanupThreshold)
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

            trace = new G9OperationTrace(key, source, ResolveLogger());
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

            if (trace.HasFailed && opts.ShowErrorG9Popup)
            {
                SafeFireAndForget(SafeShowErrorG9PopupAsync(
                    trace.UserErrorMessage!,
                    opts.ErrorTitle,
                    trace.UserErrorDiagnostics ?? SafeBuildReport(trace),
                    source));
            }
        }
        catch (OperationCanceledException)
        {
            LogSafe(LogLevel.Debug, null, "Sync operation cancelled", source, key);
        }
        catch (ObjectDisposedException)
        {
            LogSafe(LogLevel.Debug, null, "Object disposed during sync operation", source, key);
        }
        catch (Exception ex)
        {
            try
            {
                trace.Error("Unhandled exception", ex);
            }
            catch
            {
                // ignored
            }

            LogSafe(LogLevel.Error, ex, "Unhandled exception in sync action", source, key);

            if (opts.ShowErrorG9Popup)
            {
                SafeFireAndForget(SafeShowErrorG9PopupAsync(
                    opts.ErrorMessage ?? SafeGetDefaultErrorMessage(),
                    opts.ErrorTitle,
                    SafeBuildReport(trace, ex),
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

    private static void CleanupThrottleEntries(DateTime now)
    {
        try
        {
            _lastCleanupUtc = now;
            var threshold = now - CleanupThreshold;
            foreach (var kvp in ThrottleTimestamps)
            {
                if (kvp.Value < threshold)
                {
                    ThrottleTimestamps.TryRemove(kvp.Key, out _);
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
