using G9MAUIControls.Controls;
using G9MAUIControls.Theming;

namespace G9MAUIControls.Hosting;

/// <summary>
///     The full-screen cover that hides a page while it is still assembling itself, and the topmost
///     layer of <c>G9PageTemplate.xaml</c>.
///     <para>
///         <b>Why it exists at all.</b> It is inflated <i>with the control template</i> — before any
///         page-specific code runs, before the first binding resolves, before content renders — so
///         there is never a frame in which the user sees a half-built page. <see cref="G9PageBase" />
///         then animates it out and removes it from the visual tree once the page reports itself
///         ready, or after a safety timeout so a page that never signals readiness cannot leave the
///         app looking hung.
///     </para>
///     <para>
///         <b>Colour.</b> Defaults to <see cref="BackgroundColorOverride" /> when set, otherwise the
///         theme's <see cref="G9Palette.Primary" />. Set the override at startup to your splash
///         colour — matching the native splash screen is what makes the hand-off from splash to
///         first page look like one continuous screen rather than two:
///     </para>
///     <example>
///         <code>
///         G9PageLoadingOverlay.BackgroundColorOverride = Color.FromArgb("#2E7D32");
///         G9PageLoadingOverlay.SpinnerColorOverride    = Colors.White;
///         </code>
///     </example>
/// </summary>
public sealed class G9PageLoadingOverlay : ContentView
{
    /// <summary>
    ///     Overrides the cover colour for every page. Leave null to use the theme's
    ///     <see cref="G9Palette.Primary" />. Set it to the native splash screen's colour so the
    ///     splash-to-first-page hand-off reads as one screen.
    /// </summary>
    public static Color? BackgroundColorOverride { get; set; }

    /// <summary>
    ///     Overrides the spinner colour. Leave null to use the theme's
    ///     <see cref="G9Palette.OnPrimary" />, which is the correct contrast partner for the default
    ///     background.
    /// </summary>
    public static Color? SpinnerColorOverride { get; set; }

    /// <summary>Builds the overlay. Called by the control template, not by consumers.</summary>
    public G9PageLoadingOverlay()
    {
        var palette = G9Palette.Current;

        BackgroundColor = BackgroundColorOverride ?? palette.Primary;
        HorizontalOptions = LayoutOptions.Fill;
        VerticalOptions = LayoutOptions.Fill;

        // Input-OPAQUE on purpose: while the page is assembling, a tap must not reach a control
        // that is not finished wiring itself up.
        InputTransparent = false;

        Content = new G9ActivityIndicator
        {
            IsRunning = true,
            Color = SpinnerColorOverride ?? palette.OnPrimary,
            HeightRequest = 48,
            WidthRequest = 48,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
    }
}
