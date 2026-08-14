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
| `AutoSize` | `EditorAutoSizeOption` | `TextChanges` | Whether the editor auto-grows with content. |
| `IsSpellCheckEnabled` | `bool` | `true` | Enables platform spell check. |
| `IsTextPredictionEnabled` | `bool` | `true` | Enables platform text prediction. |
| `KeyboardType` | `G9KeyboardType` | `Default` | Keyboard layout. |
| `InputTextDirection` | `G9TextInputDirection` | `MatchParent` | Force LTR / RTL on the inner `Editor` only. With `MatchParent`, numeric / email / URL / phone `InputType` values default to LTR even on RTL pages — see [G9TextEntry's RTL section](../G9TextEntry/G9TextEntry.md). |
| `CustomFont` | `string?` | `null` | Override `FontFamily` on the inner `Editor`. When unset, the inner Editor resolves its font from `G9Visuals.ResolveCulturalFont()` — Persian face for Fa, Latin face for En. |

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
