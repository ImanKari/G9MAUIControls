# G9PinEntry

`G9PinEntry` is the row-of-cells PIN / OTP / verification-code input. Each cell
holds exactly one character; focus auto-advances on type and auto-walks back on
backspace. Optional grouping with separator characters supports credit-card
style layouts ("4-4-4-4 with `-` separator").

## When to use

- PIN / OTP / verification-code entry (one character per cell, auto-advance) ->
  `G9PinEntry`. Optional grouping with separator characters supports credit-card
  style layouts.
- General single-line text -> `G9TextEntry` (see `G9TextEntry.md`).

## Inheritance

Extends `G9ControlBase` directly, **not** `G9OutlinedFieldBase`. The
outlined-field-with-floating-label architecture is a single piece of value
text — the PIN entry's visual is fundamentally a row of small cells, so the
outline / notch / floating label machinery doesn't apply.

## Architecture: single hidden Entry + visual-only cells

The previous attempt at this control used **one platform `Entry` per cell**
with `MaxLength=1`. That design hit a wall of per-platform IME edge cases:

- Backspace on an empty cell is silently dropped on some Android keyboards
  (no `TextChanged` fires, no `KeyPress` event we can intercept).
- "Select-all on focus" races the IME's own focus dispatch.
- Manual `Entry.Focus()` calls inside `TextChanged` handlers crash on
  certain platforms — exactly what shipped before for the password mode.

The current architecture sidesteps every one of those:

| Element | Role |
|---|---|
| One hidden `Entry` | Holds the **full** PIN string. 1×1 dp, opacity 0, pinned to the top-left corner of the control's root `Grid` (overlapping cell 0). `InputTransparent=true` so its hit-rect doesn't block taps on the cells; `Focus()` still works because that's not gesture-driven. |
| Cell `Border` + `Label` | Pure visuals. No editable widget per cell. Each cell has a `TapGestureRecognizer` that focuses the hidden Entry. |
| `OnApplyVisuals` | Reads `_hidden.Text` and re-renders every cell's text + border state. The active cell is `min(text.Length, cells.Count - 1)` while focused — derived from the value length, never from the platform caret. |

> **Why the hidden Entry isn't off-screen.** A previous version pushed it via
> `Margin = (-1000, -1000, 0, 0)` to keep it out of any visual layer. That
> triggered the host `ScrollView`'s "scroll-to-focused-input" mechanism — the
> moment the user tapped a cell, the page jumped 1000 dp up to bring the
> off-screen Entry into view. Pinning the Entry inside the visible bounds
> (overlapping cell 0) makes scroll-to-focus a no-op because the Entry is
> already on screen.

## Behaviour matrix

This control follows the **standard sequential PIN / OTP model**: characters fill
left-to-right with no gaps; the field is append-only from the user's point of view; the
"active" (highlighted) cell is always the first empty cell, or the last cell once full.

| Action | Effect |
|---|---|
| Type into the field | Char appended to the hidden Entry. After every change the caret is forced to the END of the string, so the next keystroke appends and the active highlight auto-advances. |
| Backspace | Removes the **last** character; the active highlight walks back one cell. The caret is always at the end, so backspace is unambiguous regardless of which cell was tapped. |
| Paste N chars | One change event delivers the full string; we filter against `Type`, clamp to the cell count, and render the matching cells. |
| Tap any cell | Focuses the field and parks the caret at the END of the typed value. We deliberately do **not** edit at the tapped index — a gap-free OTP field always edits at the tail. This keeps behaviour identical across platforms. |
| Last cell filled | `Completed` fires once, `IsComplete = true`. |

> **Active cell is derived purely from the typed length** — `min(text.Length, cells.Count - 1)`
> while focused — never from the platform caret. The platform `CursorPosition` is
> unreliable across platforms (Android resets it after a programmatic `Text` write; on
> Windows the `TextChanging` bridge sets `_hidden.Text`, which snaps the virtual caret to
> 0), so deriving the highlight from it left the green focus border stuck on the first
> cell. Length-based derivation is deterministic everywhere.

## Type filtering

| `Type` | Allowed input | Keyboard |
|---|---|---|
| `Number` (default) | Digits 0-9 | Numeric |
| `Letters` | Latin letters A-Z / a-z | Default |
| `Alphanumeric` | Letters and digits | Default |
| `Password` | Digits 0-9 (cell renders the configured `MaskCharacter`) | Numeric |


The input is filtered both at the keyboard level (the on-screen keyboard) and
at the value level (`OnHiddenTextChanged` strips chars that fail the `Type`
predicate). The double filter keeps the field robust on devices where the
user can paste from a clipboard that bypasses the keyboard.

## Bindable Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Length` | `int` | `4` | Cell count when `GroupSizes` is empty / null. Ignored when `GroupSizes` is set (the sum of the groups is the effective length). |
| `Value` | `string?` | `null` | **Two-way bindable.** The full string formed by concatenating every cell's char. Setting this property programmatically distributes the chars across cells (truncating to capacity, dropping disallowed chars). |
| `Type` | `G9PinEntryType` | `Number` | See type-filtering table above. |
| `Placeholder` | `string?` | `null` | Single-character hint shown inside an empty cell. |
| `MaskCharacter` | `char` | `'●'` | Glyph rendered in cells when `Type=Password`. We never use platform `Entry.IsPassword=true` because masking the hidden Entry breaks the in-bounds 1×1 layout trick AND it crashes some platforms when toggled mid-edit. |
| `GroupSizes` | `string?` | `null` | Dash-separated list of positive integers (e.g. `"4-3-3-2"`). When set, overrides `Length` and inserts a `Separator` label between adjacent groups. |
| `Separator` | `string?` | `"-"` | Character rendered between groups. Empty / null = no separator label. |
| `CellWidth` | `double` | `44` | Per-cell width. |
| `CellHeight` | `double` | `52` | Per-cell height. |
| `AutoFocus` | `bool` | `false` | When true, the hidden Entry grabs focus when the control appears. One-shot (cleared after firing). |
| `IsComplete` | `bool` (read-only) | `false` | True once `Value.Length == Length` (the user has filled every cell). Resets on any backspace. Useful for binding a `VerifyCommand.CanExecute`. |

## Events

| Event | Payload | Fires when |
|---|---|---|
| `ValueChanged` | `string` (full assembled value) | Any cell content change. |
| `Completed` | `string` (full PIN) | The user fills the last cell. Fires once per "complete" transition. |

## Visual states

The cell border has three distinct visual states so the user can tell at a
glance which cells are filled vs. which one will receive the next keystroke:

| State | Stroke | Thickness |
|---|---|---|
| Empty + not focused | `OutlineVariant` (neutral grey) | 1.4 dp |
| Filled + not focused | `Primary` at 35% alpha (soft green) | 1.4 dp |
| Focused (any fill) | `Primary` solid (bright green) | 2 dp |

Tapping a filled cell promotes it from "soft green thin" to "solid green
thick" — the focus state is unmistakable from the resting filled state.

## Vertical centering

Cell glyphs are rendered via `Label.VerticalTextAlignment = Center` inside
the cell's `Border`. No platform tweaks needed — `Label` doesn't have
`EditText`'s baseline-with-leading-on-top metric, so centering is pixel
perfect across platforms.

## Hidden caret

The platform caret on the hidden Entry is suppressed so no stray cursor
indicator leaks through the opacity-0 surface:

- **Android** — `EditText.SetCursorVisible(false)` (caret renders even at
  opacity 0).
- **iOS / Mac Catalyst** — `UITextField.TintColor = UIColor.Clear`.
- **Windows** — `TextBox.Foreground = transparent` (WinUI 3 `TextBox` has no
  `CaretBrush` — that's WPF-only; the caret uses `Foreground` for its colour).

## Windows text bridge

On Windows the hidden Entry is 1×1 dp, `Opacity=0`, `IsHitTestVisible=false`. For such a
degenerate, hit-test-invisible `TextBox`, WinUI raises the synchronous `TextChanging`
event as the user types (and the platform `Text` grows correctly) but **does not raise
the asynchronous `TextChanged` event**. MAUI's `EntryHandler` bridges the platform text
into the virtual `Entry` exclusively from `TextChanged`, so without intervention the
virtual `_hidden.Text` never updates, `OnHiddenTextChanged` never runs, and the cells
never fill — "typing does nothing".

`HookWinUiTextBridge` (wired from the hidden Entry's `HandlerChanged`, `#if WINDOWS` only)
subscribes to the platform `TextBox.TextChanging` and pushes the platform text onto the
virtual `Entry` whenever they differ. An equality guard makes it a no-op if MAUI's own
`TextChanged` bridge ever does fire, so there's no double processing. Documented as
pitfall **W12** in `G9Controls.md` §15.

> The hidden Entry is intentionally **not** tagged with the `no-underline` `StyleId` —
> it's invisible and needs no native-chrome strip, and routing the deferred resource
> writes through it interfered with its text pipeline.


## Usage

### Numeric 4-cell (SMS OTP)

```xml
<newControls:G9PinEntry
    Length="4"
    Type="Number" />
```

### Password 6-cell

```xml
<newControls:G9PinEntry
    Length="6"
    Type="Password" />
```

### Two-way binding to a view-model

```xml
<newControls:G9PinEntry
    x:Name="OtpField"
    Length="6"
    Type="Number"
    Value="{Binding OtpCode}"
    Completed="OnOtpCompleted" />
```

```csharp
private async void OnOtpCompleted(object? sender, string code)
{
    var ok = await AuthService.VerifyAsync(code);
    if (!ok) OtpField.Clear();
}
```

### Grouped credit-card style

```xml
<newControls:G9PinEntry
    GroupSizes="4-4-4-4"
    Separator="-"
    Type="Number"
    CellWidth="36"
    CellHeight="48" />
```

Renders 16 cells split into four groups of four, with `-` labels between
them. The separator label inherits the surface text color and the cell font
size, so it visually matches.

### Alphanumeric with custom separator

```xml
<newControls:G9PinEntry
    GroupSizes="3-3"
    Separator="•"
    Type="Alphanumeric" />
```

### Programmatic clear

```csharp
PinField.Clear();              // reset every cell + return focus
PinField.FocusFirstEmpty();    // jump to the first unfilled cell (or last cell)
```

## Layout tokens

| Token | Value | Meaning |
|---|---|---|
| `PinCellWidth` | `44` | Default cell width. |
| `PinCellHeight` | `52` | Default cell height. |
| `PinCellStrokeThickness` | `1.4` | Resting stroke (empty + filled). |
| `PinCellStrokeThicknessFocused` | `2` | Focused stroke. |
| `PinCellCornerRadius` | `10` | Rounded corner. |
| `PinCellSpacing` | `8` | Inter-cell gap inside a group. |
| `PinSeparatorSpacing` | `6` | Horizontal gap between a cell and a separator label. |
| `PinCellFontSize` | `22` | Glyph size inside a cell. |
| `PinSeparatorFontSize` | `22` | Separator label size. |

## Behaviour notes

- `Value` setters are guarded with a `_suppress` flag so the hidden Entry's
  `TextChanged` handler doesn't echo back into the `Value` setter during
  programmatic distribution.
- `Completed` fires **once** per fill — flickering between full / empty
  (clear + retype) re-arms the event correctly.
- `AutoFocus` is one-shot: it's cleared to `false` after grabbing focus the
  first time so a culture flip / theme refresh doesn't repeatedly steal
  focus from another control on the page.
- The active-cell border is computed from the typed length —
  `min(text.Length, cells.Count - 1)` while focused — never from the platform
  `CursorPosition` (which is unreliable across platforms; see the "Behaviour matrix" note).
  After every text change the caret is forced to the end so typing appends and backspace
  removes the last character.
- Cell repaints are color-only writes on existing `Border` instances — no
  view recreation, no animation overhead.

## RTL / cultural typography

`G9PinEntry` is **always laid out left-to-right** regardless of the surrounding
culture. PIN / OTP / verification codes are entered LTR everywhere — the same
way SMS verification codes are read off a phone — so flipping the cell row to
RTL on a Persian page would swap `1234` into `4321` and put the leading group
of a card-style PIN on the wrong side. The control's root `Grid` is
constructed with `FlowDirection = LeftToRight` once and never re-evaluated.

The hidden `Entry` is also pinned to LTR so its caret advances in the
keystroke order even when external IMEs (like the Android Persian keyboard)
default to RTL editing.

The cell `Label` and separator `Label` glyph typeface is resolved via
`G9Visuals.ResolveCulturalFont()` on every visuals pass — Persian face
for Fa cultures, Latin face for En. The lightweight `OnCultureChangedHook`
override pushes the new font onto each cached cell label without rebuilding
the whole row, so a language switch is essentially free for filled PIN
fields.
