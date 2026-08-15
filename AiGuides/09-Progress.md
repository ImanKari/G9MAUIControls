# 09 - Progress

# G9MAUIControls
## Project Progress & Execution Log

---

# Current Status

**Step 1 (package ecosystem) COMPLETE: five packages build and pack. Step 2 COMPLETE: consumed by a real
app on all four TFMs, in both project- and package-reference mode. Outstanding: the visual pass, which
needs a human eye, and iOS NativeAOT.**

Last updated: **2026-08-15**

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
| **Overlays** | z-stack proofs, toast stacking, popup variants, the input popup, and the progress overlay run / indeterminate / fail-with-retry / top-anchored / standalone-failure |
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

# Known risks

| Risk | Note |
|---|---|
| Vector glyphs unreviewed | Cosmetic, but they are the first thing anyone sees. |
| `CollectionView` picker is new code | Selection identity restore + VSM tint have no test behind them. |
| `G9PageTemplate` z-stack unverified | The whole popup/sheet/toast layering contract depends on it. |
| Android `G9AndroidHost` unwired anywhere | Two features silently inert until a host opts in — by design, but untested. |
| Public API not frozen | Nothing has consumed the package yet, so names are still cheap to change. Change them before publishing, not after. |
