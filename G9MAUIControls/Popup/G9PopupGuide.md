# G9Popup Guide

This app ships its own popup implementation under `Common/Components/G9Popup/G9PopupView/` and exposes it through `G9PopupHelper`. Callers should not use the former third-party library (now removed)'s `SfG9Popup` (or any vendored variant), navigation-pushed modal pages, or `Microsoft.Maui.Controls.G9Popup` for popup-style UI. The popup control is mounted into the shared `OverlayHost` in `G9PageTemplate.xaml` once per page; every `G9PageBase` instance is already wired through `ModalHostRegistry`.

## Why We Own The G9Popup Source

Earlier versions of the app relied on the former third-party library (now removed)'s `SfG9Popup` from `the former third-party library (now removed).Maui.G9Popup`. The control is now hand-rolled with public MAUI primitives only (`Grid`, `Border`, `BoxView`, `RoundRectangle`, `Microsoft.Maui.Controls.Animation`) so:

- We control the open / close animation pipeline directly. The compound `Animation` runs opacity + scale (or opacity + translation) on a single animator so the two channels stay in lockstep — no fighting if the user dismisses mid-open.
- The control is hit-test-transparent when closed (`InputTransparent = true`, `CascadeInputTransparent = false`) so page content underneath stays interactive without any helper-side input gating. When opened, the overlay `BoxView` is the only thing blocking taps.
- There is no the former third-party library (now removed) internal-namespace dependency. The popup never reaches into `the former third-party library (now removed).Maui.G9Popup` or any other restricted namespace; the the former third-party library (now removed) package is referenced only for unrelated controls (charts, pickers, busy indicators, text input layouts).
- AOT-safe: no `Reflection.Emit`, no private-field reads, no unsafe access checks. All state-change work runs through `BindableProperty.PropertyChanged` callbacks and direct property writes.
- Performance: animations write `Opacity`, `Scale`, `TranslationY` directly. On every supported MAUI target those properties map to native compositor transforms (Android `RenderNode`, iOS `CALayer`, WinUI `CompositionTransform`), so the open animation runs at compositor framerate without burning layout time.

## File Structure

```
Common/Components/G9Popup/
├── G9PopupGuide.md
├── G9PopupHelper.cs                        // app-level helper (in Modal namespace; see below)
├── G9PopupExtensions.cs                    // VisualElement.ShowG9PopupAsync(...) fluent API
├── G9PopupSettings.cs                      // per-popup configuration overrides
├── G9PopupDescriptor.cs                    // internal show-request descriptor
├── G9PopupVisualProfile.cs                 // per-type icon + accent color resolver
├── G9PopupResult.cs / G9PopupResultAction.cs // what happens after a button callback returns
├── G9PopupButton.cs                        // footer button definition
├── G9PopupAnimationType.cs                 // None / FadeIn / ZoomIn / SlideUp / DropIn / Bounce
├── G9PopupFooterButtonLayout.cs           // Row (equal columns, default) / Stacked (one per row)
├── G9PopupType.cs                          // Information / Success / Warning / Error / Custom
├── G9PopupInputOptions.cs                  // input form configuration
├── G9PopupInputField.cs / G9PopupInputFieldType.cs
├── G9PopupInputOption.cs                   // checkbox / radio item
├── G9PopupInputResult.cs                   // submit / cancel / validation-failed result
└── G9PopupView/
    ├── G9PopupView.cs                       // cross-platform popup control
    ├── G9PopupViewEnums.cs                  // AutoSizeMode / OverlayMode / AnimationEasing / RelativePosition / BlurIntensity
    └── G9PopupViewOpenOptions.cs            // per-open visual options record
```

The `G9PopupView/` folder holds the cross-platform popup control plus its supporting enums and per-open options record. Every other file in the `CustomizedG9Popup` folder is part of the **public API** the helper exposes to callers (`G9PopupHelper`, `G9PopupSettings`, `G9PopupButton`, `G9PopupAnimationType`, the input form types, etc.). Their **namespace is `G9MAUIControls.Popup`** so existing callers' `using G9MAUIControls.Popup;` keeps resolving without a code change. This decouples folder layout from namespace layout — the folder reflects ownership ("everything popup-related lives here") while the namespace reflects the stable public API surface (`Components.Modal` was where `G9PopupHelper` lived before the migration).

The neighboring `Components/Modal/` folder is now popup-free and contains only toast / loading / progress-toast / sync-progress overlay code plus the cross-cutting `ModalHostRegistry` (which tracks both the popup AND the bottom-sheet registered for the active page).

## Control Layout (Visual Tree)

```
G9PopupView : Grid  [InputTransparent=true, CascadeInputTransparent=false, IsVisible=false]
├── _overlay : BoxView  [InputTransparent=false when open — blocks taps; tap raises BackgroundTapped]
└── _cardHost : Grid    [InputTransparent=true, CascadeInputTransparent=false]
    └── _cardFrame : Border  [InputTransparent=false — captures card taps so they don't fall through]
        └── caller content (header + body + footer Grid built by G9PopupHelper)
```

When closed, the host `Grid` is fully transparent to input — every tap falls through to the page content underneath. When `Open()` is called, the host flips `InputTransparent = false` and `IsVisible = true`; the overlay `BoxView` then captures every tap that misses the card. The card itself is `InputTransparent = false` so its own children (input fields, footer buttons) keep working. On `CloseAsync()` the host flips back to `InputTransparent = true` immediately after the close animation finishes — taps fall through again with no dead-zone.

## Z-Stack Position

`G9PopupView` lives inside its own dedicated `G9PopupHost` grid in `G9PageTemplate.xaml`, a later sibling of `OverlayHost`. That structure guarantees the popup paints above any open bottom sheet AND any dynamic modal scrim that `G9BottomSheetHelper` appends to `OverlayHost.Children` at runtime — without needing per-call `ZIndex` math. Above `G9PopupHost` sits `ToastHost` (toasts / loaders / progress) so a toast raised over an open popup paints on top of the popup card. Below `OverlayHost` sits `ContentHost` (page content, tab bar, startup overlay). Full bottom-to-top order:

```
RootHost
├── BackdropHost      (sheet recede color)
├── ContentHost       (page content + tab bar + InitializeOverlaySlot)
├── OverlayHost       (G9SheetView + dynamic sheets + modal scrims)
├── G9PopupHost         (G9PopupView — this control)
└── ToastHost         (every toast / loader / progress visual)
```

`G9PopupHost` and `ToastHost` also carry explicit `ZIndex` (2 and 3) as defense in depth so future template edits can't accidentally regress the contract. See `G9PageTemplate.xaml` for the canonical structure.

## Goals

- One shared popup abstraction for Android, iOS, Mac Catalyst, and Windows.
- Same app-level behavior on desktop and mobile-sized windows.
- Support every popup type used by the app: Information, Success, Warning, Error, Custom view, Input form, Confirm.
- Support 1, 2, or 3 footer buttons with the secondary-outline + primary-solid styling from `G9DesignSystem.html`.
- Support all six animation kinds (None, FadeIn, ZoomIn, SlideUp, DropIn, Bounce) on every platform with no conditional compilation.
- Stable API surface — `using G9MAUIControls.Popup;` keeps resolving the helper unchanged.

## Main APIs

Existing callers do not need any code changes.

```csharp
// Information / Success / Warning / Error preset popups
await G9PopupHelper.ShowG9PopupAsync("Message", "Title");
await G9PopupHelper.ShowSuccessG9PopupAsync("Saved!");
await G9PopupHelper.ShowWarningG9PopupAsync(message, G9StringResources.Warning, buttons);
await G9PopupHelper.ShowErrorG9PopupAsync(message, G9StringResources.Error);

// Custom-view popup: arbitrary MAUI View mounts inside the body slot
await G9PopupHelper.ShowCustomG9PopupAsync(myView, "Custom title");

// Input form: returns values + per-field validation errors
var result = await G9PopupHelper.ShowInputG9PopupAsync(new G9PopupInputOptions
{
    Title = "Quick input",
    Fields =
    [
        G9PopupInputField.Text(key: "name", label: "Name", isRequired: true),
        G9PopupInputField.Email(key: "email", label: "Email", isRequired: true)
    ]
});

// Confirm: returns bool. Wraps OK/Cancel + tcs in one call.
// Cancel is the OUTLINE button and only OK carries the accent (design guide §4c) — do not pass
// IsPrimary-style overrides to "restore" two solid buttons.
var ok = await G9PopupHelper.ShowConfirmAsync("Continue?", "Confirm");

// Confirming something the user would MISS (sign out, discard work) → Warning accent + icon.
// Default is Information; pass the type rather than hand-rolling buttons.
var signOut = await G9PopupHelper.ShowConfirmAsync(
    message, title, okCallback, type: G9PopupType.Warning);

// Configuration / cleanup
G9PopupHelper.ConfigureG9PopupDefaults(new G9PopupSettings { CloseOnBackgroundClick = true });
await G9PopupHelper.ClearG9PopupQueueAsync();
await G9PopupHelper.DismissAllG9PopupsAsync();
```

## App Startup Defaults

Configure global popup defaults once via `G9PopupHelper.ConfigureG9PopupDefaults(...)`. Per-call `G9PopupSettings` always wins — the defaults are merged in via `G9PopupSettings.WithDefaults(defaults)`.

```csharp
G9PopupHelper.ConfigureG9PopupDefaults(new G9PopupSettings
{
    AnimationDuration = 240,
    Animation = G9PopupAnimationType.SlideUp,
    AnimationEasing = G9PopupViewAnimationEasing.SinOut,
    OverlayMode = G9PopupViewOverlayMode.Transparent,
    OverlayOpacity = 0.45f,
    AutoSizeMode = G9PopupViewAutoSizeMode.Height,
    Padding = new Thickness(20, 16, 20, 12),
    CornerRadius = 16
});
```

Default centered popups use 90% of the popup host width and auto-size height to their
content.

**G9Popups cast no shadow (2026-07-28).** `G9PopupSettings.Shadow`, `G9PopupViewOpenOptions.Shadow` and
`G9PopupVisualProfile.Shadow` were removed together with `G9PopupView`'s three-layer "centered shadow"
(`_cardShadowHost` + `CreateShadowLayer` + `ApplyCenteredShadow`), which faked an even halo with
three nested translucent `Border`s because Android's native elevation has a directional
light-source bias. That workaround also added three full-size views to every popup's layout tree.
The card now separates from the scrim with its `Stroke` + `StrokeShape` and the overlay dim alone —
see `../G9/G9Controls.md` §0 for the app-wide rule and why.

## Animations

Every animation is built on top of MAUI's public `Animation` type so opacity + scale (or opacity + translation) run on the same animator. The card and the overlay use independent animators (different animation names), so the overlay alpha always fades smoothly even if the card animation is set to `None`.

| Animation kind | Open behavior | Close behavior |
| --- | --- | --- |
| `None`   | Snaps to opacity 1, scale 1, translation 0 instantly. Overlay still fades. | Snaps to opacity 0 instantly. Overlay still fades. |
| `FadeIn` | Opacity 0 → 1.                                                              | Opacity → 0. |
| `ZoomIn` | Opacity 0 → 1 + Scale 0.92 → 1 (compound).                           | Opacity → 0 + Scale → 0.92 (compound). |
| `SlideUp` (default) | Opacity 0 → 1 + TranslationY 40 → 0 (compound).                            | Opacity → 0 + TranslationY → 40 (compound). |
| `DropIn` | Opacity 0 → 1 + TranslationY −40 → 0 (compound).                            | Opacity → 0 + TranslationY → −40 (compound). |
| `Bounce` | Opacity 0 → 1 + Scale 0.6 → 1 with `Easing.BounceOut`.                      | Opacity → 0 + Scale → 0.92 (close mirrors ZoomIn for visual continuity). |

The compound animation construction in `G9PopupView.AnimateSimultaneous` adds two child animations under one parent — both run on the same `Animate(...)` call so they share clock and length. When `CloseAsync()` is invoked mid-open, `AbortRunningAnimations()` cancels the in-flight animator first; the close animator then starts from whatever values the abort left behind, so closes always look correct regardless of timing.

### Easing

`G9PopupViewAnimationEasing` maps to a public `Easing`:

| Easing value | MAUI `Easing` |
| --- | --- |
| `Linear`    | `Easing.Linear` |
| `SinIn`     | `Easing.SinIn` |
| `SinOut` (default) | `Easing.SinOut` |
| `SinInOut`  | `Easing.SinInOut` |
| `CubicOut`  | `Easing.CubicOut` |
| `BounceOut` | `Easing.BounceOut` |

`Bounce` always uses `Easing.BounceOut` for its open phase regardless of the configured `AnimationEasing` (the bounce IS the easing). All other animations honor the per-popup `AnimationEasing`, falling back to the global default.

### Auto-Close Timer

`AutoCloseDuration > 0` schedules a `Task.Delay(...)` from the `Open()` method using a private `CancellationTokenSource`. `CloseAsync()` always cancels the timer before running, so a user-driven close never collides with the auto-close. The timer also runs through `MainThread` to keep the close path identical between auto and manual.

## API Surface (Helper)

`G9PopupHelper` is a static class in the `Components.Modal` namespace. The helper queue is single-threaded — only one popup is visible at a time, additional `ShowG9PopupAsync` calls enqueue.

```csharp
public static class G9PopupHelper
{
    // Defaults
    public static void ConfigureG9PopupDefaults(G9PopupSettings settings);

    // Type-preset popups (build header icon + accent color from G9PopupVisualProfile)
    public static Task<G9PopupResult> ShowG9PopupAsync(string message, ...);            // Information
    public static Task<G9PopupResult> ShowSuccessG9PopupAsync(string message, ...);
    public static Task<G9PopupResult> ShowWarningG9PopupAsync(string message, ...);
    public static Task<G9PopupResult> ShowErrorG9PopupAsync(string message, ...);

    // Custom view in the body slot (header + footer still come from preset)
    public static Task<G9PopupResult> ShowCustomG9PopupAsync(View view, ...);

    // Input form with validation
    public static Task<G9PopupInputResult> ShowInputG9PopupAsync(G9PopupInputOptions options);

    // OK/Cancel that returns bool. Cancel = outline, OK = the type's accent.
    // `type` defaults to Information; pass Warning for a lossy confirm (sign out, discard).
    public static Task<bool> ShowConfirmAsync(
        string message, string? title = null,
        Func<CancellationToken, Task>? okCallback = null,
        Func<CancellationToken, Task>? cancelCallback = null,
        G9PopupType type = G9PopupType.Information);

    // Cleanup
    public static Task ClearG9PopupQueueAsync();
    public static Task DismissAllG9PopupsAsync();
}
```

`G9PopupResult.Action` controls what happens after a button callback returns:

- `Close` — closes the popup. The next queued popup (if any) opens after the close animation finishes.
- `DoNothing` — keeps the popup open. Used by the input popup's submit button when validation fails so the user can fix the field instead of getting their values discarded.
- `ShowNext` — closes the current popup and opens `result.NextG9Popup` immediately. Useful for guided flows.

`G9PopupResult.AfterCloseAsync` runs after the close animation finishes (and after the next popup, if any, is enqueued). Used for navigation / cleanup that should not race the open animation of the next popup.

## Per-Type Visuals (`G9PopupVisualProfile`)

`G9PopupVisualProfile.Create(descriptor, settings)` resolves the header icon, accent colors, message color, and animation defaults for each `G9PopupType`. The defaults match `G9DesignSystem.html` G9PopupView section:

| Type | Header icon | Accent color | Use-case |
| --- | --- | --- | --- |
| `Information` (default)  | `MaterialIcons.Info`         | `theme.Info`    | Neutral message |
| `Success` | `MaterialIcons.CheckCircle`  | `theme.Success` | Confirmed action |
| `Warning` | `MaterialIcons.WarningAmber` | `theme.Warning` | Recoverable issue |
| `Error`   | `MaterialIcons.ErrorOutline` | `theme.Error`   | Hard failure |
| `Custom`  | `MaterialIcons.Info`         | `theme.Info`    | Custom-view popup; header icon can be overridden via `Settings.IconOverride` |

The accent is what makes a popup READ as its type — it paints the header icon, the icon badge tint, the title, and the filled primary footer button. **Every type takes its own semantic token; none of them takes `Primary`.** Mapping `Warning` (and `Information`) onto `Primary` is what once made every popup in the app look like the same green "all good" alert no matter what it said, and it is the single most likely thing to creep back in.

The filled primary button's foreground is the accent's matching `On*` token (`OnWarning`, `OnError`, …), not a hard-coded white: in the dark palette `Warning` / `Success` / `Error` are light tints whose `On*` partner is near-black, and white-on-amber is unreadable there.

**Toast or popup? A failure the user ASKED for is a popup.** A toast auto-dismisses and is easy to
miss, so it fits background/incidental news (a sync that ran on its own, a value saved). The moment
the user pressed a button and is waiting for the answer — "send my pending items", "submit", "delete"
— a failure must be acknowledged, so it opens an error popup. `SyncService.ShowSyncFailureG9PopupAsync`
is the sync-side entry point (structured validation / package-conflict payloads keep their own richer
popups); `SyncFlushFailureReport` builds the dashboard's flush message. A field user reported the
toast version of this as "why did the error flash past, and why can't I read it?" — both halves are
the rule below.

**Write the message in the user's vocabulary, not the system's.** A sync scope name
(`SAMPLING_DETAIL_V2`), a table name, a raw GUID or an exception type in a user-facing popup is a
defect, not detail. Say WHAT data (localized label: "Sampling"), WHICH one (a short code the user can
quote to support — not the full GUID), HOW MANY items, WHY in one sentence, and — for anything that
failed to upload — that the data is still on the device. Keep the technical form in the
`OperationTrace` / logs, where support actually reads it.

**Every failure of one operation goes in ONE popup.** Do not queue a popup per problem: the flush
reports refused scopes AND rejected attachments in a single body, so the user sees the whole picture
and dismisses once.

**Destructive buttons colour themselves.** A button that performs the dangerous action sets `BackgroundColor = palette.Error` + `TextColor = palette.OnError` explicitly and is the ONLY `IsPrimary` button in that popup; everything else stays outlined. The dead-end exit prompt (`AppBackCoordinator.ShowExitPrompt`) is the reference: **Exit** is red, **Minimize** and **Cancel** are outlines. Before that, Cancel was the filled accent button and the prompt read as "the green button is the one that closes the app" — the opposite of what it did.

`Settings.IconOverride` (a `MauiIcons.Material.MaterialIcons?`) replaces the type-default icon while keeping the accent color. `Settings.CardBackgroundColor` / `BorderColor` / `TitleColor` / `MessageColor` override their respective theme-derived defaults per popup.

## Footer Layout

`G9PopupSettings.FooterButtonLayout` (`G9PopupFooterButtonLayout`, default `Row`) picks how footer buttons are arranged:

- **`Row`** (default) — buttons share one horizontal row as equal-width columns (`GridLength.Star`) with 8 dp inter-column spacing. Best for 1–2 short labels; up to 3 supported, and buttons beyond 3 are dropped with a warning log (`G9DesignSystem.html` does not specify a 4-button row layout).
- **`Stacked`** — buttons are laid out one **full-width button per row**, top to bottom in caller order, with 8 dp inter-row spacing (a `VerticalStackLayout`). No 3-button cap (vertical stacking scales to any count). Use this for 3+ buttons or long labels that would be cramped in the row layout. **Reference consumer:** the device-clock gate (`DeviceClockGateCoordinator`), whose three long Persian labels (I've fixed it / Open date & time settings / Get help) stack cleanly; the primary "I've fixed it" is placed first (top).

Button styling is the same in both layouts:

- **Primary** buttons (`G9PopupButton.IsPrimary = true`) use the type's accent color as background and white text.
- **Secondary** buttons (`IsPrimary = false`) use a 1.5 dp outline border (`theme.OutlineVariant.WithAlpha(0.85f)`) with a transparent background and `theme.OnSurface` text.
- Per-button `BackgroundColor` / `TextColor` overrides win over both the primary and secondary defaults.
- Each button's tap is wrapped in `AnimateButtonPressAsync` — a coordinated `ScaleToAsync(0.96, 80)` + `FadeToAsync(0.85, 80)` press, then the callback runs, then `ScaleToAsync(1, 120)` + `FadeToAsync(1, 120)` release. Keeps presses tactile without per-button animation controllers.

## Typography

Preset popup title text is bold by default. Shared helper typography is intentionally larger
than the early migration values so alerts stay readable on mobile:

| Text role | Size |
| --- | --- |
| Header title | 17 |
| Body message | 15 |
| Footer button | 15 |
| Input-popup message | 15 |
| Input selection field label | 14 |
| Validation message | 13 |

## Overlay Modes

- **`Transparent`** (default) — the overlay is a solid color (default `Colors.Black`) at `OverlayOpacity` (default 0.45). Same on every platform.
- **`Blur`** — best-effort blur overlay. MAUI does not have a public blur primitive, so the helper falls back to a slightly darker scrim driven by `BlurIntensity` (`Light`/`ExtraLight` add 5–10 % opacity; `Dark`/`ExtraDark` add 15–20 %). On platforms / hardware where compositor blur is not available, the result reads as a darker solid scrim — visually different from `Transparent` but still distinct.

`Settings.OverlayColor` overrides the default base color regardless of mode.

## Sizing

`G9PopupViewAutoSizeMode` controls how the card sizes itself:

- **`Both`** — card hugs its content on both axes.
- **`Height`** (default) — centered popups use the configured `Width`, or 90% of the host width when `Width` is unset. Height auto-sizes to content. Best for input forms or scrollable bodies.
- **`Width`** — card uses the configured `Height` (or full available height if unset) and auto-sizes width to content.
- **`None`** — both `Width` and `Height` are taken verbatim from settings.

The 90% default applies only to centered `Height` popups without an explicit width. Explicit
`Width`, relative positioning, absolute positioning, `Both`, `Width`, and `None` modes keep
their requested sizing behavior. This prevents default alert cards from hugging short text
while preserving the opt-in sizing modes for special flows.

The 90% value is the visible card width. (It used to have shadow spread reserved outside it; with
shadows removed the card measures edge-to-edge within that 90%.)

For long bodies, wrap your content in a `ScrollView` (the input popup does this automatically) — the card height stays bounded and the body scrolls.

## Relative & Absolute Positioning (Rare)

`Settings.RelativeView` + `Settings.RelativePosition` position the card relative to an anchor view. `Settings.StartX` / `StartY` position it absolutely on the host. Both are rarely used in this codebase — every existing caller passes `null` and the card is centered. The control supports them for parity with the legacy `SfG9Popup.ShowRelativeToView` API.

When the anchor view has not been laid out yet (Width/Height ≤ 0), the card falls back to centering and the next layout pass corrects it.

## Hardware Back

`Settings.CloseOnBackButton` (default `true`) flows through `G9PopupSettings` →
`BuildOpenOptions` into `G9PopupViewOpenOptions.CloseOnBackButton`, which `G9PopupView` stores and
exposes as `G9PopupView.ClosesOnBackButton`.

Android hardware / system / predictive back is **not** handled by the popup directly, and no
longer by any `OnBackButtonPressed` override. It is routed by `AppBackCoordinator.HandleBack()`
(registered in `MainActivity`; see App AiGuide `09` §4). The coordinator calls
`G9PopupHelper.TryHandleHardwareBack()` **before** the bottom sheet and the page: if a popup is
open it consumes the press, closing the popup only when `ClosesOnBackButton` is `true`. A
non-cancelable popup still swallows back (it never falls through to the page underneath).

If a popup must be uncancelable, set `CloseOnBackButton = false` and
`CloseOnBackgroundClick = false` so neither back nor an overlay tap can dismiss it.

> The exit prompt shown on a dead-end back press (Close / Minimize / Exit) is itself a normal
> `ShowG9PopupAsync` popup with `CloseOnBackButton` left at its default, so pressing back again
> while it is up just closes it.

## Non-Modal & Draggable G9Popups (Floating Tools)

Two `G9PopupViewOpenOptions` flags turn an `G9PopupView` into a floating, non-modal tool palette — useful for in-app developer overlays, picker palettes, or anything else that needs to stay open while the user keeps interacting with the page underneath.

| Option | Type | Effect |
| --- | --- | --- |
| `OverlayMode = G9PopupViewOverlayMode.None` | `G9PopupViewOverlayMode` | Hides the dim scrim AND makes the popup host input-transparent on empty areas. Taps that miss the card pass straight through to the page content beneath, so the page stays interactive. |
| `IsDraggable = true` | `bool` | Attaches a `PanGestureRecognizer` to the supplied `DragHandle` (or the entire card frame if `DragHandle` is null). Dragging accumulates as `_cardContainer.TranslationX/Y` so the user can move the card freely within the popup host. The drag offset resets on every `Open(...)` so a fresh open always centers the card. |
| `DragHandle = headerView` | `View?` | Scopes the drag gesture to a specific child view — typically a header bar — so taps and gestures inside the body still work normally. **Always supply a header**: leaving `DragHandle = null` makes the entire card a drag surface, which prevents body children from receiving taps. |

The reference call site is `Views/Pages/Developer/DeveloperDebugOverlay`, which hosts a private `G9PopupView` instance and opens it with these three flags. Because `OverlayMode = None` removes the modal scrim, `BackgroundTapped` never fires and `CloseOnBackgroundTap` is effectively ignored — provide an explicit close button (or call `CloseAsync()` from your own UI affordance).

```csharp
var popup = new G9PopupView();
host.Children.Add(popup);

var headerRow = new Grid { /* title + close button */ };
var content = new Grid { /* headerRow + body */ };

popup.SetContent(content);
popup.Open(new G9PopupViewOpenOptions
{
    Width = host.Width * 0.9,
    Height = host.Height * 0.45,
    AutoSizeMode = G9PopupViewAutoSizeMode.None,
    OverlayMode = G9PopupViewOverlayMode.None,   // non-modal — page stays interactive
    IsDraggable = true,                        // user can drag the card
    DragHandle = headerRow,                    // grip = header only
    Animation = G9PopupAnimationType.SlideUp,
    AnimationDuration = 220,
    CardBackground = G9Palette.Current.Background,
    BorderColor = G9Palette.Current.OutlineBorder,
    CornerRadius = 18,
    Padding = new Thickness(0)                 // content brings its own padding
});
```

### Implementation notes

- `OverlayMode.None` is wired through `ApplyOptions` in `G9PopupView.cs`: the overlay BoxView is set `IsVisible = false`, `InputTransparent = true`, and the popup root's `InputTransparent` flag is flipped to `true` while open. Combined with the constructor's `CascadeInputTransparent = false`, the only hit-testable thing is the card frame — exactly what a non-modal popup needs.
- `IsDraggable` re-attaches the pan recognizer on every `Open(...)` only when the resolved drag handle changes (object reference compare). Re-opening with the same handle keeps the existing recognizer, so the queue gate doesn't accumulate duplicate listeners.
- The drag is **cumulative**: on `Started` the card's current `TranslationX/Y` is captured, and on `Running` the new translation is `start + e.TotalX/Y`. This is more stable than per-tick deltas because `PanUpdatedEventArgs` reports total-since-gesture-start.
- `Open(...)` resets `_cardContainer.TranslationX/Y` to 0 **before** the open animation runs so a re-open always centers the card regardless of where the previous instance was dragged. The open animation then sets its own start translation (e.g. `+40` for SlideUp) and animates back to 0.
- For multiple concurrent popups, host two `G9PopupView` instances in different parents (the in-app developer overlay does this — its private `G9PopupView` lives inside `DevHost`, totally separate from the global `G9PopupView` mounted by `G9PageTemplate.xaml` in `G9PopupHost`). The helper queue (`G9PopupHelper.ShowG9PopupAsync`) only governs the global instance.

## G9ToastHelper Compatibility

`G9ToastHelper` was originally built on top of `SfG9Popup` for its full-screen blocking loader (`ShowLoadingAsync` / `DismissLoadingAsync`). The toast / loading-toast / progress-toast paths use inline `Border` views rather than popups; only the full-screen blocking loader needed migrating.

The full-screen loader now uses an inline `Grid` overlay scrim + a centered `Border` card mounted directly into the page-level layout (same `ResolveHostContext()` lookup the toast paths use). The transition matches the legacy SfG9Popup `Fade` (200 ms) so the user-perceived behavior is identical. Tap-to-dismiss is intentionally NOT supported — the loader blocks UX until the caller invokes `DismissLoadingAsync()`.

## Test Page Coverage

`TempDesignPageIman` contains coverage under the **G9Popup** tab, organized in three groups:

### Type presets (5 buttons)

- **Information (1 button, SlideUp default)** — Information G9PopupType, default SlideUp, 1-button footer.
- **Success (auto-close 2s, FadeIn)** — Success G9PopupType, AutoCloseDuration timer, FadeIn animation.
- **Warning (2 buttons + close-on-bg, SlideUp)** — Warning G9PopupType, 2-button footer (secondary outline + primary solid), CloseOnBackgroundClick, SlideUp animation.
- **Error (3 buttons, Bounce)** — Error G9PopupType, 3-button footer, Bounce animation.
- **Custom view (DropIn, blur overlay)** — Custom G9PopupType, arbitrary `View` mounted via `ShowCustomG9PopupAsync`, DropIn animation, Blur overlay mode with `Dark` intensity.

### Composability (3 buttons)

- **Confirm (returns bool)** — `ShowConfirmAsync` wrapper, secondary-outline + primary-solid styling, awaitable bool result.
- **Input form (text + email + password + radio)** — `ShowInputG9PopupAsync` building the form view, per-field validation (Required + Email format), `G9PopupResult.NoAction` on validation failure, surfacing values + errors after submit.
- **Queue 3 popups (info → warn → success)** — helper queue gate, FIFO ordering, close-then-mount handoff between consecutive popups, overlay alpha re-fade on each open.

### Animation cycle (1 button)

- **Animation variants (cycle inline)** — single popup with a "Next ▶" button that cycles through every `G9PopupAnimationType` (None, FadeIn, ZoomIn, SlideUp, DropIn, Bounce). Each press closes with the current animation and reopens with the next variant. Useful for visually comparing all 6 animation kinds side-by-side without spawning 6 separate buttons.

Run the whole G9Popup tab after changing `G9PopupHelper`, `G9PopupSettings`, `G9PopupView`, `G9PopupVisualProfile`, the helper's footer button styling, or shared modal metrics. Toggle LTR/RTL and Light/Dark while popups are open to verify icon badge color, footer button styling, secondary-outline border color, and the overlay scrim color all stay correct.

## Do Not Regress

- Do not reintroduce `the former third-party library (now removed).Maui.G9Popup` (or any SfG9Popup-typed field). The hand-rolled `G9PopupView` is the contract; if you need a feature that's missing, add it to `G9PopupView` rather than swapping the control out.
- Do not move the helper out of the `Components.Modal` namespace. It physically lives in the `CustomizedG9Popup` folder, but the **namespace must stay `G9MAUIControls.Popup`** so existing callers' `using` directives keep resolving without a code change.
- Do not run the open animation and the close animation on the same animator name. The control uses two distinct animator names (`G9PopupViewCardMotion` for the card, `G9PopupViewOverlayMotion` for the overlay) so the overlay can fade independently when the card has `Animation = None`.
- Do not replace the compound `Animation` construction in `AnimateSimultaneous` with two separate `Animate(...)` calls. The two channels (opacity + scale, or opacity + translation) MUST run on a single animator so they share clock and length — if they're separate, a mid-open close will desync them visibly (the card scales back before the opacity is fully recovered).
- Do not gate `CloseAsync` on the close animation completing before flipping `_isOpen` to false. The flip happens synchronously at the start of `CloseAsync` so a second `Open()` request can immediately abort the close animation and start a fresh open with the new content.
- Do not remove `InputTransparent = true` / `CascadeInputTransparent = false` from the `G9PopupView` constructor. Without these flags the empty area of the popup host Grid (which fills the entire `OverlayHost`) blocks taps on the page content underneath — the symptom is that LoginPage's username/password entries can't be tapped while no popup is open, but Tab still focuses them.
- Do not block the input popup's submit button on validation failure with a real `Close()`. Use `G9PopupResult.NoAction()` so the popup stays open and the user can correct the field. Returning `Close` here loses the entered values and the helper queue mistakenly advances.
- Do not hardcode per-popup spacing or radius. Use `G9LayoutMetrics` and `G9Palette` like the rest of the codebase. The defaults in `G9PopupViewOpenOptions` already pull from the design system; per-popup overrides should be the rare exception.
- Do not bypass the helper queue by calling `host.G9Popup.Open(...)` directly from anywhere outside `G9PopupHelper.PresentAsync`. The queue gate is what prevents two concurrent `ShowG9PopupAsync` calls from racing each other and showing one popup on top of another.
- Do not rebuild the toast inline overlay scrim on top of `G9PopupView`. The toast paths (`ShowToastAsync`, `ShowLoadingToastAsync`, `ShowProgressToastAsync`) use plain inline `Border` views mounted directly into the page-level layout because they don't need the popup's modal semantics. Only `ShowLoadingAsync`'s full-screen blocking overlay shares the same "scrim + card" structure — and even that one is a hand-rolled inline `Grid + BoxView + Border`, not an `G9PopupView` instance, because making it a popup would mean it gets queued behind any other open popup.
- Do not point a `G9PopupType` at `G9Palette.Primary` (or `Secondary`) in `G9PopupVisualProfile`. Information → `Info`, Success → `Success`, Warning → `Warning`, Error → `Error`; that mapping IS the type's meaning. Same for the primary button's text: take `profile.ButtonTextColor` (the accent's `On*` token), never `Colors.White`.
- Do not "simplify" the secondary-outline button styling to a `BackgroundColor = palette.SurfaceContainer` fill. The 1.5 dp outline + transparent fill is the design system spec; replacing the outline with a fill makes the secondary button visually competitive with the primary button.
- Do not add a per-platform handler for `G9PopupView` unless a feature genuinely requires native code. The control is intentionally cross-platform with no per-platform code so a feature added in `G9PopupView.cs` lights up on Android, iOS, Mac Catalyst, and Windows simultaneously.
- Do not call `View.FadeTo` / `View.ScaleTo` directly. They are obsoleted in MAUI 10. Use `View.FadeToAsync` / `View.ScaleToAsync` so we don't accumulate `CS0618` warnings; the new APIs return a `Task<bool>` that we already await.
- Do not flip `IsDraggable` on without supplying a `DragHandle`. Leaving `DragHandle = null` makes the entire card a drag surface and silently kills tap routing for body children (taps inside G9Switch / G9TextEntry / footer buttons stop working). Always pass a header bar so body children stay interactive.
- Do not couple `OverlayMode.None` with `CloseOnBackgroundTap = true` and expect the user to dismiss by tapping outside. With `None`, the overlay is hidden AND input-transparent — `BackgroundTapped` never fires. Provide an explicit close button (or wire your own outside-the-host gesture) when using non-modal popups.
