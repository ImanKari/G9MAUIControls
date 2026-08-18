using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using System.Text.RegularExpressions;

using G9MAUIControls.Icons;

using G9MAUIControls.Localization;

namespace G9MAUIControls.Controls;

/// <summary>
///     Barcode/QR scanner-aware text entry. Inherits all <see cref="G9TextEntry" /> features
///     and adds idle / scanning / accepted / error visual states plus a regex acceptor.
///     <para>
///         <b>Trailing-icon actionability follows the inherited contract.</b> The base
///         <see cref="G9OutlinedFieldBase" /> only plays the press animation + ripple
///         when <see cref="G9OutlinedFieldBase.HasTrailingActionable" /> reports true.
///         A previous version of this control wired a stub <c>TrailingCommand</c> in the
///         constructor regardless of consumer wiring, which caused the icon to ripple
///         on every tap even when no <see cref="ScanRequested" /> handler or external
///         <c>TrailingCommand</c> existed — exactly the false-positive interactivity hint
///         the base contract is designed to prevent. We now report actionability based on
///         the live wiring instead: a <see cref="ScanRequested" /> subscriber, a
///         consumer-supplied <see cref="G9OutlinedFieldBase.TrailingCommand" />, or both.
///     </para>
///     <para>
///         <b>Actionability is also state-gated.</b> The icon is only tappable in
///         <see cref="G9BarcodeTextEntryState.Idle" />. In <c>ScanBusy</c> the trailing
///         slot shows a spinner (the base swaps it via <c>IsTrailingBusy</c>) and the
///         tap is suppressed. In <c>Accepted</c> / <c>Error</c> the trailing icon is a
///         status indicator (✓ / ⓘ) — it accepts no tap, and the field's helper /
///         error text carries any further communication.
///     </para>
///     // TODO (palette step): accepted / scanning / error state colors will move to G9Palette.
/// </summary>
public partial class G9BarcodeTextEntry : G9TextEntry
{
    [AutoBindable(OnChanged = nameof(OnBarcodeChanged))] private string? _acceptedCodeRegex;
    [AutoBindable(OnChanged = nameof(OnBarcodeChanged))] private G9BarcodeScanMode _scanMode;
    [AutoBindable(OnChanged = nameof(OnBarcodeChanged))] private bool _isEditable;
    [AutoBindable(OnChanged = nameof(OnBarcodeChanged))] private string? _scanBusyText;
    [AutoBindable(OnChanged = nameof(OnBarcodeChanged))] private G9BarcodeTextEntryState _scanState;

    private EventHandler? _scanRequested;

    public G9BarcodeTextEntry()
    {
        ScanMode = G9BarcodeScanMode.Single;
        IsEditable = false;
        ScanBusyText = G9Strings.Get(G9StringKey.Scanning);
        ScanState = G9BarcodeTextEntryState.Idle;

        ForceTrailingIconRight = true;
        InputTextDirection = G9TextInputDirection.LeftToRight;
        TrailingIcon = G9Glyphs.ScanCode;
        KeyboardType = G9KeyboardType.Default;

        ApplyBarcodeState();
    }

    /// <summary>
    ///     Fires when the user taps the trailing scanner icon while the field is in
    ///     <see cref="G9BarcodeTextEntryState.Idle" />. Tracked manually (instead of the
    ///     auto-generated <c>event</c> field) so we can ask "does anyone care?" inside
    ///     <see cref="HasTrailingActionable" /> — without that we can't distinguish a
    ///     decorative icon (no consumer) from a wired call-to-action and the press
    ///     animation would always play, hinting at interactivity that doesn't exist.
    /// </summary>
    public event EventHandler ScanRequested
    {
        add => _scanRequested += value;
        remove => _scanRequested -= value;
    }

    public event EventHandler<string>? Accepted;

    public void StartScan() => ScanState = G9BarcodeTextEntryState.ScanBusy;

    public void StopScan()
    {
        if (ScanState == G9BarcodeTextEntryState.ScanBusy)
        {
            ScanState = G9BarcodeTextEntryState.Idle;
        }
    }

    public bool AcceptScannedCode(string code)
    {
        if (!IsCodeAccepted(code))
        {
            ScanState = G9BarcodeTextEntryState.Error;
            return false;
        }

        Text = ScanMode == G9BarcodeScanMode.Multiple && !string.IsNullOrWhiteSpace(Text)
            ? $"{Text}, {code}"
            : code;

        ScanState = G9BarcodeTextEntryState.Accepted;
        Accepted?.Invoke(this, code);
        return true;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The trailing icon is tappable only when (a) we're in
    ///     <see cref="G9BarcodeTextEntryState.Idle" /> AND (b) something is wired —
    ///     either a <see cref="ScanRequested" /> subscriber or a consumer-supplied
    ///     <see cref="G9OutlinedFieldBase.TrailingCommand" />. Anything else is treated
    ///     as a non-interactive icon (status indicator or busy spinner) so the base
    ///     suppresses the press animation per the inherited "no-callback = no animation"
    ///     contract.
    /// </remarks>
    protected override bool HasTrailingActionable()
    {
        if (ScanState != G9BarcodeTextEntryState.Idle) return false;
        return _scanRequested is not null || TrailingCommand is not null;
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Invoked by the base after it has played the press animation (when
    ///     <see cref="HasTrailingActionable" /> reported true). We raise
    ///     <see cref="ScanRequested" /> first so subscribers can flip the state to
    ///     <c>ScanBusy</c>, then defer to the base which routes any wired
    ///     <see cref="G9OutlinedFieldBase.TrailingCommand" />.
    /// </remarks>
    protected override void OnTrailingTap()
    {
        _scanRequested?.Invoke(this, EventArgs.Empty);
        base.OnTrailingTap();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The scan glyph is a call-to-action. In the resting <see cref="G9BarcodeTextEntryState.Idle" />
    ///     state the field outline is the neutral grey, which made the scan icon read as disabled.
    ///     When the icon is actionable we tint it <c>Primary</c> so it clearly invites a tap. The
    ///     busy spinner and the accepted (✓) / error (ⓘ) status glyphs keep the state colour.
    /// </remarks>
    protected override Color ResolveTrailingIconColor(Color stateColor)
    {
        if (ScanState == G9BarcodeTextEntryState.Idle && HasTrailingActionable())
        {
            return G9Palette.Current.Primary;
        }

        return stateColor;
    }

    private bool IsCodeAccepted(string code)
    {
        if (string.IsNullOrWhiteSpace(AcceptedCodeRegex)) return true;
        return Regex.IsMatch(code, AcceptedCodeRegex, RegexOptions.CultureInvariant);
    }

    private void OnBarcodeChanged() => ApplyBarcodeState();

    private void ApplyBarcodeState()
    {
        var palette = G9Palette.Current;

        IsReadOnly = !IsEditable || ScanState == G9BarcodeTextEntryState.ScanBusy;
        IsTrailingBusy = ScanState == G9BarcodeTextEntryState.ScanBusy;
        UseStatusColor = ScanState is G9BarcodeTextEntryState.ScanBusy or G9BarcodeTextEntryState.Accepted;
        StatusColor = ScanState == G9BarcodeTextEntryState.Accepted ? palette.Success : palette.Primary;
        HasError = ScanState == G9BarcodeTextEntryState.Error;

        if (ScanState == G9BarcodeTextEntryState.ScanBusy)
        {
            Placeholder = ScanBusyText;
        }

        TrailingIcon = ScanState switch
        {
            G9BarcodeTextEntryState.Accepted => G9Glyphs.Success,
            G9BarcodeTextEntryState.Error => G9Glyphs.Info,
            _ => G9Glyphs.ScanCode
        };
    }
}
