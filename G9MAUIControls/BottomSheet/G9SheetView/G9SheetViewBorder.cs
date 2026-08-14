namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Rounded body of the <see cref="G9SheetView" /> control. Subclasses MAUI's
///     <see cref="Border" /> so we get a real <c>RoundRectangle</c>-clipped surface on every
///     supported platform without any vendor-specific layout primitives. The class is partial
///     so per-platform handler code (Android <c>BorderHandler</c> override + iOS / MacCatalyst
///     / Windows touch-forwarding) can live in matching <c>.{platform}.cs</c> files.
/// </summary>
internal partial class G9SheetViewBorder : Border
{
    private readonly WeakReference<G9SheetView>? _ownerRef;

    /// <summary>Construct a new border for the given <see cref="G9SheetView" /> owner.</summary>
    public G9SheetViewBorder(G9SheetView owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ownerRef = new WeakReference<G9SheetView>(owner);
    }

    /// <summary>
    ///     Forward a single pointer action from the platform handler up to the owning
    ///     <see cref="G9SheetView" />. Coordinates are in dp, relative to the sheet body.
    /// </summary>
    internal void ForwardTouch(G9SheetViewTouchAction action, Point point)
    {
        if (_ownerRef?.TryGetTarget(out var owner) == true)
        {
            owner.OnHandleTouch(action, point);
        }
    }

    /// <summary>Returns the owning <see cref="G9SheetView" /> if still alive.</summary>
    internal G9SheetView? TryGetOwner()
    {
        return _ownerRef is not null && _ownerRef.TryGetTarget(out var owner) ? owner : null;
    }
}
