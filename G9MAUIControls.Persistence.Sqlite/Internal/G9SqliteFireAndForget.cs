namespace G9MAUIControls.Persistence.Sqlite;

/// <summary>
///     Runs a background task whose failure must never surface, and never escape.
///     <para>
///         <b>Why this is here rather than reusing the UI suite's safe-command runner.</b> This package
///         deliberately does not reference <c>G9MAUIControls</c> — it has nothing to do with controls, and a
///         persistence layer that dragged in a UI package would be unusable outside MAUI UI. The two call
///         sites need a fraction of what that runner does (no throttle, no single-flight, no error popup),
///         so the honest answer is these twenty lines rather than a dependency.
///     </para>
///     <para>
///         Both callers are <b>debounced cache refreshes</b>. A refresh that fails is not worth reporting:
///         the cache stays stale, the next read re-fetches, and nothing the user asked for was lost. What
///         WOULD be unacceptable is the failure escaping as an <c>UnobservedTaskException</c>, which on some
///         platforms terminates the process — so the exception is observed and dropped here, on purpose.
///     </para>
///     <para>
///         <b>"The next read re-fetches" is a promise the CACHES keep, not this class.</b> It was false for
///         a long time: a failed refresh left the cache flagged as initialised, so reads went on serving
///         the pre-write rows indefinitely. Each cache now clears that flag before letting its failure
///         reach here — dropping the exception is only safe because of that.
///     </para>
/// </summary>
internal static class G9SqliteFireAndForget
{
    /// <summary>
    ///     Starts <paramref name="operation" /> and observes its exception. Returns immediately.
    /// </summary>
    /// <param name="operation">The work. Any exception it throws is swallowed.</param>
    public static void Run(Func<Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        _ = Task.Run(async () =>
        {
            try
            {
                await operation().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Swallowed deliberately — see the class remarks. A failed cache refresh costs one
                // re-fetch on the next read; an unobserved exception can cost the process.
            }
        });
    }

    /// <summary>
    ///     Cancels a debounce source that another task may already have disposed.
    /// </summary>
    /// <remarks>
    ///     A debounce task disposes its own <see cref="CancellationTokenSource" /> when it finishes, and a
    ///     writer can have picked that same source up as "the previous one" a moment earlier.
    ///     <see cref="CancellationTokenSource.Cancel()" /> on a disposed source throws — and the callers
    ///     run right AFTER a row was committed, where a throw turns a successful write into a reported
    ///     failure and the caller's retry into a duplicate row. A source that is already disposed has
    ///     nothing left to cancel, so the exception carries no information worth that.
    /// </remarks>
    public static void CancelQuietly(CancellationTokenSource? source)
    {
        if (source is null)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already finished and cleaned up — exactly the state a cancel asks for.
        }
    }
}
