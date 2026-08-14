namespace G9MAUIControls.Popup;

public sealed record G9PopupInputOptions
{
    public required IReadOnlyList<G9PopupInputField> Fields { get; init; }
    public string? Title { get; init; }
    public string? Message { get; init; }

    /// <summary>Shorthand for the submit button label. Ignored when <see cref="SubmitButton" /> is set.</summary>
    public string? SubmitButtonText { get; init; }

    /// <summary>Shorthand for the cancel button label. Ignored when <see cref="CancelButton" /> is set.</summary>
    public string? CancelButtonText { get; init; }

    /// <summary>
    ///     Full submit button customization (text, colors, icon, etc.).
    ///     The handler overrides <see cref="G9PopupButton.CallbackAsync" /> and <see cref="G9PopupButton.IsPrimary" />.
    /// </summary>
    public G9PopupButton? SubmitButton { get; init; }

    /// <summary>
    ///     Full cancel button customization (text, colors, icon, etc.).
    ///     The handler overrides <see cref="G9PopupButton.CallbackAsync" /> and <see cref="G9PopupButton.IsPrimary" />.
    /// </summary>
    public G9PopupButton? CancelButton { get; init; }

    public G9PopupType Type { get; init; } = G9PopupType.Information;
    public G9PopupSettings? Settings { get; init; }
    public G9PopupAnimationType Animation { get; init; } = G9PopupAnimationType.SlideUp;
    public bool FocusFirstFieldOnOpen { get; init; } = true;

    public static G9PopupInputOptions Create(params G9PopupInputField[] fields)
    {
        return new G9PopupInputOptions { Fields = fields };
    }
}

