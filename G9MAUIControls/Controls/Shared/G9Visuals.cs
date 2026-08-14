using G9MAUIControls.Controls;
using G9MAUIControls.Helpers;
using G9MAUIControls.Icons;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
namespace G9MAUIControls.Controls;

/// <summary>
///     Shared visual helpers (icons, variant resolution, keyboard, etc.).
///     // TODO (palette step): variant color recipes here will move to <see cref="G9Palette" />.
/// </summary>
/// <remarks>
///     Public so an external control can resolve the same variant colours, keyboard mapping and cultural
///     typeface the built-in controls do. Without it, a consumer's "Primary" button would have to
///     re-derive the palette mapping by hand and would drift the first time the suite retuned it.
/// </remarks>
public static class G9Visuals
{
    public static bool IsRtl => G9Culture.IsRtl;

    /// <summary>
    ///     The fully-resolved colors a <see cref="G9Button" /> paints, after applying
    ///     any <c>BaseBackgroundColor</c> / <c>TextColor</c> escape hatches over the named
    ///     variant. Shape mirrors <see cref="G9VariantColors" /> so the button's apply
    ///     pass treats both paths identically.
    /// </summary>
    public readonly record struct ButtonVisualResult(
        Color Background,
        Color Text,
        Color Stroke,
        bool UsesGradient);


    /// <summary>
    ///     Resolve the typeface that text inside our controls should use right now.
    ///     Picks the Persian face (<see cref="G9Culture.RtlFontFamily" />)
    ///     when the active culture is RTL, otherwise the Latin face
    ///     (<see cref="G9Culture.LtrFontFamily" />). When the consumer
    ///     supplied a non-empty <paramref name="customFont" /> it wins — that's the
    ///     escape hatch for fields that need a brand-specific typeface.
    ///     <para>
    ///         <b>Why not let MAUI pick.</b> Without an explicit FontFamily the
    ///         platform fallback walks Android's family chain, which on Persian
    ///         strings drops to the system default sans-serif and renders Persian
    ///         glyphs in a noticeably mismatched face vs. the rest of our UI (the
    ///         floating label, the helper text, etc., all of which we set
    ///         explicitly via the app's <c>CulturalFont</c> resource). Setting it
    ///         per-control here keeps every field's inner text in the same
    ///         typeface as the surrounding UI, regardless of platform defaults.
    ///     </para>
    /// </summary>
    public static string ResolveCulturalFont(string? customFont = null)
    {
        if (!string.IsNullOrWhiteSpace(customFont)) return customFont!;
        return G9Culture.IsRtl
            ? G9Culture.RtlFontFamily ?? string.Empty
            : G9Culture.LtrFontFamily ?? string.Empty;
    }

    public static Keyboard ResolveKeyboard(G9KeyboardType keyboardType)
    {
        return keyboardType switch
        {
            G9KeyboardType.Email => Keyboard.Email,
            G9KeyboardType.Phone => Keyboard.Telephone,
            G9KeyboardType.Number => Keyboard.Numeric,
            G9KeyboardType.Url => Keyboard.Url,
            _ => Keyboard.Default
        };
    }

    public static G9VariantColors ResolveButtonVariant(G9ButtonVariant variant)
    {
        var p = G9Palette.Current;

        return variant switch
        {
            G9ButtonVariant.Tonal => new G9VariantColors(
                p.PrimaryContainer,
                p.OnPrimaryContainer,
                G9ColorHelper.Mix(p.Primary, Colors.Transparent, 0.65),
                false),
            G9ButtonVariant.Default => new G9VariantColors(p.Default, p.OnDefault, p.DefaultBorder, false),
            G9ButtonVariant.Secondary => new G9VariantColors(p.SecondaryContainer, p.OnSecondaryContainer, p.SecondaryBorder, false),
            G9ButtonVariant.Info => new G9VariantColors(p.Info, p.OnInfo, p.InfoBorder, true),
            G9ButtonVariant.Success => new G9VariantColors(p.Success, p.OnSuccess, p.SuccessBorder, true),
            G9ButtonVariant.Warning => new G9VariantColors(p.Warning, p.OnWarning, p.WarningBorder, true),
            G9ButtonVariant.Error => new G9VariantColors(p.Error, p.OnError, p.ErrorBorder, true),
            // Soft destructive: the container fill carries the Error glyph (NOT OnErrorContainer, which is
            // near-black maroon and reads as plain dark text). Flat — no gradient — so it stays quiet next
            // to a real primary action. See G9ButtonVariant.ErrorTonal.
            G9ButtonVariant.ErrorTonal => new G9VariantColors(p.ErrorContainer, p.Error, p.ErrorBorder, false),
            G9ButtonVariant.Surface => new G9VariantColors(p.SurfaceVariant, p.OnSurface, p.OutlineBorder, false),
            G9ButtonVariant.Outline => new G9VariantColors(Colors.Transparent, p.TextPrimary, p.Outline, false),
            G9ButtonVariant.Text => new G9VariantColors(Colors.Transparent, p.Primary, p.Outline, false),
            _ => new G9VariantColors(p.Primary, p.OnPrimary, p.PrimaryBorder, true)
        };
    }

    public static Color ResolveProgressColor(G9ProgressType type)
    {
        var p = G9Palette.Current;
        return type switch
        {
            G9ProgressType.Success => p.Success,
            G9ProgressType.Warning => p.Warning,
            G9ProgressType.Error => p.Error,
            _ => p.Primary
        };
    }

    /// <summary>
    ///     True when at least ONE swatch color is set. Drives the shared variety-color convention:
    ///     0 colors → no swatch, 1 color → solid circle, 2 colors → split circle.
    /// </summary>
    public static bool HasSwatch(Color? first, Color? second) => first is not null || second is not null;

    /// <summary>
    ///     Variety color swatch shown in place of an icon for items that only carry stored colors
    ///     (a variety's FirstColor / SecondColor). ONE convention shared by every surface that shows
    ///     a variety — G9 combos / pickers / selection lists AND the read-only variety displays:
    ///     <list type="bullet">
    ///         <item>no colors → nothing (a zero-size, invisible placeholder);</item>
    ///         <item>one color → a solid filled circle of that color;</item>
    ///         <item>two colors → a circle split into two vertical halves (first | second).</item>
    ///     </list>
    /// </summary>
    public static View CreateSwatch(Color? first, Color? second, double size)
    {
        var hasFirst = first is not null;
        var hasSecond = second is not null;

        if (!hasFirst && !hasSecond)
        {
            return new BoxView { WidthRequest = 0, HeightRequest = 0, Opacity = 0, InputTransparent = true };
        }

        // Exactly one colour → a solid filled circle of that colour.
        if (hasFirst ^ hasSecond)
        {
            return new Border
            {
                WidthRequest = size,
                HeightRequest = size,
                StrokeThickness = 0,
                StrokeShape = G9Colors.Round(size / 2),
                BackgroundColor = (first ?? second)!,
                InputTransparent = true
            };
        }

        // Two colours → circle split into two vertical halves (first | second).
        var secondHalf = new BoxView { Color = second!, InputTransparent = true };
        Grid.SetColumn(secondHalf, 1);

        return new Border
        {
            WidthRequest = size,
            HeightRequest = size,
            StrokeThickness = 0,
            StrokeShape = G9Colors.Round(size / 2),
            BackgroundColor = Colors.Transparent,
            InputTransparent = true,
            Content = new Grid
            {
                WidthRequest = size,
                HeightRequest = size,
                ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Star)],
                InputTransparent = true,
                Children = { new BoxView { Color = first!, InputTransparent = true }, secondHalf }
            }
        };
    }
}

/// <summary>
///     The four colours a variant resolves to: fill, foreground, stroke, and whether the fill is a
///     gradient. Public because <see cref="G9Visuals.ResolveButtonVariant" /> returns it.
/// </summary>
public readonly record struct G9VariantColors(
    Color Background,
    Color Text,
    Color Stroke,
    bool UsesGradient);
