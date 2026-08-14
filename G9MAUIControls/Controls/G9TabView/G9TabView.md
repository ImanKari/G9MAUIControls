# G9TabView / G9TabItem

`G9TabView` is the modern segmented tab view used everywhere in the app. It ships
with two visual treatments — a flat **underlined** look (the default) and the legacy
rounded **pill** segmented control — both share the same animation / RTL / lazy-content
infrastructure.

## When to use

- In-page tabbed content -> `G9TabView` + `G9TabItem`.
- Conditional / dynamic tabs: add or remove `G9TabItem`s from the `Items` collection
  in code-behind (the item has no `IsVisible` — collection membership controls visibility).

## Style (visual treatment)

| Style | Visual | Defaults |
|---|---|---|
| `Underlined` (default) | Flat. Bar is transparent with a 1 dp top + bottom hairline rule and a 1 dp `\|` separator between every pair of cells. The active cell is marked by a Primary-coloured 2.5 dp **bottom underline** that animates between cells (X + width interpolated together). Active text colour is `OnSurface`; the underline carries the highlight. Matches Material 3 "secondary tabs". | `ShowFrame=false`, `FrameCornerRadius=0`, `ContentPadding=G9LayoutMetrics.BodyElementMargin` (10/10/10/0 — the same page-edge margin used by every list in the app, with 0 bottom so scrollable content reaches the end of the page naturally). Set `ContentPadding="0"` for full-bleed (e.g. a map tab). |
| `Pill` | Rounded segmented control. The bar is a single rounded "pill" container; the active cell sits behind a smaller floating pill with a colored elevation halo. Active text colour is `OnPrimaryContainer`. | Pair with `ShowFrame=true`, `FrameCornerRadius=16`, `ContentPadding=20` for the legacy framed look. |

The default is `Underlined` because the app designer wants every tabbed surface to use
the flat style. To opt into the legacy pill, set `Style="Pill"` and (typically) the
matching `ShowFrame` / `FrameCornerRadius` / `ContentPadding` trio.

`Style` is **orthogonal** to `Mode` (`Fixed` / `Scrollable`), `BarPosition` (`Top` /
`Bottom`), and `HeaderOnly` — any combination is supported.

### IndicatorColor semantics per style

| Style | What `IndicatorColor` overrides |
|---|---|
| `Underlined` | The colour of the **bottom underline** itself. Active text stays on `OnSurface` so a custom accent never reduces label contrast. |
| `Pill` | The active **text / icon** colour (the pill is the indicator in this style). |

## Layout Modes

| Mode | Behavior | Best for |
|---|---|---|
| `Fixed` (default) | Tabs distribute equally across the full bar width with `*` columns. No scrolling — the cells host is parented **directly in the bar**, NOT inside the bar's `ScrollView` (see below). | 2–4 tabs with short labels (Material 3 "primary tabs"). App convention: tab views are full-width. |
| `Scrollable` | Tabs auto-size; the bar scrolls horizontally when total width exceeds the viewport. Edge fades hint at off-screen tabs. | 5+ tabs or tabs with variable-length labels (Material 3 "secondary tabs"). |

### Fixed mode bypasses the ScrollView (do not undo)

`AttachCellsHost` parents the cells host inside `_barScroll` only in `Scrollable` mode. In `Fixed`
mode the host goes straight into the bar and the scroll view is hidden, because on Android the
platform scroll view **reserves a ~10 dp scrollbar gutter even with both scrollbars disabled**. The
host — and therefore every cell, and therefore the underline, which is sized from the active cell —
came out 10 dp narrower than the bar, while the bar's own top/bottom rules still spanned the full
width.

That shortfall is invisible in LTR (the first tab is left-flush; the missing 10 dp falls off the far
right edge) and obvious in RTL, where the first tab is the RIGHTMOST cell: the underline sits a
little left of its tab and leaves a gap at the screen edge. Measured on a 1344 px / 3× screen: the
underline ended at x=1313 instead of 1343 — exactly 30 px. Fixed mode never scrolls, so nothing is
lost by leaving the scroll view out of the tree.

Reparenting is idempotent (`ApplyLayoutMode` runs on every visual apply) and is also called once
from the constructor: `Mode`'s default IS `Fixed`, so assigning it there does not fire its
`OnChanged` — without the explicit call the first `RebuildAll` would still measure inside the scroll
view and hand the indicator a stale width.

The indicator additionally re-snaps on `_cellsAndPillHost.SizeChanged` (skipped while it is
animating, which owns the pill for those 240 ms) so a later re-layout at a different width — sheet
resize, rotation — can never leave it sized for the old one.

## Bar Position

| Position | Layout |
|---|---|
| `Top` (default) | Bar above content panel. |
| `Bottom` | Bar below content panel. Useful as a footer-style switcher. |

When `BarPosition` is set, the root grid's row sizes are swapped so the bar always
sits in the `Auto`-sized row (its natural height) and the content always sits in the
`Star`-sized row. Just swapping `Grid.Row` without swapping the row sizes would
stretch the bar to fill leftover space — that bug was fixed.

## Header-only mode

`HeaderOnly = true` collapses the content panel + spacer rows to height 0 and hides
the content border. Only the bar renders. Use this when the bar drives an external
content host (a sibling ScrollView, a CarouselView, etc.) and the consumer reacts to
`SelectionChanged` to update that host. The tab items' `TabContent` is ignored in this
mode.

The page-level top tab bar in `TempDesignPageIman.xaml` is exactly this pattern — it
drives 7 sibling `ScrollView` content tabs through `SelectionChanged` instead of
hosting them inside the bar.

## G9TabView Bindable Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Items` | `ObservableCollection<G9TabItem>?` | `[]` | The tab list. Set via XAML content or in code. |
| `SelectedIndex` | `int` | `0` | Two-way bindable. Active tab index. |
| `Style` | `G9TabStyle` | `Underlined` | Visual treatment. `Underlined` = flat tabs with `\|` separators + animated primary bottom underline (default — used everywhere in the app). `Pill` = rounded segmented control with floating pill indicator (legacy). Orthogonal to `Mode`, `BarPosition`, `HeaderOnly`. |
| `Mode` | `G9TabMode` | `Fixed` | `Fixed` (default — equal full-width tabs) or `Scrollable` (auto-width, horizontal scroll for 5+ tabs). |
| `EagerContent` | `bool` | `false` | When `false` (default) tab bodies are realized **lazily** on first activation (see "Lazy tab-body rendering"). Set `true` to build every tab up front (legacy). |
| `BarPosition` | `G9TabBarPosition` | `Top` | `Top` or `Bottom`. |
| `HeaderOnly` | `bool` | `false` | Renders only the bar; content panel / spacer collapse to zero. |
| `TabHeight` | `double` | `52` | Tab bar height. The pill (in `Pill` style) is centered inside this with vertical inset; the underline (in `Underlined` style) sits flush at the bottom. |
| `TabBarBackground` | `Color?` | `null` | Override tab bar background. In `Underlined` defaults to transparent; in `Pill` defaults to `SurfaceContainerLow`. |
| `IndicatorColor` | `Color?` | `null` | Override the indicator colour. Tints the **underline** in `Underlined`; tints the active **text/icon** in `Pill`. |
| `ShowFrame` | `bool` | `false` | When true, paints a border + rounded corners around the content area. The `Underlined` style defaults this off (consumer hosts their own framing). The `Pill` style typically pairs with `ShowFrame=true`. |
| `FrameCornerRadius` | `double` | `0` | Corner radius for the content panel border (only relevant when `ShowFrame == true`). The `Underlined` style defaults this to 0; `Pill` consumers typically set 16. |
| `ContentPadding` | `Thickness` | `(10,10,10,0)` | Inset around the active tab's content. The `Underlined` style defaults to `G9LayoutMetrics.BodyElementMargin` so list cards / form fields don't touch the screen edges and the first row sits a touch below the bottom rule (bottom = 0 so scrollable content reaches the end of the page naturally). Set `0` for full-bleed (map tabs). `Pill` consumers typically set `20` for the framed card look. |

## G9TabView Events

- `SelectionChanged` — fires after `SelectedIndex` changes.

## G9TabItem Bindable Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Text` | `string?` | `null` | Tab label. |
| `Emoji` / `MaterialIcon` / `ImagePath` / `ImageSource` | — | `null` | Optional icon (universal icon system). |
| `BadgeCount` | `int` | `0` | Inline pill badge with the formatted count. `0` = no badge. |
| `BadgeText` | `string?` | `null` | Inline pill badge with custom text. Wins over `BadgeCount`. |
| `BadgeDot` | `bool` | `false` | Renders a 7×7 dot instead of a pill. Used when no count makes sense. |
| `TabContent` | `View?` | `null` | The content rendered inside the host when this tab is active. Ignored when `G9TabView.HeaderOnly == true`. |

`G9TabItem` is the `[ContentProperty]` for `TabContent` so XAML can place the content
directly inside the tab item element.

## Visual Anatomy

### Underlined (default)

- **Bar frame** — Transparent rectangular bar (no rounded outline, no outer stroke). Full
  bar height, zero inner padding so the rules touch the bar edges.
- **Top + bottom rules** — Two 1 dp `BoxView`s pinned `LayoutOptions.Start` / `End`,
  spanning the full bar width, coloured `OutlineVariant` at 50 % alpha. They render
  underneath the cells host so the underline indicator can paint over the bottom rule
  for the active cell's segment.
- **Cell separators** — 1 dp vertical `BoxView`s placed in their own Auto-sized columns
  interleaved between cells (`cell, sep, cell, sep, cell` — no leading or trailing
  separator). Height = `TabHeight − 2 × TabSeparatorVerticalInset` (default 52 − 24 = 28
  dp), centred vertically, coloured `OutlineVariant` at 50 % alpha.
- **Active underline indicator** — The same `_pill` `Border` used in the Pill style,
  reshaped to a thin bar: `HeightRequest = TabUnderlineThickness` (2.5 dp),
  `VerticalOptions = End`, no stroke, no rounded corners,
  `BackgroundColor = IndicatorColor ?? Primary`. It animates between cells via
  `TranslationX` + `WidthRequest` in lockstep — same animation pipeline as the pill —
  so adjacent cells of any width transition cleanly.
- **Active text/icon** — `OnSurface` (regular text token) + `FontAttributes.Bold`. The
  underline carries the highlight; the label stays on a high-contrast token so a custom
  `IndicatorColor` never reduces label legibility.
- **Inactive text/icon** — `OnSurfaceVariant`.

### Pill (legacy)

- **Bar frame** — Outer rounded `Border` (radius 26 dp) with `SurfaceContainerLow`
  background and a 1 dp 50 %-alpha `OutlineVariant` stroke. 4 dp inner padding.
- **Sliding pill** — Floating `Border` (radius 18 dp) sized to the active cell. Vertical
  bounds are computed as `TabHeight − TabBarInnerPadding × 2 − TabPillVerticalInset × 2`,
  giving a 40 dp pill inside the 44 dp inner area (2 dp clearance top + bottom for the
  stroke to render). Background = `PrimaryContainer`, stroke = 35 %-alpha `Primary`.
  No shadow — the app is shadow-free (`../G9Controls.md` §0), so the tinted ring is
  what separates the active pill from the bar.
- **Cells** — Built once per item in `BuildCell`. Each cell has a row of icon + label +
  optional badge. `HeightRequest` matches the bar's inner area
  (`TabHeight − TabBarInnerPadding × 2`) so the icon / label / badge are vertically
  centered on the pill, not on the wider bar. Cell content `FlowDirection` matches the
  page so emoji + label render in the natural reading order.
- **Active text/icon** — `OnPrimaryContainer` (matching contrast token for the pill).
- **Inactive text/icon** — `OnSurfaceVariant`.
- **Edge fades** (Scrollable mode) — `GraphicsView` overlays painted on top of the bar
  edges. Fade from `TabBarBackground` color to transparent over 24 dp. Visible only when
  the bar can scroll past that edge. Opacity ramps in/out within 24 dp of the edge.

## Smooth indicator animation (shared by both styles)

The same animation pipeline drives the indicator in both styles — the only difference is
which dimensions of the same `_pill` `Border` carry the visual:

- `Pill` style: a thick rounded segment behind the active cell.
- `Underlined` style: a thin (2.5 dp) bar pinned to the bottom of the active cell.

The previous `TranslateToAsync` with `SpringOut` animated only `TranslationX` and let
`WidthRequest` jump in one frame between cells of different widths — visually a
"stretch then slide" decomposition. The new `AnimatePillToSelected` interpolates BOTH
`TranslationX` and `WidthRequest` together inside a single `Animation` callback over
240 ms with `CubicOut` easing. Adjacent cells of any width transition cleanly, in either
style.

The animation is cancellation-safe: rapid taps abort the in-flight animation
(`AbortAnimation(PillAnimationName)`) and the new one starts from the current visible
position via the `_animatedPillX` / `_animatedPillWidth` tracking fields.

### Deferred first-measure (no dispatcher busy-loop)

When `AnimatePillToSelected` runs before the active cell has been measured
(`cell.Container.Width == 0` — common on first layout, and on Windows where the
HeaderOnly bar can report 0 width for several pump cycles), it must wait for the real
width instead of guessing. It does so with a **one-shot `cell.Container.SizeChanged`
subscription** that self-unsubscribes after the first non-zero width and then re-invokes
the animation.

It must **not** re-queue itself with `Dispatcher.Dispatch(AnimatePillToSelected)`. That
pattern works on Android / iOS (a layout pass runs between dispatcher pumps) but on WinUI
the dispatcher pump starves the layout pass — the width stays 0, a new Dispatch is queued
every pump, and the UI thread pins ~100% of one core forever on an otherwise idle page.
See `G9Controls.md` §15 (Windows pitfall **W2**). This is why the wait is `SizeChanged`-
based, not dispatcher-based.

## Smooth content transition

When the active tab changes, the new content slides in 6dp from the trailing edge (or
leading edge if moving back) and fades from 0 → 1 over 160ms with `CubicOut`. The
direction sign flips in RTL so the slide always reads as "next tab arrives from the
reading-end side." Subtle, just enough to feel alive without reading as a page swipe.

## Lazy tab-body rendering (default)

Heavy tab bodies (virtualized lists, charts) realize their native view tree on the UI thread the
first time they become visible — on the open / tab-switch frame that froze sheets for ~1–2 s
(the Transfer sheet was the worst case). By default (`EagerContent = false`) each tab's
`TabContent` is **not attached to the visual tree until its tab is first activated**:

- On first activation the content host shows a small spinner, then after `LazyContentRealizeDelayMs`
  (~240 ms) the body is attached (native handlers build) and revealed with the slide/fade. The
  delay lets the pill move + the host's fit-to-content resize settle first, so the heavy build
  doesn't blink the resize. A pending-index token ensures rapid tab taps only realize the latest.
- After first activation the body **stays attached**; subsequent switches just toggle visibility,
  so scroll / focus / selection state is preserved (lazy-**once**).
- Inactive, never-visited tabs cost nothing — this is why a sheet with several heavy tabs opens
  fast (only the active tab realizes).

Set `EagerContent = true` to build all tabs up front (the rare screen that needs every tab
measured / its data loaded immediately at open).

**Risks to keep in mind with the global default:** code that, at open, performs a *platform-handler-
dependent* operation (scroll, focus, screenshot, measure) on a **not-yet-activated** tab will hit a
null/!ready handler — set properties (`Content`, `ItemsSource`, `Text`, `IsVisible`) instead, which
are safe pre-realization; a tab body's `Loaded`/first-init now fires on first activation, not at
open, so flows that assumed all tabs initialized at open must move that logic per-tab or opt out
with `EagerContent`. Fit-to-content sizing is unaffected because the height is provided by count /
structure (`IG9BottomSheetContentHeightProvider`), not by measuring the unparented view.

## Destruction-free cell architecture

Same pattern as `G9ChipGroup`:
- Cell widgets (Label + icon View + badge) are built once in `BuildCell` and never
  recreated.
- Color updates mutate `Label.TextColor` / `MauiIcon.IconColor` on existing instances.
- `OnItemVisualChanged` (when an item's text / badge / icon changes) updates the
  existing cell in-place via `UpdateCellContent` — no full bar rebuild.

This eliminates the 1-frame default-color flash that recreating icon Views causes.

## RTL

- Root grid + scroll view + inner cells host all locked to `FlowDirection.LeftToRight`
  so cell `X` coordinates always mean "pixels from physical left." This makes the
  pill positioning math deterministic.
- RTL ordering is achieved by reversing the items iteration order when populating
  cells in `RebuildAll`. No manual `Grid.Column` mirroring (which the previous version
  did and produced double-mirror collisions on RTL pages).
- Cell row content (icon + label + badge) uses `FlowDirection.MatchParent` so the
  inner reading order follows the page's culture direction.
- **Anything that shrinks the cells host shows up in RTL first.** The reversal puts the
  FIRST (usually selected) tab on the physical right, so any width the host loses is a
  visible gap at the screen edge under the active tab, while the same loss in LTR falls
  off the far end where nobody looks — see "Fixed mode bypasses the ScrollView". When a
  user reports the indicator "slightly off in Persian only", measure the underline's
  pixel span against the bar's before assuming the positioning math is wrong: in the one
  case we had, the math was right and the host was 10 dp narrow.

## Usage

### Default (flat / Underlined) — list-of-cards tab content

```xml
<newControls:G9TabView SelectedIndex="0">
    <!--  Style="Underlined" is the default — no need to set it. ShowFrame / FrameCornerRadius
          default to false / 0; ContentPadding defaults to (10,10,10,0) — the app's standard
          page-edge body margin, with 0 bottom so a scrollable list reaches the page end naturally.  -->
    <newControls:G9TabItem Text="General Info">
        <ScrollView>
            <VerticalStackLayout Spacing="{x:Static themeManager:G9LayoutMetrics.EdgeSpacing}">
                <Label Text="Product: Pecan" />
                <Label Text="Base Species: Wichita" />
            </VerticalStackLayout>
        </ScrollView>
    </newControls:G9TabItem>

    <newControls:G9TabItem Text="Samples" BadgeCount="3">
        <customizedControls:CustomizedCollectionView ItemsSource="{Binding Samples}" />
    </newControls:G9TabItem>

    <newControls:G9TabItem Text="Packages">
        <customizedControls:CustomizedCollectionView ItemsSource="{Binding Packages}" />
    </newControls:G9TabItem>
</newControls:G9TabView>
```

### Full-bleed (e.g. a map tab) — opt out of the default padding

```xml
<newControls:G9TabView SelectedIndex="0" ContentPadding="0">
    <newControls:G9TabItem Text="Map">
        <customizedMap:ArcGisMapView Configuration="{Binding MapSession}" />
    </newControls:G9TabItem>
    <newControls:G9TabItem Text="Samples">
        <!--  This tab carries its own padding because the map tab opted out globally.  -->
        <Border Padding="{x:Static themeManager:G9LayoutMetrics.BodyPadding}">
            <customizedControls:CustomizedCollectionView ItemsSource="{Binding Samples}" />
        </Border>
    </newControls:G9TabItem>
</newControls:G9TabView>
```

### Opt-in pill (legacy framed segmented control)

```xml
<newControls:G9TabView
    Style="Pill"
    SelectedIndex="0"
    ShowFrame="True"
    FrameCornerRadius="16"
    ContentPadding="20">
    <newControls:G9TabItem Text="Fields" Emoji="🌾" BadgeCount="3">
        <VerticalStackLayout>
            <Label Text="Field queue" FontAttributes="Bold" />
            <Label Text="Three field tasks waiting" />
        </VerticalStackLayout>
    </newControls:G9TabItem>

    <newControls:G9TabItem Text="Water" Emoji="💧" BadgeDot="True">
        <Label Text="Irrigation" />
    </newControls:G9TabItem>

    <newControls:G9TabItem Text="Alerts" BadgeText="NEW">
        <Label Text="Alert stream" />
    </newControls:G9TabItem>
</newControls:G9TabView>
```

### Fixed mode (3 equal-width tabs)

```xml
<newControls:G9TabView Mode="Fixed" SelectedIndex="0">
    <newControls:G9TabItem Text="Today" Emoji="📅" />
    <newControls:G9TabItem Text="Week" Emoji="🗓" />
    <newControls:G9TabItem Text="Month" Emoji="📆" />
</newControls:G9TabView>
```

### Bottom-positioned bar (footer-style switcher)

```xml
<newControls:G9TabView Mode="Fixed" BarPosition="Bottom" SelectedIndex="0">
    <newControls:G9TabItem Text="Reports" Emoji="📊">
        <Label Text="Reports view" />
    </newControls:G9TabItem>
    <newControls:G9TabItem Text="Trends" Emoji="📈">
        <Label Text="Trends view" />
    </newControls:G9TabItem>
    <newControls:G9TabItem Text="Locations" Emoji="📍">
        <Label Text="Locations view" />
    </newControls:G9TabItem>
</newControls:G9TabView>
```

### Header-only mode (external content host)

```xml
<Grid RowDefinitions="Auto,*">
    <newControls:G9TabView
        Grid.Row="0"
        HeaderOnly="True"
        Mode="Scrollable"
        SelectionChanged="OnTabChanged">
        <newControls:G9TabItem x:Name="Tab0" Text="Home" />
        <newControls:G9TabItem x:Name="Tab1" Text="Profile" />
        <newControls:G9TabItem x:Name="Tab2" Text="Settings" />
    </newControls:G9TabView>

    <Grid Grid.Row="1">
        <ScrollView x:Name="HomeContent" />
        <ScrollView x:Name="ProfileContent" IsVisible="False" />
        <ScrollView x:Name="SettingsContent" IsVisible="False" />
    </Grid>
</Grid>
```

```csharp
private void OnTabChanged(object? sender, int index)
{
    HomeContent.IsVisible = index == 0;
    ProfileContent.IsVisible = index == 1;
    SettingsContent.IsVisible = index == 2;
}
```

### Custom indicator (active text/icon) color

```xml
<newControls:G9TabView IndicatorColor="#D97706">
    <newControls:G9TabItem Text="Today" Emoji="📅" />
    <newControls:G9TabItem Text="Week" Emoji="🗓" />
</newControls:G9TabView>
```

### Two-way binding to a view-model index

```xml
<newControls:G9TabView SelectedIndex="{Binding ActiveTabIndex, Mode=TwoWay}">
    ...
</newControls:G9TabView>
```

## Behaviour Notes

- The tab content is parented to a single `_contentHost` grid on first build. Switching
  tabs only toggles `IsVisible` — the content never re-parents, so view state (focus,
  scroll position, selection) is preserved across tab switches.
- Tap target padding inside each cell is `TabCellHorizontalPadding` (16dp).
- **Auto-scroll on selection** in `Scrollable` mode advances the bar by **2 cell-widths**
  past the selected cell on the trailing side (or pulls 2 cell-widths back on the leading
  side) so the next 1–2 unselected tabs become visible. Direction is decided by where
  the selected cell sits relative to the viewport center — taps near the trailing edge
  push the scroll forward, taps near the leading edge pull it back. The first and last
  tabs snap to scroll position 0 / max so the user never has to drag past the available
  range.
- Setting `SelectedIndex` to an out-of-range value during XAML loading is safe — the
  control uses `EffectiveIndex` (clamps to `[0, count-1]`) internally.
- When a tab becomes hidden, `UnfocusDescendantInputs` walks the visual tree and clears
  focus from any platform `Entry` / `Editor` so WinUI doesn't keep an off-screen
  TextBox as the focused element (which previously caused "scroll-into-view jumps" on
  later interactions).
- Inner ScrollViews inside an activated tab are reset to `(0, 0)` on tab show so a tab
  switch never lands the user mid-scroll inside the previous tab content.
