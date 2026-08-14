namespace G9MAUIControls.EdgePanel;

/// <summary>
///     How <see cref="G9EdgePanel" /> lays out text, icons, and menu rows inside the panel.
///     Independent of <see cref="G9EdgeSide" /> (which edge the panel attaches to).
/// </summary>
public enum G9EdgePanelContentDirection
{
    /// <summary>Follow app culture RTL/LTR (same resource as page <c>CurrentFlowDirection</c>).</summary>
    MatchApplication = 0,

    /// <summary>Force left-to-right content (e.g. mixed data in an RTL app).</summary>
    LeftToRight = 1,

    /// <summary>Force right-to-left content.</summary>
    RightToLeft = 2
}
