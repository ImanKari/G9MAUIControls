# G9CheckBox

`G9CheckBox` is the suite's check box (1.2.0, Jira ITCS-15686, ADR-0028): a Material-3 rounded
square drawn on a `GraphicsView` — 20dp, 6dp corners, 2dp outline — that **fills with `Primary`
while a tick draws itself in**, an optional **indeterminate** state (a bar on the filled box), a soft
circular **press halo**, and an optional wrapping **label** beside it. It replaces the platform
`CheckBox`, which is three different native widgets on three platforms and ignores `G9Palette`.

## When to use

- Pick any number of options from a list; "I agree to …"; a "select all" with a partial state ->
  `G9CheckBox`.
- A single setting that takes effect immediately (notifications on/off) -> `G9Switch`
  (see `../G9Switch/G9Switch.md`).
- Pick exactly one of a few options -> `G9Switch` with a shared `SelectionGroup`, or `G9Picker` /
  `G9ChipGroup` (single selection).
- Filter chips -> `G9ChipGroup` (it has its own selected-state check mark).

## Bindable properties

| Property | Type | Default | Description |
|---|---|---|---|
| `IsChecked` | `bool` | `false` | **Two-way by default.** Ticked or not. Any change (tap, code, binding) raises `CheckedChanged` and animates. |
| `IsIndeterminate` | `bool` | `false` | **Two-way by default.** The third, "some of them" state — a bar on a filled box. Wins over `IsChecked` visually while `true`. Only code / a binding sets it; a tap clears it and **checks** the box. |
| `Text` | `string?` | `null` | Optional label on the box's END side. Wraps (the box stays level with the first line). Cultural typeface (`G9Visuals.ResolveCulturalFont()`). Tapping it toggles. Also the screen-reader name. |
| `Command` | `ICommand?` | `null` | Runs after a **user** toggle (tap / `Toggle()`), once `IsChecked` / `IsIndeterminate` already hold the new state. `CanExecute` gates the command, not the toggle. Not run for code / binding changes. |
| `CommandParameter` | `object?` | `null` | Passed to `Command`. |
| `ReserveTouchTarget` | `bool` | `true` | `true`: the box sits in a 48dp touch slot. `false`: the control is exactly the 20dp box (+ label) — use it for a box **inside a row that owns the tap** (`InputTransparent="True"`), where the slot only inflated every row. No press halo in that mode; the row gives the feedback. |

`IsEnabled` and `FlowDirection` are the inherited `VisualElement` ones and are fully honoured.
There are no colour overrides — like `G9Switch`, the box takes its colours from `G9Palette`
(see *Colours*); retune the palette, or the one recipe in `G9Colors`, instead.

## Events and methods

| Member | Description |
|---|---|
| `event EventHandler<CheckedChangedEventArgs>? CheckedChanged` | Raised whenever `IsChecked` changes, with the new value in `e.Value`. The same argument type as the platform `CheckBox`, so an existing handler moves over unchanged. **Not** raised by an `IsIndeterminate`-only change. |
| `void Toggle()` | Exactly what a tap does (indeterminate → checked, otherwise flip, then `Command`). Ignored while disabled. For callers that stand in for the user — keyboard handling, UI automation. To set a state from code, assign the properties instead. |

## Usage

```xml
<g9:G9CheckBox Text="Include archived observations"
               IsChecked="{Binding IncludeArchived}" />

<!-- bare box, e.g. in a table cell -->
<g9:G9CheckBox IsChecked="{Binding IsSelected}" />

<!-- tri-state "select all" -->
<g9:G9CheckBox Text="All sites"
               IsChecked="{Binding AllSelected}"
               IsIndeterminate="{Binding SomeSelected}"
               Command="{Binding ToggleAllCommand}" />
```

```csharp
var agree = new G9CheckBox { Text = "I have read the terms" };
agree.CheckedChanged += (_, e) => submitButton.IsEnabled = e.Value;
```

The Gallery's **Inputs** page has every state, a bare-box row, a working tri-state "select all" and
an RTL block (including a wrapped label).

## Behaviour notes

### Hit target

The square is 20dp; the control is never shorter than `G9LayoutMetrics.MinTouchTarget` (48dp). The
box is centred in a 48 × 48 slot, so the slop is invisible (`08-UI-UX-Design-System.md` §9b,
`G9Controls.md` §10b). **Except with `ReserveTouchTarget="False"`** — a box inside a tappable row
(the list-picker shape): the ROW is the target there, and the 48dp slot plus the row's own padding made
each row ~72dp tall with a dead gap before the label (rejected on the first device look, 2026-09-27).

- **With `Text`** the row fills its parent's width and the **whole row** — box, label, the space
  between them — is one tap target.
- **Without `Text`** the control is just its 48dp slot (`HorizontalOptions = Start` on the inner
  row), so a bare box does not turn the full width of its parent into a tap target.
- Every child is `InputTransparent`; the one gesture owner is the inner row grid. Press the EDGES of
  the row when you verify a placement.

### Direction

The box sits on the **start** side and the label follows it, by ordinary `FlowDirection`
inheritance — in RTL the box is on the right. Set `FlowDirection` on the control or any ancestor
(the popup input form does this per field). The canvas is pinned `LeftToRight`, and the drawable
does no direction math at all: the square and the halo are symmetric, and **the tick must never
mirror** — it is a glyph, not a layout (`G9Controls.md` §9, the `G9Switch` backwards-tick bug).

### Animation

- **Check / uncheck:** one timeline (`G9Metrics.CheckBoxToggleDurationMs` = 200, `CubicOut`). The
  fill pops in over the first 40% (it grows from 78% of the box while fading in) and the tick draws
  itself in — trimmed by LENGTH, so the pen moves at a constant speed round the elbow — over the last
  75%, i.e. ~150ms of stroke. Unchecking runs the same timeline backwards.
- **Checked ↔ indeterminate:** the tick and the bar share three points; the points interpolate
  (`CheckBoxMorphDurationMs` = 150, `CubicInOut`) so one bends into the other while the box stays
  filled. Unchecked → indeterminate draws the bar in directly (no tick first).
- **Press halo:** a `Primary` disc at 14% alpha, 40dp, that spreads out from 70% of its size as it
  fades in on press (`PressDurationMs`) and fades on release (`ReleaseDurationMs`). Where a platform
  delivers no pointer press for a tap, the tap flashes it instead. A press whose release never
  arrives (a `ScrollView` took the gesture) lets go by itself after 1.2 s.
- Every animation starts from where its value IS, with a duration scaled to the distance left — a
  second tap turns a half-drawn tick around instead of restarting it. Nothing allocates per frame.
- The first paint (and anything set before it) snaps; so does a change while the control has no
  handler. Detaching (page closed, tab switched) aborts animations and drops a held halo.

### Colours

All read from `G9Palette.Current` on every paint, so a theme switch is a repaint
(`OnPaletteChanged` does not run a full visual pass).

| Part | Enabled | Disabled |
|---|---|---|
| Unchecked outline | `Outline`, 2dp | `Outline` at 55% alpha |
| Checked / indeterminate fill | `Primary` | `TextDisabled` |
| Tick / bar | `G9Colors.CheckBoxMark(palette)` = `OnPrimary` | `Surface` |
| Halo | `Primary` at `G9Colors.CheckBoxHaloAlpha` (0.14) | none |
| Label | `TextPrimary` | `TextDisabled` |

`OnPrimary` is white in the stock light palette (the design's "white tick"). In the stock **dark**
palette it is a deep green — the M3 dark look. If the product wants a white tick in dark mode as well,
`G9Colors.CheckBoxMark` is the single line to change. No shadow anywhere (`G9Controls.md` §0).

### Accessibility

`SemanticProperties.Description` = `Text`; `SemanticProperties.Hint` = the state, always announced:
`G9StringKey.Checked` / `NotChecked` / `PartiallyChecked` ("checked" / "not checked" /
"partially checked"; translate them like every other `G9StringKey`). A consumer-set description or
hint is never overwritten (`G9ControlBase.ApplySemantics`). A bare box has no name — give it a
`SemanticProperties.Description` in XAML.

### Metrics

`G9Metrics.CheckBoxSize` (= `SelectionCheckBoxSize`, 20), `CheckBoxCornerRadius`
(= `SelectionCheckRadius`, 6), `CheckBoxStrokeThickness` (2), `CheckBoxMarkStrokeThickness` (2),
`CheckBoxHaloDiameter` (40), `CheckBoxLabelSpacing` (2 → a 16dp visible gap between square and text),
`CheckBoxLabelVerticalPadding` (12), `CheckBoxLabelFontSize` (14), `CheckBoxToggleDurationMs` (200),
`CheckBoxMorphDurationMs` (150).

### Code layout

| File | Role |
|---|---|
| `G9CheckBox.cs` | Bindables, gestures, animations, semantics. |
| `G9CheckBoxDrawable.cs` | Paint only — reads three floats and the palette. |
| `G9CheckBoxMath.cs` | The timeline split, the tap rule and the tick / bar geometry, with NO MAUI dependency; file-linked into `tests/G9MAUIControls.Tests` (`G9CheckBoxMathTests`). |

### Used by

- `G9PopupHelper` input forms: a `G9PopupInputField.CheckBox(...)` field renders one `G9CheckBox` per
  option (it rendered `G9Switch` form rows before 1.2.0). Values, validation and the field's
  `FlowDirection` are unchanged.

### UI automation

`G9CheckBox` is a `ContentView`, not a platform `CheckBox`, and its tap recognizer sits on the inner
row. An automation bridge should special-case the type: actuate with `Toggle()` (respects
`IsEnabled` and runs `Command`, like a real tap) and read the state from `IsIndeterminate` /
`IsChecked`.
