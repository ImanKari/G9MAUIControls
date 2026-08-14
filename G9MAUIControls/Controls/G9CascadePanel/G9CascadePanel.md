# G9CascadePanel

A nested / cascading sliding-panel stack — the in-place **drill-down navigation**
pattern (Material 3 / iOS push navigation, Foundation's "drilldown", the cascading-menu /
master-detail stack). Pushing a nested view slides a fresh panel in **on top of** the
current one; popping slides it back off. Panels stack to arbitrary depth.

It looks like the bottom sheet's open/close motion, but with two key differences:

- **It is not full-screen.** `G9CascadePanel` lives inside whatever parent you place it in
  (a grid cell, a card slot, a column) and fills exactly that container. The slide is
  clipped to the panel's own bounds — nothing escapes the host.
- **It can open from any edge.** Nested panels can enter left→right, right→left,
  top→bottom, or bottom→top, and they stack repeatedly on top of each other.

## What it replaces

There is no off-the-shelf MAUI control for an in-place drill-down stack. Before this,
the only options were a full-screen `NavigationPage` push (wrong — it takes over the whole
screen) or a hand-rolled `Grid` + manual `TranslationX` juggling per screen. `G9CascadePanel`
packages the slide, the stack management, the content-based scroll, the lazy loading, the
back-navigation header, and the RTL handling into one control.

## Behaviour at a glance

| Capability | How |
|---|---|
| **Directional entry** | `Direction` (or a per-push override) — `Auto` / `LeftToRight` / `RightToLeft` / `TopToBottom` / `BottomToTop`. `Auto` resolves to the reading direction (L→R in LTR, R→L in RTL). |
| **Two transitions** | `Transition` — `Overlay` (default): new panel slides in over a stationary base; `Push`: base slides out as the new panel slides in (conveyor replace). |
| **Arbitrary nesting** | Each `Push` adds a level; `Pop` removes the top; `PopToRoot` collapses back to depth 0. |
| **Content-based scroll** | Every panel wraps its content in a `ScrollView`. Tall content scrolls; short content doesn't. |
| **Lazy loading** | `Push(Func<View>, …)` and `RootContentFactory` show a spinner and build the view one tick after the slide — same deferred-content idea as the bottom sheet. |
| **Fixed-vs-animated root** | The root view appears with no animation by default (`AnimateRoot = false`); nested panels always animate. |
| **Depth parallax (Overlay only)** | In `Overlay` mode the covered panel parallaxes + dims (`EnableParallax`, on by default) for an iOS-style depth cue. |
| **Built-in back header** | Optional `ShowHeader` row with a culture-aware back chevron + title; `ShowRootHeader` adds a (back-less) header to the root too. |

## Bindable properties

| Property | Type | Default | Effect |
|---|---|---|---|
| `RootContent` | `View?` | `null` | The view shown at depth 0. Swapping it live replaces the root panel without disturbing the nested stack. |
| `RootTitle` | `string?` | `null` | Title shown in the root header (when `ShowRootHeader` is true). |
| `Direction` | `G9CascadeDirection` | `Auto` | Default entry direction for nested panels. `Auto` = reading direction. |
| `Transition` | `G9CascadeTransition` | `Overlay` | `Overlay` slides the new panel in over a stationary base; `Push` slides the base out as the new panel slides in. |
| `AnimateRoot` | `bool` | `false` | When true the root fades in on first appearance; otherwise it's fixed (no animation). |
| `ShowHeader` | `bool` | `true` | Show the built-in back+title header on **nested** panels. |
| `ShowRootHeader` | `bool` | `false` | Also show a header on the root panel (no back affordance — the root has nowhere to go). |
| `AnimationDurationMs` | `uint` | `G9Metrics.CascadePanelAnimationMs` (280) | Slide duration for push / pop. |
| `EnableParallax` | `bool` | `true` | In `Overlay` mode, parallax + dim the covered panel during a push. No effect in `Push` mode. |
| `CornerRadius` | `double` | `G9Metrics.RadiusLg` (16) | Corner radius of the clipped panel surface. |

`RootContentFactory` (`Func<View>?`, a plain CLR property, not bindable) is the lazy
equivalent of `RootContent`: when set and `RootContent` is null, the root panel shows a
spinner and builds the factory one tick after load.

## Methods

| Method | Description |
|---|---|
| `Push(View content, string? title = null, G9CascadeDirection? direction = null)` | Push a built view as a new nested panel. |
| `Push(Func<View> factory, string? title = null, G9CascadeDirection? direction = null)` | Push a lazily-built view; spinner shows until the factory returns. |
| `PushAsync(View?, Func<View>?, string?, G9CascadeDirection?)` | Awaitable push; completes when the slide-in finishes. |
| `Pop()` / `PopAsync()` | Pop the top nested panel. No-op at the root. |
| `PopToRoot()` / `PopToRootAsync()` | Pop every nested panel back to the root. |

`Depth` (`int`, read-only) — current nesting depth; 0 means only the root is showing.

## Events

| Event | Argument | When |
|---|---|---|
| `PanelPushed` | `int` (new depth) | After a nested panel finishes sliding in. |
| `PanelPopped` | `int` (new depth) | After a nested panel finishes sliding out. |

## Usage — built content

```xml
<Border HeightRequest="360" Padding="0" StrokeShape="RoundRectangle 16">
    <newControls:G9CascadePanel x:Name="RegionPanel" ShowRootHeader="True" />
</Border>
```

```csharp
RegionPanel.RootTitle = "Regions";
RegionPanel.RootContent = BuildRegionList(); // a VerticalStackLayout of AppNavCards

// From a row tap inside the root list, drill into the next level:
card.Tapped += (_, _) => RegionPanel.Push(BuildFarmsPanel(region), title: region);
```

## Usage — lazy nested panel

```csharp
// Slides in immediately with a spinner; the heavy view is built one tick later.
RegionPanel.Push(
    () => BuildExpensiveDetailView(farm),
    title: farm.Name);
```

## Usage — directional override

```csharp
// This particular push enters from the bottom regardless of the control's Direction.
RegionPanel.Push(BuildFilterPanel(), title: "Filters", direction: G9CascadeDirection.BottomToTop);
```

## RTL

- `Direction = Auto` (the default) drills **right→left in RTL**, left→right in LTR — the
  natural reading-direction push.
- The translating layer is locked to `LeftToRight` so the slide math is always in physical
  pixels (same approach as `G9TabView` / `G9RangeSlider`). Panel **content** follows the
  active culture, so text, list rows, and the back chevron all mirror correctly. The back
  chevron points to the leading edge in both directions (`ChevronLeft` in LTR,
  `ChevronRight` in RTL).
- A culture flip while panels are open re-skins every level's header and content flow
  direction in place via the `OnCultureChangedHook`.

## Behaviour notes

- **Opaque panels.** Each panel surface is painted with `G9Palette.Surface` so the
  incoming panel fully occludes the one beneath during the slide — no see-through bleed.
- **Clip-safe.** The whole stack is clipped to the host `Border` via `IsClippedToBounds`,
  so a panel sliding off-screen never paints outside the container (matters on Android,
  which clips children to parent bounds anyway, and keeps the desktop slide tidy).
- **Size resolution & the no-snap fix.** The slide distance is the panel's own measured
  size. A freshly pushed panel is added to the tree first, then the control waits for its
  container's first non-zero `SizeChanged` before parking it off-screen and animating —
  setting `TranslationX/Y` before the platform handler is connected is silently dropped,
  which would make the panel snap into place with no motion (G9Controls.md §15 W2). A
  120 ms fallback keeps a collapsed-parent push from hanging.
- **Re-entrancy guard.** Push / pop are guarded by an `_animating` flag so rapid taps can't
  interleave two slides.

See `G9Controls.md` for the shared architecture (base class, metrics, RTL strategy,
destruction-free animation, and the platform pitfall catalog).
