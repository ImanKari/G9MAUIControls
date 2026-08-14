using System.Collections.ObjectModel;
using G9MAUIControls.Controls;
using G9MAUIControls.Hosting;
using G9MAUIControls.Icons;
using G9MAUIControls.TabBar;
using G9MAUIControls.Theming;

namespace G9Controls.Gallery.Pages;

/// <summary>
///     The tab bar and the tab view.
///     <para>
///         <b>The tab bar's FAB notch is the thing to look at.</b> It is the one place in the suite that uses
///         SkiaSharp, because the concave silhouette needs a genuinely blurred path on the render thread — a
///         MAUI <c>Shadow</c> there is a measured ANR (<c>G9Controls.md</c> §0). Confirm the notch reads as a
///         smooth cut-out with no hard edge and no clipped halo, in both themes.
///     </para>
///     <para>
///         In RTL the tab <b>order</b> reverses while the glyphs must not mirror — the tab bar pins its
///         internal hosts to LTR and reverses the item iteration instead, precisely so a glyph cannot
///         double-flip (§9).
///     </para>
/// </summary>
public sealed class NavigationSurfacesPage : G9PageBase
{
    public NavigationSurfacesPage()
    {
        Title = "Navigation";
        Content = Build();
    }

    private static View Build()
    {
        var palette = G9Palette.Current;
        var stack = new VerticalStackLayout { Spacing = 18, Padding = new Thickness(16) };

        stack.Add(new Label
        {
            Text = "FAB notch: a smooth concave cut-out, no hard edge, no clipped halo, in BOTH themes. "
                 + "In RTL the tab ORDER reverses but the glyphs must not mirror.",
            FontSize = 12,
            TextColor = palette.OnSurfaceVariant
        });

        var tabBar = new G9TabBar
        {
            // FabIndex marks which slot is the raised centre button; the notch is cut around it.
            FabIndex = 2,
            Items =
            [
                new G9TabBarItem("Home", G9Glyph.Menu),
                new G9TabBarItem("Search", G9Glyph.Search),
                new G9TabBarItem("Add", G9Glyph.Plus),
                new G9TabBarItem("Time", G9Glyph.Clock),
                new G9TabBarItem("Info", G9Glyph.Info)
            ],
            SubMenuItems =
            [
                new G9TabBarItem("New note", G9Glyph.Plus),
                new G9TabBarItem("Scan", G9Glyph.Search)
            ]
        };
        stack.Add(ActionsPage.Section("G9TabBar — FAB notch + overflow sub-menu", palette, tabBar));

        stack.Add(ActionsPage.Section("G9TabView — underlined", palette, NewTabView(G9TabStyle.Underlined)));
        stack.Add(ActionsPage.Section("G9TabView — pill", palette, NewTabView(G9TabStyle.Pill)));

        stack.Add(ActionsPage.Section("G9Expander", palette,
            new G9Expander
            {
                Title = "Collapsed by default",
                Icon = G9Glyph.Info,
                ExpanderContent = new Label { Text = "Revealed content.", TextColor = palette.OnSurface }
            },
            new G9Expander
            {
                Title = "Expanded",
                IsExpanded = true,
                ExpanderContent = new Label { Text = "Already open.", TextColor = palette.OnSurface }
            }));

        stack.Add(ActionsPage.Section("G9NavCard", palette,
            new G9NavCard { Title = "A navigation card", Subtitle = "With a subtitle", Icon = G9Glyph.Info, ShowChevron = true },
            new G9NavCard { Title = "With a value", Icon = G9Glyph.Clock, ValueText = "42" },
            new G9NavCard { Title = "Destructive", Icon = G9Glyph.Delete, IsDestructive = true },
            new G9NavCard { Title = "Coming soon", Icon = G9Glyph.Plus, IsComingSoon = true, ComingSoonText = "Soon" }));

        // The shimmer band is a platform-handler view (Android ImageView / iOS CALayer gradient) precisely
        // so the sweep animates off the UI thread. A static grey block here means the handler is missing.
        stack.Add(ActionsPage.Section("G9Shimmer band (platform handler)", palette,
            new G9ShimmerBandView { HeightRequest = 18 },
            new G9ShimmerBandView { HeightRequest = 18, WidthRequest = 180, HorizontalOptions = LayoutOptions.Start }));

        stack.Add(ActionsPage.Section("G9SwipeView", palette,
            new G9SwipeView
            {
                CardContent = new Label
                {
                    Text = "Swipe me horizontally",
                    Margin = new Thickness(14),
                    TextColor = palette.OnSurface
                }
            }));

        return new ScrollView { Content = stack };
    }

    private static View NewTabView(G9TabStyle style) => new G9TabView
    {
        Style = style,
        HeightRequest = 220,
        Items =
        [
            new G9TabItem
            {
                Text = "First", Icon = G9Glyph.Info,
                TabContent = new Label { Text = "First tab content", TextColor = G9Palette.Current.OnSurface }
            },
            new G9TabItem
            {
                Text = "Second", Icon = G9Glyph.Check, BadgeCount = 3,
                TabContent = new Label { Text = "Second tab content", TextColor = G9Palette.Current.OnSurface }
            },
            new G9TabItem
            {
                Text = "Third", BadgeDot = true,
                TabContent = new Label { Text = "Third tab content", TextColor = G9Palette.Current.OnSurface }
            }
        ]
    };
}
