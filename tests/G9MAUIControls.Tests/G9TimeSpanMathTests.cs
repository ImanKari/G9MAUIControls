using G9MAUIControls.Controls;

namespace G9MAUIControls.Tests;

public sealed class G9TimeSpanMathTests
{
    private const int SweepUpperBound = 4000;

    [Fact]
    public void ComposeOfDecomposeIsTheInputForEveryDayCountInTheSweep()
    {
        for (var total = 0; total <= SweepUpperBound; total++)
        {
            var (years, months, days) = G9TimeSpanMath.Decompose(total);
            Assert.Equal(total, G9TimeSpanMath.Compose(years, months, days));
        }
    }

    [Fact]
    public void MonthsNeverExceedTheDrumAndNoPartIsNegative()
    {
        for (var total = 0; total <= SweepUpperBound; total++)
        {
            var (years, months, days) = G9TimeSpanMath.Decompose(total);
            Assert.InRange(months, 0, G9TimeSpanMath.MaxMonths);
            Assert.True(years >= 0, $"years < 0 for {total}");
            Assert.True(days >= 0, $"days < 0 for {total}");
        }
    }

    [Fact]
    public void ThreeHundredSeventyDaysIsOneYearFiveDays()
    {
        // The defect the helper exists for: this used to read "1 year 10 days".
        Assert.Equal((1, 0, 5), G9TimeSpanMath.Decompose(370));
    }

    [Fact]
    public void ZeroDecomposesToZeroAndComposesToZero()
    {
        Assert.Equal((0, 0, 0), G9TimeSpanMath.Decompose(0));
        Assert.Equal(0, G9TimeSpanMath.Compose(0, 0, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-365)]
    [InlineData(int.MinValue)]
    public void NegativeDayCountsDecomposeAsZero(int totalDays)
    {
        Assert.Equal((0, 0, 0), G9TimeSpanMath.Decompose(totalDays));
    }

    [Theory]
    [InlineData(-1, -1, -1, 0)]
    [InlineData(1, -3, 2, 367)] // a negative part is dropped, not subtracted
    [InlineData(-2, 1, 0, 30)]
    [InlineData(0, 0, int.MinValue, 0)]
    public void NegativePartsComposeAsZero(int years, int months, int days, int expected)
    {
        Assert.Equal(expected, G9TimeSpanMath.Compose(years, months, days));
    }

    [Theory]
    [InlineData(359, 0, 11, 29)] // last value that needs no clamp
    [InlineData(360, 0, 11, 30)]
    [InlineData(361, 0, 11, 31)]
    [InlineData(362, 0, 11, 32)]
    [InlineData(363, 0, 11, 33)]
    [InlineData(364, 0, 11, 34)]
    [InlineData(365, 1, 0, 0)] // and the year rolls over cleanly
    [InlineData(365 + 362, 1, 11, 32)] // the same remainder inside a later year
    public void LastFiveDaysOfAYearStayInDaysInsteadOfBecomingMonthTwelve(
        int totalDays, int years, int months, int days)
    {
        Assert.Equal((years, months, days), G9TimeSpanMath.Decompose(totalDays));
        Assert.Equal(totalDays, G9TimeSpanMath.Compose(years, months, days));
    }

    [Theory]
    [InlineData(1, 0, 0, 1)]
    [InlineData(29, 0, 0, 29)]
    [InlineData(30, 0, 1, 0)]
    [InlineData(31, 0, 1, 1)]
    [InlineData(730, 2, 0, 0)]
    [InlineData(4000, 10, 11, 20)]
    public void OrdinaryValuesSplitOnTheThirtyAndThreeSixtyFiveConvention(
        int totalDays, int years, int months, int days)
    {
        Assert.Equal((years, months, days), G9TimeSpanMath.Decompose(totalDays));
    }

    [Fact]
    public void ComposeSaturatesInsteadOfOverflowing()
    {
        Assert.Equal(int.MaxValue, G9TimeSpanMath.Compose(int.MaxValue, int.MaxValue, int.MaxValue));
    }

    [Fact]
    public void LargestDayCountStillRoundTrips()
    {
        var (years, months, days) = G9TimeSpanMath.Decompose(int.MaxValue);
        Assert.InRange(months, 0, G9TimeSpanMath.MaxMonths);
        Assert.Equal(int.MaxValue, G9TimeSpanMath.Compose(years, months, days));
    }
}
