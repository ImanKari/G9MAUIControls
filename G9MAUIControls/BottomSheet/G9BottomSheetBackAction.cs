namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Determines what a bottom sheet should do after a back-navigation request.
/// </summary>
public enum G9BottomSheetBackAction
{
    /// <summary>Close the bottom sheet.</summary>
    Close,

    /// <summary>Keep the bottom sheet open; ignore the back request.</summary>
    DoNothing
}
