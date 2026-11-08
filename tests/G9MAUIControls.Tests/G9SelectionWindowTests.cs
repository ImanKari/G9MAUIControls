using G9MAUIControls.Controls;

namespace G9MAUIControls.Tests;

/// <summary>
///     The selection sheet's row window (LES-0056): a 3,416-item list built a row per item and was an ANR. These pin what the
///     window holds, so it can never grow back to one row per item.
/// </summary>
public sealed class G9SelectionWindowTests
{
    private static readonly Func<int, bool> All = _ => true;
    private static readonly Func<int, bool> None = _ => false;

    [Fact]
    public void ALongListBuildsOnlyTheFirstPageAndCountsTheRest()
    {
        var (window, hidden) = G9SelectionWindow.Compute(3416, All, None, 60);

        Assert.Equal(Enumerable.Range(0, 60), window);
        Assert.Equal(3356, hidden);
    }

    [Fact]
    public void AShortListIsBuiltWholeWithNothingHidden()
    {
        var (window, hidden) = G9SelectionWindow.Compute(12, All, None, 60);

        Assert.Equal(12, window.Count);
        Assert.Equal(0, hidden);
    }

    [Fact]
    public void ASelectedItemBeyondThePageStaysListedInItsPlaceAndIsNotCountedAsHidden()
    {
        var (window, hidden) = G9SelectionWindow.Compute(3416, All, i => i == 2000, 60);

        Assert.Equal(61, window.Count);
        Assert.Equal(2000, window[^1]);
        Assert.Equal(3355, hidden);
    }

    [Fact]
    public void TheWindowHoldsOnlyMatchesOfTheQuery()
    {
        // Every tenth item matches; the limit applies to MATCHES, not to positions in the list.
        var (window, hidden) = G9SelectionWindow.Compute(3416, i => i % 10 == 0, None, 60);

        Assert.All(window, i => Assert.Equal(0, i % 10));
        Assert.Equal(60, window.Count);
        Assert.Equal(342 - 60, hidden);
    }

    [Fact]
    public void AQueryWithNoMatchGivesAnEmptyWindow()
    {
        var (window, hidden) = G9SelectionWindow.Compute(3416, None, None, 60);

        Assert.Empty(window);
        Assert.Equal(0, hidden);
    }

    [Fact]
    public void AScrollPageKeepsEveryRowOfTheWindowBefore()
    {
        var first = G9SelectionWindow.Compute(3416, All, None, 60).Window;
        var next = G9SelectionWindow.Compute(3416, All, None, 120).Window;

        Assert.Equal(60, G9SelectionWindow.CommonPrefix(first, next));
    }

    [Fact]
    public void ANarrowingQueryKeepsOnlyTheSharedLeadingRows()
    {
        Assert.Equal(2, G9SelectionWindow.CommonPrefix([0, 1, 2, 3], [0, 1, 5]));
        Assert.Equal(0, G9SelectionWindow.CommonPrefix([0, 1], [3, 4]));
        Assert.Equal(0, G9SelectionWindow.CommonPrefix([], [3, 4]));
    }
}
