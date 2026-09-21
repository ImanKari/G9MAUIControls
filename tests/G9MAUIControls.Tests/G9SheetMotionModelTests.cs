using G9MAUIControls.BottomSheet;

namespace G9MAUIControls.Tests;

/// <summary>
///     The platform-native sheet motion models. The Material numbers are asserted against
///     androidx <c>ViewDragHelper</c>'s own arithmetic, so a "tidy-up" of the formula fails here.
/// </summary>
public sealed class G9SheetMotionModelTests
{
    private const double HostWidth = 448;
    private const double HostHeight = 1000;

    private static G9SheetMotionRequest Open(double distance, double velocityY = 0)
    {
        return new G9SheetMotionRequest(HostHeight, HostHeight - distance, HostWidth, HostHeight, velocityY);
    }

    private static G9SheetMotionRequest Close(double distance, double velocityY = 0)
    {
        return new G9SheetMotionRequest(HostHeight - distance, HostHeight, HostWidth, HostHeight, velocityY);
    }

    // ---- Material --------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.5, 256)] // a nudge: the base settle duration
    [InlineData(200, 307)] // (0.2 + 1) × 256 — the picker that used to "open" in 40 ms
    [InlineData(500, 384)]
    [InlineData(1000, 512)] // the full range
    public void MaterialProgrammaticMotionIsTimedByFractionOfRangeNotByDistance(double distance, int expectedMs)
    {
        Assert.Equal(expectedMs, G9SheetMotionModel.ResolveMaterial(Open(distance)).DurationMs);
        Assert.Equal(expectedMs, G9SheetMotionModel.ResolveMaterial(Close(distance)).DurationMs);
    }

    [Fact]
    public void MaterialShortSheetNoLongerOpensInAFewFrames()
    {
        // The regression this model exists for: 199 ms × 200/1000 = 40 ms.
        Assert.True(G9SheetMotionModel.ResolveMaterial(Open(200)).DurationMs >= 250);
    }

    [Fact]
    public void MaterialReleaseIsTimedFromTheWidthDerivedDistanceAndTheVelocity()
    {
        // ViewDragHelper.computeAxisDuration with |delta| ≥ width:
        //   distance = w/2 + w/2 × sin(0.5 × 0.3π/2) = 224 × 1.23345 = 276.29
        //   duration = 4 × round(1000 × 276.29 / 2000) = 4 × 138 = 552
        var spec = G9SheetMotionModel.ResolveMaterial(Close(600, velocityY: 2000));
        Assert.Equal(552, spec.DurationMs);

        // …and with a tiny delta: distance = 224 × (1 + sin(−0.5 × 0.3π/2)) = 171.71 → 4 × 86 = 344
        Assert.Equal(344, G9SheetMotionModel.ResolveMaterial(Close(0.5, velocityY: 2000)).DurationMs);
    }

    [Fact]
    public void MaterialFasterReleaseSettlesSoonerAndVelocityIsClamped()
    {
        var slow = G9SheetMotionModel.ResolveMaterial(Close(600, velocityY: 1000)).DurationMs;
        var fast = G9SheetMotionModel.ResolveMaterial(Close(600, velocityY: 4000)).DurationMs;
        Assert.True(fast < slow);

        var atCap = G9SheetMotionModel.ResolveMaterial(Close(600, velocityY: 8000)).DurationMs;
        var beyondCap = G9SheetMotionModel.ResolveMaterial(Close(600, velocityY: 50000)).DurationMs;
        Assert.Equal(atCap, beyondCap);
    }

    [Fact]
    public void MaterialNeverExceedsTheMaximumSettleDuration()
    {
        Assert.Equal(600, G9SheetMotionModel.ResolveMaterial(Close(600, velocityY: 60)).DurationMs);
    }

    [Fact]
    public void MaterialReleaseAgainstTheDirectionOfTravelIsIgnored()
    {
        // Flicked DOWN, but the sheet snaps back UP: settle as a programmatic motion.
        var snapBack = G9SheetMotionModel.ResolveMaterial(Open(200, velocityY: 3000));
        Assert.Equal(G9SheetMotionModel.ResolveMaterial(Open(200)).DurationMs, snapBack.DurationMs);
    }

    [Fact]
    public void MaterialReleaseBelowTheMinimumFlingVelocityIsNoRelease()
    {
        Assert.Equal(
            G9SheetMotionModel.ResolveMaterial(Close(300)).DurationMs,
            G9SheetMotionModel.ResolveMaterial(Close(300, velocityY: 49)).DurationMs);
    }

    [Fact]
    public void MaterialCurveIsTheQuinticEaseOut()
    {
        var curve = G9SheetMotionModel.ResolveMaterial(Open(500)).Curve;
        Assert.Equal(0d, curve(0), 9);
        Assert.Equal(1d, curve(1), 9);
        Assert.Equal(1 - Math.Pow(0.5, 5), curve(0.5), 9);
        Assert.True(curve(0.37) > 0.90, "90 % of the distance in the first 37 % of the time");
    }

    // ---- Cupertino -------------------------------------------------------------------------------

    [Theory]
    [InlineData(200)]
    [InlineData(600)]
    [InlineData(1000)]
    public void CupertinoCurveRunsFromZeroToOneWithoutOvershootOrReversal(double distance)
    {
        var curve = G9SheetMotionModel.ResolveCupertino(Open(distance)).Curve;

        Assert.Equal(0d, curve(0), 6);
        Assert.Equal(1d, curve(1), 6);

        var previous = 0d;
        for (var i = 1; i <= 200; i++)
        {
            var value = curve(i / 200d);
            Assert.InRange(value, previous - 1e-9, 1d);
            previous = value;
        }
    }

    [Fact]
    public void CupertinoSettlesInAboutHalfASecondWhateverTheDistance()
    {
        // A spring's settle time barely depends on how far it travels — the property that makes a
        // short sheet and a tall one feel like the same object.
        var shortSheet = G9SheetMotionModel.ResolveCupertino(Open(200)).DurationMs;
        var tallSheet = G9SheetMotionModel.ResolveCupertino(Open(1000)).DurationMs;

        Assert.InRange(shortSheet, 350, 600);
        Assert.InRange(tallSheet, 350, 600);
        Assert.True(tallSheet - shortSheet < 120);
    }

    [Fact]
    public void CupertinoCarriesTheReleaseVelocityIntoTheMotion()
    {
        var rest = G9SheetMotionModel.ResolveCupertino(Close(600));
        var thrown = G9SheetMotionModel.ResolveCupertino(Close(600, velocityY: 3000));

        // Same instant in real time (50 ms in): the thrown sheet is further along.
        var atRest = rest.Curve(50d / rest.DurationMs);
        var whenThrown = thrown.Curve(50d / thrown.DurationMs);
        Assert.True(whenThrown > atRest);
        Assert.True(thrown.DurationMs <= rest.DurationMs);
    }

    [Fact]
    public void CupertinoNeverOvershootsHoweverHardTheFlick()
    {
        var curve = G9SheetMotionModel.ResolveCupertino(Open(150, velocityY: -20000)).Curve;
        for (var i = 0; i <= 100; i++)
        {
            Assert.InRange(curve(i / 100d), 0d, 1d);
        }
    }

    [Fact]
    public void CupertinoReleaseAwayFromTheTargetIsTreatedAsRest()
    {
        var away = G9SheetMotionModel.ResolveCupertino(Open(400, velocityY: 2500));
        var rest = G9SheetMotionModel.ResolveCupertino(Open(400));
        Assert.Equal(rest.DurationMs, away.DurationMs);
    }
}
