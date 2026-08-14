namespace G9MAUIControls.Helpers;

/// <summary>
///     Configuration for <see cref="Helpers.G9SafeCommand" /> operations.
///     All properties have sensible defaults so callers only set what they need.
/// </summary>
public sealed record G9SafeCommandOptions
{
    /// <summary>
    ///     When true (default), prevents concurrent execution of the same logical operation key.
    ///     Any new call with the same key while a previous one is still running will be skipped.
    ///     Set to false only for operations that are explicitly designed to overlap (e.g. fire-and-forget background work,
    ///     or operations that cancel/replace previous runs themselves).
    /// </summary>
    public bool PreventConcurrentExecution { get; init; } = true;

    /// <summary>Throttle key override. When null, auto-generated from caller member + file.</summary>
    public string? ThrottleKey { get; init; }

    /// <summary>Minimum interval between consecutive executions of the same key.</summary>
    public TimeSpan ThrottleInterval { get; init; } = TimeSpan.FromMilliseconds(369);

    /// <summary>When false, skips the throttle/double-tap guard entirely.</summary>
    public bool EnableThrottle { get; init; } = true;

    /// <summary>User-facing error message shown on unhandled exception. Falls back to a default dictionary key.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Title for the error popup. Falls back to the localized "Error" title.</summary>
    public string? ErrorTitle { get; init; }

    /// <summary>Logical source name used in diagnostics reports (e.g. "LoginViewModel").</summary>
    public string? Source { get; init; }

    /// <summary>When true, shows an error popup on unhandled exception or when <c>trace.Fail</c> is called.</summary>
    public bool ShowErrorG9Popup { get; init; } = true;

    /// <summary>When true, re-throws the caught exception after logging and popup.</summary>
    public bool RethrowException { get; init; }

    /// <summary>Callback to toggle a busy/loading indicator (receives true on start, false on completion).</summary>
    public Action<bool>? SetBusy { get; init; }

    /// <summary>Optional callback invoked when an unhandled exception is caught (before the popup).</summary>
    public Func<Exception, Task>? OnError { get; init; }

    /// <summary>
    ///     Delay before starting the action (useful for ripple/animation effects in MAUI).
    ///     Legacy alias kept for backward compatibility. Prefer <see cref="DelayBeforeExecution" /> in new code.
    /// </summary>
    public TimeSpan BusyDelay { get; init; } = TimeSpan.Zero;

    /// <summary>
    ///     General delay before action execution. When set, this takes precedence over <see cref="BusyDelay" />.
    /// </summary>
    public TimeSpan DelayBeforeExecution { get; init; } = TimeSpan.Zero;

    /// <summary>
    ///     When true, executes the action delegate on MAUI main UI thread.
    ///     Useful for UI-only code that must be dispatched safely from background contexts.
    /// </summary>
    public bool RunActionOnMainThread { get; init; }

    public static G9SafeCommandOptions Default => new();
}
