namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Indicates the source of a back-navigation request in a bottom sheet.
/// </summary>
public enum G9BottomSheetBackRequestSource
{
    /// <summary>The toolbar back/close button was tapped.</summary>
    ToolbarButton,

    /// <summary>The device hardware or system back button was pressed.</summary>
    HardwareButton,

    /// <summary>The dim overlay behind the sheet was tapped.</summary>
    OverlayTap
}
