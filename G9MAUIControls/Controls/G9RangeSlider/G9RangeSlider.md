# G9RangeSlider

`G9RangeSlider` is the single- or two-thumb slider control. Painting lives in
`G9RangeSliderDrawable.cs` so the control file stays small and the drawing logic is
unit-testable in isolation.

## When to use

- A single value along a range -> `G9RangeSlider` with `Mode="Single"`.
- A min/max range with two thumbs -> `G9RangeSlider` with `Mode="Range"` (default).

## Modes

| Mode | Visible |
|---|---|
| `Single` | One thumb. Track fills from start to thumb. |
| `Range` (default) | Two thumbs. Track fills between them. |

## Bindable Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Minimum` | `double` | `0` | Lower bound. |
| `Maximum` | `double` | `100` | Upper bound. |
| `Value` | `double` | `0` | Two-way bindable. Used in `Single` mode. |
| `RangeStart` | `double` | `0` | Two-way bindable. Lower thumb in `Range` mode. |
| `RangeEnd` | `double` | `100` | Two-way bindable. Upper thumb in `Range` mode. |
| `Step` | `double` | `1` | Snap interval. `0` = continuous. |
| `Mode` | `G9RangeSliderMode` | `Range` | Single or Range. |
| `ShowLabels` | `bool` | `true` | Show min / max edge labels under the track. When `false`, the canvas height collapses to just the thumb + a small bottom gap (`SliderHeightCompact` = 36 dp) and the drag tooltip is suppressed too. Pair with an external `Label` bound to `Value` for a live readout in this mode. |
| `ValueFormat` | `string?` | `"0"` | `.NET` format string used for tooltip + edge labels. |

## Events

- `ValueChanged` — fires after `Value` (Single) or `RangeEnd` (Range) changes.

## Live value readout via binding

Every value property (`Value`, `RangeStart`, `RangeEnd`) is a two-way `BindableProperty`
with `OnChanged` plumbing, so a plain `{Binding}` updates whenever the user drags. Pair
the slider with an external `Label` to show the live value:

```xml
<Label
    Text="{Binding Source={x:Reference VolumeSlider}, Path=Value, StringFormat='Volume: {0:0}'}" />

<newControls:G9RangeSlider
    x:Name="VolumeSlider"
    Mode="Single"
    Minimum="0"
    Maximum="100"
    Value="42" />
```

For range sliders, bind a label per thumb:

```xml
<Grid ColumnDefinitions="*,*">
    <Label Grid.Column="0"
           Text="{Binding Source={x:Reference Window}, Path=RangeStart, StringFormat='Start: {0:0}'}" />
    <Label Grid.Column="1"
           Text="{Binding Source={x:Reference Window}, Path=RangeEnd, StringFormat='End: {0:0}'}" />
</Grid>

<newControls:G9RangeSlider
    x:Name="Window"
    Mode="Range"
    Minimum="0"
    Maximum="100"
    RangeStart="25"
    RangeEnd="78" />
```

The same pattern works from a view model — bind the slider to a VM property with
`TwoWay` mode and bind the label to the same property; both stay in sync.

## Usage

### Single value slider

```xml
<newControls:G9RangeSlider
    Minimum="0"
    Maximum="100"
    Value="{Binding Volume}"
    Mode="Single"
    Step="1" />
```

### Range slider

```xml
<newControls:G9RangeSlider
    Minimum="0"
    Maximum="100"
    RangeStart="{Binding RangeStart}"
    RangeEnd="{Binding RangeEnd}"
    Mode="Range" />
```

### Compact slider (no min / max labels)

```xml
<newControls:G9RangeSlider
    Mode="Single"
    Minimum="0"
    Maximum="100"
    Value="60"
    ShowLabels="False" />
```

The canvas drops to ~36 dp tall — just the thumb plus a small bottom gap — so several
compact sliders stack tightly. The drag tooltip is suppressed in this mode (no canvas
room above the thumb), so consumers should pair the compact slider with a bound
external `Label` for the live value.

### Custom value format

```xml
<newControls:G9RangeSlider
    Minimum="-20"
    Maximum="40"
    RangeStart="-5"
    RangeEnd="28"
    Mode="Range"
    Step="1"
    ValueFormat="0°" />
```

### Disabled state

```xml
<newControls:G9RangeSlider
    Mode="Range"
    Minimum="0"
    Maximum="100"
    RangeStart="20"
    RangeEnd="60"
    IsEnabled="False" />
```

## Visual Anatomy

- **Track** — 8px high, rounded, painted with `SurfaceVariant`. When `ShowLabels=true`
  the track Y center is pinned at `SliderTrackY=52` so the tooltip has room above the
  thumb. When `ShowLabels=false` the track is centered vertically on the (much
  shorter) canvas instead.
- **Fill** — between thumbs (Range) or from start to thumb (Single). Linear gradient
  from `Primary` to a 22%-lightened `Primary`.
- **Thumb** — 28px circle, `Surface` fill with `Primary` 2.5px stroke. Subtle drop
  shadow at 1px offset.
- **Active glow** — While a thumb is being dragged, a 26px circle drawn at 10% primary
  opacity wraps around it.
- **Drag tooltip** — A pill pinned to the top of the canvas shows the formatted value
  during drag (`InverseSurface` fill, `InverseOnSurface` text). Hidden when
  `ShowLabels=false` (the canvas isn't tall enough to host it without clipping the
  thumb) — use a bound external `Label` instead.
- **Edge labels** — `Min` / `Max` labels sit below the track with `SliderLabelGap`
  (18px) separation. Hidden when `ShowLabels=false`.

## Layout Tokens

| Token | Value | Meaning |
|---|---|---|
| `SliderHeight` | `104` | Canvas height when `ShowLabels=true` — accommodates drag tooltip above + track + thumb + edge labels below. |
| `SliderHeightCompact` | `36` | Canvas height when `ShowLabels=false` — just thumb (28 dp diameter) + ~6 dp bottom gap. |
| `SliderTrackY` | `52` | Vertical center of the track inside the tall canvas. The compact canvas centers the track on its own midpoint instead. |
| `SliderTrackHeight` | `8` | Track thickness. |
| `SliderThumbRadius` | `14` | Thumb radius. |
| `SliderHorizontalInset` | `16` | Padding from canvas left/right edges so thumbs at extreme positions stay fully visible. |
| `SliderLabelGap` | `18` | Vertical gap between thumb bottom and edge labels. |

## Drag-cancel-on-vertical-drift fix (Android)

The slider used to abort the drag back to its starting value when the user's finger
drifted a few pixels off the painted track — the parent `ScrollView` was claiming the
gesture as soon as the touch path tilted off horizontal. The standard Android fix
applies: when a thumb is grabbed in `OnStartInteraction` we walk every ancestor of the
platform view and call `parent.RequestDisallowInterceptTouchEvent(true)`, telling the
parent "don't claim this touch sequence". On `OnEndInteraction` / `OnCancelInteraction`
we reset the flag to `false`. Mirrors the same pattern `MapZoomIndicator.cs` and
`G9SheetViewBorder.Android.cs` already use elsewhere in the project.

iOS / Mac Catalyst / Windows don't need an explicit signal — gesture priority on
those platforms is resolved per-touch and the drag captures the gesture early.

## RTL Behaviour

- The `GraphicsView` is forced to `FlowDirection.LeftToRight` so canvas pixel
  coordinates do not mirror; we handle RTL inversion ourselves when mapping
  value → x and x → value. This is the only way to get consistent drag behaviour
  across MAUI Windows / Android / iOS — without this fix, Windows mirrors the canvas
  which combined with our manual inversion produced reversed dragging.
- Edge labels swap visual position in RTL: `Min` sits on the visual right, `Max` on
  the visual left.

## Behaviour Notes

- `NormalizeValues()` runs under a `_normalizing` flag so the snap / clamp / reorder
  logic can mutate `Value` / `RangeStart` / `RangeEnd` without re-entering the
  `OnValueChanged` handler.
- If `RangeStart > RangeEnd`, the values are swapped automatically.
- The drag tooltip only renders while the thumb is being held AND `ShowLabels=true`.
  Releasing hides it.
- Touch routing: `StartInteraction` picks the nearest thumb based on tap X. Subsequent
  `DragInteraction` events keep that thumb until `EndInteraction` /
  `CancelInteraction`.
- Per-frame cost: a single `_view.Invalidate()` call. No layout invalidation, no
  per-frame allocations.
