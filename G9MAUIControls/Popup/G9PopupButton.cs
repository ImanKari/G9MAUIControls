using G9MAUIControls.Icons;

namespace G9MAUIControls.Popup;

public sealed record G9PopupButton
{
    public required string Text { get; init; }
    public Func<CancellationToken, Task<G9PopupResult>>? CallbackAsync { get; init; }
    public bool IsPrimary { get; init; } = true;
    public G9IconSource? Icon { get; init; }
    public ImageSource? IconSource { get; init; }
    public Color? TextColor { get; init; }
    public Color? BackgroundColor { get; init; }
    public string? AutomationId { get; init; }

    public static G9PopupButton CloseButton(string text)
    {
        return new G9PopupButton
        {
            Text = text, CallbackAsync = _ => Task.FromResult(G9PopupResult.Close()), IsPrimary = true
        };
    }
}