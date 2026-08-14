namespace G9MAUIControls.EdgePanel;

/// <summary>
///     How the expanded close (×) tab is positioned relative to the panel card's
///     inner corner. Switches between the inset look (default) and a centered-on-edge
///     look that straddles the panel's inner corner.
/// </summary>
public enum G9EdgeCloseButtonPlacement
{
    /// <summary>
    ///     The close circle sits INSIDE the panel near the inner corner, offset by
    ///     <see cref="G9EdgePanelMetrics.ExpandedTabInset"/>. This is the default.
    ///     Reads as "a button placed on the panel's edge near the corner".
    /// </summary>
    Inset = 0,

    /// <summary>
    ///     The close circle is CENTERED on the panel's inner corner border: half of the
    ///     circle sits inside the panel, half outside, with the centre lying exactly on
    ///     the panel's inner edge. Used by full-takeover overlays where the close button
    ///     should read as a halo on the corner instead of a button on the rail.
    /// </summary>
    OnCorner = 1
}
