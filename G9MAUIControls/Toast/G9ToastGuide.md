# Toast Guide

This app ships its own toast / loading / progress overlay implementation under `Common/Components/Toast/` and exposes it through `G9ToastHelper` and `SyncProgressOverlayHelper`. Callers should not use the former third-party library (now removed)'s `SfG9Popup` for transient notifications, navigation-pushed modal pages for blocking loaders, or any third-party toast library. Every overlay is mounted as an inline view into the page-level layout — no popup queue, no extra `Page.Navigation` push, no vendor-specific control.

## Why We Own The Toast Source

- We control mount/unmount semantics directly. Toasts attach to the dedicated `ToastHost` grid in `G9PageTemplate.xaml`, which sits **above** the popup + bottom-sheet `OverlayHost` in document order. That guarantees the app-wide z-stack contract — see "Z-Stack Contract" below — without any per-toast `ZIndex` math.
- The fade + slide animations run on `View.FadeToAsync` / `View.TranslateToAsync` (MAUI 10 APIs) at compositor framerate without burning layout time.
- AOT-safe: no reflection, no private-field reads, no unsafe access checks. The state machine is plain field updates and `MainThread.InvokeOnMainThreadAsync` calls.
- No `SfG9Popup` dependency. The full-screen blocking loader is a hand-rolled `Grid` (scrim) + `Border` (card) overlay; the toast itself is a `Border` + `Grid` content with optional action button. The the former third-party library (now removed) package is referenced only for `G9ActivityIndicator` (the spinner inside the loading visuals) and unrelated controls.

## File Structure

```
Common/Components/Toast/
├── ToastGuide.md
├── G9ToastHelper.cs                        // app-level helper (in Modal namespace; see below)
├── G9ToastExtensions.cs                    // VisualElement.ShowToastAsync(...) fluent API
├── G9ToastOptions.cs                       // per-call toast configuration
├── G9ToastPosition.cs                      // 9-position grid (Top/Middle/Bottom × Left/Center/Right)
├── G9ToastType.cs                          // Information / Success / Warning / Error
├── G9InlineToastHandle.cs                  // internal mount/unmount tracker
├── SyncProgressOverlayHelper.cs          // sync-progress overlay (success/failure terminal states + retry)
└── Views/
    ├── SyncProgressToastView.xaml        // expanded + compact + terminal states
    └── SyncProgressToastView.xaml.cs     // codebehind for the sync overlay view
```

Every file's **namespace is `G9MAUIControls.Popup`** so existing callers' `using G9MAUIControls.Popup;` keeps resolving without a code change. This decouples folder layout from namespace layout — the folder reflects ownership ("everything toast-related lives here") while the namespace reflects the stable public API surface (`Components.Modal` was where `G9ToastHelper` lived before the migration, same pattern we already use for `Components.CustomizedG9Popup`).

The neighboring `Components/Modal/` folder is now toast-free and contains only `ModalHostRegistry.cs` — a cross-cutting registry that tracks the active page's popup AND bottom sheet (used by both `G9PopupHelper` and `G9BottomSheetHelper` to find the correct host for the current page).

## Goals

- One shared toast / loading / progress abstraction for Android, iOS, Mac Catalyst, and Windows.
- Same app-level behavior on desktop and mobile-sized windows.
- Support typed toasts (Information / Success / Warning / Error) with auto-dismiss, optional action button, and 9 screen positions.
- Support full-screen blocking loader, compact loading toast, and progress toast with fill-to-percent animation.
- Stable API surface — `using G9MAUIControls.Popup;` keeps resolving the helper unchanged.

## Toast API

```csharp
// Typed toasts (Information / Success / Warning / Error)
await G9ToastHelper.ShowToastAsync("Saved successfully", G9ToastType.Success);
await G9ToastHelper.ShowToastAsync(
    message,
    G9ToastType.Warning,
    new G9ToastOptions
    {
        Position = G9ToastPosition.TopCenter,
        DurationMs = 6000,
        ActionText = "Undo",
        Action = () => undoAsync(),
        Icon = MaterialIcons.History
    });

// Or via the fluent extension on any VisualElement
await this.ShowToastAsync("Quick info");

// Manual dismiss (rare — toasts auto-dismiss)
await G9ToastHelper.DismissToastAsync();
```

`G9ToastOptions` defaults:

| Field         | Default                                   |
| ------------- | ----------------------------------------- |
| `Position`    | `BottomRight` (LTR) / `BottomLeft` (RTL)  |
| `DurationMs`  | `3000`                                    |
| `Icon`        | type-default (CheckCircle / WarningAmber / ErrorOutline / Info) |
| `ActionText`  | `null` — no action button                 |
| `Action`      | `null`                                    |

## Toast Stacking

When multiple toasts target the same `(Parent, Position)`, they stack with `ToastStackGap` (8 dp) between them. `ReflowToastStackAsync` repositions the stack on every show / dismiss / size change so the stack stays visually clean. Bottom-anchored stacks honor any active `SyncProgressToastView` height so a sync toast sitting at the bottom doesn't get covered by a regular toast that spawns later — the regular toast lifts above the sync overlay and the sync overlay stays anchored.

## Loading API

Three loading variants, each with a clear semantic:

### Full-screen blocking loader

Use when the user must wait for a critical operation (e.g. signing in, syncing critical data).

```csharp
await G9ToastHelper.ShowLoadingAsync("Signing in...");
try
{
    await DoSomethingBlockingAsync();
}
finally
{
    await G9ToastHelper.DismissLoadingAsync();
}
```

Visuals: a scrim covers the whole page (theme.Scrim @ 55% alpha). A centered card with `theme.InverseSurface` background, 14 dp corner radius, and an outline stroke (flat — no shadow; see `../G9/G9Controls.md` §0) shows a `G9ActivityIndicator` (CircularMaterial) above a centered label. The scrim swallows every tap so the user cannot interact with anything underneath until the loader is dismissed. Fade-in is 200 ms, fade-out is 180 ms.

### Compact loading toast

Use when a non-critical background operation is happening but the user can keep using the page.

```csharp
await G9ToastHelper.ShowLoadingToastAsync("Refreshing...");
// ...
await G9ToastHelper.DismissLoadingToastAsync();
```

Visuals: a small horizontal `Border` with the spinner + label. Mounts at the configured `G9ToastPosition` (default = bottom corner per RTL). No scrim — page interaction continues. Does NOT auto-dismiss — the caller must call `DismissLoadingToastAsync`.

### Progress toast

Use when a long-running operation has measurable progress (file upload, large sync). The card animates a `Primary @ 34% alpha` fill from 0 → progress with `ScaleX` and shows a `0%–100%` pill.

```csharp
await G9ToastHelper.ShowProgressToastAsync(
    title: "Syncing samples...",
    detail: "12 of 250",
    progress: 0d);

// Update as the operation progresses
await G9ToastHelper.UpdateProgressToastAsync(
    title: "Syncing samples...",
    detail: "180 of 250",
    progress: 0.72d);

// When done
await G9ToastHelper.DismissProgressToastAsync();
```

The fill layer's `AnchorX` is `0` for LTR and `1` for RTL so the bar grows left-to-right or right-to-left as appropriate.

## Sync Progress Overlay

A specialized overlay for sync operations that manages its own success / failure terminal states and a retry button. Anchored at top or bottom and listens for `SyncProgressMessage` from `WeakReferenceMessenger` to update its progress without the caller having to call into it.

### No drop shadow on the sync overlay (2026-07-28)

The sync overlay is flat, like every other toast. A SkiaSharp `ToastShadowView` was added here and
reverted the same day, and the reason is worth keeping so nobody retries it blindly:

- A Skia shadow layer has to be **larger** than the card it sits under, or the blur has nowhere to
  render. Android clips negative-margin overflow (the bug that killed three sides of the tab bar's
  halo — see `../Menu/G9TabBarShadowView`), so the only alternative was an inner `Margin` on the
  card.
- That inner margin shrank the sync card off the width it **shares with the message toasts and the
  bottom tab bar**. `G9ToastHelper.ApplyInlineG9ToastPosition` sets every toast to
  `HorizontalOptions = Fill` with a 16 dp horizontal margin; the sync overlay must line up with
  that exactly.
- A first attempt also let the `SKCanvasView` — which has no intrinsic size — drive the row height,
  because it reports the full offered space when measured greedily. The overlay grew to fill most
  of the screen.

Separation comes from `Stroke` + the card fill, consistent with `../G9/G9Controls.md` §0.
If a shadow is ever genuinely required here, solve the bleed problem first: the card's own geometry
must not change.

## Animation Timings

| Animation                       | Duration  | Easing     |
| ------------------------------- | --------- | ---------- |
| Toast enter (fade + slide)      | 250 ms    | `SinOut`   |
| Toast exit (fade + slide)       | 200 ms    | `SinIn`    |
| Loading scrim fade-in           | 200 ms    | `SinOut`   |
| Loading scrim fade-out          | 180 ms    | `SinIn`    |
| Toast stack reflow              | 250 ms    | `SinOut`   |
| Sync overlay terminal countdown | 2.2 / 6.5 s | linear   |

## Host Resolution

Every toast / loading / progress / sync overlay anchors on `ToastHost`, the dedicated grid in `G9PageTemplate.xaml`. That grid is the last child of `RootHost`, so it paints above `OverlayHost` (popup + bottom sheet) by document order. `ResolveHostContext()` walks two paths:

1. `G9ModalHostRegistry.TryGetCurrentHost(out host)` — the active `G9PageBase` registered by `OnApplyTemplate`. Toasts mount on `host.ToastHost`, which outlives every sheet, popup, and page-content swap (it's part of the control template, not page content). A toast started inside a sheet keeps showing after the sheet closes AND paints above any popup that opens over it.
2. Fallback: `Application.Current.Windows[].Page` walked through the modal stack, ending at the visible page's `Content` layout. Only reachable during the brief startup window before `OnApplyTemplate` runs — no popup or sheet is open at that point.

## Z-Stack Contract

The app-wide overlay stack — defined in `G9PageTemplate.xaml`, bottom-to-top — is:

```
1. BackdropHost   (sheet recede color)
2. ContentHost    (MainPage.xaml: page content + tab bar + InitializeOverlaySlot)
3. OverlayHost    (G9SheetView + dynamic sheets + modal scrims appended by G9BottomSheetHelper)
4. G9PopupHost      (G9PopupView ONLY — dedicated layer above every sheet)
5. ToastHost      (every toast / loader / progress / sync overlay)
```

Sibling order in `RootHost` IS the z-order. `G9PopupHost` and `ToastHost` also carry explicit `ZIndex` (2 and 3) as defense in depth. A toast mounted in `ToastHost` automatically wins against everything in `G9PopupHost` and `OverlayHost`. This is what makes the "open a toast inside a bottom sheet, then close the sheet — toast keeps showing" scenario work without any per-toast tracking.

`InitializeOverlaySlot` in `MainPage.xaml` sets `ZIndex = int.MaxValue` so it covers every other child of `MainPageRoot` (page content + tab bar) during startup. G9Popups, sheets, and toasts cannot fire while it's visible — startup logic gates the tab queue and tab-bar interactions.

Single-page model: the registry is the primary path. The fallback only fires during startup before the page's `OnApplyTemplate` runs.

## Cross-Cutting With ModalHostRegistry

`Helpers/G9ModalHostRegistry.cs` is the only file remaining in the legacy `Modal` folder. It tracks the active page's popup AND bottom sheet for both `G9PopupHelper` and `G9BottomSheetHelper`. Toast resolution piggy-backs on it because every visible `G9PageBase` is already registered there — re-using the registry avoids a parallel "active page" tracker just for toasts.

## Test Page Coverage

`TempDesignPageIman` contains coverage under the **Toast** tab, organized in three groups:

### Type presets (4 buttons)

- **Information toast (default)** — Information G9ToastType, default position, 3 s auto-dismiss.
- **Success toast (top-center, 5s)** — Success G9ToastType, TopCenter position, 5 s auto-dismiss.
- **Warning + action button (Undo)** — Warning G9ToastType, ActionText "Undo" with callback, default 3 s.
- **Error toast (sticky 8s)** — Error G9ToastType, 8 s auto-dismiss for important error visibility.

### Loaders (3 buttons)

- **Full-screen loader (3s simulated)** — `ShowLoadingAsync` + delayed `DismissLoadingAsync`. Verifies the scrim blocks input.
- **Compact loading toast (manual dismiss)** — `ShowLoadingToastAsync` then `DismissLoadingToastAsync`. Verifies non-blocking behavior.
- **Progress toast (0% → 100% over 3s)** — `ShowProgressToastAsync` + repeated `UpdateProgressToastAsync` then `DismissProgressToastAsync`. Verifies fill animation + LTR/RTL anchor.

### Stacking + dismissal (2 buttons)

- **Stack 4 toasts at bottom-right** — Fires 4 toasts back-to-back to verify `ReflowToastStackAsync` lifts each successive toast 8 dp above the previous one.
- **Dismiss all overlays** — Calls `G9ToastHelper.DismissAllAsync()` to clear every active toast / loader / loading-toast / progress-toast in one shot.

Run the whole Toast tab after changing `G9ToastHelper`, `G9ToastOptions`, the inline toast/loading visuals, the toast stack reflow logic, or `SyncProgressOverlayHelper`. Toggle LTR/RTL and Light/Dark while toasts are stacking to verify the stack offset direction, the fill-layer anchor, and the type-default colors all stay correct.

## Do Not Regress

- Do not reintroduce a the former third-party library (now removed) `SfG9Popup`-backed loading / toast path. The hand-rolled inline overlays are the contract; if you need a feature that's missing (e.g. a different blur intensity for the scrim), add it to the `Build*` methods rather than swapping the implementation back to a popup.
- Do not move toast files out of `Components.Modal` namespace. They physically live in the `CustomizedToast` folder, but the **namespace must stay `G9MAUIControls.Popup`** so existing callers' `using` directives keep resolving without a code change. Same contract as `Components.CustomizedG9Popup`.
- Do not mount toasts on `host.OverlayHost`, `host.Page.Content`, or any page-level layout. The dedicated `host.ToastHost` grid in `G9PageTemplate` is the contract — it sits above `OverlayHost` so toasts paint above any popup / sheet, AND it outlives every sheet / popup / page-content swap. Mounting elsewhere either breaks the z-stack (toast painted under the sheet) or breaks survival (toast vanishes when the sheet closes).
- Do not call `MainThread.InvokeOnMainThreadAsync` inside the toast builders (`BuildInlineToastView`, `BuildInlineLoadingToastView`, `BuildInlineProgressToastView`). The public `Show*` methods already wrap their work in `MainThread.InvokeOnMainThreadAsync`; nesting a second wrap is redundant and adds a frame of latency to the open animation.
- Do not call `View.FadeTo` / `View.TranslateTo` directly. They are obsoleted in MAUI 10. Use `View.FadeToAsync` / `View.TranslateToAsync` so we don't accumulate `CS0618` warnings; the new APIs return `Task<bool>` that we already await.
- Do not add a public `AnimationType` parameter back to `ShowLoadingAsync` / `ShowLoadingToastAsync`. The internal `CircularMaterial` default is what every caller in the app uses; surfacing the parameter pushes the the former third-party library (now removed) `AnimationType` enum onto every consumer's `using` list and provides no real value.
- Do not bypass `ResolveHostContext()` by reaching directly into `Application.Current.Windows[0].Page.Content`. The registry path is what makes toast routing predictable across modal pushes and bottom sheets — the windows-walk path is a startup-only fallback.
- Do not mutate `_activeToasts` outside of `MainThread`. The list and its handles are not thread-safe; the helper relies on every public entry point being wrapped in `MainThread.InvokeOnMainThreadAsync` and every internal access happening on the UI thread.
- Do not race `ShowProgressToastAsync` with another `ShowProgressToastAsync`. Only one progress toast can be active — the second call calls `DismissProgressToast()` first and replaces the active state. If you need two concurrent progress overlays, file a feature request rather than working around it; the current single-progress contract is what makes `UpdateProgressToastAsync` safe to call from anywhere.
- Do not anchor toasts on `ContentPage.Content` directly without going through `ResolveHostContext()`. Custom resolution paths bypass the modal-stack walk and break toast visibility on pages pushed into a sheet body.

### Interaction model + cancel (redesigned)

`SyncProgressToastView` runs ONE state machine — `Running`, `Canceling`, `Success`, `Error` — plus an orthogonal `IsMinimized` flag (full detail in the view's class doc-comment and `AiGuides/03-Sync-Dotmim.md` → "Sync Progress UI"):

- **No minimize button.** While **Running**, tap the card BODY to minimize to a 72×72 bubble; tap the bubble to restore. The bubble is **draggable** anywhere on screen (the whole view is translated and clamped to the host); a small-travel pan counts as a tap, not a drag.
- **Cancel (X)** is a 44×44 target. Tapping it switches instantly to **Canceling…** (frozen bar, spinner kept, X hidden), cancels the run, then closes the overlay completely and shows a single neutral **"Sync canceled"** toast — never an error terminal. A 5 s watchdog shows a "finishing the current step" notice if a non-interruptible step is still unwinding.
- Success/Error terminals always force the full card (a failure that arrives while minimized auto-expands so it can't be missed). Body taps are ignored outside the Running state.
- Every gesture/animation handler is exception-safe (`SafeRun` / guarded `async void`): a fault in this overlay must never crash the UI loop, because it is on screen during the most failure-prone moments.

Do NOT reintroduce a minimize button, route a cancel through an error terminal, or keep showing progress for a cancelled run.
