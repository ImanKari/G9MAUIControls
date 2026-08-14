using G9MAUIControls.Theming;
using G9MAUIControls.Localization;
using G9MAUIControls.Icons;

namespace G9MAUIControls.Popup;

public sealed record G9PopupVisualProfile(
    Color IconColor,
    Color CardBackground,
    Color BorderColor,
    Color TitleColor,
    Color MessageColor,
    Color ButtonBackground,
    Color ButtonTextColor,
    Color OverlayColor,
    double? Width,
    double? Height,
    double CornerRadius,
    Thickness Padding,
    G9IconSource Icon,
    G9PopupAnimationType Animation,
    uint AnimationDuration,
    string SemanticIconDescription)
{
    public static G9PopupVisualProfile Create(G9PopupDescriptor descriptor, G9PopupSettings settings)
    {
        var palette = G9Palette.Current;
        var themeSurface = palette.Surface;
        var themeOnSurface = palette.OnSurface;

        // The accent is what makes a popup READ as its type: it paints the header icon + its badge
        // tint, the title, and the primary footer button. Every type must therefore take its own
        // semantic token — Success/Warning/Error/Info — and never Primary. Mapping Warning (and
        // Information) onto Primary is what made every popup in the app look the same green
        // "everything is fine" alert regardless of what it was actually saying.
        var accent = descriptor.Type switch
        {
            G9PopupType.Success => palette.Success,
            G9PopupType.Warning => palette.Warning,
            G9PopupType.Error => palette.Error,
            _ => palette.Info
        };

        // Foreground for the filled primary button. Taken from the matching On* token rather than a
        // hard-coded white: in the dark palette Warning/Success/Error are light tints whose On*
        // partner is near-black, and white-on-amber is unreadable there.
        var onAccent = descriptor.Type switch
        {
            G9PopupType.Success => palette.OnSuccess,
            G9PopupType.Warning => palette.OnWarning,
            G9PopupType.Error => palette.OnError,
            _ => palette.OnInfo
        };

        var overlayOpacity = settings.OverlayOpacity ?? 0.45f;
        var overlayColor = settings.OverlayColor ?? themeOnSurface.WithAlpha(overlayOpacity);

        var icon = settings.IconOverride ?? descriptor.Type switch
        {
            G9PopupType.Success => G9Glyphs.Success,
            G9PopupType.Warning => G9Glyphs.Warning,
            G9PopupType.Error => G9Glyphs.Error,
            _ => G9Glyphs.Info
        };

        var padding = settings.Padding ?? new Thickness(20, 16, 20, 12);
        var semanticDescription = descriptor.Type switch
        {
            G9PopupType.Success => GetSemantic("Success"),
            G9PopupType.Warning => GetSemantic("Warning"),
            G9PopupType.Error => GetSemantic("Error"),
            _ => GetSemantic("Information")
        };

        return new G9PopupVisualProfile(
            accent,
            settings.CardBackgroundColor ?? themeSurface,
            settings.BorderColor ?? themeSurface,
            settings.TitleColor ?? accent,
            settings.MessageColor ?? themeOnSurface,
            accent,
            onAccent,
            overlayColor,
            settings.Width,
            settings.Height,
            settings.CornerRadius ?? 16,
            padding,
            icon,
            settings.Animation ?? G9PopupAnimationType.SlideUp,
            settings.AnimationDuration ?? 300,
            semanticDescription);
    }

    private static string GetSemantic(string fallback)
    {
        return G9Strings.Resolve(fallback) ?? fallback;
    }
}
