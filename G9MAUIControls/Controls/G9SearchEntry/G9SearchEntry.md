# G9SearchEntry

`G9SearchEntry` is a search-flavored specialization of `G9TextEntry` that ships with
the M3 search-bar conventions pre-configured: leading 🔍 icon, clear "×" button,
"Search…" placeholder, debounced query notification, and a built-in voice mic that
drives `CommunityToolkit.Maui.Media.SpeechToText` when the field is empty. Inherits the
standard outlined shell from `G9TextEntry` (no separate "filled" surface — keeping
one visual style across every input on the page).

## When to use

- A search box (leading 🔍, clear button, debounced query, optional voice) ->
  `G9SearchEntry`.
- General single-line text without search conventions -> `G9TextEntry`
  (see `G9TextEntry.md`).

## Bindable Properties

### Inherited from `G9TextEntry`

Everything in `G9TextEntry.md` applies. The notable inherited defaults pre-set by
`G9SearchEntry`:

| Property | Default in `G9SearchEntry` | Why |
|---|---|---|
| `LeadingIcon` | `G9Glyphs.Search` | Search-bar convention. A built-in vector glyph, so it renders with no icon font registered. |
| `ClearButton` | `true` | One-tap clear. |
| `Placeholder` | `G9Strings.Get(G9StringKey.Search)` | Localized "Search…" string. |

### Resting colour — one muted tone

At rest (not focused, empty, no error) a search box draws its **outline**, placeholder text, and
leading search glyph in `G9Palette.InputPlaceholder`, instead of the generic
`G9Palette.Outline` hairline every other outlined field rests on. An empty search box is an
invitation, not a form field with an answer in it; the starter state should be one muted input tone,
not a dark border around lighter placeholder chrome.

This is implemented with overrides of the base's `ResolveRestingOutlineColor(G9Palette)` and
`ResolveRestingContentColor(G9Palette)` hooks. **Only the RESTING colours are a subclass's to
choose** — focused, filled, error and status states still resolve through
`G9OutlinedFieldBase.ResolveStateColor`, so a search box signals those exactly like every other
input in the app. Override those hooks if another field ever needs its own resting tone; never reach
for the private state resolution.

### Specific to `G9SearchEntry`

| Property | Type | Default | Description |
|---|---|---|---|
| `DebounceMs` | `int` | `250` | Delay between the last keystroke and `DebouncedTextChanged` / `SearchCommand` firing. Set to `0` to fire on every keystroke. |
| `VoiceEnabled` | `bool` | `true` | When false, suppresses the trailing voice-mic affordance. |
| `VoiceCulture` | `CultureInfo?` | `null` | Override for the speech-recognition culture. When null, the active `CultureInfo.CurrentUICulture` is used (so a Persian-language UI naturally recognizes Persian). |
| `SearchCommand` | `ICommand?` | `null` | Fired with the current `Text` when the debounce window elapses, or immediately when `DebounceMs == 0`. |

## Events

| Event | Payload | Fired when |
|---|---|---|
| `DebouncedTextChanged` | `string?` (current `Text`) | Debounce window elapses (or instantly when `DebounceMs == 0`). |
| `VoiceListeningStarted` | — | Voice session begins. |
| `VoiceListeningEnded` | — | Voice session ends (final transcript, cancellation, or error). |
| `VoiceFailed` | `string` (reason) | Voice session can't proceed (permission denied, recognizer unavailable, locale not supported). |

## Methods

| Method | Description |
|---|---|
| `Submit()` | Bypass the debounce window and fire `DebouncedTextChanged` + `SearchCommand` with the current `Text` immediately. Use for explicit commit (Enter key, search button, etc.). |
| `StartVoiceAsync()` | Request permissions and start a voice session in the active culture. |
| `StopVoiceAsync()` | Cancel an in-flight voice session and reset the trailing icon. |
| `ToggleVoiceAsync()` | Start a session if none is running, otherwise stop. The trailing mic icon calls this on tap. |

## Voice Recognition

Voice support is built in — no consumer wiring required for the recognizer itself. The
mic icon shows in the trailing slot only when the field is empty (so it never collides
with the clear "×" affordance) and the actionable-gate logic in
`G9OutlinedFieldBase` plays the ink-ripple press animation only when voice is
enabled.

When the user taps the mic:

1. `Permissions.Microphone` is checked / requested.
2. `SpeechToText.Default.RequestPermissions(...)` is called — covers the Apple-platform
   speech-recognition permission separately from microphone access.
3. `StartListenAsync(SpeechToTextOptions { Culture, ShouldReportPartialResults = true })`
   starts the recognizer.
4. The trailing icon swaps to a red "MicOff" glyph so the user can tap again to cancel.
5. `RecognitionResultUpdated` partials are appended to whatever the user had typed
   before tapping the mic — voice ADDS to the query, doesn't clobber it.
6. `RecognitionResultCompleted` writes the final transcript and fires the normal
   debounced search pipeline.

### Platform reality

| Platform | Persian (`fa-IR`) support | Notes |
|---|---|---|
| Android | **Yes** | Uses `android.speech.SpeechRecognizer` via Google Voice Search. Persian has been an officially supported language since Voice Search's 2016 expansion. Requires `RECORD_AUDIO` permission and an `<intent>` query for `android.speech.RecognitionService` (both already in our manifest). |
| iOS | **No** | `SFSpeechRecognizer` does not include `fa-IR` in its supported locales (verified through iOS 18). The recognizer will fail and surface the error via `VoiceFailed`. Persian users on iOS should rely on the keyboard's dictation mic instead — that's a system-keyboard feature we can't trigger from C#. |
| MacCatalyst | **No** | Same Speech Framework as iOS; same limitation. |
| Windows | **Pack-dependent** | `Windows.Media.SpeechRecognition` supports Persian only when the Persian language pack is installed via Settings → Time & Language → Language. |

### Required platform permissions

Already wired in the project's manifests (see commit history for the G9SearchEntry
introduction):

- **Android** (`Platforms/Android/AndroidManifest.xml`):
  - `<uses-permission android:name="android.permission.RECORD_AUDIO" />`
  - `<intent><action android:name="android.speech.RecognitionService" /></intent>` inside `<queries>` — required for Android 11+ to resolve the recognizer service.
- **iOS** (`Platforms/iOS/Info.plist`):
  - `NSMicrophoneUsageDescription`
  - `NSSpeechRecognitionUsageDescription`
- **MacCatalyst** (`Platforms/MacCatalyst/Info.plist`):
  - `NSMicrophoneUsageDescription`
  - `NSSpeechRecognitionUsageDescription`
- **Windows** (`Platforms/Windows/Package.appxmanifest`):
  - `<DeviceCapability Name="microphone" />`

If `VoiceFailed` fires with "Microphone permission denied", the user has refused the
permission via the OS dialog. Consumers should toast the message so the user can
reopen the system permission prompt manually.

## Usage

### Plain search bar with debounced query

```xml
<newControls:G9SearchEntry
    x:Name="ProductSearch"
    DebounceMs="300"
    DebouncedTextChanged="ProductSearch_OnDebouncedTextChanged" />
```

```csharp
private void ProductSearch_OnDebouncedTextChanged(object? sender, string? query)
{
    ProductsViewModel.Filter(query);
}
```

### Bind a `SearchCommand` instead of a code-behind handler

```xml
<newControls:G9SearchEntry
    DebounceMs="250"
    SearchCommand="{Binding FilterProductsCommand}" />
```

### Disable voice on a specific search box

```xml
<newControls:G9SearchEntry
    VoiceEnabled="False"
    Placeholder="SKU lookup..." />
```

### Force a specific recognition culture

```csharp
SearchEntry.VoiceCulture = new System.Globalization.CultureInfo("fa-IR");
```

By default the recognizer matches `CultureInfo.CurrentUICulture`, which for this app
is set by `G9Culture` — switching the app to Persian automatically switches
voice search to Persian on Android. Override only when the recognized language must
differ from the UI language (e.g. crop names in English even when the UI is Persian).

### Listen for voice events for custom UX

```xml
<newControls:G9SearchEntry
    x:Name="GlobalSearch"
    VoiceListeningStarted="GlobalSearch_OnVoiceStarted"
    VoiceListeningEnded="GlobalSearch_OnVoiceEnded"
    VoiceFailed="GlobalSearch_OnVoiceFailed" />
```

```csharp
private void GlobalSearch_OnVoiceStarted(object? sender, EventArgs e)
{
    BackgroundShade.IsVisible = true;
    HintLabel.Text = "Listening...";
}

private void GlobalSearch_OnVoiceEnded(object? sender, EventArgs e)
{
    BackgroundShade.IsVisible = false;
}

private async void GlobalSearch_OnVoiceFailed(object? sender, string reason)
{
    await this.ShowToastAsync($"Voice search: {reason}");
}
```

## Behaviour Notes

- `DebounceMs` is observed via the `Text` property's PropertyChanged pipeline, so it
  also fires for programmatic mutations (binding updates, voice-recognition partials).
  This means a debounced search engine gets one consistent API regardless of whether
  the input came from the keyboard, voice, or external code.
- The mic / clear-button swap is detected by `ResolveTrailingIconSignature`'s "voice"
  branch — switching between empty and non-empty does NOT rebuild the entire icon
  view tree, only the icon itself.
- The mic glyph and color transitions (`Mic` ↔ `MicOff`, Primary ↔ Error) are
  destruction-free: `ResolveTrailingIconSignature` returns the constant value
  `"voice"` regardless of listening state, so the base does not detach / re-attach
  the trailing host content on a mic tap. The cached `MauiIcon` instance is mutated
  in place inside `OnRefresh` after the base finishes painting. Earlier signature-
  driven rebuilds caused a 1-frame tofu rectangle while the platform handler
  re-rasterised the glyph from the embedded font; mutating in place keeps the
  handler attached so only the code-point + tint change. Same trick is in use by
  `G9ChipGroup.CheckmarkIcon` and `G9TabView`'s tab indicator
  (see `G9Controls.md` principle 12 — "destruction-free animations are mandatory").
- Tapping the mic also focuses the inner `Entry` so the on-screen keyboard appears
  immediately. The user can keep typing if they decide voice isn't what they want
  without an extra tap on the field — mirrors Google search and iOS Spotlight,
  where tapping the mic both activates the field AND starts voice in one gesture.
- The voice session captures the existing `Text` as a base before listening starts
  and appends the transcript to it. Tap-mic mid-query → continue speaking → final
  transcript is added to whatever you'd already typed.
- Cancellation: tap the mic again while it's listening (red `MicOff` glyph) to stop
  the session cleanly. Closing the page also cancels via the dispose chain.
