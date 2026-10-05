# 09 - Progress

# G9MAUIControls
## Project Progress & Execution Log

---

# Current Status

**Step 1 (package ecosystem) COMPLETE: five packages build and pack. Step 2 COMPLETE: consumed by a real
app on all four TFMs, in both project- and package-reference mode. Outstanding: the visual pass, which
needs a human eye, and iOS NativeAOT.**

Last updated: **2026-10-04**

> **This list is not complete.** It jumps 1.0.3 → 1.0.13; 1.0.4 through 1.0.12 shipped without an entry
> here. `Directory.Build.props` → `PackageReleaseNotes` has every version and is the record that has not
> drifted — read it, not this heading list, when you need to know what a version contained.

## 1.4.0 — a second tab-bar style (Sculpted), a title accessory and a filled chip on G9NavCard, day-relative dates (2026-10-04)

> **NOT YET PUBLISHED** — `G9FamilyVersion` is 1.4.0 in the working tree, uncommitted. AgriPad's
> `agripad-new-structure` working tree already USES every API below (the Sculpted bar on `MainPage`, the task
> overview sheet, `G9ControlsIntegration`), so the app builds only with `-p:UseG9Source=true` until 1.4.0 is
> pushed, indexed (all five packages) and `<G9MAUIControlsVersion>` is moved from 1.3.0. Decision record:
> ADR-0030.
>
> **Verified (source mode, AgriPad consuming the projects):** the library builds; the app builds for
> net10.0-android in Debug, and with `-p:MauiXamlInflator=XamlC`; the Sculpted outline was rendered offline
> with skia-python from the same numbers `G9TabBarOutline` uses and matched the Figma component (cradle
> shoulders, disc 8 dp into the bar). **Not verified:** any of it on a device or simulator, the open/close
> animation of the cradle, dark theme, the sub-menu / overflow cells in Sculpted, iOS / Mac Catalyst / Windows.

### What changed

- **`G9TabBar.BarStyle`** (`G9TabBarStyle.Classic` default / `Sculpted`). Everything that differs between the
  two lives in **`G9TabBarStyleMetrics`** (public; `Classic` reproduces the old `G9TabBarMetrics` constants
  exactly) and the style overloads of `G9TabBarColors`. `G9TabBar.CurrentMetrics` exposes the active one.
  `ApplyStyle()` swaps it in place: drawables' metrics, FAB sizes and glyph ratios, rebuilt items, reserved
  height, colours — selection and FAB state are untouched.
- **`G9TabBarOutline`** — ONE builder for the bar silhouette (rounded rect + semicircle or cradle), with a
  `PathF` sink (chrome) and an `SKPath` sink (shadow). Both used to carry their own copy of the path.
- **`G9TabBarItem.SelectedIcon`** — shown on the selected item in Sculpted only.
- **`G9NavCard.TitleAccessoryView`** (a view on the title's line, after the title; a flex row so the TITLE
  truncates first) and **`G9NavCard.UseFilledIconChip`** (solid accent chip, `OnPrimary` icon).
- **`G9CultureDateTimeDisplayMode.RelativeDay`** + `G9CultureDateTimeLabel.RelativeDayNow` +
  `G9StringKey.Tomorrow` / `Yesterday`.
- Gallery: `NavigationSurfacesPage` shows a Sculpted bar under the Classic one.
- **2026-10-05, still 1.4.0 (unpublished, uncommitted on top of 7743d8a):** after the owner's device review the
  Sculpted bar is 68 dp (Figma 64); the cradle path stays in Figma's design units and is multiplied by the new
  internal `CradleScale` (56/48); the disc is 66 dp in a 74 dp box (Figma 48/56), «+» 30 dp — sized INDEPENDENTLY
  of the cradle, grown until the space around it was half of the first round's (5.8 dp at the bowl). Sculpted now draws a **Skia FAB shadow** (`DrawsFabShadow`), traced around the DISC
  (`FabShadowDiameter`) at its own `FabShadowAlpha` 0.3 and `FabShadowOffsetY` 2 — Classic's values for those
  three reproduce its old behaviour exactly. Checked on the AgriPad emulator (Pixel 9 Pro XL AVD) by the owner.

### CONSUMER-VISIBLE without any app change

Nothing: every addition defaults to the 1.3.0 behaviour.

### Traps found on the way

- `Style` is `NavigableElement.Style` — a style property on the bar had to be called `BarStyle`.
- A host computing its bottom inset from `G9TabBarMetrics.BarHeight` (AgriPad's `MainPage` did) is 2 dp
  short under Sculpted; read `CurrentMetrics.BarHeight`.

## 1.3.0 — the progress overlay's placement belongs to the host (2026-10-03)

> **PUBLISHED** — nuget.org listed `G9MAUIControls` 1.3.0 on 2026-10-04 and AgriPad pins 1.3.0. (This note
> said "not yet published" when it was written.) One app-reported item (AgriPad: "separate the sync progress from the toasts; on the map show it
> under the top bar"). Decision record: ADR-0029.
>
> **Verified (source mode, AgriPad consuming the projects):** ProgressOverlay builds with 0 warnings; the
> app builds on all four TFMs. On the Pixel 9 Pro XL emulator, by screen recording: a farm sync on the Map
> tab mounted the overlay one gap under the app's site selector (provider → Top + 55 dp); the Tasks tab's
> sync mounted at the bottom (provider → null); switching Tasks → Map mid-sync faded it out at the bottom
> and in at the top within ~300 ms, mid-countdown (`RefreshPlacementAsync` edge change). **Not verified:**
> the same-edge offset glide on a device, a minimized bubble during a refresh, iOS / Mac Catalyst at runtime.

### What changed

- `G9ProgressOverlayPlacement(Position, Offset)` beside `G9ProgressOverlayPosition` (`G9ProgressContracts.cs`).
- `G9ProgressOverlayHelper.PlacementProvider : Func<G9PageBase?, G9ProgressOverlayPlacement?>` and
  `Task RefreshPlacementAsync(bool animate = true)`.
- `ShowAsync` / `ShowStandaloneFailureAsync` take `G9ProgressOverlayPosition? position = null`. An explicit
  position is fixed for the session; `null` follows the provider. Source compatible, **binary breaking** for
  a consumer compiled against 1.2.0 that passed a position.
- The session's placement is mutable (`_placement`, `_followsProvider`); `ApplyOverlayPosition` adds the
  offset; a host change re-resolves it; `G9ProgressOverlayView.IsMinimized` (internal) lets a refresh leave a
  dragged bubble alone.

### CONSUMER-VISIBLE without any app change

Nothing: with no `PlacementProvider` every overlay resolves to `Default` (bottom, no offset), exactly 1.2.0.

### Needs the app

Set the provider once, and call `RefreshPlacementAsync()` when the screen under the overlay changes. AgriPad:
`Common/Sync/SyncProgressOverlayPlacement.cs`.

## 1.2.0 — dictation without a keyboard, neutral filled fields, sheets and toasts that follow their host, a check box (2026-09-27)

> **Published 2026-09-27** (commit `5bd1822`, pipeline run 497; the five packages carry the GitHub
> mirror at that commit). Nine app-reported items (Jira ITCS-15661, -15666, -15664, -15525, -15663,
> -15685, -15686, -15687 and the measuring-tool sheets), all fixed in the library rather than around it
> in the app. Decision records: ADR-0024 … ADR-0028. Lessons: LES-0047 … LES-0052.
>
> **Verified:** unit tests 118/118; core / ProgressOverlay / Barcode on all four TFMs; the Gallery; the
> consuming app (AgriPad) on Android and Windows in both source and package mode, and an Android Release
> publish. On the Pixel 9 Pro XL emulator: the ITCS-15525 sheet shrink (LES-0051) and the fit-sheet
> stale measure (LES-0052) were reproduced by trace and verified fixed; the rest was exercised by the
> app's owner on the emulator. **Not verified:** iOS / Mac Catalyst (no Mac available), and anything on a
> low-end physical device.

### What changed

- **The microphone no longer opens the keyboard** (`G9TextEntry`, `G9SearchEntry`, `G9Editor`, and the
  popup form fields built on them). A mic tap does not focus; starting dictation while typing takes the
  keyboard down (`G9KeyboardHelper.DismissKeyboardIfFocused`, internal); focusing the field during a
  session ends it. `G9OutlinedFieldBase.OnBoxTapped` now ignores a tap that lands on an actionable icon
  slot, so the box's tap-to-focus cannot fire for an icon tap on platforms that deliver it to both.
- **Filled fields can rest neutral.** New `G9OutlinedFieldSettings` + `G9OutlinedFieldBase.Configure` /
  `.Settings`, and per field `FilledValueHighlight` (`G9FilledValueHighlight`). Default unchanged.
  Gallery → Inputs has a new "Filled value: accent vs neutral" section.
- **Full-screen sheets follow the host page's height** — BS-20 closed. Every element the helper sized
  at open is re-sized on the host page's `SizeChanged`; `IG9BottomSheetSizedView` bodies are told too.
- **Resting (collapsed / fit-to-content) sheets are capped to the host** minus the top safe-area inset
  (`G9SheetView.RestingTopReserve`, `ResolveCollapsedRestingHeight`), so the body shrinks with the keyboard
  and the header stays on screen.
- **Live toasts and the progress overlay can be re-anchored**: `G9ToastHelper.RefreshBottomInsetsAsync`,
  `G9ProgressOverlayHelper.RefreshBottomInsetAsync`.
- **Found by the trimmed Gallery publish, not by any build: a 1.1.0 IL2091** in `G9PageHostStack<THost>`
  (the IN-04 host stack). Fixed with the `PublicParameterlessConstructor` annotation — LES-0049.
- **An editor's microphone / trailing icon sits in the bottom-END corner in every configuration**
  (ITCS-15685, LES-0050). It was mid-box whenever `MaxEditorHeight` was set: the trailing host's ripple
  `GraphicsView` grew to the at-most constraint the ceiling introduces, so `VerticalOptions = End` had
  no slack to act in. `G9Editor` now gives the host an explicit height
  (`G9Metrics.EditorTrailingSlotHeight` = 36, new `EditorTrailingIconBottomInset` = 8) instead of a
  bottom margin. Gallery → Inputs → G9Editor has the observation-form and tester-form configurations
  side by side.
- **A fit-to-content sheet no longer keeps the keyboard's height after the keyboard closes**
  (ITCS-15525 reopen, LES-0051). A re-fit that ran with the keyboard up sized the sheet against the
  shrunken page and stored that as its natural height; fit caps now use a keyboard-proof reference
  (`ResolveFitReferenceHeight`: tallest host height at the current width). Reproduced and verified on the
  Pixel 9 Pro XL emulator (attribute form: collapsed to 454 before, returns to 256 after, over three rounds).
- **Fit-to-content sheets measure their body fresh** (LES-0052). The fit measure read a stale cached size
  (a label measured as one line before it wrapped), so footer-less fit sheets rested 25-40dp short — their
  bottom hung below the screen. `MeasureContentHeight` now invalidates the body's subtree first. Verified
  on the emulator (measure tool 159.7 → 182.3, point reading 418 → 458).
- **`G9CheckBox.ReserveTouchTarget`** (default `true`): `false` makes the control exactly the 20dp box for
  a box inside a row that owns the tap (device review: 48dp slots made list-picker rows ~72dp tall).
- **New control: `G9CheckBox`** (ITCS-15686, ADR-0028) — M3 square, `Primary` fill, a tick that draws
  itself in (and back out), tri-state (`IsIndeterminate`), press halo, wrapping label, row-sized 48dp
  hit target, `CheckedChanged` with the platform's `CheckedChangedEventArgs`, `Toggle()`. New
  `G9Metrics.CheckBox*` tokens (box / radius alias the existing `SelectionCheck*` ones),
  `G9Colors.CheckBoxHaloAlpha` / `CheckBoxDisabledOutlineAlpha` / `CheckBoxMark(palette)`, and
  `G9StringKey.Checked` / `NotChecked` / `PartiallyChecked` (appended — existing values unchanged).
  The pure timeline / tap rule / tick geometry is `G9CheckBoxMath`, file-linked into the unit tests
  (`G9CheckBoxMathTests`). Gallery → Inputs → G9CheckBox: every state, bare boxes, a working tri-state
  "select all", RTL with a wrapped label.
- **Popup `CheckBox` input fields render `G9CheckBox`** rows instead of `G9Switch` form rows. Values,
  validation, flow direction unchanged; radio fields still `G9Switch`.

### Verification actually run

Core, ProgressOverlay and Barcode built on all four TFMs (Debug + Release for the core), 0 warnings;
Gallery built for Android; `G9MAUIControls.Tests` (99) and the Sqlite tests (28) pass; trimmed Release
publish of the Gallery (`AndroidLinkMode=Full`, `PublishTrimmed=true`) succeeds with no IL diagnostics.
Not run: any device, `dotnet pack`, the app in source mode.

### CONSUMER-VISIBLE without any app change

- Tapping a microphone no longer shows the keyboard; tapping the field while dictating stops dictation.
- Full-screen sheets and fit sheets change size with the keyboard (AgriPad: forms in sheets).
- A trailing clear / eye icon tapped on an unfocused field no longer focuses it on iOS.
- Every `G9Editor` with `MaxEditorHeight` and a microphone / trailing icon: the icon moves from mid-box
  to the bottom-end corner, and (expected — see the gap list) the box opens at `MinimumEditorHeight`
  instead of at its ceiling. AgriPad: the observation form's description field.
- Every popup `CheckBox` field looks different: check-box rows instead of switch rows.

### Needs the app

- `G9OutlinedFieldBase.Configure(G9OutlinedFieldSettings.Default with { HighlightFilledValue = false })`
  in the app's startup integration, if the design wants neutral filled fields.
- Call both refresh methods where the tab bar is hidden / shown.
- Delete `ProfileChangePasswordContentView`'s host-height following (now done by the library).
- Move the app's platform `CheckBox` usages to `G9CheckBox`; teach the QA bridge / semantic snapshot
  the new type (`Toggle()` to actuate, `IsIndeterminate` / `IsChecked` to read); add the three
  `AppControlsChecked` / `AppControlsNotChecked` / `AppControlsPartiallyChecked` strings to the resx.

### The honest gap — look at, on a device

1. Mic tap on an unfocused field: no keyboard; on a focused field: keyboard goes down, dictation runs;
   tap the field mid-session: keyboard up, mic back to idle, partial text kept. iOS: the box must not
   focus on a trailing-icon tap.
2. Full-screen form sheet (toolbar, toolbar-less, edge-to-edge): open keyboard → footer above it, body
   scrolls to its end; close keyboard → back to full height. Watch for a one-frame jump.
3. Fit sheet with a scrolling body and a text field: keyboard up → header visible, body shorter, focused
   field scrolled into view; keyboard down → original height.
4. Hide / show the tab bar with toasts stacked and the progress overlay up, and with the bubble minimized.
5. **Editor trailing slot (ITCS-15685).** Gallery → Inputs → G9Editor, both culture directions: the
   trailing glyph 8dp above the bottom edge, at the END side, empty / multi-line / scrolled past
   `MaxEditorHeight`; the observation-form field opens at ~110dp, not at its 230dp ceiling. In the app:
   the observation form and the tester's new-report form must now look the same. Tap the mic: ripple
   fills the 40 × 36 slot, no keyboard.
6. **G9CheckBox (ITCS-15686).** Light and dark: the fill pop + tick draw-in feel like one gesture; a
   double tap turns the tick around mid-way; the halo appears under the finger on Android (pointer
   press) and flashes on a platform that reports none; press-and-scroll in a list must not leave a halo
   behind; label taps toggle; row EDGES toggle (§10b); indeterminate → tap → checked (and the bar bends
   into the tick); disabled rows ignore taps; RTL: box on the right, tick NOT mirrored, wrapped label's
   first line level with the box; TalkBack reads "<label>, not checked / checked / partially checked".
   Dark palette: decide whether the deep-green `OnPrimary` tick is wanted or should be white
   (`G9Colors.CheckBoxMark`).

## 1.1.0 — the bottom sheet is staged and measured before it opens, plus the remediation pass (2026-09-21)

> **Read the honest gap FIRST.** Everything in this entry was written and **compile-verified on all four
> TFMs, and nothing in it has been run on a device.** That is not a formality here: the headline change
> is about what a user SEES in the first 300 ms of a sheet opening, and a green build says nothing about
> that (rule 2 of `01-AIGuide`). The verification list at the end is the actual remaining work.
> Decision record: `10-Decisions.md` → ADR-0022 and ADR-0023. What the remediation pass found and did
> **not** fix: `11-EngineeringLog.md` → RSK-0003.

### What changed — bottom sheet (the reason for this release)

The consuming app's three complaints — a skeleton had to be shown before content, the first-open height
was wrong so it kept a persisted memory of heights, and a resize looked bad — traced to our own open
pipeline, not to Android:

- **The first measure ran before the sheet was attached**, where a view has no handler and measures as
  zero. It now runs after. A sheet is *staged*: parked below the screen edge, visible, laid out at its
  real height, measured for real, held for `PreOpenSettleFrames` drawn frames, and only then slid in
  (`G9SheetView.Stage`, `G9BottomSheetHelper.StageAndShowAsync`).
- **An already-built view is no longer deferred.** `DeferContent` defaults to `true` and used to hide
  even a finished view behind a placeholder for a fixed 369 + 220 + 160 ms. It now only applies to
  factory content, whose build starts from the open-motion-completed SIGNAL rather than a timer.
- **Resize and drag are translation, with one layout pass per size** (`SetFitHeight`, the drag path in
  `HandleTouchMoved`), and a sticky footer is held at the screen edge by counter-translation
  (`BottomPinnedView`). Both used to write `HeightRequest` every frame.
- **An aborted motion no longer completes**; a retargeted motion inherits the completion it replaces.
- **Velocity-aware release**, a finger takes over a moving sheet, Android touch layer stops walking the
  ancestor chain and re-reading the display density per event, and tracks the active pointer.
- **Android: the backdrop effect no longer sweeps ~96 native views twice per animation frame.**
- `ShowG9BottomSheetAsync` (faults when the pipeline throws); the `void` overloads report a pipeline
  failure instead of letting it escape on the dispatcher; `ShowListG9BottomSheetAsync` honours the
  caller's `SizeMode` and can no longer hang when throttled.
- Rollback: `G9BottomSheetSettings.StageBeforeShow = false`.

### What changed — the rest of the library

Delegated by slice and each compile-verified in isolation before integration. What was fixed is below;
what was **left** — every partial and every skip, with the reason — is `11-EngineeringLog.md` → RSK-0003,
because a defect that is consciously not fixed has to outlive the audit that found it.

- **Popup / Toast / G9SafeCommand** — `ShowConfirmAsync` no longer hangs when dismissed by hardware back
  (IN-01, the most urgent defect found); the queue no longer deadlocks on a nested popup (IN-02) and one
  exception no longer wedges the pump (IN-03); footer buttons are re-entrancy guarded; a throwing toast
  action no longer crashes; the throttle uses a monotonic clock; the busy / concurrency guard is released
  BEFORE the error popup is awaited; new `G9SafeCommand.OperationFailed` hook.
- **Hosting / Theming / TabBar / EdgePanel / ProgressOverlay** — the host registry is a stack of live
  pages (IN-04); pages, content views and the edge panel release their static palette / culture
  subscriptions (IN-05, 15, 21); follow-system-theme survives a GC (IN-07); theme switch ordering; the
  `{G9Color}` subscription list no longer grows without bound or reflects per event; tab bar resyncs
  after an unload; progress overlay follows a root-page swap.
- **Controls** — `[AutoBindable]` defaults that were silently `default(T)` (CT-01); hidden controls catch
  up on a theme / culture change (CT-02); safe buttons no longer capture `Command` once or write
  `IsEnabled` (CT-03, 04); Persian / Arabic-Indic digits (CT-05); selection-sheet dismissal no longer
  loses data (CT-06); Persian calendar range crash (CT-07); time-span picker columns and day math
  (CT-08); one lifetime model on `G9ControlBase` (XC-01); one press pipeline `G9Press` (XC-03).
- **Persistence.Sqlite** — cross-user cache republish after a reset (IN-08); captured `null` predicates
  (IN-09); UPDATE / DELETE builders refuse to build without a `WHERE` unless `.AllRows()`; one
  transaction per batch; PRAGMAs re-applied per connection; identifiers quoted; large `IN` lists through
  `json_each`. **`!=` now also matches NULL rows, as the C# it was written from does** — see the README's
  "Changes that can alter what existing code does".

### CONSUMER-VISIBLE changes to look for

- A `G9TimeSpanPicker` with no `Mode` now shows years, months **and days** (the intended default was
  never applied). AgriPad's seedling-age and graft-age fields are two such pickers.
- A nested `G9CascadePanel` now shows its back header and parallax by default.
- Two AgriPad list pickers that pass `FitToContentOptions()` will now be fit-to-content instead of
  full-screen — which is what they asked for.
- Any popup raised from inside a popup button callback is now shown immediately.

### Deliberately NOT done (each is in the plan, none is forgotten)

Shell pooling; an `INestedScrollingParent3` touch layer; IME-synchronised sheet translation; a
predictive-back callback; the structural `G9Redacted` skeleton; splitting the 6,000-line helper into a
per-sheet session object; the optional iOS 16+ native presenter; deleting the AgriPad-side compensations
(height seeds, height providers, memo keys, `CompactListSheetHelper`, `OperationsMenuContentViewBase`).
The reasoning is in ADR-0022 → *Rejected*: each is either a large refactor with no user-visible effect or
platform gesture / inset code that cannot be validated without a device and would interact with
workarounds the app still carries. They wait for the device pass on this release. Each is also an open
defect from here on, so each is in `11-EngineeringLog.md` → RSK-0003 with what specifically is missing.

**Shell pooling is the one to reconsider first**, and the device trace below is why: it was deferred as a
refactor with no user-visible effect, and the trace then showed that everything before the motion starts
is content cost — handler creation and first layout for the body — which is exactly what a pooled,
pre-warmed shell and a kept-alive heavy body remove. The plan's own design still stands: pool the
`G9SheetView`, scrim, header band and footer host per page host, swap only the content and the
header/footer children, mark the pooled root `HandlerProperties.DisconnectPolicy = Manual` so a page pop
does not tear down handlers meant for reuse, disconnect explicitly when the host page goes away, and
pre-warm one shell at page idle. Pool size 2 — primary plus one stacked — with more created on demand and
dropped.

### After the first device trace — native motion, the frame clock, and the GC (2026-09-21, same release)

The 1.1.0 engine was traced on a device through 59 sheet opens (`BS|…` lines, chapter 06 §8b of the AgriPad
guides). Three findings, three changes — ADR-0023 has the reasoning:

- **Motion was not native because it was timed by distance.** 199 ms × distance / screen = a 200 dp picker
  opening in 40 ms. Now `MotionStyle = PlatformNative`: `ViewDragHelper`'s settle on Android / Windows,
  UIKit's default spring on Apple platforms (`G9SheetMotionModel`, 18 new tests). The overlay fade and the
  close cleanup follow the running motion's own duration. `FlingVelocityThreshold` 700 → 300 dp/s.
- **The first frame of a motion was usually late, and the curve paid for it.** Motion now runs on
  `G9SheetMotionDriver` over `G9FrameClock`: clock starts on the first drawn frame, hitches become pauses,
  positions are evaluated at vsync time.
- **A bridged GC every 1.1 s.** `G9FrameAwaiter` allocated a Java callback per awaited frame; it now shares
  the process's single `G9FrameClock` callback. The larger lever is app-side (Mono nursery 4 → 32 MB).

What the trace says about the time BEFORE the motion (request → motion start, median 188 ms, p90 423 ms) —
none of it is the pipeline's own waiting (settle ≈ 33 ms, readiness ≈ 0):

| stage | median | p90 | what it is |
|---|---|---|---|
| attach | 39 ms | 113 ms | platform handler creation for the body — MAUI's cost per view |
| first layout | 43 ms | 229 ms | the first real measure / arrange of that tree |
| settle | 33 ms | 98 ms | two frames; long when the UI thread is busy (a map, a camera starting) |

So a light body (a picker: ~90 ms total) is near what the platform allows, and a heavy form (450–660 ms) is
paying for its own view count. The engine cannot make a 570-invalidation form attach faster; the app can —
by keeping heavy bodies alive and re-showing them, or by building them ahead of the tap.

### Round 3 — sheets that opened on a spinner for a LOCAL read (2026-09-21, same release)

Second device trace (78 sheets): bridged GCs one per 28 s (was 1.1 s), 14 % of opens hit (was 61 %);
median request → motion 121 ms (was 188). The owner then pointed at the Tasks-page sheets that still open
on a spinner. The trace named the causes, none of them the network:

- loadable bodies ("open then fill") whose first render reads only the local store →
  new opt-in `LoadableSheetContentView.LoadWhileStaged` / `IStagedSheetLoad`; the pipeline now waits for
  content readiness BEFORE the first measure, on its own budget;
- bodies handed over as factories although they cost 60–120 ms to build → app-side, shown pre-built;
- a picker called off the UI thread fell back to the factory path (opened at 180 dp, grew to 415 dp in
  view) → `ShowListG9BottomSheetAsync` and `G9SelectionSheet` now marshal and still build eagerly.

### Round 4 — a sheet that replaces the one it came from (2026-09-21, same release)

Owner's report: tapping «مشاهده و انجام کار» on the task-detail sheet left a dark, empty page, then the
full-screen task view rose with the dim dipping and coming back. Trace: 970 ms from the tap to the next
sheet moving (close 300 + cleanup 120 + view construction 260 + staging 290). Two causes, both fixed in the
library: a staged sheet's overlay was painted at its resting dim while the sheet was off-screen (now 0 until
the sheet moves), and there was no way to replace a sheet across sizing models without an empty gap → new
`G9BottomSheetOptions.ReplaceCurrentSheet` (hand-off at the moment the staged successor is ready, dim
inherited as a floor). A stacked child's parent now recedes when the child rises, not when it is attached.

### The honest gap — what must be looked at, on a device

`G9Controls.Gallery` → **Sheet Lab** has one button per rule, each captioned with what must be true.

1. Every sheet rises ONCE, at its final height, with content in it. Compare with the engine switch OFF.
2. Icons: are any late? If so raise `PreOpenSettleFrames` (the lab cycles 0 / 2 / 6) and note the value
   that fixes it on the slowest device.
3. Tap → motion-start on a heavy pre-built body. This is the cost the design moves; judge whether it is
   acceptable, and which sheets should become factory sheets.
4. Resize: footer welded to the edge, no stutter. Drag between detents: footer stays at the edge.
5. Fling up / down; touching a sheet while it is still rising.
6. Hardware layer on the low-end device (toggle in the lab) — any artefacts, especially over a map.
7. Stacking three sheets quickly; close during the off-screen hold; keyboard opening under a fit sheet.
8. RTL, dark theme, font scale 1.3, rotation.
9. Then AgriPad in source mode (`-p:UseG9Source=true`, delete `obj/` + `bin/` when switching): the
   tree / pot sheets, the layers sheet, the non-modal map sheets, the two list pickers.

## 1.0.13 — a G9Glyphs slot is honoured in the library's OWN XAML too, and `Refresh` is drawn right (2026-09-08)

Two fixes, one report: a consumer's icon override reached some of a control's icons and not others.

**The slot was bypassed by the library's own markup.** `G9Glyphs.X` exists so a consumer can point the
suite at its own icon font, and `G9ProgressOverlayView` set the failure banner's LEADING icon from
`G9Glyphs.Refresh` correctly. But the retry button beside it carried `Icon="Refresh"` written in the
view's XAML, and `G9IconSourceTypeConverter` resolves that to the built-in vector `G9Glyph.Refresh`
without ever consulting the slot. The banner therefore showed two refresh icons — the host's font on
one side, the library's drawing on the other, in different styles and turning opposite ways.
`ShowTerminalAsync` now assigns `TerminalRetryIcon.Icon = G9Glyphs.Refresh` in code. **It has to be
code:** the slots are configured during consumer startup, which is after `InitializeComponent`.

**And the built-in drawing was wrong anyway.** `G9Glyph.Refresh` hung its arrow head off the START of
the arc — apex 3.3 units clear of the stroke, legs opening away from the direction of travel — so at
18 dp with a round-capped stroke it rendered as a broken flag beside the ring rather than an arrow on
it. The head is now a tangent-aligned chevron at the END of the sweep (130°).

**Consumer-visible:** a control that offers a `G9Glyphs` slot now uses it for every icon it draws, so
an override that used to apply patchily applies completely. A consumer that had NOT overridden
`G9Glyphs.Refresh` gets the corrected glyph instead of the broken one. Nothing renamed, no API change.
ADR-0021, LES-0045.

## 1.0.3 — G9TabView: fix a NullReferenceException when the tab bar rebuilds off the UI thread (2026-08-18)

`RebuildAll` now marshals itself to the UI thread, and every `_cells` lookup goes through one
null-tolerant `FindCell` helper.

**The crash.** Reported from production as a `NullReferenceException` inside
`_cells.FirstOrDefault(c => c.LogicalIndex == effective)` in `PositionPillNow`, reached from a
`Dispatcher.Dispatch` queued by `RebuildAll`. In that predicate the only thing that can be null is
`c` — an element of the list — and `_cells` is only ever populated with non-null cells.

**Why an element can be null.** `List<T>.Clear()` nulls the backing array slots for a reference
element type. `RebuildAll` calls `_cells.Clear()` and runs on whatever thread raised its trigger:
`OnItemsCollectionChanged` fires on the mutating thread, so an `ObservableCollection` updated from a
background worker rebuilds the bar off-thread. A reader already enumerating on the UI thread has
captured the old count and walks into a slot that has just been nulled.

**Consumer-visible:** none, beyond the crash going away. Marshalling `RebuildAll` was already
required by MAUI (it mutates the visual tree); the guard makes that contract explicit instead of
relying on every caller to honour it. LES-0039.

## 1.0.2 — canonical GUID case corrected to LOWER, and the setting is finally honoured (2026-08-17)

`G9SqliteOptions.CanonicalIdCase` now defaults to `G9IdCase.Lower`, and `AddG9Sqlite` actually applies
it (`SqliteGuidStringNormalizer.UseCanonicalCase`). **In 1.0.1 the setter was accepted and silently
ignored**, so consumers configuring the case got no error and no effect — LES-0038 has the full
diagnosis and the one-command check. Lower matches `Guid.ToString("D")`, RFC 4122, PostgreSQL `uuid`
and Dotmim.Sync's wire format; upper made this library the only component in the stack disagreeing,
which SQL hid (`COLLATE NOCASE`) but every ordinal comparison did not. Rationale, cost and the
migration a consumer must perform: ADR-0018.

**Consumer-visible:** if you derive a file path from a normalised id, you MUST adopt the
differently-cased directory on upgrade or existing installs will look empty. Rename it; do not copy it.

## 1.0.2 — `G9CultureDateTimeLabel` stops pinning its own `FlowDirection` (2026-08-15)

**A behaviour fix, and the first defect found by simply running a consumer in the OTHER language.**

The label pinned `FlowDirection = LeftToRight` for every absolute mode so a numeric date would not be
re-ordered inside a Persian screen. Pinning the flow direction is a paragraph-direction switch, so it
silently pinned the label's ALIGNMENT too — `HorizontalTextAlignment` and `HorizontalOptions` resolve
`Start`/`End` against the view's own effective direction. The consuming app had therefore written
`HorizontalTextAlignment="End"` at several call sites to reach the right edge under Persian, and every
one of them was visibly wrong in English (a date hanging off the right edge while its own caption and
the value beside it sat left). No alignment value existed that was correct in both languages.

The order is now kept in the STRING — the formatted value is wrapped in a Unicode LTR embedding
(U+202A/U+202C) under an RTL culture — and the label is an ordinary `Label` for layout. `Relative` mode
is untouched (localized words must read in the culture's direction) and nothing is wrapped under an LTR
culture, so no invisible character enters a string an LTR app might log or export. See LES-0037 and
ADR-0017.

## 1.0.1 — packaging fix (2026-08-15)

**The first defect found by a consumer on PACKAGE references rather than project references, and it
could not have been found any other way.**

`G9MAUIControls.Persistence.Sqlite` 1.0.0 shipped a Windows `.pri` that indexed
`G9MAUIControls.Persistence.Sqlite\icon.png` — a path the nupkg does not contain, because the icon is
packed at the package root as `<PackageIcon>` requires. Every consumer building `net10.0-windows*`
failed with `MSB3030`. It was the only package affected: the other four reference
`Microsoft.Maui.Controls`, whose targets sweep root images back out of `@(Content)`, while this one is
Essentials-only by design (ADR-0009) so nothing removed it.

Fixed family-wide in `Directory.Build.props` by keeping the packaging icon out of the SDK's default
item globs (`DefaultItemExcludes`). No API or behaviour change; no source file touched.

Verified by packing all five and inspecting the artifacts (icon at package root in each, no `.pri`
indexing it), then by restoring a real consumer against a throwaway `1.0.1-localverify` build and
building both `net10.0-windows10.0.19041.0` and `net10.0-android` green with the consumer's temporary
workaround removed.

**What this says about the verification story.** Every in-repo build and every pack was green for the
whole time this was broken, and the gallery could never have caught it: it builds from PROJECT
references, which never exercises the `lib/` layout a package reference resolves. Package-reference
consumption is a distinct verification axis from project-reference consumption — see LES-0036.

## Provenance

This suite was extracted from a production .NET MAUI application whose UI, theming, hosting and local
persistence layers had grown general enough to be worth reusing. **The extraction is complete.** This
repository is the source of truth: changes flow from here into consumers, not the other way round.

That history is why parts of this guide read as a migration log, and why some ported prose still
describes a control in terms of the screens it was first built for. Where a statement here is about the
extraction rather than about the library, treat it as background — the technical claims hold, the
pointers into another codebase do not.

## Step 3 — re-integrated into the application it came from (2026-08-14)

The application this suite was extracted from now consumes it. This is the first consumption by
anything other than the gallery, and by a long way the most informative: ~51,000 lines deleted from
that application (239 files), ~1,000 files rewritten, and every seam exercised by code that was written
before the seam existed.

**Consumed as PROJECT references, deliberately** (the consuming app's `.csproj` to the five
`G9MAUIControls*` projects). Package-reference mode is gated on the visual pass below.

**It found 21 defects in the library, none of which the gallery could have found**, because the gallery
was written against the API as it is and this app was written against the API as it was:

| # | Defect | Fix |
|---|---|---|
| 1–6 | Six seams `internal` that a consumer needs — `G9ModalHostRegistry`/`ModalHost`, `G9SelectionSheet`, `G9ColorExtension.ResolveColor`, `G9PaletteSubscriptions`, `SqliteEntityAuditDefaults`, `SqliteRepositoryCacheRegistry` | public, except the modal registry: the consumer was moved onto `G9OverlayHosts` instead, which is the seam that already existed for it (LES-0026) |
| 7 | `SqliteGuidStringNormalizer` — `internal`, in a `.Internal` namespace, and the definition of the id format the storage contract depends on. 208 consumer call sites | public, moved to `G9MAUIControls.Persistence.Sqlite` |
| 8 | `G9PageBase.IsHardwareBackSuppressed` / `TryHandleInAppBack` were `internal virtual` — a consumer can neither override nor call them, so the back contract was unreachable across the package boundary | `public virtual` |
| 9 | `G9TabBarMetrics` internal — every consumer hosting the tab bar must inset its content by the bar height, and could only do it by copying the number | public |
| 10 | `IG9SqliteDatabaseLocator.DatabasePathChanged` documented as closing the connection and resetting caches; **nothing subscribed to it** (LES-0027) | `G9SqliteConnectionProvider` subscribes, and unsubscribes on dispose |
| 11 | `G9BottomSheetHeightSeeds.TryGet` compared the FULL memo key while the type's own example seeds by type name, so the documented usage never matched (LES-0027) | exact hit first, then strip the device components back to the identity |
| 12 | `G9SafeCommand` showed **More details** whenever a handler was registered, with no way to gate it on a runtime setting (LES-0027) | new `DiagnosticsAvailable` predicate, asked per popup |
| 13 | `IG9OverlayHost` exposed `ToastLayer` and `DevLayer` but not the sheet/overlay layer, so a consumer's own modal had no sanctioned layer to mount into and reached for the internal registry | added `OverlayLayer` |
| 14 | **`Icon="…"` did not compile in XAML on any control.** Every slot is `G9IconSource?`, and XamlC looks for the `[TypeConverter]` on `Nullable<G9IconSource>` rather than unwrapping it — 28 `XC0009`s, including the core README's own first sample. The gallery sets icons only from C#, so nothing had ever exercised the attribute form (LES-0029) | `implicit operator G9IconSource(string)` |
| 15 | `G9Strings.UseResources(keyPrefix:)` baked the prefix into the provider, so `Resolve(key)` — documented as taking the CONSUMER's own catalogue key — prefixed it too and found nothing. The carousel's slide titles rendered blank (LES-0031) | prefix moved to its own field, applied on the `G9StringKey` path only |
| 16 | `implicit operator G9IconSource(Enum)` threw on null, so the ordinary `Icon = icons.ResolveOrNull(name)` expression was an `ArgumentNullException` at paint time — into a slot that is already nullable (LES-0031) | takes `Enum?`, maps null to `Empty` |
| 17 | `NU1507` broke the restore of all five projects whenever a CONSUMER's solution drove it: `nuget.config` is not in the chain then, so CPM saw the consumer's sources and `TreatWarningsAsErrors` made it fatal (LES-0030) | `NoWarn=NU1507` in `Directory.Build.props`, with the reasoning there |
| 18 | **`G9HeaderActionButton` could not be instantiated AT ALL.** Its `IconProperty` passed `G9Glyph.Menu` as the default for a `typeof(G9IconSource)` property; `defaultValue` is `object`, so no implicit conversion applies and MAUI throws from the STATIC constructor. Any screen using the control died with a `TypeInitializationException` — one whole tab of the consuming app never opened. The gallery uses this control on no page (LES-0032) | cast the default: `(G9IconSource)G9Glyph.Menu` |
| 19 | **`G9IntroCarousel` hardcoded the source application's logo asset** (a brand `.png` file name) with no way to override it — every other consumer would render a broken image, silently. A string literal survives an identifier-driven rename sweep (LES-0033) | added a `LogoSource` property, defaulting to hidden |
| 20 | **Every directional glyph pointed the wrong way in RTL.** `G9IconView`'s `GraphicsView` inherited `FlowDirection`, so the platform mirrored the canvas ON TOP of the caller's already-correct RTL glyph choice — affecting `G9NavCard`, `G9CascadePanel`, three `G9EdgePanel` sites and the sheet header, plus silently mirroring every non-directional glyph (LES-0034) | pin `G9IconView`'s children to `LeftToRight` |
| 21 | The sheet header's back button was a bare drill-down **chevron** where the source product used a shafted **arrow** — a header chevron reads as an expander, not as "go back" (LES-0035) | authored `G9Glyph.ArrowBack` / `ArrowForward` + `G9Glyphs` slots; header uses them |

**What it did NOT need.** No new dependency in any package, no `InternalsVisibleTo`, and no type
re-added to the app as a private copy. The eight integration hooks (ADR-0004) all held: culture,
strings, icons, images, speech, preferences, diagnostics and background-work suppression were each
wired from the app in one place with no library change.

**Verification state for this consumer** — see "The honest gap"; the visual pass is still the blocker
and it now covers a real app rather than a gallery.

## Packages

| Package | Builds (4 TFMs) | Packs Release | Notes |
|---|---|---|---|
| `G9MAUIControls` (core) | ✅ 0 warn / 0 err | ✅ `1.0.0` | ~51,000 LOC, 184 files. 3 dependencies |
| `G9MAUIControls.Barcode` | ✅ | ✅ | camera dependency on android/ios only |
| `G9MAUIControls.IntroCarousel` | ✅ | ✅ | consumer must call `.UseMauiCommunityToolkitMediaElement(...)` — see below |
| `G9MAUIControls.ProgressOverlay` | ✅ | ✅ | core + `CommunityToolkit.Mvvm` |
| `G9MAUIControls.Persistence.Sqlite` | ✅ | ✅ | **no dependency on the core**, and no UI dependency at all |

## Apps

| App | Builds (4 TFMs) | Release | Android full-trim publish | Notes |
|---|---|---|---|---|
| `G9Controls.Gallery` | ✅ | ✅ | ✅ **0 IL warnings** | every control, both themes, both directions; 6 pages |

**Both consumption modes are verified**, and `G9Controls.Gallery` is now the only place either is —
a second consumer, the G9Node BLE client, moved out of this subtree on 2026-08-14 (it is a product app,
not part of the library deliverable). The gallery inherited its `UseG9Packages` switch so nothing was
lost:

```pwsh
dotnet pack  G9MAUIControls.slnx -c Release -o artifacts/pack
Remove-Item -Recurse -Force ~/.nuget/packages/g9mauicontrols*   # versions are immutable (LES-0025)
dotnet build G9Controls.Gallery -p:UseG9Packages=true      # resolves the packages, no project refs
```

`nuget.config` clears inherited sources so nothing global can shadow the just-built packages, and the
pack output is added as a source by `G9Controls.Gallery.csproj` **only on that path**, via
`RestoreAdditionalProjectSources`. Declaring it globally in `nuget.config` was tried first and was wrong:
`artifacts/pack` is created *by* `dotnet pack`, so a global source made every restore in the solution fail
with `NU1301` until a pack had run — including the restore that pack itself needs. A chicken-and-egg failure
in the default build path, to serve one verification path.

`project.assets.json` was inspected to confirm the libraries resolve as **packages** with a version, on the
version just packed, and that no project reference remains. Read the assets file — a green build proves
nothing about which artifact it compiled against (LES-0025).

This is the check that catches a *packaging* defect rather than a code defect — a type left `internal`, a
missing `.targets`, XAML that resolves through the project graph but not across a package boundary, or a
dependency present in the graph and absent from the `.nuspec`.

**The default stays project references, and that is not laziness.** It is the API-boundary test: a project
reference fails immediately and precisely, where a package reference fails vaguely or silently succeeds
against whatever was last packed. Package mode is an explicit opt-in (`-p:UseG9Packages=true`) because it
answers a different question, not a better one.

## Infrastructure done

| Item | State |
|---|---|
| `G9MAUIControls.slnx` — 5 packages + the gallery | ✅ |
| Shared package metadata in `Directory.Build.props` (ADR-0012) | ✅ SourceLink, `.snupkg`, icon, copyright, license expression |
| `G9MauiLibrary.props` — the shared MAUI-library shape | ✅ (LES-0007 explains why it is a separate file) |
| Family version `1.0.0` (ADR-0010) | ✅ stable: the prerelease gate was "rendered on at least one platform", now met. Every bump is content-driven; LES-0025 records what an unchanged version silently costs |
| Transitive pinning OFF for the library subtree (LES-0006) | ✅ measured: Barcode 7 deps → 3 |
| Consumer migration guide | Written, executed against a real application (Step 3), then **removed from this repository** — it documented one specific application’s migration, which is not library documentation. Executing it exposed six wrong instructions in it; the durable lesson is LES-0028. |
| Package icons | ✅ five 128×128 icons, one per package: shared tile and geometry, one accent colour and one glyph each, so the family is recognisable in a NuGet result list |
| **Anything rendered and looked at** | ⚠ Android only — see "What HAS now been looked at" and "What is STILL unlooked-at" |

## The dependency graph, verified by reading each `.nuspec`

This is what the split was for, so it was measured rather than assumed:

```
G9MAUIControls                      CommunityToolkit.Mvvm, Microsoft.Maui.Controls,
                                    SkiaSharp.Views.Maui.Controls           [all 4 TFMs]

G9MAUIControls.Barcode
  [android] [ios]                   G9MAUIControls, CameraScanner.Maui, Microsoft.Maui.Controls
  [maccatalyst] [windows]           G9MAUIControls, Microsoft.Maui.Controls

G9MAUIControls.IntroCarousel        G9MAUIControls, CommunityToolkit.Maui.Core,
                                    CommunityToolkit.Maui.MediaElement, Microsoft.Maui.Controls

G9MAUIControls.ProgressOverlay      G9MAUIControls, CommunityToolkit.Mvvm, Microsoft.Maui.Controls

G9MAUIControls.Persistence.Sqlite   Microsoft.Extensions.DependencyInjection.Abstractions,
                                    Microsoft.Extensions.Logging.Abstractions,
                                    Microsoft.Maui.Essentials, SQLitePCLRaw.lib.e_sqlite3,
                                    sqlite-net-pcl
```

Three things worth stating plainly, because each was a design goal rather than an accident:

- **The camera package appears only on the two platforms with a camera binding**, via
  `GetTargetPlatformIdentifier` conditions — Mac Catalyst and Windows consumers acquire nothing extra, and
  the package still restores on all four TFMs.
- **`Persistence.Sqlite` does not depend on the core**, and takes `Microsoft.Maui.Essentials` rather than
  `Microsoft.Maui.Controls`. A server-side or console consumer can use it without pulling in a UI toolkit.
- **Nothing depends on `SkiaSharp` except the core**, where it serves exactly one feature (the tab bar's FAB
  notch — a genuinely blurred concave path, which a MAUI `Shadow` cannot do without a measured ANR).

`TreatWarningsAsErrors` is on across the family and every analyzer suppression carries a written reason.

## Trim/AOT verification (ADR-0011)

`G9Controls.Gallery` published Release for Android with `AndroidLinkMode=Full -p:PublishTrimmed=true` and all
four UI packages named in `TrimmerRootAssembly`. **The first run failed on four real defects that no build on
any TFM had reported** — two string-path `Binding`s and two lost reflection annotations in `G9IconFonts`. All
four are fixed (the icon-font resolve path no longer reflects at all), and the publish now completes with
**zero** IL warnings. Details in ADR-0011.

The SQLite package was covered by the same check in the app that has since moved out — it is the one
package deliberately **not** trim-clean (ADR-0014): `sqlite-net-pcl` maps by reflection, so
`SqliteRepository<T>` is `[RequiresUnreferencedCode]` and a consumer takes the documented escape hatch
(root its own assembly, one suppression with a written reason). **The gallery does not reference that
package**, so a trimmed publish here no longer exercises it. Re-verify it from whichever app consumes it.

**iOS NativeAOT is still unverified.** Do not describe the family as "AOT verified" until it has been run;
NativeAOT ignores an assembly's `IsTrimmable` claim and trims everything regardless.

---

# ⚠ The honest gap — LARGELY CLOSED 2026-08-14

## What HAS now been looked at

The consuming application, built on this suite, was deployed to an Android emulator (API 36, x86_64),
**signed in against a real server, synced, and driven screen by screen.** This is the first time any of
it has been rendered. It found **six** defects — four before sign-in, two behind it — and every one was
silent: no crash, no log line, just wrong pixels or a screen that would not open. See LES-0029,
LES-0031, LES-0032 and rows 14–18 of the Step 3 table.

Confirmed working, visually, signed in:

| Area | What was actually seen |
|---|---|
| `G9TabBar` + **the SkiaSharp FAB notch** | five tabs, RTL order, selected-tab chip, the concave notch around the FAB, and the notch correctly ABSENT on tabs without a FAB. The one SkiaSharp feature in the suite, previously "tuned by eye" and unobserved |
| FAB sub-menu | expands to four action cards, FAB becomes a close X, `IsSubMenuItem` correctly not treated as a tab change |
| `G9SheetView` | sort sheet, task-detail sheet, and a **stacked sheet over a sheet**; rounded corners, the backdrop card-recede on the page behind, fit-to-content heights opening without a visible settle (the re-seeded values) |
| `G9PopupView` | error popup and a warning confirm, both over a scrim, correct accent per type, dismissing with no scrim residue |
| `G9ChipGroup` | filter chips and sort chips, selected/unselected states, with icons |
| `G9SearchEntry`, `G9TextEntry` | placeholder, magnifier, floating label on focus, the password eye + eye-off vector glyphs, keyboard avoidance |
| `G9HeaderActionButton` | sort/filter buttons including the busy spinner state |
| `G9NavCard` | profile rows and task-detail rows, icon tile + chevron |
| `G9SafeButton` | primary, and the Danger variant |
| Icons | a consumer-supplied brand icon font and Material side by side; no tofu, no wrong-but-plausible glyph found |
| **Runtime language switch** | fa-IR → en flipped strings, layout direction (nav chevron mirrored, step-pill order reversed) and typeface, live, with no page rebuild — hook #1 including `NotifyChanged()`, end to end |
| Persistence | sign-in, initial sync, and real synced rows rendering from the local database |

**No fatal exception anywhere in the session.**

## What is STILL unlooked-at

- **iOS: nothing at all.** Not one pixel, and NativeAOT remains unverified.
- Toast stacking and the toast-above-sheet claim were not reached (no flow in the session raised one).
- The ~15 vector glyphs were only seen at the sizes these screens use — no size sweep, no dark theme.
- **Dark theme was never switched on.** Everything above is the light palette.
- Disabled-state fields (the §15 A5 clipped-floating-label regression) were not exercised.
- The map's own drawing tools, sampling and NFC flows were not entered.

The remaining checklist lives with the consumer, in its own QA test cases.

---

# Step 2 — the consumer apps

## `G9Controls.Gallery` — the verification app

Six pages, each written around what actually breaks rather than around what is easy to show:

| Page | What it is for |
|---|---|
| **Glyphs** | every built-in glyph × 4 sizes × 2 backgrounds. The reason the gallery was built first |
| **Inputs** | every input control empty / filled / with icon / error / counter / disabled |
| **Actions** | all 12 `G9ButtonVariant`s, icon buttons, progress bars (including indeterminate and paused), separators |
| **Overlays** | z-stack proofs, toast stacking, popup variants, the input popup, the progress overlay run / indeterminate / fail-with-retry / top-anchored / standalone-failure, and the two-detent sheet pair (peek → fit, peek → cap + scroll) that proves the 1.0.6 drag/clamp/scroll-gate rules |
| **Navigation** | tab bar + FAB notch, both tab-view styles, expander, nav cards, the platform-handler shimmer band, swipe view |
| **Satellites** | a core `G9TextEntry` beside a `G9BarcodeTextEntry` (same base, different assembly), the barcode accept/reject regex, and the carousel resolving slide keys through a consumer-supplied catalogue |

It registers **no icon font**, on purpose: a complete-looking suite with zero icon configuration is the claim
ADR-0002 / ADR-0003 make, and this is the proof.

# What was extracted, and from where

Source: a production .NET MAUI application, authored by the same owner, whose control layer was lifted
out into this reusable package. The folder paths below are that application's, kept only to show how
the ~51,000 lines were distributed before the split.

| Area | Source folder | LOC |
|---|---|---|
| 25 controls + shared bases | `Common/Components/<controls>` | ~20,000 |
| Bottom sheet + 4 platform handlers | `Common/Components/BottomSheet` | ~10,100 |
| Toast / loader / progress | `Common/Components/Toast` | ~3,400 |
| Popup | `Common/Components/Popup` | ~3,300 |
| Tab bar | `Common/Components/Menu` | ~2,500 |
| Edge drawer | `Common/Components/EdgePeek` | ~2,400 |
| Theme engine (~110 tokens) | `Common/Utils/ThemeManager` | ~1,700 |
| Page base + overlay template | `Common/Bases`, `Resources/ControlTemplates` | ~1,900 |
| Safe-command, colour, registry helpers | `Common/Helpers` | ~1,500 |

Every public type took the `G9` prefix (ADR-0001): the application-shaped names went too —
`ThemePalette` → `G9Palette`, `ThemeManager` → `G9Theme`, `AppPageBase` → `G9PageBase`.

---

# The eight decoupling seams

Each app dependency became a hook a consumer opts into. All are optional; the suite works with none
of them wired.

| Was | Now |
|---|---|
| `AppCultureService` | `G9Culture` — `Configure(...)` + `NotifyChanged()` |
| `AppDictionary.resx` | `G9Strings` — English defaults, `UseResources` / `UseProvider` |
| a brand icon font + `MaterialIcons` (two typed slots per position) | `G9IconSource` (one slot), `G9IconFonts`, built-in vector `G9Glyph` |
| `AppStorage` | `G9Preferences` + `IG9PreferenceStore` |
| `CustomizedCachedImage` (FFImageLoading) | `G9ImageFactory.Factory` |
| `SpeechToText` (CommunityToolkit) | `IG9SpeechToText` + `G9Speech.Provider` |
| `MainActivity` (Android) | `G9AndroidHost` |
| `AdminDiagnostics*`, `SyncSensitiveAreaTracker`, `TimeKeeperHelper`, `AutomaticSyncService` | `G9SafeCommand.DiagnosticsHandler`, `G9ContentViewBase.BackgroundWorkSuppressionFactory`, device clock, — |

Removed dependencies: `MauiIcons.Core`, `MauiIcons.Material`, `CommunityToolkit.Maui`,
`LocalizationResourceManager.Maui`, `Nalu.Maui.VirtualScroll`, `FFImageLoading.Maui`.

---

# Deliberately excluded

| Dropped | Why |
|---|---|
| The onboarding carousel | ~1,600 lines of CommunityToolkit `MediaElement` + ExoPlayer-specific native workarounds. Kept out of the CORE so no consumer carries a media dependency for one onboarding control. **Later shipped as `G9MAUIControls.IntroCarousel`**, which takes that dependency explicitly — ADR-0006, superseded. |
| The barcode entry | Needs a camera-scanner package, and the base `G9TextEntry` already covers the field. **Later shipped as `G9MAUIControls.Barcode`**, which takes the camera dependency explicitly. |
| `SyncProgressOverlayHelper` + `SyncProgressToastView` | Sync-domain: a messenger contract and ~20 sync resource keys. Replaced by `IG9BottomAnchoredOverlay`, which lets any consumer overlay claim the same "toasts stack above me" behaviour. |
| `DebugAwareContentView` | Debug badges driven by a host application's own developer service. |
| `BottomSheetHeightSeeds` (the table) | The mechanism is kept and now public (`G9BottomSheetHeightSeeds.Seed`); the seeded values were one app's measurements. |

---

# Next actions, in dependency order

The docs for everything below are written and reviewed — this is implementation against a settled design,
not open design work.

All of the roadmap below is **done**. It is kept, rather than deleted, because the ORDER turned out to be
the load-bearing part and is worth having on record for the next extraction:

0. **Publish a narrow overlay-hosting contract from the core** — `IG9OverlayHost` +
   `G9OverlayHosts.TryGetCurrent`, with the registry behind it staying internal. Done first, because
   `.ProgressOverlay` could not mount an overlay without it and neither could any third-party one. A narrow
   public seam, not a blanket `public` and not `InternalsVisibleTo` (LES-0009).

1. **The three remaining satellites.** All three failed on the same class of thing: app strings, app message
   contracts, and enums that lived outside the moved folder — mechanical, but needing reading rather than
   scripting (LES-0001). Each one found a further core `internal` that should have been public (LES-0011),
   and one found that a satellite's XAML must qualify `clr-namespace` with `;assembly=` (LES-0010).

2. **The SQLite extension-point layer**, then re-pointing the ported repository at it.

3. **The verification app.** Which immediately found three things nothing else could: a public contract
   describing behaviour the package did not have (LES-0012), a mandatory resource dictionary no consumer
   could merge (LES-0013), and barcode enums stranded in the core (LES-0014).

4. **The node app**, which found the last one — that a trim-relaxed package fails its consumer's publish
   (LES-0015).

**The ordering lesson: build a consumer as early as the API allows, and a second, different consumer after
that.** Between them the two apps found six defects. Zero of the six were findable from the library side:
every one was either an absence of use, an assembly boundary, or a publish-time analysis. Four TFMs × two
configurations × `dotnet pack` found none of them.


# Follow-up work, in priority order

1. **Render everything.** A scratch page exercising all 25 controls, both themes, both directions.
   Nothing below matters until this has been done once.
2. **Look at the 15 vector glyphs** and correct the paths. `Refresh` and `Delete` are the most
   likely to need work; the `Eye` lens is a guess at proportion.
3. **Per-member XML docs.** `CS1591` is suppressed (see the csproj comment) because ~900 ported
   members document behaviour at class level only. Types added during the extraction are fully
   documented; the ported ones should catch up before a public 1.0.
4. **`G9TabBarShadowView` → `SKPathBuilder`.** SkiaSharp 4.x deprecated the mutable `SKPath` API;
   `CS0618` is suppressed file-locally. The FAB-notch silhouette was tuned by eye, so this is a
   port-and-compare job, not a blind rewrite.
5. **Trim the 26 per-control `.md` guides.** Some still describe a control in terms of the screens and
   flows of the application they were written in, and link guide files that are not part of this
   repository. None of that means anything to a package consumer.
6. **Re-check `G9ChipGroup` / `G9TabView` icon caching** after the icon-slot collapse. The §12a
   "cache the long-lived child, never re-create it" contract still holds structurally, but the
   signature strings it compares were rewritten.

---

# 1.0.5 — G9TabBar FAB interception

`G9TabBar` gained `FabTapped` (`G9TabBarFabTappedEventArgs.Handled`), raised before the control reacts
to a centre-FAB tap. It exists because a consumer needed the `+` to open its own bottom sheet instead
of the radial sub-menu, and the only previously available approach — watching `IsFabOpen` and closing
it again — fires after the fan-out has already opened and after `SelectedIndex` has already moved to
the FAB slot, so it flashes visibly.

Additive by construction: with no handler, or `Handled = false`, the FAB path is byte-for-byte the
1.0.4 behaviour. Documented in `TabBar/G9TabBar.md` → *Intercepting the FAB tap*.

---

# 1.0.6 — the bottom sheet learned what a detent is (2026-09-01)

Three reported symptoms on one sheet (a peek→medium map-layers picker in the consuming app), one
cause. `G9SheetView` derived its drag limits from the state it was IN rather than from the set of
detents it was ALLOWED to rest at, and `G9SheetViewAllowedState` has no way to say "there is a peek
step under this" — so a two-detent sheet could be dragged clear to the status bar, was snapped back
to whatever ratio the caller had guessed for its medium step (empty band under the content), and a
downward drag from that step raised a CLOSE request instead of stepping back to the peek.

What shipped:

- **`AllowCollapsedState`** on the control (helper-set via `HasPeekDetent`) — the missing bit.
  Everything else keys off it, and it is `false` for every single-detent sheet, which is what keeps
  full-screen / single-state / fit-to-content behaviour byte-for-byte unchanged.
- **A real clamp** (`ClampDragTranslation`) to the largest allowed detent, and to the smallest for a
  non-cancelable sheet. Below the smallest detent a cancelable sheet keeps its height and slides off
  rather than shrinking.
- **`ExpandedFitsContent`** — the top detent is the MEASURED content, capped by
  `MaxFitToContentHeightRatio`; taller content stops at the cap and scrolls inside a viewport the
  helper hosts; shorter-than-peek content lowers the peek too. Material's `fitToContents`, applied to
  the top detent, reusing the fit-to-content measuring tiers rather than a second engine.
- **`ScrollingExpandsSheet`** (default ON) — a drag on a scrollable body expands the sheet before the
  content scrolls. UIKit's `prefersScrollingExpandsWhenScrolledToEdge`, whose default is likewise on.
  A no-op for single-detent sheets **by construction**, which is what makes that default safe.
- Two corrections found on the way: the helper never actually applied `IsCancelable` /
  `DragCloseThreshold` to the control although the guide said it did; and the iOS/Windows handlers
  resolved the INNERMOST scroller under the finger, so a horizontal row inside a body reported
  "cannot scroll" for vertical drags and stole them from the body's own scroller.

**Verified on Android (emulator, Pixel-class), both apps.** The gallery gained two Overlays buttons —
short body (settles exactly at the content) and tall body (stops at the cap, scrolls there, does not
scroll at the peek) — and the consuming app's layers sheet was driven through the whole cycle with
`adb input`. Not exercised on iOS / Mac Catalyst / Windows: they build and the gate is shared code,
but the three handler edits are per-platform and unrendered. See ADR-0019, LES-0041.

---

# 1.0.11 — a text area that knows when to stop growing (2026-09-07)

`G9Editor.MaxEditorHeight`. `AutoSize=TextChanges` grows the box with the text and never stops, which
is correct in a scrolling page and wrong in a form with anything BELOW the field. The report form
that shipped with 1.0.10 is the case: type a long description into a fit-to-content bottom sheet and
the Cancel/Save row is pushed off the bottom of the screen — the user is left typing into a control
whose Save button they can no longer reach, with no way back except deleting text.

The fix is to constrain the MEASURE rather than to add a scroller. A platform text view is already
scrollable; it simply never had a reason to be, because nothing was bounding it. `MaximumHeightRequest`
on the inner editor gives it that reason, and the growth-then-scroll behaviour falls out.

Two details worth keeping:

- **The outlined box is capped too**, at the ceiling plus `InnerContentPadding`. Capping only the
  editor leaves the outline growing around a field that has already stopped — empty space under the
  last line, which reads as a stuck control.
- **`Math.Abs(inf - inf)` is `NaN`**, and every comparison against `NaN` is false. The defensive
  "has this actually changed?" guard that every other height write in `ApplyEditorProperties` uses
  would therefore have rewritten the UNCAPPED value on every pass, so the uncapped case gets its own
  equality helper.

Default `0` = no ceiling, so every existing editor is untouched, and the property is ignored under
`AutoSize=Disabled`, which already pins the height.

---

# 1.0.10 — dictation is a SUITE capability, not a search-box feature (2026-09-07)

A consumer needed a microphone on a title field and on a description text area. The suite had exactly
one, welded into `G9SearchEntry`: ~180 lines of session state — permission re-check, cancellation,
partial-result append, the listening visual — sitting in a control whose actual job is a debounced
query.

**The engine is now [`G9VoiceDictation`](../G9MAUIControls/Localization/G9VoiceDictation.cs)**, a small
control-agnostic class holding one session against `G9Speech.Provider`. `G9TextEntry` owns the
`VoiceEnabled` / `VoiceCulture` properties, the three events, `IsListening` and the three methods;
`G9SearchEntry` keeps only its default (`VoiceEnabled = true`) and its search behaviour; `G9Editor`
gained the same API. Consumers see no break — `G9SearchEntry` inherits every member it used to
declare — and the reason for doing it this way rather than a shared base is ADR-0020: the two
controls that needed it do not share one (`G9SearchEntry : G9TextEntry`, but `G9Editor` is a sibling
under `G9OutlinedFieldBase`, whose job is outline and notch geometry, not speech).

Two behaviour changes came with it, both fixes:

- **The microphone no longer disappears mid-session.** `ShouldShowVoiceMic` was `VoiceEnabled &&
  string.IsNullOrEmpty(Text)`, and a live transcript writes into `Text` — so the first recognized word
  removed the only control that could stop the session. It is now `IsListening || !HasContentValue`.
- **No provider, no microphone.** `IG9SpeechToText`'s own documentation says the mic is hidden unless
  a provider is registered, and the gallery page says so on screen; the code never checked. It does
  now (`G9VoiceDictation.IsAvailable`).

On the editor the affordance is deliberately NOT value-gated (there is no clear button to hand the
slot to, and a long description is what people dictate) and it is pinned to the BOTTOM of the box —
a microphone floating beside the middle of a paragraph reads as part of the text.

Also in this release: **`G9PopupInputField.Text` no longer forces `LeftToRight`.** It defaulted to LTR
because its sibling factories do, where the pin is correct for a real reason — a phone number, an
email and a password are written left-to-right in every language. Prose is not. In a Persian app the
symptom was a "Title" box whose caret sat on the left and whose text ran away from its own label.
`Phone` / `Email` / `Password` keep the pin. `Text` and `TextArea` also gained `enableVoice:`.

---

# 1.0.7 — a per-item icon colour, and a value that stays next to its icon (2026-09-02)

Two rendering defects, both reported by a consumer's dynamic-attribute option pickers, both verified on
Android before and after.

**The list ignored `G9SelectionItem.IconTintColor`.** The triggers honoured it, so a consumer projecting
coloured options got grey rows and a correctly-coloured trigger and read it as the colours simply not
being applied.

The fix is one line; **finding the second writer is the story.** `CreateRow` builds a row and
`UpdateRowVisuals` re-styles it in place on every selection change — and the second carried its own copy
of the colour rule, overwriting what the first had just set. Fixing only `CreateRow` was provably
present in the built assembly and changed nothing on screen, which sent the investigation into the
toolchain (was it deployed? was it source mode?) rather than into the code. Both now call one
`ResolveRowIconColor`. Full lesson in **LES-0043** — including the diagnostic rule, which is worth more
than the fix: when a change that is provably in the binary has no effect, look for a second writer
before re-examining the build.

**Both triggers pushed the value away from its own icon under RTL.** `G9ComboBox` and `G9Picker` each
had `HorizontalTextAlignment = IsRtl ? End : Start` on a label that was ALSO being given the culture's
flow direction — so the ternary did not select the reading edge, it inverted it — and each label was
`Fill` + `Grow`, claiming the leftover width so the text landed on whichever edge the layout resolved.
Alignment is `Start` in both now and the labels are sized to their text. Two controls with the same
mistake is a habit rather than a slip ("RTL means align right" sounds true and is not), which is the
point of **LES-0042**.

---

## 1.0.14 — a cached field icon that survived its own detachment

**`G9ComboBox` lost its search glyph for good after a value had been picked and cleared** — magnifier
to clear-x to *nothing*, with the trailing slot left empty for the rest of the field's life. A field
that had never been touched looked correct, and that asymmetry is what made it read as a combobox bug
instead of what it was: a caching bug in `G9OutlinedFieldBase`, affecting every control that mixes a
default icon with a subclass-supplied one.

`SetIconHostContent` clears the host wholesale (it keeps only the ripple `GraphicsView`), so attaching
a subclass affordance DETACHES the cached default `G9IconView` — while the cache field still points at
it. On the way back, the null-check that means "do I have one?" answered yes, the "just recolour"
branch ran, and nothing ever put the view back in the tree. Both `ShowDefaultTrailingIcon` and
`ShowDefaultLeadingIcon` now check membership rather than nullness. Full lesson in **LES-0046** — the
generalisable half is that *a cache field is not proof of attachment* whenever one code path caches a
view and another is allowed to clear its parent.

---

# Known risks

# Known risks

| Risk | Note |
|---|---|
| Vector glyphs unreviewed | Cosmetic, but they are the first thing anyone sees. |
| `CollectionView` picker is new code | Selection identity restore + VSM tint have no test behind them. |
| `G9PageTemplate` z-stack unverified | The whole popup/sheet/toast layering contract depends on it. |
| Android `G9AndroidHost` unwired anywhere | Two features silently inert until a host opts in — by design, but untested. |
| Public API not frozen | Nothing has consumed the package yet, so names are still cheap to change. Change them before publishing, not after. |
