namespace G9MAUIControls.TabBar;

/// <summary>
///     Raised when the centre FAB is tapped, BEFORE the control acts on it.
/// </summary>
/// <remarks>
///     <para>
///         The FAB's built-in behaviour is to fan out <see cref="G9TabBar.SubMenuItems" />. That is the
///         right default, but a host sometimes needs the tap to mean something else entirely — most
///         commonly opening a sheet of its own instead of the radial menu. Before this event the only
///         way to attempt that was to watch <see cref="G9TabBar.IsFabOpen" /> and close it again, which
///         flashes the fan-out open and shut.
///     </para>
///     <para>
///         Set <see cref="Handled" /> to suppress the built-in behaviour completely: the control will
///         not toggle <see cref="G9TabBar.IsFabOpen" /> and will not change the selected tab. Leave it
///         <c>false</c> and the FAB behaves exactly as it always has, so the event is additive and no
///         existing consumer changes.
///     </para>
/// </remarks>
public sealed class G9TabBarFabTappedEventArgs : EventArgs
{
    /// <summary>
    ///     Set to <c>true</c> to suppress the control's own FAB handling for this tap.
    /// </summary>
    public bool Handled { get; set; }
}
