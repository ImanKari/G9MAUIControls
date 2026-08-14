# G9Picker

`G9Picker` is the outlined trigger box that opens a single-selection bottom-sheet
picker. It inherits the shared outline architecture from `G9OutlinedFieldBase`, so the
floating label, focus ring, and icon padding all match the other input controls.

## When to use

- Single-selection from a fixed list, shown as a labelled field box -> `G9Picker`.
- Search-enabled and/or multi-selection -> `G9ComboBox` (see `G9ComboBox.md`).
- Date / time selection -> `G9DateTimePicker` (see `G9DateTimePicker.md`).
- Trigger-only UI (icon, `G9NavCard`, custom button) with no field box -> call
  `G9SelectionSheet.ShowAsync(...)` directly from the command instead of hosting a
  hidden picker.

Binds `ObservableCollection<G9SelectionItem>` + a `G9SelectionItem` selection —
project domain options onto `G9SelectionItem { Text, Key, Value }`.

## Bindable Properties

### Inherited from `G9OutlinedFieldBase`

See `../G9TextEntry/G9TextEntry.md` for the full inherited list.

### Specific to `G9Picker`

| Property | Type | Default | Description |
|---|---|---|---|
| `ItemsSource` | `ObservableCollection<G9SelectionItem>?` | `null` | The source list shown in the bottom sheet. |
| `SelectedItem` | `G9SelectionItem?` | `null` | Two-way bindable. The currently selected item. |
| `SheetTitle` | `string?` | `null` | Title shown at the top of the bottom sheet. Falls back to `Label` / `Placeholder`. |
| `RestoreOnCancel` | `bool` | `true` | Reverts to the previous selection when the user dismisses the sheet without choosing. |
| `SelectionAcceptedCommand` | `ICommand?` | `null` | Command executed when the user chooses an item. |
| `SelectionCancelledCommand` | `ICommand?` | `null` | Command executed when the user dismisses without selecting. |

## Events

- `ItemSelected` — fires after `SelectedItem` changes due to user choice.
- `SelectionCancelled` — fires when the sheet is dismissed without a selection.

## G9SelectionItem

The shared item model used by `G9Picker`, `G9ComboBox`, and `G9ChipGroup`:

| Property | Type | Description |
|---|---|---|
| `Text` | `string` | Display text. |
| `Key` | `string?` | Stable identity key. Falls back to `Text` if null. |
| `Emoji` | `string?` | Optional leading emoji. |
| `MaterialIcon` | `MaterialIcons?` | Optional leading material icon. |
| `IconPath` | `string?` | Optional file/uri path for an image icon. |
| `IconSource` | `ImageSource?` | Optional direct ImageSource. |
| `SwatchFirstColor` / `SwatchSecondColor` | `Color?` | Optional variety colour swatch shown in place of an icon when NONE of Emoji/MaterialIcon/IconPath/IconSource is set (e.g. a variety's stored colours). Renders per the shared convention: 0 colours → nothing, 1 → solid circle, 2 → split circle (`G9Visuals.HasSwatch` needs only ONE set). Shown in the trigger value AND the dropdown list rows. See `G9ComboBox.md` for the full swatch/`VarietySwatchView` story. |
| `IconTintColor` | `Color?` | Optional tint override for `MaterialIcon`; falls back to the field's neutral `OnSurfaceVariant` when unset. Ignored for Emoji/IconPath/IconSource icons. Use for semantic status options (e.g. health/growth — see `AiGuides/12-Tree-Pot-Map-Operations.md`). |
| `Value` | `object?` | Optional caller-provided payload. |
| `IsEnabled` | `bool` | When false, the row is dimmed and not tappable. |

`G9SelectionItem` implements `IVirtualScrollSelectionIdentity` so it integrates with
the project's existing virtualized list selection helpers.

## Usage

### Simple picker

```xml
<newControls:G9Picker
    Label="Crop"
    Placeholder="Choose crop"
    SheetTitle="Crop"
    ItemsSource="{Binding Crops}"
    SelectedItem="{Binding SelectedCrop, Mode=TwoWay}" />
```

### Code-behind setup

```csharp
public ObservableCollection<G9SelectionItem> Crops { get; } = new()
{
    new() { Key = "wheat", Text = "Wheat", Emoji = "🌾" },
    new() { Key = "rice", Text = "Rice", Emoji = "🌱" },
    new() { Key = "orchard", Text = "Orchard", Emoji = "🍎" }
};

CropPicker.ItemsSource = Crops;
CropPicker.ItemSelected += (_, item) => Debug.WriteLine($"Selected: {item?.Key}");
```

### Read-only picker (display only)

```xml
<newControls:G9Picker
    Label="Locked field"
    Placeholder="Locked"
    IsReadOnly="True"
    ItemsSource="{Binding Crops}"
    SelectedItem="{Binding LockedCrop}" />
```

### With command bindings

```xml
<newControls:G9Picker
    Label="Region"
    ItemsSource="{Binding Regions}"
    SelectionAcceptedCommand="{Binding LoadFarmsCommand}"
    SelectionCancelledCommand="{Binding ResetSelectionCommand}" />
```

## Behaviour Notes

- The trigger box is a tap target, not a focusable input — no keyboard, no underline.
- The bottom sheet shows a single-column list of `G9SelectionItem` rows. Tapping a row
  closes the sheet immediately and assigns `SelectedItem`.
- When the sheet opens with an existing selection, it scrolls the first selected row into
  view using `G9SelectionItem.SelectionIdentity` (`Key`, falling back to `Text`). Give
  domain rows stable `Key` values so selected-row reveal survives reordered/refreshed lists.
- **Deferred content rendering** — the sheet opens with a centered spinner FIRST and
  runs the heavy view-tree construction (one row per item) AFTER the open animation
  has played. The user sees the sheet animate in immediately. Without this, items
  with rich icons synchronously inflated before the sheet could appear, producing a
  visible 1–3s tap-to-open lag.
- The picker calls `this.Unfocus()` before opening the sheet so the parent ScrollView
  doesn't auto-scroll to the picker when the sheet captures focus. This eliminates the
  "page scrolls to top when I open a picker" issue.
- `RestoreOnCancel = true` (default) is the safer pick for forms — if the user dismisses
  without choosing, the selection stays as it was.
- The trailing chevron is `MaterialIcons.ExpandMore`, set on the base's
  `TrailingMaterialIcon`. To override, assign `TrailingMaterialIcon` after construction.
- **Selected-value icon (2026-07).** When `SelectedItem` carries an icon (Emoji/MaterialIcon/
  IconPath/IconSource) or, failing that, a swatch (`SwatchFirstColor`/`SwatchSecondColor`), it
  renders leading the value text inside the trigger box (18dp, `G9Visuals.CreateIcon`/
  `CreateSwatch`) — mirrors what `G9ComboBox` already did for its multi-select chips. No icon
  set → text-only, unchanged from before.
- `ItemsSource` accepts `ObservableCollection<G9SelectionItem>` — collection changes
  re-render the picker automatically via `INotifyCollectionChanged`.
