namespace G9MAUIControls.EdgePanel;

/// <summary>
///     Horizontal alignment of the panel's sticky header label / custom view.
///     Values <see cref="LeftToRight"/> and <see cref="RightToLeft"/> are absolute physical
///     edges and DO NOT flip with the page's <c>FlowDirection</c>; <see cref="Auto"/>
///     follows the panel's <see cref="G9EdgePanel.ContentFlowDirection"/> so the title
///     sits on the leading edge in both English (left) and Persian (right).
/// </summary>
public enum G9EdgeMenuHeaderAlignment
{
    /// <summary>
    ///     Follow <see cref="G9EdgePanel.ContentFlowDirection"/>: leading edge in LTR
    ///     content, trailing edge in RTL content. Default — matches the natural reading
    ///     direction of the panel content.
    /// </summary>
    Auto = 0,

    /// <summary>Pin the title to the absolute physical left edge of the panel.</summary>
    LeftToRight = 1,

    /// <summary>Pin the title to the absolute physical right edge of the panel.</summary>
    RightToLeft = 2,

    /// <summary>
    ///     Center the title horizontally inside the panel. Useful when the title needs to
    ///     read balanced in both English and Persian or when it doubles as a focal element
    ///     above a list. The label still keeps a small inner inset so it cannot overlap
    ///     the close (×) tab.
    /// </summary>
    Center = 3
}
