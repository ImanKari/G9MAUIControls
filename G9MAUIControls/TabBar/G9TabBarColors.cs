using G9MAUIControls.Theming;

namespace G9MAUIControls.TabBar;

/// <summary>
///     Single source of truth for every color the <see cref="G9TabBar" /> renders.
///     Every entry exposes an explicit *light* and *dark* recipe so theme-aware
///     adjustments live in one file. Colors are still composed from the global
///     <see cref="G9Palette" /> so the menu inherits the app palette, but the
///     alpha/role knobs are constants here.
/// </summary>
internal static class G9TabBarColors
{
    // ── Bar surface (translucent glass) ──
    // Near-opaque acrylic so the bar reads as a separated panel even over busy
    // backgrounds (map tiles, photos, dashboards). Real GPU blur is intentionally
    // avoided — see G9TabBar.md for the page-side scrim approach.
    public const float BarBgAlphaLight = 0.94f;
    public const float BarBgAlphaDark = 0.92f;

    // ── Bar outline (theme-appropriate ink) ──
    // Light theme: dark ink so the line is visible against white pages.
    // Dark theme: white ink so the line is visible against dark pages.
    public const float BarStrokeAlphaLight = 0.22f;
    public const float BarStrokeAlphaDark = 0.22f;

    // ── Bar top-edge "glass" highlight ──
    // 1px inset hairline painted only along the top edge of the bar (and around the
    // notch on the FAB side). Gives the bar a lit, separated edge without GPU blur.
    public const float BarTopHighlightAlphaLight = 0.85f;
    public const float BarTopHighlightAlphaDark = 0.18f;

    // ── FAB / sub-menu outer glass ──
    /// <summary>Outer FAB and sub-menu shells reuse the bar recipe so the surfaces feel unified.</summary>
    public const float FabSurfaceAlphaLight = BarBgAlphaLight;
    public const float FabSurfaceAlphaDark = BarBgAlphaDark;

    // ── FAB / sub-menu inner accent ring (primary gradient) ──
    /// <summary>Stops are sourced from <see cref="G9Palette.Primary" /> family — this is just the gradient shape.</summary>
    public const double FabInnerRadialCenterX = 0.5;
    public const double FabInnerRadialCenterY = 0.5;
    public const double FabInnerRadialRadius = 0.65;

    // ── FAB / sub-menu inner border ──
    public const float FabInnerBorderAlphaLight = 0.92f;
    public const float FabInnerBorderAlphaDark = 0.88f;

    // ── FAB / sub-menu shadow ──
    // There are NO MAUI `Shadow` colours here. The bar's and the FAB's drop shadows are Skia
    // circles/paths in G9TabBarShadowView (see ApplyTheme); every other G9TabBar surface is
    // flat by policy. See G9Controls.md → "No shadows".

    // ── Sub-menu label (theme-aware text on the bar / glass background) ──
    public const float SubMenuLabelAlphaLight = 1f;
    public const float SubMenuLabelAlphaDark = 1f;

    // ── Inactive bottom item (icon + label, when not selected) ──
    // Light theme: dark ink at reduced alpha reads fine on the light glass bar.
    // Dark theme: MUST stay a bright neutral — the previous Outline-based recipe rendered
    // almost invisible on the dark glass. Pin it to a fixed light grey (≥ #EEEEEE) so
    // unselected icons/labels are clearly legible. See G9TabBar.md.
    public const float InactiveLight = 0.82f;
    public static readonly Color InactiveDarkColor = Color.FromArgb("#EEEEEE");

    // ── Selected-item indicator pill ──
    // A primary-tinted fill (not the very pale PrimaryContainer) so the selected tab's pill
    // is clearly visible on the glass bar in both themes.
    public const float SelectedIndicatorAlphaLight = 0.28f;
    public const float SelectedIndicatorAlphaDark = 0.36f;

    // ── Backdrop (transparent tap target while a menu is open) ──
    /// <summary>Backdrop stays fully transparent — the tap is what matters, not pixels.</summary>
    public const float BackdropAlpha = 0f;

    // ── Overflow item highlight (when its source item is the active selection) ──
    /// <summary>
    ///     The selected overflow item paints its entire glass shell with the FAB's primary
    ///     gradient, so it reads as "this is the selection" — same green as the FAB action.
    /// </summary>
    public static Brush OverflowSelectedFill(G9Palette theme) => FabInnerBackground(theme);
    public static Color OverflowSelectedStroke(G9Palette theme) => FabInnerBorder(theme);
    public static Color OverflowSelectedIconColor(G9Palette theme) => theme.OnPrimary;
    public static Color OverflowSelectedLabelColor(G9Palette theme) => theme.OnPrimary;

    private static bool IsDark()
    {
        var app = Application.Current;
        return app?.UserAppTheme == AppTheme.Dark
               || app is { UserAppTheme: AppTheme.Unspecified, RequestedTheme: AppTheme.Dark };
    }

    public static Color BarBackground(G9Palette theme)
    {
        // Tinted-acrylic surface. We pick the brightest container for light and the
        // same family for dark so the bar reads as a separated panel even over a map
        // tile or a photo.
        return IsDark()
            ? theme.SurfaceContainerLowest.WithAlpha(BarBgAlphaDark)
            : theme.SurfaceContainerLowest.WithAlpha(BarBgAlphaLight);
    }

    public static Color BarStroke(G9Palette theme)
    {
        // Outline must be visible against the *page*, not the bar. Light → dark ink,
        // dark → light ink, both at low alpha.
        return IsDark()
            ? theme.White.WithAlpha(BarStrokeAlphaDark)
            : theme.Black.WithAlpha(BarStrokeAlphaLight);
    }

    /// <summary>
    ///     1px inset hairline painted along the top edge of the bar by the chrome drawable.
    ///     Bright in light theme (white-ish glass highlight), subtle in dark theme.
    /// </summary>
    public static Color BarTopHighlight(G9Palette theme)
    {
        return IsDark()
            ? theme.White.WithAlpha(BarTopHighlightAlphaDark)
            : theme.White.WithAlpha(BarTopHighlightAlphaLight);
    }

    public static Color FabSurface(G9Palette theme)
    {
        return BarBackground(theme);
    }

    public static Color FabSurfaceStroke(G9Palette theme)
    {
        return BarStroke(theme);
    }

    public static Brush FabInnerBackground(G9Palette theme)
    {
        return new RadialGradientBrush
        {
            Center = new Point(FabInnerRadialCenterX, FabInnerRadialCenterY),
            Radius = FabInnerRadialRadius,
            GradientStops =
            {
                new GradientStop(theme.PrimaryPressed, 0f),
                new GradientStop(theme.Primary, 0.55f),
                new GradientStop(theme.PrimaryHover, 1f)
            }
        };
    }

    public static Color FabInnerBorder(G9Palette theme)
    {
        return IsDark()
            ? theme.SurfaceContainerHighest.WithAlpha(FabInnerBorderAlphaDark)
            : theme.SurfaceContainerLowest.WithAlpha(FabInnerBorderAlphaLight);
    }

    public static Color SubMenuLabel(G9Palette theme)
    {
        return IsDark()
            ? theme.White.WithAlpha(SubMenuLabelAlphaDark)
            : theme.TextPrimary.WithAlpha(SubMenuLabelAlphaLight);
    }

    public static Color InactiveBottomItem(G9Palette theme)
    {
        return IsDark()
            ? InactiveDarkColor
            : theme.TextPrimary.WithAlpha(InactiveLight);
    }

    public static Color SelectedBottomItem(G9Palette theme)
    {
        // Light: dark green ink on the light-green pill. Dark: bright neutral so the label/icon
        // stay legible on the (translucent primary) pill over the dark bar.
        return IsDark() ? InactiveDarkColor : theme.OnPrimaryContainer;
    }

    public static Color SelectedIndicator(G9Palette theme)
    {
        // Primary-tinted pill (visibly green) instead of the near-invisible pale PrimaryContainer.
        return IsDark()
            ? theme.Primary.WithAlpha(SelectedIndicatorAlphaDark)
            : theme.Primary.WithAlpha(SelectedIndicatorAlphaLight);
    }

    public static Color BackdropFill(G9Palette _)
    {
        return Colors.Transparent;
    }
}
