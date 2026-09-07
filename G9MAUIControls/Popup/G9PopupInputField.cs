namespace G9MAUIControls.Popup;

public sealed record G9PopupInputField
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public string? Placeholder { get; init; }
    public string? InitialValue { get; init; }
    public bool? InitialChecked { get; init; }
    public IReadOnlyList<G9PopupInputOption>? Items { get; init; }
    public FlowDirection? FlowDirection { get; init; }
    public string? FontFamily { get; init; }
    public G9PopupInputFieldType Type { get; init; } = G9PopupInputFieldType.Text;
    public bool IsRequired { get; init; }
    public string? RequiredMessage { get; init; }
    public int? MaxLength { get; init; }
    public bool IsReadOnly { get; init; }
    public bool TrimValue { get; init; } = true;
    public bool IsTextPredictionEnabled { get; init; } = true;
    public bool IsSpellCheckEnabled { get; init; } = true;
    public Microsoft.Maui.Keyboard? Keyboard { get; init; }
    public Func<string?, string?>? Validator { get; init; }

    /// <summary>
    ///     Offers a dictation microphone on this field. Applies to <see cref="G9PopupInputFieldType.Text" />
    ///     and <see cref="G9PopupInputFieldType.TextArea" /> only, and needs a registered
    ///     <c>G9Speech.Provider</c> — with none, the microphone stays hidden.
    /// </summary>
    public bool EnableVoice { get; init; }

    /// <summary>
    ///     A free-text field.
    ///     <para>
    ///         <b><paramref name="flowDirection" /> defaults to null = follow the app's culture</b>,
    ///         which is the fix for a Persian UI whose "Title" box put the caret on the LEFT and
    ///         typed away from the label. It used to default to
    ///         <see cref="Microsoft.Maui.FlowDirection.LeftToRight" />, copied from the sibling
    ///         factories where LTR is correct for a real reason — a phone number, an email address
    ///         and a password ARE written left-to-right in every language, and mirroring one
    ///         corrupts the visible value. Prose is not: it is written in the language the user is
    ///         reading the form in. Pass a direction explicitly when a specific field carries
    ///         universally-LTR content.
    ///     </para>
    /// </summary>
    public static G9PopupInputField Text(
        string key,
        string label,
        string? placeholder = null,
        bool isRequired = false,
        string? initialValue = null,
        int? maxLength = null,
        FlowDirection? flowDirection = null,
        string? fontFamily = null,
        bool enableVoice = false)
    {
        return new G9PopupInputField
        {
            Key = key,
            Label = label,
            Placeholder = placeholder,
            IsRequired = isRequired,
            InitialValue = initialValue,
            MaxLength = maxLength,
            Type = G9PopupInputFieldType.Text,
            FlowDirection = flowDirection,
            FontFamily = fontFamily,
            EnableVoice = enableVoice
        };
    }

    public static G9PopupInputField Phone(
        string key,
        string label,
        string? placeholder = null,
        bool isRequired = false,
        string? initialValue = null,
        int? maxLength = null,
        FlowDirection flowDirection = Microsoft.Maui.FlowDirection.LeftToRight,
        string? fontFamily = null)
    {
        return new G9PopupInputField
        {
            Key = key,
            Label = label,
            Placeholder = placeholder,
            IsRequired = isRequired,
            InitialValue = initialValue,
            MaxLength = maxLength,
            Type = G9PopupInputFieldType.Phone,
            FlowDirection = flowDirection,
            FontFamily = fontFamily
        };
    }

    public static G9PopupInputField Email(
        string key,
        string label,
        string? placeholder = null,
        bool isRequired = false,
        string? initialValue = null,
        int? maxLength = null,
        FlowDirection flowDirection = Microsoft.Maui.FlowDirection.LeftToRight,
        string? fontFamily = null)
    {
        return new G9PopupInputField
        {
            Key = key,
            Label = label,
            Placeholder = placeholder,
            IsRequired = isRequired,
            InitialValue = initialValue,
            MaxLength = maxLength,
            Type = G9PopupInputFieldType.Email,
            FlowDirection = flowDirection,
            FontFamily = fontFamily
        };
    }

    public static G9PopupInputField Password(
        string key,
        string label,
        string? placeholder = null,
        bool isRequired = false,
        string? initialValue = null,
        int? maxLength = null,
        FlowDirection flowDirection = Microsoft.Maui.FlowDirection.LeftToRight,
        string? fontFamily = null)
    {
        return new G9PopupInputField
        {
            Key = key,
            Label = label,
            Placeholder = placeholder,
            IsRequired = isRequired,
            InitialValue = initialValue,
            MaxLength = maxLength,
            Type = G9PopupInputFieldType.Password,
            IsTextPredictionEnabled = false,
            IsSpellCheckEnabled = false,
            FlowDirection = flowDirection,
            FontFamily = fontFamily
        };
    }

    public static G9PopupInputField Number(
        string key,
        string label,
        string? placeholder = null,
        bool isRequired = false,
        string? initialValue = null,
        int? maxLength = null,
        string? fontFamily = null)
    {
        return new G9PopupInputField
        {
            Key = key,
            Label = label,
            Placeholder = placeholder,
            IsRequired = isRequired,
            InitialValue = initialValue,
            MaxLength = maxLength,
            Type = G9PopupInputFieldType.Number,
            FontFamily = fontFamily
        };
    }

    public static G9PopupInputField Multiline(
        string key,
        string label,
        string? placeholder = null,
        bool isRequired = false,
        string? initialValue = null,
        int? maxLength = null,
        string? fontFamily = null,
        bool enableVoice = false)
    {
        return TextArea(key, label, placeholder, isRequired, initialValue, maxLength, fontFamily, enableVoice);
    }

    public static G9PopupInputField TextArea(
        string key,
        string label,
        string? placeholder = null,
        bool isRequired = false,
        string? initialValue = null,
        int? maxLength = null,
        string? fontFamily = null,
        bool enableVoice = false)
    {
        return new G9PopupInputField
        {
            Key = key,
            Label = label,
            Placeholder = placeholder,
            IsRequired = isRequired,
            InitialValue = initialValue,
            MaxLength = maxLength,
            Type = G9PopupInputFieldType.TextArea,
            FontFamily = fontFamily,
            EnableVoice = enableVoice
        };
    }

    public static G9PopupInputField CheckBox(
        string key,
        string label,
        IEnumerable<G9PopupInputOption> items,
        bool isRequired = false,
        string? fontFamily = null)
    {
        return new G9PopupInputField
        {
            Key = key,
            Label = label,
            IsRequired = isRequired,
            Items = items?.ToList() ?? [],
            Type = G9PopupInputFieldType.CheckBox,
            TrimValue = false,
            FontFamily = fontFamily
        };
    }

    public static G9PopupInputField CheckBox(
        string key,
        string label,
        bool isRequired = false,
        bool initialChecked = false,
        string? description = null,
        string? fontFamily = null)
    {
        return new G9PopupInputField
        {
            Key = key,
            Label = label,
            Placeholder = description,
            IsRequired = isRequired,
            InitialChecked = initialChecked,
            Items =
            [
                G9PopupInputOption.Create("true", description ?? label, initialChecked)
            ],
            Type = G9PopupInputFieldType.CheckBox,
            TrimValue = false,
            FontFamily = fontFamily
        };
    }

    public static G9PopupInputField RadioButton(
        string key,
        string label,
        IEnumerable<G9PopupInputOption> items,
        bool isRequired = false,
        string? initialValue = null,
        string? fontFamily = null)
    {
        return new G9PopupInputField
        {
            Key = key,
            Label = label,
            IsRequired = isRequired,
            InitialValue = initialValue,
            Items = items?.ToList() ?? [],
            Type = G9PopupInputFieldType.RadioButton,
            TrimValue = false,
            FontFamily = fontFamily
        };
    }
}
