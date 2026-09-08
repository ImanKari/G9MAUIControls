# G9Editor

`G9Editor` is the multi-line outlined text input. It inherits the shared outline +
notched-label architecture from `G9OutlinedFieldBase`. Use it anywhere you need a
multi-line note, description, or comment box.

## When to use

- Multi-line text (notes / descriptions / comments) -> `G9Editor`.
- Single-line text -> `G9TextEntry` (see `G9TextEntry.md`).

## Bindable Properties

### Inherited from `G9OutlinedFieldBase`

See `../G9TextEntry/G9TextEntry.md` for the full inherited list — Label,
Placeholder, HelperText, ErrorText, HasError, AlwaysFloat, IsReadOnly, MaxLength,
ShowCharacterCounter, StatusColor, UseStatusColor, FieldHeight, leading/trailing icons,
IsTrailingBusy, ForceTrailingIconRight, LeadingCommand / TrailingCommand /
CommandParameter.

### Specific to `G9Editor`

| Property | Type | Default | Description |
|---|---|---|---|
| `Text` | `string?` | `""` | Two-way bindable. The current value. |
| `MinimumEditorHeight` | `double` | `96` | Minimum height of the inner `Editor`. The outlined box grows together with the editor. |
| `MaxEditorHeight` | `double` | `0` | Ceiling for the auto-growing editor; `0` means none. Past it the field scrolls its own content instead of growing. See below. |
| `AutoSize` | `EditorAutoSizeOption` | `TextChanges` | Whether the editor auto-grows with content. |
| `IsSpellCheckEnabled` | `bool` | `true` | Enables platform spell check. |
| `IsTextPredictionEnabled` | `bool` | `true` | Enables platform text prediction. |
| `KeyboardType` | `G9KeyboardType` | `Default` | Keyboard layout. |
| `InputTextDirection` | `G9TextInputDirection` | `MatchParent` | Force LTR / RTL on the inner `Editor` only. With `MatchParent`, numeric / email / URL / phone `InputType` values default to LTR even on RTL pages — see [G9TextEntry's RTL section](../G9TextEntry/G9TextEntry.md). |
| `CustomFont` | `string?` | `null` | Override `FontFamily` on the inner `Editor`. When unset, the inner Editor resolves its font from `G9Visuals.ResolveCulturalFont()` — Persian face for Fa, Latin face for En. |
| `VoiceEnabled` | `bool` | `false` | Shows a dictation microphone in the trailing slot. See below. |
| `VoiceCulture` | `CultureInfo?` | `null` | Locale to recognize in. Null follows the app's active language. |



## Growing to a limit, then scrolling

`AutoSize="TextChanges"` (the default) grows the box with the text and never stops. That is right in
a scrolling page and **wrong in a form with anything below the field** — a long value pushes the
footer buttons off the bottom, and the user ends up typing into a control whose Save button they can
no longer reach.

`MaxEditorHeight` is the ceiling. Up to it the field grows as you type; past it, it scrolls its own
content:

```xml
<newControls:G9Editor
    Label="Description"
    MinimumEditorHeight="120"
    MaxEditorHeight="220"
    MaxLength="4000" />
```

**No `ScrollView` is involved, and you must not add one.** The property constrains the field's
MEASURE, and a platform text view is already scrollable — it simply had no reason to be while
nothing bounded it. Wrapping the editor in a `ScrollView` instead gives you two scrollers fighting
for the same drag, which on Android means the outer one usually wins and the caret walks off screen.

Notes:

- The outlined box is capped along with the editor (ceiling + `InnerContentPadding`). Capping only
  the inner control would leave the outline growing around a field that had already stopped.
- Ignored when `AutoSize="Disabled"` — that already pins the height to `MinimumEditorHeight`.
- Set `MinimumEditorHeight` below `MaxEditorHeight`, or the field opens at its ceiling and the
  growth is invisible.


## Voice dictation

Set `VoiceEnabled` and the editor grows a microphone in its trailing slot. The engine, the events
(`VoiceListeningStarted` / `VoiceListeningEnded` / `VoiceFailed`), the methods (`ToggleVoiceAsync`,
`StartVoiceAsync`, `StopVoiceAsync`), `IsListening`, the session flow, the per-platform Persian
reality and the required manifest entries are all shared with `G9TextEntry` — read
[`G9TextEntry.md` → Voice dictation](../G9TextEntry/G9TextEntry.md#voice-dictation) once; it is the
canonical description, and [`G9VoiceDictation`](../../Localization/G9VoiceDictation.cs) is the engine
all three input controls drive.

**Two things are deliberately different here.**

1. **The microphone is NOT value-gated.** On `G9TextEntry` the mic hands the trailing slot to the
   clear button as soon as there is a value; an editor has no clear button to hand it to, and a long
   description is exactly the thing a user wants to keep dictating into. So it stays visible whether
   or not the field has content, and every transcript APPENDS.
2. **It is pinned to the BOTTOM of the box, not centred.** `TrailingHost.VerticalOptions` is set to
   `End` in the constructor. The base defaults to centred, which is right for a one-line entry and
   wrong for a text area: an affordance floating beside the middle of a paragraph reads as part of
   the text. Level with the last line is where "finish this thought out loud" belongs.

```xml
<newControls:G9Editor
    Label="Description"
    MinimumEditorHeight="128"
    VoiceEnabled="True" />
```

> A microphone needs a registered `G9Speech.Provider`. With none, it stays hidden — the control never
> offers an affordance that can only fail.

## Usage

### Auto-sizing notes field

```xml
<newControls:G9Editor
    Label="Notes"
    Placeholder="Add inspection notes…"
    Text="{Binding Notes}"
    AutoSize="TextChanges"
    MinimumEditorHeight="96" />
```

### Fixed-height comment with character counter

```xml
<newControls:G9Editor
    Label="Comment"
    Text="{Binding Comment}"
    MaxLength="500"
    ShowCharacterCounter="True"
    AutoSize="Disabled"
    MinimumEditorHeight="120" />
```

### Read-only display of long text

```xml
<newControls:G9Editor
    Label="Locked notes"
    Text="{Binding HistoricalNote}"
    IsReadOnly="True"
    MinimumEditorHeight="80" />
```

### Validation error

```xml
<newControls:G9Editor
    Label="Description"
    Text="{Binding Description}"
    HasError="{Binding DescriptionInvalid}"
    ErrorText="Description must include the finding and action" />
```

## Behaviour Notes

### Floating label & inner padding

- The floating label sits above the first line of text. The base reserves
  `InnerContentPadding = (0, 12, 0, 8)` so the notch never overlaps the first character.
- The same outline / notch / floating-label animation used by `G9TextEntry` applies
  here. See `../G9Controls.md` for the architecture-level behaviour and
  `../G9TextEntry/G9TextEntry.md` for the per-control rendering notes
  (focus emphasis, RTL physical icon swap, tap-to-focus, etc.).

### Sizing

- `MinimumEditorHeight` drives both the inner `Editor.MinimumHeightRequest` AND the
  outlined `Box.MinimumHeightRequest`, so the box grows together with the editor.
- When `AutoSize == EditorAutoSizeOption.Disabled`, the inner `Editor.HeightRequest`
  is pinned to `MinimumEditorHeight` so the box never shrinks below the configured size.
- `FieldHeight` (inherited) sets a fixed box height that overrides the auto-grow
  behaviour. Use it only for compact fixed-height comment boxes.

### Native chrome

- The inner `Editor` has `StyleId = "no-underline"` (the
  `G9PlatformConfig.NoUnderlineStyleId` constant), which the project's
  `EditorHandler.Mapper` patches to:
  - **Android** — strip the EditText background tint and zero the horizontal padding
    while keeping a small vertical breathing room so multi-line text doesn't visually
    clip the top / bottom edge.
  - **iOS / macOS Catalyst** — clear background, remove layer border.
  - **Windows** — routed through the **same deferred chrome-strip path as
    `G9TextEntry`** (the `EditorHandler.PlatformView` is a `MauiTextBox : TextBox`, so it
    shares the Entry's chrome). The strip runs on the platform `TextBox`'s `Loaded` event
    (immediate if already loaded) so the resource-dictionary overrides actually apply — it
    strips the focus underline, the focus visual, the border brushes, and the hidden
    `TextControlThemePadding`. The background fill in every visual state (hover / focus /
    disabled) is flattened to transparent at the **WinUI Application scope** in
    `Platforms/Windows/App.xaml` (not per-instance — see `G9Controls.md` §15 W10 / W11),
    so a multiline editor shows no background lightening on hover or focus. The startup
    auto-focus / page-jump is handled structurally by the `ScrollViewHandler` `IsTabStop`
    mapping (§15 W9). See `G9TextEntry.md` "Native chrome" and `G9Controls.md` §15
    pitfalls **W3 / W9 / W10 / W11**.
- The Editor's vertical padding is preserved on Android because zero vertical padding
  would cause the first/last lines of text to butt against the inner-content host
  edge. The horizontal sides are zeroed so the icon-to-text gap matches the metric.

### Validation on blur

- `G9Editor` validates on focus loss for self-validating `InputType`s (`Email` / `Url`)
  and `Custom` + `ValidationPattern`, surfacing the result via `HasError` / `ErrorText`.
- **The blur-validation and visual-refresh flow lives in the shared base**
  (`G9OutlinedFieldBase.HandleInnerFocusChanged`, gated by `ShouldAutoValidate` /
  `RunValidation`), identical to `G9TextEntry`. A field with NO validation rule that the
  consumer puts into an error state manually (`HasError="True"` + `ErrorText`) keeps that
  error across focus / blur — a blur never clears an externally-set error. See
  `G9TextEntry.md` "Validation errors".

### Two-way text binding

- Two-way `Text` binding is reentrancy-safe via a `_syncingText` flag.
- Platform property writes inside `ApplyEditorProperties` are guarded by equality
  checks so a focus event that re-runs the apply pass doesn't re-write properties
  that haven't actually changed.

### Tear-down safety

- Inherits the `_isDestroyed` flag handling from `G9ControlBase`. Queued visual
  passes that haven't run yet exit immediately when the page closes, and platform
  property writes inside `OnApplyVisuals` are wrapped in a defensive try/catch
  for `ObjectDisposedException`.
