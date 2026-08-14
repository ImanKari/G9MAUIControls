# G9SwipeView

`G9SwipeView` is a `ContentView` wrapper around `Microsoft.Maui.Controls.SwipeView`
that adds the four polish layers the native control lacks: card-aware clipping,
icon glyphs that always render, theme-aware colors, and culture-aware refresh that
keeps the leading-edge action consistent across LTR / RTL layouts. On **mobile**
(Android / iOS / MacCatalyst) the native SwipeView is reused unchanged underneath —
we never reimplement its gesture or reveal animation. On **Windows** the native
`SwipeControl` crashes and only supports touch, so the swipe is reimplemented as a
custom mouse-draggable drag-to-reveal — see "Platform behaviour" below.

## When to use

- Swipe-to-reveal row actions (edit / delete / archive) -> `G9SwipeView` with
  declarative `G9SwipeAction` items in `LeftActions` / `RightActions`.
- Because `G9SwipeAction` is a plain model (not a `BindableObject`), inside a
  `DataTemplate` push the row's data item onto each action's `CommandParameter` from the
  `G9SwipeView.BindingContextChanged` handler so the `Invoked` handler can resolve it.

## Why a wrapper instead of a subclass

`Microsoft.Maui.Controls.SwipeView` exposes its action collections as
`SwipeItems` directly under `LeftItems` / `RightItems`. Subclassing the native control
forces consumers to author full `SwipeItem` / `SwipeItemView` markup per row, which
defeats the goal of declarative `G9SwipeAction` models. By composing instead of
inheriting, the public API stays "give me a list of `G9SwipeAction`s" and the
control owns the visual translation to native `SwipeItemView` instances.

## Bindable Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `CardContent` | `View?` | `null` | The visible card body. XAML's content-property syntax (`<newControls:G9SwipeView>...</>`) routes here automatically. |
| `CardBackground` | `Color?` | `null` | Fill painted by the outer frame + its rounded clip. Pair it with a body that is **flat but still OPAQUE** (`ListCard Flat="True"`) — the frame does not move when the row is swiped, so a see-through body lets the action pane show through it. See "Card chrome" below. |
| `CardStroke` | `Color?` | `null` | Border color painted by the outer frame. Pairs with `CardStrokeThickness`. |
| `CardStrokeThickness` | `double` | `0` | Border thickness for `CardStroke`. |
| `CornerRadius` | `double` | `14` (= `RadiusMd`) | Outer card corner radius. The action panes that the native SwipeView paints are clipped to this radius via the wrapper's `Border.StrokeShape` + auto-clip. |
| `LeftActions` | `ObservableCollection<G9SwipeAction>` | empty | Actions revealed on the **leading** edge — physical left in LTR, physical right in RTL. |
| `RightActions` | `ObservableCollection<G9SwipeAction>` | empty | Actions revealed on the **trailing** edge — physical right in LTR, physical left in RTL. |

### Card chrome — the body must be FLAT *and* OPAQUE

> **THE RULE: the swipe body carries an opaque fill, a square edge, and no stroke.**
> Use `CollectionViews.ListCard` with `Flat="True"` (or a `SampleItem`, which derives from it) —
> it is exactly that. The wrapper keeps `CardBackground` / `CardStroke` / `CornerRadius`.

Two independent things go wrong if you get this wrong, and the app shipped BOTH:

1. **A body with NO FILL leaks the action pane through itself.** The control translates the
   **body**; the wrapper's `Border` (which paints `CardBackground`) does **not** move. So a
   transparent body — a bare `Grid`, or the old `SampleItem.Flat` that set
   `BackgroundColor = Transparent` — has nothing to cover the revealed pane with: the row's own
   text and avatars slide **across the Edit / Delete buttons**. This was the reported bug on the
   transfer-detail and batch rows. An opaque body covers the pane as it moves, which is the entire
   point of the sliding card.
2. **A ROUNDED, STROKED body doesn't meet the pane cleanly.** On reveal, a rounded inner corner
   sits against the square pane and the corners don't line up; and because the wrapper clips to its
   own radius, a stroked round corner gets sliced, leaving the arc unstroked. A square, strokeless
   body meets the pane flush and the wrapper's rounded clip supplies the card's silhouette.

So: **fill = yes, radius = no, stroke = no.** `ListCard.Flat` encodes precisely that — do not
re-derive it per row, and do not "clean up" the fill out of it.

## `G9SwipeAction` model

| Property | Type | Default | Description |
|---|---|---|---|
| `Text` | `string?` | `null` | Label rendered below the icon. Localize before assignment. |
| `MaterialIcon` | `MauiIcons.Material.MaterialIcons?` | `null` | Glyph from the bundled Material Icons font. |
| `Background` | `Color?` | `null` | When null, resolves to `G9Palette.Primary` (or `G9Palette.Error` when `IsDestructive` is true). |
| `Foreground` | `Color?` | `null` | When null, resolves to `G9Palette.OnPrimary` (or `G9Palette.OnError`). Drives icon glyph + label color. |
| `IsDestructive` | `bool` | `false` | Switches the auto color resolution to the Error palette. Use for delete / cancel / archive-style actions. |
| `IsVisible` | `bool` | `true` | Hide the pane without removing it from the collection. |
| `IsEnabled` | `bool` | `true` | Reject taps without dimming the visual (set `Background` / `Foreground` manually if you want a disabled tint). |
| `IconSize` | `double` | `22` | Glyph size in dp. |
| `WidthRequest` | `double` | `88` (= `G9SwipeView.DefaultActionWidth`) | Pane width. The native SwipeItem ignores child measure, so a hard width is the only horizontal sizing knob. |
| `Command` | `ICommand?` | `null` | Fires when the pane is tapped. |
| `CommandParameter` | `object?` | `null` | Passed to `Command.Execute(...)`. |

`G9SwipeAction.Invoked` is also exposed as an event for code-behind wiring without
ICommand plumbing.

## Behaviors

### Card-aware clipping

The wrapper's outer `Border` has `StrokeShape` = `RoundRectangle(CornerRadius)` and
its content slot holds the native `SwipeView`. The action panes are painted by the
native renderer as rectangles, but the parent `Border` clips them to the rounded
shape — no more rectangular bleed above and below the card's rounded corners.

### Native font icons that always render

Each pane is a `SwipeItemView` whose content is a `Grid` with the action's
background color, holding a `VerticalStackLayout` of `Image` (with a
`FontImageSource` source) + `Label`. The `FontImageSource` is built using the
exact contract `MauiIcons.Material`'s `<icons:Material .../>` markup extension
uses internally:

- `Glyph` = the `[Description]` attribute on the `MaterialIcons` enum value
  (the actual UTF-8 glyph string, e.g. `"\uE3C9"` for `Edit`).
- `FontFamily` = the enum type's simple name (`"MaterialIcons"`), the alias
  registered by `UseMaterialMauiIcons()` via `AddEmbeddedResourceFont`.

Earlier attempts that placed a `MauiIcon` View directly inside `SwipeItemView`,
or that built `FontImageSource` from `(int)icon` casts (Unicode codepoint), both
produced empty boxes on Android — the codepoint reaches the platform but the
font lookup chain inside the `SwipeItemView` renderer doesn't resolve the
`MaterialIcons` family correctly, so Roboto is substituted and the glyph shows
as tofu. Going through the package's own `[Description]` + type-name contract
matches what `<Image Source="{icons:Material ...}">` produces in working XAML
(e.g. `BarcodeScannerPage.xaml`), so the glyph rasterizes through the platform
image pipeline before the swipe-pane renderer paints it.

### Theme-resolved colors with destructive shortcut

Setting `IsDestructive = true` is the canonical way to opt-in to Error / OnError
tints — the user-visible mental model is "this action is destructive" rather than
"this action uses the error palette". Explicit `Background` / `Foreground` on the
action override the palette resolution.

### Culture-aware refresh

`G9SwipeView` subscribes to `G9Culture.CultureChanged` while attached to
a Handler. On a culture flip:

1. The action panes are rebuilt so a Persian label like "حذف" replaces an English
   "Delete" without consumers needing to touch the model collection.
2. The pane assignments are **swapped** when `G9Culture.IsRtl` is true:
   `LeftActions` is bound to the native `RightItems` slot (and vice-versa) so the
   leading-edge action stays on the user's leading edge across cultures. This is
   the fix for the visible bug where LTR and RTL looked identical — native
   SwipeView keys panes to physical screen edges and never participates in
   FlowDirection inversion.

### Live updates

`G9SwipeAction` implements `INotifyPropertyChanged`, so mutating
`Text` / `MaterialIcon` / `Background` / etc. updates the rendered pane in place
without rebuilding the whole row. Useful for "Mute → Unmute"-style toggles where
only the label changes between states.

## Usage

### One leading + one trailing action

```xml
<newControls:G9SwipeView CornerRadius="14" HeightRequest="68">

    <newControls:G9SwipeView.LeftActions>
        <!-- Edit on the leading edge -->
        <newControls:G9SwipeAction
            Text="Edit"
            MaterialIcon="Edit"
            Invoked="OnEditInvoked" />
    </newControls:G9SwipeView.LeftActions>

    <newControls:G9SwipeView.RightActions>
        <!-- Destructive Delete on the trailing edge -->
        <newControls:G9SwipeAction
            Text="Delete"
            MaterialIcon="Delete"
            IsDestructive="True"
            Invoked="OnDeleteInvoked" />
    </newControls:G9SwipeView.RightActions>

    <!-- The card body — corners are clipped to CornerRadius by the wrapper Border -->
    <Grid BackgroundColor="{themeManager:ThemeColor Surface}"
          Padding="14,12"
          ColumnDefinitions="Auto,*"
          ColumnSpacing="10">
        <Label Grid.Column="0" Text="📝" FontSize="22" VerticalOptions="Center" />
        <VerticalStackLayout Grid.Column="1" VerticalOptions="Center" Spacing="2">
            <Label Text="{Binding Title}" FontSize="14" FontAttributes="Bold" />
            <Label Text="{Binding Subtitle}" FontSize="12" Opacity="0.75" />
        </VerticalStackLayout>
    </Grid>

</newControls:G9SwipeView>
```

### Multi-action with custom colors

```xml
<newControls:G9SwipeView CornerRadius="14" HeightRequest="68">

    <newControls:G9SwipeView.RightActions>
        <newControls:G9SwipeAction
            Text="Archive" MaterialIcon="Archive"
            Background="#3B82F6" Foreground="White" />
        <newControls:G9SwipeAction
            Text="Star" MaterialIcon="Star"
            Background="#F59E0B" Foreground="White" />
        <newControls:G9SwipeAction
            Text="Mute" MaterialIcon="VolumeOff"
            Background="#64748B" Foreground="White" />
    </newControls:G9SwipeView.RightActions>

    <!-- card body... -->

</newControls:G9SwipeView>
```

### Live label update on a toggle

```csharp
_muteAction.Text = isMuted ? "Unmute" : "Mute";
_muteAction.MaterialIcon = isMuted ? MaterialIcons.VolumeUp : MaterialIcons.VolumeOff;
// No collection reset, no XAML rebuild — INotifyPropertyChanged drives the visual.
```

## Caveats

- Native `SwipeView` raises `Invoked` only when the user **taps** the action pane
  (not when the swipe gesture itself completes). If you want a swipe-only "open"
  gesture, set `Mode = SwipeMode.Execute` on the underlying `NativeSwipeItems` —
  not currently exposed as an `G9SwipeAction` property because every screen
  using `G9SwipeView` so far has wanted Reveal mode.
- Action panes don't support gesture-driven height — `HeightRequest` on the
  outer `G9SwipeView` is the row height. Set it explicitly; the default
  measure inside a virtualized list layout is unreliable.
- The pane background is painted by the action's `Background`, but a stray
  pixel column at the rounded corner can show through if you set `CornerRadius`
  larger than the row height divided by 2. Stick to the project default `14`.


## Platform behaviour

### Windows / WinUI 3 — custom drag-to-reveal swipe

On the Windows TFM the inner `Microsoft.Maui.Controls.SwipeView` is **not
instantiated**. Instead a lightweight custom drag-to-reveal is built: the two
action panes render in a `Grid` behind the card body (leading pane docked to the
physical-left edge, trailing pane to the physical-right edge), and a
`PanGestureRecognizer` on the body translates it horizontally with the mouse to
reveal a pane. Dragging past 50% of the pane width snaps open on release;
otherwise it snaps closed. A tap on a revealed action fires its `Command` /
`Invoked` and closes; a tap on the open body closes it.

Why not the native control: WinUI 3's `SwipeControl` (the platform peer that
MAUI's `SwipeView` wraps on Windows) reliably tears the process down with a
stowed exception from `Microsoft.UI.Xaml.dll` (status code `0xC000027B`,
`STATUS_STOWED_EXCEPTION`) within roughly 12-17 seconds of being instantiated,
even on an idle page with no user interaction (confirmed under procdump +
`dotnet-dump` + a first-chance exception logger — zero managed throws between
render and crash). It also "can only be swiped in a touch interface and will not
function with a pointer device such as a mouse" (Microsoft Learn), so it would be
useless on Windows desktops even if it didn't crash. The custom implementation
never instantiates `SwipeControl`, so the crash cannot occur, and it works with a
mouse drag.

The custom panes reuse the same `G9SwipeAction` model, theme-resolved colors,
`FontImageSource` Material glyphs, and RTL leading-edge swap as the mobile path —
`LeftActions` stays the leading edge (physical left in LTR, physical right in
RTL). Implementation (all `#if WINDOWS`): `RebuildWindowsPanes` /
`BuildWindowsActionButton` build the panes; `OnWinPan` drives the drag and the
open/close snap; `WinAnimateTo` / `WinResetClosed` animate the body translation.

See `G9Controls.md` §15 (Windows pitfall **W1**) for the consolidated platform
crash catalog and the debugging playbook used to pin down the `SwipeControl` crash.

### Mobile — full implementation

Android, iOS, and MacCatalyst use the full wrapper-around-`SwipeView`
implementation: the inner `Microsoft.Maui.Controls.SwipeView` is constructed,
the `LeftItems` / `RightItems` collections are populated from
`LeftActions` / `RightActions`, and a culture-change subscription rebuilds the
panes (with translated labels) and re-applies the leading-edge swap so a
Persian session and an English session look correct without consumers
touching the action collections.
