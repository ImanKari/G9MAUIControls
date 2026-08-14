using G9MAUIControls.Helpers;
using G9MAUIControls.Theming;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace G9MAUIControls.Controls;

/// <summary>
///     Single source of truth for every color the new app controls render.
///     // TODO (palette step): every color recipe in this file must move to
///     // <see cref="G9Palette" /> so app theming can swap palettes without touching
///     // control internals. Right now the palette holds the base brand tokens and we
///     // add G9 alpha recipes here.
/// </summary>
/// <remarks>
///     Public for the same reason as <see cref="G9Metrics" />: a control built on the suite's public base
///     classes needs the suite's alpha recipes and gradient helpers to match it.
/// </remarks>
public static class G9Colors
{
    // ── Inset highlights ──
    public const float InsetHighlightTopAlpha = 0.20f;
    public const float InsetHighlightBottomAlpha = 0.10f;

    /// <summary>
    ///     Alpha for the ink ripple that plays under leading / trailing icons in
    ///     <see cref="G9OutlinedFieldBase" />. The ripple is tinted with the icon's
    ///     resolved color and sits BELOW the icon glyph, so it must be subtle enough that
    ///     the glyph stays readable through the ripple at peak progress while still being
    ///     legible against the field's background.
    ///     <para>
    ///         0.32 was reached after testing the previous 0.22 value across several
    ///         OLED phones — under bright ambient light the ripple was nearly
    ///         invisible against the light field surface, breaking the
    ///         "tap-was-registered" feedback the ripple is supposed to deliver. 0.32
    ///         is the lowest value that stays legible on a sunlit OLED phone screen
    ///         without muddying the icon glyph (the ripple still sits well below the
    ///         glyph because the icon is rendered at full alpha on top, and the glyph
    ///         stroke is several times darker than the tinted ripple).
    ///     </para>
    /// </summary>
    public const float IconRippleAlpha = 0.32f;

    // ── Range slider tooltip ──
    public const float SliderTooltipAlphaActive = 0.10f;
    public const float SliderTooltipBorderAlpha = 1f;

    // ── Switch (off track) ──
    // Palette-derived so the switch tracks the active theme. Off-track uses the neutral
    // outline-variant surface; on-track uses brand Primary. Thumbs read OnPrimary so they
    // stay legible against both track states in light and dark.
    public static Color SwitchOffTrack(G9Palette palette)
    {
        return IsDark()
            ? palette.SurfaceContainerHighest
            : palette.OutlineVariant;
    }

    public static Color SwitchOnTrack(G9Palette palette)
    {
        return palette.Primary;
    }

    public static Color SwitchThumbOff(G9Palette palette)
    {
        return IsDark()
            ? palette.OnSurfaceVariant
            : palette.Surface;
    }

    public static Color SwitchThumbOn(G9Palette palette)
    {
        return IsDark()
            ? palette.OnPrimaryContainer
            : palette.OnPrimary;
    }

    private static bool IsDark()
    {
        var app = Application.Current;
        return app?.UserAppTheme == AppTheme.Dark
               || app is { UserAppTheme: AppTheme.Unspecified, RequestedTheme: AppTheme.Dark };
    }

    // ── Helpers ──
    public static RoundRectangle Round(double radius)
    {
        return new RoundRectangle { CornerRadius = new CornerRadius(radius) };
    }

    public static Brush BuildSolidOrGradient(Color baseColor, bool useGradient)
    {
        if (!useGradient)
        {
            return new SolidColorBrush(baseColor);
        }

        return new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            [
                new GradientStop(G9ColorHelper.Lighten(baseColor, 0.08), 0),
                new GradientStop(baseColor, 0.5f),
                new GradientStop(G9ColorHelper.Darken(baseColor, 0.08), 1)
            ]
        };
    }

    // NOTE: there is deliberately no BuildShadow helper here. The app is shadow-free — MAUI's
    // `Shadow` falls back to PlatformWrapperView.drawShadowViaDispatchDraw on Android, which
    // rasterizes the view into a bitmap and box-blurs it in SOFTWARE on the UI thread every draw
    // pass. That was the cause of the July 2026 ANR. See G9Controls.md → "No shadows".
}
