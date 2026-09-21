using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace G9MAUIControls.Controls;

/// <summary>
///     The one press pipeline for tappable controls that are not a <see cref="G9Button" />:
///     <b>act first, animate afterwards, never twice, never crash.</b>
///     <para>
///         Before this existed <see cref="G9NavCard" />, <see cref="G9Expander" /> and
///         <see cref="G9HeaderActionButton" /> each <c>await</c>ed their press animation
///         (150–190 ms) and only THEN ran the consumer's command, from an <c>async void</c>
///         handler. That cost three things at once: every tap felt a fifth of a second late; a
///         second tap landing inside that window ran the command a second time (the row that
///         navigates twice); and an exception thrown by the command surfaced on the
///         synchronization context with nobody to catch it, which takes the process down.
///     </para>
/// </summary>
internal static class G9Press
{
    /// <summary>
    ///     Presses on the same control closer together than this are treated as one. Long enough
    ///     to swallow an accidental double tap (the platform double-tap window is ~300 ms), short
    ///     enough that a deliberate second press is never noticeably refused.
    /// </summary>
    public const int DefaultGuardMs = 300;

    // Keyed weakly by the pressed control so the guard state dies with it and needs no field —
    // G9HeaderActionButton is a plain ContentView and has no shared base to put one on.
    private static readonly ConditionalWeakTable<object, PressState> States = new();

    /// <summary>
    ///     Runs a press: <paramref name="raise" /> (the control's own event), then
    ///     <paramref name="command" /> if it can execute, then <paramref name="feedback" /> without
    ///     waiting for it.
    /// </summary>
    /// <returns><c>false</c> when the press was dropped by the re-entrancy guard.</returns>
    public static bool Invoke(
        object owner,
        Action? raise,
        ICommand? command,
        object? parameter,
        Func<Task>? feedback = null,
        int guardMs = DefaultGuardMs)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var state = States.GetOrCreateValue(owner);
        var now = Environment.TickCount64;
        if (guardMs > 0 && state.HasPress && now - state.LastPressTick < guardMs)
        {
            return false;
        }

        state.HasPress = true;
        state.LastPressTick = now;

        try
        {
            raise?.Invoke();

            if (command?.CanExecute(parameter) == true)
            {
                command.Execute(parameter);
            }
        }
        catch (Exception ex)
        {
            ReportFailure(owner, ex);
        }

        if (feedback is not null)
        {
            _ = PlayFeedbackAsync(feedback);
        }

        return true;
    }

    /// <summary>
    ///     Records an exception that escaped a consumer's click handler or command. A control must
    ///     not let that tear the app down, but dropping it silently is worse than useless: the
    ///     feature "does nothing" and leaves no trace of why.
    ///     <para>
    ///         <c>G9Diagnostics</c> exposes operation / activity hooks but no error channel, so
    ///         this writes to the debug output for now. It is the single place to re-point once
    ///         the diagnostics seam grows one.
    ///     </para>
    /// </summary>
    public static void ReportFailure(object source, Exception exception)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[G9MAUIControls] {source.GetType().Name} press handler failed: {exception}");
    }

    private static async Task PlayFeedbackAsync(Func<Task> feedback)
    {
        try
        {
            await feedback().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Feedback is decoration. The press has already happened; a view torn down
            // mid-animation (the command navigated away) must not surface as a fault.
        }
    }

    private sealed class PressState
    {
        public bool HasPress;
        public long LastPressTick;
    }
}
