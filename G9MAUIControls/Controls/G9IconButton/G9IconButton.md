# G9IconButton

Compact icon-only button with async loading spinner and badge overlay. Designed for
toolbar actions (filter, sort, refresh, notifications) where a full `G9Button` with
text would be visually heavy.

## Two visual styles

| Style | Look | Use case |
|---|---|---|
| **Styled** (default) | Colored background + stroke matching `Variant` (flat — the variant drop shadow was removed 2026-07-28 with the app-wide shadow ban; see `../G9Controls.md` §0) | Primary actions in toolbars, FAB-like buttons |
| **Ghost** (`IsGhost=true`) | Transparent, no border at rest; press animation only | Secondary toolbar actions, inline row actions |

## Bindable Properties

| Property | Type | Default | Effect |
|---|---|---|---|
| `Emoji` | `string?` | null | Unicode emoji as the icon |
| `MaterialIcon` | `MaterialIcons?` | null | MauiIcons.Material enum value |
| `ImagePath` | `string?` | null | File path or URL for an image icon |
| `ImageSource` | `ImageSource?` | null | Direct ImageSource binding |
| `IconSize` | `double` | 20 | Glyph size in logical pixels |
| `ButtonSize` | `double` | 40 | Total button width/height (square) — what is DRAWN |
| `MinimumTouchTarget` | `double` | 0 (= `ButtonSize`) | Minimum TAPPABLE size, independent of the drawn button. See "Hit target". |
| `IsGhost` | `bool` | false | Ghost mode — transparent background |
| `Variant` | `G9ButtonVariant` | Surface | Color variant (same enum as G9Button) |
| `IsLoading` | `bool` | false | Replaces icon with spinner |
| `BadgeText` | `string?` | null | Text shown in the badge (e.g. "3") |
| `ShowBadgeDot` | `bool` | false | Show a small dot badge (no text) |
| `BadgeColor` | `Color?` | null (→ Error) | Badge background color |
| `MirrorBadgeTextInRtl` | `bool` | true | When true the count badge text flows RTL in RTL mode ("99+" → "+99") |
| `Command` | `ICommand?` | null | Executed on tap |
| `CommandParameter` | `object?` | null | Passed to Command |

## Hit target (`MinimumTouchTarget`)

The tap gesture is registered on the CONTROL, so **the control's measured bounds are the hit area** —
the drawn frame and the icon have nothing to do with hit-testing (the icon host, the root grid and the
badge are all `InputTransparent`). A button drawn at `ToolbarButtonSize` (42) therefore has a 42dp
target: under the 44dp accessibility floor (design guide §10), and a tap that lands a few dp outside
falls through to whatever is behind. That is what "the button sometimes doesn't work" looks like — it
was reported on the map multi-selection close (X).

`MinimumTouchTarget` grows the control's measured bounds **without** growing the drawn button (the
frame + badge stay centred inside), so the extra area is invisible slop:

```xml
<buttons:G9SafeIconButton
    ButtonSize="{DynamicResource ToolbarButtonSize}"      <!-- drawn: 42 -->
    MinimumTouchTarget="{DynamicResource MinTouchTarget}" <!-- tappable: 48 -->
    IsCircular="True"
    MaterialIcon="Close" />
```

Default `0` = hit target equals `ButtonSize`, so the deliberately tiny inline buttons (24–32dp chips
over photo thumbnails) are unaffected. Never enlarge `ButtonSize` to fix a missed tap.

## Events

| Event | Signature |
|---|---|
| `Clicked` | `EventHandler` |

## Loading pattern

```csharp
// In ViewModel or code-behind:
filterButton.IsLoading = true;
try
{
    await ApplyFiltersAsync();
}
finally
{
    filterButton.IsLoading = false;
}
```

While `IsLoading = true`:
- Icon is hidden, spinner is shown (same size)
- Button is not tappable (tap is ignored)
- Opacity drops to 70%
- Button size stays constant (no layout jump)

## Badge

- `ShowBadgeDot = true` → small 12px dot in the top-trailing corner.
- `BadgeText = "3"` → 18px pill badge with text; width auto-grows for multi-char counts.
- `BadgeColor` overrides the badge background (default = Error red).
- **The badge is the shared `G9CornerBadge`** (see `G9Controls.md` §12b) — the same
  helper `G9NavCard` uses. Its centre sits exactly on the button frame's top-trailing
  corner (half over the frame / half outside) for any count width, the number is centred
  in the circle, it is clip-safe on Android (no negative-margin overflow), and it
  auto-mirrors to the top-leading corner in RTL with the count text flipping ("99+" →
  "+99") when `MirrorBadgeTextInRtl` is true (default). **Change badge appearance/geometry
  in `G9CornerBadge`, not here.**

## Press animation

Scale(0.82) → Scale(1.0) with CubicIn/CubicOut easing — deeper press than G9Button
because icon-only buttons are small and need a more pronounced tactile response.

## Usage examples

```xml
<!-- Ghost filter button with active dot badge -->
<new:G9IconButton
    MaterialIcon="{x:Static icons:MaterialIcons.FilterList}"
    IsGhost="True"
    ShowBadgeDot="True"
    Command="{Binding OpenFiltersCommand}" />

<!-- Styled refresh button with loading -->
<new:G9IconButton
    MaterialIcon="{x:Static icons:MaterialIcons.Refresh}"
    Variant="Primary"
    IsLoading="{Binding IsRefreshing}"
    Command="{Binding RefreshCommand}" />

<!-- Notification bell with count badge -->
<new:G9IconButton
    MaterialIcon="{x:Static icons:MaterialIcons.Notifications}"
    Variant="Surface"
    BadgeText="{Binding UnreadCount}"
    BadgeColor="{StaticResource ErrorColor}"
    Command="{Binding ShowNotificationsCommand}" />
```

## RTL behaviour

- The badge auto-mirrors with the layout: it docks to the top-trailing corner —
  physical-right in LTR, physical-left in RTL — via `HorizontalOptions = End` on the badge
  Border inside a wrapper that inherits the ambient `FlowDirection` (no hard LTR lock).
- Icon is centered — no directional dependency.
