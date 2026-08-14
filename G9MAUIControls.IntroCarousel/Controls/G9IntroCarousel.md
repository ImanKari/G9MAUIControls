# G9IntroCarousel

Full-screen login onboarding carousel: looping background video (or image fallback),
dark overlay, logo, localized title/subtitle, step pills, prev/next navigation, language
button, and a primary sign-in CTA.

## When to use

- On `LoginPage` before the login form is shown (overlay; no persistence — shown on every visit).
- Not for in-app feature tours — use a dedicated flow if you need dismiss-once behaviour.

## Media

- Videos are bundled as MauiAssets under `Resources/Raw/Onboarding/*.mp4`.
- Only **one** `MediaElement` instance exists; changing slides swaps `Source` (avoids
  multiple players in a `CarouselView` template).
- All slide videos are **pre-copied to cache on first load**; slide changes update chrome
  immediately and defer the media swap by one dispatcher tick (same idea as
  `G9CascadePanel` lazy content) with a brief spinner overlay.
- Set `ImageSource` on `G9IntroSlideItem` for image-only slides or video failure fallback.
- The `CarouselView` and its item template are explicitly transparent so the green
  `_mediaHost` background (`#2E7D32`) is always visible while the first video frame loads —
  preventing a gray flash on Android.
- Videos should not need baked-in black frames. `UseMediaFadeTransitions=true` fades media
  in when a slide becomes ready, fades the outgoing media out before a manual slide change,
  and `UseVideoLoopFade=true` fades a video out near its end and back in after the loop
  restarts. This keeps the loop trick in the control instead of in every MP4 file.
- `SkipFirstSlideInitialFadeIn=true` makes the first slide's first reveal immediate so login
  never opens on a deliberate black fade. Later slide changes and video loops still use the
  configured fades.
- If a poster is still needed for unusual media, use `StartupCoverImageSource` +
  `StartupCoverDurationMs`. The cover is a normal MAUI `Image` layered above media and
  below the overlay/chrome, so it is deterministic on Android, iOS, macOS, and Windows.
  The optional `InitialVideoStartSeconds` seek remains available, but it is applied after
  `MediaOpened` and never blocks `Play()` because each platform player accepts seeks at
  slightly different points in the load cycle.
- The dark overlay can be flat or a vertical gradient. `UseGradientOverlay=true` keeps the
  bottom at `OverlayOpacity` for text readability and uses
  `OverlayTopOpacity` when explicitly set; otherwise it derives the top value from
  `OverlayOpacity * OverlayTopOpacityRatio`.
  The overlay is painted by a single `GraphicsView` drawable instead of a `Border` brush so
  Android `TextureView` video stays visible under the alpha gradient.
- **The logo/title/subtitle/nav text shadows were REMOVED (2026-07-28).** They were MAUI `Shadow`
  instances on an `Image` and four `Label`s — none of which has a `BorderDrawable` background, so
  every one of them took MAUI's Android software bitmap-blur path (`../G9Controls.md` §0). The
  four bindable properties that configured them (`UseChromeShadow`, `ChromeShadowOpacity`,
  `ChromeShadowRadius`, `ChromeShadowOffsetY`) no longer exist — delete them from any XAML that still
  sets them. **Readability over light video frames now comes from the gradient overlay**: set
  `UseGradientOverlay="True"` and raise `OverlayOpacity` / lower `OverlayTopOpacityRatio` until the
  text reads.

## Bindable properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Slides` | `IList<G9IntroSlideItem>?` | Built-in five slides | Slide definitions |
| `CurrentIndex` | `int` | `0` | Active slide (two-way) |
| `LanguageCommand` | `ICommand?` | `null` | Wired to header `SafeIconButton` |
| `CompleteCommand` | `ICommand?` | `null` | Sign-in CTA (skip intro) |
| `UseMediaFadeTransitions` | `bool` | `true` | Fades media in on reveal and fades current media out before slide changes. Applies to videos and image fallback slides. |
| `UseVideoLoopFade` | `bool` | `true` | For video slides, fades out during the final `MediaFadeOutDurationMs` and fades back in when looping restarts. |
| `SkipFirstSlideInitialFadeIn` | `bool` | `true` | Makes only the first slide's first reveal immediate, avoiding a black startup fade. |
| `MediaFadeInDurationMs` | `int` | `420` | Opacity fade-in duration. Set `0` for immediate reveal. |
| `MediaFadeOutDurationMs` | `int` | `420` | Opacity fade-out duration for slide changes and video loop ends. Set `0` for immediate swap/no loop fade out. |
| `OverlayOpacity` | `double` | `0.52` | Bottom opacity for the dark overlay. Also used as the flat overlay opacity when `UseGradientOverlay=false`. |
| `UseGradientOverlay` | `bool` | `false` | When true, paints the overlay as a top-to-bottom black gradient instead of a flat black layer. |
| `OverlayTopOpacityRatio` | `double` | `0.4` | Top opacity multiplier for gradient mode. `0.4` means the top is 60% lighter than the bottom. |
| `OverlayTopOpacity` | `double` | `NaN` | Optional explicit top opacity for gradient mode. When not set, top opacity is derived from `OverlayTopOpacityRatio`. |
| `OverlayColor` | `Color` | `Black` | Color used by both flat and gradient overlays. |
| `StartupCoverImageSource` | `string?` | `null` | Optional full-screen image shown above media on first presentation. Use a poster frame exported from the first video for best continuity. |
| `StartupCoverDurationMs` | `int` | `0` | Minimum time the startup cover remains visible. `0` disables the cover even when an image is set. |
| `StartupCoverFadeDurationMs` | `int` | `320` | Fade-out duration after the minimum cover time has elapsed and media/fallback is ready. |
| `InitialVideoStartSeconds` | `double` | `0` | Best-effort, non-blocking one-time seek applied only to the first slide's first video load. Useful to skip an intentional black intro frame. |

## Default slides

`G9IntroSlides.CreateDefault()` — five videos mapped to `IntroSlide*Title/Subtitle` resx keys.

## RTL navigation

When the active culture is RTL (e.g. Persian), `ApplyNavIconLayout(isRtl: true)` physically
swaps the grid columns of the **Next** and **Previous** tap targets so their screen positions
match the RTL reading expectation:

| Direction | Physical left (column 0) | Physical right (column 2) |
|---|---|---|
| LTR | Previous `‹` | Next `›` |
| RTL | Next `›` (بعدی) | Previous `‹` (قبلی) |

The icon chevrons also flip (`ChevronRight` ↔ `ChevronLeft`) so the arrow always points
in the direction of travel. The inner `StackLayout` children order mirrors the swap so
label and icon remain adjacent in the correct reading order.

## First-content-ready signal (`FirstContentReady`)

The carousel raises **`FirstContentReady`** (an `EventHandler`) **once**, on the main thread,
the first time it has real content on screen: the first slide's video frame is revealed, its
fallback image is shown, or media settles with nothing to display. It never fires more than
once per control lifetime.

`LoginPage` (an `G9PageBase`) shows the green `PageLoadingOverlay` splash from the moment its
template inflates. Instead of dismissing on a blind timer, `LoginPage` opts into manual release
(`PageLoadingManualRelease => true`) and calls `ReleasePageLoadingOverlay()` from a
`FirstContentReady` handler, so the splash holds until the first frame is painted and then
crossfades out as the frame fades in — no black/empty player on a cold (fresh-install) video
load. `G9PageBase`'s safety timeout (raised to 8 s on `LoginPage`) is the backstop if media
never loads; the carousel's own backdrop is the same splash green (`#2E7D32`), so a timeout
dismissal is not jarring. The subscription is removed in `OnHandlerChangingAfterParent` when
the page handler is torn down. See `AiGuides/09-Page-Lifecycle-And-Loading-Overlay.md`.

## MediaElement disposal safety (MAUI 10)

### Root cause (CTMAUI 9.x + MAUI 10)

CTMAUI 9.x adds `MauiMediaElement` (a `CoordinatorLayout`) as the **first child** of the app's
root `CoordinatorLayout` so ExoPlayer renders at the correct z-order.

`VisualDiagnosticsOverlay.Initialize()` (called by `WindowHandler.OnRootViewChanged`) does:
```
_nativeLayer = rootManager.RootView.GetFirstChildOfType<ViewGroup>()
             = MauiMediaElement            ← CTMAUI placed it first
_nativeLayer.AddView(_graphicsView, 0)     ← overlay canvas added INSIDE MauiMediaElement
```

On `window.Page = mainPage` (`RootNavigationService.SwapToMainAsync`):
1. Old fragment `onDestroyView` → MAUI auto-`DisconnectHandler` → `MauiMediaElement` **disposed**
2. New fragment `onViewCreated` → `WindowHandler.OnRootViewChanged`
   → `VisualDiagnosticsOverlay.Deinitialize()`
   → `_nativeLayer.RemoveView(_graphicsView)` on the disposed `MauiMediaElement`
   → **`ObjectDisposedException`** 💥

### Correct fix

Subscribe to `LoginPage.Disappearing` (in the constructor) and call `IntroCarousel.StopAndReleaseMedia()`.
`G9PageBase` seals `OnDisappearing`, so the `Disappearing` event is the correct hook.
`Disappearing` fires synchronously from `base.OnDisappearing()` during the old fragment's
**`onPause`** — while the fragment and `MauiMediaElement` are still alive.

`StopAndReleaseMedia()` does two things in order:
1. **`VisualDiagnosticsOverlay.Deinitialize()`** — removes `_graphicsView` from
   `MauiMediaElement` while it is alive; sets `IsPlatformViewInitialized = false`.
2. **`_mediaElement.Handler?.DisconnectHandler()`** — disposes `MauiMediaElement`.

When MAUI's `OnRootViewChanged` fires (new fragment `onViewCreated`), it sees
`IsPlatformViewInitialized = false` and skips the second `Deinitialize()` call entirely,
then re-initializes the overlay for the new page. **No crash.**

### Lifecycle order

| Step | What runs |
|---|---|
| 1 | Login success → `SwapToMainAsync()` → `window.Page = mainPage` |
| 2 | Old fragment `onPause` → `LoginPage.Disappearing` event fires |
| 3 | `StopAndReleaseMedia()` → **`VisualDiagnosticsOverlay.Deinitialize()`** → removes overlay from `MauiMediaElement` (alive) |
| 4 | `StopAndReleaseMedia()` → **`DisconnectHandler()`** → `MauiMediaElement` disposed |
| 5 | Old fragment `onDestroyView` → MAUI auto-disconnect (handler null → no-op) |
| 6 | New fragment `onViewCreated` → `OnRootViewChanged` → `IsPlatformViewInitialized=false` → **skip Deinitialize** → `Initialize()` for new page → ✅ |

**Do NOT call `DisconnectHandler()` from `G9IntroCarousel.HandlerChanging`** — that event
fires during `onDestroyView` (step 5), after `MauiMediaElement` is already disposed by step 4.
The `OnMediaElementHandlerChanging` handler is kept as a safety-net stop for ExoPlayer.

## Example (LoginPage)

```xml
<g9:G9IntroCarousel
    IsVisible="{Binding IsIntroVisible}"
    LanguageCommand="{Binding OpenLanguagePickerCommand}"
    CompleteCommand="{Binding CompleteIntroCommand}"
    UseGradientOverlay="True" />
```

Add `StartupCoverImageSource`, `StartupCoverDurationMs`, and
`StartupCoverFadeDurationMs` only when a static poster should intentionally cover the
first few seconds while media warms up.

View model: `IsIntroVisible` starts `true`; `CompleteIntroCommand` sets it `false`.
