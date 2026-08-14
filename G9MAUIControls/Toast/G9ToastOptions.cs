using G9MAUIControls.Icons;

namespace G9MAUIControls.Toast;

public sealed record G9ToastOptions
{
    /// <summary>
    ///     Position on screen. Null = auto (BottomRight for LTR, BottomLeft for RTL).
    /// </summary>
    public G9ToastPosition? Position { get; init; }

    /// <summary>
    ///     Auto-dismiss duration in milliseconds. Default 3000ms.
    /// </summary>
    public int DurationMs { get; init; } = 3000;

    /// <summary>
    ///     Custom icon override. Null = type-based default icon.
    /// </summary>
    public G9IconSource? Icon { get; init; }

    /// <summary>
    ///     Optional action button text (e.g. "Undo").
    /// </summary>
    public string? ActionText { get; init; }

    /// <summary>
    ///     Callback invoked when the action button is tapped.
    /// </summary>
    public Func<Task>? Action { get; init; }

    public static G9ToastOptions Default => new();
}