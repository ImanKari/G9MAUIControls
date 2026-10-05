namespace G9MAUIControls.TabBar;

/// <summary>
///     The visual style a <see cref="G9TabBar" /> draws itself in. Behaviour — selection, the FAB slot,
///     the sub-menu fan-out, overflow, every event — is identical in both; only the surface, the notch,
///     the FAB and the item states differ. Set it through <see cref="G9TabBar.BarStyle" />.
/// </summary>
public enum G9TabBarStyle
{
    /// <summary>
    ///     The original style: translucent glass bar (18 dp corners) with a lit top edge, a semicircular
    ///     notch under a 72 dp gradient FAB whose centre sits on the bar's top edge, and a primary-tinted
    ///     pill behind the selected item.
    /// </summary>
    Classic = 0,

    /// <summary>
    ///     «Bottom Navigation / Sculpted» (the second redesign): an opaque bar with 24 dp corners, a wide
    ///     S-shouldered cradle instead of a semicircle, a flat 48 dp primary FAB that sits 8 dp INTO the
    ///     bar, no selection pill — the selected item switches to its filled icon
    ///     (<see cref="G9TabBarItem.SelectedIcon" />) and the primary colour instead.
    /// </summary>
    Sculpted = 1
}
