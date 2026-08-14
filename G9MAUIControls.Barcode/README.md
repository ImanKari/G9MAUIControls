# G9MAUIControls.Barcode

Barcode entry field with scan state and format validation, for
[`G9MAUIControls`](https://www.nuget.org/packages/G9MAUIControls).

```
dotnet add package G9MAUIControls.Barcode
```

## What it adds

`G9BarcodeTextEntry` — an outlined text field with an integrated scan affordance. It extends the core's
`G9TextEntry`, so it inherits the whole outlined-field architecture (painted outline, floating label,
icon slots, RTL column swapping) and adds:

- a trailing scan button that raises `ScanRequested` so you can open your scanner;
- **trailing state feedback** — idle glyph → spinner while scanning → accepted tick → error glyph — using
  the core's cached-icon-host contract, so a state change never flashes a tofu box;
- optional format validation, so a scan that does not match is rejected at the field rather than
  downstream;
- a multi-scan session mode for entering many codes in a row without reopening the scanner.

## Why this is a separate package

Barcode entry is a scanning workflow, not a text field: a scan-state machine, an accept/reject rule, and
a multi-scan session are opinions about a task, so they stay out of the core's general-purpose input
surface. Keeping it separate also lets it version independently of the 25 core controls.

**What it deliberately does NOT do:** drive a camera. See "Usage" — the control raises `ScanRequested`
and you supply the scanner, which means this package never dictates which camera library your app uses.

## Platform setup

These are what **your scanner** will need. Nothing here is required by this package's own code; it is
here because every consumer of this control ends up needing it.

**Android** — `AndroidManifest.xml`:
```xml
<uses-permission android:name="android.permission.CAMERA" />
<uses-feature android:name="android.hardware.camera" android:required="false" />
```
`required="false"` matters: a hard requirement makes your app uninstallable on camera-less devices.

**iOS** — `Info.plist`:
```xml
<key>NSCameraUsageDescription</key>
<string>Scan barcodes to enter codes without typing them.</string>
```
Write a real reason. App review rejects placeholder text, and the string is shown to the user verbatim.

**Windows / Mac Catalyst** — nothing. The scanner reference is Android/iOS-only and these platforms
compile against a no-op provider, so the scan button is hidden rather than offering a control that
cannot work.

## Usage

**You own the camera; the control owns the field.** `G9BarcodeTextEntry` does not open a scanner
itself — it raises `ScanRequested` when the user taps its trailing scanner icon, and you show whatever
scanner you already use. Feed results back with `StartScan()` / `StopScan()` and let the control
validate them. That split is deliberate: a control that owned the camera would dictate which scanner
package your app uses.

```xml
<barcode:G9BarcodeTextEntry
    x:Name="CodeEntry"
    Label="Sample code"
    Text="{Binding Code}"
    AcceptedCodeRegex="^\d{3}/\d{3}/\d{6}$"
    ScanMode="Multiple" />
```

```csharp
// User tapped the scanner icon — open your scanner.
CodeEntry.ScanRequested += async (_, _) =>
{
    CodeEntry.StartScan();                    // trailing slot becomes a spinner
    try
    {
        var code = await myScanner.ScanOnceAsync();
        CodeEntry.Text = code;                // rejected silently if AcceptedCodeRegex doesn't match
    }
    finally
    {
        CodeEntry.StopScan();                 // always — otherwise the field stays busy
    }
};

// A code passed AcceptedCodeRegex.
CodeEntry.Accepted += (_, code) => viewModel.Add(code);
```

`ScanMode.Single` returns to `Idle` after one accepted code; `ScanMode.Multiple` stays armed so an
operator can enter many codes without reopening the scanner. `ScanState` is a
`G9BarcodeTextEntryState` — `Idle` / `ScanBusy` / `Accepted` / `Error` — and drives the field's chrome,
so you can also drive it directly instead of using `StartScan` / `StopScan`.

## Requirements

.NET 10 · `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows10.0.19041.0`

## License

MIT
