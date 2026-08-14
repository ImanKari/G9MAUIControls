using G9MAUIControls.Helpers;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Layouts;
using G9MAUIControls.Icons;

namespace G9MAUIControls.EdgePanel;

/// <summary>
///     Static helper for showing <see cref="G9EdgePanel" /> from anywhere in the app,
///     similar to <see cref="G9MAUIControls.Popup.G9PopupHelper" />.
///     The panel is attached to the current page's visual tree as an overlay.
/// </summary>
public static class G9EdgePanelHelper
{
    private const int LoadedFallbackDelayMs = 160;

    private static ILogger? Logger =>
        G9ServiceProvider.GetServiceNullable<ILoggerFactory>()?.CreateLogger("G9EdgePanelHelper");

    private static G9EdgePanel? _activePanel;

    /// <summary>
    ///     Attaches an edge peek panel containing a custom <see cref="View" /> to the current page.
    ///     By default only the collapsed edge tab is visible — the user taps the tab to slide the
    ///     panel in. Pass <see cref="G9EdgePanelOptions.AutoOpen" /> = true to expand immediately.
    /// </summary>
    /// <param name="content">The custom view to display inside the panel.</param>
    /// <param name="options">Configuration options for the panel appearance and behavior.</param>
    public static G9EdgePanel? ShowCustomView(View content, G9EdgePanelOptions? options = null)
    {
        var host = FindHost();
        if (host is null) return null;

        var panel = CreatePanel(options);
        panel.PanelContent = content;

        Attach(host, panel, options?.AutoOpen ?? false);
        return panel;
    }

    /// <summary>
    ///     Attaches an edge peek panel containing a navigable list menu to the current page.
    ///     By default only the collapsed edge tab is visible — the user taps the tab to expand.
    /// </summary>
    /// <param name="items">The list of menu items to display.</param>
    /// <param name="options">Configuration options for the panel appearance and behavior.</param>
    public static G9EdgePanel? ShowListMenu(IList<G9EdgeMenuItem> items, G9EdgePanelOptions? options = null)
    {
        var host = FindHost();
        if (host is null) return null;

        var panel = CreatePanel(options);
        panel.MenuItems = items;

        Attach(host, panel, options?.AutoOpen ?? false);
        return panel;
    }

    /// <summary>
    ///     Detaches the currently active panel from the page (if any). The panel is animated
    ///     closed first so the user sees a smooth exit.
    /// </summary>
    public static void Dismiss()
    {
        if (_activePanel is null) return;

        DetachActive();
    }

    /// <summary>
    ///     Returns the currently active <see cref="G9EdgePanel" />, if any.
    /// </summary>
    public static G9EdgePanel? ActivePanel => _activePanel;

    #region Internal

    private static G9EdgePanel CreatePanel(G9EdgePanelOptions? options)
    {
        options ??= new G9EdgePanelOptions();

        var panel = new G9EdgePanel
        {
            InputTransparent = true,
            CascadeInputTransparent = false,
            ZIndex = G9EdgePanelMetrics.OverlayZIndex
        };

        // Apply options.
        if (options.Side.HasValue)
            panel.Side = options.Side.Value;

        if (options.WidthRatio.HasValue)
            panel.WidthRatio = options.WidthRatio.Value;

        if (options.TopGap.HasValue)
            panel.TopGap = options.TopGap.Value;

        if (options.MaxPanelHeight.HasValue)
            panel.MaxPanelHeight = options.MaxPanelHeight.Value;

        if (options.MaxPanelHeightRatio.HasValue)
            panel.MaxPanelHeightRatio = options.MaxPanelHeightRatio.Value;

        if (options.AnimationDuration.HasValue)
        {
            panel.OpenAnimationDuration = options.AnimationDuration.Value;
            panel.CloseAnimationDuration = options.AnimationDuration.Value;
        }

        if (options.OpenAnimationDuration.HasValue)
            panel.OpenAnimationDuration = options.OpenAnimationDuration.Value;

        if (options.CloseAnimationDuration.HasValue)
            panel.CloseAnimationDuration = options.CloseAnimationDuration.Value;

        if (options.EnableOutsideTapToClose.HasValue)
            panel.EnableOutsideTapToClose = options.EnableOutsideTapToClose.Value;

        if (options.UseBackdrop.HasValue)
            panel.UseBackdrop = options.UseBackdrop.Value;

        // Helper-managed panels keep the collapsed tab visible by default so the user has a
        // persistent affordance to expand the panel. Callers can override via the option.
        panel.ShowCollapsedTab = options.ShowCollapsedTab ?? true;

        if (options.PanelBackgroundColor is not null)
            panel.PanelBackgroundColor = options.PanelBackgroundColor;

        if (options.TabBackgroundColor is not null)
            panel.TabBackgroundColor = options.TabBackgroundColor;

        if (options.ContentFlowDirection.HasValue)
            panel.ContentFlowDirection = options.ContentFlowDirection.Value;

        if (options.MenuHeader is not null)
            panel.MenuHeader = options.MenuHeader;

        if (options.MenuHeaderAlignment.HasValue)
            panel.MenuHeaderAlignment = options.MenuHeaderAlignment.Value;

        if (options.CollapsedTabIcon.HasValue)
            panel.CollapsedTabIcon = options.CollapsedTabIcon.Value;

        if (options.CloseButtonPlacement.HasValue)
            panel.CloseButtonPlacement = options.CloseButtonPlacement.Value;

        return panel;
    }

    private static void Attach(Layout host, G9EdgePanel panel, bool autoOpen)
    {
        // Detach any previously active panel before adding the new one.
        if (_activePanel is not null)
        {
            DetachPanel(_activePanel);
        }

        _activePanel = panel;
        PrepareLayoutSlot(host, panel);
        panel.IsVisible = true;

        if (!autoOpen)
        {
            host.Children.Add(panel);
            return;
        }

        // Defer Open() until the panel has actually been integrated into the visual tree.
        // On Android, calling Open() in the same frame as Children.Add can race the layout
        // pass and leave the slide animation stuck (panel input-capturing but unrendered).
        var opened = false;

        void OpenOnce()
        {
            if (opened || !ReferenceEquals(_activePanel, panel))
            {
                return;
            }

            opened = true;
            panel.Open();
        }

        EventHandler? loadedHandler = null;
        loadedHandler = (_, _) =>
        {
            panel.Loaded -= loadedHandler;
            OpenOnce();
        };
        panel.Loaded += loadedHandler;

        host.Children.Add(panel);

        panel.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(LoadedFallbackDelayMs), OpenOnce);
    }

    private static void PrepareLayoutSlot(Layout host, G9EdgePanel panel)
    {
        panel.VerticalOptions = LayoutOptions.Fill;
        panel.HorizontalOptions = LayoutOptions.Fill;

        if (host is Grid grid)
        {
            Grid.SetRow(panel, 0);
            Grid.SetColumn(panel, 0);
            Grid.SetRowSpan(panel, Math.Max(1, grid.RowDefinitions.Count));
            Grid.SetColumnSpan(panel, Math.Max(1, grid.ColumnDefinitions.Count));
            return;
        }

        if (host is AbsoluteLayout)
        {
            AbsoluteLayout.SetLayoutFlags(panel, AbsoluteLayoutFlags.All);
            AbsoluteLayout.SetLayoutBounds(panel, new Rect(0, 0, 1, 1));
        }
    }

    private static void DetachActive()
    {
        if (_activePanel is null)
        {
            return;
        }

        var panel = _activePanel;
        if (panel.IsOpen)
        {
            // Animate the close, then detach when finished.
            void OnClosedOnce(object? _, EventArgs __)
            {
                panel.Closed -= OnClosedOnce;
                DetachPanel(panel);
            }

            panel.Closed += OnClosedOnce;
            panel.Close();
            return;
        }

        DetachPanel(panel);
    }

    private static Layout? FindHost()
    {
        // Try to find the current page's root layout.
        var page = GetCurrentPage();
        if (page is null)
        {
            Logger?.LogWarning("G9EdgePanelHelper: no visible page found. Panel skipped.");
            return null;
        }

        // Walk the page content to find a suitable Grid or AbsoluteLayout.
        if (page.Content is Layout layout)
            return layout;

        Logger?.LogWarning("G9EdgePanelHelper: page content is not a Layout. Panel skipped.");
        return null;
    }

    private static ContentPage? GetCurrentPage()
    {
        var app = Application.Current;
        if (app is null) return null;

        var window = app.Windows.FirstOrDefault(w => w.Page is not null);
        var rootPage = window?.Page;

        // Single-page model: window.Page is always a ContentPage (MainPage / LoginPage /
        // CriticalErrorPage). Modal stack walking isn't needed because this helper renders
        // into the visible page's content layout, not any pushed modal page.
        return rootPage as ContentPage;
    }

    private static void DetachPanel(G9EdgePanel panel)
    {
        void Detach()
        {
            if (ReferenceEquals(_activePanel, panel))
            {
                _activePanel = null;
            }

            if (panel.Parent is Layout parent)
            {
                parent.Children.Remove(panel);
            }
        }

        if (MainThread.IsMainThread)
        {
            Detach();
            return;
        }

        MainThread.BeginInvokeOnMainThread(Detach);
    }

    #endregion
}

/// <summary>
///     Configuration options for <see cref="G9EdgePanelHelper" />.
/// </summary>
public sealed class G9EdgePanelOptions
{
    /// <summary>Which edge the panel slides from. Default: Left.</summary>
    public G9EdgeSide? Side { get; set; }

    /// <summary>Panel width as a fraction of parent width (0–1). Default: 0.35.</summary>
    public double? WidthRatio { get; set; }

    /// <summary>Distance from the top edge in device-independent pixels. Default: 112.</summary>
    public double? TopGap { get; set; }

    /// <summary>Maximum panel height. Default: 560.</summary>
    public double? MaxPanelHeight { get; set; }

    /// <summary>
    ///     Maximum panel height as a fraction of parent height (0–1). Default: 0.69. Combined with
    ///     <see cref="MaxPanelHeight" /> as the lower of ratio-based and absolute caps.
    /// </summary>
    public double? MaxPanelHeightRatio { get; set; }

    /// <summary>Sets both open and close slide durations when used alone. Default: 560 ms.</summary>
    public uint? AnimationDuration { get; set; }

    /// <summary>Open (expand) slide duration in milliseconds. Overrides <see cref="AnimationDuration" /> when set.</summary>
    public uint? OpenAnimationDuration { get; set; }

    /// <summary>Close slide duration in milliseconds. Overrides <see cref="AnimationDuration" /> when set.</summary>
    public uint? CloseAnimationDuration { get; set; }

    /// <summary>Whether tapping outside the panel closes it. Default: true.</summary>
    public bool? EnableOutsideTapToClose { get; set; }

    /// <summary>Whether a modal backdrop is shown behind the panel. Default: true.</summary>
    public bool? UseBackdrop { get; set; }

    /// <summary>
    ///     Whether the collapsed edge tab remains after close. Helper-managed panels default
    ///     to <c>true</c> so the user can re-open the panel by tapping the tab.
    /// </summary>
    public bool? ShowCollapsedTab { get; set; }

    /// <summary>
    ///     Whether the helper should expand the panel immediately after attaching. Default
    ///     <c>false</c> — only the collapsed tab is visible until the user taps it.
    /// </summary>
    public bool? AutoOpen { get; set; }

    /// <summary>Override panel background color. When null, theme default is used.</summary>
    public Color? PanelBackgroundColor { get; set; }

    /// <summary>Override tab background color. When null, theme default is used.</summary>
    public Color? TabBackgroundColor { get; set; }

    /// <summary>
    ///     Text/icon/menu layout inside the panel. When omitted, matches the active app culture
    ///     (same as <c>CurrentFlowDirection</c>).
    /// </summary>
    public G9EdgePanelContentDirection? ContentFlowDirection { get; set; }

    /// <summary>Optional title row above list menu items (root level only from helper).</summary>
    public G9EdgeMenuHeader? MenuHeader { get; set; }

    /// <summary>
    ///     Horizontal alignment of the sticky header label / custom view. Default
    ///     <see cref="G9EdgeMenuHeaderAlignment.Auto"/> follows
    ///     <see cref="ContentFlowDirection"/> so the title sits on the leading edge in
    ///     both English and Persian. Use <see cref="G9EdgeMenuHeaderAlignment.Center"/>
    ///     to balance the title in the middle, or the explicit physical values
    ///     (<see cref="G9EdgeMenuHeaderAlignment.LeftToRight"/>,
    ///     <see cref="G9EdgeMenuHeaderAlignment.RightToLeft"/>) to pin the title to a
    ///     specific edge regardless of content direction.
    /// </summary>
    public G9EdgeMenuHeaderAlignment? MenuHeaderAlignment { get; set; }

    /// <summary>
    ///     Override the icon shown on the collapsed tab handle. When null (default) the panel
    ///     uses a directional chevron that points toward the panel's opening side
    ///     (<c>ChevronRight</c> for Left side, <c>ChevronLeft</c> for Right side).
    ///     Set to any <see cref="G9IconSource"/> value to use a custom icon instead.
    ///     The close icon (×) when the panel is expanded is always <c>G9Glyphs.Clear</c>
    ///     and is not affected by this property.
    /// </summary>
    public G9IconSource? CollapsedTabIcon { get; set; }

    /// <summary>
    ///     How the expanded close (×) tab sits relative to the panel's inner corner.
    ///     <see cref="G9EdgeCloseButtonPlacement.Inset"/> (default) keeps the legacy
    ///     look where the close button sits inside the panel near the corner.
    ///     <see cref="G9EdgeCloseButtonPlacement.OnCorner"/> centres the close circle
    ///     ON the inner corner border (half inside / half outside the panel).
    /// </summary>
    public G9EdgeCloseButtonPlacement? CloseButtonPlacement { get; set; }
}
