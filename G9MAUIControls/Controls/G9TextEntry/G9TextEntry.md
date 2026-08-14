# G9TextEntry

`G9TextEntry` is the standard outlined single-line text input. It inherits the shared
outline + notched-label + icon-padding architecture from `G9OutlinedFieldBase`.

## When to use

- General single-line text -> `G9TextEntry`.
- A search box (leading search icon + clear button + debounce + optional voice) ->
  `G9SearchEntry` (see `G9SearchEntry.md`).
- Multi-line text -> `G9Editor` (see `G9Editor.md`).
- Scanner-aware barcode / QR input -> `BarcodeEntry` (built on `G9BarcodeTextEntry`).

## Bindable Properties

### Inherited from `G9OutlinedFieldBase`

| Property | Type | Default | Description |
|---|---|---|---|
| `Label` | `string?` | `null` | Floating label text. |
| `Placeholder` | `string?` | `null` | Rest-state placeholder shown when no value and no focus. |
| `HelperText` | `string?` | `null` | Helper text shown below the box. |
| `ErrorText` | `string?` | `null` | Error text shown below the box (only when `HasError` is true). |
| `HasError` | `bool` | `false` | Forces the error color and shows `ErrorText`. |
| `AlwaysFloat` | `bool` | `false` | Keeps the floating label up regardless of focus / value state. |
| `IsReadOnly` | `bool` | `false` | Disables typing while keeping the value visible. |
| `MaxLength` | `int` | `-1` | Character cap. `-1` = no limit. |
| `ShowCharacterCounter` | `bool` | `false` | Shows `n / max` in the footer. Requires `MaxLength > 0`. |
| `StatusColor` | `Color?` | `null` | Override outline color when `UseStatusColor = true`. |
| `UseStatusColor` | `bool` | `false` | Forces the outline to draw with `StatusColor` (useful for "busy" states). |
| `FieldHeight` | `double` | `0` (= use default) | Optional override for the box height. `0` means use `G9Metrics.ControlHeight` (52). Useful for compact / dense forms or to prove the layout scales. |
| `ReserveFloatingLabelClearance` | `bool` | `false` | Reserves the floated-label overhang (`G9Metrics.FloatingLabelClearance` = 6) as top padding INSIDE the field, so the floated label renders within the field's own bounds instead of spilling above it and being covered/clipped by whatever sits directly on top (a bottom-sheet header, a card edge). Turn ON when the field's top butts a hard edge. `G9SearchEntry` defaults it ON; height-matched search+sort/filter lanes turn it OFF to keep the shared centre line. See `08-UI-UX-Design-System.md` §4. |
| `LeadingEmoji` / `LeadingMaterialIcon` / `LeadingImagePath` / `LeadingImageSource` | — | `null` | Leading icon. |
| `TrailingEmoji` / `TrailingMaterialIcon` / `TrailingImagePath` / `TrailingImageSource` | — | `null` | Trailing icon. |
| `IsTrailingBusy` | `bool` | `false` | Replaces trailing icon with a spinner. |
| `ForceTrailingIconRight` | `bool` | `false` | Pins the trailing icon on the physical right edge regardless of FlowDirection. The leading icon still follows logical placement. |
| `LeadingCommand` / `TrailingCommand` | `ICommand?` | `null` | Tap commands for the icon slots. |
| `CommandParameter` | `object?` | `null` | Passed to leading/trailing commands. |

### Specific to `G9TextEntry`

| Property | Type | Default | Description |
|---|---|---|---|
| `Text` | `string?` | `""` | Two-way bindable. The current value. |
| `IsPassword` | `bool` | `false` | Masks input with bullets. |
| `PasswordToggle` | `bool` | `false` | Adds an eye icon that toggles visibility. |
| `ClearButton` | `bool` | `false` | Adds a × icon when the field has a value. |
| `InputType` | `G9InputType` | `Default` | Semantic input typing — drives keyboard, live keystroke filter, and on-blur validation in one go. See "Input typing" below. |
| `AllowedCharsPattern` | `string?` | `null` | Regex character class used for the live filter when `InputType == Custom`. |
| `ValidationPattern` | `string?` | `null` | Regex run on focus loss when `InputType == Custom`. |
| `ValidationErrorText` | `string?` | `null` | Custom error message shown when validation fails (overrides the built-in localized default). |
| `KeyboardType` | `G9KeyboardType` | `Default` | Legacy keyboard-only setter. New code should set `InputType` instead — `KeyboardType` is automatically synced from it. |
| `InputTextDirection` | `G9TextInputDirection` | `MatchParent` | Force LTR / RTL on the inner `Entry` only (label / outline still match parent). |
| `CustomFont` | `string?` | `null` | Override `FontFamily` on the inner `Entry`. |
| `Validator` | `IG9TextValidator?` | `null` | Custom validator invoked on `Validate()`. |
| `ValidateOnTextChanged` | `bool` | `false` | Auto-runs `Validate()` on every text change. |

## Methods

- `Validate()` — runs `Validator?.Validate(Text)` first, then `G9InputTypePolicy.Validate(...)`
  for `Email` / `Url` / `Custom`. Returns `true` if no error. Sets `HasError` /
  `ErrorText` accordingly.

## Input typing

`InputType` is the **single switch** that controls three things at once:

1. The on-screen keyboard (numeric / email / telephone / URL / default).
2. A **live keystroke filter** — every character typed (or pasted) is checked, and
   characters that don't match the input type are silently dropped before they reach
   the bound `Text`. This is what makes `InputType.Number` actually accept only digits
   even when the user has a hardware keyboard or pastes "abc 123" from the clipboard.
3. **On-blur validation** for types that have a "shape" (Email, URL, Custom). When the
   field loses focus, the value is checked and `ErrorText` / `HasError` are surfaced
   if it's malformed.

| `InputType` | Keyboard | Live filter | On-blur validation |
|---|---|---|---|
| `Default` | Default | (no filter) | none |
| `Number` | Numeric | digits 0-9 | none |
| `Decimal` | Numeric | digits + one culture-aware decimal separator | none |
| `SignedNumber` | Numeric | optional leading `-` + digits | none |
| `SignedDecimal` | Numeric | optional leading `-` + digits + separator | none |
| `Phone` | Telephone | digits, `+`, `-`, space, `(`, `)` | none |
| `Email` | Email | rejects whitespace | RFC-shape regex |
| `Url` | URL | rejects whitespace | `Uri.TryCreate(..., Absolute, _)` |
| `Letters` | Default | Latin a–z A–Z | none |
| `LettersAndNumbers` | Default | Latin letters + digits, no specials | none |
| `LettersNumbersSpace` | Default | Latin letters + digits + space | none |
| `PersianLetters` | Default | Persian / Arabic letters (U+0600–U+06FF) + space | none |
| `PersianLettersAndNumbers` | Default | Persian letters + Persian or ASCII digits | none |
| `MultilingualLettersAndNumbers` | Default | Latin + Persian letters + digits + space | none |
| `NoSpecialChars` | Default | letters (Latin / Persian) + digits + space | none |
| `Custom` | Default | `AllowedCharsPattern` (per-character regex) | `ValidationPattern` |

Live filtering applies on **every** path that mutates the inner `Entry`: typing,
clipboard paste, hardware keyboard, programmatic `Text = ...` (so a ViewModel that
pushes "abc" into a `Number` field will see the value sanitized to ""). The filter is
a simple character-class predicate so it's safe to run on each keystroke without
visible lag.

### Decimal separators

`Decimal` / `SignedDecimal` resolve the active culture's decimal separator
(`CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator`). The filter also
accepts `.` and the Persian ٫ U+066B and normalises every variant to the active
culture's separator on the way to `Text`, so the parsed value round-trips cleanly
through `double.TryParse` regardless of which separator the user typed.

### Validation errors

The on-blur validation surfaces a localized error via `ErrorText` / `HasError`. Pass
`ValidationErrorText` to override the message:

> **Externally-set errors survive focus changes.** On-blur validation only runs when the
> field actually has a validation rule — an `IG9TextValidator`, a self-validating
> `InputType` (`Email` / `Url`), or `Custom` with a `ValidationPattern`. This guard lives
> in the shared base (`G9OutlinedFieldBase.ShouldAutoValidate` / `RunValidation`, invoked
> from `HandleInnerFocusChanged`), so a field with NO rule that the consumer puts into an
> error state manually (setting `HasError="True"` + `ErrorText`) keeps that error across
> focus / blur instead of having it cleared by a blur-time `Validate()` that found nothing
> wrong. `G9Editor` shares the exact same flow.


```xml
<newControls:G9TextEntry
    Label="Email"
    InputType="Email"
    ValidationErrorText="Please enter a valid email" />
```

### Custom rules — `InputType="Custom"`

Use `Custom` plus `AllowedCharsPattern` (live filter) and `ValidationPattern` (on-blur
shape check). Both are optional but `AllowedCharsPattern` is what makes the filter
do anything beyond passing through.

```xml
<newControls:G9TextEntry
    Label="Inventory code"
    InputType="Custom"
    AllowedCharsPattern="[A-Z0-9_]"
    ValidationPattern="^[A-Z]{2}_\d{4}$"
    ValidationErrorText="Code must look like AA_1234" />
```

`AllowedCharsPattern` is matched **per character** — pass a single character class
like `[A-Z0-9_]`. Multi-character patterns can't be applied incrementally as the user
types and will be applied char-by-char anyway. `ValidationPattern` is matched against
the **full value** on focus loss.

### Inheritance

Every entry that inherits `G9TextEntry` (`G9SearchEntry`, `G9BarcodeTextEntry`) gets
`InputType` for free — `G9SearchEntry` typically uses `Default`, `G9BarcodeTextEntry`
typically uses `Default` (the scanner pipes already-validated codes via
`AcceptScannedCode`). When the consumer sets `InputType` on those subclasses, the same
filter / keyboard / blur-validation flow applies. `G9Editor` mirrors `InputType` for
multi-line input (no password types).

## Built-in Trailing Affordances

The trailing icon slot has built-in logic for two common patterns. They run before any
`TrailingCommand` you set:

1. **`PasswordToggle`** → the trailing icon becomes a `Visibility` / `VisibilityOff` glyph.
   Tapping it toggles `_passwordVisible` so the user sees the actual characters while
   the bindable `IsPassword` stays `true`.
2. **`ClearButton`** → the trailing icon becomes a `Close` glyph. Tapping it clears `Text`.

If neither applies, your `TrailingCommand` runs on tap.

**Both are VALUE-GATED (2026-07-29): an empty field renders neither icon AND reserves no room for
one.** The gate is `HasExtraTrailingAffordance()`, which is the same predicate the layout uses to
reserve trailing room — so the two can never disagree and produce a blank reserved slot.

- Why it matters beyond tidiness: **in RTL the placeholder sits on the trailing side**, so a
  reserved-but-empty icon slot insets the placeholder and then lets it snap outward on the first
  keystroke. Value-gating removes the shift in both directions. Verified on device in Persian: the
  empty field's placeholder runs to the trailing edge, the filled field puts the eye in that space.
- An eye on an EMPTY password reveals nothing, so it is pure noise on the app's first screen.
- **Clearing a revealed password re-masks it** (`_passwordVisible` resets in `OnTextChanged`). The
  eye disappears with the value, so a latched "visible" would reveal whatever is typed next with no
  control left to hide it.

## Icon Press Feedback

Leading and trailing icons play an ink-ripple + soft scale-dip animation when tapped.
The animation only fires when the icon is **actionable** — meaning one of:

- The field defines `LeadingCommand` (for the leading icon)
- The field defines `TrailingCommand`, OR has an active built-in trailing affordance
  (`PasswordToggle`, `ClearButton` with text, scanner state, picker arrow, etc.)

Decorative icons (e.g. a wheat 🌾 next to a Farm Name field with no command wired)
stay completely passive — no ripple, no scale dip, no false-positive interactivity
hint. This was a deliberate change from the previous behavior where every icon tap
animated regardless of whether anything would happen.

The motion is destruction-free:

- Each icon host owns a stable `GraphicsView` ripple overlay built once in the base
  constructor. The ripple drawable's `Center`, `Progress`, and `Color` are mutated per
  frame instead of recreating views.
- Ripple tint follows the icon's resolved color at low alpha (`G9Colors.IconRippleAlpha`)
  so it stays readable on every theme — light icon on dark surface gives a subtle pale
  ripple, dark icon on light surface gives a faint dark ripple.
- The tap origin is captured from `TappedEventArgs.GetPosition` and normalized to 0..1
  inside the host's bounds, so the ripple radiates from where the user actually touched.
  Falls back to the host's center if the platform doesn't surface tap coordinates.
- The scale dip target is `G9Metrics.IconPressScaleTo` (0.92 — soft) instead
  of the previous 0.78. The ripple does the heavier visual lifting; the scale just adds
  a subtle tactile cue. The release uses a `SpringOut` ease for a slightly bouncy
  feel that distinguishes "press accepted" from a flat scale return.

## Usage

### Plain field with floating label

```xml
<newControls:G9TextEntry
    Label="Farm name"
    Placeholder="Enter farm name"
    Text="{Binding FarmName}" />
```

### Email with validation

```xml
<newControls:G9TextEntry
    Label="Email"
    Placeholder="name@example.com"
    InputType="Email"
    Text="{Binding Email}"
    ValidateOnTextChanged="True"
    ErrorText="Please enter a valid email" />
```

### Password with eye toggle

```xml
<newControls:G9TextEntry
    Label="Password"
    IsPassword="True"
    PasswordToggle="True"
    Text="{Binding Password}"
    LeadingMaterialIcon="Lock"
    ForceTrailingIconRight="True" />
```

### Counter

```xml
<newControls:G9TextEntry
    Label="Bio"
    Text="{Binding Bio}"
    MaxLength="120"
    ShowCharacterCounter="True" />
```

### Always-floated label

Use when the field has a known value populated from the view model on first render:

```xml
<newControls:G9TextEntry
    Label="Owner"
    Text="{Binding OwnerName}"
    AlwaysFloat="True" />
```

### Compact field

```xml
<newControls:G9TextEntry
    Label="Search"
    LeadingMaterialIcon="Search"
    FieldHeight="40" />
```

### LTR text inside an RTL page

Phone numbers, IDs, emails — anything that should always read left-to-right:

```xml
<newControls:G9TextEntry
    Label="Phone"
    Text="{Binding Phone}"
    InputType="Phone"
    InputTextDirection="LeftToRight"
    ForceTrailingIconRight="True" />
```

The label / outline still mirror with the page; only the inner text reads LTR.

### Busy trailing indicator

Use during async validation (e.g., checking if a code is unique):

```csharp
CodeEntry.IsTrailingBusy = true;
CodeEntry.UseStatusColor = true;
try
{
    await _api.CheckCodeAsync(code);
}
finally
{
    CodeEntry.IsTrailingBusy = false;
    CodeEntry.UseStatusColor = false;
}
```

### Custom validator

```csharp
public sealed class FarmNameValidator : IG9TextValidator
{
    public string? Validate(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "Required" : null;
}

// In XAML or code-behind:
NameEntry.Validator = new FarmNameValidator();
NameEntry.ValidateOnTextChanged = true;
```

## Behaviour Notes

### Floating label & outline

- The floating label is fully transparent. The outline drawable opens a notch where the
  label sits, so the parent background shows through automatically — there is no
  `LabelBackgroundColor` to tune. The control works on any background.
- **Focus emphasis is a thicker stroke on the same outline**, not a separate outer
  ring. Setting `EmphasisStrokeThickness` on the drawable bumps the outline from
  `G9Metrics.OutlinedFieldStrokeThickness` (1.5 dp) to
  `OutlinedFieldEmphasisStrokeThickness` (2.5 dp); the colour comes from the resolved
  state colour (`Primary` for focus, `Error` for error, `StatusColor` for
  `UseStatusColor`). Focus can also paint a soft inner `HaloStrokeColor` on the same
  notched path, giving a two-stroke focus read without clipping outside the
  `GraphicsView` — but that inner glow is **off by default**: it is gated behind the
  `ShowFocusHalo` bindable (default `false` on every outlined field via the shared base).
  Set `ShowFocusHalo="True"` on a field to bring the glow back; focus without it is shown
  by the thicker emphasis stroke alone.
- **Filled-valid blur keeps the active colour.** After a field with a valid value loses
  focus, the floating label and outline stay `Primary`, but the outline returns to the
  resting 1.5 dp thickness and the halo is removed. Empty untouched fields still return
  to the neutral outline colour; errors and explicit status colours keep their own state
  colours.
- **Floating-label animation** smoothly interpolates `TranslationX`, `TranslationY`,
  and `Scale` together. The slide between rest-over-icon and floated-at-corner is one
  composited animation, no jump.
- The label's rest-state X auto-tracks `G9Metrics.InputIconStartMargin` — change
  the margin and the slide updates in lock-step.

### RTL

- **Leading and trailing icons physically swap** across the box on culture flip. The
  leading icon appears on physical-left in LTR and physical-right in RTL where reading
  starts. The trailing icon mirrors unless `ForceTrailingIconRight = true`.
- **Floating-label X tracking** — the gate that decides whether to re-run the float
  animation watches the `IsRtl` flag explicitly, so a culture toggle that changes
  nothing else (no leading icon → no rest-X change, no value change → no floated
  state change) still re-applies the transform. Without this, the label kept its
  stale TranslationX from the previous direction.
- The inner `Entry` text direction is independent of the label / outline direction.
  Use `InputTextDirection = LeftToRight` for IDs / phone numbers / emails inside an
  RTL page.
- **Default LTR for value-style input types.** `InputType` values that represent
  universally-LTR data (`Number`, `Decimal`, `SignedNumber`, `SignedDecimal`,
  `Phone`, `Email`, `Url`) default the inner `Entry`'s `FlowDirection` to LTR even
  when the page itself is RTL. Same applies when `IsPassword = true`. The floating
  label, outline notch, helper text and icon placement still flow with the page
  culture — only the entered value and its caret are pinned LTR. Without this the
  string `+98 21 1234` renders as `1234 21 89+` on Persian pages and the leading
  `+` ends up on the wrong side. Override per field via `InputTextDirection =
  RightToLeft` if the consumer truly wants the RTL flow on a numeric field.
- **Cultural typeface on the inner text.** The inner `Entry` resolves
  `FontFamily` from `G9Visuals.ResolveCulturalFont(CustomFont)` — Persian
  face for Fa, Latin face for En, with `CustomFont` winning when set. Without an
  explicit family the platform fallback drops Persian glyphs to a generic
  sans-serif that mismatches the floating label / helper text (which already go
  through the app's `CulturalFont` resource).

### Tap-to-focus

- A wrapper-level tap recognizer on the box calls `Entry.Focus()` so taps on the
  floating label, the outline edges, the empty padding, and the inner text area all
  reliably bring up the keyboard. The icon hosts have their own gesture recognizers
  that consume the tap first, so taps on the leading / trailing icons still trigger
  their respective commands without focusing the field.

### Native chrome

- The inner `Entry` has `StyleId = "no-underline"` (the `G9PlatformConfig.NoUnderlineStyleId`
  constant), which the project's `EntryHandler.Mapper` patches to:
  - **Android** — strip the EditText background tint, intrinsic horizontal padding, and
    `compoundDrawablePadding`.
  - **iOS** — set `BorderStyle = None`, clear the layer border.
  - **Windows** — strip the focus underline, the focus visual, the border brushes,
    and the hidden `TextControlThemePadding` (12 px content-area padding baked into the
    template) so the icon-to-text gap matches the explicit `InputIconStartMargin` metric.
    The border / padding strip is **deferred to the platform `TextBox`'s `Loaded` event**
    (run immediately only if it is already loaded), because the mapper fires during
    `SetVirtualView` while the `TextBox` has no `XamlRoot` yet — writing `tb.Resources[...]`
    at that moment throws `COMException 0x80070580` ("Invalid window; it belongs to other
    thread"), which the outer try/catch silently swallowed. Deferring guarantees a live
    `XamlRoot` so every resource override actually applies. See `G9Controls.md` §15
    pitfall **W3**.
- **Windows behaviour notes** (Android / iOS already behave correctly):
  - **No background fill in any state (hover / focus / disabled).** WinUI's default
    `TextBox` template swaps `BorderElement.Background` to a different brush per visual
    state — `TextControlBackgroundPointerOver` on hover and `TextControlBackgroundFocused`
    on focus — which read as the input "lightening" or showing a white fill. These brushes
    are flattened to transparent at the **WinUI Application scope** in
    `Platforms/Windows/App.xaml` (`TextControlBackground` / `*PointerOver` / `*Focused` /
    `*Disabled`), NOT per-instance. A per-instance `Resources` override of the *Focused*
    brush does not win — the template's `Focused` visual-state storyboard resolves the
    `ThemeResource` against the framework / app dictionaries, so the app-level override is
    the one that takes effect. See `G9Controls.md` §15 pitfalls **W10** (hover) / **W11**
    (focus fill).
  - **No startup auto-focus / page-jump.** WinUI parks focus on the first focusable
    element (the first `TextBox`, e.g. "Farm name") on the first stray click after window
    activation, and the parent `ScrollViewer` scrolls it into view — the page appears to
    auto-scroll and auto-focus a field the user never touched. The fix is structural: the
    Windows `ScrollViewHandler` mapper sets `IsTabStop = true` on the WinUI
    `ScrollViewer`'s inner `ContentPanel`
    (`G9PlatformConfig.RegisterWindowsScrollViewFocusFix`), giving a click on empty
    page chrome a valid local focus target so focus stays put and nothing scrolls. This
    also makes a click on empty chrome blur the active input (tap-outside-to-blur on
    desktop). See `G9Controls.md` §15 pitfall **W9**.
- Inner padding is computed from icon presence so the text never collides with an icon
  edge. Padding is symmetric — the same gap appears on the wall side and the inner-text
  side of the icon.

### Two-way text binding

- Two-way `Text` binding is reentrancy-safe via a `_syncingText` flag — the wrapper and
  the inner `Entry` never echo each other's updates.
- Platform property writes (`IsPassword`, `IsReadOnly`, `Keyboard`, `FlowDirection`,
  etc.) inside `ApplyEntryProperties` are guarded by equality checks so a focus event
  that re-runs the apply pass doesn't re-write platform widget swaps. Critical on
  WinUI where `IsPassword` swaps `TextBox` ↔ `PasswordBox` at the platform layer —
  re-writing it during a focus dispatch is a known AOT crash hazard.

### Tear-down safety

- The base `G9ControlBase` listens to `HandlerChanging` and sets a `_isDestroyed` flag
  when the platform handler goes null. Queued visual passes that haven't run yet exit
  immediately, and any platform property write inside `OnApplyVisuals` is wrapped in
  a defensive try/catch for `ObjectDisposedException`. This prevents crashes when the
  page closes while a visual update is still pending in the dispatcher queue.

### Status / error precedence

- The trailing busy spinner and the error state both colour the outline; setting
  `HasError = true` overrides any `UseStatusColor` colour so error always wins.
