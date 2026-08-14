namespace G9MAUIControls.Helpers;

/// <summary>
///     Everything <see cref="G9SafeCommand" /> knows about a failed operation, handed to the app's
///     own diagnostics surface through <see cref="G9SafeCommand.DiagnosticsHandler" />.
/// </summary>
/// <param name="Source">
///     Where the failure came from — the operation key, or the caller file and member the safe-run
///     wrapper derived it from.
/// </param>
/// <param name="UserMessage">
///     The message the user was shown. Useful for correlating a support report with a screenshot.
/// </param>
/// <param name="DiagnosticsText">
///     The operation trace, when one was collected: the ordered step log the safe-run wrapper built
///     while the operation was in flight, which is usually more informative than the exception alone
///     because it says what had already succeeded.
/// </param>
/// <param name="Exceptions">
///     Every exception involved, innermost included. Empty rather than null when the failure was a
///     reported error with no exception behind it.
/// </param>
public sealed record G9DiagnosticsReport(
    string Source,
    string? UserMessage,
    string? DiagnosticsText,
    IReadOnlyList<Exception>? Exceptions);
