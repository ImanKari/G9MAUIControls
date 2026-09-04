namespace G9MAUIControls.Helpers;

/// <summary>
///     The library's one observation seam: a place for a host application to learn that
///     <em>something started</em> and <em>something finished</em>, without the library knowing
///     or caring what the host does with that.
///     <para>
///         It exists because the interesting choke points of a MAUI application built on these
///         controls — every command, every tab switch, every deferred content build — live in
///         this library, not in the app. An app that wants to know when it is busy (to wait for
///         quiescence instead of sleeping), or to record what a user actually did (semantic
///         actions rather than raw touches), would otherwise have to wrap several hundred call
///         sites by hand and re-wrap every new one somebody adds.
///     </para>
///     <para>
///         <b>This is not a testing feature and must not become one.</b> The library never
///         references a test framework, never decides that a build is a test build, and never
///         collects anything on its own. It raises two events. Everything opinionated —
///         what counts as busy, what is worth recording, whether any of it happens at all — is
///         the host's decision, made in the host's code, in the host's own build configuration.
///     </para>
/// </summary>
/// <remarks>
///     <b>Cost when unused.</b> Both hooks are static delegate fields that are null until
///     somebody assigns them, and every call site is a null check. A build that never subscribes
///     pays a predictable-branch per operation and nothing else — which is why this can ship in
///     every configuration rather than being conditionally compiled.
///     <para>
///         <b>Every invocation is guarded.</b> An observer that throws must not be able to break
///         the operation it was observing. A subscriber's exception is swallowed here, on
///         purpose: the alternative is a diagnostics hook taking down a user's save.
///     </para>
/// </remarks>
public static class G9Diagnostics
{
    /// <summary>
    ///     Raised when a <see cref="G9SafeCommand" /> operation begins.
    ///     <para>Parameters: <c>source</c> (usually the caller's file name) and <c>key</c> (the throttle/operation key).</para>
    /// </summary>
    public static Action<string, string>? OperationStarted { get; set; }

    /// <summary>
    ///     Raised when a <see cref="G9SafeCommand" /> operation ends, successfully or not, with its
    ///     elapsed milliseconds.
    ///     <para>
    ///         Guaranteed to fire for every operation that raised <see cref="OperationStarted" />:
    ///         it is emitted from a <c>finally</c>, so a host counting in-flight work can never
    ///         leak a count and hang forever waiting for an operation that already failed.
    ///     </para>
    /// </summary>
    public static Action<string, string, long>? OperationCompleted { get; set; }

    /// <summary>
    ///     Raised when a named UI activity that is not a command begins or ends — a tab activation,
    ///     a deferred content build, a bottom sheet opening. <c>busy</c> is <see langword="true" />
    ///     on entry and <see langword="false" /> on exit.
    ///     <para>
    ///         Kept separate from the command hooks because these are not operations with a result;
    ///         they are periods during which the UI is not settled, and a host that only wants
    ///         "is anything happening?" needs both.
    ///     </para>
    /// </summary>
    public static Action<string, bool>? ActivityChanged { get; set; }

    /// <summary>Fire <see cref="OperationStarted" />, never throwing.</summary>
    public static void RaiseOperationStarted(string source, string key)
    {
        var handler = OperationStarted;
        if (handler is null) return;
        try
        {
            handler(source, key);
        }
        catch
        {
            // An observer must never be able to break the operation it observes.
        }
    }

    /// <summary>Fire <see cref="OperationCompleted" />, never throwing.</summary>
    public static void RaiseOperationCompleted(string source, string key, long elapsedMs)
    {
        var handler = OperationCompleted;
        if (handler is null) return;
        try
        {
            handler(source, key, elapsedMs);
        }
        catch
        {
            // See RaiseOperationStarted.
        }
    }

    /// <summary>Fire <see cref="ActivityChanged" />, never throwing.</summary>
    public static void RaiseActivityChanged(string name, bool busy)
    {
        var handler = ActivityChanged;
        if (handler is null) return;
        try
        {
            handler(name, busy);
        }
        catch
        {
            // See RaiseOperationStarted.
        }
    }

    /// <summary>
    ///     Scope helper for the <see cref="ActivityChanged" /> pair, so a caller cannot forget the
    ///     closing half. <c>using var _ = G9Diagnostics.Activity("sheet:open");</c>
    /// </summary>
    public static IDisposable Activity(string name)
    {
        RaiseActivityChanged(name, true);
        return new ActivityScope(name);
    }

    private sealed class ActivityScope(string name) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            // Interlocked because a double dispose would decrement a host's busy count twice and
            // leave it permanently negative — i.e. permanently "idle" while work is in flight.
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            RaiseActivityChanged(name, false);
        }
    }
}
