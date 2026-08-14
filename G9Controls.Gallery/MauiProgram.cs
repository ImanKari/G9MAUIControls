using CommunityToolkit.Maui;
using G9MAUIControls.BottomSheet;
using G9MAUIControls.Hosting;
using G9MAUIControls.Icons;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Microsoft.Extensions.Logging;

namespace G9Controls.Gallery;

/// <summary>
///     Startup for the verification app. Also the reference for what a consumer must wire — every hook the
///     suite exposes is configured here, in the order a real app would do it.
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            // Registers the bottom-sheet platform handler. Without it a sheet has no gesture handling on
            // any platform — the one piece of wiring that is NOT optional.
            .UseG9SheetView()
            // Required by G9MAUIControls.IntroCarousel: its video slides use
            // CommunityToolkit.Maui.MediaElement, whose analyzer (MCTME001) fails the BUILD of any app
            // that references it without this call. A consumer that only wants image slides still has to
            // make it — which is itself an argument for the carousel living outside the core.
            // isAndroidForegroundServiceEnabled: false — the carousel's video is foreground UI only. A
            // foreground service exists so playback can CONTINUE with the app backgrounded, which for an
            // onboarding carousel would be wrong behaviour and an extra Android permission.
            .UseMauiCommunityToolkitMediaElement(isAndroidForegroundServiceEnabled: false)
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // ── Culture ────────────────────────────────────────────────────────────────────────────────
        // The gallery flips direction from a switch rather than from a real localization stack, so the
        // accessors read a static the toggle writes. A real app points these at its own language state.
        G9Culture.Configure(
            currentCulture: () => GalleryState.Culture,
            isRtl: () => GalleryState.IsRtl);
        G9Culture.LtrFontFamily = "OpenSansRegular";
        G9Culture.RtlFontFamily = "OpenSansRegular";

        // ── Icons ──────────────────────────────────────────────────────────────────────────────────
        // Nothing registered on purpose: the gallery verifies the BUILT-IN vector glyphs, which need no
        // font and no configuration. That is the claim being tested — a fresh consumer gets a complete
        // suite with zero icon setup.

        // ── Splash continuity ──────────────────────────────────────────────────────────────────────
        G9PageLoadingOverlay.BackgroundColorOverride = Color.FromArgb("#2E7D32");
        G9PageLoadingOverlay.SpinnerColorOverride = Colors.White;

        var app = builder.Build();

        // Applies the persisted theme (or the system one) and pushes ~110 tokens into G9Palette.
        return app;
    }
}

/// <summary>
///     The two knobs every gallery page reads. Static because the gallery has no view models — it is a
///     verification harness, and a DI graph would be ceremony that obscures what is being tested.
/// </summary>
public static class GalleryState
{
    public static System.Globalization.CultureInfo Culture { get; set; } =
        System.Globalization.CultureInfo.GetCultureInfo("en-US");

    public static bool IsRtl { get; set; }

    /// <summary>
    ///     Flips direction and tells every loaded control to repaint. Both halves matter: without the
    ///     notify, controls already on screen keep their old direction — which is the single most common
    ///     way a culture hook is wired incorrectly.
    /// </summary>
    public static void SetRtl(bool isRtl)
    {
        IsRtl = isRtl;
        Culture = isRtl
            ? System.Globalization.CultureInfo.GetCultureInfo("fa-IR")
            : System.Globalization.CultureInfo.GetCultureInfo("en-US");
        G9Culture.NotifyChanged();
    }

    /// <summary>Switches theme. G9Theme persists the choice and re-pushes the palette.</summary>
    public static void SetTheme(AppTheme theme) => G9Theme.ChangeTheme(theme);
}
