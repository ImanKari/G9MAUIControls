# G9Shimmer — render-thread shimmer skeleton placeholders

## What it is

`G9Shimmer.CreateSheetSkeleton(skeleton, rowCount, backgroundColor)` builds a loading
placeholder made of **static gray shape rows** (plain MAUI `Border`s — zero animation cost)
overlaid by a **`G9ShimmerBandView`** highlight sweep. It is consumed by `G9BottomSheetHelper` through
`G9BottomSheetOptions.LoadingSkeleton` / `LoadingSkeletonRowCount` (it becomes the
`DeferredContentView.LoadingView` for the sheet's loading window), and can be used anywhere else a
loading placeholder `View` is accepted.

Skeleton shapes (`G9BottomSheetLoadingSkeleton`):

- `ListRows` — selection/menu rows: leading circle + text bar, `G9Metrics.SelectionRowHeight`
  tall. Pass the real item count so the placeholder matches the content (`G9SelectionSheet`
  does this).
- `FormFields` — full-width field-height bars (detail cards / forms).
- `None` — keep the default centered spinner.

## Why the band animation never freezes (the whole point)

Every previous shimmer in this app froze exactly when it was needed, because its animation ticked
on the **UI thread** (MAUI `Animation`, SkiaSharp `IDispatcherTimer` + `InvalidateSurface`) — the
same thread that was busy inflating the heavy content the shimmer was masking. The stock
`ActivityIndicator` never freezes because Android's indeterminate `ProgressBar` is an
`AnimatedVectorDrawable`, and **AVDs animate on the RenderThread since API 25**.

`G9ShimmerBandView` uses the same mechanism per platform:

| Platform | Mechanism | Animation thread |
|---|---|---|
| Android | `AnimatedVectorDrawable` (`Resources/drawable/g9_shimmer_band.xml` → `g9_shimmer_vector.xml` + `animator/g9_shimmer_translate.xml`) hosted in an `ImageView` (`G9ShimmerBandViewHandler.Android.cs`) | RenderThread |
| iOS / Mac Catalyst | `CAGradientLayer` + repeating `CABasicAnimation` (`G9ShimmerBandViewHandler.MaciOS.cs`) | Core Animation render server |
| Windows | no handler — the band is omitted (`#if` in `G9Shimmer`), static skeleton only | — |

## Theme-adaptive band colour (light highlight vs dark lowlight)

The **light** band is a **fixed translucent-WHITE gradient sweep** (Android `g9_shimmer_vector.xml`
peaks at `#59FFFFFF` ≈ 35%; iOS `CAGradientLayer` peaks at 0.38 white). Over the light-gray skeleton
rows that white sweep reads as a subtle highlight. In **dark** theme the identical white band is a
harsh, high-contrast bright shape sliding across — a visibly "bad" light slab — because the band
overlays the WHOLE placeholder, including the near-black host background (`Background` = `#0C0D0C`)
between the rows, and anything lighter than near-black is plainly visible there. Neither dimming the
white (lower alpha) nor narrowing its geometry fixed it: a white highlight over a near-black gap is a
light streak no matter how faint or thin.

Dark mode therefore **inverts the sweep to a translucent-BLACK "lowlight"** rather than a white
highlight. Black is invisible over the near-black background but darkens the raised skeleton bars
(`SurfaceContainerHigh` = `#22262A`) as it passes, so the shimmer shows up ONLY over the bars — a soft
shadow sweep that belongs to the dark theme instead of a bright box floating over it. The geometry and
timing are otherwise the same as light (a broad, soft parallelogram): Android selects the dedicated
`g9_shimmer_band_dark.xml` / `g9_shimmer_vector_dark.xml` AVD (same path as the light vector,
gradient recoloured to black peaking at `#33000000` ≈ 20%), while iOS swaps `UIColor.White` for
`UIColor.Black` at peak alpha 0.20 with the same broad `0.35 / 0.50 / 0.65` stops. **The dark strength
is a one-value knob** — the middle gradient stop's opacity (Android) / `peakAlpha` (iOS); lower it for
a subtler sweep. The animation still
runs on the RenderThread / Core Animation render server. Dark-theme detection uses the app-wide pattern
(`UserAppTheme` set by the Profile picker, falling back to system `RequestedTheme` only while
unspecified — same as `G9Colors`). **The light vector, gradient, opacity, and timing are
unchanged.** Theme is read when the transient shimmer is created, so a theme flip self-corrects on the
next load.

## Rules

- **Do NOT reimplement the band with MAUI `Animation`, timers, or SkiaSharp invalidation loops** —
  that reintroduces the frozen-shimmer failure mode this control exists to remove.
- The skeleton rows must stay **static** plain views; only the band animates.
- The band is a **white highlight in light theme, a black "lowlight" in dark theme** (above): in dark
  mode it must never be a white / lighter-than-background sweep, or it becomes a bright slab over the
  near-black gaps between rows. If you retune the band colour, peak, or geometry, adjust BOTH the
  Android dark vector (`g9_shimmer_vector_dark.xml`) AND the iOS handler
  (`G9ShimmerBandViewHandler.MaciOS.cs`) while leaving the light-theme recipe unchanged.
- The host paints an opaque background (like the default spinner placeholder) so content
  underneath never leaks through while loading.
- Keep the Android resource names (`g9_shimmer_*`) in sync with
  `G9ShimmerBandViewHandler.Android.cs` (`Resource.Drawable.g9_shimmer_band` and
  `Resource.Drawable.g9_shimmer_band_dark`). Both AVDs share
  `animator/g9_shimmer_translate.xml`.
- Handler registration lives in `Common/Extensions/MauiAppBuilderExtensions.cs`
  (`AddCustomizedOrNewHandlers`).
