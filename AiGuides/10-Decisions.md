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
