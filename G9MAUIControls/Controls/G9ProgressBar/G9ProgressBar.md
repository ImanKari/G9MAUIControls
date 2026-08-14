# G9ProgressBar

`G9ProgressBar` is the linear progress indicator. Painting lives in
`G9ProgressBarDrawable.cs`.

## When to use

- Determinate or indeterminate linear progress -> `G9ProgressBar`.
- A circular/spinner busy indicator -> `G9ActivityIndicator` (no G9 equivalent yet).

## Bindable Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Value` | `double` | `0` | 0–1. Auto-clamped. Drives the fill width. |
| `TrackColor` | `Color?` | `null` | Track color. Defaults to `SurfaceVariant`. |
| `ProgressColor` | `Color?` | `null` | Fill color. Defaults to the color resolved from `ProgressType`. |
| `CornerRadius` | `double` | `999` | Corner radius (`Pill` by default). |
| `BarHeight` | `double` | `10` | Bar height in dp. |
| `LabelPlacement` | `G9ProgressLabelPlacement` | `None` | `None` / `Above` / `End` — controls where the percent label renders. |
| `IsIndeterminate` | `bool` | `false` | Sliding "knight rider" indicator. Ignores `Value`. |
| `ShowSegments` | `bool` | `false` | Paints 4 segment dots over the bar. |
| `ProgressType` | `G9ProgressType` | `Primary` | `Primary` / `Success` / `Warning` / `Error` — picks the default fill color from the palette. |
| `IsPaused` | `bool` | `false` | Stops the indeterminate ticker and the diagonal stripe overlay. |

## Usage

### Determinate

```xml
<newControls:G9ProgressBar Value="0.64" ProgressType="Primary" />
```

### Determinate with percent label above

```xml
<newControls:G9ProgressBar
    Value="{Binding ProgressFraction}"
    LabelPlacement="Above"
    ProgressType="Success" />
```

### Indeterminate

```xml
<newControls:G9ProgressBar IsIndeterminate="True" ProgressType="Primary" />
```

### Paused / completed

```xml
<newControls:G9ProgressBar
    Value="0.22"
    ProgressType="Error"
    IsPaused="True" />
```

### Segmented

```xml
<newControls:G9ProgressBar
    Value="0.42"
    ShowSegments="True"
    BarHeight="14"
    ProgressType="Warning" />
```

## Behaviour Notes

- Determinate value transitions are animated over 300ms with `CubicOut` easing. Setting
  `Value` from a binding produces a smooth fill animation, not a snap.
- Indeterminate mode uses a `Dispatcher.StartTimer` ticker at 16ms frame intervals. The
  ticker exits when `IsIndeterminate` flips back to `false` or when the control unloads.
- The diagonal "stripe" overlay only paints when `IsPaused == false` and `IsEnabled == true`.
- `Value` is clamped to `[0, 1]` inside `OnValueChanged` — the visual is always sane,
  even if a binding pushes a value outside the range.
- The percent label color always matches `ProgressColor` (or the resolved
  `ProgressType` color), so completed progress automatically reads as the expected
  semantic color.
