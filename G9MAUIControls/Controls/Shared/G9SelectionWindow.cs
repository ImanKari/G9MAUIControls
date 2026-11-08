namespace G9MAUIControls.Controls;

/// <summary>
///     Which rows a <see cref="G9SelectionSheet" /> builds for a query (LES-0056): the first <c>limit</c> matches in list order,
///     plus every selected match beyond them — a chosen value must stay on screen to be un-chosen — and how many matches are
///     left out (the «N more» footer). Deliberately free of any MAUI dependency, so the test project can link it.
/// </summary>
internal static class G9SelectionWindow
{
    /// <param name="count">How many items the sheet holds.</param>
    /// <param name="matches">Whether item <c>i</c> matches the query.</param>
    /// <param name="isSelected">Whether item <c>i</c> is selected now.</param>
    /// <param name="limit">How many matching rows the window may hold (selected ones beyond it do not count).</param>
    public static (List<int> Window, int Hidden) Compute(int count, Func<int, bool> matches, Func<int, bool> isSelected, int limit)
    {
        var window = new List<int>();
        var hidden = 0;
        var shown = 0;
        for (var i = 0; i < count; i++)
        {
            if (!matches(i)) continue;
            if (shown < limit)
            {
                window.Add(i);
                shown++;
            }
            else if (isSelected(i))
            {
                window.Add(i);
            }
            else
            {
                hidden++;
            }
        }

        return (window, hidden);
    }

    /// <summary>How many leading rows two windows share — the rows a re-render keeps instead of rebuilding.</summary>
    public static int CommonPrefix(IReadOnlyList<int> previous, IReadOnlyList<int> next)
    {
        var keep = 0;
        while (keep < previous.Count && keep < next.Count && previous[keep] == next[keep]) keep++;
        return keep;
    }
}
