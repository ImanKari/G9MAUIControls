#if IOS || MACCATALYST
using CoreAnimation;
using CoreGraphics;
using Foundation;
using Microsoft.Maui.Handlers;
using UIKit;

namespace G9MAUIControls.Controls;

/// <summary>
///     iOS / Mac Catalyst handler for <see cref="G9ShimmerBandView" />: a <see cref="UIView" />
///     hosting a <see cref="CAGradientLayer" /> swept by a repeating <see cref="CABasicAnimation" />.
///     Core Animation runs committed animations in the render server, so the sweep keeps moving
///     while the app's main thread is blocked building the heavy sheet body.
/// </summary>
public sealed class G9ShimmerBandViewHandler : ViewHandler<G9ShimmerBandView, ShimmerBandPlatformView>
{
    public static readonly IPropertyMapper<G9ShimmerBandView, G9ShimmerBandViewHandler> PropertyMapper =
        new PropertyMapper<G9ShimmerBandView, G9ShimmerBandViewHandler>(ViewMapper);

    public G9ShimmerBandViewHandler()
        : base(PropertyMapper)
    {
    }

    protected override ShimmerBandPlatformView CreatePlatformView()
    {
        return new ShimmerBandPlatformView();
    }
}

/// <summary>Native view: a narrow translucent-white gradient band translated across the bounds.</summary>
public sealed class ShimmerBandPlatformView : UIView
{
    private const string SweepAnimationKey = "G9ShimmerSweep";
    private readonly CAGradientLayer _gradientLayer;

    public ShimmerBandPlatformView()
    {
        UserInteractionEnabled = false;
        ClipsToBounds = true;
        BackgroundColor = UIColor.Clear;

        // Keep light mode unchanged. In dark mode the sweep is a translucent BLACK "lowlight", not a
        // white highlight: white (even dimmed or narrowed) is lighter than the near-black background
        // between the rows, so it always read as a bright slab sliding across. Black is invisible over
        // that background and only darkens the raised skeleton bars as it passes, matching the dark
        // theme. Same broad, soft stops as light — only the colour flips.
        var isDarkTheme = IsDarkTheme();
        var sweepColor = isDarkTheme ? UIColor.Black : UIColor.White;
        // Dark-mode strength knob (0 = invisible, 1 = solid black). Lower = subtler sweep.
        var peakAlpha = isDarkTheme ? 0.20f : 0.38f;

        _gradientLayer = new CAGradientLayer
        {
            Colors =
            [
                sweepColor.ColorWithAlpha(0f).CGColor,
                sweepColor.ColorWithAlpha(peakAlpha).CGColor,
                sweepColor.ColorWithAlpha(0f).CGColor
            ],
            Locations = [NSNumber.FromDouble(0.35), NSNumber.FromDouble(0.5), NSNumber.FromDouble(0.65)],
            StartPoint = new CGPoint(0, 0.5),
            EndPoint = new CGPoint(1, 0.5)
        };

        Layer.AddSublayer(_gradientLayer);
    }

    public override void LayoutSubviews()
    {
        base.LayoutSubviews();

        _gradientLayer.Frame = Bounds;
        RestartSweep();
    }

    private void RestartSweep()
    {
        _gradientLayer.RemoveAnimation(SweepAnimationKey);

        if (Bounds.Width <= 0)
        {
            return;
        }

        var sweep = CABasicAnimation.FromKeyPath("transform.translation.x");
        sweep.From = NSNumber.FromDouble(-Bounds.Width);
        sweep.To = NSNumber.FromDouble(Bounds.Width);
        sweep.Duration = 1.1;
        sweep.RepeatCount = float.PositiveInfinity;
        sweep.RemovedOnCompletion = false;
        _gradientLayer.AddAnimation(sweep, SweepAnimationKey);
    }

    // Mirrors the app-wide dark-theme detection (G9Colors / G9TabBarColors …): driven by the
    // MAUI UserAppTheme the Profile picker sets, falling back to the system RequestedTheme only while
    // unspecified.
    private static bool IsDarkTheme()
    {
        var app = Microsoft.Maui.Controls.Application.Current;
        return app?.UserAppTheme == Microsoft.Maui.ApplicationModel.AppTheme.Dark
               || app is { UserAppTheme: Microsoft.Maui.ApplicationModel.AppTheme.Unspecified, RequestedTheme: Microsoft.Maui.ApplicationModel.AppTheme.Dark };
    }
}
#endif
