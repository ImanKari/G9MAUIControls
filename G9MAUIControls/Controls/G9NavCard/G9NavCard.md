# G9NavCard

`G9NavCard` is the Material-style navigation list card with a leading icon chip,
title, optional subtitle, and a flexible trailing accessory. It mirrors the common
list-row patterns from Material 3 list items and the iOS settings list.

## When to use

- A tappable settings/list row (leading icon chip + title + optional subtitle + trailing
  accessory or chevron) -> `G9NavCard`. It also serves as the visible trigger for a
  bottom-sheet selection (open `G9SelectionSheet.ShowAsync` from its `Command`).

## Accessory models

The card composes four trailing / badge accessories that can be mixed freely:

| Model | Property | Looks like |
|---|---|---|
| **Chevron** | `ShowChevron="True"` (default) | A 24dp disclosure arrow — "tap to navigate to detail". Auto-mirrors to a left chevron in RTL. |
| **Value + chevron** | `ValueText="12"` | A trailing value label (count / state / selected option) rendered before the chevron — the iOS-settings "row with current value" pattern. |
| **Coming soon** | `IsComingSoon="True"` | A localized red-on-red-container `به زودی` / `Coming soon` badge that replaces the value + chevron for visible, inactive future actions. |
| **Icon count badge** | `IconBadgeText="3"` | A small circle with a count on the icon chip's top-trailing corner (notification style). Width auto-grows for longer text. |
| **Icon dot badge** | `ShowIconBadgeDot="True"` | A small empty dot on the icon chip corner — the "has updates / unread" indicator. |
| **Custom trailing** | `TrailingView` | Any view (switch, chip, spinner) takes over the trailing slot completely (wins over `ValueText` + `ShowChevron`). |

## Bindable Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Title` | `string?` | `null` | Card title. |
| `Subtitle` | `string?` | `null` | Optional subtitle below the title. |
| `AccentColor` | `Color?` | `null` | Tint used for the icon chip. Defaults to `Primary`. |
| `UseAccentSurface` | `bool` | `false` | When true the WHOLE row background is painted as a soft tint of `CardAccentColor` (→ `AccentColor` → `Primary`) mixed with the surface — the pastel "colored list row" look. Default keeps the standard surface card so existing usages are unchanged. |
| `CardAccentColor` | `Color?` | `null` | Accent for the row-background tint when `UseAccentSurface` is on. Defaults to `AccentColor` then `Primary`. Independent of the icon-chip accent. |
| `IconEmoji` / `MaterialIcon` / `IconPath` / `IconSource` | — | `null` | Leading icon (universal icon system). |
| `ShowChevron` | `bool` | `true` | Show a directional chevron on the trailing edge when no `TrailingView` is set. |
| `ValueText` | `string?` | `null` | Trailing value label rendered before the chevron. Empty hides it. |
| `ValueColor` | `Color?` | `null` | Override color for `ValueText`. Defaults to the muted tertiary text color (Error when `IsDestructive`). |
| `IsComingSoon` | `bool` | `false` | Replaces `ValueText` / `ShowChevron` with the standard coming-soon badge. Use for visible future actions with no command/toast. |
| `ComingSoonText` | `string?` | `null` | Optional override for the badge text. Defaults to `G9Strings.ComingSoon` (`AppControlsComingSoon`). |
| `IconBadgeText` | `string?` | `null` | Count / text shown in a circular badge on the icon chip corner. Takes precedence over the dot. |
| `ShowIconBadgeDot` | `bool` | `false` | Show a small empty dot badge on the icon chip corner (when `IconBadgeText` is empty). |
| `IconBadgeColor` | `Color?` | `null` | Override color for the icon badge. Defaults to `Error`. |
| `MirrorBadgeTextInRtl` | `bool` | `true` | When true the count badge text flows RTL in RTL mode ("99+" → "+99"). |
| `TrailingView` | `View?` | `null` | Custom content for the trailing slot. Wins over `ValueText` / `ShowChevron`. |
| `IsDestructive` | `bool` | `false` | Title color flips to `Error`. |
| `Command` | `ICommand?` | `null` | Executed on tap after the `Tapped` event. |
| `CommandParameter` | `object?` | `null` | Passed to `Command`. |

## Events

- `Tapped` — fires after the press animation, before `Command.Execute`.

## Usage

### Basic nav card (icon + chevron)

```xml
<newControls:G9NavCard
    Title="Profile"
    Subtitle="Manage your account"
    MaterialIcon="Person"
    Command="{Binding OpenProfileCommand}" />
```

### Value text + chevron

```xml
<newControls:G9NavCard
    Title="Sync queue"
    MaterialIcon="Sync"
    ValueText="12" />
```

### Coming-soon row

```xml
<newControls:G9NavCard
    Title="Observation"
    MaterialIcon="ManageSearch"
    IsComingSoon="True" />
```

Coming-soon rows should not bind a command that only shows an unavailable toast. Keep the row visible
for roadmap affordance, but let `IsComingSoon` own the trailing badge and passive behavior.

### Icon count badge

```xml
<newControls:G9NavCard
    Title="Notifications"
    MaterialIcon="Notifications"
    IconBadgeText="3" />
```

### Icon dot badge + custom trailing

```xml
<newControls:G9NavCard Title="Updates available" MaterialIcon="CloudSync" ShowChevron="False" ShowIconBadgeDot="True">
    <newControls:G9NavCard.TrailingView>
        <Label Text="New" FontAttributes="Bold" VerticalOptions="Center"
               TextColor="{themeManager:ThemeColor Primary}" />
    </newControls:G9NavCard.TrailingView>
</newControls:G9NavCard>
```

### Destructive

```xml
<newControls:G9NavCard
    Title="Delete account"
    IsDestructive="True"
    MaterialIcon="DeleteForever"
    Command="{Binding DeleteCommand}" />
```

### Accent-surface row (pastel colored row)

```xml
<newControls:G9NavCard
    Title="عملیات روی درخت"
    MaterialIcon="Visibility"
    UseAccentSurface="True"
    CardAccentColor="{themeManager:ThemeColor Primary}"
    AccentColor="{themeManager:ThemeColor Primary}"
    Command="{Binding OpenCommand}" />
```

When `UseAccentSurface="True"` the whole row gets a soft tint of `CardAccentColor`; the icon
chip keeps its own `AccentColor`. Used by the map tree/pot operations menus.

### Disabled

```xml
<newControls:G9NavCard
    Title="Premium feature"
    Subtitle="Available in pro plan"
    MaterialIcon="Lock"
    IsEnabled="False" />
```

## Behaviour Notes

- The icon chip background is a soft mix of `AccentColor` and `Surface` — a tinted
  backdrop that doesn't dominate the card.
- The default chevron size comes from `G9Metrics.NavCardChevronSize` (24dp) so disclosure
  arrows stay visually comparable to the leading icon chip across all card usages.
- `IsComingSoon = true` hides both `ValueText` and the chevron, then shows a red text badge on
  `ErrorContainer`. The badge is deliberately SMALLER than the row's title — it is a passive roadmap
  marker, not an action, and at the title scale it out-weighed the title beside it. Its geometry is
  the dedicated `G9Metrics.NavCardComingSoon*` set (font `10`, padding `7,3.5`, radius `11` —
  the old title-scale values at ~70%). Retune those tokens, not the badge's construction site.
- `IsComingSoon = true` is passive: tap, `Tapped`, `Command`, and hover-lift behavior are suppressed.
- **The corner badge is the shared `G9CornerBadge`** (see `G9Controls.md` §12b) —
  the same helper `G9IconButton` uses. Its centre sits exactly on the icon chip's
  top-trailing corner (half in / half out) for any count width, it is clip-safe on
  Android (no negative-margin overflow), and it auto-mirrors to the top-leading corner
  in RTL. The count text flips RTL ("99+" → "+99") when `MirrorBadgeTextInRtl` is true
  (default). **Change badge appearance/geometry in `G9CornerBadge`, not here.**
- The card responds to pointer hover (Windows / macOS): a 4% accent tint applied to the
  card background plus a 1px lift.
- Tap animates a slight scale dip (`0.985` for 70ms, then back over 120ms).
- The chevron direction is `ChevronRight` in LTR and `ChevronLeft` in RTL, so the trailing
  visual always points "forward" along the reading direction.
- `IsDestructive = true` colors only the title — the subtitle stays in tertiary text
  color so the card still reads as informational.
