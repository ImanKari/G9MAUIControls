# G9Expander

`G9Expander` is the collapsible section control: a tappable header (leading icon +
title + a trailing expand/collapse chevron) over a content region that is revealed when
the header is tapped. It mirrors the Material 3 / iOS "disclosure group" pattern and the
in-sheet section headers used across the app.

## When to use

- A collapsible section inside a form, settings page, or **bottom sheet** — group related
  fields/rows under a titled header the user can open and close. This is the primary use.
- For a non-collapsible section title with a divider line, use `G9Separator` instead.
- For a tappable navigation row (no inline content reveal — it opens a detail screen or a
  sheet), use `G9NavCard` instead.
- For switching between mutually-exclusive panels, use `G9TabView`.

## Height behaviour & animation (read this for bottom sheets)

Opening / closing **animates** the content host's height between 0 and its measured natural
height (the host is clipped during the animation so it reads as a slide-reveal), while the
chevron rotates in lockstep (~220 ms). The moment the open animation finishes, the height is
reset to auto and clipping is turned off — so the final resting layout is the **exact natural
height**. That means a fit-to-content bottom sheet settles on the correct size after the
animation, and content like a picker's floating label is never statically clipped. The
animation is smooth without leaving an ambiguous fixed height behind it.

The control is **not** wrapped in a content-clipping `Border`: a MAUI `Border` clips its
content to its rounded `StrokeShape` even when transparent, which would cut off the header
icon / title near the corners. The optional framed surface is painted by a content-less
background `Border` behind a non-clipping `Grid` (the design system's "background border +
sibling overlay" pattern — `08-UI-UX-Design-System.md` §4).

## Icon system

The leading icon uses the same universal icon system as every G9 control, routed
through `G9Visuals.CreateIcon`:

- `IconEmoji` — an emoji glyph.
- `MaterialIcon` — a `MauiIcons.Material.MaterialIcons` glyph.
- `IconPath` / `IconSource` — a bitmap (goes through FFImageLoading / `G9CachedImage`).

## Bindable Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Title` | `string?` | `null` | Header title text. |
| `TitleColor` | `Color?` | `null` | Override for the title color. Defaults to `TextPrimary`. |
| `IconEmoji` / `MaterialIcon` / `IconPath` / `IconSource` | — | `null` | Leading header icon (universal icon system). |
| `IconColor` | `Color?` | `null` | Tint for the leading icon. Defaults to `TextPrimary`. |
| `ChevronColor` | `Color?` | `null` | Tint for the expand/collapse chevron. Defaults to `TextSecondary`. |
| `ExpanderContent` | `View?` | `null` | The collapsible content shown while expanded. This is the **XAML `ContentProperty`** — nested XAML content lands here automatically. |
| `IsExpanded` | `bool` | `false` | Open / closed state. **Two-way bindable.** Toggling animates the chevron and reveals/hides the content. |
| `ShowFrame` | `bool` | `false` | When true, paints the whole control as a rounded surface card (background + hairline outline). When false (default) it is chrome-free — just the header over the content — matching the app's inline section headers. |
| `HeaderHeight` | `double` | `48` | Minimum height of the header row (keeps the tap target ≥ the accessibility floor). |
| `ContentPadding` | `Thickness` | `0` | Padding applied inside the revealed content region. |

## Events

- `ExpandedChanged` — `EventHandler<bool>`; fires after `IsExpanded` changes, carrying the
  new state.

## Methods

- `Toggle()` — flips `IsExpanded` (same effect as tapping the header).

## Usage

### Inline section (default, chrome-free) — matches the app's in-sheet sections

```xml
<newControls:G9Expander Title="Tree status" MaterialIcon="Park" IsExpanded="True">
    <VerticalStackLayout Spacing="10">
        <newControls:G9Switch IsInFormRow="True" Title="Index tree" />
        <newControls:G9Picker Label="Health status" ItemsSource="{Binding HealthItems}" />
        <newControls:G9Picker Label="Growth status" ItemsSource="{Binding GrowthItems}" />
    </VerticalStackLayout>
</newControls:G9Expander>
```

The nested `VerticalStackLayout` is assigned to `ExpanderContent` via the control's
`ContentProperty` — no `<newControls:G9Expander.ExpanderContent>` wrapper needed.

### Card-style (framed)

```xml
<newControls:G9Expander
    Title="Basic information"
    MaterialIcon="Description"
    ShowFrame="True">
    <Label Text="..." />
</newControls:G9Expander>
```

### Two-way bound open state + event

```xml
<newControls:G9Expander
    x:Name="LinkInfoExpander"
    Title="Link information"
    IconEmoji="🔗"
    IsExpanded="{Binding LinkSectionOpen, Mode=TwoWay}" />
```

```csharp
LinkInfoExpander.ExpandedChanged += (_, open) => { /* react to open/close */ };
```

## Behaviour Notes

- **Tap target is the header only** — tapping inside the revealed content never collapses
  the section.
- Opening / closing **animates** (content height reveal + chevron rotation, ~220 ms); the
  height is reset to auto when the open animation finishes so the resting layout measures
  exactly (see the height section above).
- The chevron is the `KeyboardArrowDown` glyph at rest (pointing down = collapsed) and
  rotates 180° to point up when expanded. It is a vertical glyph, so it needs no RTL
  mirroring.
- **RTL:** the header relies on the ambient `FlowDirection` (same as `G9NavCard`). The
  icon + title sit on the reading-start edge (right in RTL) and the chevron on the
  reading-end edge (left in RTL), mirroring automatically on a culture flip.
- The chevron view is built once and never recreated, so its rotation survives theme /
  culture / property refreshes (see `G9Controls.md` §12a).
- Disabled (`IsEnabled = false`) dims the whole control to 45% and ignores taps.
- The header tap plays a subtle scale dip (`0.99` for 60 ms, back over 90 ms) for tactile
  feedback.
