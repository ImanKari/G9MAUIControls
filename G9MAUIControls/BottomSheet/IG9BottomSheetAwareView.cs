namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Implement on modal views that need direct access to their owner bottom sheet handle.
/// </summary>
public interface IG9BottomSheetAwareView
{
    /// <summary>
    ///     Gets or sets the current owner bottom sheet handle.
    ///     This is injected automatically by <see cref="G9BottomSheetHelper" /> when the view is shown.
    /// </summary>
    IG9BottomSheetHandle G9BottomSheetHandle { get; set; }
}