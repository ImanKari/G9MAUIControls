# 10 - Decisions

# G9MAUIControls
## Architecture Decision Records

Numbered from ADR-0001 in this subtree, and independent of any other ADR series in the wider repository.

---

## ADR-0001 — One `G9*` prefix for every public type

**Decision.** Every control, base class, drawable, metric and enum carries the `G9` prefix. Types that
arrived with an application-shaped name were renamed on the way in: the sheet views, the palette, the
theme manager and the page base all became `G9SheetView*`, `G9Palette`, `G9Theme`, `G9PageBase`.

**Why.** This code began life inside an application, and carried that application's brand prefix. In a
package called `G9MAUIControls` a foreign prefix reads as two brands stitched together, and a consumer
who types it has no way to know why it is there. The rename is mechanical, done once, at the only
moment it is cheap: before anything consumes the package. Doing it later means breaking every consumer
and re-writing 26 per-control guides.

**Cost.** Guide prose written against the old names is now slightly off, and 26 guides need a prose
pass — recorded as follow-up in `09-Progress.md`.

---

## ADR-0002 — One `G9IconSource` slot per position, not one slot per font

**Decision.** The two typed icon slots the original design had per position — one per icon font it
knew about — collapse into one `G9IconSource?`. Any icon-font enum converts to it implicitly:
font family = the enum's **type name**, glyph = its `[Description]`.

**Why.** A control library cannot know which icon font its consumer uses. A slot typed against a
specific enum welds the library to that font, forces its package on every consumer, and needs a NEW
slot on every control the moment somebody adds a second font — which is exactly how the source ended
up with two. Carrying *(font family, glyph)* in a value type makes the consumer's own font a
first-class citizen instead of a special case.

The type-name-as-family convention is not invented here: it is what icon-font enum generators in this
ecosystem already emit, which is why an existing icon enum works with **no adapter and no change**.

**Alternatives rejected.**
- *Keep depending on `MauiIcons.Material`.* Fastest, and it forces a Material font onto consumers who
  only ever use their own glyphs.
- *`object?` slots.* No compile-time help, no XAML converter, no discoverability.

---

## ADR-0003 — The library's own chrome glyphs are vector paths, not a bundled font

**Decision.** The ~15 glyphs the controls draw for themselves (chevron, clear ×, eye, search, check,
calendar, clock, mic, popup type accents, plus/minus, menu, refresh, delete) are authored as `PathF`
geometry on a 24×24 design grid in `G9GlyphDrawable`, rendered by a `GraphicsView`.

**Why.** The alternatives both fail for a package:
- *Depend on an icon package for defaults* — puts a multi-megabyte font in every consumer's app so
  that a combo box has a chevron.
- *Bundle a subset TTF* — adds a binary asset to maintain, and a font can silently fail to resolve
  inside some Android native renderers and paint a tofu box (catalogued as A2 in
  `Controls/G9Controls.md` §15). The suite hits exactly that hazard in `G9SwipeView`.

Paths cannot tofu, cost nothing to package, stay crisp at any size and density, and let the suite look
complete with **zero** icon configuration. Every default is individually overridable through
`G9Glyphs`, so a consumer with a house icon set replaces them wholesale.

**Cost.** Hand-authored geometry that has not yet been looked at (`09-Progress.md`, "The honest
gap"). A path that is subtly off-centre is a real possibility and only an eye will catch it.

---

## ADR-0004 — Eight optional integration hooks instead of a required host contract

**Decision.** Every app dependency becomes a static hook or an interface the consumer opts into.
None is mandatory; with nothing wired the suite works and the affected feature is inert.

**Why.** The realistic alternatives were a required `IG9Host` interface — which turns "add a package"
into "implement twelve members before you can render a button" — or a base `MainActivity` /
`Application`, which is impossible for anyone already inheriting something else. Opt-in hooks compose;
required contracts compete.

**The one exception is the page host.** `G9PageBase` + `G9PageTemplate` are NOT optional for the
overlay layers: popup, toast and bottom sheet all resolve their host through
`G9ModalHostRegistry` → the template's six-layer z-stack (`BackdropHost` / `ContentHost` /
`OverlayHost` / `PopupHost` / `ToastHost` / `DevHost`). Sibling order in that template IS the
z-order, and it is what makes "a toast opened inside a sheet keeps showing after the sheet closes"
work without per-toast tracking. Shipping the overlays without it is not possible.

---

## ADR-0005 — This repository stops the MSBuild property walk

**Decision.** `Directory.Build.props` and `Directory.Packages.props` do not import the
repository root equivalents.

**Why.** The root `Directory.Build.props` pins `<TargetFramework>net10.0</TargetFramework>` for the
server solution; a multi-targeted MAUI project inheriting a single-TFM pin fails restore outright.
The root's `NoWarn` and `AnalysisLevel` choices also belong to that solution's standards, and a
redistributable package needs a stricter warning policy than an app does — a package must not ship
warnings its consumers inherit. MSBuild stops at the first props file it finds walking up, so this
repository's own copies are the seam — which is what keeps a build correct when the tree is vendored
inside a larger repository.

**Consequence.** MAUI package versions live in `Directory.Packages.props` and are *not* visible
to the root solution. Adding a package there is a decision with weight: every reference becomes a
dependency in the published `.nupkg`.

---

## ADR-0006 — Keep the onboarding carousel out of the CORE — *superseded*

> **Superseded.** It shipped, in the shape this ADR predicted: `G9MAUIControls.IntroCarousel`, a
> satellite that takes the media dependency explicitly. The decision below stands as written for the
> *core*; only the "not extracted at all" part was overtaken.

**Decision.** The onboarding carousel does not go in the core package.

**Why.** ~1,600 lines, of which the substance is CommunityToolkit `MediaElement` plumbing plus native
ExoPlayer workarounds specific to that package (handler-disconnect ordering, a
`VisualDiagnosticsOverlay` de-initialization dance, `MauiMediaElement` FrameLayout transparency).
Keeping it in the core means keeping the media dependency for every consumer, for one login-screen
control. Its value *is* the video-slide handling, which cannot be separated from a media package.

**Outcome.** A satellite package, which is the general answer this family gives to exactly this shape
of problem — see ADR-0009.

---

## ADR-0007 — Replace Nalu `VirtualScrollView` with MAUI `CollectionView` in the list picker

**Decision.** `G9BottomSheetListPickerModal` is rewritten on `CollectionView`. Nalu's
`IVirtualScrollSelectionIdentity` becomes the suite's own `IG9SelectionIdentity`.

**Why.** The virtual scroller was one project's performance choice and a third-party dependency for a
picker that shows tens of rows, not thousands. `CollectionView` is in the box.

**What the rewrite removed, and what replaced it.** Nalu needed a *resolved viewport* before
`SetItemsSource` would work, so the original waited up to 60 frames for eight consecutive stable
measurements before populating. `CollectionView` needs none of that — the wait loop is gone entirely.
What did NOT come for free is the selection visual: `CollectionView`'s native selection differs on
every target (full-bleed accent on Windows, ripple-tinted on Android, sometimes nothing on iOS), so
the row now paints its own tint through a `VisualStateManager` group. That is new, unrun code.

---

## ADR-0008 — Keep `TreatWarningsAsErrors`, suppress with written reasons

**Decision.** Warnings stay errors. Each suppression in the csproj carries a comment saying why the
rule is *wrong here*, not merely inconvenient.

**Why.** A package that ships warnings hands them to every consumer. But several rules are genuinely
wrong for this codebase, and the biggest is not a matter of taste: `CS0169` / `IDE0044` fire on ~300
`[AutoBindable]` backing fields as **false positives**. Those fields exist only to declare a bindable
property's name, type and default; the generated property reads through `BindableProperty`
`GetValue`/`SetValue`, never the field. The compiler cannot see the use, and making the field
`readonly` would break the generator. Verified against the emitted `*.generated.cs`, not assumed.

`CS1591` (missing XML docs) is suppressed for a different and weaker reason: XAML-generated partials
emit public types nobody authored, and ~900 ported members document at class level only. That one is
a debt with a plan (`09-Progress.md` item 3), not a principled exemption.

---

## ADR-0009 — One reserved prefix, satellite packages named after the capability they add

**Supersedes the exclusions in ADR-0006**, and the "drop it" halves of the barcode and sync-overlay
decisions. Those components stay in the ecosystem; they leave the *core package*.

**Decision.** The ecosystem is one NuGet ID family under the `G9MAUIControls` prefix. The core holds
everything with minimal dependencies. Anything that drags a heavy or opinionated dependency, or that is
domain-shaped rather than general, ships as its own satellite named after the capability it adds:

| Package | Adds | Extra dependency |
|---|---|---|
| `G9MAUIControls` | 25 controls, sheet, popup, toast, tab bar, edge panel, theming, hosting | none beyond ADR-0008's three |
| `G9MAUIControls.IntroCarousel` | `G9IntroCarousel` — onboarding slides with video | `CommunityToolkit.Maui.MediaElement` |
| `G9MAUIControls.Barcode` | `G9BarcodeTextEntry` + its scan surface | a camera-scanner package |
| `G9MAUIControls.ProgressOverlay` | staged progress overlay: cancel, retry, terminal states | none — see below |
| `G9MAUIControls.Persistence.Sqlite` | SQLite repository / query builder / migrations | `sqlite-net-pcl` |

**Why split, in the guidance's own words.** The .NET library guidance is explicit: *"DO review your
.NET library for unnecessary dependencies"*, because *"it's not possible to know what packages will be
used alongside your own"* and the mitigation for diamond-dependency breakage is to minimise dependency
count. A consumer who wants a text entry should not acquire a media stack, a camera stack and an ORM to
get one. Splitting is the standard remedy, not a preference.

**Why this naming shape.** `CommunityToolkit.Maui` is the closest analogue in the ecosystem — a MAUI
control suite whose dependency-heavy pieces ship as `CommunityToolkit.Maui.MediaElement`, `.Camera`,
`.Maps`. That establishes both halves of the convention followed here: **one reserved prefix** (prefix
reservation earns the verified badge and is granted per prefix, so fragmenting into unrelated IDs costs
discoverability and trust) and **satellites named after the capability**, not after the dependency they
happen to use. Hence `.Barcode`, not `.Camera`; `.ProgressOverlay`, not `.Sync`.

**`.ProgressOverlay` adds no dependency and is still split.** Its justification is API-surface hygiene,
not dependency weight: it is an *opinionated* component (a four-state machine with a cancel contract, a
retry affordance, a message-driven progress source) where the core deliberately ships only the generic
seam, `IG9BottomAnchoredOverlay`. Keeping it out keeps the core surface small and lets the overlay
iterate without moving the core's version. Folding it in later is a non-breaking merge; pulling it out
later would be breaking. So: split now, cheaply.

**The `Persistence.Sqlite` name carries a known tension, recorded deliberately.** That package does
**not** reference the core and has nothing to do with controls, so nesting it under a `*Controls` ID is
imperfect — NuGet ID hierarchy implies a relationship the assembly graph does not have. It is named
this way anyway because the ecosystem is meant to be one discoverable, prefix-reservable family, and a
consumer browsing `G9MAUIControls.*` should find it. **This is the last moment the rename is free.** If
it is ever renamed, do it before first publish; `G9Persistence.Sqlite` is the sibling-family
alternative, following how `CommunityToolkit.Mvvm` sits beside `CommunityToolkit.Maui` rather than under
it.

**Alternatives rejected.**
- *One package with optional dependencies.* NuGet has no optional-dependency concept; every dependency
  in a package is acquired by every consumer.
- *Multi-target the extras behind `#if`.* The dependency still lands in the package graph.
- *Shared-source packages* — the guidance's own suggestion for small pieces, and explicitly unsuitable
  here: *"DO NOT have shared source package types in your public API"*, and these are all public API.

---

## ADR-0010 — Prerelease until something has actually been rendered — *condition met at 1.0.0*

**Decision.** Every package in the family shipped prerelease until the suite had been rendered and
exercised on at least one platform. **That condition was met, and the family released `1.0.0`.**

**Why the gate existed.** The guidance is unambiguous — *"DO publish a package as a pre-release package
if it is non-stable or a preview"* — and at the time the honest status was: compiles on four target
frameworks, packs, and nothing has been run. A `1.0.0` would have made a stability promise the project
had not earned, and it cannot be withdrawn: consumers pin to it, and SemVer then forbids the breaking
fixes a first real render will almost certainly demand. That was the right call — the first real render
found 21 defects, six of which no build could have caught.

**Why the gate is now satisfied.** The controls are not new code. They have been carrying a production
application for a long time; what was unproven was the *package boundary*, and that boundary has now
been exercised end to end by that same application on Android — ~51,000 lines of consumer code, every
seam, on a device. The remaining unknowns are per-platform rendering (iOS, Mac Catalyst, Windows), which
`09-Progress.md` states plainly. Those are gaps in verification breadth, not signs of an unstable API,
and SemVer has a mechanism for what they might produce: a minor or patch release.

One mechanical consequence, which is why the family crossed together: **a stable package cannot depend
on a prerelease package.** Moving one at a time is not possible.

---

## ADR-0011 — Trim/AOT posture: declare, verify with a real consuming app, claim nothing more

**Decision.** A package sets `IsAotCompatible` (which implies `IsTrimmable`) **only when its whole
dependency closure supports the claim**; the rest set `EnableTrimAnalyzer` alone. Verification is a
consuming app published with full linking, not the library build. `VerifyReferenceTrimCompatibility`
stays **off**.

**Corrected 2026-08-09.** This ADR originally read "every package sets `IsAotCompatible`", which was never
true of the codebase — only two of the five do, and each omission was deliberate and reasoned in its own
`.csproj` from the start. The ADR was the thing that was wrong. What the family actually declares:

| Package | `IsAotCompatible` | `EnableTrimAnalyzer` | Why |
|---|---|---|---|
| `G9MAUIControls` | ✅ | implied | own code is clean; every dependency is annotated |
| `G9MAUIControls.ProgressOverlay` | ✅ | implied | same |
| `G9MAUIControls.Barcode` | ❌ | ✅ | `CameraScanner.Maui`'s platform camera bindings are not trim-annotated |
| `G9MAUIControls.IntroCarousel` | ❌ | ✅ | `CommunityToolkit.Maui.MediaElement`'s platform players are not annotated |
| `G9MAUIControls.Persistence.Sqlite` | ❌ | ✅ | `sqlite-net-pcl` maps by reflection (ADR-0014) |

**The rule that produces that table: the claim covers the package AND everything it drags in.** A package
whose own code is clean but whose dependency is not is *not* AOT compatible from the consumer's point of
view — the consumer publishes one app, not five libraries. So `IsAotCompatible` is set only where the whole
closure holds, and `EnableTrimAnalyzer` alone is set elsewhere: warnings stay visible, no promise is made.

A false claim is worse than an absent one. NativeAOT ignores an assembly's `IsAotCompatible` and trims
everything regardless, so the claim buys nothing at publish time and converts a build warning into a
runtime failure.

**Why each part.**

- **`IsAotCompatible` over bare `IsTrimmable`** — it implies `IsTrimmable` *and* enables the AOT
  analyzers. This matters more each release: MAUI moves to CoreCLR on mobile in .NET 11, NativeAOT is
  already supported on iOS / Mac Catalyst, and Android is in progress. Clean now is cheap; retrofitted
  later is not.
- **A library build does not find the warnings.** Per the trimming guidance, when building a library
  *"the implementations of the dependencies aren't available"* and reference assemblies carry too little
  information — so project-level analysis only sees what the library itself does. Full coverage needs a
  consumer published with `PublishTrimmed` and the library named in `TrimmerRootAssembly`, which makes
  the trimmer treat every path in it as reachable.
- **The honest MAUI caveat.** The guidance's recipe is a plain console app, which cannot reference a
  MAUI library targeting platform TFMs. The equivalent here is the scratch app published Release for
  Android with `AndroidLinkMode=Full`, plus iOS with NativeAOT. Until that has been run, the correct
  claim is "declared and analyzer-clean", **not** "verified trim-safe" — NativeAOT ignores an
  assembly's `IsTrimmable` claim and trims everything regardless, and an app is not guaranteed to work
  unless there are **zero** trimmer warnings.
- **`VerifyReferenceTrimCompatibility` off.** It warns (IL2125) for any reference lacking `IsTrimmable`
  metadata. It is opt-in precisely because many trim-clean libraries have not added the metadata, so the
  noise would be about our dependencies rather than our code. Revisit per package once each surface is
  clean.

**Outcome — run, and it earned its keep.** `G9Controls.Gallery` published Release for Android with
`AndroidLinkMode=Full -p:PublishTrimmed=true`, all four UI packages named in `TrimmerRootAssembly`. The
first run **failed** (`NETSDK1144`) on four findings, none of which any Debug or Release *build* on any of
the four TFMs had reported:

| Finding | Where | Fix |
|---|---|---|
| IL2026 - string-path `Binding` | `G9DrumColumn.CreateRow` | assigned directly; the source property is `init`-only, so the binding could never have fired twice |
| IL2026 - string-path `Binding` | `ProcessingSheetContentView` | replaced with a `PropertyChanged` handler; one bool, one target, on self |
| IL2067 - annotation lost through a dictionary | `G9IconFonts.Resolve` | see below |
| IL2070 - `Type.GetField` demands `NonPublicFields` | `G9IconFonts.TryReadMember` | see below |

The icon-font pair mattered most, because by-name icon resolution is a headline feature. A `Type` pulled out
of `Dictionary<string, Type>` carries no `[DynamicallyAccessedMembers]`, so no annotation on `TryReadMember`
could ever satisfy the trimmer - and suppressing it would have been a lie: under a full trim the enum's
fields really can be removed, and `Resolve("MyIcons.Valve")` would start returning `null` **in release
builds only**. The fix removes reflection from the resolve path entirely: `Register<TEnum>()` already walks
the members inside a generic context where they are statically rooted, so it now snapshots them into a
per-font map and `Resolve` does dictionary lookups. Trim-safe by construction rather than by annotation, and
faster.

After those four fixes the full-trim publish completes clean - **zero** IL warnings. The claim may now be
"verified trim-safe on Android under full linking". iOS NativeAOT remains unverified and is still an open
item in `09-Progress.md`; do not upgrade the claim to cover it until it has actually been run.

---

## ADR-0012 — Metadata completed to the authoring checklist, with SourceLink

**Decision.** Every package sets the full recommended set: `PackageId`, `PackageVersion`, `Title`,
`Description`, `Authors`, `Copyright`, `PackageTags`, `PackageIcon` (128×128 PNG, transparent
background), `PackageReadmeFile`, `PackageProjectUrl`, `PackageLicenseExpression`,
`PackageReleaseNotes`, and SourceLink. Symbols ship as `.snupkg`.

**Why the non-obvious ones.**

- **`PackageLicenseExpression`, never `LicenseUrl`.** The URL form is deprecated *because* it is legally
  ambiguous: changing the license at that URL retroactively changes the displayed license for every
  version already published.
- **SourceLink rather than hand-written repository metadata.** It sets `RepositoryUrl` /
  `RepositoryType` itself *and* records the exact commit the package was built from, which is what makes
  step-into debugging work for a consumer.
- **`.snupkg` rather than embedded PDBs.** Embedding costs roughly 30% package size for everyone; a
  symbol package is fetched on demand, only by someone actually debugging.
- **A README per package, not one shared.** The README *is* the package page. A shared one would
  describe features the consumer did not install.

**Dependency version shape:** plain minimum versions through Central Package Management. Per the
guidance — never omit a minimum, avoid exact pins, avoid upper bounds. An upper bound guarantees a
restore failure the first time a consumer legitimately needs a newer transitive version.

---

## ADR-0013 — `dotnet pack -c Release` is part of "it builds"

**Decision.** A change is not done until `dotnet pack -c Release` succeeds for every package.

**Why.** XAML compilation only runs in Release. Debug builds passed on all four TFMs while seven
`XamlC` errors sat in five files — stale `xmlns` declarations and a renamed markup extension that no C#
file referenced, so nothing else could have caught them. See LES-0002 in `11-EngineeringLog.md`.

---

## ADR-0014 — The SQLite repository is `[RequiresUnreferencedCode]`, not made trim-safe

**Decision.** `SqliteRepository<T>` carries `[RequiresUnreferencedCode]` **on the type**, and the package
sets `WarningsNotAsErrors` for the reflection-related IL codes (IL2026, IL2070, IL2072, IL2075, IL2077,
IL2087, IL2090, IL2091, IL2111) rather than suppressing them. A consumer needing full trimming keeps its
entity types in an assembly named in `TrimmerRootAssembly`.

**Why.** `sqlite-net-pcl` maps entities by reflecting over public properties and attributes at runtime.
That is not an implementation detail that can be annotated away - it is the library's entire design.
Building the package produced ~84 IL2087 and friends, exactly as expected for a reflection-based mapper.
Three options existed:

1. **Annotate through.** Impossible past the first hop: the mapper takes a `Type`, and the annotation is
   lost the moment a `Type` is stored in a collection - the same wall hit in `G9IconFonts` (see ADR-0011),
   where the fix was to *delete the reflection*. That option is not available here, because the reflection
   belongs to the dependency.
2. **Suppress.** Would produce a package claiming trim-safety it does not have, and the failure mode is
   silent: a trimmed app loses a property and that column simply stops round-tripping.
3. **Declare it.** `[RequiresUnreferencedCode]` propagates to every use, so a consumer gets a build-time
   warning naming the exact call site, plus a documented escape hatch.

Option 3 is the only honest one. The attribute sits on the **type** rather than on each member
deliberately: member-level attributes would let a consumer construct the repository warning-free and only
learn about the constraint at whichever member they happened to call first.

**`WarningsNotAsErrors` rather than `NoWarn`.** The warnings stay visible in every build. They are a
standing description of a real constraint, and silencing them would mean a *new* reflection dependency
added later blends into an already-silent baseline (ADR-0008's rule, applied).

**The consumer escape hatch is TWO things, not one** — amended after a real consumer's trimmed publish
proved the original wording insufficient (LES-0015):

1. `<TrimmerRootAssembly Include="YourEntityAssembly" />`, so the entity properties actually survive the
   link; **and**
2. `<WarningsNotAsErrors>$(WarningsNotAsErrors);IL2026;IL2070;IL2077;IL2087;IL2091;IL2111</WarningsNotAsErrors>`
   in the consumer's own project.

(2) is unavoidable and was initially missed. `WarningsNotAsErrors` is per-project: the package's setting
governs the package's compilation only. When a consumer publishes with `PublishTrimmed`, ILLink re-analyses
the package's IL and attributes every finding to the **consumer's** project, where the codes are errors
again — and `[SuppressMessage]` in consumer code cannot reach diagnostics raised inside another assembly.
Before this was understood the app's publish failed with `NETSDK1144` and ~280 trim errors, none of which
any build on any of the four TFMs had reported.

Most of that count was not irreducible. Propagating `[DynamicallyAccessedMembers(All)]` through the whole
generic chain — repository, four query builders, five accessor interfaces *and* their implementations, plus
generic method type parameters — reduced it from ~280 to ~20, and the annotation is better than a
suppression because it makes the trimmer **preserve** the members rather than merely stop warning. The ~20
that remain are the mapper's true reflection, where the `Type` arrives from a dictionary and no annotation
can be expressed.

**No trimmer descriptor ships, and that is a decision rather than an omission.** The package README and
`04-SqlitePersistence.md` once said a `TrimmerRootDescriptor` XML shipped; it never existed (the csproj
packs `build\*.xml` only `If Exists` — IN-30), and the claim has been removed rather than made true. The
library has no reflection targets of its own to root: what must survive the link is the **consumer's**
entity assembly, which a package cannot name. A descriptor preserving this assembly would look like a fix
and change nothing, so (1) above stays the consumer's line to write.

**What would change this.** A source-generated mapping layer replacing `sqlite-net-pcl`'s mapper outright.
That is a v2 conversation: it changes the persistence engine, not the packaging.

---

## ADR-0015 — `sqlite-net-pcl`, not `Microsoft.Data.Sqlite`

**Decision.** `G9MAUIControls.Persistence.Sqlite` depends on `sqlite-net-pcl` plus
`SQLitePCLRaw.bundle_green`, matching the architecture the source guide describes, rather than porting to
`Microsoft.Data.Sqlite`.

**Why not `Microsoft.Data.Sqlite`.** It is the better-supported package in the abstract, and on a green
field it would be the default. Here it is the wrong trade:

- **It is ADO.NET, not an ORM.** The ported architecture is built on `sqlite-net`'s attribute mapping,
  `CreateTable<T>`, and typed `Query<T>` / `Table<T>`. Switching means writing the mapping layer, which is
  the bulk of the reusable value - and rewriting the *entire* ported repository while simultaneously
  extracting it would mean shipping code nobody has ever run.
- **It does not remove the trim problem, it relocates it.** Hand-written `SqliteDataReader` mapping is
  trim-safe; a *generic* `SqliteRepository<T>` over it is not, unless the mapping is source-generated. The
  IL warnings of ADR-0014 would return with a different stack trace.
- **`bundle_green` is the right native provider for MAUI.** It bundles SQLite for every target platform,
  including those where the OS-provided library is absent or an unpredictable version.
  `Microsoft.Data.Sqlite` needs the same `SQLitePCLRaw` plumbing underneath, so this is not a dependency
  saved.

**Consequences accepted.** No `DbConnection` / `DbCommand` interoperability, so consumers using Dapper or
ADO.NET tooling cannot reach into the connection. Encryption via SQLCipher would require swapping the
`SQLitePCLRaw` bundle - documented in `04-SqlitePersistence.md`, and relevant to the provisioning threat
model, which is exactly why secret material never goes into this database — a consumer holding secrets
should put them in platform `SecureStorage` and keep only a *reference* here.

**What would change this.** A source-generated mapper of our own. At that point ADO.NET becomes the better
substrate and `Microsoft.Data.Sqlite` the obvious choice - the same v2 conversation as ADR-0014, and the two
decisions should be revisited together.

---

## ADR-0016 — The public surface is drawn around what a CONSUMER builds, not around what the suite renders

**Decision.** A type or member is public when an app building a control, a page or an entity *beside*
the suite needs it. Nine seams were promoted on that test during the first real consumer integration:
`G9SelectionSheet`, `G9TabBarMetrics`, `G9ColorExtension.ResolveColor`, `G9PaletteSubscriptions`,
`G9PageBase.IsHardwareBackSuppressed` / `TryHandleInAppBack`, `SqliteGuidStringNormalizer`,
`SqliteEntityAuditDefaults` and `SqliteRepositoryCacheRegistry`. `IG9OverlayHost` gained
`OverlayLayer`. Full list and symptoms: `09-Progress.md` → Step 3, and LES-0026.

**Why.** The extraction drew the boundary around *what the suite needs to render itself*. That is the
natural line to draw from inside, and it is the wrong one: a control suite exists to be built
**alongside**, so the consumer's own controls need the suite's metrics, its palette subscription
mechanism and its layer registry, and the consumer's own back dispatcher needs to both call and
override the page's back contract. Re-integrating the source app produced 33 `CS0122`s in one build —
not one of them a case where the consumer was reaching somewhere it should not have.

**What did NOT become public, and why the distinction is the whole ADR.** `G9ModalHostRegistry` and the
internal `ModalHost` record stay internal. They carry the popup and bottom-sheet CONTROL instances, and
external code reaching those bypasses the queueing and animation contracts the helpers own. The
consumer that was using them was moved onto `G9OverlayHosts` — the narrow seam that already existed for
exactly this — and the one thing it genuinely could not express there (mounting an app-owned modal at
sheet level) became `IG9OverlayHost.OverlayLayer`. **A missing seam is answered by adding the narrow
seam, never by widening the internal one**, which is the same call LES-0009 made.

**Alternatives rejected.**
- *`InternalsVisibleTo` for the consumer.* Solves it for one app and for nobody else, and encodes a
  consumer's identity into the package. Explicitly ruled out by the migration guide's §6.7.
- *Leave them internal and let consumers copy the values.* This was the status quo for
  `G9TabBarMetrics`, whose `BarHeight` every consumer hosting the tab bar must know. A copied constant
  drifts the first time the bar's height changes, and nothing fails — the content just stops clearing
  the bar.

**Cost.** A larger public surface is a larger compatibility commitment, and these are now covered by
SemVer. That is the right moment to pay it: the family is still `-preview` and nothing external has
consumed it, so this is the last point where the surface is cheap to get right (ADR-0010's reasoning,
applied to shape rather than to stability).

---

## ADR-0017 — Text ORDER is fixed in the string; a control never pins its own `FlowDirection` to get it

**Decision.** When a control needs a run of text to read left-to-right regardless of the surrounding
language — a numeric date, a version, a coordinate pair — it wraps that STRING in a Unicode LTR
embedding (`U+202A` … `U+202C`). It does **not** set `FlowDirection` on itself.
`G9CultureDateTimeLabel` was moved onto this rule in 1.0.2.

**Why.** `FlowDirection` is the paragraph direction, and every logical layout value in MAUI resolves
against the view's own effective flow direction: `HorizontalTextAlignment`, `HorizontalOptions`,
`Grid` placement of its children. Pinning it to fix glyph ORDER therefore also pins ALIGNMENT, and the
consumer has no way to opt out of the second effect while keeping the first. The concrete failure:
`Start` meant the physical LEFT edge under Persian, so a date could not be aligned with the plain
`Label` beside it in both languages — one of the two was always wrong (LES-0037). Bidi control
characters are the mechanism the Unicode algorithm provides for exactly this, they apply to the run
they wrap and to nothing else, and they leave the view an ordinary view.

**Scope, and the one thing this does NOT overturn.** The rule is about TEXT. `G9IconView` still pins
its children to `LeftToRight` (see the 1.0.0 notes and LES-0034), and that stays correct: there the
platform was mirroring a drawing CANVAS, which no string-level mark can reach and which has no logical
alignment to lose. The test is *what is being pinned* — a canvas has only a direction; a text view has a
direction **and** an alignment that consumers depend on.

**Rules that come with it.**
- Wrap only under an RTL culture. Under LTR the marks are inert but not free: they would enter every
  string an app might log, export, diff or compare, for no benefit.
- Wrap only content that is genuinely direction-neutral. Localized WORDS must read in the culture's
  direction; embedding them is the same defect inverted (which is why `Relative` mode is excluded).
- Prefer embedding (`U+202A`/`U+202C`) over isolates (`U+2066`/`U+2069`) when the control IS the whole
  paragraph: isolation buys nothing there, and embedding is honoured by every bidi engine since
  Unicode 2.0.

**Alternatives rejected.**
- *Keep the pin and translate the consumer's logical alignment inside the control* (flip `Start`/`End`
  when the culture is RTL, since the control knows it pinned itself). It works, but it makes one
  control's `HorizontalTextAlignment` mean something different from every other view's, and the next
  property that resolves against flow direction — `HorizontalOptions`, a nested `Grid` column — would
  need the same hand-translation or would silently disagree.
- *Let the consumer set the alignment from the culture.* That is the workaround the consumer already
  had, and it is one per call site, invisible until someone runs the app in the other language.

---

## ADR-0019 — A bottom sheet's drag is governed by its DETENTS, and the top one may be measured

**Status:** accepted, 1.0.6 (2026-09-01).

**Context.** `G9SheetView` had three notions of size — `CollapsedHeight`, `HalfExpandedRatio`,
`FullExpandedRatio` — plus `AllowedState`, which says which of the two LARGE ones exists. Nothing
said whether the collapsed height was a real resting step or just where a fixed sheet happens to
sit, and the drag limits were computed from the state the sheet was currently IN. A sheet declared
`States = [Peek, Medium]` therefore had no upper limit short of the window, snapped back to a
caller-guessed ratio on release, and treated a downward drag from its medium step as a dismissal.
Every fix for one of those symptoms in isolation (clamp harder, tune the ratio, special-case the
close) leaves the other two.

**Decision.** Model the sheet as an ordered set of DETENTS and derive all three behaviours from it.

1. `AllowCollapsedState` (bindable, default `false`) supplies the bit `AllowedState` cannot express.
   The helper sets it only when the caller declared `Peek` alongside another state.
2. The drag is clamped to `[smallest allowed detent (or 0 when cancelable), largest allowed detent]`.
3. Release snaps to the NEAREST allowed detent, ties going to the current state.
4. `ExpandedFitsContent` lets the largest detent be the measured content height instead of a ratio,
   capped by `MaxFitToContentHeightRatio`.
5. `ScrollingExpandsSheet` (default `true`) gives the sheet gesture priority over an inner scroller
   until the sheet is at its largest detent.

**Alternatives rejected.**

- *Just clamp the drag and leave the ratio to callers.* Fixes the over-drag and nothing else. The
  empty band is not a tuning failure: the layers sheet's group count varies with the site, the
  operator's permissions and the office's authored attributes, so no constant is right twice. The
  caller cannot compute it either — it does not know the helper's chrome (that is the same reason the
  chrome contract exists for fit-to-content).
- *Make the caller wrap its body in a `ScrollView` and enable/disable it per state.* This is what the
  consuming app was doing, and it is why the work started: `CanChildScrollVertically` measures
  content against viewport and never asks whether scrolling is switched on, so a disabled scroller
  still wins the gesture and then does nothing — a dead drag. Attaching and DETACHING the scroller
  does work, but it re-parents the body on every state change (a native detach/re-attach, i.e. the
  glyph-race the reveal machinery exists to avoid) and every future multi-detent sheet has to copy it.
- *A new `SizeMode` (e.g. `PeekThenFit`).* `SizeMode` selects an ENGINE; this is a property of one
  detent within the existing States engine, and expressing it as a mode would have forced a third
  measuring path beside `FitToContent` and `States` instead of reusing the fit engine's tiers.
- *`ScrollingExpandsSheet` default `false` (opt-in).* Safer-sounding and wrong: it is the platform
  default on both iOS and Android, and it is a no-op for single-detent sheets by construction, so
  opt-in would mean every new multi-detent sheet ships the backwards behaviour until someone notices.
- *Rubber-band overshoot past the top detent.* Neither platform does it — `UISheetPresentationController`
  and `BottomSheetBehavior` both stop dead at the largest detent — and it would have re-introduced
  exactly the "it moved past and snapped back" reading the change exists to remove.

**Consequences.** Two new options and two new bindable properties, all additive. Multi-detent sheets
change behaviour (that is the point); single-detent sheets do not, and the "no-op by construction"
property of `IsAtMaximumDetent` is load-bearing for that — see the Do-Not-Regress list in
`BottomSheet/G9BottomSheetGuide.md`. `ExpandedFitsContent` inherits the fit engine's cold-measure
reality: the top detent is only correct once the platform can measure, so it is re-resolved by the
same settle passes rather than trusted on the first frame.

---

## ADR-0018 — The canonical GUID case is LOWER, and `UseCanonicalIdCase` is honoured

**Decision.** `G9SqliteOptions.CanonicalIdCase` defaults to `G9IdCase.Lower`, and
`SqliteGuidStringNormalizer` defaults to the same. Changed in 1.0.2, together with the wiring that
makes the setting take effect at all.

**Why.** Every other layer in the stack emits lower case: `Guid.ToString("D")`, RFC 4122, PostgreSQL's
`uuid` output, and Dotmim.Sync's wire format. Upper case made this library the only component
disagreeing, and the disagreement was invisible in the place people look — SQL — because every id
column is `COLLATE NOCASE`. It was NOT invisible anywhere comparisons are ORDINAL: dictionary and
`HashSet` keys, a sync engine's hashed scope parameters, and any path that derives a FILE NAME from a
normalised id. A sync engine that writes rows straight into SQLite bypasses this normaliser entirely,
so the server's lower-case ids land verbatim and the same entity ends up with two different string
forms depending on which side wrote it. Measured on a consumer's production device: 55,870 of ~56,000
stored ids were already lower case; only the 175 locally-created rows were upper.

**The history matters, because it explains a defect that shipped.** The property was originally
declared with a `Lower` default that was never read — the normaliser was hard-coded to upper, so
`UseCanonicalIdCase` was silently a no-op. When the setting was finally wired up, the default was
changed to `Upper` to preserve the only behaviour the library had ever had. That was
bug-compatibility, and it made the wrong value the documented one. **1.0.1 shipped without the wiring
at all**, so a consumer calling `UseCanonicalIdCase(Lower)` against 1.0.1 gets no error, no effect,
and no way to tell — see `11-EngineeringLog.md` LES-0038.

**The cost, and why it is still worth paying.** This is a DATA event, not a setting change. Ids already
written keep their old casing (harmless — `COLLATE NOCASE`), but anything KEYED by a normalised id
moves: a per-user database directory renames, and a sync engine's parameterised scopes re-register
under the new casing and re-download once. `UseCanonicalCase` therefore freezes on first use and throws
if asked to change afterwards, so a consumer cannot flip it accidentally mid-session. Consumers that
derive paths from ids must adopt the differently-cased directory on upgrade; the reference
implementation is AgriPad's `UserDataPartitionService.TryAdoptDifferentlyCasedDirectory`, which
RENAMES (atomic, no free space needed) rather than copying — the databases are routinely hundreds of
MB, so a copy fails exactly on the devices holding the most data.

---

## ADR-0020 — A capability two controls need, and no shared base can hold, becomes a CLASS

**Status:** accepted (2026-09-07, 1.0.10)

### Context

Voice dictation was implemented inside `G9SearchEntry` — roughly 180 lines of session state
(permission re-check, cancellation token, partial-result append, the listening visual and its
signature-stable icon). A consumer then needed the same microphone on a plain title field and on a
multi-line description.

The obvious move is to push it down into a base class. There is no base class that can hold it.
`G9SearchEntry : G9TextEntry`, so a title field is reachable — but `G9Editor` is a SIBLING under
`G9OutlinedFieldBase`, and that class's job is the outline, the notch, the floating label and the two
icon slots. Speech recognition is not a property of a rectangle with a notch in it. Putting it there
would have meant every future outlined control — a picker, a date field, a PIN entry — inheriting a
microphone it can never use, plus the `G9Speech` dependency in the geometry layer.

### Decision

The SESSION becomes a class: `G9VoiceDictation`, in `Localization/` beside `G9Speech` and
`IG9SpeechToText`. It is control-agnostic — it is constructed with three delegates (read the text,
write the text, tell me to re-render) and owns everything else. Each control keeps only its own
microphone *affordance*: when it is shown, where it sits, and what its glyph does.

`G9TextEntry` declares the public members (`VoiceEnabled`, `VoiceCulture`, `IsListening`,
`Start`/`Stop`/`ToggleVoiceAsync`, and the three events). `G9SearchEntry` inherits them and sets one
default. `G9Editor` declares the same surface and drives the same engine.

### Consequences

- No consumer break: every member `G9SearchEntry` used to declare is still resolvable on it.
- The permission re-check, the cancellation contract and the append-not-replace semantics have ONE
  definition. Copying them a second and a third time is how they drift; the copy is the defect, not
  the duplication of lines.
- A control that should not dictate simply never sets `VoiceEnabled` — and, unlike a base-class
  mixin, never carries the members at all.
- The two behaviour fixes this refactor surfaced (the microphone vanishing mid-session, and the
  missing provider check) were each present once, not three times, which is the argument in
  miniature.

### Rejected

- **Voice on `G9OutlinedFieldBase`.** Above.
- **An `IG9VoiceField` interface with default implementations.** Default interface methods cannot hold
  the per-instance state (the token source, the base text), so each control would still carry the
  fields — the duplication that mattered — with the ceremony added on top.
- **Leave it in `G9SearchEntry` and let the app host a `G9SearchEntry` as a title field.** It is a
  search box: it defaults to a search glyph, a debounce timer and a `SearchCommand`. Dressing one up
  as a title field is how a design system stops meaning anything.

---

## ADR-0021 — A control reads its `G9Glyphs` slot for EVERY icon it draws, including the ones its own XAML declares

**Status:** accepted (1.0.13, 2026-09-08)

### Context

`G9Glyphs` is the suite's icon indirection: about thirty settable `G9IconSource` slots
(`G9Glyphs.Refresh`, `G9Glyphs.Close`, …) that default to the built-in vector `G9Glyph` set, so a
consumer can re-point the whole suite at its own icon font with a handful of assignments at startup.

`Icon="Refresh"` written inside a library view's XAML does **not** go through that indirection.
`G9IconSourceTypeConverter` resolves the literal name against the built-in glyphs (and the registered
default font) and produces a `G9IconSource` directly. That is correct for the converter — it is a
value converter, not a theme lookup — but it means a control can end up drawing icons from two
different sources at once: whatever its code passes through the slot, and whatever its markup names.

That is not hypothetical. `G9ProgressOverlayView` set the failure banner's leading icon from
`G9Glyphs.Refresh` (correct) while its retry button declared `Icon="Refresh"` in XAML. A consumer that
had mapped `G9Glyphs.Refresh` onto its own font got its icon on one side of the banner and the
library's built-in drawing on the other — two refresh marks, different weights, turning opposite ways,
on one 48 dp strip. Reported as the refresh icon being drawn backwards, which is what it looked like.

### Decision

**If a control exposes an icon through a `G9Glyphs` slot anywhere, every icon of that kind it draws
comes from that slot.** A literal `Icon="…"` in a library view's XAML is reserved for marks that
deliberately have no slot.

Slot-backed icons are assigned in **code**, not markup, because the slots are configured during
consumer startup — after `InitializeComponent` has already run for any view constructed earlier, and
the assignment has to happen at a point that observes the final value.

### Consequences

- A consumer's override applies completely or not at all. "Applies to some of this control's icons" is
  no longer a reachable state, and it was the failure mode that is hardest to attribute: the consumer's
  configuration is correct and the control still looks wrong.
- Slot-backed icons cost a line of code per view rather than an attribute in markup. That is the price
  of the indirection being real.
- Where no slot exists, the literal stays. `G9Glyphs` has no `Close` slot — the nearest is `Clear`,
  which means "empty this field" — so the overlay's ✕ keeps the built-in vector deliberately rather
  than being routed through a slot whose meaning is different.

### Rejected

- **Make the type converter consult `G9Glyphs` by name.** It would fix this case and break the ability
  to name a specific built-in glyph on purpose, silently, everywhere in the suite — and a converter
  whose output depends on host startup state is not a converter.
- **Leave it and document the mixing.** The defect is invisible until a consumer overrides a slot AND a
  control happens to draw the same icon both ways. Nothing about the code shows it; only the screen does.

---

## ADR-0022 — A sheet is STAGED off-screen and measured for real before it opens; its motion is a translation, never a relayout

**Status:** accepted (2026-09-21, 1.1.0) — built and compile-verified on all four TFMs; **not yet run on a device**

### Context

Three complaints from the consuming app, all about the bottom sheet, all worst on Android: a loading
skeleton had to be shown before content; the first-open height was wrong, so the app kept a persisted
memory of past heights plus a compiled seed table; and a sheet that changed size after opening did so
badly. A fourth: it was slow.

`BottomSheet/G9BottomSheetGuide.md` recorded the accepted explanation — "the first measure of a sheet
body on Android is ALWAYS cold … platform reality … the placeholder / memo / settle machinery is
unavoidable". Reading the open pipeline says otherwise. `ConfigureSheetContent` ran the first
`ApplySheetContentSizing` BEFORE `overlayHost.Children.Add(sheet)`. A view that is not in the tree has
no platform handler, and a handler-less MAUI view reports a desired size of zero. The zero was ours. On
top of it, `DeferContent` defaults to `true`, so a view the caller had ALREADY BUILT (55 of the app's 58
call sites) was wrapped in a `DeferredContentView` and hidden behind a placeholder for a fixed
369 + 220 + 160 ms — after its construction cost had been paid on the tap.

The resize was a tween that rewrote the detent ratios every tick, each write setting the body's
`HeightRequest`: a full measure and arrange of the sheet body per frame. A drag between detents did the
same per touch-move. And on Android, every position event below the backdrop threshold ran two ~96-view
native tree sweeps over JNI to "reset" a transform that was already identity.

### Decision

1. **Stage before show.** `OpenSheet` parks the body BELOW the screen edge — visible, attached, laid
   out at its real height (`G9SheetView.Stage`) — and `StageAndShowAsync` then measures it (handlers
   exist now), waits for one layout pass, measures again, holds for `PreOpenSettleFrames` drawn frames,
   honours `IDeferredContentReadiness`, and only then starts the open motion. Every wait is a frame or a
   signal, all bounded by one deadline (`PreOpenMaxHoldMs`). A translation, never `IsVisible = false`:
   an invisible view is skipped by layout and would realize nothing.
2. **A pre-built view is never deferred.** `DeferContent` keeps its meaning for FACTORY content only.
3. **Motion is translation; layout happens once per size.** A drag grows the body to its largest detent
   on the first move and translates from then on. `SetFitHeight` resizes with one layout pass — grow:
   take the height first (the extra hangs below the screen edge), then slide up; shrink: slide down,
   then take the height. A sticky footer is kept at the screen edge by counter-translation
   (`BottomPinnedView`).
4. **An aborted motion does not complete.** `finished` honours `cancelled`; a retargeted motion
   inherits the completion of the one it replaces, so `OpenMotionCompleted` fires once, at the real end.
5. **Release is judged on velocity as well as distance** (Android `VelocityTracker` in SCREEN
   coordinates — the view moves with the finger, so view-relative velocity reads as zero; iOS
   `VelocityInView(null)`).
6. **Everything has a rollback.** `G9BottomSheetSettings.StageBeforeShow = false` restores the previous
   pipeline wholesale; `DeferPrebuiltContent` and `UseHardwareLayerDuringMotion` are finer switches.

### Consequences

- A sheet whose data is in hand opens once, at its exact height, with finished content. The height memo
  and the seed table are no longer consulted for it (they remain, as hints, for open-then-fill bodies).
- **The cost moves; it does not vanish.** Handler creation and first layout now happen BEFORE the motion
  instead of during or after it. Tap → motion-start gets longer by exactly that work (two frames for a
  light menu; more for a heavy body), in exchange for tap → usable getting much shorter and for nothing
  happening during the animation. The modal scrim appears at once, so the tap is acknowledged
  immediately. If that trade is wrong for a particular heavy sheet, it should be shown from a FACTORY,
  which still opens first and builds after.
- The app-side compensations become deletable, but are deliberately NOT deleted in this change: the 10
  height seeds, 6 hand-written height providers, 2 memo keys, `OperationsMenuContentViewBase`,
  `CompactListSheetHelper`. They are harmless with the new engine and they are the safety net until it
  has been seen on a device.
- `ShowListG9BottomSheetAsync` honours a caller's `SizeMode`. Two app call sites that pass
  `FitToContentOptions()` and were silently given a full-screen panel will now get what they asked for.
- A sheet is "in flight" while staged. `GetOpenSheetCount` counts it, and `CloseSheet` closes it.

### Rejected

- **A native container** (`BottomSheetDialogFragment` / `BottomSheetBehavior` /
  `UISheetPresentationController`). Material's behaviour has no smooth content-driven resize either — it
  snaps on relayout, which is the very symptom (material-components-android #2062, #1729). An
  activity-level container renders BEHIND .NET 9+ modal pages; a Dialog cannot be non-modal over a live
  map; any of them leaves the shared `OverlayHost → PopupHost → ToastHost` z-stack, so toasts would draw
  under the sheet. CommunityToolkit.Maui went the other way for the same reason (Popup v2). Only iOS 16+
  has a primitive that genuinely solves resize (`animateChanges { invalidateDetents() }`); it stays open
  as a possible per-platform presenter later.
- **"Native animators" as the performance fix.** This was the plan's own first instinct and it was
  wrong: `ViewPropertyAnimator` and `SpringAnimation` run on the UI thread exactly like MAUI's ticker, so
  they stall just the same when the UI thread is inflating content. What keeps a motion smooth is that
  NOTHING else runs during it — i.e. sequencing, which is decision 1. The one native assist kept is a
  hardware layer for the duration of the motion, because a translation never dirties it.
- **Keeping the persisted height memo as the first-open answer.** It is a guess that is wrong on a fresh
  install, after every app update, at another font scale and for any sheet whose height depends on its
  data — and it existed only to hide a zero we were producing ourselves.
- **Pooling the sheet shell, an `INestedScrollingParent3` touch layer, IME-synchronised translation, a
  predictive-back callback, a structural `G9Redacted` skeleton, and splitting the 6,000-line helper into
  a per-sheet session object.** All are recorded as open defects in `11-EngineeringLog.md` → RSK-0003,
  and all were deliberately left out of this change. Each is either a large refactor with no user-visible effect, or platform gesture / inset
  code that cannot be validated without a device and would interact with workarounds the app still
  carries (`KeyboardInsetScope`, its back coordinator). Shipping them blind would have put the part that
  fixes the actual complaints at risk.

---

## ADR-0023 — Sheet motion is the PLATFORM's model, resolved per motion, and runs on the display's frame clock

**Date:** 2026-09-21 · **Status:** accepted · **Supersedes:** the "size-scaled duration" rule in the guide

### Context

ADR-0022 made a sheet arrive finished. The first device trace taken with it (59 sheets, the AgriPad app,
Pixel 9 Pro XL emulator) then showed why it still did not FEEL native:

- The open duration was `configured × distance / screen`. With the app's 199 ms that is a constant
  5000 dp/s: a 200 dp picker opened in **40 ms**, a 720 dp sheet in 144 ms. Native sheets take
  ~250–500 ms whatever the distance.
- 34 of 119 motions had a frame gap over 25 ms — nearly always the FIRST frame (hardware layer + first
  rasterisation). MAUI's ticker charges that gap to the curve, and an ease-out is fastest at the start,
  so the sheet appeared part-way open instead of sliding in.
- One .NET collection with Java bridge processing every 1.1 s during the session, each stalling the UI
  thread for tens of ms; 36 of 59 opens contained one. The engine's own per-frame Java callback
  objects were feeding it.

### Decision

1. **`G9BottomSheetSettings.MotionStyle = PlatformNative` is the default.** Duration AND curve are resolved
   per motion by `G9SheetMotionModel`: androidx `ViewDragHelper`'s settle on Android / Windows (every
   constant read from source and asserted by tests), UIKit's default critically-damped spring on iOS /
   Mac Catalyst (an inference — Apple publishes no sheet figures — and labelled as one). `Timed` keeps the
   1.0 behaviour. A per-sheet duration override still wins, on the native curve.
2. **Motion runs on `G9SheetMotionDriver`** against `G9FrameClock` (Android `Choreographer`): the clock
   starts on the first drawn frame, a hitch is absorbed as a pause rather than a jump, positions are
   evaluated at vsync time, and the motion ends within 0.5 dp of its target. Other platforms run the same
   curve on MAUI's ticker. `UseFrameClockMotion = false` is the kill switch.
3. **`G9FrameClock` owns the ONLY Java frame-callback object in the process.** Nothing in the engine may
   allocate a Java peer per frame.
4. **Whatever accompanies a motion reads that motion** (`CurrentMotionDurationMs`, `IsMotionRunning`) — the
   overlay fade and the close cleanup no longer assume a configured constant.
5. The model is MAUI-free (`Func<double,double>`, not `Easing`) so it is file-linked into the test project.

### Rejected

- **A spring everywhere (Compose 1.4's `DefaultSpatial`, ζ 0.9 / k 700).** Closer to new Compose apps, but
  it overshoots, and this body is exactly as tall as its detent: an overshoot lifts its bottom edge off the
  screen edge and shows the page under it. It would need an over-tall body first.
- **Material's `BottomSheetDialog` window animation (20 % translate + fade, 400 ms, emphasized).** It is what
  a dialog-hosted sheet does on show, but it is a fade, not a slide, and it has no answer for a release or
  a detent change. The settle model covers every motion with one rule.
- **Native animators (`ViewPropertyAnimator`, `SpringAnimation`).** Unchanged from ADR-0022: they run on the
  same UI thread and stall with it; they would also fork the motion code per platform.
- **Letting the curve absorb hitches** (the MAUI ticker's behaviour). Measured, above.

### Consequences

- The app-wide `OpenAnimationDurationMs` / `CloseAnimationDurationMs` / `SizeScaledAnimationDuration` are
  inert under the default style. AgriPad's `MauiProgram.cs` still sets 199 / 199; they are annotated there.
- `FlingVelocityThreshold` 700 → 300 dp/s (Material ≈ 170, Compose 125).
- Not a library matter, but found by the same trace and recorded in the app: the Mono nursery is raised to
  32 MB through an `AndroidEnvironment` file (`MONO_GC_PARAMS`), so bridged collections are ~8× rarer. Note
  for anyone repeating it: the variable REPLACES the SDK's own `major=marksweep-conc`, so that has to be
  restated; and in a Debug fast-deployed build the file is parsed on the device by a reader that treats
  any line containing `=` as a variable — comments included.

---

## ADR-0024 — Dictation never takes focus; a microphone tap puts the keyboard AWAY

**Date:** 2026-09-23 · **Status:** accepted (1.2.0) — compile-verified; **not yet run on a device** · Jira ITCS-15661

### Context

`G9TextEntry.OnTrailingTap` and `G9Editor.OnTrailingTap` called `Focus()` on the inner `Entry` / `Editor`
before `ToggleVoiceAsync()`, "so the user can keep typing if they change their mind". Focusing a text
control is precisely what raises the soft keyboard, so every dictation started with a keyboard sliding up
over the form the user was about to SPEAK into — on an editor, over most of the text being dictated.
Dictation never needed focus: `G9VoiceDictation` writes through the control's `Text` delegate, and the
control mirrors `Text` into the platform view whether or not it is focused.

Reading around it found a second route to the same symptom: the field's box carries a wrapper-level tap
recognizer (tap anywhere → focus), and the icon hosts sit inside the box. Android delivers a touch to the
innermost consuming view, but on Apple platforms a superview's `UITapGestureRecognizer` also sees touches
in its subviews, so a trailing-icon tap could ALSO focus the field.

### Decision

1. **A microphone tap never focuses.** When it STARTS a session and the field is focused (the user was
   typing), the keyboard is dismissed and focus released (`G9KeyboardHelper.DismissKeyboardIfFocused`:
   `HideKeyboardAndClearFocus`, then MAUI's `Unfocus` where that is a no-op — Windows). Stopping leaves
   focus alone.
2. **Focus arriving during a session ends it.** Since the mic no longer focuses, a focus mid-session can
   only be the user tapping the field — they chose to type. The partial transcript is already in `Text`,
   and a recognizer still appending under their keystrokes would write over them.
3. **The box yields to an ACTIONABLE icon, by position.** `OnBoxTapped` hit-tests the tap against a
   visible, actionable icon host and ignores it there; when the platform cannot report a position it
   decides one dispatcher turn later, after the icon's recognizer has had its turn (a 400 ms "icon just
   tapped" window). A decorative icon still focuses the field, as it always did.
4. `G9PopupHelper`'s voice-enabled form fields are `G9TextEntry` / `G9Editor`, so they inherit the rule.
   Its own `Focus()` calls are the form's first-field autofocus and first-invalid-field focus, not mic
   taps, and are unchanged.

### Consequences

- The keyboard does not appear for dictation; dictating into a field the user was typing in takes the
  keyboard down. Tapping the field is the one-gesture way back to typing, and it stops the recognizer.
- A clear button or password eye tapped on an UNFOCUSED field no longer brings the keyboard up as a side
  effect on platforms where both recognizers fired.
- Android's `TapOutsideKeyboardDismisser` already hides the IME and clears focus on a tap outside the
  focused `EditText` — which a trailing-icon tap is — before the view's own handler runs. Decision 1 is
  idempotent with it, and needed where it does not run (a page that never attached the dismisser, iOS
  without its window recognizer, Windows).

### Rejected

- **Keep the focus, suppress the keyboard** (`ShowSoftInputOnFocus = false` on Android, an empty
  `InputView` on iOS, `TextBox.PreventKeyboardDisplayOnProgrammaticFocus` on Windows). Three per-platform
  handler mutations that would have to be undone exactly when the user taps the field to type — the
  moment that is hardest to tell apart from our own programmatic focus — and each has a known way of
  leaving the field with no keyboard at all afterwards. It also keeps the caret blinking in a field the
  user is not typing in. Focus buys dictation nothing, so the defect was the focus itself.
- **Focus, then dismiss the keyboard straight after.** The keyboard animates up and back down: the
  flicker is the bug in a different shape.
- **Leave the session running when the field is focused.** Two writers into one field — the recognizer's
  append and the user's keystrokes — is the "value with two writers" failure (LES-0043).

---

## ADR-0025 — Filled-value accent is an app-wide setting with a per-field override; the default does not change

**Date:** 2026-09-23 · **Status:** accepted (1.2.0) — compile-verified · Jira ITCS-15666

### Context

`G9OutlinedFieldBase.ResolveStateColor` returned `Primary` for `IsContentFocused || HasFilledValue()`, so
any field that merely HAS a value — a pre-selected `G9Picker`, every field of an edit form — draws its
outline, floated label and trailing icon in the focus colour. `G9Controls.md` §3 described that as the
"filled-valid rest state" on purpose ("keeps completed fields visually active"). The consuming app's
design now wants the opposite: filled fields at rest look like empty ones, so the one focused field is
the only accented one.

### Decision

- `G9OutlinedFieldSettings` (a `sealed record` with `Default`, same shape as `G9BottomSheetSettings`),
  one setting so far: `HighlightFilledValue` (default `true` — today's behaviour). Applied with
  `G9OutlinedFieldBase.Configure(settings)` at startup; read back through the public
  `G9OutlinedFieldBase.Settings` so a hand-rolled field frame elsewhere in an app can follow the same rule.
- Per field: `FilledValueHighlight` (`G9FilledValueHighlight.Inherit` / `Accent` / `Neutral`), default
  `Inherit`.
- Neutral means: a filled, UNFOCUSED, no-error, no-status field uses the resting outline colour, its
  floated label the resting CONTENT colour (the empty field's label / placeholder grey,
  `ResolveRestingContentColor`), and its trailing icon the resting colour. The label still floats and
  turns bold. Focus, error and status colours, emphasis stroke and halo logic are untouched.

### Consequences

- No consumer sees a change until it calls `Configure` or sets the property.
- `Configure` does not repaint live fields (there is no registry of them, and there should not be one for
  a once-per-process call); it must run before fields are built. Documented on the method.
- Subclass overrides of the RESTING colours (`G9SearchEntry`'s `InputPlaceholder`) now also apply to the
  neutral filled state, which is what makes a neutral search box look like its own empty state.

### Rejected

- **Change the default.** A published package whose every consumer's filled fields change colour on a
  minor bump — for a design preference, not a defect.
- **Make `ResolveStateColor` protected virtual and let the app subclass.** The app does not own the
  controls' types (it would need a subclass of every outlined control), and the label colour is resolved
  in two other places that would have to be kept in step by hand.
- **A theme token (a `FilledOutline` colour in `G9Palette`).** A token can make the filled state a
  different colour, not "the same as empty": neutral must follow each control's own resting colours,
  which differ between controls by design.
- **Neutral label in the resting OUTLINE colour** (what reusing `stateColor` would give). The outline
  colour is a hairline tone, too light for text; the label is text and takes the text grey.

---

## ADR-0026 — A sheet follows its host's size: full-screen heights are re-applied, resting heights are capped

**Date:** 2026-09-23 · **Status:** accepted (1.2.0) — compile-verified; **not yet run on a device** · closes BS-20 · Jira ITCS-15664, ITCS-15525

### Context

The consuming app pads its window by the keyboard height while a `KeyboardInsetScope` is open, so the
host page SHRINKS when the keyboard opens. Two sheet models ignored that:

- **Full-screen sheets** stamped `HeightRequest` / `MinimumHeightRequest` on their root / sizing host /
  body ONCE at open (`ApplyFullScreenHeight`). `G9SheetView` resized its body with the host, but the
  content inside kept the old height, so the footer and the bottom of the content's `ScrollView` hung
  past the container, under the keyboard (BS-20, recorded as "partly" in RSK-0003). The app worked around
  it in one view (`ProfileChangePasswordContentView`) and only for edge-to-edge sheets.
- **Resting (collapsed) sheets** — every fit-to-content sheet, and a peek — rest at a FIXED
  `CollapsedHeight` (a scroller body is given ~75 % of the page at open). `ResolveRestingHeight` kept it
  and only translated the body, so the body never got shorter (an inner `ScrollView` saw no
  `SizeChanged` and the app's "scroll the focused field into view" never ran), and a tall keyboard made
  the translation negative — the header left the top of the screen.

### Decision

1. **Full-screen: every sized element is registered on the sheet and re-sized on the host page's
   `SizeChanged`.** `ApplyFullScreenHeight(sheet, element, options, isContentHeight)` records a
   `FullScreenHeightTarget`; the first registration subscribes the CURRENT host page once per sheet and
   remembers it, so the handler is removed from the page it was added to (`CleanupSheetVisualsNow`, and
   whenever a sheet's behaviour state is replaced; the handler also detaches itself if it fires for a
   behaviour that is no longer current). Re-sizing is a plain property write — the page has already
   changed size, and `G9SheetView` moves its own body in the same layout pass, so an animation here
   would be a second motion fighting the motion engine. A body implementing `IG9BottomSheetSizedView`
   is told each new height. Applies to every full-screen preset, edge-to-edge included.
2. **Resting: the body rests at `min(CollapsedHeight, hostHeight − RestingTopReserve)`.**
   `G9SheetView.ResolveCollapsedRestingHeight` is used by every place that turns the collapsed detent into
   geometry — resting position and height, `SetFitHeight`, drag limits, release snap. The helper sets the
   reserve to the page's top safe-area inset (the display cutout). `CollapsedHeight` itself is not
   rewritten, so the body grows back when the keyboard closes with nothing re-measured. The existing
   rule in `ApplyBodyHeightForState` does the moving: a large host change animates the translation and
   the body takes its new height when the motion completes; the sticky footer is held at the host edge by
   the bottom pin meanwhile.

### Consequences

- A full-screen form keeps its footer and its scroll range above the keyboard, with no view-side code.
  The app's `ProfileChangePasswordContentView` host-height workaround becomes redundant.
- A fit sheet whose content fits above the keyboard just moves up, as before. One that does not is
  squeezed: a scrolling body scrolls, and its `ScrollView` gets the `SizeChanged` the app waits for. A
  NON-scrolling measured body is clipped at the bottom while the keyboard is up — the same thing that
  happens at the 75 % cap, and the authoring rule already says a body that can outgrow the cap must scroll.
- Per-step host changes (a keyboard inset animated per frame) write the collapsed body height per step,
  exactly as ratio detents already did for continuous resizes.
- Residual: a fit pass that runs WHILE the page is shrunk (a tracker or provider event) still computes its
  cap from the shrunken page, so a scroller body can come back smaller than it opened until the next fit
  pass. Recorded in RSK-0003.

### Rejected

- **IME-synchronised translation** (`ViewCompat.SetWindowInsetsAnimationCallback`, `UIKeyboard`
  notifications). Still deferred (ADR-0022): the app already turns the keyboard into a page-size change,
  and following the page is one rule for keyboard, split screen and rotation alike.
- **Subscribe to the sheet view's own `SizeChanged`.** It would work, but the heights are PAGE heights and
  the page's size is set before its children are arranged; following the source avoids a pass with the
  old value.
- **Re-run the fit-to-content engine on a host change** instead of capping. It rewrites the detent from a
  measurement taken against a temporarily small page, and nothing would restore it when the keyboard
  closes; and for a scroller body it would re-apply "75 % of a smaller page", which is not a better answer.
- **Leave it to each view** (the app's per-view workaround). One view had it, only for one preset — the
  shape of a library defect being paid for in the app (library-source-mode rule).

---

## ADR-0027 — Live toasts and the progress overlay re-anchor on an explicit call, and glide by translation

**Date:** 2026-09-23 · **Status:** accepted (1.2.0) — compile-verified · Jira ITCS-15663

### Context

A toast's position is a margin computed once, at show (`ApplyInlineG9ToastPosition` →
`ResolveBottomInset`), from the page's insets and — for bottom toasts — a tab-bar clearance
(`BottomSafeAreaWithTabBar − BottomSafeAreaInset`) that applies only while no bottom sheet is open. The
progress overlay does the same (`G9ProgressOverlayHelper.ResolveBottomInset`). The app now changes
`BottomSafeAreaWithTabBar` when its tab bar hides or shows, and toasts already on screen stayed where the
tab bar used to be.

### Decision

- `Task G9ToastHelper.RefreshBottomInsetsAsync(bool animate = true)` — every live stacked, loading and
  progress toast gets its margin re-applied; the resulting move of its layout slot is added to its
  `TranslationY` (so nothing jumps) and the translation then glides to its resting offset (220 ms,
  `CubicOut`). Stack offsets are recomputed in the same pass (`ResolveStackOffsets`, now shared with
  `ReflowToastStackAsync`), because a bottom stack sits on an `IG9BottomAnchoredOverlay` whose margin moves
  with the same inset.
- `Task G9ProgressOverlayHelper.RefreshBottomInsetAsync(bool animate = true)` — the same for the overlay,
  followed by a reflow of the toasts stacked on it. A minimized bubble keeps the spot the user dragged
  it to. The two calls converge in either order.

### Rejected

- **Subscribe each toast to the page's `PropertyChanged`.** `BottomSafeAreaWithTabBar` is observable, but
  the "is a sheet open" half of the rule is not, so a subscription would be half a mechanism that looks
  complete. The code that hides the tab bar is the one place that knows the answer changed.
- **Animate the margin.** A margin tween is a layout pass per frame for the toast host; `TranslationY` is
  what every toast motion already uses (enter, exit, reflow) and composes with them — a new slide on the
  same property supersedes an in-flight one instead of racing it.
- **Dismiss and re-show live toasts.** Loses the remaining lifetime, replays the enter animation and
  re-runs an action toast's accessibility announcement.

---

## ADR-0028 — A check box is its own drawn control: M3 square, tick drawn in by length, tri-state, row-sized hit target

**Date:** 2026-09-27 · **Status:** accepted (1.2.0) — compile-verified; **not yet run on a device** · Jira ITCS-15686

### Context

QA: "the checkbox design is very ugly; G9MAUIControls has no check box". The suite had none. Consumers
used the platform `CheckBox` — Android's AppCompat box, UIKit has no check box at all (MAUI draws its
own), WinUI's square — three looks, none following `G9Palette`, and on Android a hit area the size of
the glyph. The suite's own guidance filled the gap with a `G9Switch` per option ("former checkboxes"),
and `G9PopupHelper` rendered a `CheckBox` input field that way: a column of settings toggles where the
form means "pick any of these".

The design chosen by the product owner: Material 3 — a 20dp square, 6dp corners, 2dp stroke in
`Outline` when unchecked; checked fills `Primary` and a white tick DRAWS ITSELF IN (~150ms, eased),
un-checking reverses it; a soft circular `Primary` halo (~40dp) on press; an indeterminate state (a
white bar on `Primary`); disabled tints; the whole row tappable.

### Decision

1. **`G9CheckBox : G9ControlBase`, painted on a `GraphicsView`** (`G9CheckBoxDrawable`), like
   `G9Switch` — not a restyled platform `CheckBox`. Bindables `IsChecked` (two-way), `IsIndeterminate`
   (two-way), `Text`, `Command`, `CommandParameter`; event `CheckedChanged` with the platform's
   `CheckedChangedEventArgs`; `Toggle()` = the user action.
2. **One float drives the checked transition, split into overlapping phases** (`G9CheckBoxMath`): the
   fill pops in over the first 40%, the tick draws over the last 75%. Unchecking plays it backwards,
   and every animation starts from the value's current position with a duration scaled to the
   distance left — so a reversal mid-way is continuous by construction.
3. **The tick is trimmed by LENGTH along a two-segment polyline**, and **the indeterminate bar is the
   same polyline** with its elbow raised. Checked ↔ indeterminate is a point interpolation; one trim
   draws either shape in. Drawn as two `DrawLine`s with round caps — no `PathF` per frame.
4. **Tri-state is visual precedence, not a third value of `IsChecked`.** `IsIndeterminate` wins while
   true; a TAP clears it and checks the box (the Android / iOS / WinUI convention: "mixed" resolves to
   "all"); only code or a binding can set it. `CheckedChanged` stays a plain `bool`.
5. **Hit target = the row.** The square sits centred in a 48dp slot (`G9LayoutMetrics.MinTouchTarget`);
   with `Text` the whole row is the single gesture owner, without it the control collapses to its slot
   (`HorizontalOptions = Start` on the inner row) so a bare box does not claim its parent's width.
6. **Direction is layout, never paint.** The box sits at the START by `FlowDirection` inheritance; the
   canvas is pinned LTR and the drawable does no direction math — the tick never mirrors (§9).
7. **Colours are palette tokens read at paint time**, with the tick colour in ONE recipe,
   `G9Colors.CheckBoxMark` = `OnPrimary` (white in the stock light palette). No colour-override
   bindables — `G9Switch` has none either; a rebrand retunes the palette.
8. **Command runs on USER toggles only**, after the state is written, through `G9Press` with a 0 ms
   guard (never-crash, but no double-tap swallowing — every tap on a check box is a real toggle).
9. **The popup `CheckBox` field renders `G9CheckBox`.** Radio fields stay `G9Switch` + `SelectionGroup`.
10. **Geometry reuses the existing `SelectionCheckBoxSize` / `SelectionCheckRadius` tokens** by alias
    (`CheckBoxSize`, `CheckBoxCornerRadius`) rather than declaring a second 20 / 6.

### Rejected

- **Restyle the platform `CheckBox` through handlers.** Three native widgets to bend, no drawn-in tick
  on any of them (Android animates its own vector, UIKit has none), and the hit area stays the glyph.
  It is the thing QA called ugly.
- **Keep `G9Switch` for multi-select.** A switch says "this setting is on now"; a check box says "this
  is one of your choices". The popup form was the visible casualty.
- **Separate fill and tick animations.** Two timelines have to be reconciled when a tap reverses one
  mid-flight; one timeline with overlapping phases reverses for free.
- **Morph by cross-fading a tick glyph into a bar glyph.** A cross-fade shows both marks at once for
  half its duration; interpolating shared points never does.
- **A white tick in both themes.** Honours the design literally, but hard-codes a colour a rebrand
  cannot fix (a pale primary would lose the tick). `OnPrimary` is white where the design was drawn;
  the stock dark palette gives a deep-green tick instead (M3's dark look). Flagged to the product owner;
  the change, if wanted, is one line in `G9Colors.CheckBoxMark`.
- **A `bool?` `IsChecked` for tri-state.** Every binding and handler would have to cope with `null`;
  a separate `IsIndeterminate` keeps the common two-state case exactly as simple as the platform one.
- **Executing `Command` on every `IsChecked` change** (the platform `CheckBox` behaviour). A
  view-model command would then also fire on page load and on its own writes back through the binding.

---

## ADR-0029 — The progress overlay's placement belongs to the HOST, and a live overlay can move

**Date:** 2026-10-03 · **Status:** accepted (1.3.0) — see `09-Progress.md` for what was verified

### Context

The overlay took one `G9ProgressOverlayPosition` per `ShowAsync` call, fixed for the session's life, at the
top or bottom safe-area inset plus a 14 dp gap. AgriPad asked for it to sit under the map's top bar while the
map tab is on screen, right under the status bar over its full-screen sampling map, and at the bottom
everywhere else. That keeps it apart from the toasts and from the map's own bottom controls. Three things
made that impossible:

1. the right spot depends on the SCREEN, not on the caller. One sync is started from several screens, and the
   user switches screens while it runs;
2. "under the map's top bar" needs a distance the library cannot know;
3. a live overlay could only follow a bottom-inset change (`RefreshBottomInsetAsync`, ADR-0027), never a
   change of edge.

### Decision

- `G9ProgressOverlayPlacement(Position, Offset)`: an edge plus extra distance from that edge's inset.
- `G9ProgressOverlayHelper.PlacementProvider : Func<G9PageBase?, G9ProgressOverlayPlacement?>`. It receives
  the HOST page, so a provider written for one page answers `null` (the default, bottom) for any other: a
  login page, or a page left behind by a root swap.
- `ShowAsync(contextText, G9ProgressOverlayPosition? position = null)`: an explicit position wins and is fixed
  for the session, exactly as before. `null` asks the provider and keeps following it.
  `ShowStandaloneFailureAsync` likewise.
- `Task G9ProgressOverlayHelper.RefreshPlacementAsync(bool animate = true)` re-reads the provider and moves a
  live overlay. Same edge with a new offset: ADR-0027's carry-and-glide. A change of edge: fade out at the old
  edge, re-anchor, fade in at the new one, using the motions mount and teardown already use. The toast stack is
  reflowed either way (it rests on a bottom-anchored overlay and ignores a top one).
- A minimized bubble is never moved (it stays where the user dragged it). A host change re-resolves the
  placement for the new page.

### Rejected

- **A per-call position from every caller.** Every sync site would need to know which screen is showing, and
  none can follow a tab switch made while it runs.
- **Gliding across an edge change.** The anchor flips between the layer's top and bottom; a translation
  across the whole screen sweeps the card over everything between.
- **Subscribing to the host's state.** Same reason as ADR-0027: what decides the placement (which tab, whether
  a full-screen sheet is up) is not one observable property. The host knows the moment it changes.
- **Moving the TOASTS instead.** Toasts are owned by many callers and already stack on a bottom overlay. Moving
  the one overlay separates the two with no toast change at all.

---

## ADR-0030 — A tab bar has STYLES, held as one metrics object per style; chrome and shadow share one outline

**Date:** 2026-10-04 · **Status:** accepted (1.4.0) — see `09-Progress.md` for what was verified

### Context

AgriPad's designers delivered a second bottom navigation («Bottom Navigation / Sculpted»): an opaque bar with
24 dp corners, a wide S-shouldered cradle instead of the semicircle notch, a flat 48 dp disc that sinks 8 dp into
the bar, and no selection pill — the selected tab switches to a filled icon. The owner wanted the existing bar
KEPT and the new one added beside it. Every geometric value of `G9TabBar` was a `const` in `G9TabBarMetrics`
used directly by ~40 call sites, and the notch path existed twice (chrome drawable and Skia shadow).

### Decision

- `G9TabBarStyle { Classic, Sculpted }` and a bindable `BarStyle` (not `Style` — MAUI owns that name).
- `G9TabBarStyleMetrics`: one immutable object per style holding everything that differs (bar height and radii,
  FAB size / disc / glyph ratios / how far its centre sits below the bar top, notch shape and half-width, item
  sizes, whether there is a pill, shadow recipe). `Classic` is built FROM the old constants, so a bar that never
  sets `BarStyle` is the same bar. The shared insets, the sub-menu row, overflow and timings stay constants.
- `G9TabBarOutline.Build(sink, metrics, …)` traces the silhouette once; the chrome (`PathF`) and the shadow
  (`SKPath`) are sinks. The FAB centre clamp uses the same `ClampNotchCenter`, so FAB and notch cannot part.
- Colours: style overloads in `G9TabBarColors`; the style-less ones remain the Classic recipe.
- Sculpted's selected state is `G9TabBarItem.SelectedIcon` + Primary. Classic ignores `SelectedIcon`.

### Rejected

- **A second control (`G9SculptedTabBar`).** The behaviour — selection, FAB slot, sub-menu, overflow, the
  reserved-height choreography — is 2,700 lines that must not fork.
- **Making the constants mutable statics.** Two bars in one app (or the gallery) would fight over them.
- **A free-form "outline provider" delegate.** The shadow, the FAB clamp and the highlight all need to know the
  notch's half-width; a shape enum plus metrics keeps them consistent.
