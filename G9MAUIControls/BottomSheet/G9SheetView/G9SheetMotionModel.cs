namespace G9MAUIControls.BottomSheet;

// This file is deliberately free of any MAUI dependency: it is file-linked into
// tests/G9MAUIControls.Tests and compiled there as plain net10.0. A curve is therefore a
// Func<double, double> here; the sheet view wraps it in an Easing at the point of use.

/// <summary>How a sheet's open / close / settle motion is timed and shaped.</summary>
public enum G9SheetMotionStyle
{
    /// <summary>
    ///     Move the way the platform's own bottom sheet moves: Material's settle on Android (and on
    ///     Windows, which has no sheet of its own), UIKit's sheet spring on iOS and Mac Catalyst.
    ///     Duration is RESOLVED PER MOTION from the distance travelled and the speed the finger
    ///     released at; the app-wide open / close durations are not used.
    /// </summary>
    PlatformNative = 0,

    /// <summary>
    ///     The 1.0 behaviour: a <c>CubicOut</c> tween lasting the configured open / close duration,
    ///     optionally scaled by the fraction of the screen travelled.
    /// </summary>
    Timed = 1
}

/// <summary>One motion the sheet is about to run, as the motion model sees it (dp and dp/s).</summary>
/// <param name="From">Current <c>TranslationY</c> of the body.</param>
/// <param name="To">Target <c>TranslationY</c>.</param>
/// <param name="HostWidth">Width of the sheet's host.</param>
/// <param name="HostHeight">Height of the sheet's host — the full range the body can travel.</param>
/// <param name="VelocityY">Release speed, positive = downward. <c>0</c> for programmatic motion.</param>
internal readonly record struct G9SheetMotionRequest(
    double From,
    double To,
    double HostWidth,
    double HostHeight,
    double VelocityY)
{
    public double Distance => Math.Abs(To - From);

    /// <summary>True when the body is rising (opening / expanding).</summary>
    public bool IsRising => To < From;
}

/// <summary>The resolved shape of one motion: how long, and progress (0 → 1) over normalised time (0 → 1).</summary>
internal readonly record struct G9SheetMotionSpec(int DurationMs, Func<double, double> Curve);

/// <summary>
///     The platform-native motion models. Pure functions of the request — no state, no platform
///     calls — so they are unit-tested and identical on every target that selects them.
/// </summary>
internal static class G9SheetMotionModel
{
    // ------------------------------------------------------------------------------------------
    // Material (Android)
    // ------------------------------------------------------------------------------------------
    //
    // A Material bottom sheet is moved by androidx ViewDragHelper: BottomSheetBehavior.startSettling
    // calls settleCapturedViewAt (a release) or smoothSlideViewTo (a programmatic state change).
    // Every number below is ViewDragHelper's own — BASE_SETTLE_DURATION, MAX_SETTLE_DURATION,
    // sInterpolator, computeAxisDuration, distanceInfluenceForSnapDuration — read from
    // androidx-main, not estimated. Its feel is two things:
    //
    //   • the CURVE — always a quintic ease-out, (t−1)^5 + 1: very fast off the mark, then a long
    //     soft landing. 90 % of the distance is covered in the first 37 % of the time.
    //   • the DURATION — not proportional to distance.
    //       programmatic:  (distance/range + 1) × 256 ms   → 256 ms (a nudge) … 512 ms (full range)
    //       released:      4 × round(1000 × d′ / |v|) ms,  d′ derived from the parent WIDTH (sic —
    //                      ViewDragHelper uses the width even for vertical motion). For a quintic
    //                      the initial speed is 5 × d / T, so a duration of 4 × d / v leaves the
    //                      hand at 1.25 × the hand's speed: no visible change of pace on release.
    //     both capped at 600 ms.
    //
    // The proportional duration this replaces (199 ms × distance / screen) moved every sheet at a
    // constant 5000 dp/s: a 200 dp picker "opened" in 40 ms — two and a half frames.

    private const int MaterialBaseSettleMs = 256;
    private const int MaterialMaxSettleMs = 600;

    /// <summary><c>ViewConfiguration</c>'s minimum fling velocity: a slower release is "no velocity".</summary>
    private const double MaterialMinVelocity = 50;

    /// <summary><c>ViewConfiguration</c>'s maximum fling velocity.</summary>
    private const double MaterialMaxVelocity = 8000;

    /// <summary>ViewDragHelper's <c>sInterpolator</c>.</summary>
    public static readonly Func<double, double> MaterialSettle = t =>
    {
        t -= 1d;
        return (t * t * t * t * t) + 1d;
    };

    public static G9SheetMotionSpec ResolveMaterial(G9SheetMotionRequest request)
    {
        var delta = request.Distance;
        var speed = Math.Min(Math.Abs(request.VelocityY), MaterialMaxVelocity);

        // A release only shapes the motion when it points the way the sheet is going. A sheet
        // flicked down that snaps back UP (short of the close threshold) settles as if unthrown —
        // the one place this departs from ViewDragHelper, which would hurry the snap-back.
        var isThrown = speed >= MaterialMinVelocity &&
                       (request.VelocityY < 0) == request.IsRising;

        int duration;
        if (isThrown && request.HostWidth > 0)
        {
            var halfWidth = request.HostWidth / 2d;
            var distanceRatio = Math.Min(1d, delta / request.HostWidth);
            var distance = halfWidth + (halfWidth * DistanceInfluenceForSnapDuration(distanceRatio));
            duration = 4 * (int)Math.Round(1000d * Math.Abs(distance / speed));
        }
        else
        {
            var range = request.HostHeight > 0 ? request.HostHeight : Math.Max(delta, 1d);
            duration = (int)((Math.Min(1d, delta / range) + 1d) * MaterialBaseSettleMs);
        }

        return new G9SheetMotionSpec(Math.Min(duration, MaterialMaxSettleMs), MaterialSettle);
    }

    private static double DistanceInfluenceForSnapDuration(double f)
    {
        f -= 0.5d;
        f *= 0.3d * Math.PI / 2d;
        return Math.Sin(f);
    }

    // ------------------------------------------------------------------------------------------
    // UIKit (iOS / Mac Catalyst)
    // ------------------------------------------------------------------------------------------
    //
    // Apple publishes no figures for UISheetPresentationController. What IS documented is UIKit's
    // default transition spring — UISpringTimingParameters.init(): mass 3, stiffness 1000, damping
    // 500, 0.5 s — the one behind the system's own modal and keyboard transitions. Core Animation
    // solves an over-damped spring as CRITICALLY damped, which makes the effective motion ζ = 1,
    // ω₀ = √(1000/3) = 18.26 rad/s. That the sheet uses exactly this spring is an inference, not a
    // published fact; that it is a no-bounce spring carrying the release velocity is Apple's own
    // guidance (WWDC 2018 "Designing Fluid Interfaces": start from 100 % damping).
    //
    // A spring has no duration of its own, and it carries the finger's release velocity into the
    // motion — which is why an iOS sheet never changes speed at the moment it is let go. For
    // remaining distance x (1 → 0) and initial speed v0 towards the target, both as fractions of
    // the distance:
    //
    //         x(t) = (1 + (ω − v0)·t) · e^(−ω·t)
    //
    // It is evaluated in closed form and handed over as a curve across the time the spring takes to
    // come within half a dp of rest, so everything that asks "how long is this motion" — the dim
    // fade, the close cleanup — still gets a number.

    private const double CupertinoOmega = 18.2574; // √(1000 / 3)
    private const int CupertinoMaxSettleMs = 700;
    private const double CupertinoRestDistance = 0.5;

    public static G9SheetMotionSpec ResolveCupertino(G9SheetMotionRequest request)
    {
        var distance = Math.Max(request.Distance, 1d);
        const double omega = CupertinoOmega;

        // Speed towards the target, as a fraction of the distance per second. Capped at ω: beyond
        // it the closed form overshoots, and this body is exactly as tall as its detent — an
        // overshoot would lift its bottom edge off the screen edge and show the page under it.
        var towards = request.IsRising ? -request.VelocityY : request.VelocityY;
        var v0 = Math.Clamp(towards / distance, 0d, omega);

        double Remaining(double seconds)
        {
            return (1d + ((omega - v0) * seconds)) * Math.Exp(-omega * seconds);
        }

        // Time to come within CupertinoRestDistance of rest. x(t) is monotonic for v0 ≤ ω, so a
        // coarse scan is exact enough, and it runs once per motion.
        var restFraction = Math.Min(0.5d, CupertinoRestDistance / distance);
        var durationMs = CupertinoMaxSettleMs;
        for (var ms = 16; ms < CupertinoMaxSettleMs; ms += 8)
        {
            if (Remaining(ms / 1000d) <= restFraction)
            {
                durationMs = ms;
                break;
            }
        }

        var seconds = durationMs / 1000d;
        var covered = 1d - Remaining(seconds);

        return new G9SheetMotionSpec(
            durationMs,
            t => Math.Clamp((1d - Remaining(t * seconds)) / covered, 0d, 1d));
    }

    /// <summary>The model for the platform this code is running on.</summary>
    public static G9SheetMotionSpec ResolvePlatformNative(G9SheetMotionRequest request)
    {
#if IOS || MACCATALYST
        return ResolveCupertino(request);
#else
        return ResolveMaterial(request);
#endif
    }
}
