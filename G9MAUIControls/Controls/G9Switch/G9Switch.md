# G9Switch

`G9Switch` is the animated on/off toggle. Drawing is handled by `G9SwitchDrawable`,
which paints the track + a "drop-shape" thumb that morphs (stretches horizontally
mid-flight) during the toggle.

## When to use

- Boolean on/off toggle (a single setting) -> `G9Switch`.
- Multi-select list of options (former checkboxes) -> a `G9Switch` per option,
  with `IsInFormRow="True"`.
- Single-select group (former radio buttons) -> a `G9Switch` per option sharing one
  `SelectionGroup` key (mutually exclusive via the selection-group registry); or, for a
  picker-style field, `G9Picker` (see `G9Picker.md`).

## Bindable Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `IsOn` | `bool` | `false` | Two-way bindable. The current state. |
| `IsRequired` | `bool` | `false` | When true, the switch can be turned on but not off (tap shows a shake). |
| `SelectionGroup` | `string?` | `null` | Switches sharing this string are mutually exclusive (turning one on turns the others off). |
| `IsInFormRow` | `bool` | `false` | Switches to a "form row" layout: title + description + trailing toggle. |
| `Title` | `string?` | `null` | Form-row title text. |
| `Description` | `string?` | `null` | Form-row description text below the title. |

## Events

- `Toggled` — fires after `IsOn` changes.

## Layouts

### Compact (default)

```xml
<newControls:G9Switch IsOn="{Binding NotificationsEnabled, Mode=TwoWay}" />
```

Renders just the 56×32 switch widget at center.

### Form row

```xml
<newControls:G9Switch
    IsInFormRow="True"
    Title="Push notifications"
    Description="Receive daily reminders"
    IsOn="{Binding NotificationsEnabled, Mode=TwoWay}" />
```

Renders the title + description on the start side and the switch on the trailing side
inside a 16×12 padded row. RTL automatically flips the switch to the visual left.

## Required Mode

A required switch can be turned on but cannot be turned off — a tap on a required-on
switch plays a brief shake animation instead. Use this for mandatory settings:

```xml
<newControls:G9Switch
    IsInFormRow="True"
    Title="Accept terms"
    Description="You must accept to continue"
    IsRequired="True"
    IsOn="True" />
```

## Selection Group (single-selection radio behaviour)

Switches sharing the same `SelectionGroup` string behave like radios — turning one on
automatically turns the others off:

```xml
<newControls:G9SwitchGroup MinOneActive="True" ShowLiveStatus="True">
    <newControls:G9Switch IsInFormRow="True" Title="Soil" SelectionGroup="aspect" IsOn="True" />
    <newControls:G9Switch IsInFormRow="True" Title="Water" SelectionGroup="aspect" />
    <newControls:G9Switch IsInFormRow="True" Title="Weather" SelectionGroup="aspect" />
</newControls:G9SwitchGroup>
```

`G9SwitchGroup` (a `VerticalStackLayout` subclass) provides:

| Property | Type | Default | Description |
|---|---|---|---|
| `MinOneActive` | `bool` | `false` | Prevents the user from turning off the last active switch in the group. |
| `ShowLiveStatus` | `bool` | `false` | Appends a `n / total` count label that updates live. |

## Behaviour Notes

- The thumb morph runs as a single MAUI `Animation` writing the drawable's `Progress`.
  The morph factor peaks at progress=0.5 (mid-flight), stretching the thumb to ~1.4× its
  resting width like a drop, then settling back to a circle on the opposite side.
- The track color also interpolates smoothly between off and on tints; the track-mark
  glyph cross-fades from a small dark dot (off) to a white check (on).
- `OnApplyVisuals` only seeds `Progress` on the very first apply (`_initialized` flag).
  Subsequent visual refreshes (e.g., theme change) never reset the running animation.
  This keeps the morph smooth even if the theme changes mid-toggle.
- The `G9SwitchGroupRegistry` static holds weak references to every switch with a
  non-empty `SelectionGroup`. Switches register themselves on `OnApplyVisuals` and are
  cleaned up automatically when their `WeakReference` is no longer reachable.
- RTL: when the culture is RTL, the off / on positions of the thumb swap (off = right edge,
  on = left edge) and the track mark moves to the opposite (filled) end, matching reading
  direction. **All of that is decided by `G9SwitchDrawable.IsRtl` — the drawable is the
  SINGLE source of direction**, which is why the host `GraphicsView` pins
  `FlowDirection = LeftToRight` (the same thing `G9RangeSlider` does, see
  `G9Controls.md` §9).
  - **Why the pin is load-bearing:** an INHERITED RTL flow direction makes the platform mirror
    the whole canvas. Combined with the drawable's own RTL math that is a DOUBLE flip — the
    thumb lands back on the LTR side, and, worse, the on-state check is painted as a
    **backwards tick**. A check mark is a glyph, not a layout: Material never mirrors `check`,
    so it must read identically in both cultures. Do not remove the pin, and do not "fix" a
    mirrored tick by flipping the stroke coordinates in the drawable.
