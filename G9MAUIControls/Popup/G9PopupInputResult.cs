namespace G9MAUIControls.Popup;

public sealed record G9PopupInputResult(
    bool IsSubmitted,
    IReadOnlyDictionary<string, string> Values,
    IReadOnlyDictionary<string, IReadOnlyList<string>>? ArrayValues = null,
    IReadOnlyDictionary<string, string>? ValidationErrors = null)
{
    public static G9PopupInputResult Cancel()
    {
        return new G9PopupInputResult(
            false,
            new Dictionary<string, string>(),
            new Dictionary<string, IReadOnlyList<string>>());
    }

    public static G9PopupInputResult Submit(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? arrayValues = null)
    {
        return new G9PopupInputResult(
            true,
            new Dictionary<string, string>(values),
            arrayValues is null
                ? new Dictionary<string, IReadOnlyList<string>>()
                : new Dictionary<string, IReadOnlyList<string>>(arrayValues));
    }

    public static G9PopupInputResult ValidationFailed(
        IReadOnlyDictionary<string, string> values,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? arrayValues,
        IReadOnlyDictionary<string, string> errors)
    {
        return new G9PopupInputResult(
            false,
            new Dictionary<string, string>(values),
            arrayValues is null
                ? new Dictionary<string, IReadOnlyList<string>>()
                : new Dictionary<string, IReadOnlyList<string>>(arrayValues),
            new Dictionary<string, string>(errors));
    }

    public string? GetValue(string key)
    {
        return Values.TryGetValue(key, out var value) ? value : null;
    }

    public IReadOnlyList<string> GetArrayValues(string key)
    {
        if (ArrayValues is not null && ArrayValues.TryGetValue(key, out var values))
        {
            return values;
        }

        return [];
    }
}
