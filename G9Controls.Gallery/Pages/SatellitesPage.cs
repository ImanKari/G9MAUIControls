using G9MAUIControls.Controls;
using G9MAUIControls.Hosting;
using G9MAUIControls.Icons;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using G9MAUIControls.Toast;

namespace G9Controls.Gallery.Pages;

/// <summary>
///     The two optional UI satellites, on one page because what needs verifying about them is the same
///     thing: that a package which is <b>not</b> the core still behaves like part of the suite.
///     <list type="bullet">
///         <item>
///             <b><c>G9MAUIControls.Barcode</c></b> — the field must be indistinguishable from a core
///             <c>G9TextEntry</c> in every state. It derives from the same outlined-field base, so a visible
///             difference in label float, focus halo, or RTL slot order means the base's protected surface is
///             not sufficient for an out-of-assembly subclass, which is an API-boundary defect, not a
///             styling nit.
///         </item>
///         <item>
///             <b><c>G9MAUIControls.IntroCarousel</c></b> — slides carry resource <i>keys</i>, resolved
///             through the core's <c>G9Strings.Resolve</c> seam against the consumer's own catalogue. The
///             page registers a tiny in-memory catalogue below to prove that path end to end; a slide that
///             renders its raw key means the seam is not wired.
///         </item>
///     </list>
/// </summary>
public sealed class SatellitesPage : G9PageBase
{
    public SatellitesPage()
    {
        Title = "Satellites";
        Content = Build();
    }

    private static View Build()
    {
        var palette = G9Palette.Current;
        var stack = new VerticalStackLayout { Spacing = 18, Padding = new Thickness(16) };

        stack.Add(new Label
        {
            Text = "The barcode field must look EXACTLY like a core G9TextEntry beside it — it subclasses the "
                 + "same base from another assembly. The carousel must show resolved titles, not raw keys.",
            FontSize = 12,
            TextColor = palette.OnSurfaceVariant
        });

        // Side by side, deliberately: a difference between these two is the thing being looked for.
        stack.Add(ActionsPage.Section("Core field vs. Barcode field (same base, different assembly)", palette,
            new G9TextEntry { Label = "G9TextEntry (core)", LeadingIcon = G9Glyph.Search, Text = "12345" },
            new G9BarcodeTextEntry { Label = "G9BarcodeTextEntry (satellite)", Text = "12345" }));

        var busy = new G9BarcodeTextEntry { Label = "Scan busy (trailing slot becomes a spinner)" };
        busy.StartScan();

        var rejecting = new G9BarcodeTextEntry
        {
            Label = "Rejects anything but 4 digits",
            AcceptedCodeRegex = @"^\d{4}$",
            ScanMode = G9BarcodeScanMode.Multiple
        };

        stack.Add(ActionsPage.Section("G9BarcodeTextEntry states", palette,
            busy,
            rejecting,
            new G9Button
            {
                Text = "Feed \"99\" (must be rejected)",
                Variant = G9ButtonVariant.Outline,
                Command = new Command(() => Report(rejecting.AcceptScannedCode("99")))
            },
            new G9Button
            {
                Text = "Feed \"9999\" (must be accepted)",
                Variant = G9ButtonVariant.Outline,
                Command = new Command(() => Report(rejecting.AcceptScannedCode("9999")))
            },
            new G9BarcodeTextEntry { Label = "Disabled", Text = "0000", IsEnabled = false }));

        stack.Add(ActionsPage.Section("G9IntroCarousel (image slides; keys resolved by the consumer)",
            palette, BuildCarousel()));

        return new ScrollView { Content = stack };
    }

    private static void Report(bool accepted) => _ = G9ToastHelper.ShowToastAsync(
        accepted ? "Accepted" : "Rejected by AcceptedCodeRegex",
        accepted ? G9ToastType.Success : G9ToastType.Warning);

    private static View BuildCarousel()
    {
        // The consumer owns localization: the carousel only ever asks G9Strings for a key. A real app points
        // this at its RESX/catalogue; the gallery uses a dictionary so the seam itself is what gets tested.
        G9Strings.UseProvider((key, _) => Catalogue.TryGetValue(key, out var value) ? value : null);

        var carousel = new G9IntroCarousel
        {
            HeightRequest = 380,
            // No video slide here: MediaElement needs a real asset, and an absent file would show as a
            // black frame that reads like a carousel bug rather than a missing file.
            Slides =
            [
                new G9IntroSlideItem
                {
                    TitleResourceKey = "intro.one.title",
                    SubtitleResourceKey = "intro.one.subtitle"
                },
                new G9IntroSlideItem
                {
                    TitleResourceKey = "intro.two.title",
                    SubtitleResourceKey = "intro.two.subtitle"
                },
                new G9IntroSlideItem
                {
                    // Deliberately unregistered, to prove the fallback: an unknown key must render empty,
                    // never the key itself. A key on screen in a shipped app is a visible defect.
                    TitleResourceKey = "intro.three.missing",
                    SubtitleResourceKey = "intro.three.missing"
                }
            ]
        };

        carousel.BeginPresentation();
        return carousel;
    }

    private static readonly Dictionary<string, string> Catalogue = new(StringComparer.Ordinal)
    {
        ["intro.one.title"] = "Resolved from the consumer's catalogue",
        ["intro.one.subtitle"] = "The carousel never owns your strings.",
        ["intro.two.title"] = "Second slide",
        ["intro.two.subtitle"] = "Swipe: in RTL, paging must reverse."
    };
}
