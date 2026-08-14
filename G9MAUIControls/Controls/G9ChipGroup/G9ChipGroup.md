# G9ChipGroup

`G9ChipGroup` is the wrapping chip group with single- or multi-selection. Selected
chips paint with the primary gradient + a primary-tinted stroke per the G9 design recipe
(flat — the app carries no shadows; see `../G9Controls.md` §0).

## When to use

- Inline, always-visible choice chips (filters, quick toggles) -> `G9ChipGroup`.
- A field-style trigger that opens a sheet for selection -> `G9ComboBox` /
  `G9Picker` (see their guides).

Binds `ObservableCollection<G9SelectionItem>` — project domain options onto
`G9SelectionItem { Text, Key, Value }`.

## Per-item selected color

Each `G9SelectionItem` may set an optional `SelectedColor` (and `SelectedTextColor`). When set,
that chip paints with its own color **while selected** instead of the group-wide `SelectedBackground`
/ `SelectedTextColor`; the **unselected** look is always the default chip style. This is what the
Tasks state-filter chips use — each state chip is selected in its own state `foregroundColorCode`
(To Do / Doing / Done). Precedence when selected: item `SelectedColor` → group `SelectedBackground`
→ theme `Primary` (and likewise for text). Leave them `null` for the uniform group behavior.

```csharp
new G9SelectionItem { Text = "To Do", Key = stateId, SelectedColor = Color.FromArgb("#c29800") };
```


## Bindable Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `ItemsSource` | `ObservableCollection<G9SelectionItem>?` | `null` | The source list. |
| `SelectedItem` | `G9SelectionItem?` | `null` | Two-way bindable. Used when `SelectionMode == SingleSelection`. |
| `SelectedItems` | `ObservableCollection<G9SelectionItem>?` | `[]` | Two-way bindable. Used when `SelectionMode == MultiSelection`. |
| `SelectionMode` | `G9ChipGroupSelectionMode` | `MultiSelection` | Single vs multi. |
| `AllowNullSelection` | `bool` | `false` | Single mode only. Allow tapping the selected chip to deselect (set `SelectedItem = null`). |
| `ItemSpacing` | `double` | `6` | Horizontal + vertical spacing between chips. Tightened from 8dp to match the Material 3 chip-group default. |
| `ChipHeight` | `double` | `36` | Height of each chip. |
| `IconSize` | `double` | `16` | Icon size inside the chip. |
| `SelectedBackground` | `Color?` | `null` | Override selected chip background. Defaults to `Primary`. |
| `SelectedTextColor` | `Color?` | `null` | Override selected chip text color. Defaults to `OnPrimary`. |
| `LayoutMode` | `G9ChipGroupLayoutMode` | `Wrap` | `Wrap` = chips flow onto more lines. `SingleLineScroll` = one line, scrolls horizontally on overflow (scroll bar hidden). See "Layout modes". |
| `ShowSelectionCheckmark` | `bool` | `true` | Whether a selected chip grows the trailing M3 checkmark. Set `false` for chips that carry their own meaningful icon. See "Selection checkmark". |
| `ChipCornerRadius` | `double` | `G9LayoutMetrics.ControlCornerRadius` (9) | Corner radius of every chip. See "Corner radius". |

## Corner radius (`ChipCornerRadius`)

Chips are **not pills**. They default to the app-wide `G9LayoutMetrics.ControlCornerRadius` (9) —
literally the same token the task cards, list cards and other rounded surfaces read — so retuning that
one token restyles the chips along with everything else, and a chip never reads as a stray shape.
(Before 2026-07 the radius was hardcoded to `G9Metrics.RadiusPill` = 999.)

```xml
<g9:G9ChipGroup ChipCornerRadius="999" ... />   <!-- opt back into the fully-rounded pill -->
```

Changing it reshapes the live chips in place (pure geometry — no rebuild, no animation restart).

## Events

- `SelectionChanged` — fires after the selection set changes. Provides the resolved
  read-only selection list.

## Usage

### Multi-selection

```xml
<newControls:G9ChipGroup
    ItemsSource="{Binding Aspects}"
    SelectedItems="{Binding SelectedAspects}"
    SelectionMode="MultiSelection" />
```

### Single-selection (radio behaviour)

```xml
<newControls:G9ChipGroup
    ItemsSource="{Binding Periods}"
    SelectedItem="{Binding SelectedPeriod}"
    SelectionMode="SingleSelection"
    AllowNullSelection="False" />
```

### Custom selected color (e.g. orange brand)

```xml
<newControls:G9ChipGroup
    ItemsSource="{Binding Topics}"
    SelectionMode="MultiSelection"
    SelectedBackground="#D97706"
    SelectedTextColor="White" />
```

## Layout modes (`LayoutMode`)

| Mode | Host | Use it when |
|---|---|---|
| `Wrap` (default) | wrapping `FlexLayout` | The group may grow taller without hurting anything (inside a sheet / form section). |
| `SingleLineScroll` | `HorizontalStackLayout` inside a horizontal `ScrollView`, both scroll bars `Never` | The group's height must be constant — a filter strip pinned above a list. On a narrow phone the chips scroll instead of wrapping onto a second line and pushing the content below down. `TasksPageContentView`'s state filters are the reference. |

```xml
<g9:G9ChipGroup
    ItemsSource="{Binding StateFilterChips}"
    LayoutMode="SingleLineScroll"
    SelectionMode="MultiSelection" />
```

- **`SingleLineScroll` must NOT host the wrapping `FlexLayout`.** A wrapping `FlexLayout` inside a
  horizontal `ScrollView` measures against an infinite width and degenerates — every chip lands on its
  own row. That exact bug is why `Common/Components/GroupItems/ScrollableChipGroup` deleted its
  `ScrollView` and (despite its name) does not scroll. `SingleLineScroll` therefore uses a
  `HorizontalStackLayout`, which has no wrap to degenerate. Do not "simplify" it back to one host.
- Chips keep their trailing/bottom margin in both modes, so the strip's geometry is identical
  (the line host's own `Spacing` is 0 for that reason).
- A layout-mode switch re-parents every chip, so it rebuilds the strip (a chip can only live in one
  layout at a time).

## Selection checkmark (`ShowSelectionCheckmark`)

Default `true` — the M3 trailing check (below). Set it to `false` for chips that already carry their
own meaningful icon: the check is then a second glyph competing with the chip's identity icon, and its
0 → `IconSize` width animation reflows the chip on every tap. With it off the widgets are never built,
the chip's width is constant across selection, and selection reads through the background / stroke /
text crossfade. The Tasks state-filter chips (hourglass / in-progress / done) use `false`.

> **Accessibility caveat (design guide §10 — "never signal state by color alone"):** with the check
> disabled, selection is carried by fill color. Only turn it off where the chip has its own icon AND
> the selected fill is a strong, per-item color (as the task-state chips do); do not disable it on a
> plain text chip.

## Selection Animation

The chip group implements the **Material 3 filter-chip motion**: when a chip becomes
selected, a trailing checkmark slides in from 0-width with a synchronised opacity and
scale fade-in, while the chip's background, border, text, and icon colors crossfade in
lockstep. When a chip is deselected, the checkmark collapses back to 0-width and fades
out. The label naturally slides to make room for the check as the row reflows.

The check sits at the **trailing** edge (after the chip's icon and label) so it
never collides visually with chips that carry their own leading icon (e.g. weather
sun, water drop). An earlier version put the check at the leading edge — that
stacked two icons at the start of selected chips, making it unclear which was the
chip's identity icon vs the selection state.

### Motion timing

| Token | Value | Role |
|---|---|---|
| `ChipSelectionAnimationMs` | `140` | Total color crossfade duration. Curve: `CubicInOut`. |
| `ChipCheckmarkAnimationMs` | `120` | Sub-window inside the master timer for the checkmark width / opacity / scale. |

The checkmark finishes ~20ms before the colors settle so the icon "arrives" first,
signalling the selection, then the colors complete to reinforce — matches the M3 spec
recipe and feels responsive without being abrupt.

### Architecture (destruction-free)

Earlier versions destroyed the icon `View` on the final animation frame and swapped
the chip's brush type from `SolidColorBrush` (during animation) → `LinearGradientBrush`
(at end), which produced a 1-frame "default-color flash" on the icon and a brush-type
pop at t=1. The current approach:

- **Cell widgets are built once** in `BuildChip`. The icon `View`, the text `Label`,
  the checkmark host + icon, and the `Border` are never recreated — value updates
  happen by mutating properties on existing instances.
- **Stable brush instances**. Each chip owns a `LinearGradientBrush` with three
  `GradientStop`s (top, mid, bottom) for the background and a `SolidColorBrush` for
  the stroke. We mutate `GradientStop.Color` and `SolidColorBrush.Color` per animation
  frame instead of allocating new brushes. The brush type stays the same throughout —
  no t=1 pop.
- **No shadow to animate.** The selected-state drop shadow was removed 2026-07-28 with the
  app-wide shadow ban (`../G9Controls.md` §0); selection is carried by the background
  gradient, the stroke tint, the text colour and the checkmark.
- **Checkmark widget driven via three properties** — `WidthRequest` (0 → IconSize
  for layout reflow), `Opacity` (0 → 1 for fade), `Scale` (0.5 → 1 anchored at the
  trailing edge for an "extruding from the chip's end" feel). All three driven by a
  single `checkProgress` value (0..1) computed inside the master animation callback
  so they stay perfectly in sync.
- **Live progress tracking** on each binding (`CurrentBg`, `CurrentStroke`,
  `CurrentText`, `CurrentCheckProgress`). When a new
  animation starts mid-flight (rapid double-tap), it reads `Current*` as the "from"
  — no jump back to the resting state before re-interpolating.
- **Cancellation-safe `finished` callback** — checks the `cancelled` flag so the old
  animation's final frame doesn't overwrite the newer animation's in-flight frames.

A `ScaleTo(0.94, 80, CubicIn)` press pulse plays alongside the color/check animation
on tap, then springs back to `1.0` over 140ms.

Only the toggled chip animates — every other chip stays untouched because
`ApplySelectionState` short-circuits when the binding's `IsSelected` matches the new
value.

## Behaviour Notes

- The chip group caches a `binding` per item so it can diff state changes. When
  `SelectedItem` / `SelectedItems` changes, only chips whose `IsSelected` differs from
  the cached value are repainted — other chips are left alone.
- Chip layout never resizes between selected and unselected states — same width, same
  padding, same icon size. Only colors change. This eliminates layout reflow
  on selection.
- `ItemsSource` accepts `ObservableCollection<G9SelectionItem>` — collection changes
  rebuild the chip strip. The cached bindings are cleared and rebuilt on full rebuild.
- For the icon priority order, see `G9Button.md`.
