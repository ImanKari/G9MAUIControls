namespace G9MAUIControls.Controls;

/// <summary>How a multi-select <see cref="G9ComboBox" /> draws its chosen items in the field.</summary>
public enum G9ComboBoxChipStyle
{
    /// <summary>Primary-filled chips, the text in OnPrimary (the default).</summary>
    Filled,

    /// <summary>
    ///     Light pills - SurfaceContainerLow, an OutlineVariant hairline, the text in TextPrimary - each with its own «×» that removes
    ///     that item. A people field's chips in AgriPad's Figma («مسئول(های) انجام»).
    /// </summary>
    Tonal
}
