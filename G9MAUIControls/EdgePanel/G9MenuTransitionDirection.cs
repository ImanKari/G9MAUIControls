namespace G9MAUIControls.EdgePanel;

/// <summary>
///     Direction of the slide animation used when swapping the displayed menu list inside an
///     <see cref="G9EdgePanel" />.
/// </summary>
internal enum G9MenuTransitionDirection
{
    /// <summary>No animation — the new list replaces the previous one immediately.</summary>
    None,

    /// <summary>Pushing into a deeper sub-list. The new list slides in following the reading direction.</summary>
    Forward,

    /// <summary>Returning to a parent list. The new list slides in opposite to the reading direction.</summary>
    Back
}
