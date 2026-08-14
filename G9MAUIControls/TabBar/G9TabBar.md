# G9TabBar Usage

`G9TabBar` is a highly customizable bottom navigation bar featuring a dynamic floating action button (FAB) and a horizontal sub-menu row that gracefully fans out from the FAB center.

## Basic XAML

```xml
<customizedMenu:G9TabBar
    x:Name="MainBottomMenu"
    Margin="12,0,12,12"
    VerticalOptions="End"
    FabIndex="2"
    DefaultSelectedIndex="2"
    FlowDirection="{Binding FlowDirection, Source={x:Reference PageRoot}}" />
```

`FabIndex` is zero-based and selects which bottom item becomes the floating circle.
Set it to `-1` to disable the FAB entirely (plain tab bar, no notch).

`DefaultSelectedIndex` is zero-based and sets the initially selected item.

Use `SelectedIndex` when you need two-way runtime selection binding.
Use `DefaultSelectedIndex` when you only want to choose the initial selected item.

## Configure Items

```csharp
MainBottomMenu.FabIndex = 2;          // third item is the FAB
MainBottomMenu.Items =
[
    new G9TabBarItem("Dashboard", MaterialIcons.Dashboard),
    new G9TabBarItem("Tasks",     MaterialIcons.Assignment),
    new G9TabBarItem("Map",       MaterialIcons.Map),        // ← FAB slot
    new G9TabBarItem("Announcements", MaterialIcons.Announcement),
    new G9TabBarItem("Profile",   MaterialIcons.ManageAccounts)
];
```

You can also put the FAB at index 0 (left edge), the right edge, or any other position. The notch and sub-menus will automatically clamp and align perfectly:

```csharp
MainBottomMenu.FabIndex = 0;          // first item is the FAB
MainBottomMenu.Items =
[
    new G9TabBarItem("Create", MaterialIcons.Add),           // ← FAB slot
    new G9TabBarItem("Dashboard", MaterialIcons.Dashboard),
    new G9TabBarItem("Map",     MaterialIcons.Map),
    new G9TabBarItem("Profile", MaterialIcons.ManageAccounts)
];
```

Or disable the FAB completely:

```csharp
MainBottomMenu.FabIndex = -1;         // no FAB — plain tab bar
```

## Configure FAB Sub-Menu Items

Sub-menu items appear in a **horizontal row** above the FAB, revealed with a synchronized, center-outward staggered animation.

```csharp
MainBottomMenu.SubMenuItems =
[
    new G9TabBarItem("Requests",  MaterialIcons.Assignment),
    new G9TabBarItem("Teams",     MaterialIcons.Group),
    new G9TabBarItem("Reports",   MaterialIcons.BarChart),
    new G9TabBarItem("Approvals", MaterialIcons.CheckCircle)
];
```

> **Note:** The horizontal row is edge-guarded. If the FAB is positioned near the screen edge, the entire sub-menu row will elegantly shift inward to prevent clipping off-screen.

## Click Handling

Each `G9TabBarItem` supports both `Clicked` and `Command`. `Clicked` runs first and receives a `G9TabBarClickContext`.

```csharp
new G9TabBarItem("Dashboard", MaterialIcons.Dashboard)
{
    Clicked = context =>
    {
        var index = context.Index;
        var isFabSubMenuItem = context.IsSubMenuItem;
        var item = context.Item;
    },
    Command = OpenDashboardCommand
}
```

You can also handle all selections directly from the control:

```csharp
MainBottomMenu.ItemSelected += (_, args) =>
{
    var index = args.Index;
    var isFabSubMenuItem = args.IsSubMenuItem;
};
```

## Useful Properties

- `FabIndex`: which bottom item is the FAB (-1 = none). Bindable, two-way.
- `Items`: bottom bar items.
- `SubMenuItems`: horizontal row FAB sub-menu items.
- `SelectedIndex`: current bottom item index, two-way bindable.
- `DefaultSelectedIndex`: initial selected bottom item index.
- `IsCenterFloating`: whether the FAB circle is visible above the bar.
- `IsFabOpen`: whether the FAB sub-menu row is open.
- `IsOverflowOpen`: whether the overflow (>5 items) column is expanded. Two-way bindable.
- `OverflowText`: explicit override for the **More** trigger label. When unset, the bar falls back
  to the localized `More` string from `G9StringResources` and re-reads it on every visual pass so a
  culture flip is reflected without requiring `Items` to be reassigned.
- `ResetCenterOnMenuSelection`: closes the FAB state when a non-FAB bottom item is selected.

## Overflow ( > 5 items )

When `Items` contains more than 5 entries the bar collapses everything past slot 4 into a vertical
overflow column anchored to a three-dot trigger that takes the last visible slot. Tapping the
trigger fans the column upward with a stagger from the bottom (closest to the trigger) outward,
mirroring the FAB sub-menu animation language. The trigger and the FAB are mutually exclusive: if
`FabIndex` lands on or after the trigger slot, the FAB is silently disabled while overflow is
active.

```csharp
MainBottomMenu.Items =
[
    new G9TabBarItem("Dashboard", MaterialIcons.Dashboard),
    new G9TabBarItem("Tasks",     MaterialIcons.TaskAlt),
    new G9TabBarItem("Map",       MaterialIcons.Map),
    new G9TabBarItem("Reports",   MaterialIcons.Insights),
    new G9TabBarItem("Teams",     MaterialIcons.Groups),    // ← collapses into overflow
    new G9TabBarItem("Approvals", MaterialIcons.FactCheck), // ← collapses into overflow
    new G9TabBarItem("Profile",   MaterialIcons.AccountCircle)
];
```

Tapping an overflow item invokes its `Clicked` / `Command`, raises `ItemSelected`, **and** updates
`SelectedIndex` to the source index. Because the source slot isn't visible in the bar, the
indicator pill snaps to the **More** trigger and the trigger picks up the selected styling. The
selected overflow item itself lights up an inner green halo on its glass shell. Overflow
auto-collapses when the FAB opens, when any regular bar item is tapped, or when the user taps the
empty area covered by the backdrop.

## Visual Surfaces

The FAB, the sub-menu cells, and the overflow column items all use the same two-layer composition:

- **Outer shell** — translucent glass that mirrors the main bar (same stroke recipe; flat, no shadow),
  rendering the cell on a softly-frosted backdrop instead of floating in mid-air. Sub-menu cells and
  overflow items use a rounded rectangle (`SubMenuRowOuterCornerRadius`) so labels stay legible at
  the edges; the FAB shell stays a perfect circle.
- **Inner accent ring** — primary radial gradient that hosts the icon (`FabInnerSize`,
  `SubMenuRowItemInnerSize`). For overflow items the equivalent piece is a **selection halo** —
  hidden by default and lit only behind the icon of whichever overflow item is the active selection.

The plus / close glyph is rendered through `G9FabPlusIconDrawable`, a `GraphicsView` overlay that
draws bold strokes with rounded line caps so the symbol stays crisp at any size and rotation. Tune
arm length and stroke thickness via `FabPlusArmRatio` / `FabPlusStrokeRatio` in `G9TabBarMetrics`.

Every color used by the menu lives in [`G9TabBarColors.cs`](G9TabBarColors.cs) — explicit alpha
recipes per role, branched per theme. Edit one file to retheme the entire bar.

### Selected pill + inactive-item legibility (2026-07)

Two recipes in `G9TabBarColors.cs` are tuned specifically for visibility on the translucent glass
bar — do not revert them to the pale defaults:

- **Selected-item indicator pill (`SelectedIndicator`)** — a **primary-tinted** fill
  (`theme.Primary.WithAlpha(SelectedIndicatorAlphaLight=0.28 / Dark=0.36)`), NOT the near-invisible
  pale `PrimaryContainer` it used before. This makes the active tab's pill clearly green in both
  themes. The selected icon/label (`SelectedBottomItem`) is `OnPrimaryContainer` in light and the
  bright neutral `InactiveDarkColor` in dark so it stays legible on the tinted pill.
- **Inactive bottom item in DARK theme (`InactiveBottomItem`)** — pinned to a fixed light grey
  `InactiveDarkColor = #EEEEEE` (was `theme.Outline` at 0.92 alpha, which rendered almost invisible
  on the dark glass). Unselected icons/labels must stay at least this bright in dark mode. Light
  theme keeps dark ink at `InactiveLight=0.82` alpha.

## Drop shadow (SkiaSharp, notch-following)

The bar casts its drop shadow through `G9TabBarShadowView` — an `SKCanvasView` layer
inside the component, drawn *behind* the chrome `GraphicsView`. This replaced both the
zeroed-out chrome `canvas.SetShadow` and the earlier page-level `BoxView` shadow.

Why SkiaSharp and not a native / `Shadow` / Sharpnado shadow: the bar is a **transparent**
`GraphicsView` with a **concave FAB notch** carved into its top edge. Every native-outline
shadow path (Android `setOutlineProvider`, iOS `CALayer` corner-radius shadow, MAUI
`Shadow`, and `Sharpnado.Maui.Shadows` — whose author confirms it follows the view's
*corner radius*, not arbitrary shapes) can only describe a **convex rounded-rect** outline.
That means they either (a) drop the shadow when the transparent shape view re-lays-out
(the "shadow shows on first render, vanishes on tab change" bug), or (b) fill the notch with
shadow colour and kill the scoop. SkiaSharp's `SKMaskFilter.CreateBlur` blurs the *actual
path we draw*, so by drawing the same notched silhouette the chrome draws, the notch stays
a hole in the shadow (scoop preserved) and even gets a soft inner shadow on its curve.
SkiaSharp renders identically on Android / iOS / Windows / MacCatalyst, so it is reliable
everywhere and survives relayout.

Key points:
- `G9TabBarShadowView` mirrors `G9TabBarChromeDrawable.BuildBarPath` exactly (same
  corner radii, same kappa-based notch curve, same four insets) so the shadow lines up
  pixel-for-pixel with the painted bar. If you change the chrome path, change the shadow
  path the same way.
- **The FAB's drop shadow is drawn here too (2026-07)** — a blurred circle in the same Skia
  pass, mirrored from `LayoutFabButton` (`FabCenterX/Y`, `FabRadius` rides the FAB's scale,
  `FabVisibility` fades the circle with the button). The FAB's `Border` carries **no MAUI
  `Shadow`**: its old `Offset=(0,8), Radius=18` shadow rendered as a hard dark crescent
  under the FAB on some devices (MAUI Android shadow blur is device-dependent — open
  upstream dotnet/maui #15565 / #16311; design guide §12b). Do not re-add a `Shadow` to
  `_fabOuterSurface`. **The sub-menu / overflow buttons no longer carry MAUI shadows either** — they
  were removed 2026-07-28 with the app-wide shadow ban (design guide §12b); their glass fill +
  stroke now carry the separation. `G9TabBarColors.SubMenuShadow`/`OverflowShadow` and their
  alpha/opacity constants were deleted with them (superseding the old §12b near-centered
  `Offset 0,2` recipe, which no longer exists anywhere in the app).
- **The whole halo lives INSIDE the control — no negative margin.** Early versions used a
  negative outer margin (`BleedDip`) to bleed the blur past the bar. Android's `SKCanvasView`
  host clips that overflow on every side, so the left / right / top halo was missing (and the
  bottom too, once the bar sat at the screen edge) — the reported "shadow only shows on one
  edge" bug. The fix is to reserve transparent room INSIDE the control on all four sides and
  draw the bar silhouette inset by those amounts, so the soft blur always renders in-bounds:
  - `G9TabBarMetrics.ChromeShadowPadding` (14 dp) — top inset.
  - `G9TabBarMetrics.BarBottomGap` (14 dp) — bottom inset.
  - `G9TabBarMetrics.BarHorizontalGap` (14 dp) — left/right inset.
  The chrome and the shadow both compute the bar rect as
  `[BarHorizontalGap, width - BarHorizontalGap] × [height - BarBottomGap - BarHeight, height - BarBottomGap]`.
  `G9TabBar.LayoutElements`, `ResolveFabCenterX`, and `ResolveIndicatorTarget` all place
  bottom items / FAB / indicator within that same inset span, so everything stays aligned.
  `MainPage` zeroes `MainTabBar`'s left/right margin and trims the bottom margin (the bar's
  own insets now provide the gutter) so the bar keeps its on-screen position. If you change
  any gap, no other edit is needed — every consumer references the constants.
- `G9TabBar.SyncShadow()` pushes the chrome's live geometry (`LayoutHeight`,
  `NotchCenterX`, `CenterProgress`) onto the shadow view and repaints it. It is called
  everywhere the chrome itself is invalidated — `LayoutElements`, the notch open/close
  animation ticks, and `ApplyTheme` — so the shadow tracks the animating notch frame for
  frame.
- The shadow colour is **black at ~0.5 alpha**, hard-coded in `ApplyTheme`. It used to read
  `G9Palette.Current.Shadow`; that token was deleted on 2026-07-28 with the app-wide shadow ban,
  and black is exactly what it resolved to in BOTH the light and dark dictionaries, so the render is
  unchanged. Blur sigma lives in `G9TabBarShadowView.BlurSigmaDip`.
- **Shadow offset.** `G9TabBarShadowView.ShadowOffsetX` / `ShadowOffsetY` default to
  `(0, 0)` so the blurred silhouette stays centered directly under the bar and the soft
  halo wraps every border evenly — left, right, top, AND bottom. Set non-zero only if a
  directional drop cast is wanted (e.g. a downward-only shadow); the symmetric default
  is the look the bar ships with.
- The host page does **not** own a shadow anymore. `MainPage` only keeps the page-content
  scrim (`TabBarScrim`); the bar's depth comes entirely from this in-component shadow.

## Glass separation (no live blur)

The bar reads as a separated panel without any GPU blur library. ArcGIS / OpenGL surfaces and
Android `SurfaceView`-class content cannot be reliably blurred (see Dimezis/BlurView limitations),
so separation is achieved with a layered approach — two pieces inside the bar and one outside:

- **Tinted-acrylic surface (inside the bar)** — `BarBackground` at high alpha (~0.94 light /
  ~0.92 dark) so map tiles, photos, and scrolling content do not bleed through.
- **Top-edge "glass" highlight (inside the bar)** — `BarTopHighlight` paints a 1px inset hairline
  along the top of the bar (and around the notch when the FAB is floating). Bright in light theme,
  subtle in dark theme. Gives the surface a lit upper edge.
- **Page-side scrim (outside the bar, owned by the host page)** — a `BoxView` painted with a
  vertical `LinearGradientBrush` in the page background color, sticky to the bottom, full width,
  height ≈ `BarHeight + 2 × bottom margin`, `InputTransparent="True"`. The cheapest cross-platform
  primitive that produces an "elevated bar" effect without per-frame drawing work or a third-party
  blur library. Identical on Android, iOS, Windows, and macOS. The page can hide it on tabs that
  need a full-bleed background (e.g. the Map tab) and rebuild the gradient when the theme
  background changes.

The component now paints a soft drop shadow via the SkiaSharp `G9TabBarShadowView` layer
(see "Drop shadow" above) — that is what produces the bar's depth/separation from content
*below/around* it. The legacy chrome `canvas.SetShadow` call is still issued in the chrome
drawable but is fully zeroed in `G9TabBarMetrics` (`BarShadow* = 0`), because
`canvas.SetShadow` is unreliable on Android's hardware-accelerated `GraphicsView`. The
page-side **scrim** remains the separator for content *above* the bar.

Tune the in-bar pieces in `G9TabBarColors.cs` (`BarBgAlphaLight/Dark`, `BarStrokeAlphaLight/Dark`,
`BarTopHighlightAlphaLight/Dark`) and `G9TabBarMetrics.cs` (`BarStrokeSize`,
`BarTopHighlightStrokeSize`). Tune the scrim in the host page (e.g. `MainPage.xaml` and
`MainPage.ApplyTabBarScrimGradient`).

> **Page safe-area math** continues to use `BarHeight` (the visible bar height) plus the bar's
> outer bottom margin. The scrim sits behind the bar and is `InputTransparent`, so it does not
> affect hit-testing.

## Outside-tap to close

While `IsFabOpen` or `IsOverflowOpen` is true, an invisible backdrop covers the expanded portion of
the control. A tap that lands on the backdrop (i.e. that misses every button in the expanded zone)
closes both menus and returns the bar to its compact reserved height. Buttons sit at a higher
ZIndex so their own taps are consumed before the backdrop sees them.

## Overflow selection behaviour

Tapping an overflow item promotes its source index to the active selection (`SelectedIndex` is
updated). Because the underlying slot isn't visible in the bar:

- the **selection indicator pill** anchors on the **More** trigger slot (same green pill the bar
  uses for any other tab — selecting from the overflow column lights up More just like selecting
  Feeds lights up Feeds),
- the **More** trigger picks up the selected styling (bold label + accent color),
- the **selected overflow item itself** paints its entire glass shell with the FAB primary
  gradient, so the green selection color spans the whole circle (icon + label flip to OnPrimary
  for contrast). It reads as "this is the live selection" without a separate inner halo.

## Sub-Menu Animation

Each sub-menu item reveals along the horizontal row with:
- **Center-Outward Stagger** — items closest to the FAB center appear first, cascading outward to the edges.
- **Slide** — items travel slightly on the X and Y axes (`SubMenuRowRevealTravelX/Y`) during reveal.
- **Fade** — opacity goes from 0 → 1.
- **Scale** — a subtle ease-out-back scale from ~0.72 → 1.

Tune the row layout and stagger in `G9TabBarMetrics.cs`:
- `SubMenuItemFabRatio`, `SubMenuRowSpacing`, `SubMenuRowAboveFabGap`
- `SubMenuRowRevealTravelX/Y`, `SubMenuRowStaggerStep`, `SubMenuRowStaggerCap`
- `SubMenuRowEdgeGuard`

## Perfect Notch Chrome

The bar notch perfectly mirrors the FAB slot, drawn as an exact semicircle derived from the
`FabSize` and `NotchGap`. When `FabIndex` changes at runtime, the notch and floating button
gracefully animate horizontally to the new position. When `FabIndex = -1`, the bar simply renders
flat without a notch.

### Smooth spring open

When the FAB rises, both the FAB body and the chrome notch animate in parallel through a single
`easeOutBack` curve — one smooth pass with a natural overshoot and settle, no multi-stage stitching
or under-shoot rebounds. Two coefficients control how punchy each one feels:

- `FabPopBackCoefficient` — the FAB's overshoot (default 1.70158, the Material spring default).
- `NotchPopBackCoefficient` — the notch's overshoot (default 3.20, larger so the hole visibly grows
  deeper than the FAB body pops).

Closing uses `Easing.CubicIn` for a clean retract — bouncing back into a flat bar feels jittery, so
no overshoot on close.

## Interaction Polish

- **Jitter-Free Selection:** Non-FAB menu selection updates rely entirely on an animated background pill and color shifts. Label size remains static to avoid layout jitter, keeping the icon perfectly centered.
- **Bold Selected Label:** The currently selected (or overflow-pinned) item switches to `FontAttributes.Bold` so the active section is unmistakable at a glance.
- **Startup Reveal:** The selected item gracefully reveals once after the control loads. Tune it via `StartupSelectionRevealDelayMs` in metrics.
