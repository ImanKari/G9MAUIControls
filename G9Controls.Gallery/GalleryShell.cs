using G9Controls.Gallery.Pages;
using G9MAUIControls.Controls;
using G9MAUIControls.Icons;
using G9MAUIControls.Theming;

namespace G9Controls.Gallery;

/// <summary>
///     Hosts the gallery pages and — the part that matters — the theme and direction toggles.
///     <para>
///         Both toggles are in the shell rather than on a settings page on purpose: the point of the gallery
///         is to flip theme and direction <b>while looking at a control</b>. A toggle you have to navigate
///         away to reach tests a reload, not a live re-theme, and live re-theming is exactly what the
///         palette and culture hooks claim to support.
///     </para>
/// </summary>
public sealed class GalleryShell : Shell
{
    public GalleryShell()
    {
        // Shell's own chrome is left alone. The suite's G9TabBar is verified on its own page rather than
        // by replacing navigation here, so a bug in it cannot make the gallery unusable.
        Items.Add(new ShellContent { Title = "Glyphs", ContentTemplate = new DataTemplate(() => new GlyphPage()) });
        Items.Add(new ShellContent { Title = "Inputs", ContentTemplate = new DataTemplate(() => new InputsPage()) });
        Items.Add(new ShellContent { Title = "Actions", ContentTemplate = new DataTemplate(() => new ActionsPage()) });
        Items.Add(new ShellContent { Title = "Overlays", ContentTemplate = new DataTemplate(() => new OverlaysPage()) });
        Items.Add(new ShellContent { Title = "Navigation", ContentTemplate = new DataTemplate(() => new NavigationSurfacesPage()) });
        Items.Add(new ShellContent { Title = "Satellites", ContentTemplate = new DataTemplate(() => new SatellitesPage()) });

        ToolbarItems.Add(new ToolbarItem
        {
            Text = "Theme",
            Command = new Command(ToggleTheme)
        });

        ToolbarItems.Add(new ToolbarItem
        {
            Text = "RTL",
            Command = new Command(ToggleDirection)
        });
    }

    private static void ToggleTheme()
    {
        var next = Application.Current?.UserAppTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        GalleryState.SetTheme(next);
    }

    private void ToggleDirection()
    {
        GalleryState.SetRtl(!GalleryState.IsRtl);

        // Shell itself does not follow G9Culture — it is MAUI's, not the suite's — so the gallery sets it
        // explicitly. A consumer app does the same from wherever it switches language.
        FlowDirection = GalleryState.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }
}
