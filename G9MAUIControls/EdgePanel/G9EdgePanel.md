# G9EdgePanel Usage

`G9EdgePanel` is a modern animated edge peek panel — a compact map-tools drawer that slides from the left or right screen edge with a morphing tab/close button. It supports custom `View` content, navigable list menus with nested sub-lists, dark/light theme awareness, and full LTR/RTL support.

Use `G9EdgePanelHelper` for normal page usage. By default the helper attaches the panel showing **only** the collapsed edge tab — the user taps the tab to slide the panel in, and tapping the close button collapses it back to the tab (the panel stays attached so it can be re-opened). `G9EdgePanelHelper.Dismiss()` detaches the panel entirely. Pass `AutoOpen = true` in the options to expand the panel immediately on attach. Direct XAML embedding is supported for special cases.

The `Side` property maps directly to the absolute visual edge (`Left` = visually left, `Right` = visually right) regardless of the page's `FlowDirection`. RTL/LTR only affects the panel's content (text alignment, back-arrow direction, chevron direction). Page controls outside the collapsed tab stay fully interactive — the wrapper shrinks to the tab footprint when the panel is collapsed.

## Basic XAML

```xml
<customizedG9Edge:G9EdgePanel
    x:Name="MyEdgePanel"
    Side="Left"
    WidthRatio="0.40"
    TopGap="112"
    MaxPanelHeight="560"
    EnableOutsideTapToClose="True"
    UseBackdrop="True"
    ShowCollapsedTab="True"
    FlowDirection="{Binding FlowDirection, Source={x:Reference PageRoot}}" />
```

## Custom View Content

Set `PanelContent` to any `View` to show custom UI inside the panel:

```csharp
MyEdgePanel.PanelContent = new VerticalStackLayout
{
    Spacing = 12,
    Padding = new Thickness(16),
    Children =
    {
        new Label { Text = "Map Tools", FontSize = 18, FontAttributes = FontAttributes.Bold },
        new Label { Text = "Configure your map layers and tools here." },
        new Button { Text = "Locate Me" },
        new Button { Text = "Toggle Layers" }
    }
};
```

## List Menu

Set `MenuItems` to show a navigable list. Supports Material icons, image sources through `G9CachedImage`/FFImageLoading, emoji, localized text, nested sub-lists, and tap callbacks:

```csharp
MyEdgePanel.MenuItems = new List<G9EdgeMenuItem>
{
    new("Locate", MaterialIcons.MyLocation)
    {
        Clicked = item => { /* handle locate */ }
    },
    new("Layers", MaterialIcons.Layers)
    {
        NextList = new List<G9EdgeMenuItem>
        {
            new("Satellite", MaterialIcons.Satellite),
            new("Terrain", MaterialIcons.Terrain),
            new("Hybrid", MaterialIcons.Map)
        }
    },
    new("Radius", MaterialIcons.RadioButtonChecked),
    new("Route", MaterialIcons.Route)
    {
        ShowDividerBelow = true
    },
    new G9EdgeMenuItem
    {
        Emoji = "🌱",
        Text = "Farm Info"
    },
    new G9EdgeMenuItem
    {
        ImageSource = "farm_icon.png",
        Text = "My Farm"
    },
    new G9EdgeMenuItem
    {
        LocalizedTextKey = "MapFarmSelectorTitle"
    }
};
```

### Nested Sub-Lists

When an item has a `NextList`, tapping it replaces the current list with the sub-list and a back button is auto-prepended. This can be nested to any depth:

```csharp
var layerItems = new List<G9EdgeMenuItem>
{
    new("Base Maps", MaterialIcons.Map)
    {
        NextList = new List<G9EdgeMenuItem>
        {
            new("OpenStreetMap", MaterialIcons.Public),
            new("Google Satellite", MaterialIcons.Satellite),
            new("Bing Aerial", MaterialIcons.AirplanemodeActive)
        }
    },
    new("Overlays", MaterialIcons.Layers)
    {
        NextList = new List<G9EdgeMenuItem>
        {
            new("Soil Types", MaterialIcons.Grass),
            new("Water Sources", MaterialIcons.Water),
            new("Roads", MaterialIcons.EditRoad)
        }
    }
};
```

## G9EdgePanelHelper — Call From Anywhere

Use the static `G9EdgePanelHelper` to show panels from any code-behind, ViewModel, or service without needing a reference to a specific panel instance. The helper automatically attaches the panel to the current page.

### Show Custom View

```csharp
var panel = G9EdgePanelHelper.ShowCustomView(
    new Label { Text = "Hello from anywhere!", FontSize = 16 },
    new G9EdgePanelOptions
    {
        Side = G9EdgeSide.Right,
        WidthRatio = 0.45,
        UseBackdrop = true,
        EnableOutsideTapToClose = true
    });
```

### Show List Menu

```csharp
var panel = G9EdgePanelHelper.ShowListMenu(
    new List<G9EdgeMenuItem>
    {
        new("Settings", MaterialIcons.Settings) { Clicked = _ => { /* ... */ } },
        new("Help", MaterialIcons.Help),
        new("About", MaterialIcons.Info)
    },
    new G9EdgePanelOptions
    {
        Side = G9EdgeSide.Left,
        TopGap = 80
    });
```

### List menu — header, forced LTR content, nested headers

Root title uses `MenuHeader` (plain text, dictionary key, or custom `View`). Each item that opens `NextList` can set `SubMenuHeader` for that level. `ContentFlowDirection` forces how rows read without changing `Side`.

```csharp
G9EdgePanelHelper.ShowListMenu(
    new List<G9EdgeMenuItem>
    {
        new("Layers", MaterialIcons.Layers)
        {
            SubMenuHeader = G9EdgeMenuHeader.FromDictionaryKey("MyLayersTitleKey"),
            NextList = new List<G9EdgeMenuItem>
            {
                new("Base maps", MaterialIcons.Map) { BackgroundColor = Colors.GhostWhite, TextColor = Colors.DarkSlateGrey }
            }
        }
    },
    new G9EdgePanelOptions
    {
        Side = G9EdgeSide.Left,
        MenuHeader = G9EdgeMenuHeader.FromText("Tools"),
        ContentFlowDirection = G9EdgePanelContentDirection.LeftToRight
    });
```

### Auto-Open

By default the helper attaches the panel showing only the collapsed tab. Pass `AutoOpen = true` to expand the panel as soon as it's attached.

```csharp
G9EdgePanelHelper.ShowCustomView(
    content,
    new G9EdgePanelOptions
    {
        Side = G9EdgeSide.Left,
        AutoOpen = true
    });
```

### Dismiss

```csharp
G9EdgePanelHelper.Dismiss();
```

Helper-managed panels remain attached when the close tab or backdrop collapses them — only `Dismiss()` (or showing a different panel via `ShowCustomView`/`ShowListMenu`) detaches the panel from the page.

### Live Updates — Changing Content After Setup

The panel supports updating its content after it has been shown. There are three patterns:

**Reassign `MenuItems` or `PanelContent`** — replaces the entire content with a cross-fade transition:
```csharp
panel.MenuItems = newItems;          // full replace, resets navigation stack
panel.PanelContent = newCustomView;  // full replace
```

**Use `ObservableCollection<G9EdgeMenuItem>`** — structural changes (add/remove/replace/reset) are detected automatically via `INotifyCollectionChanged` and the current list level is rebuilt:
```csharp
var items = new ObservableCollection<G9EdgeMenuItem> { ... };
panel.MenuItems = items;
// Later:
items.Add(new G9EdgeMenuItem("New item", MaterialIcons.Add));  // auto-rebuilds
items.RemoveAt(0);                                               // auto-rebuilds
```

**Mutate existing items + call `RefreshMenuItems()`** — for in-place property changes on items that are already in the list (toggling `IsEnabled`, changing `Text`, updating icon/color):
```csharp
someItem.IsEnabled = false;
someItem.Text = "Updated label";
panel.RefreshMenuItems();   // rebuilds the currently visible level
```

`RefreshMenuItems()` rebuilds whichever level is currently visible (root or a sub-list). It does not reset the navigation stack — if the user is inside a sub-list, that sub-list is rebuilt in place.

### Non-Modal Mode

Set `UseBackdrop = false` to show the panel without the modal background layer. In this mode, only the panel and close tab receive input; page controls outside the panel remain interactive.

```csharp
G9EdgePanelHelper.ShowCustomView(
    content,
    new G9EdgePanelOptions
    {
        UseBackdrop = false
    });
```

### Access Active Panel

```csharp
var active = G9EdgePanelHelper.ActivePanel;
if (active is not null)
{
    active.Side = G9EdgeSide.Right;
}
```

## Bindable Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Side` | `G9EdgeSide` | `Left` | Which edge the panel slides from. Two-way bindable. |
| `WidthRatio` | `double` | `0.35` | Panel width as fraction of parent (0–1). Two-way bindable. |
| `TopGap` | `double` | `112` | Distance from top edge (dp). |
| `MaxPanelHeight` | `double` | `0` | Optional absolute max height (dp). `0` = no extra cap beyond ratio + available space. |
| `MaxPanelHeightRatio` | `double` | `0.69` | Max height as a fraction of parent height (0–1). Combined with `MaxPanelHeight` and layout as the most restrictive cap. |
| `IsOpen` | `bool` | `false` | Open/close state. Two-way bindable. |
| `PanelContent` | `View?` | `null` | Custom view displayed inside the panel. |
| `MenuItems` | `IList<G9EdgeMenuItem>?` | `null` | List menu items (replaces PanelContent when set). |
| `PanelBackgroundColor` | `Color?` | `null` | Override panel background (uses theme when null). |
| `TabBackgroundColor` | `Color?` | `null` | Override tab background (uses theme when null). |
| `OpenAnimationDuration` | `uint` | `450` | Expand slide duration (ms). |
| `CloseAnimationDuration` | `uint` | `450` | Collapse slide duration (ms). |
| `AnimationDuration` | `uint` | `450` | Convenience: sets both open and close durations. Getter returns open duration. |
| `EnableOutsideTapToClose` | `bool` | `true` | Whether tapping the backdrop closes the panel. |
| `UseBackdrop` | `bool` | `true` | Whether to render and hit-test the modal backdrop while open/collapsing. |
| `ShowCollapsedTab` | `bool` | `false` | Whether the edge tab remains visible when the panel is closed. |
| `CollapsedTabIcon` | `MaterialIcons?` | `null` | Override the icon on the collapsed tab handle. When null, uses a directional chevron (`ChevronRight`/`ChevronLeft`). The close icon (×) when expanded is always `Close` and is not affected. |
| `CloseButtonPlacement` | `G9EdgeCloseButtonPlacement` | `Inset` | Where the EXPANDED close (×) tab sits relative to the panel's inner corner. `Inset` keeps the legacy look (the close circle sits INSIDE the panel near the corner, offset by `G9EdgePanelMetrics.ExpandedTabInset`). `OnCorner` centres the close circle ON the panel's inner top corner POINT — both horizontally and vertically — so the circle's centre lies on the corner intersection (`panelInnerEdgeX`, `TopGap`), with half the circle inside the panel and half outside. The horizontal offset is achieved by adjusting `_tabRelExpanded` (used as the morph-target via `TranslationX`); the vertical offset is achieved by morphing `TranslationY` from `0` (collapsed Y stays at the slot position `TopGap + TabTopInset`) to `-(TabTopInset + halfTab)` (expanded Y at the corner). The collapsed tab is unchanged in either mode — it still hangs flush off the panel's outer edge at its natural inset Y. When `OnCorner` is set, the sticky-header padding on the close-tab side shrinks from the full 96dp Inset footprint to 28dp (half the tab plus an 8dp breathing margin) so the title gets back the horizontal room the inset reserved. |
| `ContentFlowDirection` | `G9EdgePanelContentDirection` | `MatchApplication` | `MatchApplication`, `LeftToRight`, or `RightToLeft` for text/icons/menu inside the card. |
| `MenuHeader` | `G9EdgeMenuHeader?` | `null` | Optional title row above list items (root list). |
| `MenuHeaderAlignment` | `G9EdgeMenuHeaderAlignment` | `Auto` | Horizontal alignment of the sticky header label / custom view. `Auto` follows `ContentFlowDirection`; `LeftToRight` / `RightToLeft` pin to the absolute physical edge; `Center` balances the title in the middle. |

## G9EdgeMenuHeader

Use **exactly one** of `Text`, `LocalizationKey` (G9StringResources name), or `CustomView`. Factories: `FromText`, `FromDictionaryKey`, `FromView`.

Nested levels: set `G9EdgeMenuItem.SubMenuHeader` on any item that defines `NextList`.

`CustomView` may be a **reused** instance (same object on an item across navigations); the panel removes it from the old menu layer before attaching it to the new one so platforms such as Android never throw “child already has a parent.”

## G9EdgeMenuItem Properties

| Property | Type | Description |
|---|---|---|
| `Icon` | `MaterialIcons?` | Material icon enum (optional). |
| `ImageSource` | `string?` | `G9CachedImage` source path (optional; wraps FFImageLoading). |
| `Emoji` | `string?` | Emoji string (optional, when Icon and ImageSource are null). |
| `Text` | `string?` | Direct display text. Priority over `LocalizedTextKey`. |
| `LocalizedTextKey` | `string?` | G9StringResources resource key for localized text. |
| `NextList` | `IList<G9EdgeMenuItem>?` | Nested sub-list. Auto-generates back navigation. |
| `SubMenuHeader` | `G9EdgeMenuHeader?` | Header when opening this item's `NextList`. |
| `BackgroundColor` | `Color?` | Row background (transparent when null). |
| `TextColor` | `Color?` | Label color (theme default when null). |
| `IconColor` | `Color?` | Leading icon / chevron tint (theme default when null). |
| `Clicked` | `Action<G9EdgeMenuItem>?` | Callback invoked on tap. |
| `Command` | `ICommand?` | Command executed on tap (after Clicked). |
| `CommandParameter` | `object?` | Parameter for Command. |
| `ShowDividerBelow` | `bool` | Draws a thin divider line below this item. |
| `IsEnabled` | `bool` | Disabled items are dimmed and not tappable. Default `true`. |
| `CloseAfterClick` | `bool` | When `true`, the panel collapses to its tab after `Clicked` / `Command` runs. Use for leaf actions whose effect must be visible on the host underneath the panel (e.g. "zoom map to this block" while the panel is in modal `UseBackdrop = true` mode). Ignored when `NextList` is set — drilling in stays on screen. Default `false`. |

## G9EdgePanelOptions Properties

| Property | Type | Description |
|---|---|---|
| `Side` | `G9EdgeSide?` | Edge side (Left/Right is the absolute visual edge — not flipped by RTL). |
| `WidthRatio` | `double?` | Width fraction. |
| `TopGap` | `double?` | Top gap. |
| `MaxPanelHeight` | `double?` | Optional absolute max height; `0`/omit uses ratio + available space only. |
| `MaxPanelHeightRatio` | `double?` | Max height as fraction of parent height (default `0.69`). |
| `AnimationDuration` | `uint?` | Sets both open and close durations when used alone (default 450ms). |
| `OpenAnimationDuration` | `uint?` | Overrides open duration when set (after `AnimationDuration` if both provided). |
| `CloseAnimationDuration` | `uint?` | Overrides close duration when set. |
| `EnableOutsideTapToClose` | `bool?` | Outside tap closes panel. |
| `UseBackdrop` | `bool?` | Shows/hides the modal backdrop. |
| `ShowCollapsedTab` | `bool?` | Keeps the collapsed edge tab visible after close. Defaults to `true` for helper-managed panels. |
| `AutoOpen` | `bool?` | Expand the panel immediately on attach. Default `false` — only the collapsed tab is visible until tapped. |
| `PanelBackgroundColor` | `Color?` | Override panel bg. |
| `TabBackgroundColor` | `Color?` | Override tab bg. |
| `ContentFlowDirection` | `G9EdgePanelContentDirection?` | Panel content direction; omit to match app culture. |
| `MenuHeader` | `G9EdgeMenuHeader?` | Root list header (helper / options). |
| `MenuHeaderAlignment` | `G9EdgeMenuHeaderAlignment?` | Alignment for the sticky header label. `null` keeps the panel default (`Auto`). |
| `CollapsedTabIcon` | `MaterialIcons?` | Override the collapsed tab icon. Null = directional chevron. |
| `CloseButtonPlacement` | `G9EdgeCloseButtonPlacement?` | `Inset` (default) keeps the close circle inside the panel near the corner. `OnCorner` centres the close circle ON the panel's inner corner edge (half inside / half outside the panel). |

## G9EdgePanelContentDirection

| Value | Meaning |
|---|---|
| `MatchApplication` | Same as active app culture (`CurrentFlowDirection` / `G9Culture`). |
| `LeftToRight` | Force LTR inside the panel (e.g. numeric or English data in an RTL app). |
| `RightToLeft` | Force RTL inside the panel. |

## Behavior Details

### Animation
- **Duration**: 450ms default for open and close, configurable via `OpenAnimationDuration` / `CloseAnimationDuration` or together via `AnimationDuration`.
- **Collapsed tab entrance**: When `ShowCollapsedTab` is true, the first time the tab appears in tab-only mode (after attach or after turning the tab back on), it slides in from just off-screen (~320ms). Detaching resets this so the next attach can play again.
- **Easing**: `CubicOut` for open, `CubicIn` for close. The matched ease pair gives the panel a smooth start on open and a smooth deceleration into the wall on close.
- **GPU-only slide**: The slide animation drives the panel and tab via `TranslationX` only — no per-frame `SetLayoutBounds`, no per-frame shape allocation. Layout bounds and the round-rectangle stroke shapes are committed once when the wrapper enters fill mode (and again only when the side / width changes) so each animation frame is a free GPU compositing op. Cached `OuterMode` state guards against re-applying `HorizontalOptions`, `Margin`, `WidthRequest`, etc. on frames where those properties haven't actually changed.
- **Tab Morph**: The tab morphs continuously from a 48×64 chevron handle to a 40×40 close circle through the slide. The corner-radius and size morphs run on a **single reused `RoundRectangle` instance** (`_tabShapeMorphing`) — its `CornerRadius` struct is updated in-place each frame instead of allocating a new shape. Quantized-key caches skip the property write when no corner moved by ≥0.5dp and skip the size write when no edge moved by ≥1dp. Keeps the smooth continuous shape transition while eliminating the per-frame allocation churn that caused judder in earlier versions.
- **Tab tracking**: The tab's per-frame X is composed as `panel slide + tab edge offset` and applied via `TranslationX`. Same transform pipeline as the panel, so the tab stays locked frame-for-frame with the panel edge. The collapsed-relative offset is set to **exactly the panel width** (no extra gap) so the tab stays glued to the panel's outer edge through the entire close — no late-close gap visible during the final frames.
- **Content fade on close**: The card content (`_panelCardContent`) fades to opacity 0 over the first ~35% of the close animation (`CubicIn`, minimum `ContentFadeOnCloseMinMs`). The remaining 65% slides a flat colored rectangle, freeing the GPU from rendering the menu list during the high-acceleration tail of the close. Eliminates the late-close stutter most visible on lower-end Android devices.
- **Tab Fade**: When `ShowCollapsedTab` is false, the tab opacity ramps from 0→1 over the first ~30% of the open animation (and reverses on close), so the arrow does not pop in suddenly when the panel is first attached. When `ShowCollapsedTab` is true the persistent tab stays solid.
- **Wall flush**: Both the panel card and the collapsed tab extend `WallOverlapDp` (2dp) behind the screen wall when fully positioned. This hides the stroke hairline on the wall-facing edge so the panel and wall look seamlessly joined.
- **Corner Radius**: Corner values use MAUI's `TopLeft, TopRight, BottomLeft, BottomRight` order. Left collapsed tab is `0, 18, 0, 18`; right collapsed is `18, 0, 18, 0`; expanded is full circle. All morphing is done on the single `_tabShapeMorphing` instance — no allocations during animation.
- **Icon**: Chevron arrow (pointing into screen) when collapsed → X when expanded. Swaps at the midpoint of animation.
- **Hit Testing**: When the panel is collapsed, the wrapper shrinks to the tab footprint and is positioned via `HorizontalOptions=Start/End` (flipped for RTL parents) so taps anywhere outside the tab still reach the page below — Android's `InputTransparent` flag on a parent native View blocks taps from reaching children even with `CascadeInputTransparent=false`, so a full-page input-transparent wrapper would either swallow page taps or fail to deliver them to the tab. When the panel is expanding/expanded, the wrapper grows to fill so the slide animation, panel card, and backdrop tap-to-close all work.
- **Tap Debounce**: Tab and backdrop taps are ignored while the slide animation is running and within a short debounce window of the last accepted interaction. This guards against Android firing the same logical tap on both the tab and the backdrop (which would otherwise close-then-reopen the panel during the close animation).
- **Menu transition**: Forward/back between menu levels uses a synchronized cross-fade (200ms, `CubicInOut`). The incoming menu is **attached to the visual tree before being measured** — an unattached view has no platform handler and no font resolution context on Android, so its `Measure` returns the height of just the layout chrome (close button row), which is what made earlier versions briefly collapse to ~80dp before snapping back to the full content height. Now the dispatcher yields one pass for handlers to attach, then the real measured height is used for the parallel height tween. The card height is held at the tween's final value through the cross-fade so the card never re-auto-sizes mid-animation.
- **First-open spinner**: On the very first panel open, the panel's height is **deterministically estimated from `MenuItems` count** (header height + N × `MenuItemHeight` + dividers + padding) and pinned via `HeightRequest` before the slide starts. For custom `PanelContent` the platform `Measure` is used as a fallback. A `G9ActivityIndicator` is shown **centered** in the full-height card during the slide instead of the menu list (deferring heavy rendering of menu rows / icons until the panel has arrived), then swapped for the real content with no height change because the card was already sized correctly. A `_firstOpenPreSizeActive` flag guards `ApplySideLayout` and `EnterFillChildState` from resetting `HeightRequest` or switching the `AbsoluteLayout` bounds height back to `AutoSize` while the spinner is showing — either of those would collapse the card to the spinner's 72dp minimum mid-slide. Subsequent opens show content immediately at its natural size.
- **Panel height smoothing**: Before the outgoing list is removed (or before an immediate content replace), the card **freezes** its current height so the layout cannot flash shorter for a frame. The new content's height is obtained via **measure after attach** (not before — see "Menu transition" above), then the card tweens from the frozen height to that target (~240ms, `CubicOut`). For the menu transition this height tween runs in parallel with the content fade, not sequentially after it.
- **Translation Rule**: The panel card is laid out at its final on-screen position and translated off-screen for animation. This follows MAUI's documented guidance that translated interactive views should be laid out at their final position so the visual and input layouts match after animation.

### RTL / LTR Support
- `Side` maps to the absolute visual edge: `G9EdgeSide.Left` is always the visually-left edge, `G9EdgeSide.Right` is always the visually-right edge — independent of the page's `FlowDirection`. The panel's internal `AbsoluteLayout` is locked to LeftToRight so positions are deterministic.
- **Collapsed tab vs app direction**: `HorizontalOptions` Start/End for the small tab-only wrapper follows the **page** flow direction. That direction is aligned with `G9Culture` (same source as `CurrentFlowDirection`). The panel subscribes to `G9Culture.CultureChanged` so after an in-app language switch the tab stays on the correct physical edge with correct corner radii — thread `CurrentUICulture` alone is not used for this.
- **Content direction**: Text, icons, menu rows, back arrow, and chevrons follow `ContentFlowDirection`, or app culture when `MatchApplication`. Back arrow: `ArrowBack` in LTR / `ArrowForward` in RTL content; chevron for `NextList`: `ChevronRight` / `ChevronLeft` accordingly.
- Menu forward/back: the parallax direction (which side incoming arrives from) follows `ContentFlowDirection`. Forward = incoming from trailing edge; Back = incoming from leading edge.

### Theme Support
- Follows `G9Palette.Current` — automatically updates when the app theme changes.
- Panel background matches the FarmSelector toolbar style (SurfaceContainerLowest in light, Dark in dark).
- All colors are centralized in [`G9EdgePanelColors.cs`](G9EdgePanelColors.cs).
- All sizing constants are centralized in [`G9EdgePanelMetrics.cs`](G9EdgePanelMetrics.cs).

### Performance
- Lightweight MAUI controls only (Border, Grid, ScrollView, Label) plus this suite's own wrappers for shared primitives.
- No WebView, no HTML/CSS, no heavy reflection.
- AOT-safe — no dynamic code generation.
- Menu list rebuilds only when `MenuItems` changes or a navigation occurs.
- `G9CachedImage` used for image items with standard FFImageLoading cache/downsample settings.
- Helper-managed panels stay attached after close so the user can re-open by tapping the tab. Call `G9EdgePanelHelper.Dismiss()` (or show a different panel via the helper) to detach.
- Panel card auto-sizes its height to its content, capped by `MaxPanelHeight`. Content that exceeds the cap scrolls inside the card via the internal `ScrollView`.
- Panel card and inner sub-views are hidden (`IsVisible = false`) and input-transparent when fully collapsed to skip rendering and hit testing for that subtree.
- **Per-frame cost is minimal**: the slide animation only writes `TranslationX` (GPU compositing, no layout pass) and occasionally `Opacity`. All layout-heavy operations (`SetLayoutBounds`, `HorizontalOptions`, `Margin`, `WidthRequest`) are guarded by `OuterMode` state and only applied on mode transitions, not every frame.
- **Zero per-frame allocations**: the tab morph reuses a single `RoundRectangle` instance; corner-radius and size writes are skipped by quantized-key caches when the value hasn't moved by a perceptible amount.

## Map Component Integration

The ArcGIS map component (`Common/Components/Map/ArcGisMapView`) exposes an opt-in slot for `G9EdgePanel` through `MapEdgePanelOptions` on `MapToolsOptions.EdgePanel`. When enabled, the map's tools host renders the panel as a sibling overlay of the `MapView`; consumer code never instantiates the panel directly.

```csharp
controller.Configure(new MapSessionOptions
{
    InteractionHandler = scenarioHandler,
    // ... other options
    Tools = new MapToolsOptions
    {
        EdgePanel = new MapEdgePanelOptions
        {
            IsEnabled = true,
            Side = G9EdgeSide.Left,
            ShowCollapsedTab = true,
            UseBackdrop = true,           // modal mode: dim layer + outside-tap-to-close
            MaxPanelHeightRatio = 0.40,   // ~40% of map height
            TopGap = 112,                 // clear page tab bar
            MenuHeader = G9EdgeMenuHeader.FromText("Blocks"),
            MenuHeaderAlignment = G9EdgeMenuHeaderAlignment.Center,        // balanced title in EN/FA
            CloseButtonPlacement = G9EdgeCloseButtonPlacement.OnCorner,    // halo on the corner, not a button on the rail
            MenuItemsProvider = MyNavigator.CreateMenuItemsProvider(controller, item)
        }
    }
});
```

`MenuItemsProvider` is invoked once after the panel attaches so heavy DB / sync queries don't block the map session opening. Pair menu callbacks with the controller's navigation helpers so a pick zooms the map and (optionally) re-uses the standard tap flow:

- `controller.ZoomToLayerFeatureAsync(layerName, featureId)` — animate to a feature.
- `controller.SelectLayerFeatureAsync(layerName, featureId)` — clear-then-select.
- `controller.TryEmitMapTapAsync(layerName, featureId)` — synthesize a tap so the existing bottom-sheet flow runs without duplicating its open logic.

There is **no shipping consumer of this slot right now**: the sampling map's block→pot navigator (`Views/Pages/Tasks/SamplingMapNavigator.cs`) was removed on 2026-07-27 along with the sampling map's second `Configure` call, so `MapToolsOptions.EdgePanel` is left at `MapEdgePanelOptions.Disabled` everywhere. The pattern it used is still the right one for a new consumer: build the menu in a provider, and let the controller's helpers (above) do the map work rather than calling back into the panel imperatively.

### `UseBackdrop` decision matrix

The map host always sets `EnableOutsideTapToClose = true`, but the actual outside-tap-to-close behaviour depends on `UseBackdrop`:

| `UseBackdrop` | Empty-area taps | Map under panel | Outside-tap-to-close | Visual |
|---|---|---|---|---|
| `true` (default) | Captured by the backdrop layer | Frozen while panel is open | Works — backdrop relays the tap | Dimmed modal feel |
| `false` | Pass through to the map | Pan / pinch / select still work | Does NOT work — taps never reach the panel | Floating overlay |

Use `UseBackdrop = false` when the user must keep interacting with the map while the panel stays open (e.g. a passive cluster legend). Use `UseBackdrop = true` for any panel that is meant to **own input until dismissed** (e.g. block / pot navigator). Picking the wrong mode is the most common reason "the panel doesn't capture taps" — see the next section.

## Who must use this control / pitfall checklist

If you're embedding `G9EdgePanel` somewhere new and clicks on the panel (close button, tab, list rows) seem to fall through to the layer underneath, **read this section first**.

### When should I use `G9EdgePanel`?
- A modal-feeling drawer that slides from the screen edge with a peek tab. The only live example left in this codebase is the design-area mock pages (helper-managed list menus over a normal page) — the sampling task map's **Blocks** navigator, which used to be the reference consumer, was removed on 2026-07-27.
- For non-modal, always-visible side rails or static toolbars use a regular `Border` + `VerticalStackLayout` instead — the slide animation, morphing tab, and backdrop machinery here are pure overhead in that case.

### Pitfall: clicks fall through to the host (close button, tab, or list rows do nothing)

**Symptom**: the panel renders, but tapping the × button, the collapsed tab, or any menu row does nothing — the host (the page or the map) appears to receive the touch instead.

**Root cause**: the panel's outer `ContentView` and inner `_root` `AbsoluteLayout` ship with `InputTransparent = true` + `CascadeInputTransparent = false`. That combo is a deliberate trick: the wrapper does not eat taps in empty areas (so a non-modal panel lets the page underneath stay interactive), and the panel card / tab keep `InputTransparent = false` so they still hit-test thanks to `CascadeInputTransparent = false`.

**That trick has a Windows MAUI / Android caveat**: when a parent native peer is marked `IsHitTestVisible = false` (`InputTransparent = true`), the platform stops dispatching pointer events into the subtree even when `CascadeInputTransparent = false` says descendants should still be hit-testable. The result is that any **translated** child (the panel card slides via `TranslationX`) becomes un-tappable.

**Fix encoded into the panel**: while in fill mode (`ApplyOuterAsFill`), the outer `ContentView` is forced to `InputTransparent = false` regardless of the backdrop choice; only `_root.InputTransparent` keeps the `!captureBackdropInput` logic so empty-area taps still pass through to the map when no backdrop is in play. Do not override these flags from outside the panel — they are the contract.

**Fix from your end (consumer)**: if you are embedding the panel and clicks still don't reach it, the answer is almost always **set `UseBackdrop = true`**. With the backdrop on, the wrapper is unambiguously input-opaque while the panel is open and every platform routes taps correctly. Use `UseBackdrop = false` only when you have a real reason to need click-through and you have tested all four edges + the close button on the platforms you ship.

**Other things to check before reaching for layout hacks**:

1. The panel's parent must not itself set `InputTransparent = true`. The map host's `MapToolsHost` grid does set `InputTransparent = true`, but with `CascadeInputTransparent = false`; the panel's own outer-mode handling fixes the rest. Other hosts must follow the same pattern or just leave the parent input-opaque.
2. The panel card slides via `TranslationX` and is laid out at its final on-screen position. Translated interactive views are hit-tested at their **rendered** position on iOS / Android / Windows, but only when no parent has flipped `IsHitTestVisible` off. See above.
3. If you wrap the panel in a host that toggles `IsVisible` on the wrapper across the open/close lifecycle, the panel will be input-capturing-but-unrendered for one frame on Android. Use the panel's own `IsOpen` flag (or the helper) to drive visibility — `G9EdgePanel` itself never toggles `IsVisible` on its outer wrapper for this reason.
4. Tap debounce: tab and backdrop taps within ~220ms of the last accepted interaction are dropped on purpose to defeat Android's double-tap-firing on tab + backdrop. If your test taps faster than that you will see them ignored — wait or re-tap.

### Pitfall: title looks off-centre in RTL

Drive title alignment with `MenuHeaderAlignment`:

- `Auto` (default) sits the label on the leading edge of `ContentFlowDirection`, so the title naturally swaps from left in English to right in Persian alongside the menu rows.
- `LeftToRight` / `RightToLeft` are absolute physical edges that do **not** flip with `ContentFlowDirection`.
- `Center` balances the title in the middle of the panel and reserves padding equal to the close-tab footprint on both sides so the label can never overlap the × button regardless of which edge the panel sits on.

If the title still looks off-centre after picking an alignment, it is almost always because a custom `MenuHeader.CustomView` is forcing its own `HorizontalOptions = Start` / `Fill` — switch the custom header view to `Center` (or whichever alignment matches the panel's `MenuHeaderAlignment`).

### Pitfall: menu item tap "does nothing"

If you click a menu row and the host (e.g. the map) appears unchanged, run through this checklist before assuming the click was lost:

1. **Is the panel covering what you expect to see change?** With `UseBackdrop = true` the panel is a modal drawer over the host. A `Clicked` callback that zooms the map runs immediately, but the user sees no movement because the panel is still on screen with the dim layer over the map. Set `CloseAfterClick = true` on leaf rows so the panel collapses after the action and the host effect is visible. Sub-list items (`NextList` set) ignore `CloseAfterClick` because drilling in IS the visual feedback.
2. **Is the row really enabled?** Disabled rows skip wiring `TapGestureRecognizer` entirely — the row dims to 0.5 opacity, but a quick glance can miss that. Toggle `IsEnabled = true`.
3. **Where to put a breakpoint?** Item taps land in `G9EdgePanel.OnMenuItemTapped(G9EdgeMenuItem item)` (the only method that fans out to `item.Clicked`, `item.Command`, sub-list navigation, and the `CloseAfterClick` close). The TapGestureRecognizer that calls it is wired inside `BuildMenuRow` (search for `OnMenuItemTapped(item)`). If the breakpoint there never hits, the gesture itself isn't reaching the row — typical causes are an `InputTransparent = true` ancestor (see "clicks fall through" above) or the row being disabled.
4. **Async callbacks**: `Clicked` is an `Action<G9EdgeMenuItem>` — if your handler does async work, fire-and-forget the task (`_ = MyAsync()`, or your own small fire-and-forget helper that logs the fault). Awaiting from inside the synchronous lambda silently drops exceptions and won't block the panel from closing if `CloseAfterClick = true`.

## File Structure

```
Common/Components/G9Edge/
├── G9EdgePanel.cs                  # Main control
├── G9EdgePanel.md                  # This guide
├── G9EdgePanelColors.cs            # Theme-aware color recipes
├── G9EdgePanelContentDirection.cs  # MatchApplication / LTR / RTL for panel content
├── G9EdgePanelHelper.cs            # Static helper + G9EdgePanelOptions
├── G9EdgePanelMetrics.cs           # Sizing constants
├── G9EdgeCloseButtonPlacement.cs   # Inset (legacy) / OnCorner (centered on corner) modes
├── G9EdgeMenuHeader.cs             # Text / dictionary key / View header model
├── G9EdgeMenuHeaderAlignment.cs    # Auto / LTR / RTL / Center
├── G9EdgeMenuItem.cs               # Menu item model
├── G9EdgeSide.cs                   # Left/Right enum
└── G9MenuTransitionDirection.cs        # Forward/Back/None for menu slide
```

## Customization

### Colors
Edit [`G9EdgePanelColors.cs`](G9EdgePanelColors.cs) to change any color recipe. All alpha values and theme branches are in one file.

### Sizing
Edit [`G9EdgePanelMetrics.cs`](G9EdgePanelMetrics.cs) to change tab sizes, panel corner radius, animation durations, menu item heights, etc.

### Width / Position
- `WidthRatio` — fraction of parent width (e.g. `0.40` = 40%).
- `TopGap` — distance from top in dp (e.g. `112` to clear a toolbar).
- `MaxPanelHeight` — max height before scrolling kicks in.
