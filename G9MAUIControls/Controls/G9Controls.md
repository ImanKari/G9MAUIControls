# G9Controls Architecture Guide

The `Controls/` folder contains the G9MAUIControls input and feedback controls. Every control here is
hand-rolled on top of MAUI primitives (`Border`, `Grid`, `Label`, `Entry`, `Editor`, `GraphicsView`)
and works consistently across Windows / Android / iOS / Mac Catalyst without per-platform XAML.

---

## ⚠ Read this before trusting the rest of the file

This guide came across from the product these controls were extracted from, and **most of it is still
exactly right** — in particular §0 (no shadows) and §15 (the platform crash catalog) are the most
valuable pages here and are unchanged. Three things did change during extraction, and where the text
below still describes the old shape, this section wins.

**1. Icons: one typed slot per position.** An earlier design gave every position two font-typed
properties — one per icon font it knew about. Both are gone. Every position now has **one**
`G9IconSource?` slot, and the font is part of the value rather than part of the property name:

| Was | Now |
|---|---|
| a pair of per-font leading slots | `LeadingIcon` |
| a pair of per-font trailing slots | `TrailingIcon` |
| a pair of per-font icon slots | `Icon` |
| `G9Visuals.HasIcon(...)` / `G9Visuals.CreateIcon(...)` | `G9IconFactory.HasIcon(...)` / `G9IconFactory.Create(...)` |
| a `MauiIcon` view | a `G9IconView` (`Color` / `Size`, not `IconColor` / `IconSize`) |

`G9IconSource` takes any icon-font enum implicitly (family = the enum's type name, glyph = its
`[Description]` — the convention icon-font generators already emit), one of the built-in vector
`G9Glyph` members, an explicit font/glyph pair, or a name resolved at runtime through
`G9IconFonts.Resolve`. Slot precedence is **emoji → icon → image**. The reasoning is ADR-0002 in
`AiGuides/10-Decisions.md`.

**2. The library's own chrome glyphs are vector paths, not a font.** The chevron, clear ×, eye,
search, check, calendar, clock, mic, popup type accents and so on are `PathF` geometry authored on a
24×24 grid in `Icons/G9GlyphDrawable.cs`. No font is bundled and none is required, so a fresh consumer
gets a complete-looking suite with zero configuration and no possibility of the Android tofu-box
failure catalogued in §15 A2. Every default is individually overridable through `G9Glyphs`
(`G9Glyphs.Chevron = MyIcons.ExpandMore`). ADR-0003.

**3. Host dependencies are optional hooks.** Everything the controls would otherwise reach into a
host application for — the culture service, the string catalogue, preference storage, image loading,
speech-to-text, the Android activity — is a hook a consumer opts into (`G9Culture`, `G9Strings`, `G9Preferences`,
`G9ImageFactory`, `G9Speech`, `G9AndroidHost`). None is mandatory. ADR-0004.

**Also note:** a few passages below still describe a control in terms of the screens and flows it was
originally built for, and point at guide files that are not part of this repository. The technical
claims still hold; those pointers do not. Cleaning the remaining prose up is tracked in
`AiGuides/09-Progress.md`.

---

The folder layout is intentionally one-control-per-folder so each control owns its public
class, its drawables, and its sub-views. A single `Shared/` folder carries the
shared infrastructure that every control inherits.

```
Common/Components/G9/
├── G9Controls.md                      # this file
├── Shared/                                # shared base / drawables / metrics / colors / platform
│   ├── G9ControlBase.cs                  # ContentView base — lifecycle + theme/culture + re-entrancy
│   ├── G9Colors.cs                # G9 color recipes (will move to G9Palette)
│   ├── G9Enums.cs                 # G9ButtonVariant, G9KeyboardType, G9TextInputDirection, …
│   ├── G9Metrics.cs               # All tunable layout / animation tokens (const)
│   ├── G9PlatformConfig.cs        # Platform handler tweaks (no-underline + chrome strip)
│   ├── G9Strings.cs               # Internal localized strings (Done, Search, etc.)
│   ├── G9Visuals.cs               # Icon factory + variant resolution helpers
│   ├── G9CornerBadge.cs                  # Shared corner notification badge (dot / count) — G9IconButton + G9NavCard
│   ├── G9FieldSlotLayout.cs              # Icon-column / label-X / inner-padding math (single source)
│   ├── G9OutlinedFieldBase.cs            # G9ControlBase + outline + floating label + icon slots
│   ├── G9OutlinedFieldDrawable.cs        # The painted outline (rounded box + notch + focus emphasis)
│   ├── G9OutlinedFieldVisual.cs          # Floating-label TranslationX/Y/Scale animator
│   ├── G9RippleDrawable.cs               # G9Button ripple + inset highlight
│   ├── G9SelectionItem.cs                # Shared item model for picker / combo / chip
│   ├── G9SelectionSheet.cs               # Bottom-sheet picker UI shared by Picker / ComboBox; reveals initial selection
│   └── Helpers/
│       └── TapOutsideKeyboardDismisser.cs # Page-scoped tap-outside-to-dismiss-keyboard for inputs
├── G9Button/                          # G9Button + .md guide
├── G9CascadePanel/                    # G9CascadePanel (nested drill-down stack) + .md guide
├── G9ChipGroup/                       # G9ChipGroup + .md guide
├── G9ComboBox/                        # G9ComboBox + .md guide
├── G9DateTimePicker/                  # G9DateTimePicker + drum picker + .md guide
├── G9Editor/                          # G9Editor + .md guide
├── G9Expander/                        # G9Expander (collapsible section) + .md guide
├── G9IconButton/                      # G9IconButton + .md guide
├── G9NavCard/                         # G9NavCard + .md guide
├── G9Picker/                          # G9Picker + .md guide
├── G9PinEntry/                        # G9PinEntry + .md guide
├── G9ProgressBar/                     # G9ProgressBar + drawable + .md guide
├── G9RangeSlider/                     # G9RangeSlider + drawable + .md guide
├── G9SearchEntry/                     # G9SearchEntry + .md guide
├── G9Separator/                       # G9Separator + .md guide
├── G9SwipeView/                       # G9SwipeView + G9SwipeAction + .md guide
├── G9Switch/                          # G9Switch + drawable + .md guide
├── G9TabView/                         # G9TabView + G9TabItem + .md guide
├── G9TextEntry/                       # G9TextEntry + .md guide
├── G9IntroCarousel/                   # G9IntroCarousel (login onboarding slides) + .md guide
└── G9BarcodeEntry/                    # G9BarcodeTextEntry + .md guide
```

## Design Principles

### 0. No shadows — anywhere in the app (HARD RULE)

**Never set a MAUI `Shadow` on anything.** Not on a G9 control, not on a `Border`, a
`Grid`, a `Label`, an `Image`, a bottom sheet, a popup, a toast, or a map overlay. Not in
XAML (`<Border.Shadow>`, `Shadow="…"`), not in C# (`X.Shadow = new Shadow { … }`). There is
no approved exception and no helper to build one — `G9Colors.BuildShadow` was deleted.

**Why (measured, not stylistic).** On Android, `PlatformWrapperView.drawShadow` only takes
its cheap GPU path when the wrapped child's platform background is a MAUI `BorderDrawable`
that reports `canDrawShadow()`. Everything else falls through to
`drawShadowViaDispatchDraw`, which — verified against the `maui.aar` bytecode for the MAUI
version this app builds on — does the following **on the UI thread, on every draw pass**:

1. takes an `ARGB_8888` `Bitmap` the size of the view from Glide's `BitmapPool`,
2. rasterizes the entire child subtree into it via `super.dispatchDraw(shadowCanvas)`,
3. draws that bitmap through a `BlurMaskFilter` paint — which has no hardware
   implementation, so Skia box-blurs it in **software** (`SkMaskBlurFilter::blur`).

On 2026-07-28 this produced a **hard ANR** on a Doogee S96Pro (Android 10): ~250 ms per
frame in display-list recording on top of ~200 ms of layout, 821 then 594 skipped frames,
input queue head age 7.7 s, and the system killed the process. The full post-mortem is in
[`../../../AiGuides/02-Crash-And-Build-Hazards.md`](../../../AiGuides/02-Crash-And-Build-Hazards.md)
→ "MAUI `Shadow` = software bitmap blur on the UI thread".

**Express elevation without a shadow.** In priority order:

1. A **`Stroke` + `StrokeShape`** hairline on a `Border` — this is the house style and what
   every G9 control now uses.
2. A **surface-tone step** — `SurfaceContainerLow` → `SurfaceContainerHigh` etc. Material's
   own tonal-elevation model, and free at render time.
3. A **background gradient** via `G9Colors.BuildSolidOrGradient`.
4. If a soft edge is genuinely required, paint it yourself. Two DIFFERENT mechanisms exist
   in-tree and they are not equivalent — do not confuse them:
   - **SkiaSharp (`SKCanvasView` + `SKMaskFilter.CreateBlur`)** — the sanctioned one. Blurs
     exactly the path you draw, on the Skia render thread, identically on every platform.
     Precedents: `G9TabBarShadowView` (bar + FAB silhouette) and
     `SkiaPhotoEditorTextRenderer` (`SKImageFilter.CreateDropShadow`). A third
     (`ToastShadowView`, for the sync overlay) was added and reverted on 2026-07-28 — the
     card had to be inset to make room for the blur, which broke the width it shares with
     the message toasts and the tab bar.
   - **`ICanvas.SetShadow` inside a MAUI `GraphicsView` `IDrawable`** — NOT Skia. On Android
     this compiles to `android.graphics.Paint.setShadowLayer` on the platform canvas, and
     the repo has already found it **unreliable on Android's hardware-accelerated
     `GraphicsView`** (which is why `G9TabBarChromeDrawable`'s `SetShadow` call is zeroed
     out via `BarShadow* = 0`). It is cheap — it blurs one small primitive, not a view
     subtree, so it is nowhere near the ANR path — but treat it as best-effort decoration.
     It now has **zero users**: `G9SwitchDrawable` and `G9RangeSliderDrawable` dropped
     their thumb shadows on 2026-07-28 so the app is uniformly flat. Do not reintroduce it.

   Whichever you pick, the blur must render **inside** the control's own bounds: Android
   clips negative-margin overflow (that is what killed three sides of the tab bar's halo).
   Reserve padding internally and draw inset by it — accepting that this changes the shadowed
   control's own geometry, which is what sank the sync-toast attempt.

There is also no `Shadow` / `ShadowLight` colour token: both were removed from
`G9Palette`, `G9PaletteValues`, `G9ColorToken`, `G9Theme` and both theme
dictionaries. The Skia painters listed above hard-code black, which is exactly what those
tokens resolved to in **both** light and dark.

**Pre-merge audit.** This is a runtime-cost regression, not a compile error, so grep for it.
Both of these must return zero hits outside `bin/` and `obj/`:

```pwsh
rg -n '<[A-Za-z.:]*\.Shadow>|\bShadow\s*=\s*"' --glob '*.xaml' G9MAUIControls
rg -n 'new Shadow\s*[{(]|\.Shadow\s*=' --glob '*.cs' G9MAUIControls
```

### 1. Two-tier inheritance — one base per family

Every control inherits from one of two shared base classes:

- **`G9ControlBase : ContentView`** — the lightweight base used by feedback / display
  controls (`G9Button`, `G9CascadePanel`, `G9ChipGroup`, `G9Expander`, `G9IntroCarousel`, `G9NavCard`, `G9PinEntry`,
  `G9ProgressBar`, `G9RangeSlider`, `G9Separator`, `G9Switch`, `G9TabView`).
- **`G9OutlinedFieldBase : G9ControlBase`** — the input-field base used by every control
  that renders an outlined box with a floating label and optional leading/trailing icons
  (`G9TextEntry`, `G9Editor`, `G9Picker`, `G9ComboBox`, `G9DateTimePicker`).
  `G9BarcodeTextEntry` extends `G9TextEntry` so it picks up the same outline + floating
  label + icon-padding behaviour.

Fixing a bug in the base class fixes every control built on it. Adding a feature to the
base class makes that feature available to every control without touching subclasses.

### 2. `G9ControlBase` — central lifecycle and visual state

Responsibilities:

- Subscribes to `G9Palette.Current.PropertyChanged` on `Loaded` and unsubscribes on
  `Unloaded`. Every control automatically re-applies its visuals when the theme changes.
- Subscribes to `G9Culture.CultureChanged` so a language switch repaints the
  control without the consumer reaching into the control's internals.
- Centralises a re-entrancy guard around `OnApplyVisuals`. Any setter that calls
  `RequestVisualUpdate` is safe even if the apply pass itself causes more property
  changes — the base re-runs once per pending request and never recurses.
- Standardises `IsEnabled` / `FlowDirection` change handling so every subclass refreshes
  itself when the parent toggles enabled or flips direction.
- **Tear-down safety**: hooks `HandlerChanging` and sets an `_isDestroyed` flag when the
  platform handler goes null. Queued visual passes that haven't run yet exit immediately,
  and any platform property write inside `OnApplyVisuals` is wrapped in a defensive
  try/catch for `ObjectDisposedException`. This prevents the
  `IServiceProvider has been disposed` crash that previously occurred when the page
  closed while a visual update was still pending in the dispatcher queue.

Subclasses only have to implement `OnApplyVisuals()`. They never wire theme handlers,
they never re-implement re-entrancy guards.

### 3. `G9OutlinedFieldBase` — one outline architecture for every input

The five input controls share a single outline architecture:

1. **A `GraphicsView` that paints the outline as a `PathF`.** The outline is a rounded
   rectangle with an optional notch on the top edge so a floating label can sit on the
   border without needing a parent-matching background fill (this is the same approach
   Material 3 and other professional toolkits use). The drawable is in
   `G9OutlinedFieldDrawable.cs`.

   **Focus / error emphasis** is painted as a thicker stroke on the same outline, NOT
   as a separate outer ring. The drawable exposes `EmphasisStrokeThickness` — when set
   (focus / error / `UseStatusColor`), the outline is rendered at the heavier thickness;
   otherwise the resting thickness applies. A focused field CAN also paint a soft
   `HaloStrokeColor` / `HaloStrokeThickness` on the inside of the same notched path, so
   it reads as a two-stroke focus state without exceeding the `GraphicsView` bounds. This
   avoids the clipping artefact a separate outer ring produced (it extended past the
   canvas rect and was clipped by the parent `GraphicsView`, leaving a thin sliver inside
   the box and a faint horizontal line below the field on focus).

   **The inner focus halo is OFF by default.** That soft inner glow (the "inner second
   border / shadow glow") is gated behind the `ShowFocusHalo` bindable property on
   `G9OutlinedFieldBase`, which defaults to `false` for EVERY outlined field (text
   entry, editor, picker, combo box, date/time picker, barcode entry — they all inherit
   the base). A focused field is still clearly marked by the thicker primary-coloured
   emphasis stroke; the halo is just the extra glow on top of it. Opt a specific field
   back in with `ShowFocusHalo="True"`. The halo is never painted in the error /
   status-colour states regardless of the flag (`showFocusHalo` in `ApplyOutlineChrome`
   ANDs `ShowFocusHalo` with `IsContentFocused && !HasError && !UseStatusColor`).

   **Filled-valid rest state** is resolved in the shared base. If an outlined field has a
   value (`HasContentValue` for text/editor controls or `IsValueFloated` for picker-like
   controls), no error, and no explicit status colour, blur keeps the label and outline
   on `Primary` but uses the resting stroke thickness and no halo. This keeps completed
   fields visually active while preserving a clear distinction from actual focus.

2. **A row grid with three columns** — leading icon slot (auto-width), inner content
   (star), trailing icon slot (auto-width). All three live in a 3-column `Grid` whose
   `FlowDirection` is locked to `LeftToRight`. The base re-assigns `Grid.Column` on the
   icon hosts every visual pass based on the resolved physical column from
   `G9FieldSlotLayout` (see §4 below) so a culture flip physically swaps the leading
   and trailing icons across the box.

3. **A floating `Label` overlaid on top of the box.** The label is fully transparent
   in both rest and floated states. It animates between the two via `TranslationX`,
   `TranslationY`, and `Scale` — all three composed into a single `Animation` so the
   transforms run on the compositor and stay in sync. When floated, the outline drawable
   opens a notch around the label's measured width so the label appears to break the
   outline — without painting any background.

   **Anti-jump gate** — the animation only kicks when the floated state actually
   transitions OR when the rest target / floated target / RTL flag moved. Re-running
   the animation on every visual pass would replay it endlessly because every pass
   triggers a label remeasure (Bold ↔ Regular flip changes width). Critically, the
   gate also tracks the `IsRtl` flag so a culture toggle (LTR ↔ RTL) — which only
   flips the sign of the floated X without changing the floated state — still
   triggers a fresh transform write. Without that, fields without a leading icon kept
   the stale TranslationX from the previous direction and the floated label drifted.

   **Floated-label overhang + `ReserveFloatingLabelClearance`.** Because the floated label
   sits centered on the top border (`FloatingLabelFloatedY = -11`, scaled by
   `FloatingLabelFloatedScale = 0.78`), it overhangs the box's top edge by a few dp. The box
   reserves NO space for that overhang, so when a field's top butts directly against another
   element (a bottom-sheet header, a card edge) the overhang spills above the field and is
   covered/clipped. In a normal form it floats into the empty inter-field spacing, so it's
   fine — the problem is only the "hard top edge" case (a bottom-sheet header, a card edge, or a
   **popup form**, where the first field butts against the popup's title divider — that one shipped
   with the label visibly clipped until `G9PopupHelper` turned the clearance on for its
   `G9TextEntry`/`G9Editor` fields). The opt-in `ReserveFloatingLabelClearance`
   bindable reserves `G9Metrics.FloatingLabelClearance` (6) as top padding on the field
   ROOT (not the box), so the floated label renders within the field's own bounds. Off by
   default (reserving it always would add dead space + break height-matched lanes);
   `G9SearchEntry` turns it ON. Full rule + the lane opt-outs: `08-UI-UX-Design-System.md` §4.

4. **A wrapper-level tap recognizer** on `_box` that calls `FocusTarget.Focus()` so
   taps anywhere in the box (over the floating label, over the outline edges, in the
   empty padding) reliably activate the inner Entry / Editor and bring up the keyboard.
   Subclasses with a focusable inner element (`G9TextEntry`, `G9Editor`) override
   `FocusTarget`. Picker-type subclasses (`G9Picker`, `G9ComboBox`,
   `G9DateTimePicker`) leave it null and use their own gesture recognizer to open
   the sheet — the wrapper tap is a no-op for them.

5. **A helper / counter footer** with consistent typography, RTL-aware padding, and an
   error state that swaps the outline color.

Because the outline is painted (not a platform `Border` background), the same control
renders correctly on **any** parent background — green map tiles, photo backgrounds,
white surfaces, dark cards. There is no `LabelBackgroundColor` to tune.

### 4. `G9FieldSlotLayout` — one struct, all the icon math

`G9FieldSlotLayout` is the single source of truth for icon-column geometry shared by
every outlined field. Constructed once per `OnApplyVisuals` from
`(HasLeadingIcon, HasTrailingIcon, IsRtl, ForceTrailingIconRight)`, it resolves:

- **Physical Grid columns** for the leading and trailing icons. Column 0 is always
  physical-left, column 2 is always physical-right (the box's `FlowDirection` is locked
  to `LeftToRight`). In LTR the leading icon goes to column 0; in RTL it goes to column 2
  so the icon appears at the physical-right edge where reading starts.
- **Inner-content padding** — `0` on the physical side that has a visible icon (the
  icon's symmetric margin already provides the inner-text-side gap), and the full
  `InputHorizontalPadding` on bare sides so the inner text aligns with the corner inset.
  The padding is computed in **physical** coordinates (left / right) because the parent
  Grid's `FlowDirection` is locked to LTR. In RTL the leading icon occupies physical-right
  (column 2), so the method queries `ResolvePhysicalLeadingColumn()` /
  `ResolvePhysicalTrailingColumn()` to determine which physical side has an icon.
  `ForceTrailingIconRight` is respected — when active, the trailing icon is always on
  physical-right regardless of flow direction.
- **Floating-label rest TranslationX** — slides the label past the icon column when an
  icon is present, sign-flipped in RTL.
- **Floating-label floated TranslationX** — the small breathing-room offset
  (`FloatingLabelFloatedExtraX`) that pushes the floated label away from the rounded
  corner curvature.

`ForceTrailingIconRight` (used by passwords / barcode entries) keeps the trailing icon
pinned to physical-right regardless of flow direction.

### 5. Drawables are dedicated files

Anything that draws on a `GraphicsView` lives in its own file:

| Control | Drawable file |
|---|---|
| G9Button | `Shared/G9RippleDrawable.cs` (ripple + inset highlight) |
| G9OutlinedField (every input) | `Shared/G9OutlinedFieldDrawable.cs` |
| G9RangeSlider | `G9RangeSlider/G9RangeSliderDrawable.cs` |
| G9ProgressBar | `G9ProgressBar/G9ProgressBarDrawable.cs` |
| G9Switch | `G9Switch/G9SwitchDrawable.cs` |
| G9DateTimePicker drum overlay | `G9DateTimePicker/G9DrumColumnDrawable.cs` |

The drawable holds plain data (state colors, progress, geometry) and a single `Draw`
method. The control's main `.cs` file orchestrates layout, gestures, and animation
commits. This split mirrors the pattern in `CustomizedMenu/G9TabBar` and keeps each
file focused.

### 6. Metrics, colors, and strings are centralised

Three shared static classes carry every magic number. Per-control files only consume
the named tokens, never raw literals.

- **`G9Metrics`** — radii, control heights, padding presets, icon sizes,
  animation durations, font sizes. **Tunable from a single file.** The most relevant
  tokens for the outlined-field family:

  | Token | Meaning |
  |---|---|
  | `InputHorizontalPadding` | Wall-side gap for bare (icon-less) edges. Default 14. |
  | `InputIconSize` | Icon glyph size. Default 20. |
  | `InputIconStartMargin` | Symmetric horizontal margin around the LEADING icon — appears identically on the wall side and the inner-text side. Default 8. Set to 0 for a flush icon. |
  | `InputIconEndMargin` | Same as above, for the TRAILING icon. Default 8. |
  | `InputLabelLeadingIconOffset` | Derived: `InputIconSize + 2 × InputIconStartMargin − InputHorizontalPadding`. The X offset that slides the floating label past the icon column at rest. Auto-updates when you change the start margin. |
  | `FloatingLabelFloatedExtraX` | Extra TranslationX applied to the floated label so it doesn't sit flush against the corner curvature. Default 8. |
  | `FloatingLabelFloatedY` | TranslationY applied to the floated label. Default −11 (places the label centre exactly on the top edge of the box, Material 3 convention). |
  | `FloatingLabelFloatedScale` | Scale of the floated label. Default 0.78. |
  | `FloatingLabelDurationMs` | Float animation duration. Default 160. |
  | `OutlineNotchTextGap` | Horizontal gap inside the notch on each side of the visible label text. Default 8. Tune to widen / tighten the empty stroke showing inside the notch. |
  | `OutlinedFieldStrokeThickness` | Resting outline thickness. Default 1.5. Used by empty and filled-valid rest states. |
  | `OutlinedFieldEmphasisStrokeThickness` | Focus / error / status outline thickness. Default 2.5. |
  | `OutlinedFieldFocusHaloThickness` | Inner halo thickness for focused fields. Default 6. Only applied when the field opts in via `ShowFocusHalo="True"` (halo is off by default). |
  | `FocusRingOpacity` | Alpha applied to the focus halo. Default 0.22. Only applied when `ShowFocusHalo` is enabled. |

  **Icon glyph sizing** — the `CreateIcon` factory sets `WidthRequest = InputIconSize`
  on every icon branch (emoji Label, MauiIcon, cached bitmap). Only the emoji branch
  omits `HeightRequest` — emoji characters at a given FontSize render taller than their
  em-square (~27 dp for FontSize 20), so a fixed HeightRequest clips the bottom of
  the glyph. The icon host Grid has `VerticalOptions = Center` and
  `IsClippedToBounds = false`, so the taller emoji is vertically centred within the
  52 dp field with ~12.5 dp clearance on each side. The bitmap branch wraps the
  cached image in a `Border` for rounded-corner masking — see §12c for the
  FFImageLoading routing rule.

  **Floating label height** — `FloatingLabelHeight = 22` (up from 18) so the rest-state
  placeholder fully contains Persian / Arabic descenders at FontSize 15 without clipping.

  **`const` vs `static readonly`** — these tokens are `const` on purpose. The C# compiler
  inlines `const` values at the call site at compile time; that's actually what we
  want here, because every consumer recompiles in the same build pass and the
  inlined value lands deterministically. (The downside — that hot-reload sessions
  don't pick up new values without a full rebuild — is the same downside Material's
  own design tokens have.)

- **`G9Colors`** — G9 alpha recipes (inset highlight, switch off/on tracks, etc.)
  plus helper methods for common visual tokens (`Round(radius)`,
  `BuildSolidOrGradient(color, useGradient)`). There is deliberately **no** `BuildShadow`
  — see §0.
  // TODO (palette step): every color in this file moves into `G9Palette` once the
  palette migration runs.

- **`G9Strings`** — localized strings used only inside control internals
  (`Done`, `Search`, `No results`, `Hour`, `Minute`, etc.). Resolves from
  `G9StringResources.AppControls{Suffix}` with sensible English fallbacks.

### 7. Bindable properties via `[AutoBindable]`

Every public property is generated from a private field with the
`Maui.BindableProperty.Generator` `[AutoBindable]` attribute. Default values are set in
the constructor; change handlers fire `OnVisualChanged` (or a more specific handler)
which calls `RequestVisualUpdate()`. Two-way bindings are declared explicitly with
`DefaultBindingMode = nameof(BindingMode.TwoWay)`.

Convention:

- **One-way "visual only"** properties → `[AutoBindable(OnChanged = nameof(OnVisualChanged))]`
- **Two-way value** properties → `[AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnValueChanged))]`
- **Pure data** (not visually reflected) → `[AutoBindable]`

### 8. Re-entrancy guards on every value setter

Two-way value setters guard against infinite loops:

- `G9RangeSlider.NormalizeValues` runs under a `_normalizing` flag so the setters that
  re-fire `OnValueChanged` exit early.
- `G9TextEntry` / `G9Editor` use `_syncingText` so the `Entry`/`Editor` and the wrapper
  never echo each other's text into a recursion. Platform property writes in
  `ApplyEntryProperties` / `ApplyEditorProperties` also have explicit equality checks
  (`if (entry.IsPassword != target) entry.IsPassword = target;`) so a focus event that
  re-runs the apply pass doesn't re-write platform widget swaps (especially `IsPassword`,
  which on WinUI swaps `TextBox` ↔ `PasswordBox` — a known AOT crash hazard).
- `G9Switch` only runs the toggle animation from `OnIsOnChanged` — `OnApplyVisuals`
  seeds `Progress` only on the very first apply (`_initialized` flag) so subsequent
  visuals refreshes never override the running animation.
- `G9OutlinedFieldBase` defers its first `OnApplyVisuals` until subclass field
  initializers run; if the inner content field is still null, the apply pass exits and
  the subclass triggers another pass when its constructor finishes.

These guards mean the controls survive aggressive setter cascades during XAML loading
without freezing the UI thread.

### 9. RTL is opt-in per layer

The controls respect `G9Culture.IsRtl` and the parent's `FlowDirection`:

- **Outlined fields physically swap the leading and trailing icons** across the box on
  culture flip via `G9FieldSlotLayout.ResolvePhysicalLeading/TrailingColumn`. The base
  re-assigns `Grid.Column` on the icon hosts every apply pass, so the leading icon
  appears on the physical-left in LTR and physical-right in RTL where reading starts.
- Floating labels anchor to the trailing edge (right in LTR, left in RTL) including
  `AnchorX` on the label so the floated scaling shrinks the label toward the notch.
- `ForceTrailingIconRight` opt-out for things like password / barcode where the trailing
  icon must always sit on the physical-right edge regardless of direction.
- `G9ComboBox` resolves selected value direction separately from the outlined shell:
  placeholder / floating label / icon slots follow culture, while selected single text
  and multi-select chips follow `ValueTextDirection` (`MatchParent` by default,
  `LeftToRight` for Latin codes inside RTL forms). The shared selection sheet accepts
  the same override for item rows only; header and search chrome remain culture-directed.
- `G9ComboBox` multi-select chips use a compact form of the `G9ChipGroup`
  selected-state recipe (primary gradient, on-primary bold text, optional item icon,
  primary stroke) with a lower radius and no trailing checkmark. Overflow `+N`
  chips stay neutral.
- `G9RangeSlider` forces its `GraphicsView` to `FlowDirection.LeftToRight` so the
  canvas pixel coordinates do not mirror — we then handle RTL inversion ourselves
  when mapping value → x and x → value. This is the only way to get consistent drag
  behaviour across MAUI Windows / Android / iOS; without it, Windows mirrors the
  canvas and combined with our inversion produced reversed dragging. The drawable
  paints in pure LTR order and swaps the visual min / max edge labels under RTL.
- **Any drawable that does its OWN RTL math must pin its `GraphicsView` to `LeftToRight`,
  for the same reason.** An inherited RTL flow direction mirrors the WHOLE canvas, so the
  control flips twice and lands back where it started — except glyphs, which come out
  BACKWARDS. `G9Switch` shipped with exactly that bug: its on-state track check painted
  as a reversed tick in Persian (the thumb's double flip was invisible, the glyph's was not).
  Fixed by pinning the switch's `GraphicsView` to `LeftToRight` so
  `G9SwitchDrawable.IsRtl` is the single source of direction. Rule of thumb: **a canvas
  either mirrors itself or the drawable mirrors — never both**; and a check / tick is a glyph
  that must never mirror at all.
- `G9TabView` locks its bar / scroll view / inner cells host to `LeftToRight` so the
  pill's `TranslationX` and cell `X` are always in physical-pixel coordinates. RTL
  ordering is achieved by reversing the items iteration order when populating cells
  (no manual `Grid.Column` mirroring). Cell row content uses
  `FlowDirection.MatchParent` so the inner icon + label + badge order follows the
  page's reading direction.
- `G9Separator` uses a similar pattern: root grid locked to LTR for column-index
  determinism, with a `PhysicalAlignment` resolver that flips which physical column
  the title sits in for RTL alignment. The title host's own `FlowDirection` follows
  the culture so the icon lands on the leading side of the title in both directions.

### 10. Native chrome and intrinsic content padding are stripped

Every control hosting a platform `Entry` / `Editor` sets `StyleId = "no-underline"`
(or the equivalent constant `G9PlatformConfig.NoUnderlineStyleId`). The shared
class **`G9PlatformConfig`** owns the per-platform handler tweak that the design
contract depends on, registered once at app startup from
`MauiAppBuilderExtensions.AddCustomizedOrNewHandlers`:

| Platform | What gets stripped |
|---|---|
| **Android** | EditText background drawable + tint list, intrinsic horizontal padding, `compoundDrawablePadding`. Re-applied on focus events because Android replays the EditText drawable on focus state changes. |
| **iOS / macOS Catalyst** | `BorderStyle = None` (also removes the rounded-rect / line text inset), background colour, layer border. Re-applied on focus events. |
| **Windows** | The `TextControlBorderThemeThickness*` resources (focus underline), the border brushes, the `UseSystemFocusVisuals` flag, and **`TextControlThemePadding`** — the hidden 12-px content-area padding that survives `BorderThickness=0` and `Padding=0` because it's baked into the `ScrollViewer` template part. Without this override the inner text appeared asymmetrically far from a leading icon (8 px on the wall side, ~20 px on the inner-text side). **Deferred to the platform `TextBox`'s `Loaded` event, not applied during `SetVirtualView`** — the `Resources[...]` writes need a live `XamlRoot`; running them too early throws `COMException 0x80070580` that the outer try/catch swallowed (see §15 W3). Each write is individually try/catched and the whole resource block is skipped while `XamlRoot is null`. NOT re-applied on focus events — mutating these resources during the platform's own focus-event dispatch reliably crashes AOT with `ExecutionEngineException`. **The background fill in every state (`TextControlBackground` / `*PointerOver` / `*Focused` / `*Disabled`) is flattened to transparent at the WinUI Application scope in `Platforms/Windows/App.xaml`, NOT per-instance** — the `Focused` visual-state storyboard resolves the `ThemeResource` against the app dictionaries, so a per-instance override of the focused brush does not win (see §15 W10 / W11). The startup auto-focus / page-jump is fixed structurally via the `ScrollViewHandler` `IsTabStop` mapping, not here (see §15 W9). |


Why does it live in the G9 folder? Because the design contract is the new control
system's responsibility — `G9OutlinedFieldBase` sets the `StyleId` on every Entry /
Editor it owns, and the platform strip is what actually makes the visible icon-to-text
gap match the explicit `InputIconStartMargin` / `InputIconEndMargin` metric. Keeping
the platform tweak alongside the C# layout / drawable code means anyone touching the
new controls finds the platform contract in the same place.

Any remaining caller that sets `StyleId="no-underline"` directly on a plain MAUI Entry
keeps working because the mapper just looks at `StyleId`. Search/text inputs across the
app now use `G9TextEntry` / `G9SearchEntry`, which set this `StyleId` internally
and pick up the same underline-stripping behaviour for free.

### 10b. A press MUST fire in every situation (mandatory for every new control)

A tap that "works most of the time" is a BUG, not polish. When you author a new control — or make an
existing one interactive, or place one over a new surface — you must be able to say WHY a press fires
in every case. The full checklist is the design-system rule book (`AiGuides/08-UI-UX-Design-System.md`
§9b); the G9-specific contract is:

- **The gesture goes on the control root, so the control's MEASURED BOUNDS are the hit area.** What the
  control paints is irrelevant to hit-testing. A `G9IconButton` drawn at 42 has a 42dp target,
  under the 44dp floor — presses landing a few dp off simply fall through. Expose / set
  `MinimumTouchTarget` (`G9LayoutMetrics.MinTouchTarget` = 48) so the control MEASURES big while
  still painting small; the content stays centred, so the slop is invisible. Never enlarge the drawn
  control to fix a missed tap, and never ship an interactive control that measures below the floor.
- **Every child visual is `InputTransparent`.** Icon hosts, labels, spinners, ripple layers, corner
  badges — the finger must reach the gesture owner from anywhere inside the control. (`G9IconButton`'s
  `_iconHost` / `_rootGrid` and `G9CornerBadge`'s badge + label are already transparent — which is
  why a missed tap there is NEVER "the icon blocking it", however much it looks like that.)
- **One gesture owner.** Do not stack a recognizer on both the root and an inner surface, and do not
  rely on a parent's recognizer to catch what a child let through.
- **Verify by pressing the EDGES of the control**, not the middle. A too-small target only fails at its
  edges — which is exactly why the defect reads as "intermittent" in a bug report.

### 11. Animations run on the compositor

- Floating-label animation interpolates `TranslationX`, `TranslationY`, and `Scale`
  together via a single `Animation` so the three transforms stay in sync. Smooth
  slide between rest and floated; no jump.
- `G9Button` press uses `ScaleToAsync` (no layout invalidation).
- `G9Switch` thumb morph uses a single `Animation` that writes `Progress` and calls
  `Invalidate` — no layout passes.
- `G9TabView` slides the active pill via a single `Animation` callback that
  interpolates BOTH `TranslationX` and `WidthRequest` together over 240ms (`CubicOut`).
  Adjacent cells of any width transition cleanly with no "stretch then slide"
  decomposition. Pill widget is built once and never recreated — its colour brushes are
  stable instances whose `.Color` we mutate per frame.
- `G9ChipGroup` uses a destruction-free color crossfade — stable
  `LinearGradientBrush` (with three `GradientStop`s mutated per frame) for the
  background and a stable `SolidColorBrush` for the stroke. Icon `View` is built once and
  never destroyed; color updates mutate `Label.TextColor` / `MauiIcon.IconColor` on
  existing instances. Only the toggled chip animates — every other chip stays untouched.
- `G9RangeSlider` updates the drawable and calls `Invalidate` per pointer event —
  no layout invalidation.
- `G9DateTimePicker` drum picker uses a `ScrollView` with a 16ms-tick settle
  detector — when `ScrollY` hasn't changed for 2 consecutive ticks the snap fires
  with a 169ms animated `ScrollToAsync`. No `Task.Delay`-based debounce that previous
  versions had (which compounded to 1–2s delays). The `Today` button animates every
  column in parallel via `Task.WhenAll` to avoid a multi-hundred-ms freeze.

Per-frame allocations are zero in steady-state for every control.

### 12. Destruction-free animations are mandatory

Several controls (`G9ChipGroup`, `G9TabView`) used to recreate the icon `View` at
the end of selection animations. This caused a 1-frame visible flash because the new
platform view rendered with its default color before the mapper applied the explicit
`IconColor` / `TextColor`. The same pattern caused brush-type pops at t=1 when
swapping `SolidColorBrush` (during animation) → `LinearGradientBrush` (final state).

The fix that every animated state-change control now follows:

1. **Build widgets once** in the chip / cell builder. Never recreate icon Views,
   labels, or badges in response to a state change. Mutate properties on existing
   instances.
2. **Stable brush instances**. Mutate `SolidColorBrush.Color` or `GradientStop.Color`
   per animation frame instead of allocating new brushes. The platform handler
   observes a cheap "color changed" notification rather than an expensive
   "brush replaced".
3. **No brush type swaps mid-animation**. Pick one brush type at construction time
   (e.g., `LinearGradientBrush` with stops collapsed to the same color for the
   "flat" state) and keep it for the lifetime of the widget.
4. **Never animate a shadow** — there are none to animate (§0). A state change is carried
   by background, stroke, text colour, scale and opacity, all of which are cheap.
5. **Live color tracking** on each binding so a fresh animation that starts
   mid-flight uses the current visible color as its "from" — no jump back to the
   resting color before re-interpolating.
6. **Cancellation-safe `finished` callbacks** — check the `cancelled` flag so the
   old animation's final frame doesn't overwrite the newer animation's in-flight
   frames.

#### 12a. Outlined-field icon slots cache the default `MauiIcon`

`G9OutlinedFieldBase` extends principle 12 to its leading and trailing icon hosts.
Earlier the host swapped icon child views on every signature change — for the common
case of a Material-icon trailing slot this looked fine, until a state transition
crossed a non-icon view. The two reproduced bugs:

- **Spinner ↔ glyph swap on `IsTrailingBusy` (G9BarcodeTextEntry).** When the
  `ScanBusy` state cleared, the `ActivityIndicator` was removed and a fresh
  `MauiIcons.Core.MauiIcon` was attached. The new MauiIcon's platform handler
  needed one frame to load the glyph from the embedded MaterialIcons font — that
  frame painted as a tofu rectangle.
- **Mic ↔ MicOff swap on `G9SearchEntry`.** Same root cause via a different
  path: the `ResolveTrailingIconSignature` returned different strings for idle /
  listening, so the base detached & re-attached a freshly-built MauiIcon
  on every mic tap. One-frame tofu rectangle each toggle.

The base now caches **two long-lived children** in each icon host:
`_trailingDefaultMauiIcon` (the Material-icon view for the default `TrailingMaterialIcon`
path) and `_trailingBusyIndicator` (the spinner). Both remain attached for the
lifetime of the field. State transitions toggle `IsVisible` and mutate properties on
the cached instances:

- `Idle → ScanBusy`: spinner `IsVisible = true`, cached MauiIcon `IsVisible = false`.
  The MauiIcon stays in the visual tree with its platform handler alive.
- `ScanBusy → Idle / Accepted / Error`: cached MauiIcon's `Icon` is mutated
  (`QrCodeScanner` ↔ `CheckCircle` ↔ `Info`), `IconColor` is mutated, then
  `IsVisible = true`. The font is already rasterized from the previous lifetime —
  the next paint frame already shows the glyph at the correct color, no tofu.

Subclasses that return a custom `View` from `ResolveTrailingIcon` (e.g. G9TextEntry's
clear-button / password-toggle, G9SearchEntry's mic) still flow through
`SetIconHostContent` with their own caching strategy — that path is unchanged. The
caching only kicks in for the default `TrailingMaterialIcon` / `LeadingMaterialIcon`
path, which is where every outlined field bottom-feeds.

`UpdateIconColor` was tweaked to skip hidden children so a focus / blur color update
only writes to the visible icon, leaving the dormant cached one untouched until it
becomes visible again.

#### 12c. Bitmap icons go through FFImageLoading (`G9CachedImage`), never bare `Image`

Every G9 control that exposes `ImagePath` / `ImageSource` icon properties
(`G9Button.LeadingImagePath` / `TrailingImagePath`, `G9IconButton.ImagePath`,
`G9NavCard.IconPath`, `G9ChipGroup.IconPath`, `G9Separator.IconPath`,
`G9TabView`'s tab item `ImagePath`, …) routes them through `G9Visuals.CreateIcon`,
which is the **single bitmap-icon factory for the entire design system**. The factory
**must** wrap the bitmap in a `G9CachedImage` (the FFImageLoading wrapper) — never
a bare `Microsoft.Maui.Controls.Image`.

**Why.** A plain `Image` decodes its source on the UI thread on every state-change rebuild
(focus, hover, theme flip, badge appears, button enters loading), with no cache and an
animated platform fade-in. Reusing an icon across many cells (chip groups, tab bars,
nav cards) re-decodes the same source N times. Three observable bugs traced back to this
before the rule was made explicit:

- **Per-paint icon flash on Android.** Tapping a button caused the icon view to be rebuilt
  with a fresh `Image`; the platform fade-in animation made the icon dim then re-pop on
  every press.
- **Visible 1-frame stall on Android while a chip group with PNG icons selected**. Each
  per-frame mutation of the selected chip rebuilt the host once and paid the bitmap
  decode again on the UI thread.
- **GC pressure on Windows during list scrolling**. Identical PNGs in many `G9NavCard`
  rows kept allocating fresh decoded bitmaps because nothing held onto them.

**The factory's image branch.** `G9Visuals.CreateIcon` resolves the
`(string? imagePath, ImageSource? imageSource)` pair through `ResolveImageSource` to a
`Microsoft.Maui.Controls.ImageSource`, then constructs a `Border` (for the rounded-corner
mask) whose `Content` is a `G9CachedImage` configured with the project-standard
performance recipe used everywhere else in the app (mirrors `FarmsCollectionView.xaml` and
the 01-Project-Rules.md "app image" rule):

| Setting | Value | Why |
|---|---|---|
| `CacheType` | `All` | In-memory + on-disk caches both warm — same bitmap reused across cells. |
| `DownsampleToViewSize` | `true` | Decoder produces an icon-size bitmap; a 2048-px source is never loaded full-resolution to render at 22 dp. |
| `BitmapOptimizations` | `true` | Platform-specific decode fast paths. |
| `FadeAnimationEnabled` | `false` | Instant paint; no platform alpha tween that looks wrong on a button press / focus halo update. |
| `LoadingDelay` | `169` | Defers the decode by ~169 ms so a transient state (button quickly tapped through, focus halo crossed in passing) does not pay for a decode the next state instantly invalidates. |

**Consumers don't bypass this.** None of `G9Button`, `G9IconButton`, `G9NavCard`,
`G9ChipGroup`, `G9Separator`, or `G9TabView` constructs an `Image` directly —
they all call into `G9Visuals.CreateIcon` and inherit the cached path. Only the
`G9SwipeView` action panes use a bare `Image` with `FontImageSource` (Android tofu-box
workaround for `NativeSwipeItemView` font-resolution failures, see §15 A2). That is a
**glyph synthesis path**, not a bitmap one — `FontImageSource` rasterizes a font glyph
on the fly, so there is nothing to cache and FFImageLoading is the wrong tool for the job.
Do NOT reintroduce a bare `Image` for bitmap sources anywhere else in the design system —
add the property to `G9Visuals.CreateIcon`'s call chain instead.

**Checklist when you add a new G9 control with a bitmap-icon property.**

1. Expose `ImagePath` / `ImageSource` (string + ImageSource pair) as `[AutoBindable]` fields.
2. In `OnApplyVisuals`, render the icon by calling `G9Visuals.CreateIcon(emoji, materialIcon, imagePath, imageSource, color, size)`. Do not construct an `Image` yourself.
3. If the new control overlays the icon on a non-rounded background, leave `imageRadius` at its default; if you need a custom radius, pass it through.
4. Cache long-lived icon hosts following §12a where state changes can swap the icon (e.g. between an `ActivityIndicator` and a `MauiIcon`). The cached-bitmap path adds zero requirements on top of §12a.
5. If your control sources its image from a remote URI, FFImageLoading already handles the download, retry, and disk cache — pass the URL string directly through `ImagePath`. Do not pre-resolve the bytes and feed an `ImageSource.FromStream`.

#### 12b. Corner notification badges share one helper — `G9CornerBadge`

Multiple controls overlay a small notification badge (an empty dot or a count pill) on
the top-trailing corner of an icon: `G9IconButton` (toolbar action counts /
"filters active" dot) and `G9NavCard` (icon-chip count / "has updates" dot). **That
badge is implemented once in `Shared/G9CornerBadge.cs` and both controls
delegate to it** — there is no per-control badge geometry anymore.

`G9CornerBadge` wraps the host view (icon chip / button frame) and owns:

- **Corner-centred for any width.** The badge's CENTRE lands exactly on the host's
  top-trailing corner — half over the host, half outside — regardless of count width.
  A wide "1299" / "+99" straddles the corner symmetrically instead of growing inward
  across the icon. Achieved by pinning the host to the stack's bottom-leading corner,
  docking the badge to the top-trailing corner, and sizing the stack to
  `host + half-badge` (computed per update). There are **no negative margins**, so the
  whole badge stays inside the stack bounds — which is why it isn't clipped on Android
  (Android clips children drawn outside their parent; this was the original bug where
  the count badge rendered cut off / grew inward on `G9IconButton`).
- **RTL-aware.** The stack inherits the ambient `FlowDirection`, so the badge
  auto-mirrors to the host's top-leading corner in RTL. The count text itself can flip
  RTL ("99+" → "+99") via the `mirrorTextInRtl` flag, exposed on each control as the
  `MirrorBadgeTextInRtl` bindable property (default true).
- **Centred number.** The count label uses `Fill` layout + `LineHeight 1`, and the badge
  `Border` has `Padding 0`, so the digit sits dead-centre in the circle.

> **Any change to badge appearance or geometry MUST be made in `G9CornerBadge`** so it
> applies to every control that uses it — do not re-implement the badge per control.
> The **`G9TabView` badge is the one intentional exception**: it uses a different,
> tab-specific device (inline in the cell row, not a corner overlay), so it does NOT use
> `G9CornerBadge` and is not covered by this rule.

### 13. Tap-outside-to-dismiss-keyboard is wired at the page base

Every input in this folder (`G9TextEntry`, `G9Editor`, `G9SearchEntry`,
`G9BarcodeTextEntry`, `G9Picker` / `G9ComboBox` / `G9DateTimePicker` when their inner
search field is focused) opens the OS soft keyboard on focus. Without an explicit
dismisser the user is stuck — tapping the page body, tapping a button, or even tapping
a different input keeps the keyboard up until the platform's "back" / "return" key is
pressed. That breaks the form-fill flow on every mobile device.

G9 inputs do NOT own Android window soft-input policy. The app default is the
activity-level `AdjustResize` configured in `Platforms/Android/MainActivity.cs`; a flow
that needs a different keyboard avoidance behavior must apply it at the window/sheet
boundary and restore the default when it closes. Use
`G9KeyboardHelper.UseAndroidPanSoftInputMode()` for the existing shared scoped-Pan helper;
it returns an idempotent restore action. Current scoped exception:
`StateTransitionCommentSheetContentView` holds that helper action only while the mandatory
state-machine comment sheet is open.

**Why we don't use the built-in `ContentPage.HideSoftInputOnTapped`.** MAUI ships
that property and it works — when the page is reached via direct MAUI navigation. The
`HideSoftInputOnTappedChangedManager` only registers a page after its `NavigatedTo`
event fires. Our pages are managed by Nalu Shell navigation, which doesn't raise
`NavigatedTo` for Shell-routed pages, so the manager never wires up the underlying
`Window.DispatchTouchEvent` listener and the property silently does nothing.

**The replacement: `TapOutsideKeyboardDismisser`** in
`Shared/Helpers/TapOutsideKeyboardDismisser.cs`. It exposes a single static
`Attach(Page)` method that returns an `IDisposable` lifetime token. The token is
created in `OnAppearing` and disposed in `OnDisappearing`, so the platform listeners
live exactly as long as the page is on screen.

| Platform | Hook | Behaviour |
|---|---|---|
| **Android** | `MainActivity.TouchDispatched` event raised from `Activity.DispatchTouchEvent`. | On `ACTION_UP`, hit-test the touch point against the focused `EditText`'s on-screen bounds. Inside the focused field → ignore (caret placement / selection). On a different `EditText` → ignore (Android moves focus, IME stays). Anywhere else with the IME visible → call `InputMethodManager.HideSoftInputFromWindow(token)` plus `View.ClearFocus()`. IME visibility is checked via `WindowInsets.Type.Ime()` on Android 11+ (API 30+) and a height-comparison heuristic on older releases. |
| **iOS / Mac Catalyst** | `UITapGestureRecognizer` on the key window with a `UIGestureRecognizerDelegate` whose `ShouldReceiveTouch` returns false when the touch lands on a `UITextField`, `UITextView`, or any ancestor that `CanBecomeFirstResponder`. | On a recognized tap calls `window.EndEditing(true)`, which dismisses the keyboard and unfocuses the input. `CancelsTouchesInView = false` so the underlying view (button / chip / card) still receives its tap normally. |
| **Windows** | `PointerPressed` on the page's WinUI content root, added with `handledEventsToo: true` so every press is seen even after a child handled it. | When the currently-focused element is a `TextBox` and the press did NOT land on a `TextBox` (checked by walking the press target's ancestor chain), move focus to a neutral focusable container so the input blurs. `FindFocusTarget` walks up from the focused `TextBox` to the nearest `ScrollViewer`'s `IsTabStop` `ContentPanel` (made tab-stoppable by the `ScrollViewHandler` mapper — see §15 W9) and calls `FocusManager.TryFocusAsync(target, FocusState.Pointer)`. This fires the `TextBox`'s `LostFocus` and MAUI's `Unfocused`. Three approaches were tried and rejected first: (1) MAUI's built-in `HideSoftInputOnTapped` only hides the soft keyboard, never removes focus (MAUI #21053, closed not-planned); (2) `FocusManager.TryMoveFocus(Next)` walks the tab order and focuses the NEXT input; (3) `TryFocusAsync` on `XamlRoot.Content` returns `Succeeded=False` because the root container isn't itself focusable. Only the focusable-container target works. |

**Why a desktop tap-outside-to-blur at all?** Windows has a hardware keyboard so there's
no soft keyboard to dismiss, but the WinUI `TextBox` keeps its visual focus state (cursor
+ focused outline) until focus explicitly moves elsewhere — clicking empty page chrome
does not blur it by default. Clearing focus on outside-click matches the mobile behaviour
and the focus visual stays consistent with our painted outline.


**Why the lifetime is OnAppearing → OnDisappearing.** Per-page rather than global so
each page only listens while it's on screen — no cross-page leaks, the next page wires
up its own listener with its own page reference. `OnAppearing` also guards against
duplicate wiring (modal push/pop replays) by disposing any token from a previous run
before allocating a new one.

> **Wiring it up in another project.** This dismisser depends on a base page class
> (`G9PageBase` in this codebase) that calls `Attach`/`Dispose` from the
> `OnAppearing`/`OnDisappearing` lifecycle hooks. If a downstream project doesn't have
> a shared `G9PageBase`, every `ContentPage` that hosts an input must duplicate the
> same wiring directly:
>
> ```csharp
> public partial class MyPage : ContentPage
> {
>     private IDisposable? _tapOutsideDismisser;
>
>     protected override void OnAppearing()
>     {
>         base.OnAppearing();
>         _tapOutsideDismisser?.Dispose();
>         _tapOutsideDismisser = TapOutsideKeyboardDismisser.Attach(this);
>     }
>
>     protected override void OnDisappearing()
>     {
>         _tapOutsideDismisser?.Dispose();
>         _tapOutsideDismisser = null;
>         base.OnDisappearing();
>     }
> }
> ```
>
> The Android implementation also requires a `MainActivity` that exposes a
> `TouchDispatched` event from its `DispatchTouchEvent` override (the same pattern we
> use for `ArcGISMapView` touch interception). On a project where `MainActivity` is
> the stock `MauiAppCompatActivity` without that event, the dismisser would need to
> hook a different touch source — either by overriding the activity itself or by
> attaching a `Java.Lang.Object.IDispatchTouchEvent`-style listener at the decor view.

### 14. Drag gestures inside a scrolling parent disallow intercept (Android)

Any control with a continuous drag gesture (`G9RangeSlider`,
`G9DateTimePicker` drum, `MapZoomIndicator` rail, the bottom sheet handle on
`G9SheetViewBorder`) sits inside a scrolling ancestor — a `ScrollView`, a
`CollectionView`, the page's `VerticalStackLayout` inside a `ScrollView`, etc.
Without an explicit signal, Android's parent steals the gesture as soon as the
touch path tilts a few degrees off horizontal: the drag aborts back to the
starting value, the user gets a frustrating "the slider keeps snapping back".

The fix is the standard Android handoff pattern. On the touch-down event, walk
every ancestor of the platform view and call
`parent.RequestDisallowInterceptTouchEvent(true)`. On touch-up / cancel, reset
to `false`:

```csharp
#if ANDROID
private void SetParentDisallowIntercept(bool disallow)
{
    if (_view.Handler?.PlatformView is Android.Views.View nativeView)
    {
        var parent = nativeView.Parent;
        while (parent is not null)
        {
            parent.RequestDisallowInterceptTouchEvent(disallow);
            parent = parent.Parent;
        }
    }
}
#endif
```

`G9RangeSlider`, `MapZoomIndicator`, and `G9SheetViewBorder.Android` all
follow this pattern. iOS / Mac Catalyst / Windows don't need an equivalent —
gesture priority on those platforms is resolved per-touch and the drag
captures the gesture early without an explicit signal.

> **Why a ScrollView is the typical culprit.** `ScrollView.onInterceptTouchEvent`
> measures the touch slop after every `ACTION_MOVE`. As soon as the cumulative
> vertical delta exceeds the slop (typically 8 dp), the `ScrollView` claims the
> touch sequence by returning `true` from `onInterceptTouchEvent` — at which
> point the child `GraphicsView` stops receiving move events. Calling
> `RequestDisallowInterceptTouchEvent(true)` from the child marks the parent's
> `mGroupFlags` so it short-circuits its own intercept logic until the next
> touch-down.

### 15. Platform crash & pitfall catalog

This is the consolidated list of platform-specific behaviours that have caused crashes,
hangs, render glitches, or build breaks while building these controls. **Read this before
adding any control that touches a platform handler, a `GraphicsView` gesture, a native
`SwipeView` / `TextBox` / `Editor`, or any background-thread work that ends up on the UI.**
Each entry is: *symptom → platform → root cause → rule → where it lives.*

The earlier principles (especially §2, §8, §10, §12, §12a, §14) describe these in context;
this section is the quick-reference index so a future task doesn't rediscover them the hard
way.

#### Windows / WinUI 3

WinUI 3 is by far the most crash-prone target because most failures surface as **stowed
exceptions** (`Microsoft.UI.Xaml.dll`, exit code `0xC000027B` / `STATUS_STOWED_EXCEPTION`)
that the .NET CLR cannot catch — no `try/catch`, no `AppDomain.UnhandledException`, no
`Application.UnhandledException` will see them. They appear only in the Windows Application
event log. When debugging one, use `procdump -e -ma` + `dotnet-dump analyze`, and a
temporary `AppDomain.CurrentDomain.FirstChanceException` logger that captures
`Environment.StackTrace` (the exception's own `StackTrace` is empty for WinRT throws at
first-chance time).

| # | Symptom | Root cause | Rule | Where |
|---|---|---|---|---|
| W1 | Process dies ~12-17 s after page render, idle, no user input, `0xC000027B` from `Microsoft.UI.Xaml.dll`. | `Microsoft.Maui.Controls.SwipeView` wraps WinUI `SwipeControl`, which self-destructs on WinUI 3 (and is touch-only on Windows per MS docs, so useless with a mouse anyway). | **Do not instantiate `SwipeView` on Windows.** `G9SwipeView` renders a custom mouse-draggable drag-to-reveal instead (action panes behind the card body + a `PanGestureRecognizer`), so swipe works on desktop without the native control. | `G9SwipeView` ctor + `RebuildWindowsPanes` / `OnWinPan` `#if WINDOWS` |
| W2 | One CPU core pinned ~100% on an idle page; UI eventually unresponsive. | `Dispatcher.Dispatch(SelfMethod)` re-queued while waiting for a layout value (e.g. cell `Width == 0`) never settles on WinUI — the dispatcher pump starves the layout pass. | **Never busy-wait for layout via `Dispatcher.Dispatch` self-recursion.** Subscribe to a one-shot `SizeChanged` that self-unsubscribes after the first non-zero measure. | `G9TabView.AnimatePillToSelected` |
| W3 | First-chance `COMException` HResult `0x80070580` ("Invalid window; it belongs to other thread") flooding from a control's first render; chrome strip silently never applies. | Writing `control.Resources["..."] = value` (a `ResourceDictionary` insert) goes through the WinRT marshaler, which needs a live `XamlRoot` / dispatcher. During `SetVirtualView` the platform `TextBox` isn't parented yet. A high volume of these can itself destabilize the render thread. | **Defer `Resources[...]` writes until `FrameworkElement.Loaded`, and skip them while `XamlRoot is null`.** Prefer setting theme resources once at App / Window scope over per-instance. | `G9PlatformConfig` (Windows TextBox chrome strip) |
| W4 | AOT crash (`ExecutionEngineException`) when stripping `TextBox` chrome during focus. | Mutating WinUI text-control theme resources while the platform control is draining its own GotFocus / LostFocus cycle corrupts the visual-state machine under AOT. | **Never re-apply the Windows chrome strip on focus events.** Strip once on attach only (Android / iOS re-strip on focus; Windows must not). | §10, `G9PlatformConfig` |
| W5 | AOT crash when toggling `IsPassword` / password mode. | On WinUI, `IsPassword` swaps the platform peer `TextBox` ↔ `PasswordBox`. Re-writing it when the value didn't change (e.g. a focus event re-runs the apply pass) re-triggers the swap and crashes AOT. | **Guard every platform property write with an equality check** (`if (entry.IsPassword != target) entry.IsPassword = target;`). Applies doubly to `IsPassword`. | §8, `G9TextEntry.ApplyEntryProperties` |
| W6 | Build error `CS1061 'TextBox' does not contain 'CaretBrush'`. | `CaretBrush` is WPF-only; WinUI 3 `TextBox` doesn't have it. | **Use `Foreground` (e.g. a transparent brush) to hide the caret on WinUI**, not `CaretBrush`. | `G9PinEntry.HideHiddenCaret` |
| W7 | Stowed-exception teardown from a **background thread** that calls a WinUI API with thread affinity. | WinUI APIs like `DeviceDisplay.MainDisplayInfo`, `AppWindow`, window/display lookups must run on the UI thread. Calling them off-thread throws `COMException 0x80070580` that becomes a stowed exception. | **Marshal any WinUI / `DeviceDisplay` / window API call to the UI thread** (`MainThread.InvokeOnMainThreadAsync`). Be wary of third-party libraries (telemetry, enrichers) that read display info from worker threads. | general rule |
| W8 | Parse error at XAML load: `Requested value 'X' was not found` for an enum attribute that clearly exists. | Stale source-gen / compiled-XAML artifacts after a `[AutoBindable]` or enum change; the XAML loader resolves the property against an outdated type. | **Clean `obj/` + `bin/` for the Windows TFM and full-rebuild** when an enum or `[AutoBindable]` property used in XAML changes and the value "disappears". | build hygiene |
| W9 | On startup, clicking anywhere makes the page scroll to and focus the first text input ("Farm name") the user never touched. | When a WinUI `ScrollViewer`'s content panel isn't a tab-stop, a click on non-focusable chrome has nowhere to put focus, so the focus manager walks to the first focusable descendant and the scroll viewer brings it into view. | **Set `IsTabStop = true` on the WinUI `ScrollViewer`'s inner `ContentPanel`** via a `ScrollViewHandler` mapper, giving the click a valid local focus target. (Documented .NET MAUI / WinUI behaviour; an earlier per-`TextBox` `GettingFocus`-cancel guard was tried and rejected — it didn't fix the jump and broke programmatic focus on the PIN's hidden Entry.) Also gives tap-outside-to-blur on desktop. | `G9PlatformConfig.RegisterWindowsScrollViewFocusFix` |
| W10 | Mouse hover lightens the input background. | The default `TextBox` template's `PointerOver` visual state swaps `BorderElement.Background` to `TextControlBackgroundPointerOver`. A per-instance `Resources` override applied during `SetVirtualView` threw pre-`XamlRoot` (W3) and was swallowed. | **Flatten `TextControlBackgroundPointerOver` (and `TextControlBackground`) to transparent at the WinUI Application scope (`Platforms/Windows/App.xaml`).** App-scope resolves reliably for the template's `ThemeResource` lookup. | `Platforms/Windows/App.xaml` |
| W11 | Focusing an input shows an opaque (white) background fill even though hover was fixed. | The `Focused` visual-state storyboard sets `BorderElement.Background` to `{ThemeResource TextControlBackgroundFocused}` (= `ControlFillColorInputActiveBrush`). A **per-instance** `Resources` override of that key does NOT win — the storyboard's `ThemeResource` resolves against the framework / app dictionaries, not the late instance-scope write (which is why the per-instance hover/`PointerOver` override worked but the focused one didn't). | **Override `TextControlBackgroundFocused` (and `*Disabled`) at the WinUI Application scope in `Platforms/Windows/App.xaml`.** | `Platforms/Windows/App.xaml` |
| W12 | A degenerate hidden `TextBox` (1×1 dp, `Opacity=0`, `IsHitTestVisible=false`, used by `G9PinEntry`) accepts keystrokes (`TextChanging` fires, platform `Text` grows) but the virtual MAUI `Entry.Text` never updates, so dependent UI never reacts ("typing does nothing"). | WinUI raises the synchronous `TextChanging` for such a box but suppresses the asynchronous `TextChanged`. MAUI's `EntryHandler` bridges platform→virtual text **only** from `TextChanged`, so the virtual `Entry` is never updated. | **Bridge from the platform `TextChanging` event yourself**: when `platformTextBox.Text` differs from the virtual `Entry.Text`, push it onto the virtual Entry (guarded by an equality check so it's a no-op if `TextChanged` ever does fire). | `G9PinEntry.HookWinUiTextBridge` |

#### Android

| # | Symptom | Root cause | Rule | Where |
|---|---|---|---|---|
| A1 | A continuous drag (slider, drum, rail, sheet handle) snaps back to its start as soon as the finger tilts off-axis. | The scrolling ancestor (`ScrollView` / `CollectionView`) claims the gesture once the touch slop is exceeded. | **On touch-down, walk every ancestor of the platform view and call `RequestDisallowInterceptTouchEvent(true)`; reset to `false` on up / cancel.** | §14, `G9RangeSlider`, `G9DrumColumn`, `MapZoomIndicator`, `G9SheetViewBorder` |
| A2 | Icon renders as a tofu box (`☐`) for one frame after a state change, or permanently inside a `SwipeItemView`. | A freshly-created `MauiIcon` / icon view needs a frame to load the glyph from the embedded font; inside some native renderers (e.g. `SwipeItemView`) the `MaterialIcons` family doesn't resolve at all and Roboto is substituted. | **Cache long-lived icon views and toggle `IsVisible` + mutate `Icon` instead of recreating** (§12a). Inside `SwipeItemView`, use `FontImageSource` built from the `[Description]` glyph + the enum type-name family, never a `(int)codepoint` cast. | §12a, `G9OutlinedFieldBase`, `G9SwipeView.ApplyActionVisuals` |
| A3 | `EditText` underline / background reappears after focusing the field. | Android replays the EditText drawable on focus state changes, undoing the one-time chrome strip. | **Re-apply the Android no-underline strip on focus / unfocus events** (opposite of the Windows rule W4). | §10, `G9PlatformConfig` |
| A4 | Drum / cell drag "jumps" to a neighbouring item on a slow drag; settle animation fights the finger. | Android dispatches `ACTION_DOWN` to a child cell (its tap recognizer claims it), so a parent-level down-detector misses it; the settle timer then fires under the finger. | **Track finger-down via a passive `IOnTouchListener` that reads `ACTION_DOWN`/`MOVE`/`UP`/`CANCEL` and returns `false`; short-circuit the settle timer while down.** Include `ACTION_MOVE` as a down-signal. | `G9DrumColumn` finger-down hooks |
| A5 | The floating placeholder of an outlined field (`G9TextEntry` / `G9Editor` / `G9Picker` / `G9ComboBox` / `G9DateTimePicker`) renders only its bottom half the moment the field flips to its disabled / readonly state (e.g. a login form going to its loading state on submit). The label in the rest state is unaffected. | Material 3 outlined-field convention sits the floated label HALF outside the box (`TranslationY = FloatingLabelFloatedY = -11dp`). When the field flips to disabled the base used to set `Opacity = 0.45` on `this`; on Android, MAUI's `Opacity` translates to `View.setAlpha(<1)`, which forces the view into a hardware-accelerated offscreen alpha layer **clipped to the view's own bounds**. The half of the label that physically extends above those bounds is the half that disappears. iOS / Windows / Mac don't do this offscreen clipping, so the bug only surfaces on Android. | **Never set `Opacity < 1` on `G9OutlinedFieldBase` (or any parent that contains the floating label).** Pin `Opacity = 1` on `this` and dim every child of `_box` EXCEPT the floating label individually (`_outlineView.Opacity`, `_innerRow.Opacity`, plus the helper / counter labels below the box). Dim the floating label through its `TextColor` alpha (multiply by the disabled dim factor). The visual reads as disabled while the label keeps its full bounds. **Do not collapse this back to a single `Opacity = ...` on the parent** — every G9 outlined field derives from this base, so the bug returns across all five controls. | §`G9OutlinedFieldBase.Refresh`, `G9OutlinedFieldBase.OnPaletteChanged` |
| A5 | `ObjectDisposedException` ("`PreviewView`") tears down the app after the camera scanner page closes. | The AndroidX Camera `PreviewView` is disposed while an async callback is still in flight. | **Recognize and swallow the known scanner-dispose exception** in the global handler rather than navigating to the error page. | `GlobalExceptionHandler.IsKnownScannerDisposeException` |
| A6 | The app freezes under touch and the system kills it with `ANR in …` / `Input dispatching timed out`. No managed exception, no tombstone, no OOM — the app's own log is silent. Preceded by `Skipped NNN frames`, `Davey! duration=…`, `[ANR Warning]onLayout time too long`. | A MAUI `Shadow` on a view whose platform background is not a `BorderDrawable` that can draw the shadow itself. `PlatformWrapperView.drawShadow` then falls to `drawShadowViaDispatchDraw`, which allocates an `ARGB_8888` bitmap the size of the view, rasterizes the whole subtree into it, and box-blurs it through a `BlurMaskFilter` **in software on the UI thread, every draw pass**. Cost scales with view area × blur radius, so a large or deeply-nested shadowed view (a full-screen sheet host) alone can blow the frame budget. | **Never set a `Shadow` — see §0.** Express elevation with `Stroke` + `StrokeShape`, a surface-tone step, or a gradient. If a concave silhouette truly needs a soft edge, paint it on SkiaSharp / `IDrawable` (render thread), as `G9TabBarShadowView` does. | §0, post-mortem in `../../../AiGuides/02-Crash-And-Build-Hazards.md` |

#### iOS / Mac Catalyst

| # | Symptom | Root cause | Rule | Where |
|---|---|---|---|---|
| I1 | A thin focus border / rounded-rect inset reappears on the text field when focused. | iOS re-introduces the layer border / `BorderStyle` inset on focus. | **Re-apply `BorderStyle = None` + clear layer border on focus events** (same as Android, opposite of Windows W4). | §10, `G9PlatformConfig` |
| I2 | Voice search silently fails / raises `VoiceFailed` in a Persian (`fa-IR`) session. | iOS has no Persian acoustic model for `SpeechToText`; Android (Google recognizer) does. | **Treat `VoiceFailed` as expected on iOS for unsupported locales** — surface a graceful message, don't crash. | `G9SearchEntry` |
| I3 | Layout overlaps the notch / home indicator, or safe-area padding is wrong after rotation. | Safe-area insets must be read after the handler is live and re-read on size changes. | **Read `SafeAreaInsets` on the iOS lifecycle hook and recompute** rather than caching once. | `G9PageBase` (`ApplyIOSPaddingAsync`) |

#### Cross-platform (all targets)

| # | Symptom | Root cause | Rule | Where |
|---|---|---|---|---|
| X1 | `IServiceProvider has been disposed` / `ObjectDisposedException` crash when a page closes while a visual update is still queued. | A dispatcher-queued `OnApplyVisuals` runs after the handler is gone. | **Set an `_isDestroyed` flag on `HandlerChanging→null`; queued passes exit early; wrap platform writes in defensive try/catch.** | §2, `G9ControlBase` |
| X2 | UI thread freezes during XAML load under aggressive setter cascades. | Two-way value setters echo each other into infinite recursion. | **Guard two-way setters with a `_syncing` / `_normalizing` flag and equality checks.** | §8, `G9RangeSlider`, `G9TextEntry`, `G9Editor`, `G9Switch` |
| X3 | 1-frame color flash / brush-type "pop" at the end of a selection animation. | Recreating icon views or swapping `SolidColorBrush` ↔ `LinearGradientBrush` mid-animation. | **Build widgets once; mutate stable brush instances per frame; never recreate views or swap brush types mid-animation.** | §12, `G9ChipGroup`, `G9TabView` |
| X4 | Soft keyboard stays up (mobile) / input keeps focus (Windows) after tapping elsewhere on the page. | MAUI's `HideSoftInputOnTapped` only registers pages that raise `NavigatedTo` (Nalu Shell pages don't), and even when it does fire on Windows it only hides the soft keyboard without removing focus (MAUI #21053). | **Attach `TapOutsideKeyboardDismisser` from the page base `OnAppearing` / `OnDisappearing`.** On Windows it hooks `PointerPressed` and moves focus to the nearest `ScrollViewer`'s `IsTabStop` `ContentPanel` via `FocusManager.TryFocusAsync` (not `TryMoveFocus`, not `XamlRoot.Content`). | §13, `TapOutsideKeyboardDismisser` |
| X5 | An ink ripple hosted inside a padded container only paints the centre region / is clipped to the inner box (visible on WinUI / iOS / Mac; correct on Android). | A padded layout arranges the ripple `GraphicsView` within the content rect, so its `dirtyRect` — and the ripple's max radius (computed from the rect diagonal) — shrinks by the padding. Android's canvas overflows the padding so it's already correct. | **Counter the host padding with an equal *negative* margin on the ripple `GraphicsView`** so its arranged rect equals the full host bounds. Exempt Android (margin 0) — adding the negative margin there would over-expand it. Same root cause as the G9Button `Padding`-on-row fix (§11). | `G9OutlinedFieldBase.ExpandRippleToHost`; `G9Button` (`_frame.Padding = 0` + `_row.Margin = padding`) |

#### General debugging playbook for a WinUI stowed-exception crash

When a Windows-only crash has no managed stack trace:

1. Reproduce headless: launch the built `.exe` detached, poll `Get-Process … Responding` / `CPU`, and watch the Application event log for the `0xC000027B` "Application Error" entry (faulting module + offset).
2. Attach a **temporary** `AppDomain.CurrentDomain.FirstChanceException` logger that appends `Environment.StackTrace` to a file (the exception's own `StackTrace` is empty for WinRT throws at first-chance). Remove it before committing.
3. If first-chance logging shows nothing before the crash, it's an **unmanaged** WinUI failure — bisect the visual tree. Replace the page with a minimal `G9PageBase` shell, confirm it survives, then add control groups back in batches; when a batch crashes, remove it and add its members back one at a time until a single control reproduces it.
4. Capture a full dump with `procdump -accepteula -e -ma <pid> <dir>` and read it with `dotnet-dump analyze` (`clrstack -all`) to confirm the throwing frame.
5. Clean up all probes, dumps, and first-chance loggers before committing — they live in the gitignored scratch space only.

## Reading the per-control guides

Each control folder ships a `<ControlName>.md` next to the `.cs` file. **When you build a
screen, read the target control's `.md` guide first** — it is the authoritative reference
for that control. The per-control guides cover:

1. What the control is for and when to reach for it (and which sibling G9 control to
   use instead for an adjacent need — e.g. `G9SearchEntry` vs `G9TextEntry`,
   `G9ComboBox` vs `G9Picker`).
2. Bindable properties with type, default, and effect.
3. Events the consumer can subscribe to.
4. Quick XAML / code-behind usage examples.
5. Common composition patterns (forms, RTL, validation, etc.).
6. Behaviour notes that aren't obvious from the property list.

If you find yourself wondering "how does this work in RTL?" or "can I bind this from a
view model?", the per-control guide is the place to look first. If a behaviour applies
to multiple controls, it's documented here in the architecture guide and the per-control
guide just links to the relevant section.

## Tuning the icon gap from a single place

The most-asked layout question is "where does the icon margin live?". One answer:

- `G9Metrics.InputIconStartMargin` — symmetric leading-icon margin (default 8).
- `G9Metrics.InputIconEndMargin` — symmetric trailing-icon margin (default 8).

The value is the gap that appears identically on **both sides** of the icon — between
the icon and the box wall, and between the icon and the inner text. Set to 0 for a flush
icon; set to N for N px on each side.

Every dependent metric (icon column width, inner-content padding on the icon side, the
floating-label rest-state slide, the outline notch anchor) is derived from these so
there's nothing else to update.

## Adding a new control to this folder

1. Create `G9Foo/G9Foo.cs`.
2. Inherit from `G9ControlBase` (display / feedback) or `G9OutlinedFieldBase` (input
   with outline + floating label).
3. Declare bindable properties as `[AutoBindable]` private fields.
4. Implement `OnApplyVisuals()` — this is the one place the visual tree is updated from
   property values.
5. If the control draws onto a `GraphicsView`, add a sibling `G9FooDrawable.cs` and keep
   the painting code there.
6. Add metric / color tokens to `G9Metrics` / `G9Colors` rather than
   hard-coding values inside the new control.
7. If the control hosts a focusable inner element (`Entry`, `Editor`), set
   `StyleId = G9PlatformConfig.NoUnderlineStyleId` on the inner element so the
   platform chrome and intrinsic padding are stripped.
8. Drop a `G9Foo/G9Foo.md` next to the `.cs` file documenting what the control is
   for, its properties, events, and a usage example. Mirror the structure of the existing
   per-control guides. Describe the G9 control on its own terms — do NOT anchor the
   guide on any removed legacy control; point to sibling G9 controls for adjacent needs.
9. Add a row to the table at the top of this file.

> **Keeping the docs in sync.** Whenever you add, rename, retire, or materially change a
> control, update BOTH this architecture guide (`G9Controls.md` — the folder table
> and any affected section) AND that control's own `G9Foo.md` guide in the same task.
> The two are the single source of truth for the design system; a code change that leaves
> either stale is incomplete.

## Palette migration TODO

Every file in this folder has `// TODO (palette step):` comments where G9-design
alpha recipes will move into `G9Palette`. The migration is a single pass that reads
each `// TODO` and pushes the value into a new palette token without changing the
control's behaviour. Until then, color tokens live in `G9Colors.cs` and metric
tokens live in `G9Metrics.cs`.
