// Internal record used by G9PopupHelper to describe a single popup show request before it is
// merged with G9PopupSettings + per-type defaults at presentation time. Lives in the G9Popup
// component folder; namespace follows the folder path (Components.G9Popup).
namespace G9MAUIControls.Popup;

public sealed record G9PopupDescriptor
{
    public required G9PopupType Type { get; init; }
    public string? Title { get; init; }
    public string? Message { get; init; }
    public View? CustomView { get; init; }
    public IReadOnlyList<G9PopupButton>? Buttons { get; init; }
    public G9PopupSettings? Settings { get; init; }
    public G9PopupAnimationType Animation { get; init; } = G9PopupAnimationType.SlideUp;

    public static G9PopupDescriptor ForPreset(
        G9PopupType type,
        string message,
        string? title,
        IEnumerable<G9PopupButton>? buttons,
        G9PopupSettings? settings,
        G9PopupAnimationType animation)
    {
        return new G9PopupDescriptor
        {
            Type = type,
            Message = message,
            Title = title,
            Buttons = buttons?.ToList(),
            Settings = settings,
            Animation = animation
        };
    }

    public static G9PopupDescriptor ForCustom(
        View view,
        string? title,
        IEnumerable<G9PopupButton>? buttons,
        G9PopupSettings? settings,
        G9PopupAnimationType animation,
        G9PopupType type = G9PopupType.Custom)
    {
        return new G9PopupDescriptor
        {
            Type = type,
            CustomView = view,
            Title = title,
            Buttons = buttons?.ToList(),
            Settings = settings,
            Animation = animation
        };
    }
}
