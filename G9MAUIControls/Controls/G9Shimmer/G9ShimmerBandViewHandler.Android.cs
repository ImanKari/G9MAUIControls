#if ANDROID
using Android.Graphics.Drawables;
using Android.Widget;
using Microsoft.Maui.Handlers;

namespace G9MAUIControls.Controls;

/// <summary>
///     Android handler for <see cref="G9ShimmerBandView" />: an <see cref="ImageView" /> showing the
///     <c>g9_shimmer_band</c> <see cref="AnimatedVectorDrawable" /> (a translating gradient
///     band; see <c>Platforms/Android/Resources/drawable/g9_shimmer_band.xml</c>). AVDs run on
///     the RenderThread on API 25+, so the sweep keeps animating while the UI thread builds the
///     heavy sheet body the shimmer is masking.
/// </summary>
public sealed class G9ShimmerBandViewHandler : ViewHandler<G9ShimmerBandView, ImageView>
{
    public static readonly IPropertyMapper<G9ShimmerBandView, G9ShimmerBandViewHandler> PropertyMapper =
        new PropertyMapper<G9ShimmerBandView, G9ShimmerBandViewHandler>(ViewMapper);

    public G9ShimmerBandViewHandler()
        : base(PropertyMapper)
    {
    }

    protected override ImageView CreatePlatformView()
    {
        var imageView = new ImageView(Context);
        imageView.SetScaleType(ImageView.ScaleType.FitXy);
        // Keep the existing light-theme sweep byte-for-byte unchanged. Dark mode uses a dedicated
        // vector whose gradient is translucent BLACK, not white: a white highlight (even dimmed or
        // narrowed) is lighter than the near-black background between the rows, so it always read as a
        // bright slab sliding across. The black "lowlight" is invisible over that background and only
        // darkens the raised skeleton bars as it passes — a soft shadow sweep that belongs to the
        // dark theme.
        imageView.SetImageResource(IsDarkTheme()
            ? Resource.Drawable.g9_shimmer_band_dark
            : Resource.Drawable.g9_shimmer_band);
        return imageView;
    }

    // Mirrors the app-wide dark-theme detection (G9Colors / G9TabBarColors …): the theme is
    // driven by the MAUI UserAppTheme the Profile picker sets, falling back to the system
    // RequestedTheme only while unspecified. Fully qualified because 'Application' would otherwise
    // collide with Android.App.Application in this platform file.
    private static bool IsDarkTheme()
    {
        var app = Microsoft.Maui.Controls.Application.Current;
        return app?.UserAppTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark
               || app is { UserAppTheme: Microsoft.Maui.ApplicationModel.AppTheme.Unspecified, RequestedTheme: Microsoft.Maui.ApplicationModel.AppTheme.Dark };
    }

    protected override void ConnectHandler(ImageView platformView)
    {
        base.ConnectHandler(platformView);

        if (platformView.Drawable is AnimatedVectorDrawable { IsRunning: false } drawable)
        {
            drawable.Start();
        }
    }

    protected override void DisconnectHandler(ImageView platformView)
    {
        if (platformView.Drawable is AnimatedVectorDrawable { IsRunning: true } drawable)
        {
            drawable.Stop();
        }

        base.DisconnectHandler(platformView);
    }
}
#endif
