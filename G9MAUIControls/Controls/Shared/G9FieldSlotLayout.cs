namespace G9MAUIControls.Controls;

/// <summary>
///     Single source of truth for the leading-icon / inner-content / trailing-icon slot
///     geometry shared by every outlined-field control (text entry, editor, picker, combo
///     box, date / time picker, barcode entry).
///     <para>
///         <b>Layout shape</b> — every outlined field uses a 3-column <see cref="Grid" />
///         with its <see cref="VisualElement.FlowDirection" /> locked to
///         <see cref="FlowDirection.LeftToRight" />:
///     </para>
///     <list type="bullet">
///         <item><description>Column 0 — physical-LEFT icon slot.</description></item>
///         <item><description>Column 1 — inner content (Entry / Editor / value Label).</description></item>
///         <item><description>Column 2 — physical-RIGHT icon slot.</description></item>
///     </list>
///     <para>
///         Each icon column is auto-sized to <c>InputIconSize + 2 × icon-margin</c>. The
///         icon-margin is symmetric — it appears identically on the wall side and the
///         inner-text side of the icon glyph, by attaching it as the icon Label's
///         <see cref="View.Margin" />. So setting margin = 0 makes the icon flush against
///         both the box wall and the inner text; margin = 8 makes both gaps 8 px.
///     </para>
///     <para>
///         The <b>logical</b> leading icon is closest to the start of reading direction;
///         the <b>logical</b> trailing icon is closest to the end. In LTR these map to
///         column 0 (left) and column 2 (right). In RTL we swap them so the logical
///         leading icon sits physical-right (where reading starts in RTL) and trailing
///         sits physical-left.
///     </para>
///     <para>
///         <see cref="ForceTrailingIconRight" /> — when true (passwords, barcode entry),
///         the TRAILING icon is pinned to physical-right regardless of flow direction.
///     </para>
/// </summary>
internal readonly record struct G9FieldSlotLayout(
    bool HasLeadingIcon,
    bool HasTrailingIcon,
    bool IsRtl,
    bool ForceTrailingIconRight,
    bool ForceLeadingIconLeft)
{
    /// <summary>
    ///     Padding applied to the inner-content host. With icons present the inner-text
    ///     side gets <c>0</c> because the icon's own symmetric margin already provides
    ///     the inner-text-side gap. Without an icon the side gets the full
    ///     <see cref="G9Metrics.InputHorizontalPadding" /> so the inner text
    ///     aligns with the corner padding of the outline.
    ///     <para>
    ///         The padding is applied in PHYSICAL coordinates (left / right) because the
    ///         parent Grid's FlowDirection is locked to LTR. In RTL the leading icon
    ///         occupies the physical-right column, so we swap which side gets the icon
    ///         gap vs the wall padding. <see cref="ForceTrailingIconRight"/> pins the
    ///         trailing icon to physical-right regardless of direction (passwords, barcodes).
    ///     </para>
    /// </summary>
    public Thickness ResolveInnerPadding(Thickness perControlPadding)
    {
        // Determine which PHYSICAL side has an icon (gap = 0) vs needs wall padding (14).
        // The Grid columns are always physical: col 0 = left, col 2 = right.
        // Leading column = ResolvePhysicalLeadingColumn(); Trailing = ResolvePhysicalTrailingColumn().
        var hasIconOnPhysicalLeft =
            (HasLeadingIcon && ResolvePhysicalLeadingColumn() == 0) ||
            (HasTrailingIcon && ResolvePhysicalTrailingColumn() == 0);

        var hasIconOnPhysicalRight =
            (HasLeadingIcon && ResolvePhysicalLeadingColumn() == 2) ||
            (HasTrailingIcon && ResolvePhysicalTrailingColumn() == 2);

        var physicalLeftPad = hasIconOnPhysicalLeft ? 0.0 : G9Metrics.InputHorizontalPadding;
        var physicalRightPad = hasIconOnPhysicalRight ? 0.0 : G9Metrics.InputHorizontalPadding;

        return new Thickness(
            physicalLeftPad + perControlPadding.Left,
            perControlPadding.Top,
            physicalRightPad + perControlPadding.Right,
            perControlPadding.Bottom);
    }

    /// <summary>
    ///     Physical Grid.Column index for the LEADING icon host. Column 0 is physical-left
    ///     (regardless of FlowDirection because the box is locked to LTR). In LTR the
    ///     leading icon goes to column 0; in RTL it goes to column 2 so it appears at the
    ///     physical-right edge where reading starts.
    ///     <para>
    ///         <see cref="ForceLeadingIconLeft" /> — when true (email, password), the
    ///         leading icon is pinned to physical-left (column 0) regardless of flow
    ///         direction. Used for LTR-content fields where the leading icon must stay
    ///         on the left even in RTL pages.
    ///     </para>
    /// </summary>
    public int ResolvePhysicalLeadingColumn()
    {
        if (ForceLeadingIconLeft) return 0;
        return IsRtl ? 2 : 0;
    }

    /// <summary>
    ///     Physical Grid.Column index for the TRAILING icon host. Mirrors the leading
    ///     column unless <see cref="ForceTrailingIconRight" /> is true — then the trailing
    ///     icon is pinned to physical-right (column 2) regardless of flow direction. Used
    ///     by password / barcode entries where the trailing affordance should always sit
    ///     on the physical-right edge.
    /// </summary>
    public int ResolvePhysicalTrailingColumn()
    {
        if (ForceTrailingIconRight) return 2;
        return IsRtl ? 0 : 2;
    }

    /// <summary>
    ///     X offset added via <c>TranslationX</c> to the floating label in its rest state.
    ///     When a leading icon is present the rest label slides past the icon column so it
    ///     visually sits over the inner text. When floated this offset returns to 0 and
    ///     the label slides back to the corner padding where the outline notch starts.
    ///     The sign is flipped in RTL so the label slides toward the physical-right edge
    ///     (where the leading icon now lives).
    ///     <para>
    ///         Additionally, when <see cref="ForceTrailingIconRight" /> is active in RTL,
    ///         the trailing icon is pinned to physical-right — the same side where reading
    ///         starts. Without an offset, the rest-state label sits directly under the
    ///         trailing icon. We treat this like a "logical leading icon" for label
    ///         positioning purposes: the label must slide past the trailing icon column.
    ///     </para>
    /// </summary>
    public double ResolveLabelRestTranslationX()
    {
        if (HasLeadingIcon)
        {
            // When ForceLeadingIconLeft is active in RTL, the leading icon is pinned to
            // physical-left — the OPPOSITE side from reading start. The label should NOT
            // offset past it because the label sits at the reading-start side (right in RTL).
            if (IsRtl && ForceLeadingIconLeft) return 0;
            return IsRtl ? -G9Metrics.InputLabelLeadingIconOffset : G9Metrics.InputLabelLeadingIconOffset;
        }

        // In RTL with ForceTrailingIconRight, the trailing icon sits on the reading-start
        // side (physical-right = logical-start in RTL). The label must move past it.
        if (IsRtl && ForceTrailingIconRight && HasTrailingIcon)
        {
            var trailingOffset = G9Metrics.TrailingIconSlotWidth - G9Metrics.InputHorizontalPadding;
            return -trailingOffset;
        }

        return 0;
    }

    /// <summary>
    ///     X offset added via <c>TranslationX</c> to the floating label in its floated state.
    ///     A small leading-direction shift away from the corner curvature so the floated
    ///     label has visible breathing room from the rounded corner of the outline.
    /// </summary>
    public double ResolveLabelFloatedTranslationX()
    {
        return IsRtl ? -G9Metrics.FloatingLabelFloatedExtraX : G9Metrics.FloatingLabelFloatedExtraX;
    }
}
