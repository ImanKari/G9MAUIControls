namespace G9MAUIControls.Controls;

/// <summary>
///     App-wide defaults for every outlined field — everything built on
///     <see cref="G9OutlinedFieldBase" />: <c>G9TextEntry</c>, <c>G9Editor</c>, <c>G9SearchEntry</c>,
///     <c>G9Picker</c>, <c>G9ComboBox</c>, <c>G9DateTimePicker</c>, <c>G9TimeSpanPicker</c> and the
///     barcode entry. Applied once at startup with <see cref="G9OutlinedFieldBase.Configure" />.
/// </summary>
/// <remarks>
///     Same shape as <c>G9BottomSheetSettings</c>: an immutable record with a <see cref="Default" />
///     instance, so a consumer states only what differs
///     (<c>G9OutlinedFieldSettings.Default with { HighlightFilledValue = false }</c>) and every setting
///     added later arrives with its library default instead of breaking the call.
/// </remarks>
public sealed record G9OutlinedFieldSettings
{
    /// <summary>The library's own defaults. Configuring with this is the same as never configuring.</summary>
    public static G9OutlinedFieldSettings Default { get; } = new();

    /// <summary>
    ///     Whether a field that merely HOLDS a value — unfocused, valid, no status colour — is painted
    ///     in the accent colour (<c>G9Palette.Primary</c>) or in the same neutral
    ///     resting colours as an empty field. Default <c>true</c>: the accent, which is the library's
    ///     behaviour since 1.0.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         With <c>false</c>, a filled field at rest draws its outline in the resting outline colour,
    ///         its floated label in the resting CONTENT colour (the grey an empty field's label and
    ///         placeholder use) and its trailing icon in the resting colour. The label still floats and
    ///         turns bold exactly as before; focus, error and status colours, and every stroke
    ///         thickness, are unchanged.
    ///     </para>
    ///     <para>
    ///         Why a design might want <c>false</c>: the accent is the focus colour. On a form that opens
    ///         pre-filled — an edit screen, a picker with a pre-selected value — every field then
    ///         looks focused at once, and the one field that really is focused stops standing out.
    ///     </para>
    ///     <para>
    ///         A single field overrides this through <see cref="G9OutlinedFieldBase.FilledValueHighlight" />.
    ///     </para>
    /// </remarks>
    public bool HighlightFilledValue { get; init; } = true;
}
