namespace G9MAUIControls.Popup;

public sealed record G9PopupInputOption
{
    public required string Value { get; init; }
    public required string Text { get; init; }
    public bool IsSelected { get; init; }

    public static G9PopupInputOption Create(string value, string? text = null, bool isSelected = false)
    {
        return new G9PopupInputOption
        {
            Value = value,
            Text = text ?? value,
            IsSelected = isSelected
        };
    }
}

