using G9MAUIControls.Helpers;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text;

namespace G9MAUIControls.Helpers;

/// <summary>
///     Collects step-by-step trace entries during an operation.
///     Passed to the user action in <see cref="G9SafeCommand" /> run callbacks so callers can record
///     diagnostics that appear in the admin-debug "More details" report.
/// </summary>
public sealed class G9OperationTrace
{
    private readonly List<TraceEntry> _entries = [];
    private readonly string _operationKey;
    private readonly string _source;
    private readonly ILogger? _logger;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    internal G9OperationTrace(string operationKey, string source, ILogger? logger)
    {
        _operationKey = operationKey;
        _source = source;
        _logger = logger;
    }

    internal string? UserErrorMessage { get; private set; }
    internal string? UserErrorDiagnostics { get; private set; }
    internal bool HasFailed => UserErrorMessage is not null;

    public TimeSpan Elapsed => _stopwatch.Elapsed;

    /// <summary>Records an informational step in the trace timeline.</summary>
    public void Step(string message, string? detail = null)
    {
        _entries.Add(new TraceEntry(TraceLevel.Info, message, detail, _stopwatch.Elapsed));
        _logger?.LogDebug("[{Source}/{Key}] {Message} {Detail}", _source, _operationKey, message, detail ?? "");
    }

    /// <summary>Records a warning in the trace timeline.</summary>
    public void Warning(string message, string? detail = null)
    {
        _entries.Add(new TraceEntry(TraceLevel.Warning, message, detail, _stopwatch.Elapsed));
        _logger?.LogWarning("[{Source}/{Key}] {Message} {Detail}", _source, _operationKey, message, detail ?? "");
    }

    /// <summary>Records an error in the trace timeline.</summary>
    public void Error(string message, Exception? ex = null, string? detail = null)
    {
        _entries.Add(new TraceEntry(TraceLevel.Error, message, detail, _stopwatch.Elapsed, ex));
        _logger?.LogError(ex, "[{Source}/{Key}] {Message} {Detail}", _source, _operationKey, message, detail ?? "");
    }

    /// <summary>
    ///     Marks the operation as failed with a user-facing message.
    ///     <see cref="G9SafeCommand" /> will show an error popup with this message
    ///     when <c>ShowErrorPopup</c> is enabled.
    /// </summary>
    public void Fail(string userMessage, string? diagnostics = null)
    {
        UserErrorMessage = userMessage;
        UserErrorDiagnostics = diagnostics;
        _entries.Add(new TraceEntry(TraceLevel.Error, $"Operation failed: {userMessage}", diagnostics,
            _stopwatch.Elapsed));
        _logger?.LogWarning("[{Source}/{Key}] Operation failed: {Message}", _source, _operationKey, userMessage);
    }

    /// <summary>
    ///     Builds a human-readable diagnostics report containing every trace entry,
    ///     timing information, and an optional final exception stack trace.
    /// </summary>
    public string BuildReport(Exception? exception = null)
    {
        _stopwatch.Stop();

        var sb = new StringBuilder();
        sb.AppendLine("Operation Trace Report");
        sb.AppendLine("======================");
        sb.AppendLine($"Key: {_operationKey}");
        sb.AppendLine($"Source: {_source}");
        sb.AppendLine($"TimestampUtc: {DateTimeOffset.UtcNow:O}");
        sb.AppendLine($"Duration: {_stopwatch.Elapsed.TotalMilliseconds:F0}ms");
        if (_entries.Count > 0)
        {
            sb.AppendLine("───────────────────────────────");
            foreach (var entry in _entries)
            {
                var levelTag = entry.Level switch
                {
                    TraceLevel.Warning => "WARN",
                    TraceLevel.Error => "ERR ",
                    _ => "INFO"
                };
                sb.AppendLine($"[{entry.Elapsed.TotalMilliseconds,7:F0}ms] [{levelTag}] {entry.Message}");

                if (!string.IsNullOrWhiteSpace(entry.Detail))
                    sb.AppendLine($"            -> {entry.Detail}");

                if (entry.Exception is not null)
                    sb.AppendLine(
                        $"            !! {entry.Exception.GetType().Name}: {entry.Exception.Message}");
            }
        }

        if (exception is not null)
        {
            sb.AppendLine();
            sb.AppendLine("===============================");
            sb.AppendLine("[Unhandled Exception]");
            sb.AppendLine(exception.ToString());
        }

        return sb.ToString();
    }

    private enum TraceLevel
    {
        Info,
        Warning,
        Error
    }

    private readonly record struct TraceEntry(
        TraceLevel Level,
        string Message,
        string? Detail,
        TimeSpan Elapsed,
        Exception? Exception = null);
}
