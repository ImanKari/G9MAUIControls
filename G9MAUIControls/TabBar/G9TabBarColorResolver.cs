using G9MAUIControls.Theming;

namespace G9MAUIControls.TabBar;

/// <summary>
///     Thin theme-aware bridge to <see cref="G9TabBarColors" />. Exists to keep call
///     sites short and to preserve the historical resolver name; the real per-theme
///     recipes live in <see cref="G9TabBarColors" />.
/// </summary>
internal static class G9TabBarColorResolver
{
    public static bool IsDarkTheme()
    {
        var app = Application.Current;
        return app?.UserAppTheme == AppTheme.Dark
               || app is { UserAppTheme: AppTheme.Unspecified, RequestedTheme: AppTheme.Dark };
    }

    public static Color ResolveBarColor(G9Palette theme) => G9TabBarColors.BarBackground(theme);

    public static Brush ResolveFabBackground(G9Palette theme) => G9TabBarColors.FabInnerBackground(theme);

    public static Color ResolveFabBorderColor(G9Palette theme) => G9TabBarColors.FabInnerBorder(theme);

    public static Color ResolveSubMenuContentColor(G9Palette theme) => G9TabBarColors.SubMenuLabel(theme);

    public static Color ResolveInactiveMenuColor(G9Palette theme) => G9TabBarColors.InactiveBottomItem(theme);

    public static Color ResolveSelectedContentColor(G9Palette theme) => G9TabBarColors.SelectedBottomItem(theme);

    public static Color ResolveSelectedIndicatorColor(G9Palette theme) => G9TabBarColors.SelectedIndicator(theme);

    public static Color ResolveSurfaceStrokeColor(G9Palette theme) => G9TabBarColors.BarStroke(theme);

    public static Color ResolveFabSurfaceColor(G9Palette theme) => G9TabBarColors.FabSurface(theme);

    public static Color ResolveFabSurfaceStrokeColor(G9Palette theme) => G9TabBarColors.FabSurfaceStroke(theme);
}
