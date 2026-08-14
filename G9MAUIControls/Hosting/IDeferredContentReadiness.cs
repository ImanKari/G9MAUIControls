namespace G9MAUIControls.Hosting;

/// <summary>
///     Opt-in contract for deferred bottom-sheet content that keeps mutating its VISUAL after it is
///     first built — a form that reveals with empty pickers and then fills them from an async data
///     load, a list that scroll-jumps to its selected row on <c>Loaded</c>, etc.
///     <para>
///         <b>Why it exists.</b> <see cref="DeferredContentView" />'s covered reveal masks
///         CONSTRUCTION and first layout, then fades the spinner on a fixed timer. Content that
///         applies more state AFTER that timer re-renders in full view — the one-time "blink" the
///         user sees. A view that implements this interface tells the reveal "don't drop the cover
///         yet": the spinner is held (bounded by a timeout) until <see cref="ContentReady" /> fires,
///         so the content is revealed already-settled. This generalizes the guarantee
///         <see cref="LoadableSheetContentView" /> gives (spinner until data applied) to any deferred
///         body, without forcing it onto the loadable base's structure.
///     </para>
///     <para>
///         <b>Contract.</b> Raise <see cref="ContentReady" /> (and flip
///         <see cref="IsContentReady" /> true) ON THE MAIN THREAD exactly once, after the initial
///         async work that changes the visual has been applied. Firing is idempotent and safe to
///         call before the reveal subscribes (the reveal re-checks <see cref="IsContentReady" />
///         after subscribing). If it never fires, the reveal proceeds on its safety timeout — a
///         missing signal degrades to today's behavior, never a stuck spinner.
///         <see cref="DeferredContentReadinessSignal" /> provides the boilerplate.
///     </para>
/// </summary>
public interface IDeferredContentReadiness
{
    /// <summary>True once the initial visual-affecting work has been applied.</summary>
    bool IsContentReady { get; }

    /// <summary>Raised on the main thread when <see cref="IsContentReady" /> flips true.</summary>
    event EventHandler? ContentReady;
}

/// <summary>
///     Drop-in backing for <see cref="IDeferredContentReadiness" />: a latched flag plus a
///     one-shot event. A view holds one of these and forwards the interface members to it, then
///     calls <see cref="MarkReady" /> when its initial async work is done. Main-thread only, like
///     the reveal that consumes it.
/// </summary>
public sealed class DeferredContentReadinessSignal
{
    private EventHandler? _ready;

    public bool IsReady { get; private set; }

    public event EventHandler? Ready
    {
        add
        {
            _ready += value;

            // Fire immediately if we already settled before this subscriber attached (the reveal
            // subscribes after the swap, by which point a fast load may already be done).
            if (IsReady)
            {
                value?.Invoke(this, EventArgs.Empty);
            }
        }
        remove => _ready -= value;
    }

    public void MarkReady()
    {
        if (IsReady)
        {
            return;
        }

        IsReady = true;
        _ready?.Invoke(this, EventArgs.Empty);
    }
}
