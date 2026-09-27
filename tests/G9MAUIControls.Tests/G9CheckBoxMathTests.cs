using G9MAUIControls.Controls;

namespace G9MAUIControls.Tests;

public sealed class G9CheckBoxMathTests
{
    private const float Tolerance = 0.0001f;

    [Theory]
    [InlineData(false, false, true, false)] // unchecked -> checked
    [InlineData(true, false, false, false)] // checked -> unchecked
    [InlineData(false, true, true, false)]  // indeterminate (unchecked underneath) -> checked
    [InlineData(true, true, true, false)]   // indeterminate (checked underneath) -> checked
    public void TapResolvesToTheNextState(bool isChecked, bool isIndeterminate, bool expectedChecked, bool expectedIndeterminate)
    {
        var (nextChecked, nextIndeterminate) = G9CheckBoxMath.NextOnTap(isChecked, isIndeterminate);

        Assert.Equal(expectedChecked, nextChecked);
        Assert.Equal(expectedIndeterminate, nextIndeterminate);
    }

    [Theory]
    [InlineData(false, false, 0f)]
    [InlineData(true, false, 1f)]
    [InlineData(false, true, 1f)]
    [InlineData(true, true, 1f)]
    public void CheckedAndIndeterminateBothRestFilled(bool isChecked, bool isIndeterminate, float expected)
    {
        Assert.Equal(expected, G9CheckBoxMath.TargetProgress(isChecked, isIndeterminate));
    }

    [Fact]
    public void FillArrivesBeforeTheMarkFinishes()
    {
        Assert.Equal(0f, G9CheckBoxMath.FillAmount(0f));
        Assert.Equal(1f, G9CheckBoxMath.FillAmount(G9CheckBoxMath.FillPhaseEnd));
        Assert.Equal(1f, G9CheckBoxMath.FillAmount(1f));

        Assert.Equal(0f, G9CheckBoxMath.MarkAmount(0f));
        Assert.Equal(0f, G9CheckBoxMath.MarkAmount(G9CheckBoxMath.MarkPhaseStart));
        Assert.Equal(1f, G9CheckBoxMath.MarkAmount(1f));

        // The two phases overlap: the tick has started drawing while the fill is still arriving.
        var midOverlap = (G9CheckBoxMath.MarkPhaseStart + G9CheckBoxMath.FillPhaseEnd) / 2f;
        Assert.InRange(G9CheckBoxMath.FillAmount(midOverlap), 0.01f, 0.99f);
        Assert.InRange(G9CheckBoxMath.MarkAmount(midOverlap), 0.01f, 0.99f);
    }

    [Theory]
    [InlineData(float.NaN, 0f)]
    [InlineData(-3f, 0f)]
    [InlineData(0.5f, 0.5f)]
    [InlineData(7f, 1f)]
    public void Clamp01NeverLetsAPaintPassSeeGarbage(float input, float expected)
    {
        Assert.Equal(expected, G9CheckBoxMath.Clamp01(input));
    }

    [Fact]
    public void AnEmptyTrimDrawsNothing()
    {
        Assert.True(G9CheckMarkGeometry.Check.Trim(0f).IsEmpty);
        Assert.True(G9CheckMarkGeometry.Check.Trim(float.NaN).IsEmpty);
    }

    [Fact]
    public void AFullTrimDrawsTheWholeTick()
    {
        var mark = G9CheckMarkGeometry.Check;
        var trim = mark.Trim(1f);

        Assert.False(trim.IsEmpty);
        Assert.Equal(mark.Bx, trim.FirstEndX, Tolerance);
        Assert.Equal(mark.By, trim.FirstEndY, Tolerance);
        Assert.True(trim.HasSecond);
        Assert.Equal(mark.Cx, trim.SecondEndX, Tolerance);
        Assert.Equal(mark.Cy, trim.SecondEndY, Tolerance);
    }

    [Fact]
    public void TheTrimReachesTheElbowAtTheFirstArmsShareOfTheLength()
    {
        var mark = G9CheckMarkGeometry.Check;
        var elbow = mark.FirstLength / (mark.FirstLength + mark.SecondLength);

        var atElbow = mark.Trim(elbow);
        Assert.False(atElbow.HasSecond);
        Assert.Equal(mark.Bx, atElbow.FirstEndX, Tolerance);
        Assert.Equal(mark.By, atElbow.FirstEndY, Tolerance);

        var pastElbow = mark.Trim(elbow + 0.05f);
        Assert.True(pastElbow.HasSecond);
    }

    [Fact]
    public void TheTickIsAShortArmThenALongOne()
    {
        var mark = G9CheckMarkGeometry.Check;

        // A tick reads as a tick only when the second arm is clearly the longer one, and goes UP.
        Assert.True(mark.SecondLength > mark.FirstLength * 1.5f);
        Assert.True(mark.By > mark.Ay);
        Assert.True(mark.Cy < mark.By);
    }

    [Fact]
    public void MorphEndsAreTheTickAndAFlatBar()
    {
        Assert.Equal(G9CheckMarkGeometry.Check, G9CheckMarkGeometry.Resolve(0f));

        var bar = G9CheckMarkGeometry.Resolve(1f);
        Assert.Equal(G9CheckMarkGeometry.Dash, bar);
        Assert.Equal(bar.Ay, bar.By, Tolerance);
        Assert.Equal(bar.By, bar.Cy, Tolerance);
    }

    [Fact]
    public void MorphInterpolatesEveryPoint()
    {
        var half = G9CheckMarkGeometry.Resolve(0.5f);
        var check = G9CheckMarkGeometry.Check;
        var dash = G9CheckMarkGeometry.Dash;

        Assert.Equal((check.By + dash.By) / 2f, half.By, Tolerance);
        Assert.Equal((check.Cx + dash.Cx) / 2f, half.Cx, Tolerance);
    }
}
