namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Shape of the loading placeholder a deferred bottom sheet shows while its body builds.
///     <see cref="None" /> keeps the classic centered spinner; the other values render a
///     <c>G9Shimmer</c> skeleton sized to the sheet's placeholder height (see
///     <c>G9BottomSheetOptions.LoadingSkeleton</c> / <c>LoadingSkeletonRowCount</c>).
/// </summary>
public enum G9BottomSheetLoadingSkeleton
{
    /// <summary>Centered activity spinner (default).</summary>
    None,

    /// <summary>Selection/menu-style rows: a leading circle + a text bar per row.</summary>
    ListRows,

    /// <summary>Form-style rows: full-width outlined-field-height bars.</summary>
    FormFields
}
