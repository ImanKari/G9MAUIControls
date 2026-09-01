# Bottom Sheet Guide

This app ships its own bottom-sheet implementation under `Common/Components/G9BottomSheet/G9SheetView/` and exposes it through `G9BottomSheetHelper`. Callers should not create `G9SheetView` directly and should not use `Plugin.Maui.G9BottomSheet`, the former third-party library (now removed)'s `SfG9BottomSheet`, or navigation-pushed modal pages for modal-style UI. The handler is registered by `builder.UseG9SheetView()` in `MauiProgram.cs`.

## Why We Own The G9BottomSheet Source

Earlier versions of the app vendored the former third-party library (now removed) Toolkit's `SfG9BottomSheet` and reapplied a long list of local edits on every upstream refresh. The control is now hand-rolled with public MAUI primitives only (`Grid`, `Border`, `RoundRectangle`, `Microsoft.Maui.Controls.Animation`) so:

- We control the `PositionChanged` hook needed for the modal-overlay alpha and the backdrop card recede without reflection — important under AOT.
- The `AnimationDurationProvider` (direction-aware + size-scaled durations) is a first-class field, not a patched-in extension.
- There is no the former third-party library (now removed) internal-namespace dependency anymore. The bottom-sheet code never reaches into `the former third-party library (now removed).Maui.Toolkit.Internals` or any other restricted namespace, and the the former third-party library (now removed) package is referenced only for unrelated controls (popups, charts, pickers).
- AOT-safe: no `Reflection.Emit`, no private-field reads, no unsafe access checks. All state machine work runs through `BindableProperty.PropertyChanged` callbacks.

## File Structure

```
Common/Components/G9BottomSheet/
├── G9BottomSheetBackAction.cs
├── G9BottomSheetBackRequestSource.cs
├── G9BottomSheetExtensions.cs
├── G9BottomSheetGuide.md
├── G9BottomSheetHelper.cs
├── G9BottomSheetListItem.cs
├── G9BottomSheetListPickerModal.xaml
├── G9BottomSheetListPickerModal.xaml.cs
├── G9BottomSheetOptions.cs
├── G9BottomSheetToolbarItem.cs
├── IG9BottomSheetAwareView.cs
├── IG9BottomSheetHandle.cs
├── IG9BottomSheetSizedView.cs
└── G9SheetView/
    ├── G9SheetView.cs                        // cross-platform control
    ├── G9SheetViewBorder.cs                  // shared border + ForwardTouch entry point
    ├── G9SheetViewBorder.Android.cs          // Android handler + custom ContentViewGroup
    ├── G9SheetViewBorder.iOS.cs              // iOS / Mac Catalyst handler + UIPanGestureRecognizer subclass
    ├── G9SheetViewBorder.Windows.cs          // Windows handler with PointerPressed/Moved/Released wiring
    ├── G9SheetViewEnums.cs                   // State / AllowedState / ContentWidthMode / TouchAction
    ├── G9SheetViewEventArgs.cs               // StateChanged + PositionChanged event args
    └── G9SheetViewMauiAppBuilderExtensions.cs // builder.UseG9SheetView()
```

The folder under `G9SheetView/` is the cross-platform sheet control plus its per-platform handlers. The remaining files in the parent folder are the app-level helper, options, handles, picker view, and extension methods built on top of it.

## Control Layout (Visual Tree)

```
G9SheetView : Grid  [InputTransparent=true, CascadeInputTransparent=false]
├── Content (the host page content placed behind the sheet by RebuildChildren)
├── overlayGrid (BoxView-style modal scrim, inserted just before the body when IsModal=true)
│   [InputTransparent=false — blocks taps when visible; flipped to true on close]
└── G9SheetViewBorder : Border  [InputTransparent=false — captures body taps/drags]
    └── _bottomSheetContent : Grid
        ├── _grabberGrid : Grid         // row 0 — drag handle
        │   └── _grabber : Border       // rounded pill
        └── _contentBorder : Border     // row 1 — caller's content
```

The host Grid is input-transparent so the empty area outside the body falls through to the page content underneath (critical for non-modal sheets and for the page to stay interactive while the sheet is closed). Children opt out of the cascade so the body and the internal overlay keep their own hit-testing. The body uses MAUI's public `Border` so we get a real `RoundRectangle`-clipped surface on every platform without any vendor-specific layout primitive.

## Z-Stack Position

`G9SheetView` is the static template instance held by `OverlayHost` in `G9PageTemplate.xaml`. `G9BottomSheetHelper` opens primary and stacked sheets by appending more children to that same `OverlayHost.Children` collection. Above `OverlayHost` are two dedicated sibling layers, ordered bottom-to-top:

```
RootHost
├── BackdropHost      (sheet recede color)
├── ContentHost       (page content + tab bar + InitializeOverlaySlot)
├── OverlayHost       (G9SheetView + dynamic sheets + modal scrims — this control's home)
├── G9PopupHost         (G9PopupView — paints above every sheet)
└── ToastHost         (every toast / loader / progress visual)
```

**Toolbar actions are guarded PER BUTTON, never by a shared key.** `CreateToolbarActionButton` runs each action through `G9SafeCommand` with a per-button `ThrottleKey`. Without one, the helper derives the key from caller file + member — the same key for every toolbar button in the app — and since `PreventConcurrentExecution` defaults to `true`, a sheet opened FROM a toolbar action (whose `AsyncAction` awaits until that sheet closes) held the guard and **silently disabled every toolbar button on the sheet stacked above it**. Symptom to recognise: a header action that works when its sheet is opened one way and does nothing when the same sheet is opened from another sheet's toolbar. A double-tap on the SAME button is still guarded.

A popup raised while a sheet is open paints above the sheet. A toast raised over a sheet (with or without an open popup) paints above everything. Closing the sheet does not affect the toast or popup layer — they outlive the sheet. `G9PopupHost` and `ToastHost` carry explicit `ZIndex` (2 and 3) as defense in depth so future template edits can't accidentally regress the contract. See `G9PageTemplate.xaml` for the canonical structure.

## Per-Platform Handlers

### Android — `G9SheetViewBorder.Android.cs`

`G9SheetViewBorderHandler` overrides `BorderHandler.CreatePlatformView()` and returns a custom `G9SheetViewBorderPlatformView` (a `ContentViewGroup` subclass). The platform group handles gestures through **two complementary paths**:

**Path 1 — `OnTouchEvent` (non-scrollable body):** When no child inside the body consumes `ACTION_DOWN` (the common case for sheets containing only labels, borders, and non-clickable views), Android delivers the entire gesture stream directly to the parent's `OnTouchEvent`. The handler synthesises a `Pressed` on Down, forwards per-frame `Moved`, and raises `Released` on Up — giving the sheet's state machine a clean gesture stream for drag-to-state and drag-to-close.

**Path 2 — `OnInterceptTouchEvent` (scrollable-edge handoff):** When a scrollable child (`RecyclerView`, `NestedScrollView`, `ScrollView`, `AbsListView`) is under the finger and consumes Down, Android keeps calling `OnInterceptTouchEvent` on subsequent Move events. The handler:

- Walks the visual tree under the touch to find the first scrollable (`FindScrollableUnder`).
- While that inner scrollable can still scroll in the drag direction (`CanChildScrollVertically`), returns `false` so the inner gesture wins.
- Once the inner scrollable hits its edge, calls `RequestDisallowInterceptTouchEvent(true)` on every ancestor, stops nested-scroll on the child (`ViewCompat.StopNestedScroll`, `RecyclerView.StopScroll`, `NestedScrollView.StopNestedScroll`), forwards a synthetic `Pressed` then `Moved` to the bottom sheet via `border.ForwardTouch`, and returns `true`. Android then delivers all subsequent events to `OnTouchEvent` (Path 1) which keeps forwarding them.

A `_gestureForwarded` flag prevents double-fire between the two paths and ensures a `Pressed` is always synthesised before the first `Moved` regardless of which path activates.

### iOS / Mac Catalyst — `G9SheetViewBorder.iOS.cs`

`G9SheetViewBorderHandler` overrides `BorderHandler.ConnectHandler(Microsoft.Maui.Platform.ContentView)` and adds `G9SheetViewPanGestureRecognizer` (a `UIPanGestureRecognizer` subclass) to the platform view. The recogniser:

- Sets `CancelsTouchesInView = false`, `DelaysTouchesBegan/Ended = false`, and `ShouldRecognizeSimultaneously = true` so it never blocks the inner `UIScrollView` / `UICollectionView`.
- On `TouchesBegan`, walks `Superview` chain (`ResolveScrollableAncestor`) to find the first `UIScrollView` — that includes plain `UIScrollView`, `UICollectionView`, and `UITableView`.
- During pan, computes `dy` against `_lastPoint`. While the inner scroller can still scroll (`CanInnerScroll` honors `AdjustedContentInset` for safe-area-aware lists), the recogniser stays passive.
- Once the inner reaches its edge, captures the pan and forwards `Pressed` then per-frame `Moved` to the bottom sheet. On `Ended` / `Cancelled` / `Failed` it forwards `Released` or `Cancelled` and resets state.

### Windows — `G9SheetViewBorder.Windows.cs`

`G9SheetViewBorderHandler` overrides `BorderHandler.ConnectHandler(ContentPanel)` and wires:

- `PointerPressed` — finds the nearest `ScrollViewer` ancestor of `e.OriginalSource` via `VisualTreeHelper.GetParent`. If none, immediately forwards `Pressed` to start the sheet gesture.
- `PointerMoved` — suppresses synthetic mouse moves on touch devices, then while the inner scroller can scroll (`CanInnerScroll` checks `VerticalOffset` vs `ScrollableHeight`) stays passive. At the edge it captures the pointer and forwards `Pressed` + `Moved`.
- `PointerReleased` / `PointerCanceled` / `PointerCaptureLost` — forward `Released` or `Cancelled` to the sheet, then `ReleasePointerCaptures()` and reset.

## Goals

- One shared bottom-sheet abstraction for Android, iOS, Mac Catalyst, and Windows.
- Same app-level behavior on desktop and mobile-sized windows.
- Support state sheets, fit-to-content sheets, fixed full-screen modal sheets, stacked sheets, lazy content loading, toolbar actions, and virtualized list pickers.
- Keep heavy sheet content deferred so the sheet opens first and shows a spinner.

## Main APIs

### Automatic Stacking (one entry point, no manual stacked API)

`ShowG9BottomSheet` is the single entry point for opening a sheet. **Stacking is automatic**: the helper checks whether a sheet is already open (`GetOpenSheetCount() > 0`) and:

- **No sheet open** → opens the new sheet as the **primary** sheet.
- **A sheet is already open** (primary or stacked) → opens the new sheet **stacked on top** of it.

```csharp
// First call → primary sheet. A later call while it is still open → stacked automatically.
G9BottomSheetHelper.ShowG9BottomSheet(
    content,
    G9BottomSheetOptions.FitToContentOptions());
```

There is no `ShowStackedG9BottomSheet` anymore — callers never choose primary-vs-stacked. `ShowFullScreenAsync` uses the same automatic detection. To **replace** the current sheet instead of stacking on it, use `ReplaceG9BottomSheet(content, options)` (see "Replace In Place (morph)" below) — or close it first (e.g. `CloseG9BottomSheet()` / a handle's `Close()`), then open the new one.

### Stacked parent recede (`RecedeParentOnStack`, default ON)

When a sheet opens STACKED, the parent the user was looking at **recedes**: it sinks by ~25% of
its visible height and fades to invisible (`StackParentRecedeHeightFraction` /
`StackParentRecedeDurationMs`), **in parallel** with the child's open animation — so a smaller
child never sits awkwardly on a taller parent. The parent stays **alive** (rendered tree, state,
scroll position all intact); the moment the child closes it slides back in
(`RestoreRecededParent`), which is what makes back-navigation in stacked flows instant — no
rebuild, no spinner, no glyph re-realization. **Full-screen parents never recede** — a full-screen
sheet is its flow's backdrop (e.g. the sampling task screen), and hiding it would expose the page
underneath around a smaller child; the child simply opens on top of it. Opt a child out with
`G9BottomSheetOptions.RecedeParentOnStack = false` when a PARTIAL-height parent should stay visible
behind it.

This is the mechanism behind **step-chained flows** (tree/pot operations, bulk selection, the
task-management → change-status pair): every deeper step opens stacked; Cancel/back closes only
the top step (the parent restores); a successful commit closes the WHOLE stack with
`CloseG9BottomSheet()`. The stale-sheet hazard that once pushed these flows to replace semantics
(only the top sheet closed on success) is addressed by that close-all rule — do not close just
the top sheet on a commit path.

### Replace In Place (morph) — `ReplaceG9BottomSheet`

`ReplaceG9BottomSheet(content, options)` is the step-transition primitive for chained sheet flows
(tree/pot operations, bulk selection). When the open sheet and the replacement are both **modal
fit-to-content** sheets and nothing is stacked, the body is swapped **in place**: the old body
fades out (~90 ms), the new body fades in (~140 ms), and the sheet height animates between the two
— no close/open cycle, no dead time, no spinner between steps. Otherwise it falls back to the
classic close-then-open path, marking the closing sheet for a **sped-up close**
(`QueuedReplaceCloseDurationScale = 0.5` on both the close motion and the cleanup wait) because a
replacement is already queued.

Semantics to know:

- On the in-place path the replaced step's `ClosingCommand`/`ClosedCommand` do **NOT** run — a
  replace is an ADVANCE, not a close (identical to the `advancing = true` convention the map
  flows use around the fallback path). The new options' commands take over for the eventual real
  close; hardware back / overlay taps route against the NEW options immediately (back handling
  reads the live behavior state).
- The new content should be **prebuilt** (construct it before calling). The morph forces
  `DeferContent = false` — there is no open animation to defer past, and a deferred wrapper would
  only insert a between-steps spinner.
- The new height resolves up-front when the persisted **height memo** knows this body — a learned
  entry, or its first-install seed for a never-shown one (height tween runs together with the
  crossfade); otherwise the sheet **holds its current height** and the settle passes animate to
  the real height as soon as the platform can measure the new body. It never dips to the loading
  floor between steps.
- Transitions involving a full-screen (`States`) step always use the fallback path — morphing
  across sizing models is deliberately out of scope.
- Same throttle window as `ShowG9BottomSheet`, so a rapid double-tap on a menu row produces one
  transition.

`MapViewModel.ReplaceG9BottomSheet` delegates to this API (see `AiGuides/12-Tree-Pot-Map-Operations.md`
"Sheet chaining").

> Why the split was removed: the old `ShowStackedG9BottomSheet` always created a stacked sheet even when nothing was open, producing an orphan stacked sheet with no primary/backdrop owner; and `ShowG9BottomSheet` always replaced the primary, so opening a child sheet from inside an open sheet required the caller to know which API to call. Detecting the open sheet in one place removes both foot-guns. In practice production primary sheets are modal — their overlay intercepts page taps — so a second sheet only ever opens programmatically (the intended stack case); replace semantics are reached by closing first.

Use `ShowFullScreenAsync` for modal-style screens. Prefer the factory overload for heavy XAML/content.

```csharp
await G9BottomSheetHelper.ShowFullScreenAsync(
    this,
    () => new MyModalContentView(),
    G9BottomSheetOptions.FullScreenModalOptions("Title"));
```

Use `ShowListG9BottomSheetAsync` for shared selectable lists. It returns selected `G9BottomSheetListItem` values after the sheet closes. Its rows are **flat and borderless** (52dp, 16dp horizontal inset, tint-only selection, no divider) — the same standard as `G9SelectionSheet`; see `AiGuides/08-UI-UX-Design-System.md` §5.

```csharp
var selectedItems = await G9BottomSheetHelper.ShowListG9BottomSheetAsync(
    title,
    items,
    selectedItems,
    allowMultipleSelection: true,
    closeOnSingleSelection: false);
```

### Processing Sheet (instant spinner → async build → error popup/close)

`ShowProcessingG9BottomSheet` is the entry point for **heavy** sheet bodies whose synchronous
construction (XAML inflation + first layout) would otherwise delay the open by hundreds of
milliseconds. It opens the sheet **instantly** showing only a spinner (zero work on the tap),
then — after the open animation — runs an async `buildAsync` callback that loads data and
constructs the real content **off the open frame**, and swaps it in. If `buildAsync` throws, an
`onError` callback runs; when `onError` is `null` the default behavior shows an error popup and
closes the sheet.

```csharp
G9BottomSheetHelper.ShowProcessingG9BottomSheet(
    buildAsync: async (handle, ct) =>
    {
        // Runs AFTER the spinner sheet is visible. Do data work with ConfigureAwait(false),
        // then construct the View on the UI thread (MAUI view construction is not thread-safe).
        var data = await dataService.LoadAsync(ct).ConfigureAwait(false);
        if (data is null)
        {
            return null; // returning null closes the spinner sheet quietly (target gone)
        }

        return await MainThread.InvokeOnMainThreadAsync<View?>(() => new HeavyContentView(data));
    },
    options: G9BottomSheetOptions.FitToContentOptions() with
    {
        ShowToolbar = true,
        ShowCloseButton = true,
        Title = titleKnownSynchronously,     // toolbar title is available at open time
        ClosedCommand = new RelayCommand(OnClosed)
    },
    onError: async (ex, handle) =>            // optional; omit for the default popup + close
    {
        await CleanupAsync();
        await handle.CloseAsync();
    });
```

Why this exists and when to use it:

- **The two callbacks you get:** `buildAsync` (the "process on opening" callback, run once after the
  sheet is visible) and `onError` (the "on exception" callback — default: error popup + close).
- **Fixes the tap → open delay AND the double-open bug.** Because the sheet opens immediately, the
  single-key open throttle collapses a rapid double-tap into one sheet. A heavy body built
  synchronously before `ShowG9BottomSheet` (the old pattern) lands outside that window and a second
  tap opens a duplicate / closes the first. Measured on the sampling target sheet: tap→sheet went
  from ~525 ms (≈450 ms of it synchronous `SamplingTargetContentView` construction before the open)
  to an instant spinner with the build happening behind it.
- **Threading contract (important):** `buildAsync` may `await` data with `ConfigureAwait(false)`,
  but the returned `View` MUST be constructed on the UI thread — wrap the `new XView(...)` in
  `MainThread.InvokeOnMainThreadAsync<View?>(...)`. Return `null` to close the sheet quietly.
- **Fit-to-content sizing:** the sheet opens at `loadingHeight` (the spinner, default 160) and grows
  to the built content's height once swapped in. If the built view implements
  `IG9BottomSheetContentHeightProvider` (tabbed / list bodies like `SamplingTargetContentView`), its
  height changes are forwarded so the sheet keeps resizing as the user interacts.
- **The built body gets THIS sheet's scoped handle (`IG9BottomSheetAwareView`).** The real content is
  a CHILD of the `ProcessingSheetContentView`, so the helper's `IG9BottomSheetAwareView` injection only
  reached the wrapper, never the built body — the body kept its default `InitG9BottomSheet()` handle
  whose owner is null. A self-close from inside the body (e.g. a header **X** calling
  `G9BottomSheetHandle.Close()`, as `SamplingTargetContentView` does) then fell through to
  `CloseG9BottomSheet()` and tore down the **primary** sheet (the whole flow — for the stacked sampling
  target sheet that meant the map closed and the user was dumped back on the Tasks list). `AttachBuilt`
  now forwards its sheet-scoped handle to the built body when it is `IG9BottomSheetAwareView`, so the
  body's `Close()` closes exactly THIS (stacked) sheet. Do NOT regress this — a built body must never
  rely on the null-owner `Close()` fallback.
- **Backed by the existing "open then fill" pipeline:** the helper wraps `buildAsync` in a
  `ProcessingSheetContentView` (a `LoadableSheetContentView` / `IDeferredSheetLoad`), so it reuses
  the after-open load trigger, the close/detach cancellation token, and the fit-to-content resize
  engine. It forces `DeferContent = false` so the processing view's own spinner is the only one
  (no double-spinner).

Current consumers: the sampling **pot/target** sheet (`SamplingHandler.Picking.ShowSamplingPickSheetAsync`)
and the main-map **block-info** sheet (`MapViewModel.ShowBlockInfoG9BottomSheetAsync`). Prefer
`ShowProcessingG9BottomSheet` over a synchronous `new HeavyView(...)` + `ShowG9BottomSheet(view)` for any
new heavy map/selection sheet.

## App Startup Defaults

Configure app-wide bottom-sheet behavior once in `MauiProgram.cs`:

```csharp
G9BottomSheetHelper.Configure(new G9BottomSheetSettings
{
    ModalOverlayMinimumOpacity = 0.3,
    ModalOverlayMaximumOpacity = 0.9,
    OpenAnimationDurationMs = 300,
    CloseAnimationDurationMs = 300,
    SizeScaledAnimationDuration = true,
    EnableBackdropCardEffect = true,
    BackdropCardColor = Colors.Black
});
```

Use this for shared helper behavior — modal overlay opacity, the open and close animation durations (tuned independently), the size-scaling toggle, the backdrop card recede master switch, and the dark color painted behind the receded page. Do not hardcode per-sheet overlay opacity unless a specific sheet uses `WindowBackgroundColor`.

The five "global default" fields specifically:

- `OpenAnimationDurationMs` — app-wide open duration (sheet rising / expanding toward a larger state). Drives the modal overlay fade-in, the opened-command delay, the fit-to-content grow animation, and — together with size-scaling — the sheet's `AnimateG9BottomSheet` for any motion that travels upward. Per-sheet `G9BottomSheetOptions.OpenAnimationDurationMs` is `double?` and overrides this when set; leave it `null` (the default) so every sheet shares the helper-wide value.
- `CloseAnimationDurationMs` — app-wide close duration (sheet receding / collapsing toward a smaller state). Drives the modal overlay fade-out, the close-completion wait, the fit-to-content shrink animation, and — together with size-scaling — the sheet for any downward motion. Per-sheet `G9BottomSheetOptions.CloseAnimationDurationMs` is `double?` and overrides this when set. Tune Close shorter than Open if you want close animations to feel snappier than opens (a common iOS pattern).
- `SizeScaledAnimationDuration` — when `true` (default), the resolved Open/Close duration is treated as the time for a full-height traversal (`Hidden ↔ FullExpanded`) and partial traversals consume a proportional slice. A `Hidden → HalfExpanded` open takes ~half the configured Open duration; a `HalfExpanded → Hidden` close takes ~half the configured Close duration. Only the sheet motion is size-scaled — modal overlay fades, state-aware top padding, fit-to-content, and backdrop card recede always use the full configured duration so they read cleanly even for short snaps. Set to `false` for a flat "same duration regardless of distance" feel.
- `EnableBackdropCardEffect` — app-wide kill switch for the page recede effect. When `false`, every sheet skips the effect even if its own `G9BottomSheetOptions.EnableBackdropCardEffect` is `true`. When `true`, the per-sheet option still acts as an extra opt-out.
- `BackdropCardColor` — color applied to the always-present `BackdropHost` `BoxView` in `G9PageTemplate.xaml`. Read once per page when its control template is applied, so this must be configured before any page in the app initialises. Defaults to `Colors.Black` to match the iOS native bottom-sheet look and keep the receded area dark in both Light and Dark themes.

## Animation Duration And Easing

The app-wide open and close durations are configured independently:

- `G9BottomSheetSettings.OpenAnimationDurationMs` (default **300 ms**) — sheet rising / expanding.
- `G9BottomSheetSettings.CloseAnimationDurationMs` (default **300 ms**) — sheet receding / collapsing.
- `G9BottomSheetSettings.SizeScaledAnimationDuration` (default **true**) — when on, those durations represent a *full-height* traversal and partial state changes scale proportionally.

`G9SheetView.AnimateG9BottomSheet` consults a helper-installed `AnimationDurationProvider` on every motion (open, close, drag-release snap, programmatic `State = …`). The provider:

1. **Picks direction** — if the target `TranslationY` is above the current position (sheet rising), the Open duration is used; otherwise the Close duration. Drag-release snaps land on the correct side automatically because the provider sees the live `TranslationY` for that motion.
2. **Optionally size-scales** — when `SizeScaledAnimationDuration` is `true`, the picked value is multiplied by `|target − current| / Height`, clamped to `[0, 1]`. So:
   - `Hidden → HalfExpanded` takes ~`OpenDuration × 0.5`.
   - `Hidden → FullExpanded` takes the full `OpenDuration`.
   - `HalfExpanded → FullExpanded` takes ~`OpenDuration × 0.5`.
   - `FullExpanded → Hidden` takes the full `CloseDuration`.
   - `HalfExpanded → Hidden` takes ~`CloseDuration × 0.5`.
   The visual velocity stays constant across every state transition, matching the iOS native sheet feel.

Helper-side animations that *don't* size-scale (per design — keeps them readable on short snaps) still pick the right open/close direction via `ResolveOpenAnimationDurationMs` / `ResolveCloseAnimationDurationMs`:

- **Modal overlay fade** — fades to 0 alpha using Close duration when `sheet.State == Hidden`; otherwise uses Open duration.
- **Fit-to-content resize** — uses Open duration when content is growing (`targetHeight ≥ previousHeight`), Close duration when shrinking.
- **Opened work (OpenedCommand + deferred "open then fill" load)** — fires from the sheet's `OpenMotionCompleted` event, i.e. at the ACTUAL end of the (size-scaled) open motion; a fit-to-content open completes its opened work in ~80 ms instead of waiting the full configured duration. The full (non-scaled) Open-duration timer remains only as a fallback for opens that never animate.
- **Close cleanup wait** — uses Close duration (we must wait for the worst-case full-height close before tearing down visuals).

`G9SheetView.AnimateG9BottomSheet` uses `Easing.CubicOut` for both translation and overlay alpha, so motion decelerates into the snap target instead of stopping abruptly. The helper's modal overlay (separate from the sheet's own internal overlay, which stays disabled because `IsModal = false`) uses the same easing for visual consistency. `G9SheetViewStateChangedEventArgs.AnimationDurationMs` exposes the static `AnimationDuration` value as a coarse hint for external subscribers; helper consumers don't depend on it.

```csharp
// App-wide retune in MauiProgram.cs — drives every sheet that leaves Open/Close at null:
G9BottomSheetHelper.Configure(new G9BottomSheetSettings
{
    OpenAnimationDurationMs  = 320, // a slightly longer, more deliberate open
    CloseAnimationDurationMs = 220, // snappier close, iOS-like
    SizeScaledAnimationDuration = true
});

// Disable size-scaling globally if a flat duration is preferred:
G9BottomSheetHelper.Configure(new G9BottomSheetSettings
{
    SizeScaledAnimationDuration = false
});

// One-off per-sheet override (rare):
var options = G9BottomSheetOptions.FullScreenModalOptions("Edit") with
{
    OpenAnimationDurationMs = 180,
    CloseAnimationDurationMs = 180
};
```

## Presets

`DefaultOptions()`:
Medium initial state, large allowed state, modal, draggable, visible grabber. Use for normal bottom sheets that can resize.

`FitToContentOptions()`:
Measures content and sets sheet ratios so the sheet fits its content. Use for small temporary panels. Do not combine with explicit states. Native state swiping is disabled for this fixed sizing mode; close it through helper close APIs, hardware back, or the modal overlay. The sheet keeps tracking its content size as it settles — heavy or asynchronously-loaded bodies that report a too-small natural height on the first frame are remeasured automatically (see "Fit-To-Content Live Resize").

**Authoring rule — fit-to-content bodies must be NON-scrolling.** A fit-to-content view's body root must use intrinsic-height rows (e.g. `Grid RowDefinitions="Auto,Auto"` with body in row 0 and a pinned footer in row 1) and must NOT wrap the body in a `ScrollView` and must NOT use a greedy `*` row. A `*`-row or a `ScrollView`-as-body is "greedy content" that reports its viewport (or 0 on a cold Android open), so the sheet opens too small and shows an internal scroll instead of fitting (tiers 2/3 in "Fit-To-Content Sizing Engine"). Full-screen sheets (`FullScreenWithoutHandleOptions` / `FullScreenModalOptions`) are the opposite — they have a fixed height, so they SHOULD use `RowDefinitions="*,Auto"` + an inner `ScrollView` so tall bodies scroll. Reference fit-to-content views: `TreeStatusContentView`, `TreeDeleteContentView`, `BlockInfoContentView`.

`FullScreenWithoutHandleOptions()`:
Large-only fixed full-screen body sheet without toolbar. Used by picker-style flows that own their own header/body. **`IsDraggable = false` by default** — full-screen sheets can't be drag-resized (only one allowed state) and the convention is to close through caller-owned buttons / hardware back. Set `IsDraggable = true` per call site if a flow needs the drag-down close gesture.

`FullScreenModalOptions(...)`:
Large-only fixed full-screen modal sheet with the shared 3-slot header (RTL-aware back button, centered title by default, optional `ToolbarItems`). Override per-slot views (`HeaderLeadingView` / `HeaderTitleView` / `HeaderTrailingView`), spanned slots (`HeaderLeadingAndTitleView` / `HeaderTitleAndTrailingView`), or the entire header (`HeaderView`) when the shared template's defaults don't fit. Pair with `FooterButtons` or `FooterView` for the shared footer area. **`IsDraggable = false` by default** — same reasoning as `FullScreenWithoutHandleOptions`. Set `IsDraggable = true` per call site if a flow needs the drag-down close gesture.

`FullScreenEdgeToEdgeModalOptions(hardwareBackCloses = true)`:
The shared **diagnostics-chrome** preset: a full-screen sheet for a view that paints its OWN full-bleed header / coloured background. Derives from `FullScreenModalOptions(showCloseButton: false)` but flips `ShowToolbar = false` and zeroes the whole top safe-area band (`UseTopSafeAreaPadding = false` + `TopSafeAreaPaddingOverride = 0` + `AdditionalTopSafeAreaPadding = 0`) so the view's own header reaches the very top edge (the camera/cutout overlays it, no helper padding band above it). The hosted view MUST set `SafeAreaEdges="None"` on its root. This is ONE recipe shared by all diagnostics surfaces that draw their own header: `AdminDiagnosticsG9BottomSheetOptions.FullScreen()` (delegates to it), the Live Diagnostic overlay (`LiveDiagnosticModalService.OpenAsync`), and the Send / Save full-diagnostic-report sheet (`DiagnosticReportSheetView`). Do NOT hand-assemble the `with { ShowToolbar = false, UseTopSafeAreaPadding = false, … }` block at a new diagnostics call site — call this preset. See "Full-Screen Safe Area" below and `Views/Pages/AdminDiagnostics/AdminDiagnostics.md`.

## Sizing Rules

App states map to `G9SheetViewState` values:

- `G9BottomSheetState.Peek` -> `Collapsed`
- `G9BottomSheetState.Medium` -> `HalfExpanded`
- `G9BottomSheetState.Large` -> `FullExpanded`

For multi-state sheets, configure:

```csharp
G9BottomSheetOptions.DefaultOptions() with
{
    CurrentState = G9BottomSheetState.Peek,
    States = [G9BottomSheetState.Peek, G9BottomSheetState.Medium, G9BottomSheetState.Large],
    PeekHeight = 128,
    CollapsedHeight = 128
};
```

Fixed-state sheets are sheets with `FitToContent` or only one allowed state. Fixed-state sheets do not show a grabber, even if `HasHandle = true`.

### Detents, and what `AllowedState` does NOT say

The control thinks in **detents** — the heights it may come to rest at. `AllowedState` only says
which of the two LARGE detents exists (`HalfExpanded` / `FullExpanded` / both); it cannot say
whether a peek step sits under them. `G9SheetView.AllowCollapsedState` is that missing bit, and the
helper sets it (`HasPeekDetent`) exactly when the caller declared `Peek` **alongside** another
state. It is what separates a two-detent peek→medium sheet from a single-state sheet that happens
to carry the same `AllowedState`, and three behaviours read it:

| | Single detent (full-screen, single-state, fit) | Two+ detents (`[Peek, Medium]`, `[Peek, Medium, Large]`) |
|---|---|---|
| Drag DOWN past `DragCloseThreshold` | close request | step DOWN a detent; only from the SMALLEST detent is it a close request |
| Drag UP | clamped at the one detent | clamped at the largest ALLOWED detent |
| Release between detents | snaps back | snaps to the nearest ALLOWED detent, ties going to the current one |

⛔ **The drag is clamped to the largest ALLOWED detent, not to the window.** Before 2026-09 the
limit was derived from the state the sheet was IN, so a sheet resting at `Peek` had no upper limit
short of the screen: it could be dragged to the status bar and was then snapped back down, which is
what reads to a user as "it over-drags and then leaves a gap". Below the smallest detent a
cancelable sheet keeps its height and SLIDES OFF instead of shrinking — a dismiss is not a resize,
and shrinking re-lays the body out on every frame.

### Content-sized top detent — `ExpandedFitsContent`

For a multi-detent `States` sheet, `G9BottomSheetOptions.ExpandedFitsContent = true` sizes the
LARGEST detent to the MEASURED content instead of to a ratio. This is Material's
`BottomSheetBehavior.fitToContents` applied to the top detent, and it is the answer whenever the
body's height varies with data, permissions or locale — a tuned ratio cannot be right twice.

```csharp
G9BottomSheetHelper.ShowG9BottomSheet(body, G9BottomSheetOptions.DefaultOptions() with
{
    SizeMode      = G9BottomSheetSizeMode.States,
    CurrentState  = G9BottomSheetState.Peek,
    States        = [G9BottomSheetState.Peek, G9BottomSheetState.Medium],
    PeekHeight    = 470,           // what the sheet OPENS at
    CollapsedHeight = 470,
    ExpandedFitsContent = true,    // what it settles at when dragged open
    MaxFitToContentHeightRatio = 0.9
});
```

- The measured height is written to whichever detent is largest — `HalfExpandedRatio` when the
  states stop at `Medium`, `FullExpandedRatio` when they include `Large`. The control clamps the
  half ratio to **0.9**, so a sheet whose top detent must go higher has to declare `Large`.
- **Content taller than the cap** (`MaxFitToContentHeightRatio`, default 0.75) stops at the cap and
  SCROLLS. To make that possible the helper hosts the body in its own vertical scroll viewport —
  skipped when the body already is a scroller/list, which would nest two scrollers.
- **Content shorter than `PeekHeight`** lowers the peek to the content too. Only ever lowered: the
  peek stays the caller's "open short" answer for a tall body.
- **Authoring rule, inverted from fit-to-content:** the body must still be INTRINSIC-height (no
  `ScrollView`-as-body, no greedy `*` row) — a scroller reports its viewport, so the top detent
  would silently fall back to the cap. The helper adds the scrolling, the body does not.
- A cold measure (Android measures a not-yet-attached tree as 0) is DISCARDED rather than applied:
  the peek is a fixed height and already correct, so there is nothing to hold. The same settle
  passes (0 / 160 / 380 ms), `MeasureInvalidated` tracker and
  `IG9BottomSheetContentHeightProvider` events that drive fit-to-content re-run this.
- Ignored for `SizeMode.FitToContent` (already content-sized) and for single-state sheets.

### Scroll belongs to the sheet until it is fully open — `ScrollingExpandsSheet`

`G9BottomSheetOptions.ScrollingExpandsSheet` (**default `true`**) decides who owns a drag that
starts on a scrollable part of the body: while the sheet is below its largest detent the SHEET does
— the drag expands it — and the inner scroller takes over only once there is nowhere further to go.
That is UIKit's `prefersScrollingExpandsWhenScrolledToEdge` (whose default is likewise on) and
Material's nested-scroll contract, and it is why a peek-then-expand sheet feels right: at the peek,
dragging up opens the sheet; at the top, the same drag scrolls; at the scroller's top edge, dragging
down collapses it again.

**It is a no-op for a single-detent sheet by construction** — full-screen, single-state and
fit-to-content sheets are always AT their maximum detent, so `IsAtMaximumDetent` is true and their
content scrolls exactly as it always did. Set it `false` only for a multi-detent sheet whose body
must scroll at every step (it then resizes from the grabber alone).

The gate lives in the control (`G9SheetView.ShouldInnerScrollerConsumeDrag`) and is asked by each
per-platform handler BEFORE its edge test, so all four targets share one rule. The helper also
rewinds its own viewport to the top when the sheet leaves the top detent — the grabber sits outside
the scroller and can collapse the sheet from any scroll offset, which would otherwise strand the
peek on the middle of the content with scrolling switched off.

## Full-Screen Safe Area

Full-screen top safe-area padding is state-aware:

- It is not applied while a multi-state sheet is in medium/peek state.
- It is applied when the sheet reaches `Large` / `FullExpanded`.
- It animates during state changes.
- It updates during interactive content drags where the platform reports drag movement.
- Fixed full-screen sheets apply it immediately.

The padding is based on `G9PageBase.TopSafeAreaInset` and these options:

```csharp
UseTopSafeAreaPadding = true,      // per-sheet ENABLE/DISABLE of the top safe-area inset
AdditionalTopSafeAreaPadding = 0,  // small additive per-screen tweak (after the inset + base gap)
TopSafeAreaPaddingOverride = null  // exact, absolute top padding (replaces the whole calc)
```

Use `TopSafeAreaPaddingOverride` only for exceptional screens. Use `AdditionalTopSafeAreaPadding` for small per-screen adjustments.

### What `TopSafeAreaInset` means (camera cutout, not the status bar)

The app is permanently full-screen / immersive — `MainActivity` hides the status and navigation
bars — so `G9PageBase.TopSafeAreaInset` is resolved from the **display cutout only** (the camera
notch / punch-hole), never the hidden system bars (see `G9PageBase.ApplyAndroidSafeAreaInsets`).
On a device with no top camera it is `0`. That is why a full-screen sheet is edge-to-edge by
default and only reserves room for the physical camera when it opts in.

### Enable / disable the top inset per sheet

`UseTopSafeAreaPadding` is the per-sheet switch:

- `true` (default) — the sheet reserves `TopSafeAreaInset` (the camera height) + an 8dp base gap
  above its header/body, so its first content clears the camera.
- `false` — the sheet does **not** reserve the camera inset; only the 8dp base gap remains, so the
  sheet's own top (e.g. a full-bleed colored header) sits at the very top and the camera overlays
  it. Use this for a sheet that owns a header where drawing under the camera is acceptable.
- For a truly flush, zero-gap top (not even the 8dp base), set `TopSafeAreaPaddingOverride = 0`.

**Example — Admin Diagnostics** (`AdminDiagnosticsModalService`): a body-only full-screen sheet
whose view paints its own colored header bar, so it opts out of the camera inset:

```csharp
G9BottomSheetOptions.FullScreenModalOptions(showCloseButton: false)
    with { ShowToolbar = false, UseTopSafeAreaPadding = false }
```

The colored header then reaches the top edge (the camera sits on the header) instead of leaving a
camera-height band of sheet background above it.

For body-only full-screen sheets without the helper toolbar, the helper accounts for the native grabber area so the first body content does not get an oversized top gap under the camera cutout.

## Modal Overlay Opacity

The helper renders its own modal overlay behind every sheet (a `BoxView` inserted into `OverlayHost` ahead of the sheet). The `G9SheetView`'s internal overlay is left disabled (`G9SheetView.IsModal = false`) because the helper-owned overlay covers the host even at the `Collapsed` state and animates in lockstep with state changes plus interactive drags.

Modal overlay opacity is state-aware. For multi-state sheets, the smallest allowed state uses `G9BottomSheetSettings.ModalOverlayMinimumOpacity`, and `Large` / full-screen uses `G9BottomSheetSettings.ModalOverlayMaximumOpacity`. Intermediate states interpolate between those values. The overlay is animated alongside the sheet's state-change animation (same `AnimationDurationMs`) and updated continuously during interactive content drags so dragging Medium → Large fades smoothly to the maximum value.

`WindowBackgroundColor` overrides this automatic opacity behavior for sheets that need a specific overlay color.

### Non-Modal Sheets

When `G9BottomSheetOptions.IsModal = false`, the helper skips the page-level overlay `BoxView` entirely. The `G9SheetView` host itself is `InputTransparent = true` with `CascadeInputTransparent = false`, so taps anywhere outside the sheet body fall through to the page content underneath. Use this for persistent panels (e.g. a map toolbar, a media player mini-bar) where the user must interact with both the sheet and the page simultaneously.

```csharp
G9BottomSheetHelper.ShowG9BottomSheet(
    content,
    new G9BottomSheetOptions
    {
        CurrentState = G9BottomSheetState.Medium,
        States = [G9BottomSheetState.Medium],
        IsModal = false,
        IsCancelable = false,
        IsDraggable = false,
        HasHandle = false,
        EnableBackdropCardEffect = false
    });
```

### Modal Overlay Instant-Unblock On Close

When a modal sheet closes, the helper flips `overlay.InputTransparent = true` on the page-level `BoxView` **immediately** — before the alpha-fade animation starts. The visual fade still plays out for the full close duration (300 ms by default), but hit-testing on the overlay is disabled from frame one. This eliminates the ~700 ms dead zone that previously blocked page taps after the sheet visually disappeared (caused by the overlay BoxView staying `InputTransparent = false` until `DetachModalOverlay` ran at the end of the cleanup delay).

When the sheet re-opens, `AttachModalOverlay` creates a fresh `BoxView` with `InputTransparent = false` and the next state-change animation fades alpha back in normally — no leaked state.

## Backdrop "Card Recede" Effect

The helper mimics the iOS native bottom-sheet behavior where the page behind the sheet appears to slide back into the screen as the sheet expands. Once the sheet visible-height ratio crosses `BackdropCardEffectThreshold` (default `0.75` from `G9LayoutMetrics.G9BottomSheetBackdropCardThreshold`), the helper interpolates two values on `G9PageBase.ContentHost`:

- `Scale` from `1.0` down to `G9LayoutMetrics.G9BottomSheetBackdropCardMinScale` (`0.93`).
- `TranslationY` from `0` up to `G9LayoutMetrics.G9BottomSheetBackdropCardTranslationY` (`12`).

Both values are written directly on the `Grid` via property setters — no animator, no measure pass, no extra `Layout` cycle. On every supported MAUI target those properties map to native compositor transforms (Android `RenderNode`, iOS `CALayer`, WinUI `CompositionTransform`), so the recede tracks the drag at compositor framerate without burning layout time. The effect plays automatically through the sheet's `PositionChanged` stream (drag tick + open/close/snap animation tick + helper-driven fit-to-content resize), so the page recedes on open, follows interactive drags 1:1, **and animates back to identity during programmatic close** (overlay tap, hardware back, helper `CloseG9BottomSheet()`, etc.) in lockstep with the close tween. The position-event handler keeps applying the transform even after `State` becomes `Hidden`, otherwise the recede would freeze at the last drag value and only snap back when `CleanupSheetVisualsNow` runs at the end of the close-cleanup delay (~`AnimationDurationMs`).

### Why the progress is inverse-cubic, not linear

`AnimateG9BottomSheet` uses `Easing.CubicOut` for both open and close motions, which means `visibleRatio(t) = 1 − (1 − t)³` during programmatic state-change animations (Hidden → FullExpanded and back). If the backdrop progress were mapped linearly from `(visibleRatio − threshold) / (1 − threshold)`, the recede would visibly "rush then stall" during a full-screen open — roughly **50 %** of the recede would land in the first **~13 %** of post-threshold animation time and the remaining 50 % would crawl across the remaining ~50 % of time. Users perceive that as "a small stop near the end of the open animation."

`BackdropCardBinding.ApplyForRatio` algebraically inverts the sheet's `CubicOut` curve:

```
visibleRatio(t) = 1 − (1 − t)³
rawProgress    = (visibleRatio − threshold) / (1 − threshold)
progress(t)    = 1 − (1 − rawProgress)^(1/3)
```

…which makes `progress` exactly linear in animation time. During interactive drags `visibleRatio` is linear in finger position (the sheet ticks visibleHeight per frame with no easing), so the same formula reads as a gentle InCubic feel — the backdrop "commits" as the user approaches full open, mirroring the native iOS modal behavior. `Math.Pow` is hardware-accelerated and is called at most once per frame, so this is purely a perceptual reshape, never a perf cost.

### Backdrop color (light vs dark theme)

Because the page `ContentHost` scales and translates, the area at the screen edges that the receded card no longer covers needs an explicit color — otherwise it would reveal the underlying page background, which is light in Light theme. The template handles this with an always-present `BoxView` named `BackdropHost` painted directly under `ContentHost` in `G9PageTemplate.xaml`. Its color comes from `G9BottomSheetSettings.BackdropCardColor` (default `Colors.Black`) and is applied once per page in `G9PageBase.OnApplyTemplate`. The `BoxView` is opaque, has `InputTransparent = true`, costs essentially nothing when fully covered by `ContentHost`, and only becomes visible at the edges while the recede effect is active.

### Defaults & overrides

- Enabled for every primary sheet via `G9BottomSheetOptions.EnableBackdropCardEffect = true` and `G9BottomSheetSettings.EnableBackdropCardEffect = true`.
- `G9BottomSheetSettings.EnableBackdropCardEffect` is the app-wide kill switch: when `false`, every sheet skips the effect regardless of its own option. Per-sheet `G9BottomSheetOptions.EnableBackdropCardEffect = false` opts a single sheet out while leaving the global default alone.
- Disabled automatically for fit-to-content sheets (their content height rarely reaches the threshold) and for stacked sheets (only the primary sheet owns the backdrop so two sheets never fight over the same transform).
- Reset to identity (`Scale = 1`, `TranslationY = 0`) by `CleanupSheetVisualsNow` so navigation never leaves a transformed page behind.

Disable per sheet only when a flow explicitly needs the page behind the sheet to stay perfectly still (e.g. screen-recording or media-capture screens). Adjust the threshold (`BackdropCardEffectThreshold`) per sheet when a flow needs the recede to engage earlier or later than 75 % — for app-wide tuning, change the constants in `G9LayoutMetrics` so every platform stays aligned.

## Drag, Swipe, And Closing

### Gesture Ownership

The `G9SheetView` control owns **all** drag gestures end-to-end on every platform. The helper does NOT attach any MAUI `PanGestureRecognizer` or `SwipeGestureRecognizer` to the sheet body — those are intentional no-ops in the helper (`AttachSheetContentDragGesture`, `AttachDragToCloseGesture`, `AttachDragToStateGesture` are all stubs). The per-platform handlers (`G9SheetViewBorder.{Android,iOS,Windows}.cs`) intercept vertical drags at the native level, forward them to `G9SheetView.OnHandleTouch`, and the control's state machine decides whether to snap to a new state or raise `BackRequested` for a close.

### `BackRequested` Event

When the user drags the body past `DragCloseThreshold` (default 72 dp) downward on a fixed-state sheet and releases, the control raises `G9SheetView.BackRequested` with reason `DragToClose`. The helper subscribes to this event in `AttachBackRequestedRouting` and routes it through `HandleBackRequest(sheet, G9BottomSheetBackRequestSource.ToolbarButton)` — which honors the caller's `OnBackRequested` callback and `IsCancelable` rules. The control does NOT close itself; it only raises the event and snaps back to its resting position.

### `IsCancelable` and `DragCloseThreshold`

- `G9SheetView.IsCancelable` (default `true`) — when `false`, the control suppresses `BackRequested` on drag-to-close AND refuses to be dragged below its smallest detent. The sheet still animates back to its resting position after a drag; only the close request is suppressed.
- `G9SheetView.DragCloseThreshold` (default `72`) — dp of downward FINGER travel on release that triggers the close request. Finger travel, not sheet movement: a sheet clamped at its detent may not have moved at all, and that is precisely the case the gesture exists for.

Both are bindable properties set by the helper in `ApplyOptions` from `G9BottomSheetOptions.IsCancelable` (they were documented as such long before the helper actually wrote them — fixed 2026-09).

`IsDraggable` controls state dragging and drag-to-close support.

Visible grabber rules:

- Shown only when `HasHandle = true`.
- Hidden when `IsDraggable = false`.
- Hidden for fixed-state sheets.
- Hidden for full-screen modal sheets by default.

Close behavior:

- `CloseG9BottomSheet()` closes the primary sheet.
- `CloseTopG9BottomSheet()` closes the top stacked sheet first, otherwise the primary sheet, and routes through `OnBackRequested` like toolbar close.
- Modal overlay taps close cancelable sheets through `OnBackRequested` (source `OverlayTap`). The helper-owned overlay always covers the host while the sheet is open, including at Peek state. The sheet's internal `IsModal` and `CollapseOnOverlayTap` are intentionally left disabled.
- Toolbar back and helper drag-to-close route through `OnBackRequested` when provided.
- Hardware/system back routes through `HandleHardwareBackPressed()`.
- If `OnBackRequested` is set, the callback decides whether the sheet closes.
- Fixed full-screen sheets treat a native sheet drag from `FullExpanded` into `Collapsed` or `HalfExpanded` as a close request. This prevents a modal-style sheet from parking at the bottom of the screen after a drag-down gesture.

Content drag behavior:

- Multi-state sheets can be dragged from the content body, not only from the grabber.
- Scrollable content has priority **once the sheet is at its largest detent**. When a `ScrollView`, `CollectionView`, `CarouselView`, `ListView`, or Nalu `VirtualScroll` can scroll in the drag direction, the content scrolls first; otherwise the drag promotes to a sheet state change or close. The decision is made by the per-platform `G9SheetViewBorder` handler so it uses real native `CanScrollVertically` / `ContentOffset` data instead of inferred MAUI heights. Below the largest detent the SHEET takes the drag instead — see `ScrollingExpandsSheet` under "Sizing Rules"; that gate is always open for a single-detent sheet, so this priority is unchanged for them.
- The iOS and Windows handlers resolve the scroller under the finger by walking UP the tree, and skip scrollers that cannot scroll vertically at all. Without that, a side-scrolling row inside the body (a tile rail, a chip strip) resolved as "the scroller", reported "cannot scroll" for every vertical drag and handed the gesture to the sheet — so the body's own vertical scroller never got a chance. Android resolves the OUTERMOST scroller under the point and was already correct.
- **Fit-to-content sheets receive no drag at all.** `ShouldEnableNativeStateSwiping` turns
  `EnableSwiping` off for `SizeMode.FitToContent` (and for any sheet with an `OnBackRequested`
  callback or `IsDraggable = false`), so the per-platform handlers forward nothing and none of the
  drag rules above apply to them. They are dismissed by the overlay tap, hardware back, or their own
  close button. Do not document or rely on a drag-to-close for a fit sheet.

Example:

```csharp
await G9BottomSheetHelper.ShowFullScreenAsync(
    this,
    () => new MyModalContentView(),
    G9BottomSheetOptions.FullScreenModalOptions("Edit") with
    {
        HardwareBackCloses = false,
        OnBackRequested = async source =>
        {
            if (source == G9BottomSheetBackRequestSource.HardwareButton)
            {
                await G9ToastHelper.ShowToastAsync(G9StringResources.Warning, G9ToastType.Warning);
                return G9BottomSheetBackAction.DoNothing;
            }

            return G9BottomSheetBackAction.Close;
        }
    });
```

## Toolbar Sheets

Use the built-in toolbar instead of custom modal headers for full-screen bottom sheets.

```csharp
var toolbarItems = new List<ToolbarItem>
{
    new G9BottomSheetToolbarItem
    {
        MaterialIcon = MaterialIcons.Info,
        ShowBusyIndicator = true,
        AsyncAction = () => G9ToastHelper.ShowToastAsync("Info", G9ToastType.Information)
    }
};

await G9BottomSheetHelper.ShowFullScreenAsync(
    this,
    () => content,
    G9BottomSheetOptions.FullScreenModalOptions(
        title: "Toolbar Sheet",
        toolbarItems: toolbarItems));
```

If toolbar actions depend on lazily-created content, use `ToolbarItemsFactory`.

### Custom header-action icon size

`G9BottomSheetToolbarItem.IconSize` (dp) sets a custom glyph size for a single header action; `0` (default) uses the shared `G9LayoutMetrics.ToolbarIconSize`, so existing toolbars are unchanged. Combine with `IsActive` + `ShowActiveBadge` for a stateful, TAPPABLE action. Use this instead of a custom `HeaderTrailingView` when all you need is a differently-sized/stateful icon. For a READ-ONLY trailing glyph (no tap target at all — e.g. the tree "indicator" star shown in the main-map tree detail sheet's header, see `AiGuides/12-Tree-Pot-Map-Operations.md`), use a plain `HeaderTrailingView` instead (`MapViewModel.BuildIndicatorHeaderView`) — a `G9BottomSheetToolbarItem` always renders as an interactive button.

> **No in-content modal chrome.** The old `Views/ContentViews/Base/ModalBase` / `ModalHeader` / `ModalFooter` / `ModalButton` / `ModalIconButton` shell was removed. Content views are body (+ optional in-view pinned footer row) only; the sheet's toolbar owns the title/close/header-actions (`FullScreenModalOptions(title)` / `with { ShowToolbar = true, Title = …, ShowCloseButton = true }`). Do NOT reintroduce a per-modal header inside content.

## Shared Header / Footer Template

When `ShowToolbar = true` (all `FullScreenModalOptions(...)` defaults do this), the helper renders a shared 3-slot header above the sheet body and an optional footer below it. Every spacing, padding, icon button size, and footer button size comes from `G9LayoutMetrics`, so every sheet using the template has the same visual rhythm across Android, iOS, Mac Catalyst, and Windows.

**Standard body-top gap.** The helper gives every toolbar sheet's body a `SheetHeaderVerticalGap`
(10) top margin below the header hairline (`CreateSheetContentRoot`) — the band's own padding ends
AT the hairline, so without this the first body element sat flush against the divider. Bodies keep
adding NO top gap of their own; `ResolveHelperChromeHeight` accounts for it in all
placeholder/provider/memo math.

**Opting OUT of that gap — `G9BottomSheetOptions.UseStandardBodyTopGap = false`.** Set it only for a
body whose FIRST element is itself a chrome band that has to read as part of the header — a tab
strip, a segmented control, a sticky filter bar. For those the standard gap leaves the band orphaned
between two rules instead of attached to the header. Do NOT use it to make a body "look tighter".
The flag drives BOTH the layout margin and the `ResolveHelperChromeHeight` term, so a fit-to-content
sheet still sizes correctly with the gap suppressed — never suppress the gap by giving the body a
negative top margin, which would desynchronize the two. **Always comment the call site with why that
sheet is an exception.** Current opt-out: `TaskSamplingMapContentView` (the sampling map modal),
whose Map / Samples / Packages tab strip is sticky to the header.

**Header band height + centering.** The header grid enforces `G9LayoutMetrics.SheetHeaderMinHeight` (64) as a MINIMUM height (it still grows for taller content) via a single `Star` row, and every slot — the back/close button, the title, the trailing toolbar icons — is `VerticalOptions=Center`, so they all sit on ONE centre line in the band. Before 2026-07 the band was just `close button (30) + SheetHeaderVerticalGap ×2 ≈ 50dp`, which read as cramped. The header is also wrapped (`WrapHeaderWithBottomDivider`) with a **subtle bottom hairline** (theme-aware `OutlineVariant` @ 0.35α, `G9BottomSheetFooterTopBorderThickness`, mirroring the footer's TOP border): the header and body share the sheet background, so without a line the centered title reads as "too high" against the perceived header+body region — the hairline delineates where the band ends so its centering is apparent. This applies to BOTH the slotted header and the full-custom `HeaderView` host. As of 2026-07 the multi-select `G9SelectionSheet` (its count pill + Done now ride the header's `HeaderTrailingView`) and the `G9BottomSheetListPickerModal` list picker ALSO use this shared header — the only sheets that opt OUT are the **diagnostics-chrome** ones (`ShowToolbar=false`, `FullScreenEdgeToEdgeModalOptions`) which paint a full-bleed coloured header.

The `G9BottomSheetListPickerModal` refactor is also the reference for "how a picker fills below the shared header": it dropped `IG9BottomSheetSizedView` + its explicit list-height math and instead fills a `*` row (`RowDefinitions="*,Auto"`, list in row 0, Apply button in row 1). This is because `ResolveFullScreenContentHeight` returns the FULL height when `ShowToolbar=true` (it does NOT subtract the header), so an explicit-height sized view would overflow the toolbar — every full-screen sheet with a toolbar must fill the content area via layout, not compute its own height.

### Header (leading | title | trailing)

Default slots:

- **Leading** — RTL-aware back arrow when `ShowCloseButton = true` (a close `×` when `UseCloseIcon`). Tapping it routes through `OnBackRequested` / `IsCancelable` exactly like the toolbar back button. It paints and MEASURES `ModalHeaderIconButtonSize` (30) but is TAPPABLE over 44 × 44 — see "Virtual hit area" below.
- **Title** — `Title` text, `G9Palette.Current.Secondary` (green), bold, `G9LayoutMetrics.SheetHeaderTitleFontSize` (16.2). By default the title is centered in the middle slot (`HeaderTitlePlacement = Center`). Use `G9BottomSheetHeaderTitlePlacement.NearBack` when the title is long or the trailing slot has multiple action icons. **A sheet with a standard header never renders its own title label** — set `Title` (+ `HeaderTitlePlacement`) and inherit the shared colour/size, or the sheet becomes the one whose header looks different (see the 2026-07-28 entry in `AiGuides/08-UI-UX-Design-System.md`).
- **Trailing** — `ToolbarItems` rendered with `G9BottomSheetToolbarItem` (icons, busy state, active badge). The trailing slot is always on the opposite end from the leading slot; the helper applies `FlowDirection` to the header so RTL automatically flips the slot order.

#### Virtual hit area on the back/close button (costs zero layout)

The leading button paints a 24 dp glyph in a 30 dp box (`ModalHeaderIconButtonSize`). 30 dp is **under
the app's own 44 dp touch floor** (design guide §9b/§10) — a press landing a few dp off the paint
falls through to the header behind it, which is the "the close button sometimes doesn't work" defect
class. It is enlarged by `AttachHeaderBackButtonHitSlop`, which adds **nothing to the visual tree and
nothing to layout**: the HEADER grid carries one `TapGestureRecognizer` that hit-tests
`e.GetPosition(backButton)` against the button's bounds inflated by `ResolveHeaderBackButtonHitSlop()`.

- **Size.** The slop is derived, not chosen: the target is the largest square the band can host —
  `SheetHeaderMinHeight − 2 × SheetHeaderVerticalGap` (64 − 20 = **44 dp**), i.e. **7 dp per side**
  around the 30 dp button. 2.15× the old touch area, exactly the §10 floor, and it tracks a live edit
  of either token (Developer Tools metric editor included).
- **Why not padding / margin / `Minimum*Request`.** All three widen the MEASURED box, and the leading
  column is `Auto` — so the title would be pushed away and the band could grow. Guide §9b forbids
  buying hit slop with padding for exactly this reason (`EntityStateBadge` shipped that bug). The
  button's own geometry must stay untouched.
- **Two gesture owners, disjoint regions.** The button keeps its OWN recognizer, so a direct hit
  behaves exactly as before on every platform; the header handler serves only the ring OUTSIDE the
  button's real bounds and early-returns for points inside them. That guard is load-bearing: iOS/WinUI
  can raise both recognizers for one press, and handling it twice would fire `HandleBackRequest` twice
  and close two stacked sheets on a single tap.
- **RTL.** The test is relative to the BUTTON, so it mirrors with it — no flow-direction branch.
- Only the STANDARD button gets this. A caller-supplied `HeaderLeadingView` (or a full `HeaderView`)
  owns its own gesture and hit area — give it `MinimumTouchTarget` or its own slop.

#### How "centered" really centers

When the title placement is `Center` (the default), the helper switches the header grid columns from the legacy `Auto / Star / Auto` to **`Star / Auto / Star`**. Both side slots now reserve equal proportional widths, so the centered title is visually centered on the screen even when only one side has content (the very common back-button + title case from `FullScreenModalOptions(...)`). The leading slot is left-anchored inside its Star column (`HorizontalOptions = Start`), the trailing slot is right-anchored (`HorizontalOptions = End`), and the middle column auto-sizes to the title text — long titles get `LineBreakMode = TailTruncation` so they never push the side slots off-balance.

You can opt out per sheet with `ReserveEmptyHeaderSlots = false`, which reverts to the legacy `Auto / Star / Auto` layout (empty trailing slot collapses to zero width). The opt-out is also bypassed automatically when a spanned header view (`HeaderLeadingAndTitleView` / `HeaderTitleAndTrailingView`) is in use, because those spans intentionally take their natural width; or when the title placement is `NearBack`, which always uses `Auto / Star / Auto` so the title can sit flush with the leading slot.

Each slot is independently overridable via `G9BottomSheetOptions`:

```csharp
G9BottomSheetOptions.FullScreenModalOptions("Title") with
{
    HeaderLeadingView  = MyLeadingBadgeView,
    HeaderTitleView    = MyTitleViewWithIcon,
    HeaderTrailingView = MyTrailingActionsView
};
```

Two adjacent slots can be merged into one spanning view:

- `HeaderLeadingAndTitleView` spans columns 0..1 (replaces both leading and title; trailing still renders).
- `HeaderTitleAndTrailingView` spans columns 1..2 (replaces both title and trailing; leading still renders).

When you need the entire header replaced, set `HeaderView`. It overrides all three slots, both spanning variants, and any of `ShowCloseButton` / `Title` / `ToolbarItems`. Setting `HeaderView` does **not** enable the header on its own — `ShowToolbar` must still be true (it's the gate that decides whether the header row is rendered at all).

Priority order when multiple header options are set:

1. `HeaderView` — replaces the whole header.
2. `HeaderLeadingAndTitleView` — spanned leading+title, trailing built from `HeaderTrailingView` / `ToolbarItems`.
3. `HeaderTitleAndTrailingView` — spanned title+trailing, leading built from `HeaderLeadingView` / back button.
4. Per-slot views (`HeaderLeadingView` / `HeaderTitleView` / `HeaderTrailingView`), with the default back button / title / toolbar-items filling any slot left unset.

`ToolbarItemsFactory` is meaningless when the trailing slot has a custom view or when `HeaderView` replaces the header — the helper has nowhere to graft the factory-produced items into.

### Footer

Set one of these to render the shared footer area beneath the sheet body:

- `FooterButtons` — a list of `View` buttons rendered in rows of `FooterMaxButtonsPerRow` (default 3). Buttons are sized equally (every column is `Star`), so each row of equal-width buttons keeps the same visual rhythm. When you supply more buttons than fit on a row they wrap to a new row that keeps the same equal-width columns.
- `FooterView` — a fully custom footer view. When set, `FooterButtons` is ignored.

`FooterMaxButtonsPerRow` defaults to `G9LayoutMetrics.G9BottomSheetFooterMaxButtonsPerRow` (`3`). The wrap is purely on count — there is no measure-based packing — so callers stay in control of the row breakpoint regardless of platform width.

The footer adds a thin top divider drawn from `G9Palette.Current.OutlineVariant` so it visually separates from the body. It is also available without the helper toolbar: setting `FooterButtons` or `FooterView` on a sheet that has `ShowToolbar = false` wraps the body content in a 2-row grid (body Star + footer Auto) and keeps the existing drag gestures attached to the body.

```csharp
await G9BottomSheetHelper.ShowFullScreenAsync(
    this,
    () => contentView,
    G9BottomSheetOptions.FullScreenModalOptions("Edit") with
    {
        HeaderTitlePlacement = G9BottomSheetHeaderTitlePlacement.Center,
        FooterButtons =
        [
            new G9SafeButton { Text = "Cancel", BackgroundColor = palette.SurfaceContainerHigh, TextColor = palette.OnSurface, ... },
            new G9SafeButton { Text = "Save",   BackgroundColor = palette.Primary,             TextColor = palette.OnPrimary, ... }
        ]
    });
```

## Lazy Loading

`DeferContent = true` is the default. Deferred sheets use `DeferredContentView` and show a spinner until content is ready. Both direct-content and factory overloads honor `DeferContent = false`.

- **Set `DeferContent = false` when you pass an ALREADY-BUILT view (not a factory) whose content is populated synchronously in its constructor.** Deferring such a view is pointless (there is nothing to build after open) and HARMFUL: the sheet shows the loading skeleton at the seeded height, then SWAPS the real view in, and that swap triggers a fit-to-content re-measure that briefly shrinks the sheet ~20px before settling back — the "opens correct, shrinks, pops back" glitch. Showing the pre-built content directly measures the sheet once, at the right height. (Deferral is for FACTORY content that does real build/data work after open — e.g. `SamplingTargetContentView` builds lazily and is fine; `SampleCategoryContentView` is built eagerly and must set `DeferContent = false`.)
- **A `LoadableSheetContentView` loading root must fill the seeded height, not size to the spinner.** During the loading window the sheet holds its seeded/memo height. If the loading spinner sits in a `RowDefinitions="Auto"` grid, the Auto row is the 40dp spinner pinned to the sheet TOP, and a later layout pass re-centres it (the "spinner starts near the top, then jumps to centre" glitch — noticeable when the seed is large). Use a single **star cell** (no `RowDefinitions`) so the cell fills the seeded height and the centred spinner is centred from the first frame; fit-to-content still measures the real body's natural height when it reveals.

### Skeleton placeholders (`LoadingSkeleton`)

Set `G9BottomSheetOptions.LoadingSkeleton` (`ListRows` / `FormFields`) + `LoadingSkeletonRowCount`
to replace the deferred spinner with a `G9Shimmer` skeleton: static gray shape rows swept by a
highlight band whose animation runs on the platform **render/compositor thread** (Android AVD on
the RenderThread, iOS/MacCat Core Animation), so it keeps moving even while the UI thread builds
the heavy body — unlike every UI-thread shimmer this app tried before. `G9SelectionSheet`
passes `ListRows` with the real item count so the placeholder matches the final list. See
`Common/Components/G9/G9Shimmer/G9Shimmer.md`.

## Open Then Fill (async data load) — `IDeferredSheetLoad` / `LoadableSheetContentView`

`DeferContent` / `DeferredContentView` defer the **construction** of a view tree by a timer; they do **not** know about async **data** loading. For sheets whose body needs a non-trivial data fetch (DB reads, service calls) before it can render, do **not** load the data before `ShowG9BottomSheet` — that delays the open (the user taps and waits) and, because the open lands outside the single-key `ShowG9BottomSheet` throttle window, a rapid double-tap can open the sheet twice. Instead, open the sheet immediately with a loading/preview state and fill it after it is visible.

Mechanism:

- **`IDeferredSheetLoad`** (`Common/Components/G9BottomSheet/IDeferredSheetLoad.cs`) — content implementing this is detected when the sheet body is prepared. `G9BottomSheetHelper` skips the `DeferredContentView` wrapper for it (the view paints its own spinner/preview, so a second timer-spinner would only add a delay) and calls `LoadDeferredAsync(ct)` **once, after the open animation completes** (from `RunOpenedCommandLater`). The `ct` is cancelled when the sheet tears down (`CancelDeferredLoad` in `CleanupSheetVisualsNow`), so a late-completing load never applies onto a dead sheet. The call is fire-and-forget through `G9SafeCommand`, so a data-fetch failure can never crash the open.
- **`LoadableSheetContentView`** (non-generic, `Common/Bases/LoadableSheetContentView.cs`) — the XAML-friendly base (`<bases:LoadableSheetContentView x:Class="…">`, no `x:TypeArguments`). Owns the `IsLoading` bindable (bind spinner/body visibility to it), the `IsClosed`/`MarkClosed()` guard, the per-open `CancellationToken` (cancelled on sheet close **and** view detach), and the `G9BottomSheetHandle` injection. Subclasses implement `RunDeferredLoadAsync`.
- **`LoadableSheetContentView<TData>`** (generic) — for code-built (non-XAML-root) bodies: pass a `Func<CancellationToken, Task<TData?>>` loader to the base ctor and implement `ApplyLoadedData(TData)`; the base runs the loader off-thread, marshals the apply to the UI thread, and clears `IsLoading`. A `null` result means "nothing to apply" (cancelled / target gone); override `OnLoadReturnedNull` to react.

Because the loadable view is itself the sheet body **from the first frame**, it is also the `IG9BottomSheetContentHeightProvider` the helper resolves at attach time; raise `G9BottomSheetContentHeightChanged` from your apply step so the fit-to-content sheet grows from the spinner height to the real content height. (A nested "host that builds the child after load" would break height resolution, which is captured at attach — keep the loadable view single-tier.)

Reference implementation: `Views/ContentViews/Map/BlockInfoContentView` (block tap — preview header shown instantly, heavy block/cultivar/sample data filled after open).

```csharp
// Open immediately with a preview; the heavy fetch runs after the sheet is visible.
var modal = new BlockInfoContentView(preview, ct => LoadBlockInfoAsync(…, ct));
G9BottomSheetHelper.ShowG9BottomSheet(modal, G9BottomSheetOptions.FitToContentOptions() with
{
    ClosedCommand = new RelayCommand(() => { modal.MarkClosed(); ClearSamplingMapSelection(); })
});
```

Use the factory overload for expensive content:

```csharp
await G9BottomSheetHelper.ShowFullScreenAsync(
    this,
    () => new HeavyContentView(),
    G9BottomSheetOptions.FullScreenModalOptions("Heavy Content"));
```

For fit-content sheets, the helper first shows a minimum spinner height and then remeasures after deferred content loads, animating from the spinner height to the measured content height (`AnimateFitToContentHeight`) so the resize never snaps. Each tween tick:

- Updates `CollapsedHeight`, `HalfExpandedRatio`, `FullExpandedRatio`, and re-applies `State = Collapsed` on the sheet.
- The sheet's `OnStateChangedInternal` short-circuits when the state value didn't actually change, so each tick is a cheap height/translation update — no re-running of `AnimateG9BottomSheet` per frame.
- The sheet's `UpdateCollapsedHeight` raises `PositionChanged` after the resize so the modal-overlay alpha and backdrop card recede track the smooth height change.

## Post-reveal mutation → the "blink", and the readiness gate (`IDeferredContentReadiness`)

**The single most common way to ship a broken sheet.** The covered reveal (`DeferredContentView` with `FadeContentIn`, on by default for sheets) masks a body's **construction and first layout**, then fades the spinner on a fixed ~220 ms timer. It does **not** know about anything the body does *after* that: an async data load that fills pickers/lists, a collection `Clear()`+repopulate, a `ScrollView`/`CollectionView` scroll-to-selection on `Loaded`, an image source that resolves late. Any such change lands in full view, and the user sees the content appear and then **re-render once — the "blink."** This was diagnosed in 2026-07 across the site selector (jump-scroll to the selected row), the tree edit/plant/replant form and register-sample sheet (crop/variety/attachment data filling ~1–3 s after reveal), and the list-picker modal (items applied after a viewport-stability wait).

**There are exactly two correct ways to author a body that isn't settled at construction. Pick one:**

1. **`LoadableSheetContentView` / `IDeferredSheetLoad`** (previous section) — the body paints its **own** `IsLoading` spinner from the first frame and reveals via `RevealLoadedContentAsync` once the data is applied. Use this when the body is genuinely a "load then show" screen and you want a visible in-body loading state. The helper skips the covered-reveal spinner for these (they own the spinner).

2. **The readiness gate** — the body reveals through the normal covered spinner, but tells that spinner **"don't drop the cover yet."** Use this for a body that is *mostly* built at construction and just needs its late touches applied under cover (the common case: a form whose static fields are ready but whose pickers fill from a quick local DB read, or a list that scroll-positions on `Loaded`). The contract is `IDeferredContentReadiness` (`Common/Bases/IDeferredContentReadiness.cs`), and **every `DebugAwareContentView` already implements it** — so for the usual app content view it is just two calls:

   ```csharp
   public MySheetContentView(...)
   {
       InitializeComponent();
       // ... synchronous setup ...

       GateContentReadiness();               // 1. opt in — hold the cover
       RunBackgroundSafe(async () =>
       {
           try     { await LoadAndApplyAsync(); }   // fill pickers / lists / selection
           finally { await MainThread.InvokeOnMainThreadAsync(MarkContentReady); } // 2. release
       }, nameof(LoadAndApplyAsync));
   }
   ```

   - `GateContentReadiness()` (constructor) flips the view from "ready immediately" to "held". **Non-gated views are unaffected** — the gate is opt-in and a no-op unless you call this.
   - `MarkContentReady()` releases the cover. **Always call it in a `finally`** (or an unconditional path) so an errored/cancelled/early-return load still reveals — the reveal's own ~5 s timeout is a last resort, not the mechanism.
   - For a `Loaded`-driven mutation (scroll-to-selection, viewport-stable apply), call `MarkContentReady()` *after* that work in the `Loaded` handler (see `FarmsAndGreenhousesContentView`).
   - A body that is **not** a `DebugAwareContentView` (e.g. `G9BottomSheetListPickerModal : Grid`, `G9SelectionSheet : Grid`) implements the interface directly with a `DeferredContentReadinessSignal` field forwarding `IsContentReady`/`ContentReady`, and calls `_readiness.MarkReady()`.

   How it works: `DeferredContentView`'s covered reveal, after its glyph-settle delay, awaits `ContentReady` (via `WaitForContentReadinessAsync`) before fading the spinner. The body is already parented under the opaque spinner at that point, so its `Loaded` has fired and its async load is running **while covered** — it is revealed already-settled. On a slow device the spinner simply shows a beat longer; it never blinks.

**Authoring checklist for a new sheet body — does it settle at construction?**

- Builds entirely synchronously (a menu, a static form, content passed in via constructor)? → **Do nothing.** It reveals cleanly. (tree/pot operations, status, delete, sort/filter pickers.)
- Loads data async and wants a visible in-body loading state / spinner? → **`LoadableSheetContentView`.** (`BlockInfoContentView`.)
- Loads data async / scrolls / repopulates after construction but should reveal *complete*? → **Readiness gate** (`GateContentReadiness()` + `MarkContentReady()`, or direct `IDeferredContentReadiness` for non-`DebugAwareContentView` bodies). (site selector, tree edit/plant/replant form, bulk-edit form, register-sample, new mark, list-picker, `G9SelectionSheet`.)
- Already shows its own busy/loading indicator inside the body (e.g. a nested list with an `IsBusy` shimmer)? → **Do nothing** — that IS its loading state; gating it would just delay the whole sheet.

**Do not** try to fix a blink by lengthening the glyph-settle delay, by adding your own `IsVisible` toggles, or by suppressing the mutation — those either add latency for everyone or hide state the user needs. Hold the cover until the content is actually settled, then reveal once.

> ⚠️ **When you change the readiness plumbing on `DebugAwareContentView` (or any base a sheet body derives from), do a CLEAN Android build (`rm -rf obj/Debug/net10.0-android bin/Debug/net10.0-android`).** Incremental compilation on this repo has repeatedly served a stale base assembly where a base-class method (here `GateContentReadiness`) silently had no runtime effect — the gate read "ready" instantly and every gated sheet blinked, while the source looked correct. If a base-level behavior "isn't taking effect," suspect a stale build before you suspect the logic.

### Measured background — why the sizing engine is built this way (2026-07 instrumentation)

A full per-sheet lifecycle trace (26+ real sheets, Debug/emulator; the reusable trace recipe is in
`AiGuides/06-Android-Debug-Loop.md`) established the facts these rules rest on. Keep them in mind
before "simplifying" any of the machinery below:

- **The first measure of a sheet body on Android is ALWAYS cold** (`raw=0`, every sheet, every
  time): an unattached / not-yet-laid-out MAUI tree measures 0 or garbage. This is platform
  reality, not an app bug — see
  [dotnet/maui #4880](https://github.com/dotnet/maui/issues/4880),
  [#7531](https://github.com/dotnet/maui/issues/7531),
  [#18328](https://github.com/dotnet/maui/issues/18328),
  [#10281](https://github.com/dotnet/maui/issues/10281) and the team's
  [layout design doc](https://github.com/dotnet/maui/blob/main/docs/design/layout.md). A native
  Android `G9BottomSheetBehavior` gets its height from the native layout pass for free; a
  cross-platform sheet cannot, so the placeholder/memo/settle machinery is unavoidable — the
  engine's job is to make the correction INVISIBLE, not to remove it.
- **Historical defects the current rules replace** (all verified in the trace, all fixed 2026-07):
  every fit sheet used to open at the 180dp loading floor and then POP instantly to its real
  height (up to +300dp) because post-open resizes only animated on the provider path; the floor
  clamped caller placeholders and genuinely-short bodies (a 125dp menu forced to 180); caller
  height estimates settled exactly ~65dp low because they couldn't know the helper header chrome;
  the opened-work timer waited the full non-scaled 399ms although a fit open's real motion is
  ~80ms, starting deferred loads ~300ms late; the close-then-open replace chain added ~400ms dead
  time per step and rebuilt the previous view from scratch on every back.
- **Font-glyph "tofu"**: icons render as empty rectangles for the first frames after a body
  attaches — a MAUI-Android first-draw race
  ([#25783](https://github.com/dotnet/maui/issues/25783),
  [#19846](https://github.com/dotnet/maui/issues/19846),
  [#5157](https://github.com/dotnet/maui/issues/5157)) that typeface pre-warming alone
  (`Common/Helpers/IconFontPreWarmer`, run at startup) cannot fully remove. The covered reveal
  exists for this; and because a native DETACH+RE-ATTACH re-runs the race, revealed content must
  never be re-parented (see the reveal-host rule below).
- **Loading visuals must never drive the size**: `IsContentLoaded` flips at load START
  (mid-spinner), and skeletons/spinners measure differently from the final body — any path that
  treats the loading UI's measurement as authoritative drags the sheet around. Only
  `IsRevealSettled` / `IsLoading` describe the visual state.

### Fit-To-Content Sizing Engine (cap + scroll + count estimate + no-jump)

Fit-to-content needs a content height. The naive "measure the view tree" only works for content with an **intrinsic height**; it fails for greedy content (a virtualized list, a `*`-row, a `ScrollView`) which reports 0 / ∞ unconstrained and just returns the constraint when bounded. The engine therefore resolves the height in three tiers and clamps the result:

1. **Height provider (preferred for tabbed / list content).** If the sheet content (or a descendant) implements `IG9BottomSheetContentHeightProvider`, the helper calls `GetDesiredG9BottomSheetContentHeight(width, cap)` and subscribes to `G9BottomSheetContentHeightChanged`. This is the **count-aware / tab-aware** path: e.g. the samples tab returns `chrome + itemCount × rowHeight`, so a short list opens small and a long list returns a large number. `SamplingTargetContentView` and `BlockInfoContentView` implement it (tab-aware: each raises the change event on tab switch + data/count change). The provider returns the **natural** height; the helper caps it.

   **A provider value is authoritative — a static estimate in it silently rots.** Because the helper trusts the provider (its settle passes just re-ask it, they don't re-measure independently), any hard-coded constant a provider returns has NO safety net: when the real body outgrows the constant it clips content out of frame with no animated correction (this bit `SamplingTargetContentView`'s general-info tab — a `TreeInfoCardView`-based body whose static "126dp" estimate drifted below the card's grown ~165dp and clipped the register button). **Rule:** once the tab body is attached, `Measure(availableWidth, ∞)` it and return the real height; keep a static estimate ONLY as the pre-attach cold-open value (Android measures a detached tree as 0), and aim it slightly LOW so the post-attach correction *grows* the sheet (natural) instead of shrinking it.

   **Close the cold-open race with a `Loaded` re-raise.** On a slow cold open, all of the helper's settle passes (0 / 160 / 380 ms) can run before Android attaches the tab content, so the measured override never gets a chance and the low cold estimate sticks — producing an *intermittent* few-dp clip (mostly fine, occasionally short). Subscribe the measurable body's `Loaded` and call `RaiseG9BottomSheetHeightChanged()` from it: that is the deterministic "measurable now" signal, and the helper debounces it into a single animated resize (repeat raises on tab re-attach are harmless).
2. **Root-scroller → cap directly.** If the body *is itself* a scroller (`IsRootGreedyScroller` unwraps transparent wrappers and finds a `ScrollView` / `CollectionView` / `CarouselView` / `ListView` as the whole body, no provider), the engine **does not measure it** — it uses the cap. A scroller reports its viewport, not its content, and on a **cold first open Android hasn't measured the inner children's text yet** (label height = 0 until rendered), so the natural height comes back far too small and *never settles within that open* (confirmed in AGENT logs: a 40-row stack measured 74 → 202 → only reached the real 1290 on a later *warm* open). A scroller-as-body inherently means "this scrolls", so capping + inner scroll is the correct, deterministic answer. **For exact-fit, use a height provider or non-scrolling content** instead of a raw scroller as the body.
3. **Generic measure** (`MeasureContentHeight`) for non-scroller content without a provider — `ResolveFitToContentMeasureTarget` unwraps `ScrollView` / `ContentView` / single-child grids to the real inner view and measures it with `Measure(width, ∞)`.
4. **Clamp to the cap.** `cap = G9BottomSheetOptions.MaxFitToContentHeightRatio × screenHeight` (default **0.75 = 75%**). Short content fits exactly; tall content stops at the cap.

**Bounded body → scroll.** Fit-to-content bodies are hosted `Fill` (`CreateFillHost`) so the sheet body is a **bounded viewport** equal to the resolved height. Under the cap the body equals the natural height (no scroll); at the cap the inner `ScrollView` / virtualized list scrolls. This is what makes "grow to 75% then scroll" work.

**Scroll bars are hidden inside sheets (`G9BottomSheetScrollBarPolicy`).** The visible track/thumb reads as noise on a short floating sheet, so every scroller inside an `G9SheetView` has its `ScrollBarVisibility` forced to `Never` (scrolling itself is untouched). It is done with **handler mappers** (`ScrollViewHandler` on every platform; `CollectionView`/`CarouselView` on the mobile targets — Windows keeps default bars, it's a dev-only target), NOT a tree walk or per-XAML flag: a mapper fires the instant a scroller's platform view is created, so tab-switched and deferred/"open-then-fill" scrollers are covered with no timing gap. The mappers are global and self-scope by walking the parent chain for an `G9SheetView` ancestor, so full-page scrollers keep their normal bars. Registered once from `UseG9SheetView`.

**No-jump rule (instant vs animated).** `ApplyFitToContentHeight` snaps **instantly** only for the
pre-open passes; **every post-open change animates** — provider-driven tab switches / data loads
AND layout-settling growth surfaced by the tracker, the scheduled settle passes, or the
deferred-content swap (`shouldAnimate = (animate || sheet.IsOpen) && IsFitContentSettled`). On open
the sheet never shows a small→large blink, and once it is on screen it never pops — corrections
read as a smooth grow/shrink. The provider change event is debounced
(`FitContentRemeasureDebounceMs`) into one animated resize.

**Floor semantics (loading floor vs absolute minimum).** The 180dp
`G9LayoutMetrics.FitContentLoadingMinHeight` is a **loading** floor: it applies only while the
height is genuinely unknown (a cold first measure with no previous height, or the spinner window
of a deferred sheet with no placeholder). Authoritative heights — a caller placeholder / height
memo, a provider value, or a real measure of loaded content — are clamped only to the absolute
minimum (`FitContentAbsoluteMinHeight` = 80), so a genuinely short body (e.g. a 125dp two-row
menu) opens at its real height instead of being inflated to 180. A **cold re-measure while the
sheet already has a height holds the current height** (tier `measureHold`) rather than collapsing
to the floor.

**Helper chrome contract (BODY heights).** Caller-supplied heights — a provider's
`GetDesiredG9BottomSheetContentHeight` result, `DeferredLoadingPlaceholderHeight`, the height memo —
are **BODY heights**: the helper adds the chrome it renders itself (`ResolveHelperChromeHeight`:
the shared header band + hairline when `ShowToolbar`, the grabber area, `Padding`). Callers must
NOT bake the shared header into their estimates (that was the source of the constant ~65dp
under-estimate the selection sheets used to settle through). For today's toolbar-less provider
sheets the chrome is zero, so the contract change is behavior-neutral for them.

**Height memo — persisted across restarts (`G9BottomSheetHeightMemoStore`).** When a fit body
settles through the measure tier, its BODY height is recorded keyed by body identity + width-dp +
culture + platform font scale, and PERSISTED to Preferences
(`AppStorageKeys.G9BottomSheetHeightMemo`) — so even the FIRST open of a sheet per session (after a
cold start) opens at its remembered height instead of the loading floor. The next open consumes
the memo as an implicit placeholder — no spinner→content resize.

The safety and staleness model that makes persistence acceptable:

- A memo value is **only ever a better opening guess** — the engine still measures the loaded
  content and corrects with an ANIMATED resize, so a stale/wrong entry degrades to the no-memo
  behavior and can never strand a sheet at a wrong size.
- The blob is **version-stamped** and discarded wholesale on an app update (layout changes are
  the systematic staleness source); width/culture/font-scale changes MISS the cache (they are in
  the key) instead of hitting a wrong value.
- **Variability belongs in the key, chosen by the caller** — the store is dumb, most-recent-wins.
  A sheet whose height varies by situation (permissions, item counts) bakes the variable into
  `G9BottomSheetOptions.HeightMemoKey` (see `CompoundSortPickerContentView`'s
  `{title}|{fields.Count}`); do not add variance-detection heuristics to the store.
- Heights are **DEVICE-specific**: text metrics differ across platforms (StaticLayout vs
  CoreText vs DirectWrite) and wrap points differ across widths/font scales — the storage key is
  `includeInBackup: false` so the memo never travels to another device.
- **Performance:** lazily loaded on the first sheet open (never on the startup path); writes are
  debounced (~3 s) and capped at 64 entries; identical re-records are no-ops.

**First-install seeds (`G9BottomSheetHeightSeeds`).** A fresh install has no learned memo, so its
first-ever opens would start at the loading floor. `TryGet` therefore falls back — **on miss
only** — to a compiled seed table of BODY heights keyed by memo identity (device components
stripped off the key). The gate is one-time *by construction*: seeds are read-only and never
persisted, so the first real settle records a learned height that wins every later lookup, and
seed values can change freely in any future version (new layouts, other platforms/devices) with
zero migration or overwrite risk. Rules of the table:

- Values are harvested from a real device pass (Pixel-class emulator, 411 dp / fa-IR / fs1) and
  are approximations elsewhere — fine, they animate-correct like any memo value.
- Type-keyed entries use `typeof(...).FullName` so a renamed sheet **breaks the build** at the
  seed table instead of going silently stale. `HeightMemoKey` entries (localized title in the
  key) only serve the culture they were harvested in; other cultures just miss.
- Situation-variable sheets seed the **smallest** observed value — growing to fit reads as
  natural motion, opening big and shrinking is the glitch the memo exists to prevent.
- **Re-harvest recipe** (after layout changes make seeds drift): add temp `AgentLog` lines (guide
  06 §2 pattern) in `G9BottomSheetHeightMemoStore.Record`/`TryGet`/`EnsureLoaded` logging
  `MEMO|record/hit/miss/loaded|key=…|bodyH=…`, exercise every sheet on the emulator, dump logcat
  filtered by the AGENT tag, copy the surviving key→height pairs into the seed table, then delete
  the temp logs.

Factory content has no type to key on before it is built — a factory sheet opts in by supplying
`HeightMemoKey`. Do NOT give a fit sort/list picker `UseFullScreenLoadingPlaceholder` — that makes
it OPEN at full height and shrink to content; use a skeleton + the memo key instead.

**The loading window HOLDS the height — it never tracks the loading UI.** While loading content
is on screen (deferred spinner/skeleton, a crossfade reveal in progress, or a loadable body's
`IsLoading`), the measure tier holds the sheet's current height (or the loading floor before any
height exists) instead of applying what it measured — a 3-row skeleton measuring taller than the
real 2-row menu must not drag the sheet up and back down (`tier=measureLoading` in the trace).

**"Open then fill" bodies participate in the loading window too.** A `LoadableSheetContentView`
body with `IsLoading = true` counts as loading content (`ContainsLoadingDeferredContent`): its
loading skeleton's measure is NOT treated as authoritative (the sheet holds the loading floor /
placeholder height instead of shrinking to the skeleton's 125dp and re-growing), it is never
recorded into the memo, and its memo-driven placeholder window ends when `IsLoading` flips false
(the helper subscribes in `InitializeFitPlaceholder`). `ProcessingSheetContentView` is excluded —
it sizes itself through its own height provider.

**Late settle re-measures (non-provider, non-scroller tall content).** A top-level `ScrollView` **absorbs `MeasureInvalidated`** (its own desired size doesn't change when its content grows), so the size tracker on the outer root never re-fires for content that measured short on the first frame. `ConfigureSheetContent` therefore also schedules two delayed instant re-measures (~160 ms, ~380 ms) to catch the settled height. (Note: a body that is *itself* a scroller is handled deterministically by tier 2 above — the cap — so these re-measures only matter for plain non-scroll content wrapped in a tracking-absorbing host.) These are cheap no-ops for provider/short content.

**`AttachFitToContentSizeTracking`** subscribes to the **content root's `MeasureInvalidated`** as a secondary settle signal (for non-scroll content that does propagate growth). It is **suppressed when a provider is present** (the provider event drives resizes; a competing layout signal would re-introduce jitter). Two guards keep it loop-free:

- `SheetBehaviorState.IsApplyingFitContent` is raised across `ApplyFitToContentHeight` (whose trailing `InvalidateMeasure` calls would otherwise re-enter the tracker forever).
- `SheetBehaviorState.IsFitContentRefreshScheduled` coalesces invalidation bursts into one debounced remeasure.

The provider subscription + tracker are torn down in `CleanupSheetVisualsNow` (`DetachFitToContentSizeTracking`). Applies to primary and stacked sheets (both run through `ConfigureSheetContent`).

### Lazy heavy-content rendering (G9TabView + DeferredContentView)

Heavy view-tree realization (virtualized lists, charts, drum pickers) freezes the UI thread for ~1–2 s when it runs on the open / tab-switch frame. Two mechanisms keep that off the critical frame:

- **`DeferredContentView`** (default for the factory + `DeferContent = true` paths) shows a spinner, then builds the heavy content after the open animation.
- **`G9TabView` lazy tab realization** (default; see `G9TabView.md`): each tab body is attached to the tree only on **first activation**, behind a spinner + a short delay (`LazyContentRealizeDelayMs` ≈ 240 ms) so the resize / pill animation settles before the build — no blink. After first visit the body stays attached (scroll/focus preserved). Opt out with `G9TabView.EagerContent = true`. This is why the Transfer sheet no longer freezes 1–2 s on open (only the active tab realizes).
- **Covered reveal** (`DeferredContentView.FadeContentIn`, exposed as `G9BottomSheetOptions.FadeDeferredContentIn`, **default ON for sheets** since 2026-07): the built tree is parented at FULL opacity UNDER the still-opaque loading placeholder (spinner or skeleton — both paint an OPAQUE background matching the sheet's `BackgroundColor`); it renders, lays out, and its icon fonts apply completely while covered for `FadeRevealDelayMs` (220 ms), then ONLY the placeholder fades away and is removed, exposing an already-finished tree — the content STAYS inside the reveal host permanently (re-parenting it out, the old "flatten", detached/re-attached the native tree, visibly re-rendering the sheet ~0.4 s after the reveal and re-running the glyph race). Sequential on purpose — a parallel crossfade let half-applied "tofu" glyphs show through mid-fade. The whole window (placeholder phase + cover) is signalled by `DeferredContentView.IsRevealSettled == false`; the fit engine holds its height until it settles. NOTE: `IsContentLoaded` is a load-start latch (flips true mid-spinner) — never use it as a visual-state signal; that mistake once made the engine measure the skeleton as real content. `LoadableSheetContentView` bodies get the same treatment via `RevealLoadedContentAsync(contentRoot)` — call it from the data-apply step instead of flipping `IsVisible`/`IsLoading` directly. Set `FadeDeferredContentIn = false` per sheet for an instant swap.

#### Known-height deferred sheets (no resize jump): `DeferredLoadingPlaceholderHeight`

When a deferred fit-content sheet's final BODY height is **predictable up-front** (a selection
list = `rows × rowHeight + content chrome`), pass it as
`G9BottomSheetOptions.DeferredLoadingPlaceholderHeight` so the **loading placeholder opens at the
final height** and the swapped-in content shares that height → no spinner→content resize. The
value is a **BODY height** — the helper adds its own chrome (header band, grabber, padding) on
top and no longer clamps the result up to the 180dp loading floor, so short estimated sheets are
honored exactly.

The naïve approach (set `DeferredContentView.MinimumHeightRequest` and let the engine measure it) **flaps badly** and must not be used: the first measure runs before layout (`raw = 0`) and falls back to the loading floor, and `DeferredContentView.IsContentLoaded` flips `true` at the *start* of the load delay (spinner still showing) — so `ResolveFitToContentMeasureTarget` dives into the still-present spinner (≈42 px) and the sheet **grows→shrinks→grows** (180 → placeholder → 180 → content). Instead the engine holds the placeholder **statically**:

- `SheetBehaviorState.UseDeferredPlaceholderHeight` is set at construction (deferred + fit-content + `DeferredLoadingPlaceholderHeight > 0`). While true, `ApplyFitToContentHeight` returns the placeholder height directly and **never measures** the placeholder/spinner.
- It's cleared in `DeferredContentView.ContentLoaded` (real content is now in the tree), after which the normal measure path runs once on the actual content.
- The value is still clamped to `MaxFitToContentHeightRatio`, so an over-estimate opens at the cap and scrolls.

`G9SelectionSheet` uses this: a single `EstimateContentHeight(count, showSearch)` drives both its own `MinimumHeightRequest` and the `DeferredLoadingPlaceholderHeight`, so the open height and the loaded height match (a ≤ ~8 px settle is normal when the real rows are a hair taller than the estimate).

> Inherent residual: the heavy *build itself* still blocks one frame when it runs — the spinner + delay **mask** it (the sheet is already open and sized), but it can't be made non-blocking without chunked realization. `DeferredLoadingPlaceholderHeight` removes the *resize*, not the build cost.

### Load Delay vs Open Animation (full-screen sheets)

`G9BottomSheetOptions.LoadDelayMs` controls how long the deferred spinner is shown before the heavy `ContentFactory()` / `Content = newContent` swap runs. For **full-screen** sheets the helper automatically lifts the effective load delay to `max(options.LoadDelayMs, ResolveOpenAnimationDurationMs(options) + 32 ms)` so the swap **never** lands while `AnimateG9BottomSheet` is still tweening `TranslationY`. Without this lift, the layout pass triggered by the swap eats 1–3 frames on Android (the well-known "small stop" mid-rise on full-screen programmatic opens, particularly visible when the configured open duration is longer than `G9LayoutMetrics.DeferredContentLoadDelayMs` of 369 ms).

Partial sheets (half / collapsed / fit-to-content) keep their original `LoadDelayMs` because their motion is size-scaled — a half-open with a 639 ms full-height duration runs in ~320 ms, so the default 369 ms delay already settles after the animation. If a programmer raises `LoadDelayMs` above the auto-computed floor (e.g. to mask a slow factory), the higher value wins via `Math.Max`.

For small fit-content panels where content is already cheap, opt out:

```csharp
G9BottomSheetOptions.FitToContentOptions() with
{
    DeferContent = false
};
```

For modal-like pickers that need a full-screen loading placeholder:

```csharp
G9BottomSheetOptions.FitToContentOptions() with
{
    UseFullScreenLoadingPlaceholder = true
};
```

## List Picker And VirtualScrollView

Shared list picker sheets must use the Nalu-backed `VirtualScrollView`; do not replace it with `CollectionView`.

Important Android rules:

- The bottom-sheet host must provide a bounded full-screen height before the virtual scroller receives items.
- `G9BottomSheetListPickerModal` applies the sheet height through `IG9BottomSheetSizedView`.
- The list picker delays `SetItemsSource(...)` until `VirtualScrollView` has both a real height and a real width.
- `VirtualScrollView`, `G9BottomSheetListPickerModal`, `DeferredContentView`, and helper-owned sheet host wrappers use `Grid` roots instead of `ContentView` roots. Avoid adding `ContentView` wrappers between `G9SheetView.G9BottomSheetContent` and Nalu `VirtualScroll` on Android; that can reintroduce `ContentViewGroup#onMeasure()` crashes during native measurement.

Selection rules:

- `G9BottomSheetListItem` provides stable selection identity through `SelectionIdentity`.
- Always pass current `selectedItems` to `ShowListG9BottomSheetAsync`.
- For single selection that closes immediately, use `allowMultipleSelection: false` and `closeOnSingleSelection: true`.

### Optional search (2026-08)

`ShowListG9BottomSheetAsync(..., searchPlaceholder: "…")` adds a `G9SearchEntry`
above the list. It is **opt-in and off by default** — a two-row sort picker with a
search box above it is worse, not better — so pass a placeholder only for lists
that are genuinely long (the NFC site / pot pickers are the first consumers).

Rules that come with it:

- **The caller sorts; the picker only filters.** There is no built-in ordering,
  because "alphabetical" is wrong for a sort picker and right for a site list.
  Sort with the culture's comparer (`StringComparer.Create(G9Culture.CurrentCulture, …)`),
  not the ordinal default, or Persian titles collate by UTF-16 code unit.
- **The filter mutates the bound collection in place.** `_allItems` keeps the
  unfiltered source; `_items` is what the adapter is bound to. Re-calling
  `SetItemsSource` per keystroke would rebuild the whole virtualized list and
  throw away scroll position and row recycling.
- **It is wired to `DebouncedTextChanged` in CODE, not XAML.** `G9SearchEntry`
  has no `TextChanged` — that name only exists on lower-level MAUI inputs. Written
  in XAML it builds green in Debug (which runtime-parses XAML) and fails at
  **XamlC/publish** with `XC0009: No property, BindableProperty, or event found for
  "TextChanged"`. This was caught by a trimmed Release publish and by nothing else,
  which is exactly the hazard chapter `02` describes. Check the control's real API
  before binding an event in XAML.

## Full-Screen Content Height

Content that needs the resolved sheet height can implement:

```csharp
public interface IG9BottomSheetSizedView
{
    void ApplyG9BottomSheetHeight(double height);
}
```

The helper calls it during preparation. This is required for virtualized content that must know its viewport height before loading.

Content that needs a close handle can implement `IG9BottomSheetAwareView`; the helper assigns an `IG9BottomSheetHandle`.

## Theme And RTL

The helper reads current app flow direction and theme palette when building sheet content.

- Toolbar back icon is RTL-aware.
- Sheet background uses `G9Palette.Current`.
- Shared spacing and radii must come from `G9LayoutMetrics`.
- Test page `TempDesignPageIman` includes language and theme toggle buttons for checking bottom-sheet behavior in LTR/RTL and Light/Dark.

## Testing Buttons

`TempDesignPageIman` contains temporary coverage under the **G9BottomSheet** tab, organized in three groups:

### Core sizing modes (6 buttons)

- **Default sheet (Medium ↔ Large drag)** — modal, draggable, grabber visible, overlay opacity interpolation.
- **Non-modal Medium (page stays interactive)** — `IsModal = false`, single-state Medium, no grabber, no drag, no backdrop card. Verifies page content stays tappable while the sheet is open.
- **Fit-to-content + lazy resize** — deferred content, spinner-to-content smooth resize tween, drag-down body closes.
- **Peek/Medium/Large + backdrop recede** — three-state sheet, drag-snap-to-nearest, backdrop card recede past 75%.
- **Full-screen + drag-to-close + back guard** — full-screen modal, toolbar, hardware-back veto via `OnBackRequested`, drag-down body closes.
- **Lazy full-screen (heavy factory)** — deferred spinner, factory invocation after open animation, `OnContentCreated` callback.

### Two-detent sizing (2 buttons, `G9Controls.Gallery` → Overlays)

The reference app carries the pair that proves the 2026-09 detent work; run BOTH after touching the
drag/clamp/scroll-gate code, on a device (a build says nothing about a gesture):

- **Sheet — peek, drag to FIT (short body)** — dragging up settles exactly under the last row: no
  empty band, and the sheet cannot be dragged past it.
- **Sheet — peek, drag to CAP + scroll (tall body)** — dragging up stops at the cap and the body
  then scrolls; at the PEEK step the same drag expands instead of scrolling; from the top, a drag
  down at the scroller's top edge steps back to the peek and a further one dismisses.

### Composability (3 buttons)

- **Stacked sheet (open more from inside)** — recursive stacked-sheet opening, modal overlay layering.
- **List picker (VirtualScrollView, multi-select)** — Nalu virtualized list, inner-scroll → sheet handoff on edge, multi-selection with initial selection.
- **Toolbar + footer buttons + busy/active badge** — toolbar items with busy spinner + active badge, 3 equal-width footer buttons, centered title default.

### Header / Footer template (2 buttons)

- **Header variants (cycle inline)** — single sheet with a "Next variant ▶" button that cycles through: centered title, legacy slots-not-reserved, near-back, custom leading/title/trailing slot, leading+title spanned, title+trailing spanned, full custom HeaderView.
- **Footer variants (cycle inline)** — same pattern cycling: 2 / 3 / 5 equal-width buttons, custom FooterView.

Run the whole G9BottomSheet tab after changing `G9BottomSheetHelper`, `G9BottomSheetOptions`, `G9BottomSheetSettings`, `G9SheetView`, `DeferredContentView`, `VirtualScrollView`, the helper's header/footer builders, or shared modal metrics. Toggle LTR/RTL and Light/Dark while sheets are open to verify slot order, title alignment, footer divider color, and the backdrop card recede color all stay correct.

## Do Not Regress

- **The drag clamp is the largest ALLOWED detent, not the window.** `ClampDragTranslation` /
  `ResolveMaximumDetentHeight`. Deriving it from the current state is the bug that let a peek sheet
  be dragged to the status bar.
- **`ScrollingExpandsSheet` must stay a no-op for single-detent sheets.** It is what makes a default
  of `true` safe. If `IsAtMaximumDetent` ever stops being trivially true for full-screen /
  fit-to-content sheets, every one of them loses its inner scrolling.
- **`UpdateStateBasedOnNearestPoint` breaks ties toward the CURRENT state.** A fit-to-content sheet
  puts all three detents at the same position; nearest-wins would promote it to `FullExpanded` and
  every state-driven consumer would act on a state it never entered.

- Do not reintroduce `Plugin.Maui.G9BottomSheet`.
- Do not reintroduce a vendored copy of the former third-party library (now removed)'s `SfG9BottomSheet`. The hand-rolled `G9SheetView` is the contract; if you need a feature that's missing, add it to `G9SheetView` rather than swapping the control out.
- Do not create modal pages for full-screen modal-style flows.
- Do not replace list picker virtualization with `CollectionView`.
- Do not hardcode repeated sheet spacing; use `G9LayoutMetrics`.
- Do not create per-modal custom headers when `FullScreenModalOptions` + the 3-slot template (per-slot views, two-slot spans, or `HeaderView`) can handle it.
- Do not hand a `HeaderTitleView` a plain label just to restyle the title. A per-sheet title label opts that ONE sheet out of the shared colour (`Secondary` green) and size (`SheetHeaderTitleFontSize`), which is exactly how the task-detail sheet ended up as the only dark, centred, 22 pt title in the app (fixed 2026-07-28). Use `Title` + `HeaderTitlePlacement`; reserve `HeaderTitleView` for a title that genuinely needs extra VISUALS (an icon, a badge, a subtitle line).
- Do not grow the header back/close button (padding, margin, `Minimum*Request`, a bigger `ModalHeaderIconButtonSize`) to make it easier to press, and do not delete the header-level hit-slop recognizer. The button's box is what the `Auto` leading column measures, so growing it pushes the title away and can grow the band; the tappable region is virtual by design (see "Virtual hit area"). If you change the slop, keep the inside-real-bounds early-return — without it one press can fire `HandleBackRequest` twice.
- Do not change `G9BottomSheetOptions.OpenAnimationDurationMs` / `CloseAnimationDurationMs` per-sheet to compensate for "too fast" or "too slow" motion — leave the per-sheet overrides at `null` and update the app-wide `G9BottomSheetSettings.OpenAnimationDurationMs` / `CloseAnimationDurationMs` instead so every platform stays consistent.
- Do not read `G9BottomSheetOptions.OpenAnimationDurationMs` or `G9BottomSheetOptions.CloseAnimationDurationMs` directly anywhere in the helper. Always route through `ResolveOpenAnimationDurationMs(options)` / `ResolveCloseAnimationDurationMs(options)` so the null-means-Settings contract holds; otherwise a `Configure(new G9BottomSheetSettings { ... })` call silently fails to retune part of the pipeline (typical symptom: the sheet motion changes but the modal overlay fade, fit-to-content resize, opened-command delay or close-completion wait don't, and the animations desync visibly during a drag-to-close).
- Do not bypass `ResolveSheetMotionDurationMs` by setting `sheet.AnimationDuration` directly anywhere except `ApplyOptions` (which seeds it as a fallback). The `AnimationDurationProvider` installed there is what drives size-scaled and direction-aware durations for drag-release snaps; overwriting `AnimationDuration` mid-flight will silently disable both behaviors for the next motion.
- Do not size-scale the modal overlay fade, fit-to-content resize, opened-command delay or close-cleanup wait. These deliberately use the full configured duration so they remain well-defined for short / partial transitions. Size-scaling is reserved for the sheet translation itself (via the `AnimationDurationProvider`).
- Do not load heavy content synchronously before opening a full-screen sheet.
- Do not construct a heavy sheet body synchronously (`new HeavyView(...)`) and pass it to
  `ShowG9BottomSheet(view)` for map/selection sheets. That pays the full XAML-inflation cost on the
  tap before the sheet opens (measured ~360–450 ms for the block-info / sampling-target sheets),
  which both delays the open and lets a rapid double-tap open a second sheet. Use
  `ShowProcessingG9BottomSheet(buildAsync, options, onError)` so the spinner opens instantly and the
  build runs behind it. The `buildAsync` callback must construct the returned `View` on the UI
  thread (wrap in `MainThread.InvokeOnMainThreadAsync`); return `null` to close quietly.
- Do not bypass `ResolveDeferredContentLoadDelayMs(options)` by wiring `DeferredContentView.LoadDelayMs = options.LoadDelayMs` directly in the helper. The resolver is what guarantees the spinner-to-real-content swap lands **after** `AnimateG9BottomSheet` finishes on full-screen opens; bypassing it puts the heavy `InitializeComponent()` + measure pass back on the rising-animation frame and brings back the "small stop" mid-rise (most visible when the configured `OpenAnimationDurationMs` exceeds `G9LayoutMetrics.DeferredContentLoadDelayMs` — e.g. the app-startup 639 ms vs the 369 ms spinner delay). Partial sheets are deliberately left on `options.LoadDelayMs` so we don't lengthen perceived loading time for half/collapsed/fit-to-content opens; their size-scaled motion already settles well before the spinner times out.
- Do not turn `ReserveEmptyHeaderSlots` off as a general setting just because one specific layout looks off-center — the symmetric Star/Auto/Star layout is what makes a centered title actually look centered across every platform. Disable it only for the sheet that truly needs the legacy collapse behavior.
- Do not animate the backdrop card effect manually or layer extra `BoxView`/`Border` overlays on the page to fake an iOS-style recede — the helper already drives `ContentHost.Scale` / `ContentHost.TranslationY` from the existing `PositionChanged` stream, which stays hardware-accelerated on Android. Adding a parallel animator would just compete with the helper and waste a Choreographer frame per drag tick.
- Do not "simplify" the inverse-cubic progress in `BackdropCardBinding.ApplyForRatio` back to a linear `(visibleRatio − threshold) / (1 − threshold)` mapping. The `1 − (1 − rawProgress)^(1/3)` formula is the algebraic inverse of the sheet's `Easing.CubicOut` motion (see "Why the progress is inverse-cubic, not linear"). A linear progress here is what produces the "small stop near the end" perception on programmatic full-screen opens — the recede otherwise lands 50 % of its travel in ~13 % of post-threshold animation time and crawls through the rest. If `AnimateG9BottomSheet`'s easing is ever changed away from `CubicOut`, update this formula in lockstep (the exponent matches the easing curve).
- Do not gate the backdrop card transform on `sheet.State != Hidden` again in `UpdateOverlayFromPositionEvent`. `G9SheetView.Close()` flips `State` to `Hidden` synchronously before its close animation runs, so bailing on `Hidden` is exactly what caused the original "transform stays for ~`AnimationDurationMs`, then snaps" bug. Only the modal-overlay alpha update is allowed to bail when state is `Hidden` (because `OnPrimarySheetStateChanged` already drives its fade-out).
- Do not make `BackdropCardBinding.ResetTransform` a managed-property-only reset. On Android activity-result paths (confirmed on Doogee S96Pro after returning from the Files picker), MAUI can report `ContentHost.Scale=1` / `TranslationY=0` while a native MAUI container still has a compositor scale/translation, leaving the full-screen map visually shifted down with a black top band after sheets close. Reset both the MAUI properties and the Android native tree under the `PlatformView`; also run the same cleanup through `MainActivity` from the activity content root because the stale transform may live on a full-height `LayoutViewGroup` / `ContentViewGroup` sibling outside the `ContentHost` platform subtree on this ROM.
- Keep the final backdrop reset tied to the sheet's `Hidden` transition, not only to `CleanupSheetVisuals`. `ShouldCleanupClosedSheet` can skip cleanup when a sheet is still parented/handled during close timing, but the visual transform still needs a delayed identity reset after the close animation.
- Do not rely on theme background colors to keep the receded-page edges dark. The `BackdropHost` `BoxView` in `G9PageTemplate.xaml` is the contract; if you change it, also update `G9BottomSheetSettings.BackdropCardColor` so the helper-applied color stays consistent with the template.
- Do not raise `PositionChanged` from `G9SheetView.OnSizeAllocated`. The MAUI layout pass calls `OnSizeAllocated` during page rotation, virtual-keyboard show/hide, and Mac Catalyst window resizes — surfacing a position event on every layout tick would cause the helper's modal-overlay alpha and backdrop card recede to recompute on layout cycles where the user didn't actually drag. The position event is intentionally raised from `AnimateG9BottomSheet`'s tick, `UpdateG9BottomSheetPosition` (drag tick), and `UpdateCollapsedHeight` / `UpdateHalfExpandedRatio` / `UpdateFullExpandedRatio` (helper-driven fit-to-content tween) — those are the only places it should fire.
- Do not re-run `Show()` / `AnimateG9BottomSheet` from `OnStateChangedInternal` when `oldValue == newValue`. The helper's fit-to-content tween writes `State = Collapsed` on every animation frame to refresh the snap target; if this short-circuit is removed each frame would abort and restart the sheet animation and the resize would stutter.
- Do not remove `InputTransparent = true` / `CascadeInputTransparent = false` from the `G9SheetView` constructor. Without these flags the empty area of the sheet host Grid (which fills the entire `OverlayHost`) blocks taps on the page content underneath — the symptom is that text boxes, buttons, and other interactive elements on the page can only be focused via Tab, not tapped.
- Do not remove the `overlay.InputTransparent = sheetIsHidingOrClosed` line in `UpdateModalOverlayBackground`. Without it the helper's modal overlay BoxView keeps consuming taps for the full close-cleanup window (~700 ms) after the sheet visually disappears, making the page feel unresponsive after every sheet close.
- Do not attach MAUI `PanGestureRecognizer` or `SwipeGestureRecognizer` to the sheet body from the helper. The control owns all drag gestures through its per-platform handlers. Adding helper-side recognizers causes double-fire on Windows (visible as a blink every drag tick) and is unreliable on Android (MAUI pan gestures over Border children rarely fire on Android pre-15).
- Do not forward gestures only from `OnInterceptTouchEvent` on Android. When no child inside the body consumes `ACTION_DOWN` (the common case for non-scrollable content), Android delivers the entire gesture stream to `OnTouchEvent` directly, bypassing `OnInterceptTouchEvent` for Move events. Both paths must forward to the sheet — see the `_gestureForwarded` flag pattern in `G9SheetViewBorderPlatformView`.
- Do not reintroduce a `ShowStackedG9BottomSheet` API or branch callers on `GetOpenSheetCount()` themselves. `ShowG9BottomSheet` (and `ShowFullScreenAsync`) detect the open sheet in one place and stack automatically. Callers always use `ShowG9BottomSheet`; to replace the current sheet they use `ReplaceG9BottomSheet` (or close first). A separate stacked API reintroduces the orphan-stacked-sheet foot-gun (a stacked sheet opened with no primary owner has no backdrop and a different cleanup path).
- Do not hand-roll a replace as `CloseTopG9BottomSheet()` + `ShowG9BottomSheet(...)` at call sites — use `ReplaceG9BottomSheet`. The hand-rolled pair always pays the full close/open cycle, skips the in-place morph, skips the quick-close marking, and double-runs the show throttle.
- Do not run the outgoing step's `ClosingCommand`/`ClosedCommand` on the morph path of `ReplaceG9BottomSheet` — a replace is an advance. Callers that need close-time cleanup on flow EXIT put it in the ClosedCommand of the step it belongs to (gated on their `advancing` flag), exactly like the fallback path.
- Do not clamp authoritative fit-to-content heights (placeholder / memo / provider / a real measure of loaded content) up to `FitContentLoadingMinHeight`. That floor is for the unknown-height loading window only; authoritative values clamp to `FitContentAbsoluteMinHeight`. Re-flooring them reintroduces the "every short sheet opens at exactly 180 then resizes" defect.
- Do not add the helper header band back into caller-side height estimates (`G9SelectionSheet.EstimateContentHeight` etc.). Placeholder/provider/memo heights are BODY heights; `ResolveHelperChromeHeight` adds the chrome exactly once. Baking it into a caller doubles it.
- Do not let an `IG9BottomSheetContentHeightProvider` return a hard-coded constant for content whose real height can change (a shared card, a row template). The helper trusts the provider without independently re-measuring, so a drifted constant clips content out of frame with NO animated correction — this is what clipped `SamplingTargetContentView`'s register button when `TreeInfoCardView` grew past the old 126dp estimate. Measure the attached body (`Measure(width, ∞)`) and return that; keep a static number only as the pre-attach cold-open value, aimed slightly low. And subscribe that body's `Loaded` → `RaiseG9BottomSheetHeightChanged()` so the slow-cold-open case (all settle passes run before attach) still gets one animated correction instead of an intermittent few-dp clip.
- Do not ship a deferred sheet body that mutates its visual after construction (async data fill, collection repopulate, scroll-to-selection on `Loaded`, late image source) without either a `LoadableSheetContentView` in-body spinner OR the readiness gate (`GateContentReadiness()` + `MarkContentReady()`). The covered reveal drops its spinner on a fixed timer, so an ungated post-reveal mutation blinks. See "Post-reveal mutation → the blink". When editing the gate on a base class, do a CLEAN Android build — stale incremental output silently disables the base method.
- Do not make post-open fit resizes instant again (`shouldAnimate` must keep the `sheet.IsOpen` arm). The instant path is reserved for pre-open passes; an instant post-open apply is exactly the visible "pop" the 2026-07 instrumentation caught on all 18 fit sheets.
- Do not remove the `OpenMotionCompleted` wiring from `RunOpenedCommandLater` and fall back to the fixed timer alone. The timer is the non-scaled worst case; on partial-height sheets it starts deferred loads ~300 ms late, which pushes the content's real height past the scheduled settle passes and makes the correction land visibly late.
- Do not replace the `G9Shimmer` band with a MAUI `Animation`/timer/SkiaSharp-invalidation shimmer. Only render-thread mechanisms (Android AVD, iOS Core Animation) keep animating while the UI thread builds the content the placeholder masks — a UI-thread shimmer freezes at exactly the wrong moment (see `G9Shimmer.md`).
- Do not add reopen-previous `ClosedCommand`s to stacked step flows, and do not close only the TOP sheet on a commit path. Stacked chaining's contract is: back closes the top step (the receded parent restores itself), commit closes the whole stack (`CloseG9BottomSheet()`). A reopen-ClosedCommand duplicates the parent; a top-only commit close resurrects the historical stale-sheet bug.
- Do not restore a receded parent with an animation from whole-stack teardown paths (`CloseAllStackedSheets` drops the recede links deliberately) — a fade-in on a sheet that is itself closing reads as a ghost flash.
- Do not put a show/hide loading or refresh indicator INSIDE a fit-to-content sheet body's layout flow. Post-open resizes animate, so an inline spinner appearing for a fast background sync reads as the sheet "jumping larger then back" (the StateTransition sheet's inline `RefreshSpinner` bug). Overlay such indicators layout-neutrally (grid overlay, `InputTransparent`, `VerticalOptions="Start"`) so toggling them contributes zero height. Width-only toggles inside a horizontal row (the TaskManagement chip row) are fine.
- Do not re-parent a sheet body after it has been revealed (and do not "flatten" the covered-reveal host). Detaching + re-attaching a MAUI view tree on Android destroys and recreates its native views — a visible full re-render that re-runs the icon-font race. One attach per body, ever; reveals remove only the placeholder.
- Do not remove the `IsApplyingFitContent` guard around `ApplyFitToContentHeight`'s body. The fit-to-content live-resize tracker (`AttachFitToContentSizeTracking`) listens to the content's `MeasureInvalidated`; the apply ends by calling `InvalidateMeasure`, so without the guard the apply's own invalidations re-enter the tracker and it loops forever at the debounce interval. Genuine content growth still fires `MeasureInvalidated` after the apply returns (flag back to false).
- Do not drop the `MeasureInvalidated` tracking and rely only on the one-shot measures (initial + `DeferredContentView.ContentLoaded`). Heavy / async-populated fit-to-content bodies (block-info, pot/tree, attachment thumbnails) report a too-small natural height on the first frame and grow later; the one-shot measures miss that growth and the sheet stays stuck small. Always measure with `Measure(width, double.PositiveInfinity)` so the natural height is read even while the body clips it at the current `CollapsedHeight`.
