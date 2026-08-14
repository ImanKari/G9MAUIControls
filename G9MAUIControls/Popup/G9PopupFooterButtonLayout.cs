namespace G9MAUIControls.Popup;

/// <summary>
///     How <c>G9PopupHelper</c> arranges the footer action buttons.
/// </summary>
public enum G9PopupFooterButtonLayout
{
    /// <summary>
    ///     Default. Buttons share one horizontal row as equal-width columns (good for 1–2 short
    ///     labels; up to 3 supported). Long labels get cramped — use <see cref="Stacked" /> then.
    /// </summary>
    Row,

    /// <summary>
    ///     Buttons are stacked one per full-width row (top to bottom, in caller order). Use for 3+
    ///     buttons or long labels (e.g. the device-clock gate) where the row layout squeezes text.
    /// </summary>
    Stacked
}
