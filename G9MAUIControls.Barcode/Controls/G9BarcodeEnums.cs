namespace G9MAUIControls.Controls;

// Namespace note: G9MAUIControls.Controls, matching G9BarcodeTextEntry, so a consumer needs exactly one
// using for the control and its vocabulary. A satellite sharing the core's namespace is deliberate here —
// the alternative (G9MAUIControls.Barcode.Controls) would make every XAML file that scans a barcode carry
// a second xmlns for two enums. See ADR-0007 on namespace policy across the ecosystem.

/// <summary>
///     Whether the scanner closes after one code or keeps reading.
/// </summary>
public enum G9BarcodeScanMode
{
    /// <summary>Read one code, then stop. The default, and the right choice for a single-field form.</summary>
    Single,

    /// <summary>
    ///     Keep the camera open and keep accepting codes — batch entry. The consumer is responsible for
    ///     de-duplicating: the same physical label held in frame produces repeated reads.
    /// </summary>
    Multiple
}

/// <summary>
///     The entry's scan state, tracked explicitly rather than inferred.
///     <para>
///         It is a separate property from the field's own visual state because "the camera is open" and "the
///         field has an error" are independent: a scan can fail validation while the camera keeps running,
///         and a field can show an error with no camera involved at all.
///     </para>
/// </summary>
public enum G9BarcodeTextEntryState
{
    /// <summary>No scan in progress. The trailing affordance is the scan trigger.</summary>
    Idle,

    /// <summary>A scan is running. The trailing affordance becomes a busy indicator.</summary>
    ScanBusy,

    /// <summary>A code was accepted. Transient — the entry returns to <see cref="Idle" />.</summary>
    Accepted,

    /// <summary>A code was read but rejected, e.g. by <c>AcceptedCodeRegex</c>.</summary>
    Error
}
