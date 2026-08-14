# G9BarcodeTextEntry

`G9BarcodeTextEntry` is a scanner-aware `G9TextEntry`. It inherits everything from
`G9TextEntry` and adds idle / scanning / accepted / error visual states plus a regex
acceptor for validating scanned codes.

## When to use

- This is the base for scanner-aware text input. The app-level scanner control
  `BarcodeEntry` (in `Common/Components/BarcodeScanner`) inherits this and wires in the
  camera `BarcodeScanService` — use `BarcodeEntry` on pages, and put generic scan-field
  behaviour here in `G9BarcodeTextEntry`.
- Plain text with no scanner -> `G9TextEntry` (see `G9TextEntry.md`).

## States

| State | Visual |
|---|---|
| `Idle` (default) | Outlined input with a scanner-icon trailing button. Tapping the icon raises `ScanRequested`. The scan glyph is tinted `Primary` while actionable (a `ScanRequested` subscriber or `TrailingCommand` is wired) so it reads as a call-to-action rather than a disabled grey icon — via the `G9OutlinedFieldBase.ResolveTrailingIconColor` override. |
| `ScanBusy` | Border tinted `Primary` (focus emphasis). Trailing icon swaps to a spinner. Field becomes read-only. Placeholder swaps to `ScanBusyText`. |
| `Accepted` | Border tinted `Success`. Trailing icon swaps to `CheckCircle`. |
| `Error` | Border tinted `Error`. Trailing icon swaps to `Info`. `ErrorText` shown below. |

Visual emphasis (the thicker stroke) is inherited from the base outline drawable —
`UseStatusColor` and `HasError` both trigger the heavier outline thickness; there is no
separate outer ring. The soft inner focus halo is reserved for normal focused fields, so
barcode status states keep their solid status outline.

## Bindable Properties

### Inherited from `G9TextEntry`

See `../G9TextEntry/G9TextEntry.md` for the full inherited list — `Text`,
`Label`, `Placeholder`, `HelperText`, `ErrorText`, `HasError`, `FieldHeight`, leading /
trailing icon properties, etc.

### Specific to `G9BarcodeTextEntry`

| Property | Type | Default | Description |
|---|---|---|---|
| `AcceptedCodeRegex` | `string?` | `null` | Regex pattern. Scanned codes must match before being accepted. Empty / null = accept any. |
| `ScanMode` | `G9BarcodeScanMode` | `Single` | `Single` replaces `Text`; `Multiple` appends to a comma-separated list. |
| `IsEditable` | `bool` | `false` | Allow manual keyboard typing in addition to scanning. |
| `ScanBusyText` | `string?` | `"Scanning..."` | Placeholder text shown while `ScanState == ScanBusy`. |
| `ScanState` | `G9BarcodeTextEntryState` | `Idle` | Current visual state. Updated by your scanner integration code. |

## Events

- `Accepted` — fires when `AcceptScannedCode(string code)` succeeds. Provides the code.
- `ScanRequested` — fires when the user taps the scanner icon (in `Idle` state).

## Methods

- `StartScan()` — sets `ScanState = ScanBusy`. Wire this up before invoking your camera.
- `StopScan()` — returns to `Idle` if currently `ScanBusy`.
- `AcceptScannedCode(string code)` — validates the code against `AcceptedCodeRegex`,
  appends or replaces according to `ScanMode`, sets `ScanState = Accepted` on success
  or `ScanState = Error` on failure. Returns `true` if accepted.

## Usage

### Code-behind integration with a camera scanner

```csharp
BarcodeField.ScanRequested += async (_, _) =>
{
    BarcodeField.StartScan();
    try
    {
        var code = await _scanner.ScanAsync(); // your scanner service
        BarcodeField.AcceptScannedCode(code);
    }
    catch
    {
        BarcodeField.ScanState = G9BarcodeTextEntryState.Error;
    }
};
```

### XAML — single-code with regex

```xml
<newControls:G9BarcodeTextEntry
    Label="Scan code"
    Placeholder="Tap the scanner"
    AcceptedCodeRegex="^[A-Z]{2}-[0-9]{4}$"
    ScanMode="Single" />
```

### Multi-code editable list

```xml
<newControls:G9BarcodeTextEntry
    Label="Scanned items"
    ScanMode="Multiple"
    IsEditable="True"
    HelperText="Add codes manually or scan repeatedly" />
```

### Showing different visual states

```csharp
// Idle (default).
BarcodeField.ScanState = G9BarcodeTextEntryState.Idle;

// Trigger emphasis ring while waiting for the camera.
BarcodeField.ScanState = G9BarcodeTextEntryState.ScanBusy;

// Manual error message.
BarcodeField.ScanState = G9BarcodeTextEntryState.Error;
BarcodeField.ErrorText = "Code does not match expected format";
```

## Behaviour Notes

### Trailing-icon actionability

`G9BarcodeTextEntry` inherits `G9OutlinedFieldBase`'s "no-callback = no animation"
contract: the press-ripple under the trailing icon plays only when the tap is wired
to do something. Specifically, the icon is treated as actionable when **all** of the
following are true:

- `ScanState == Idle` — in `ScanBusy` the trailing slot shows a spinner (set by the
  base via `IsTrailingBusy`); in `Accepted` / `Error` the slot is a status indicator
  (✓ / ⓘ) and accepts no tap.
- At least one of: a `ScanRequested` subscriber, or a consumer-supplied
  `TrailingCommand`.

If neither is wired and you want the icon to be visibly inert (no ripple, no
highlight on tap), simply don't subscribe and don't set `TrailingCommand`. This is
how the showcase page renders the static `Accepted` / `Error` / `Multiple`
demonstration cards — they're visual snapshots, no action attached, so tapping them
does nothing and shows nothing.

### RTL / icon placement

- The trailing icon position is forced to the visual right edge
  (`ForceTrailingIconRight = true`) even in RTL pages — barcode scanning conventions
  never flip the scanner icon. The leading-icon slot still follows logical placement
  (swaps physically across the box on culture flip), so a leading icon you add via
  `LeadingMaterialIcon` etc. mirrors with the page direction.
- The inner text direction is forced to LTR (`InputTextDirection = LeftToRight`)
  because barcodes are intrinsically left-to-right symbol sequences. The label and
  outline still follow the page direction.

### State swap

- The placeholder is swapped automatically to `ScanBusyText` while
  `ScanState == ScanBusy` so the user sees a "Scanning..." hint while the camera
  initializes.
- `Accepted` events do NOT fire from manual keyboard input — they only fire from
  `AcceptScannedCode`. If you need a hook for manual entry, listen to `Text` changes
  via the binding instead.
- Setting `ScanState = Accepted` colors the border green but does NOT clear after a
  delay — it's the consumer's responsibility to drop back to `Idle` if they want a
  short success flash.
- The accepted state plays well with `ScanMode == Multiple`: each successful scan
  keeps the green border until the user taps the scanner again or types into the
  field.
