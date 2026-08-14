# 03 - Package Architecture

# The `G9MAUIControls` ecosystem
## Boundaries, naming, and the rules for adding to it

---

# The shape

One reserved NuGet prefix, one small stable core, and satellites that each add one capability and pay
for their own dependencies.

```
G9MAUIControls                          ← core. 25 controls, sheet, popup, toast,
│                                          tab bar, edge panel, theming, hosting.
│                                          Deps: Microsoft.Maui.Controls,
│                                                CommunityToolkit.Mvvm, SkiaSharp
│
├── G9MAUIControls.IntroCarousel        → core + CommunityToolkit.Maui.MediaElement
├── G9MAUIControls.Barcode              → core + a camera-scanner package
├── G9MAUIControls.ProgressOverlay      → core only (split for API hygiene, ADR-0009)
│
└── G9MAUIControls.Persistence.Sqlite   → NO core dependency. sqlite-net-pcl only.
```

The dependency arrows only ever point at the core. **No satellite may depend on another satellite** — that
is how a package family turns into a graph nobody can version independently.

---

# Why a family instead of one package

Because NuGet has no concept of an optional dependency. Every entry in a package's dependency list is
acquired by every consumer, transitively. A single package containing the carousel and the barcode entry
would hand a media stack and a camera stack to somebody who wanted a text field.

The .NET library guidance makes this a *rule*, not a taste: **"DO review your .NET library for
unnecessary dependencies"**, on the reasoning that you cannot know what else will be installed alongside
you, and that the mitigation for diamond-dependency breakage is to have fewer dependencies. A control
suite is exactly the kind of package that ends up in every project in a solution, so its dependency list
is the one that matters most.

---

# Naming rules

Derived from `CommunityToolkit.Maui`, which is the closest analogue in the ecosystem — a MAUI control
suite whose dependency-heavy pieces ship as `CommunityToolkit.Maui.MediaElement`, `.Camera`, `.Maps`.

1. **One prefix for the whole family.** Prefix reservation earns the verified badge on nuget.org and is
   granted per prefix. Splitting into unrelated IDs forfeits it and scatters discovery.
2. **Name the satellite after the capability it adds, not the dependency it uses.** `.Barcode`, because
   that is what a consumer wants; not `.Camera`, which is how it happens to be implemented. If the scanner
   implementation is swapped later, the package name is still right.
3. **Singular capability nouns.** `.IntroCarousel`, `.ProgressOverlay` — not `.Extras`, `.Extensions`,
   `.Additional`. A package called `.Extras` can never be reasoned about.
4. **`.Persistence.Sqlite` uses two segments** because the second names the *technology* the way
   `Microsoft.EntityFrameworkCore.Sqlite` does. A future `.Persistence.X` would sit beside it.
5. **Assembly name == package ID.** No exceptions, so a stack trace names the package.
6. **Root namespace == assembly name.** A satellite's public types live under
   `G9MAUIControls.<Capability>`, never injected into the core's namespace. Namespace-squatting makes it
   impossible to tell which package a type came from.

---

# The test for where something belongs

Apply in order. The first answer that is "yes" decides it.

**1. Does it need a dependency the core does not already have?**
→ Satellite. This is the primary reason and needs no further argument.

**2. Is it domain-shaped rather than general?**
→ Satellite. `.ProgressOverlay` is the case: a four-state machine with a cancel contract and a retry
affordance encodes opinions about a workflow. The core ships the *seam*
(`IG9BottomAnchoredOverlay`) and lets the opinion live outside.

**3. Would it want to version independently of the core?**
→ Satellite. A component under active iteration drags the core's version with it otherwise, and the core's
version is the one every consumer sees.

**4. Otherwise** → core.

**The asymmetry that decides marginal cases:** folding a satellite into the core later is a
**non-breaking** merge (the types keep their names; the old package becomes a metapackage or is
deprecated). Pulling a component out of the core later is **breaking** for everyone. So when it is a
close call, split — the cheap direction is inward.

---

# Rules for every package in the family

## Metadata (ADR-0012)

Every package sets: `PackageId`, `PackageVersion`, `Title`, `Description`, `Authors`, `Copyright`,
`PackageTags`, `PackageIcon` (128×128 PNG, transparent), `PackageReadmeFile`, `PackageProjectUrl`,
`PackageLicenseExpression`, `PackageReleaseNotes`, SourceLink, `.snupkg` symbols.

Shared values live in `Directory.Build.props`; only `PackageId`, `Title`, `Description`, `PackageTags`
and the README path are per-package. **The README is per-package and mandatory** — it is the package page,
and a shared one would document features the consumer did not install.

## Versioning (ADR-0010)

- The whole family shares one version and ships together. Independent cadence is a v2 problem; today the
  packages are being extracted in lockstep and skew would only create untested combinations.
- Everything is `1.0.0-preview.N` until the suite has been rendered and exercised. **A stable package
  cannot depend on a prerelease one**, so the family went stable together, at 1.0.0, rather than one
  package at a time.
- A satellite depends on the core with a plain minimum version — no exact pins, no upper bounds.

## Dependencies

- Adding a dependency to **any** package requires an ADR naming the alternative that was rejected.
- Adding one to the **core** additionally requires justifying why it cannot be a satellite instead.
- Plain minimum versions via Central Package Management in `Directory.Packages.props`. Never an
  upper bound: it guarantees a restore failure the first time a consumer needs a newer transitive version.
- Platform-conditional references use
  `Condition="$([MSBuild]::GetTargetPlatformIdentifier('$(TargetFramework)')) == 'android'"`, which is the
  form that survives a TFM version bump — unlike comparing the whole TFM string.

## Trimming / AOT (ADR-0011)

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

- `.Persistence.Sqlite` additionally annotates its reflective surface and declares the irreducible
  remainder with `[RequiresUnreferencedCode]` — see ADR-0014, including the two settings a consumer needs.
- `VerifyReferenceTrimCompatibility` stays off; it reports on dependencies, not on us.
- Real verification is a consuming app published with full linking, because a library build cannot see
  its dependencies' implementations. Until that has run the claim is "declared and analyzer-clean".

## Target frameworks

`net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, and `net10.0-windows10.0.19041.0` (Windows only
when building on Windows). **No `netstandard2.0` and no plain `net10.0`** — every package touches MAUI
types or platform APIs, so a non-platform TFM could only be a reference assembly that throws at runtime.
`.Persistence.Sqlite` is the one that could plausibly add plain `net10.0` later for unit tests; it does
not today because its database locator resolves MAUI app-data paths.

## Build gate (ADR-0013)

`dotnet pack -c Release` for the whole solution. XAML only compiles in Release, so Debug success proves
nothing about XAML.

---

# Adding a new package

1. Apply the placement test above. Write the ADR **first** — if the reasoning does not survive writing
   down, the package is wrong.
2. `G9MAUIControls.<Capability>/` with a csproj that inherits the shared props and sets only its own
   metadata.
3. Add to `G9MAUIControls.slnx`.
4. Its own `README.md`, leading with what it adds and what it costs in dependencies.
5. Add the row to the table at the top of this file and to `14-SolutionStructure.md`.
6. `dotnet pack -c Release` and confirm the produced `.nupkg` dependency list contains what you expect —
   the package graph is the thing being designed, so look at it rather than assuming.

---

# What is deliberately NOT going to be a package

**A metapackage that references everything.** It would defeat the entire point: consumers would install it
by reflex and acquire every dependency, which is the situation the split exists to avoid.

**`.Analyzers`.** Two analyzers are wanted (a case-sensitive-GUID-comparison detector for the SQLite
package, a `Shadow`-usage detector for the core). Both are worth building; both ship *inside* the package
they analyse, via `analyzers/dotnet/cs`, so a consumer gets the diagnostic automatically without knowing
a second package exists.

**A `.Testing` package.** The fake BLE transport and the in-memory preference store are genuinely useful
for consumers, but they are small enough to live as `internal` test doubles until somebody actually asks.
Shipping a testing package before there is a consumer is speculative surface area.
