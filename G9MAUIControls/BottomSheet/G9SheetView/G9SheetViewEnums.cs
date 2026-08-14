namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Display state of the <see cref="G9SheetView" /> control. Mirrors the original
///     Syncfusion vendored enum so the helper layer's mapping logic is unchanged.
/// </summary>
public enum G9SheetViewState
{
    /// <summary>The sheet covers the entire screen.</summary>
    FullExpanded,

    /// <summary>The sheet covers ~half the screen (driven by <see cref="G9SheetView.HalfExpandedRatio" />).</summary>
    HalfExpanded,

    /// <summary>The sheet shows only its <see cref="G9SheetView.CollapsedHeight" /> peek strip.</summary>
    Collapsed,

    /// <summary>The sheet is fully off-screen.</summary>
    Hidden
}

/// <summary>
///     Allowed states for the <see cref="G9SheetView" /> control. The state machine inside
///     the control clamps any incoming <see cref="G9SheetViewState" /> change to fit within
///     this constraint, mirroring the vendored Syncfusion behavior.
/// </summary>
public enum G9SheetViewAllowedState
{
    /// <summary>Only <see cref="G9SheetViewState.FullExpanded" /> (and Hidden) are allowed.</summary>
    FullExpanded,

    /// <summary>Only <see cref="G9SheetViewState.HalfExpanded" /> (and Hidden) are allowed.</summary>
    HalfExpanded,

    /// <summary>All four states are allowed.</summary>
    All
}

/// <summary>
///     Content width sizing mode for the <see cref="G9SheetView" /> control. <c>Full</c>
///     stretches the body to the host width; <c>Custom</c> centers the body and uses
///     <see cref="G9SheetView.G9BottomSheetContentWidth" />.
/// </summary>
public enum G9SheetViewContentWidthMode
{
    /// <summary>Fill the entire host width.</summary>
    Full,

    /// <summary>Use <see cref="G9SheetView.G9BottomSheetContentWidth" /> centered in the host.</summary>
    Custom
}

/// <summary>
///     Pointer action delivered to <see cref="G9SheetView.OnHandleTouch" /> from the
///     per-platform handlers. Replaces the original Syncfusion
///     <c>Syncfusion.Maui.Toolkit.Internals.PointerActions</c> dependency so the control can
///     ship without referencing the vendor's internal types.
/// </summary>
public enum G9SheetViewTouchAction
{
    Pressed,
    Moved,
    Released,
    Cancelled,
    Exited
}
