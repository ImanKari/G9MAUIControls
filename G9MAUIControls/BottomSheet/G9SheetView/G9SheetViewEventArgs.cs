namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Carries old/new <see cref="G9SheetViewState" /> plus the duration the sheet is using
///     to animate the transition. The helper layer (modal overlay fade, state-aware top
///     padding) consumes <see cref="AnimationDurationMs" /> so secondary animations stay in
///     lockstep with the sheet motion across Android, iOS, Mac Catalyst, and Windows.
/// </summary>
public class G9SheetViewStateChangedEventArgs : EventArgs
{
    /// <summary>Previous state.</summary>
    public G9SheetViewState OldState { get; internal set; }

    /// <summary>New state.</summary>
    public G9SheetViewState NewState { get; internal set; }

    /// <summary>
    ///     The animation duration (ms) the sheet is using for the transition that produced
    ///     this event. Subscribers driving follow-along animations should use this value so
    ///     timing matches across platforms.
    /// </summary>
    public double AnimationDurationMs { get; internal set; }
}

/// <summary>
///     Fires every frame the sheet's visible height changes — drag tick, open/close
///     animation tick, and snap finished. Consumed by the helper to drive the modal-overlay
///     alpha and the page <c>ContentHost</c> backdrop card recede.
/// </summary>
public sealed class G9SheetViewPositionChangedEventArgs : EventArgs
{
    /// <summary>Initialises a new instance with current visible / full heights.</summary>
    public G9SheetViewPositionChangedEventArgs(double visibleHeight, double fullHeight)
    {
        VisibleHeight = visibleHeight;
        FullHeight = fullHeight;
    }

    /// <summary>Current visible height of the sheet body, in dp.</summary>
    public double VisibleHeight { get; }

    /// <summary>Full host height in dp (used to compute a 0..1 ratio).</summary>
    public double FullHeight { get; }
}

/// <summary>
///     Reason for an <see cref="G9SheetView.BackRequested" /> event so subscribers can
///     mirror it onto the helper's <c>G9BottomSheetBackRequestSource</c> contract.
/// </summary>
public enum G9SheetViewBackRequestReason
{
    /// <summary>The user dragged the body past the threshold and released.</summary>
    DragToClose,

    /// <summary>The user tapped the modal overlay (only fires when <c>CollapseOnOverlayTap == false</c>).</summary>
    OverlayTap
}

/// <summary>Payload for <see cref="G9SheetView.BackRequested" />.</summary>
public sealed class G9SheetViewBackRequestedEventArgs : EventArgs
{
    /// <summary>Construct a new event args with the given source reason.</summary>
    public G9SheetViewBackRequestedEventArgs(G9SheetViewBackRequestReason reason)
    {
        Reason = reason;
    }

    /// <summary>The reason the close was requested.</summary>
    public G9SheetViewBackRequestReason Reason { get; }
}
