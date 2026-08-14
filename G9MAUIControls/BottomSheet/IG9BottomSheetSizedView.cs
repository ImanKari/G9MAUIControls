namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Implement on bottom-sheet content that needs the resolved host height before Android native measurement.
/// </summary>
public interface IG9BottomSheetSizedView
{
    /// <summary>
    ///     Applies the available bottom-sheet content height in device-independent units.
    /// </summary>
    void ApplyG9BottomSheetHeight(double height);
}
