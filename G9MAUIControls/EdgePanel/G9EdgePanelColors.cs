using G9MAUIControls.Theming;

namespace G9MAUIControls.EdgePanel;

/// <summary>
///     Single source of truth for every color the <see cref="G9EdgePanel" /> renders.
///     Theme-aware recipes branched per light/dark, composed from the global
///     <see cref="G9Palette" /> so the panel inherits the app palette.
///     Mirrors the pattern established by <c>G9TabBarColors</c>.
/// </summary>
internal static class G9EdgePanelColors
{
    // ── Panel surface (matches `MapPageContentView` FarmSelector toolbar) ──
    public const float PanelBgAlphaLight = 0.94f;
    public const float PanelBgAlphaDark = 0.92f;
    public const float PanelStrokeAlphaLight = 0.60f;
    public const float PanelStrokeAlphaDark = 0.30f;

    // ── Tab button ──
    public const float TabBgAlphaLight = 0.94f;
    public const float TabBgAlphaDark = 0.92f;
    public const float TabStrokeAlphaLight = 0.45f;
    public const float TabStrokeAlphaDark = 0.25f;

    // ── Menu item ──
    public const float MenuItemHoverAlphaLight = 0.08f;
    public const float MenuItemHoverAlphaDark = 0.12f;
    public const float MenuItemDividerAlpha = 0.12f;

    // ── Backdrop ──
    public const float BackdropAlpha = 0.18f;

    private static bool IsDark()
    {
        var app = Application.Current;
        return app?.UserAppTheme == AppTheme.Dark
               || app is { UserAppTheme: AppTheme.Unspecified, RequestedTheme: AppTheme.Dark };
    }

    /// <summary>Panel card background — matches FarmSelector (SurfaceContainerLowest in light, Dark in dark).</summary>
    public static Color PanelBackground(G9Palette theme)
    {
        return IsDark()
            ? theme.Dark.WithAlpha(PanelBgAlphaDark)
            : theme.SurfaceContainerLowest.WithAlpha(PanelBgAlphaLight);
    }

    /// <summary>Panel stroke — OutlineVariant-based border like FarmSelector.</summary>
    public static Color PanelStroke(G9Palette theme)
    {
        return IsDark()
            ? theme.OutlineVariant.WithAlpha(PanelStrokeAlphaDark)
            : theme.OutlineVariant.WithAlpha(PanelStrokeAlphaLight);
    }

    /// <summary>Tab background — PrimaryContainer based for a subtle accent.</summary>
    public static Color TabBackground(G9Palette theme)
    {
        return IsDark()
            ? theme.PrimaryContainer.WithAlpha(TabBgAlphaDark)
            : theme.PrimaryContainer.WithAlpha(TabBgAlphaLight);
    }

    /// <summary>Tab stroke — PrimaryBorder based.</summary>
    public static Color TabStroke(G9Palette theme)
    {
        return IsDark()
            ? theme.PrimaryBorder.WithAlpha(TabStrokeAlphaDark)
            : theme.PrimaryBorder.WithAlpha(TabStrokeAlphaLight);
    }

    /// <summary>Icon color inside the tab button.</summary>
    public static Color TabIcon(G9Palette theme)
    {
        return theme.OnPrimaryContainer;
    }

    /// <summary>Panel title / heading text.</summary>
    public static Color TitleText(G9Palette theme)
    {
        return theme.OnSurface;
    }

    /// <summary>Panel description / body text.</summary>
    public static Color BodyText(G9Palette theme)
    {
        return theme.OnSurfaceVariant;
    }

    /// <summary>Menu item text.</summary>
    public static Color MenuItemText(G9Palette theme)
    {
        return theme.OnSurface;
    }

    /// <summary>Menu item icon color.</summary>
    public static Color MenuItemIcon(G9Palette theme)
    {
        return theme.OnSurfaceVariant;
    }

    /// <summary>Menu item hover/press overlay.</summary>
    public static Color MenuItemHover(G9Palette theme)
    {
        return IsDark()
            ? theme.OnSurface.WithAlpha(MenuItemHoverAlphaDark)
            : theme.OnSurface.WithAlpha(MenuItemHoverAlphaLight);
    }

    /// <summary>Menu item divider line.</summary>
    public static Color MenuItemDivider(G9Palette theme)
    {
        return theme.Divider.WithAlpha(MenuItemDividerAlpha);
    }

    /// <summary>Semi-transparent backdrop behind the panel when outside-tap-to-close is enabled.</summary>
    public static Color Backdrop(G9Palette theme)
    {
        return theme.Scrim.WithAlpha(BackdropAlpha);
    }

    /// <summary>Close button X icon color (expanded tab).</summary>
    public static Color CloseIcon(G9Palette theme)
    {
        return theme.OnPrimaryContainer;
    }

    /// <summary>
    ///     Sticky header background — slightly elevated above the panel surface so the header
    ///     reads as a distinct toolbar row. Uses SurfaceContainer in light and a slightly
    ///     lighter dark surface in dark mode.
    /// </summary>
    public static Color StickyHeaderBackground(G9Palette theme)
    {
        return IsDark()
            ? theme.SurfaceContainer.WithAlpha(0.80f)
            : theme.SurfaceContainer.WithAlpha(0.70f);
    }
}