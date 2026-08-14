# G9Separator

`G9Separator` is the section divider with optional title and icon. The title host is
laid out so Start / End really hug the start / end edge while the line fills the rest;
Center keeps the symmetric two-line layout.

## Bindable Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Title` | `string?` | `null` | Optional title text. When null/empty, renders a single full-width line. |
| `TitleAlignment` | `G9SeparatorTitleAlignment` | `Auto` | `Auto` / `Start` / `Center` / `End`. `Auto` uses Start in LTR, End in RTL. |
| `TitleColor` | `Color?` | `null` | Override the title color. Defaults to `TextTertiary`. |
| `LineColor` | `Color?` | `null` | Override the divider color. Defaults to `Divider`. |
| `Thickness` | `double` | `1` | Line thickness in dp. |
| `IconEmoji` / `MaterialIcon` / `IconPath` / `IconSource` | — | `null` | Optional inline icon shown next to the title. |

## Usage

### Title-less divider

```xml
<newControls:G9Separator />
```

### Auto-aligned title (Start in LTR, End in RTL)

```xml
<newControls:G9Separator Title="Pickers" />
```

### Forced alignments

```xml
<newControls:G9Separator Title="Start title" TitleAlignment="Start" IconEmoji="🌱" />
<newControls:G9Separator Title="Center title" TitleAlignment="Center" Thickness="2" />
<newControls:G9Separator Title="End title" TitleAlignment="End" />
```

### Custom colors

```xml
<newControls:G9Separator
    Title="Warning section"
    TitleColor="{themeManager:ThemeColor Warning}"
    LineColor="{themeManager:ThemeColor WarningBorder}" />
```

## Behaviour Notes

- The title text is rendered uppercase with bold weight and a 1.2× character spacing —
  matches the G9 section header style.
- **Per-alignment column rebuild**. The root grid's column definitions are rebuilt on
  every visual update so each alignment uses exactly the minimum columns it needs. This
  fixes a gap that appeared in the previous fixed `Star, Auto, Star` layout — when the
  title sat in column 0 (Star) for `Start` mode, the layout engine still gave column 0
  half the width, leaving a half-width empty space between the title and the line.
  - `Start` → 2 columns: `Auto, Star`. Title hugs column 0 at its natural width; line
    fills column 1. Gap is exactly `ColumnSpacing` (10dp).
  - `End` → 2 columns: `Star, Auto`. Line fills column 0; title hugs column 1.
  - `Center` → 3 columns: `Star, Auto, Star`. Equal lines flank the centered title.
  - No title → 1 column: `Star`. One full-width line.
- **RTL handling** is expressed via a `PhysicalAlignment` resolver. The root grid's
  `FlowDirection` stays locked to `LeftToRight` so column 0 always means physical-left,
  making the layout deterministic. RTL alignment flips which physical column the title
  sits in (Start in RTL = physical-right). Title text glyphs still respect the page's
  reading direction through the `Label`'s inherited `FlowDirection`.
- **Title host follows culture FlowDirection**. The `_titleHost` (which holds icon +
  label) sets its `FlowDirection` from `G9Visuals.IsRtl` per visual update so
  the icon lands on the LEADING side of the title in both directions — physical-left
  in LTR, physical-right in RTL. The two FlowDirection choices (root LTR-locked,
  title-host culture-following) serve different purposes and are deliberately
  independent.
- Empty / null title collapses the title host and spans the divider line across one
  column for a clean full-width rule.
