# G9ComboBox

`G9ComboBox` is the outlined trigger box that opens a search-enabled bottom-sheet
picker. Single-selection mode shows the chosen item's text inside the box;
multi-selection mode shows compact selected chips plus a neutral "+N" overflow chip.

## When to use

- Selection from a long / searchable list, or multi-selection -> `G9ComboBox`.
- Short single-selection list with no search -> `G9Picker` (see `G9Picker.md`).

Binds `ObservableCollection<G9SelectionItem>` + the selection — project domain options
onto `G9SelectionItem { Text, Key, Value }`.

## Bindable Properties

### Inherited from `G9OutlinedFieldBase`

See `../G9TextEntry/G9TextEntry.md` for the full inherited list.

### Specific to `G9ComboBox`

| Property | Type | Default | Description |
|---|---|---|---|
| `ItemsSource` | `ObservableCollection<G9SelectionItem>?` | `null` | The source list. |
| `SelectedItem` | `G9SelectionItem?` | `null` | Two-way bindable single-selection. |
| `SelectedItems` | `ObservableCollection<G9SelectionItem>?` | `[]` | Two-way bindable multi-selection collection. |
| `AllowMultipleSelection` | `bool` | `false` | Switches between single- and multi-selection modes. |
| `SheetTitle` | `string?` | `null` | Title at the top of the bottom sheet. |
| `EmptyStateText` | `string?` | `"No results"` | Text shown when the search filter returns nothing. |
| `ClearButton` | `bool` | `true` | When true and a value is selected, the trailing icon becomes a × that clears the selection. |
| `ValueTextDirection` | `G9TextInputDirection` | `MatchParent` | Direction for the selected value / chip strip and combo sheet item rows. Use `LeftToRight` for codes or Latin values inside an RTL page. Placeholder, floating label, sheet title, and search field still follow the app culture. |

## Bottom-sheet Layout

- **Header** — the SHARED bottom-sheet header (back arrow + title, `NearBack`) for BOTH single-
  and multi-select (2026-07: the old inline multi-select header was removed). In multi-select the
  header's **trailing slot** carries the live selected-count pill + Done button (built in
  `G9SelectionSheet.ShowAsync`, handed to `HeaderTrailingView`). The count badge is a rounded
  12dp pill with `PrimaryContainer` background and `OnPrimaryContainer` text that hides when the
  count is 0.
- **Search field** — `G9TextEntry` with a leading search icon. Filters the list as the
  user types.
- **List** — Scrollable list of `G9SelectionItem` rows. Each row is **flat and borderless**
  (no card frame), `SelectionRowHeight` (52dp) tall with `SelectionRowHorizontalPadding` (16dp)
  horizontal content inset; rows are full-bleed and separated by nothing (no divider):
    - Unselected: transparent — the sheet `Surface` shows through.
    - Selected: a subtle primary-tint fill (**no stroke/border**), `Primary` text in bold,
      `Primary` leading icon, `Primary` trailing checkmark.
    - A tap plays a subtle press flash (a low-alpha `OnSurface` wash) as the pressed cue.
- **Done button** (multi-select) — Confirms the current selection and closes.

### No-flicker selection toggling

In multi-select mode, tapping a row updates ONLY that row's visuals (background tint,
text, icon, trailing check opacity) instead of rebuilding the whole list. The trailing
checkmark is always present in the visual tree with `Opacity = 0` for unselected rows
and `Opacity = 1` for selected rows. Toggling any row never causes the sibling rows to
re-render. This eliminates the "all checks blink" visual we had before. Search-driven
filtering still rebuilds the list (rows actually need to come and go).

## Usage

### Single combo with search

```xml
<newControls:G9ComboBox
    Label="Crop"
    Placeholder="Search crop"
    SheetTitle="Crops"
    ItemsSource="{Binding Crops}"
    SelectedItem="{Binding SelectedCrop, Mode=TwoWay}" />
```

### Multi combo

```xml
<newControls:G9ComboBox
    Label="Categories"
    Placeholder="Pick categories"
    SheetTitle="Categories"
    AllowMultipleSelection="True"
    ItemsSource="{Binding AllCategories}"
    SelectedItems="{Binding SelectedCategories}" />
```

### LTR selected value inside RTL UI

```xml
<newControls:G9ComboBox
    Label="Code"
    Placeholder="Search code"
    SheetTitle="Codes"
    ValueTextDirection="LeftToRight"
    ItemsSource="{Binding Codes}"
    SelectedItem="{Binding SelectedCode, Mode=TwoWay}" />
```

### Read-only combo

```xml
<newControls:G9ComboBox
    Label="Locked combo"
    Placeholder="Locked"
    IsReadOnly="True"
    ItemsSource="{Binding AllCategories}" />
```

### No clear button

```xml
<newControls:G9ComboBox
    Label="Required"
    ClearButton="False"
    ItemsSource="{Binding Items}"
    SelectedItem="{Binding Required}" />
```

## Behaviour Notes

- Multi-select mode shows up to 2 selected items as compact selected chips inside the
  box: primary gradient, lower 6dp radius, bold on-primary text, optional item icon,
  and the same flat selected-chip recipe used by `G9ChipGroup` (gradient + stroke, no
  shadow — see `../G9Controls.md` §0). These inline
  chips intentionally do not show a trailing checkmark; they only render icon + text.
  If more than 2 items are selected, the third chip is a neutral `+N` overflow count.
  Chips use `VerticalOptions = Center` so they sit on the field center line — no
  bottom-margin drift.
- In RTL culture, the selected single value and multi-select chip strip start on the
  physical-right by default. Set `ValueTextDirection="LeftToRight"` when the selected
  value is a code, SKU, URL fragment, or other Latin/LTR token that should stay on the
  physical-left. The combo sheet applies the same override to item rows only; header,
  search, placeholder, floating label, and title remain culture-directed.
- The trailing icon is `MaterialIcons.Search` by default; when `ClearButton` is true and
  the field has a value it switches to `MaterialIcons.Close`. Tapping × clears the
  selection.
- The combo unfocuses the parent before opening the sheet so the page doesn't
  auto-scroll to the combo when the sheet captures focus.
- The bottom sheet uses `G9SelectionSheet` which is shared with `G9Picker`. The same
  sheet supports search, single / multi mode, empty state, and the close-on-select rule.
- **Deferred content rendering** — `ShowAsync` opens the sheet host with a centered
  spinner FIRST and runs the heavy view-tree construction (rows, icons, search field,
  header) only AFTER the open animation has played. The user sees the sheet animate
  in immediately and the content materializes a frame or two later. Without this, the
  sheet had a visible 1–3 second tap-to-open lag because every row was constructed
  synchronously on the UI thread before the open animation could start. Measured on
  emulator: tap → ShowAsync return ~80ms → factory invoked +492ms (after open
  animation) → factory done +24ms = ~516ms total perceived time.
- Tapping the × clear icon inside the combo's trigger does NOT open the sheet — the
  built-in handler short-circuits. This is why we expose `ClearButton` and check the
  selection state inside `OnTrailingTap`.
- `SelectedItems` is a two-way bindable `ObservableCollection`. The combo reattaches its
  internal collection-changed handler when the bound collection instance changes, so
  swapping the entire collection in the view model behaves correctly.
- **Single-select value icon (2026-07).** In single-selection mode, the trigger box now shows the
  selected `G9SelectionItem`'s icon (Emoji/MaterialIcon/IconPath/IconSource) or a variety colour
  swatch fallback (`SwatchFirstColor`/`SwatchSecondColor`, for items like a variety that only carry
  stored colors, no real icon image) leading the value text — same rendering `CreateSelectedChip`
  already used for multi-select chips, now shared via `CreateValueIcon`. The icon color defaults to
  neutral `OnSurfaceVariant`; set `G9SelectionItem.IconTintColor` for a semantic tint (e.g. a
  status option colored green/orange/red — see `AiGuides/12-Tree-Pot-Map-Operations.md`).
- **Variety colour swatch — ONE convention everywhere (2026-07).** `G9Visuals.CreateSwatch` /
  `HasSwatch` render a variety's stored colours as **0 colours → nothing, 1 colour → solid circle,
  2 colours → circle split into two halves (first | second)** (previously only the 2-colour case
  drew anything). This is shown in the combo/picker **selected value** AND now in the **dropdown list
  rows** (`G9SelectionSheet` draws the swatch when an item has `SwatchFirstColor/SecondColor` and
  no icon). The SAME `CreateSwatch` is wrapped by the reusable `Common/Components/MinimalComponents/
  VarietySwatchView` control for the non-G9 surfaces that also show a variety: the Sampling
  variety picker (`VarietySelectionContentView`, which additionally shows the crop **product icon**),
  the sampling selected-variety field (`NewSampleContentView`), and the read-only variety displays
  (`TreeInfoCardView` — used by the tree-detail sheet AND the sampling-target card — and
  `BlockInfoContentView`'s cultivar rows). Bind `VarietySwatchView.FirstColor/SecondColor` to the
  variety's parsed `Color?`s. The map draws the same two-colour split as a pot symbol — see
  `AiGuides/12-Tree-Pot-Map-Operations.md`.
- **RTL selected-value alignment bug (fixed 2026-07).** `CreateValueLabel`'s `HorizontalTextAlignment`
  was hardcoded to `TextAlignment.Start` regardless of culture — `FlowDirection` was set correctly,
  but relying on `FlowDirection` alone to flip a plain `Label`'s text alignment is NOT reliable on
  every platform. The fix mirrors `G9Picker.cs`'s existing explicit flip:
  `flow == FlowDirection.RightToLeft ? TextAlignment.End : TextAlignment.Start`. Any NEW label built
  inside this control (or `G9Picker`) must apply the same explicit flip — do not assume
  `FlowDirection` alone reorients text.
