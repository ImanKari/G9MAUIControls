namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Implemented by bottom-sheet content that can report its own desired height for the
///     fit-to-content sizing mode. This is the count-aware / structure-aware escape hatch from
///     the generic "measure the view tree" path, which cannot produce a meaningful height when
///     the content contains a virtualized list / greedy <c>*</c>-row / <c>ScrollView</c> (those
///     report 0 or infinity when measured unconstrained).
///     <para>
///         The provider returns the content's <b>natural</b> desired height (e.g. for a list:
///         header + itemCount × rowHeight + footer). The helper clamps that to
///         <c>min .. MaxFitToContentHeightRatio × screenHeight</c> and gives the sheet body a
///         bounded height, so:
///         <list type="bullet">
///             <item>short content fits its content exactly (no scroll), and</item>
///             <item>tall content stops at the cap and scrolls inside its now-bounded viewport.</item>
///         </list>
///     </para>
///     <para>
///         Raise <see cref="G9BottomSheetContentHeightChanged" /> whenever the natural height
///         changes after the sheet is open (a tab switch, an async data load, an item
///         insert/remove). The helper subscribes and resizes the sheet — debounced for async
///         bursts so the sheet never jitters; see <c>G9BottomSheetHelper</c>.
///     </para>
/// </summary>
public interface IG9BottomSheetContentHeightProvider
{
    /// <summary>
    ///     Returns the content's natural desired height in device-independent units for the given
    ///     available width. The caller passes the resolved fit-to-content cap as
    ///     <paramref name="maxHeight" /> so the provider can short-circuit expensive measurement
    ///     once it knows it already exceeds the cap; the caller still clamps the result.
    /// </summary>
    double GetDesiredG9BottomSheetContentHeight(double availableWidth, double maxHeight);

    /// <summary>
    ///     Raised when <see cref="GetDesiredG9BottomSheetContentHeight" /> would now return a
    ///     materially different value (tab switch, async load, item count change). The helper
    ///     re-queries and resizes the sheet (debounced).
    /// </summary>
    event EventHandler? G9BottomSheetContentHeightChanged;
}
