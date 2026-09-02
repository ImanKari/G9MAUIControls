# 11 - Engineering Log

# G9MAUIControls
## Issues, Risks, Lessons

Lessons earned in this subtree. The test for inclusion: *would a competent engineer, doing this again
in three weeks with no memory of today, plausibly repeat it?*

The source product's own hard-won lessons are NOT duplicated here — they live in
`G9MAUIControls/Controls/G9Controls.md` §0 and §15, which came across with the code. Read those; they
are the expensive ones.

> **A note on `G9NodeControl.App`.** Several entries below were found by, or in, a BLE client app that
> lived in this subtree as the suite's second consumer. It **moved out on 2026-08-14** — it is a product
> app, not part of the library deliverable, and this repository is now purely the packages plus the gallery that
> verifies them. The entries keep its name because that is what happened, and a log rewritten to match
> today's layout stops being a record. Only the *paths* are stale; every lesson still applies, and
> `G9Controls.Gallery` inherited the `UseG9Packages` verification path that app used to carry.

---

## LES-0001 — A non-greedy regex ate two whole methods, twice

**Symptom.** After scripting the removal of a `TouchBehavior` factory from `G9TabBar.cs`, the build
reported `CS0103: The name 'CreateTapGesture' does not exist` plus a stray `{` at file scope. The
same mistake then recurred on the re-derived file.

**Root cause.** The pattern was
`private static TouchBehavior CreateItemTouchBehavior\(ICommand command\)\r?\n[ \t]*\{.*?\r?\n[ \t]*\}\r?\n[ \t]*\}\r?\n\r?\n`.
The method's body ends `};` then `}` — and `};` does not match `\}\r?\n`, so the non-greedy `.*?`
sailed past the intended closing brace and stopped at the next `}\n    }\n\n` several methods later,
deleting everything in between.

**Rule.** **Do not delete a C# member with a brace-counting regex.** Either match the member's exact
full text (read it first, replace it literally), or splice the surrounding region back from source and
cut precisely. A regex that "usually" finds the end of a method will one day find the end of a
different one, and the failure is silent until the compiler happens to notice.

**Corollary that saved this twice:** after any scripted removal, assert the count of what should
remain, not just the count of what was removed:
```pwsh
foreach ($m in 'CreateTapGesture','OnBottomItemTapped') {
    ([regex]::Matches($text, "(?m)^\s*(private|internal|public).*\b$m\b\s*\(")).Count
}
```

---

## LES-0002 — Debug builds do not compile XAML, so `pack` finds errors nothing else does

**Symptom.** All four TFMs built clean in Debug. `dotnet pack -c Release` then failed with seven
`XamlC error XC0000: Cannot resolve type` errors across five `.xaml` files.

**Root cause.** `MauiStrictXamlCompilation` (and XAML compilation generally) only runs in Release.
Every stale `xmlns` and renamed markup extension in a `.xaml` file was invisible in Debug.

Two distinct classes of error hid there:
1. **A renamed markup extension.** `ThemeColorExtension` → `G9ColorExtension` means the XAML usage
   must change from `{themeManager:ThemeColor …}` to `{theme:G9Color …}` — XAML resolves a markup
   extension by its class name **minus the `Extension` suffix**, so renaming the class silently
   invalidates every usage.
2. **A removed package's XAML namespace.** `xmlns:icons="http://www.aathifmahir.com/…"` and its
   `{icons:Material Icon=Add}` usages survived the C# icon migration entirely, because no C# file
   referenced them.

**Rule.** **Run `dotnet pack -c Release` before believing any XAML change.** Add it to the definition
of "built". And when renaming a markup extension class, grep `.xaml` for the OLD extension name, not
the old class name.

---

## LES-0003 — `[AutoBindable]` backing fields legitimately trip `CS0169`

**Symptom.** ~300 `error CS0169: The field '_showApplyButton' is never used` and a matching
`IDE0044: Make field readonly`, on fields that obviously ARE used — the generator builds a public
property from each one.

**Root cause.** `M.BindableProperty.Generator` emits a property that reads and writes through
`BindableProperty` `GetValue`/`SetValue`. It never touches the field. The field exists purely to
declare the property's name, type and default to the generator, so from the compiler's point of view
it is genuinely unreferenced — and `readonly` would break the constructor defaults the generator
relies on.

**Rule.** These two are false positives inherent to the generator and are suppressed project-wide
with that reason written in the csproj. **But verify before suppressing at this scale:** the check
that settled it was `dotnet build -p:EmitCompilerGeneratedFiles=true` followed by reading the emitted
`*.generated.cs` and confirming the field name appears only in an `<inheritdoc cref="_field"/>`
comment. Suppressing 300 errors on a hunch would have hidden real ones.

**Related trap.** A first attempt to count these errors used
`... | grep -oE "error (CA|CS|IDE)[0-9]+" | sort | uniq -c`, which reported 638 IDE0044 and 616
CS0169 — roughly double the truth, because MSBuild echoes each diagnostic more than once. **Always
`sort -u` the full `file(line,col): error CODE: message` line before counting**, or a build looks
twice as broken as it is and priorities get set from noise.

---

## LES-0004 — A `(?<!G9)` lookbehind does not stop a namespace-qualified match

**Symptom.** After applying `(?<!G9|IG9)\bBottomSheet` → `G9BottomSheet` to bring some late-copied
files in line, four files failed with
`CS0234: The type or namespace name 'G9BottomSheet' does not exist in the namespace 'G9MAUIControls'`.

**Root cause.** The lookbehind guards against a `G9` **prefix** on the token. In
`using G9MAUIControls.BottomSheet;` the character before `BottomSheet` is `.`, so the guard passes
and the namespace segment became `G9MAUIControls.G9BottomSheet`.

**Rule.** When renaming a token that also appears as a **namespace segment**, protect namespaces
first with placeholders, rename types, then restore — which is what the main extraction pass did
correctly, and what the follow-up patch skipped. Same lesson, second form: the ordered
protect → rename → restore pipeline is not ceremony, and dropping it "for one small fix" is exactly
when it bites.

---

## LES-0005 — Insert-once patterns silently insert N times

**Symptom.** `CS0102: The type 'G9BottomSheetHeightMemoStore' already contains a definition for
'MemoPreferenceKey'` — six times — plus malformed XML doc comments.

**Root cause.** The intent was "add this const at the top of the class". The pattern used was
`(?m)^(\s*)(private|internal|public)(\s+static\s+)` with a replacement, and `[regex]::Replace` in
PowerShell replaces **every** match by default. It fired on every static member in the file. A
follow-up line-level dedupe then removed the duplicate consts but left their doc comments orphaned,
producing `CS1570` on top.

**Rule.** For "insert once", pass an explicit count (`[regex]::Replace($t, $p, $r, 1)`) or anchor on
the class declaration and use `String.Insert`. And when a scripted edit needs a second scripted edit
to clean up after it, stop scripting and edit the file directly — the second script is where the
orphaned fragments come from.

---

## LES-0006 — Central transitive pinning inflates a *published* package's dependency list

**Symptom.** The first `G9MAUIControls.Barcode` pack advertised seven dependencies, including
`Microsoft.Extensions.DependencyInjection.Abstractions` and `Microsoft.Extensions.Logging.Abstractions` —
packages Barcode neither references nor uses. They were `PackageVersion` entries declared for the SQLite
package.

**Root cause.** `CentralPackageTransitivePinningEnabled=true` works by promoting transitive dependencies
into **direct** ones. For a packable project, direct references are what land in the `.nuspec`. So every
version declared anywhere in `Directory.Packages.props` became a published dependency of every package in
the subtree.

**Why it matters more than it looks.** This is exactly the dependency-list inflation the whole ecosystem
split exists to prevent (ADR-0009). The split had been done correctly at the project level and then undone
at the packaging level, silently, by a property that is *good advice for applications*.

**Rule.** **Transitive pinning ON for apps, OFF for library subtrees.** An app's dependency list costs
nothing because nothing consumes it; a library's is its contract. Verified by measurement, not reasoning:
turning it off took Barcode from 7 advertised dependencies to 3 on Android/iOS and 2 elsewhere.

**Corollary — inspect the `.nuspec`, do not assume it.** The dependency graph is the thing being designed
in a package family, so look at it:

```pwsh
# after dotnet pack
python -c "import zipfile,glob,xml.etree.ElementTree as ET; z=zipfile.ZipFile(glob.glob('*.nupkg')[0]); ..."
```
It is the only way to see what a consumer actually acquires.

---

## LES-0007 — `Directory.Build.props` cannot read a flag the project sets

**Symptom.** Every satellite failed restore with
`NETSDK1013: The TargetFramework value '' was not recognized`, despite each setting
`<G9MauiLibrary>true</G9MauiLibrary>` and `Directory.Build.props` containing a
`Condition="'$(G9MauiLibrary)' == 'true'"` block that sets `TargetFrameworks`.

**Root cause.** `Directory.Build.props` is imported at the **top** of the project, before the project body
is evaluated. The condition was therefore tested against an empty value every time, the block never
applied, and `TargetFrameworks` came out blank.

**Rule.** A shared block that a project must **opt into** goes in a separate `.props` file the project
`<Import>`s in its own body — not behind a condition in `Directory.Build.props`. `Directory.Build.props` is
for what applies unconditionally; `Directory.Build.targets` (bottom import) is for what needs to see
project-set values but is not needed during evaluation of `TargetFrameworks`.

Here: `G9MauiLibrary.props`, imported as the first line of each satellite's body. The import also
reads better at the call site than an invisible flag.

---

## LES-0008 — A source generator's emitted attribute collides across a package family

**Symptom.** Every satellite referencing the core reported
`CS0436: The type 'AutoBindableAttribute' ... conflicts with the imported type ... in 'G9MAUIControls'`.

**Root cause.** `M.BindableProperty.Generator` emits its own `AutoBindableAttribute` into **every project
it runs in**, and the emitted copy is `public`. A satellite that both references the core and uses the
generator therefore sees two identical declarations. The compiler resolves to the local one and says so in
the message — it is a genuine false conflict, and not fixable from our side because the generator controls
the emitted visibility.

**Rule.** Suppress `CS0436` in any satellite that uses the generator, with that reason written at the site.
The alternative — hand-writing `BindableProperty` declarations in satellites to avoid the generator — is
worse: more code, and the two halves of the family would then be written in different styles.

**Generalisation worth remembering:** a source generator that emits a *public* marker type is
package-family-hostile. When evaluating a generator for a multi-package library, check whether its emitted
attributes are `internal`.

---

## LES-0009 — A satellite package found the core's overlay-hosting API is `internal`

**Symptom.** `G9MAUIControls.ProgressOverlay` cannot compile against the core:

```
CS0122: 'G9ModalHostRegistry' is inaccessible due to its protection level
CS0122: 'ModalHost.Page' is inaccessible due to its protection level
CS0122: 'ModalHost.ToastHost' is inaccessible due to its protection level
```

**Root cause.** In the source app every overlay lived in the same assembly, so `internal` cost nothing.
Split into packages, the host registry becomes the boundary a satellite has to cross: mounting an overlay
means resolving the current page's `ToastHost`, and that is the only way to do it.

**Why this is the interesting part.** The core's *public* surface was designed by inspecting what
consumers would call. It was still wrong, and no amount of review would have caught it — **building a
second package against the first is what found it.** An extracted library's real API boundary is not
visible until something outside the assembly tries to use it.

**Rule.** When extracting a library from an app, treat the **first satellite as an API test**, not as a
bonus deliverable. Build it early and cheaply, precisely to discover which `internal`s were only internal
because everything used to be one assembly.

**The fix** (pending, tracked in `09-Progress.md`) is not a blanket `public`. The right shape is a narrow,
documented hosting contract — enough for "give me the current page's toast layer", not the whole registry:

```csharp
public interface IG9OverlayHost           // what a satellite actually needs
{
    Layout ToastHost { get; }
    Page   Page      { get; }
}

public static class G9OverlayHosts        // public accessor, internal registry stays internal
{
    public static bool TryGetCurrent(out IG9OverlayHost host);
}
```

`[assembly: InternalsVisibleTo]` was considered and rejected: it would make every satellite a friend of
the core's entire internals, which is a far larger commitment than the one method needed — and it does
nothing for a third-party consumer who wants to write their own overlay.

---

## LES-0010 — A satellite's XAML must qualify `clr-namespace` with `;assembly=`

**Symptom.** `G9MAUIControls.ProgressOverlay` built clean on all four TFMs, then failed to pack:

```
XamlC error XC0000: Cannot resolve type "clr-namespace:G9MAUIControls.Theming:G9Color"
```

The identical markup — `{theme:G9Color SurfaceContainerLowest}` against the identical namespace — compiles
and packs fine inside the core.

**Root cause.** A `clr-namespace` with no `;assembly=` resolves **in the declaring assembly**. In the
core's own XAML that is the core, so it works. In a satellite's XAML the declaring assembly is the
satellite, so every core type it names is invisible — even though the C# in the same project resolves them
without ceremony, because C# uses assembly references and XAML does not.

**Rule.** Every `xmlns` in a satellite's XAML that names a core type must be written
`clr-namespace:G9MAUIControls.Theming;assembly=G9MAUIControls`. This applies to `Icons`, `Controls`,
`Theming`, `Hosting`, `Popup`, `BottomSheet` and `Toast`.

**Why it is easy to lose an afternoon to:** it compounds with LES-0002. XAML only compiles in Release, so
the *only* signal is `dotnet pack`, and the error names the type rather than the missing qualifier — which
sends you looking at the type's name and visibility (both fine) instead of at the `xmlns` line.

---

## LES-0011 — Extracting a library leaves `internal` in the wrong places, and only a satellite finds them

**Symptom.** Building satellites against the core turned up four separate accessibility failures, in order:

| Blocked | Why the satellite needed it |
|---|---|
| `G9ModalHostRegistry`, `ModalHost.ToastHost/.Page` | mount an overlay into the page's toast layer |
| `G9ToastHelper.ReflowInlineToastsForHostAsync` | tell the toast stack its own height changed |
| `G9Metrics` | match the suite's radii, paddings, durations |
| `G9Colors`, `G9Visuals`, `G9VariantColors` | match the suite's alpha recipes and variant mapping |

**Root cause, and the pattern.** In the source app everything lived in one assembly, so `internal` cost
nothing and was the sensible default. After extraction the assembly boundary is a real API boundary — and
the members that had to cross it were not the ones anybody would have predicted by reviewing the public
surface.

The `G9Metrics` case is the sharpest illustration: `G9ControlBase` and `G9OutlinedFieldBase` were **already
public**, so the suite advertised "derive from these to build your own control" while keeping every design
token needed to make such a control look right `internal`. A public base class whose supporting tokens are
unreachable is an incomplete boundary, and no amount of staring at the public API reveals it.

**Rule.** **Treat the first satellite as an API-boundary test, and build it early.** It is the only thing
that finds which `internal`s were internal merely because everything used to be one assembly. Reviewing the
public surface does not, because the question is not "is this API good" but "is this API sufficient", and
only a real external consumer can answer that.

**Corollary — fix the boundary, do not widen it wholesale.** Each of the four was resolved by publishing
the narrowest thing that answered the need: a two-member `IG9OverlayHost` rather than the whole registry;
one method rather than the whole toast helper's internals. `[assembly: InternalsVisibleTo]` would have
silenced all four in one line and made every satellite a friend of everything — including for a third-party
consumer, who gets no benefit from it at all.

---

## LES-0012 — Contracts written ahead of the implementation shipped eight promises the package cannot keep

**Symptom.** Writing the verification app's overlay page against `G9MAUIControls.ProgressOverlay`, every
line of the natural usage failed to compile: `G9ProgressOverlayHelper.ShowAsync(new G9ProgressOverlayOptions
{ ... })`, `.Report(...)`, `.CompleteAsync(...)`, `.FailAsync(...)`. None of those existed. What did exist
was `ShowAsync(string contextText, position)`, `TryShowCurrentSuccessAsync`, `TryShowCurrentFailureAsync`,
and a `WeakReferenceMessenger` broadcast for progress.

**Root cause.** `G9ProgressContracts.cs` was authored from the design notes *before* the ported
implementation was re-pointed at the package, and then never reconciled with it. Three public types
described behaviour that was never built:

| Public type | Status |
|---|---|
| `G9ProgressOverlayOptions` | dead — nothing accepted it. Advertised a title distinct from the context text, per-session `OnCancel`/`OnRetry` delegates, tunable `SuccessLinger`/`ErrorLinger`, an `AllowMinimize` opt-out, and an `OnClosed` callback. The session implements **none** of the six. |
| `G9ProgressOutcome` | dead — nothing produced it. Existed only as `OnClosed`'s payload. |
| `G9ProgressOverlayState` | live *concept*, dead *type* — the view carried a private `ToastVisualState` with the identical four members and never referenced the public one. |

Every one of them compiled, documented itself convincingly, and would have shipped in 1.0.

**Why nothing caught it.** A `public` type needs no consumer to compile, and a package builds and *packs*
green with an entire unused vocabulary in it. Reviewing the file reads as reasonable API design — the
members are plausible, the XML docs justify them. The defect is only visible from **outside**, at the moment
someone tries to use the API and finds the call does not exist.

**What was done.**

* Deleted `G9ProgressOverlayOptions` and `G9ProgressOutcome`, leaving a comment block in their place that
  lists what the overlay actually offers and why each absent knob is absent — so the next reader does not
  re-add them from the same design notes.
* Made `G9ProgressOverlayState` the single state model: the view's private duplicate is gone, and
  `G9ProgressOverlayView.VisualState` exposes it.
* Added the two calls a consumer actually needs, so the messenger stops being the public contract:
  `G9ProgressOverlayHelper.Report(...)` / `ReportQueued(...)` in, and a `CancelRequested` event out.

**Rule.** **A public type with no consumer inside the package is a defect, not neutral surface.** It is a
promise the implementation has not agreed to, and it is *free* to write and *breaking* to withdraw once
published. Before shipping, grep each public type for a use outside its own declaration; anything with zero
hits is either wired up or deleted. And where the design notes and the code disagree, the code is the
specification — the fix is to correct the contract, not to leave the aspiration public.

**Corollary.** The verification app earned its cost here. Nothing else — not four TFMs, not `pack`, not
reading the file — could have found this, because the fault was *absence of use*.

---

## LES-0013 — The one resource dictionary every consumer MUST merge could not be merged at all

**Symptom.** The gallery's Release build (Debug says nothing — LES-0002) failed on its own `App.xaml`:

```
error XC0124: Resource "/Hosting/G9PageTemplate.xaml" not found.
```

The path is correct and the file is in the package.

**Root cause.** MAUI has no cross-assembly URI form for `<ResourceDictionary Source="..." />`. A source path
resolves **only** against the assembly whose XAML declares it. `G9Theme.Light.xaml` was unaffected because
it carries `x:Class`, which compiles it into a type that can be instantiated from anywhere;
`G9PageTemplate.xaml` had no `x:Class`, so it was reachable from inside the core and from nowhere else.

This was the worst possible file to have that defect. `G9PageBase` resolves the template by resource key in
its constructor and **throws** without it, so the dictionary is mandatory for every consumer — the suite was
shipping a required piece of setup that no consumer could perform.

**Fix.** `x:Class="G9MAUIControls.Hosting.G9PageTemplate"` plus a code-behind partial, and consumers merge
by **type**:

```xml
xmlns:g9="clr-namespace:G9MAUIControls.Hosting;assembly=G9MAUIControls"
...
<ResourceDictionary.MergedDictionaries>
    <g9:G9PageTemplate />
    <g9Theming:G9ThemeLight />
</ResourceDictionary.MergedDictionaries>
```

**Rule.** Every resource dictionary a library expects a consumer to merge must carry `x:Class`, and the
documentation must show the merge-by-type form. Never document a `Source=` path across an assembly
boundary — it works in the declaring assembly, which is exactly why it survives review.

---

## LES-0014 — The core was holding public vocabulary for a capability it does not ship

**Symptom.** Writing the satellites page needed `G9BarcodeScanMode`. It resolved — from
`G9MAUIControls.Controls.Shared.G9Enums` in the **core**, while the only control that uses it,
`G9BarcodeTextEntry`, ships in `G9MAUIControls.Barcode`.

**Root cause.** Ordinary extraction residue: in the source app the enum sat with its siblings in one shared
file, and splitting the *controls* into packages did not split the *enums* that travelled with them. A grep
confirmed nothing in the core referenced either barcode enum.

**Why it matters more than tidiness.** It inverts the ecosystem's central claim. The core is meant to be
small and stable so consumers can depend on it cheaply; instead it published two barcode types, meaning a
consumer who never scans anything still sees barcode vocabulary in IntelliSense, and the core's public
surface would have to stay stable for a satellite's benefit. Moved to `G9MAUIControls.Barcode` — keeping the
`G9MAUIControls.Controls` namespace, so a consumer still needs exactly one `using` for the control and its
enums.

**Rule.** After splitting a package, audit the core for types whose only consumer is a satellite —
enums, option records, `EventArgs`, and interfaces are the usual stowaways. Ask of each public type in the
core: *does the core itself use this?* A no means it is in the wrong package. Do it before the first publish;
afterwards the move is a breaking change.

---

## LES-0015 — `WarningsNotAsErrors` does not travel to a consumer, and a trim-relaxed package fails its consumer's publish

**Symptom.** `G9MAUIControls.Persistence.Sqlite` builds clean. `G9NodeControl.App` builds clean. Then:

```
dotnet publish -c Release -f net10.0-android -p:AndroidLinkMode=Full -p:PublishTrimmed=true
→ error NETSDK1144: Optimizing assemblies for size failed
   232 × Trim analysis error IL2091, 20 × IL2087, 6 × IL2111, … all inside the PACKAGE's source files
```

**Root cause, in two parts.**

1. **`WarningsNotAsErrors` is per-project.** It governed the package's own compilation and stopped there.
   When the app publishes trimmed, ILLink re-analyses the package's IL and attributes every finding to the
   **app's** project, where the codes are errors again. ADR-0014 recorded the decision to *declare* the
   reflection constraint rather than hide it, and that decision was right — but the ADR described the
   consumer escape hatch as `TrimmerRootAssembly`, and that is necessary and **not sufficient**.
2. **`[SuppressMessage]` in consumer code cannot reach it.** The suppression on `G9DeviceRegistry` correctly
   silenced IL2026 at *our* call sites. It has no effect on IL2087/IL2091 raised inside
   `SqliteQueryBuilder<T>` — those are the package's own code, and a consumer has no suppression scope over
   another assembly.

**What was done — annotate everything annotatable, then declare the remainder.**

The flood was not irreducible. `SqliteDtoCache<TEntity, TDto>` already carried
`[DynamicallyAccessedMembers(All)]`, but the types feeding it did not, so the chain broke at the first
unannotated hop and every call downstream reported. Annotating the whole chain — `SqliteRepository<T>`, the
four query builders, the five accessor interfaces **and their five implementations**, plus the generic
*method* type parameters (`GetDtoCacheData<TEntity, TDto>`, `InnerJoin<TLeft, TRight>`,
`WhereLocalized<TEntity>`, …) — cut it from **232 IL2091 to 4**:

| | Before | After |
|---|---|---|
| IL2091 | 232 | 4 |
| IL2087 | 20 | 6 |
| IL2111 | 6 | 6 |
| total trim errors | ~280 | ~20 |

That is strictly better than a suppression, and not only for tidiness: `DynamicallyAccessedMembers` makes
the trimmer **preserve** the entity's members rather than merely stop complaining, so a consumer's columns
survive the link.

The ~20 that remain are the genuinely reflective core — `GetEntityColumnMap`, `IsLocalized`, a delegate over
an annotated method — where the annotation cannot be expressed because the `Type` arrives from a dictionary.
For those the consumer must relax the same codes the package does, in its own project:

```xml
<WarningsNotAsErrors>$(WarningsNotAsErrors);IL2026;IL2070;IL2077;IL2087;IL2091;IL2111</WarningsNotAsErrors>
```

Narrow list, deliberately, so a *new* kind of trim violation still fails the build. Still warnings, never
`NoWarn`.

**Two rules.**

- **A library that relaxes a diagnostic must document that its consumers have to relax it too.** Otherwise
  the library builds green, the consumer builds green, and the failure surfaces at `publish` — the last
  place anybody looks, and usually the day of a release.
- **Before declaring a reflection constraint irreducible, follow the annotation chain to its end.** Nearly
  90% of these were not the mapper's reflection at all; they were one missing attribute propagating through
  a dozen generic hops. Annotating a `T` is cheap; each unannotated one multiplies.

**And the meta-lesson, again.** Two apps and five packages all built clean on all four TFMs, in Debug and
Release, and packed. None of that touched this. Only `PublishTrimmed` on a real consumer did — which is
exactly the claim ADR-0011 makes about why a consuming app is the verification mechanism, now demonstrated
twice.

---

## LES-0016 … LES-0021 — six defects found by running the app on a device, none findable otherwise

Deploying `G9NodeControl.App` to an emulator produced **six** distinct failures in a row, five of them in
the core packages. Every one of them had survived: 0-error builds on four TFMs in both configurations,
`dotnet pack` of all five packages, a full-trim Android publish with zero IL warnings, and a
package-reference consumption check. **Not one was reachable without launching the app.**

They are grouped because they share a single shape: *a library that works inside its own assembly and fails
the first time a stranger wires it up.*

### LES-0016 — `G9Theme.Init()` cannot be called where every consumer will call it

`Init()` pushes ~110 palette tokens into `Application.Current.Resources` and subscribes to
`RequestedThemeChanged`. Called from `MauiProgram.CreateMauiApp` — where all the other wiring goes, and
where **the migration guide told consumers to put it** — `Application.Current` is null for the whole of
`MauiApplication.OnCreate`, so it threw `NullReferenceException` from inside `ApplyCurrent`, with a stack
pointing at theming internals rather than at the wrong call site.

Three `Application.Current!` dereferences were claims, not checks. Replaced with one `RequireApplication`
guard that throws an `InvalidOperationException` naming the correct call site (`App`'s constructor, after
`InitializeComponent()`). **A library cannot prevent a consumer calling it too early; it can refuse in a
sentence that says what to do instead.**

### LES-0017 — `AddG9Sqlite` never registered the services its own provider asks DI for

`G9SqliteConnectionProvider(IG9SqliteDatabaseLocator locator)` resolves its locator from the container, and
`AddG9Sqlite` registered `G9SqliteOptions` and the provider but **not** the three ambient services the
builder had already settled. First database touch:
`Unable to resolve service for type 'IG9SqliteDatabaseLocator'`. Fixed with `TryAddSingleton` from the
frozen options (TryAdd so a consumer's own registration wins). The same file also claimed to register an
open generic `IG9SqliteRepository<T>` — **a type that does not exist** (the LES-0012 pattern again).

### LES-0018 — a theme dictionary the consumer merged could never be replaced

`ReplaceMergedDictionary` removed only the dictionary it remembered adding, so the very first
`ApplyCurrent()` — with `_activeTheme` still null — **added a second theme dictionary on top of the
consumer's**. Both stayed merged for the app's lifetime. Now removes every `G9ThemeLight`/`G9ThemeDark`
present. **Worth recording that this was diagnosed as the cause of a mixed light/dark render and was not**
— fixing it changed nothing on screen. The real cause was LES-0019. The bug is real; the hypothesis was
wrong, and it was deployed before being proven, against this repo's own rule.

### LES-0019 — a page with no background renders BLACK, in every theme

`G9PageTemplate` keeps an always-present `BackdropHost` `BoxView` directly beneath `ContentHost`, painted
`G9BottomSheetSettings.BackdropCardColor` — **default `Colors.Black`** — so the screen edges stay dark while
a bottom sheet recedes the page. A page that leaves its own background unset is transparent over that, so
the entire screen renders black regardless of theme, with dark body text on it and nothing wrong in any
dictionary.

In the source app every page's content happened to be opaque, so it never surfaced. `G9PageBase` now paints
`G9Palette.Background` by default (respecting a subclass override, same detection as `G9ContentViewBase`).
**And the page background alone is not enough**: the backdrop sits above it, so any transparent *region* of
the page content is still black — which is why `MainPage`'s root grid sets its own background.

### LES-0020 — `Resources["Key"]` throws, so the `?? fallback` beside it can never run

Four sites across `G9TabBar`, `G9ToastHelper` and `G9PopupHelper` read an optional app-supplied font key as:

```csharp
Application.Current?.Resources["CulturalFont"] as string ?? G9Culture.RtlFontFamily ?? string.Empty
```

The author's intent is unmistakable, and unreachable: the indexer throws `KeyNotFoundException` before the
`??` is ever evaluated. `G9TabBar` took the whole app down **in its constructor** on the first consumer that
did not happen to define the key — which for optional convenience keys is the normal case. Replaced with one
`G9Culture.ResolveAppFont(key, fallback)` built on `TryGetValue`.

**Rule: never index a `ResourceDictionary` for a key the consumer is not required to define.** A
`?? fallback` next to an indexer is a false comfort, and it reads as safe in review.

### LES-0021 — using `G9TabBar` requires `.UseSkiaSharp()`, and nothing said so

`G9TabBar`'s FAB-notch silhouette is a SkiaSharp view. Without the handler registration the tab bar throws
`HandlerNotFoundException` for `G9TabBarShadowView` as it builds. The suite ships `UseG9SheetView()` for its
own handler but says nothing about Skia. Documented in `07-DesignSystem.md` §2 and the migration guide;
an aggregate `UseG9Controls()` that registers both is the better long-term API.

### The meta-lesson, now demonstrated three times

Builds, packs, trim publishes and package-reference checks all verify that code is *well-formed*. **They
cannot verify that it is usable.** Every defect above is a wiring contract — an ordering requirement, a
missing registration, a resource the library reads but does not seed, a handler it needs but does not ask
for. Those live in the gap between "compiles" and "a stranger got it running", and only launching closes it.

Corollary for this repo: `09-Progress.md`'s "honest gap" was right to call the visual pass the highest-value
remaining task, and it under-sold it. The first launch found six defects before anyone looked at a pixel.

---

## LES-0022 — a manifest permission is not a granted permission

**Symptom.** Tapping "Scan for nodes" killed the app instantly:

```
java.lang.SecurityException: Need android.permission.BLUETOOTH_SCAN permission
  … ScanController.registerScanner()
  at android.bluetooth.le.BluetoothLeScanner.startScan
```

`BLUETOOTH_SCAN` and `BLUETOOTH_CONNECT` were both declared in `AndroidManifest.xml`, correctly, with
`neverForLocation` on the scan permission.

**Root cause.** From Android 12 (API 31) those are **runtime** permissions. Declaring them is necessary and
not sufficient; they must be requested. The failure mode is what makes this worth an entry: the platform
does not return an error code — `startScan` throws a Java `SecurityException` that arrives as a FATAL
EXCEPTION on the main thread, so **the app dies rather than the call failing**. There is nothing to
`try`/`catch` at the call site if you did not know to look.

**Fix, in the session rather than the view.** `G9BleSession` now gates both `ScanAsync` and `ConnectAsync`
on `Permissions.RequestAsync<Permissions.Bluetooth>()` — one request covers SCAN/CONNECT/ADVERTISE on 31+
and the legacy location-backed permission below it. A denial becomes
`G9SessionState.PermissionDenied`: a real state with its own pill and its own explanation, not an error and
not folded into `Disconnected`, because it is a decision the user made and can reverse.

There is also a `catch` for a refusal around the radio calls themselves — **a grant checked a moment ago is
not a guarantee for the call that follows**, since permissions can be revoked from Settings while the app
runs.

**Rules.**

- **Put the permission gate where the assumption lives.** The session is the only thing that touches the
  radio, so it is the only thing that should have to know the radio needs asking. A gate in the view would
  have to be repeated in every view that ever scans.
- **Every runtime-permission denial needs a user-visible state and a sentence saying how to undo it.**
  "Nothing found" and "you declined" look identical otherwise.
- When adding a platform capability, check whether its permission is install-time or runtime **on the
  minimum API the app targets**, not on the emulator you happen to have.

---

## LES-0023 — An extraction has an upstream, and nobody wrote down which commit it was

**Symptom.** A newer copy of the source application was dropped into a scratch working copy, with the
question "is anything missing from the packages?" There was no recorded answer, because **nothing in
this repository said which revision of that application the packages were extracted from.** The scratch
copy was gitignored, so it carried no history here, and the extraction commits named the source folder
but not a revision.

**What that cost.** The baseline had to be *inferred* — and the first inference was wrong. Reasoning from
the extraction commit's timestamp gave the newest source commit that predates it, which produced a tidy
and completely false answer: "one file changed, port it."
Checking it against the code disproved it in one grep — the packages did not contain
`ResolveRestingContentColor`, added by `1294dcf77` on 2026-08-08, a day *before* the supposed baseline.
The real baseline was `73b0e216c` (2026-08-03): the working copy used for the extraction was six days
stale, which no timestamp could have revealed.

Between the two candidate baselines the answer changes from one file to three, and the two files the
wrong baseline hides are both **visual** — a resting-colour split and an iOS safe-area fix. Neither
breaks a build. Both would have shipped as "the package looks subtly different from the app", which is
the most expensive class of bug to trace back to its cause.

**Root cause.** An extraction is a fork, and a fork without a recorded merge-base cannot be updated —
only guessed at. This one was additionally invisible: everything builds, packs and publishes exactly as
well at the wrong baseline as at the right one.

**Rules.**

- **Record the upstream revision in the repository that holds the extraction, at the moment you extract.**
  A single versioned line — `Extracted from <source> <commit> (<date>)` — is the whole fix, and it belongs
  in the extraction's own repository, not in the ignored working copy of the source. Update it every time
  you re-synchronise.
- **Never infer a baseline from timestamps.** A working tree is whatever was last pulled into it, which
  can be arbitrarily older than the commit that used it. Timestamps bound the answer from one side only.
- **Verify any inferred baseline against the artifact before trusting it.** Pick a change from a commit
  in the suspected range and grep the extracted code for it. One grep falsified a plausible answer here;
  it is cheap enough to do even when confident, and confidence is exactly when it is skipped.
- **Diff on the FULL extracted path set, not the headline folders.** The three real changes were in
  `Common/Bases` and two files under the controls folder; the same diff also surfaced ~15 changed files in
  `Common/Entities`, `Common/Enums` and `Common/Utils` that are the application's own domain and must NOT
  be ported. Both directions of that judgement need the whole list in front of you.

---

## LES-0024 — Six wrong instructions in a migration guide nobody had executed yet

**Symptom.** Auditing the consumer migration guide against the code it describes — the first time
anyone had — found six defects. The guide had been marked "synchronized with the final implementation" in
`09-Progress.md`.

| Defect | Failure mode |
|---|---|
| §5 + §8 renamed `AppCultureService` → `G9Culture` | **Worst of the six.** The application's culture service *stays* (hook #1 reads from it). The rename yields `G9Culture.Configure(currentCulture: () => G9Culture.CurrentCulture)` — compiles, reads plausibly, and silently pins the app to one language |
| §3a `using G9MAUIControls.Persistence.Sqlite.Configuration` | namespace does not exist; the folder adds no segment |
| §3a `.Cache(G9CachePolicy.PerQuery)` | `G9CachePolicy` is a class with `Immediate()` / `Debounced(ms)`, not an enum. No such member |
| §6.6 named the popup layer `PopupHost` | it is `G9PopupHost`. `GetTemplateChild("PopupHost")` returns **null**, silently |
| §2 never mentioned `G9ServiceProvider.Initialize(app.Services)` | throws at first control that resolves a collaborator — after startup, so it can pass a smoke test |
| §1b's "the app currently does this" example | cited paths the application does not have, and a theme-dictionary merge it never did |

**Root cause.** Every one of the six is a claim about code, written from memory of the design rather than
read out of the source, in a document with no compiler behind it. The two that matter most — the culture
rename and the popup-host name — are also the two that fail *silently* rather than loudly, which is not a
coincidence: a wrong instruction that breaks the build gets fixed by whoever runs it in ten minutes, so
the ones that survive to be found by an audit are exactly the ones that don't.

**Rules.**

- **A guide that has never been executed is a draft, whatever the progress table says.** "Synchronized
  with the implementation" was written by the same pass that wrote the guide. Only an audit against the
  source, or an actual migration, can retire that status.
- **Every identifier in a migration document is a testable claim — grep it.** Namespaces, type names,
  member names, template-child keys, file paths. The §3 delete list was verified path-by-path against the
  current app in one loop; it was the only section that was already fully correct.
- **Re-measure counts, don't carry them.** The blast-radius and `xmlns` occurrence tables had drifted
  upward in nine days (196 → 207 files for `ThemeManager`). A stale number is a small error that quietly
  licenses the reader to trust the rest of the document.
- **When a rename table meets a decoupling hook, check they don't contradict each other.** §2 said
  "read from `AppCultureService`" and §5 said "`AppCultureService` no longer exists" — two sections of one
  document disagreeing, each self-consistent.

---

## LES-0025 — The verification build went green against the packages it was supposed to replace

**Symptom.** The family was bumped to `1.0.0-preview.2`, all five packages re-packed, and the
package-mode check re-run:

```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

Then `obj/project.assets.json` was read, and every entry said **`1.0.0-preview.1`**.

**Root cause — two independent version numbers that nothing tied together.** `$(Version)` in
`Directory.Build.props` sets the version the family **produces**. The `<PackageVersion>` pins in
`Directory.Packages.props` set the version `G9NodeControl.App` **consumes** on the `UseG9Packages=true`
path. Bumping the first left the second on preview.1, so restore went looking for preview.1 — and found
it, because the previous verification run had re-populated `~/.nuget/packages/g9mauicontrols*` with
exactly that. Every input to a green build was present and stale.

The comment sitting directly above those pins read *"must move with the family version (ADR-0010)"*. It
was correct, it was three lines long, and it did not move anything. **A comment is not a mechanism.**

**Why this is worse than a red build.** A build that fails tells you something is wrong. This one
reported success while exercising none of the changed code — the three ported fixes, the entire reason
for the exercise, were not in the binary. Had the assets file not been read, the session would have ended
with "package consumption verified" written down, and the first thing anyone would trust it for is the
consumer migration.

**Fix.** Derive rather than duplicate — but **not** from `$(Version)`, which was the obvious first attempt
and is wrong:

```xml
<!-- Directory.Build.props -->
<G9FamilyVersion>1.0.0-preview.2</G9FamilyVersion>
<Version>$(G9FamilyVersion)</Version>

<!-- Directory.Packages.props -->
<PackageVersion Include="G9MAUIControls" Version="$(G9FamilyVersion)" />
```

`Directory.Packages.props` is evaluated **in the consuming project**, and `$(Version)` does not mean the
same thing there. For a MAUI *app* the SDK derives `$(Version)` from `$(ApplicationDisplayVersion)`, so the
pins resolved to the app's own `1.0` and restore failed:

```
error NU1102: Unable to find package G9MAUIControls.Persistence.Sqlite with version (>= 1.0.0)
  - Found 1 version(s) in …\artifacts\pack [ Nearest version: 1.0.0-preview.2 ]
```

A dedicated property name has one meaning in every project that imports it. `$(Version)`, `$(Configuration)`
and friends are *ambient* — reusing one as a cross-file contract silently rebinds it to whatever the local
project means by that word.

**This second mistake is the more interesting one.** The first (two hardcoded versions) failed silently and
took reading the assets file to catch. The fix for it failed *loudly*, at restore, in four seconds — which
is what a correct design does when you get a detail wrong. Prefer mechanisms whose failure mode is an
error over ones whose failure mode is a stale success, even while you are still getting them working.

**Rules.**

- **One number, one definition.** Two files holding the same version is not redundancy, it is a divergence
  waiting for the moment nobody re-reads both. Derive the second from the first, or generate it.
- **Verify the artifact identity, not the exit code.** "Build succeeded" answers *did it compile*, never
  *what did it compile against*. For a package-consumption check the assertion is in
  `obj/project.assets.json`: right names, right **version**, `"type": "package"`. It is now a checklist
  item in the migration guide's §9 for exactly this reason.
- **A populated package cache makes a stale pin invisible.** Purge `~/.nuget/packages/<id>*` before any
  local package verification. NuGet treats a version as immutable, so it will never re-extract a `.nupkg`
  over a folder that already exists — the freshly packed file is simply ignored.
- **When a comment says "keep X in sync with Y", treat it as an open defect.** It is documenting a manual
  step that will eventually be skipped, and the comment will still be there afterwards, still correct,
  still ignored.

---

## LES-0026 — Six seams were `internal`, and every one of them blocked the app the suite was extracted from

**Symptom.** The first real consumption of the family — re-integrating the application the code came out
of — produced ~730 compiler errors. After the mechanical renames were worked
through, **33 of the remaining ones were `CS0122: inaccessible due to its protection level`**, across
six types:

| Type | What the consumer needed it for |
|---|---|
| `G9ModalHostRegistry` + `ModalHost` | resolving the current page for its own back dispatcher and modal parenting |
| `G9SelectionSheet` | showing a selection list — three call sites |
| `G9ColorExtension.ResolveColor` + `G9PaletteSubscriptions` | painting an app-owned control from the palette, and re-painting it on a theme flip |
| `SqliteGuidStringNormalizer` | **208 call sites** — the canonical id form the repository stores and matches on |
| `SqliteEntityAuditDefaults` | stamping audit columns on rows the app builds itself |
| `SqliteRepositoryCacheRegistry` | resetting caches at a session boundary |

**Root cause.** The extraction drew the public surface around *what the suite needs to render itself*,
not around *what a consumer needs to build alongside it*. Every one of these is a seam a consumer
reaches for the moment it has a control of its own — which is exactly the case §6.7 of the migration
guide already anticipated, and then under-served.

`SqliteGuidStringNormalizer` is the sharpest of the six, and its accessibility was not even the worst
part: it was `internal` **and** sitting in a namespace called `.Internal`, while being the definition of
the id format the whole storage contract depends on. Ids are GUID strings, SQLite compares strings
**ordinally**, and a consumer that normalises differently does not get an error — it silently stops
finding rows. That is a data-correctness contract, not an implementation detail.

**Rule.** *A seam is public when a consumer building a control or an entity beside the suite needs it —
not when the suite needs it.* Before publishing, walk the public surface once from the outside: for each
capability the README claims, write the call the consumer would write, and check it compiles against the
package's public API alone. That exercise is what LES-0009 and LES-0011 each found one instance of; this
entry is what happens when the whole app is put through it at once.

---

## LES-0027 — Three documented behaviours that the code did not have

Found in the same pass, and worse than the accessibility set, because each one *reads* as working:

1. **`IG9SqliteDatabaseLocator.DatabasePathChanged`** — the interface documented it as "the provider
   closes the current connection and resets every cache", and **nothing in the package subscribed to
   it**. The connection swap happened to work anyway, because the path is re-read on every
   `Connection` acquisition, so the NEXT caller noticed — while any already-handed-out
   `SQLiteAsyncConnection` kept writing to the previous user's file. A shared-device user switch was
   the failure case, and the doc said it was handled.

2. **`G9BottomSheetHeightSeeds`** — the type's own example says to seed by a body's type name, but
   `TryGet` compared against the FULL memo key, which
   `BuildFitHeightMemoKey` builds as `identity|w{width}|{culture}|fs{fontScale}`. So the documented
   usage could never match, and the seeding feature was inert for every consumer that followed the
   example. The source app had the identity-stripping logic; the extraction kept the table's shape and
   dropped the lookup.

3. **`G9SafeCommand.DiagnosticsHandler`** — the "More details" button appeared whenever a handler was
   registered, with no way to gate it. Consumers gate a diagnostics surface on a runtime setting, so
   the only options were a permanently visible button that does nothing for ordinary users, or nulling
   a static from wherever the setting changes.

**Root cause, shared by all three.** Each was written from the design, one hop ahead of the code, and
never exercised — the same failure mode as the four READMEs in LES-0024, in a place no reader would
think to double-check. XML docs are the most trusted prose in a package and the least tested.

**Rule.** *When a doc comment states what the package DOES — subscribes, resets, matches, falls back —
find the line that does it before shipping the sentence.* A claim about behaviour is a test case with
no assertion attached.

---

## LES-0028 — A substring find/replace across a consumer corrupts its domain vocabulary

**Symptom.** The migration guide's own starting script (§8) uses plain `string.Replace` for the type
pass. Run unchanged against that application, `'BottomSheet' -> 'G9BottomSheet'` would have produced
`BlockInfoG9BottomSheetData`, `ShowTreeEditG9BottomSheetAsync`, `SamplingTargetG9BottomSheetRequest`
and ~70 more — every domain identifier that merely *contains* the word. `Popup`, `Toast` and `EdgePeek`
each had a comparable set.

**Root cause.** The extraction's own renames were done with substring replacement inside a codebase
where the words `BottomSheet` / `Popup` / `Toast` belonged to the suite. In the consumer they belong to
the *domain* as well, and the two are indistinguishable to `Replace`.

**Rule.** *Rename by whole identifier (`Name`), never by substring, in any tree you do not own
end-to-end.* A useful side effect: with word anchors the ordering problem disappears too —
`SafeButton` cannot match inside `SafeIconButton`, so the "longest first" discipline that §8's
ordered hashtable exists to enforce stops being load-bearing.

(Related, and the reason this is worth writing down twice: LES-0001 and LES-0004 are the same lesson
learned from the *inside* of the extraction. This is what it costs on the outside.)

---

## LES-0029 — Not one icon in the suite could be set from XAML, and the README's own sample was the proof

**Symptom.** The first Release build of a real consumer produced **28 identical errors**:

```
XamlC error XC0009: No property, BindableProperty, or event found for "Icon",
                    or mismatching type between value and property.
```

The property exists. It is `public G9IconSource? Icon { get; set; }`, generated from
`[AutoBindable] private G9IconSource? _icon;`, on nine controls.

**Root cause.** `G9IconSource` carries
`[TypeConverter(typeof(G9IconSourceTypeConverter))]`, and that is how `Icon="Search"` was supposed to
work. But every slot in the suite is declared **`G9IconSource?`**, and MAUI's XAML compiler looks for
the converter attribute on the *property's* type — `Nullable<G9IconSource>` — without unwrapping it.
The attribute sits on the wrapped type, so it is never found, and the error blames the property name
while saying nothing about the conversion.

**How it survived to here.** `G9Controls.Gallery` sets every icon **from C#**, where the
`implicit operator G9IconSource(Enum)` applies and no converter is involved. It sets no icon slot in
XAML at all — so a four-TFM Debug + Release build of the gallery, a full `dotnet pack`, and a trimmed
Android publish all passed while the single most-used attribute in the suite could not compile in a
consumer. The README's own first sample, `LeadingIcon="Search"`, was one of the things that did not
work (LES-0024's failure mode, in a file that had already been audited for it).

**Fix.** An `implicit operator G9IconSource(string)` alongside the enum and glyph ones. Implicit
conversions ARE resolved through the nullable lift, so one operator fixes all nine controls and both
slot positions. The converter stays — it produces the good error message for an unknown name.

**Rules.**
1. *A `[TypeConverter]` on a struct does not reach a `T?` property.* Any value type used as a nullable
   XAML-settable property needs an implicit conversion, not just a converter.
2. *A verification app must exercise each public surface the way a CONSUMER will, not the way that is
   convenient to write.* The gallery's C#-only icon usage was the easy path and it left the XAML path —
   the one every consumer takes and the one the README documents — completely unexercised. Add the
   attribute form to the gallery for every slot the README shows.

---

## LES-0030 — A subtree's `nuget.config` governs restores DRIVEN from it, not restores OF it

**Symptom.** Everything in this document said the migration was green: four TFMs, Release, a trimmed
publish, the library solution standalone. Then the app was deployed to an emulator from Visual Studio
and all five library projects failed to restore:

```
error NU1507: Warning As Error: There are 2 package sources defined in your configuration.
  When using central package management, please map your package sources with package source
  mapping ... The following sources are defined: <internal-feed>, nuget.org
```

**Root cause — two correct facts that do not compose.**

1. `nuget.config` declares `<clear />` then nuget.org, so this subtree resolves **one** source.
   `dotnet nuget list source` run from the repository root confirms it: one entry.
2. NuGet resolves settings from the directory that **drives** the restore, not from each project's own
   directory. When the consuming app's solution drives it — Visual Studio restoring
   the application's own `.sln` from ITS repository root — the walk starts there and
   `nuget.config` **is never in the chain**. The projects then see the consumer's sources
   (a private proxy plus nuget.org), which is two, which with `ManagePackageVersionsCentrally` is
   NU1507, which with `TreatWarningsAsErrors` is a build break.

**Why nothing caught it.** Every verification in this subtree, and every build during the migration,
was rooted at this repository or invoked a `.csproj` path directly — and on those paths NuGet does resolve
per-project, so `<clear />` applies and the count is one. The failing path is the one an actual
consumer uses and the one no verification exercised. `dotnet restore <consumer>.sln` from the CLI also
passes; **it needs the IDE's solution-scoped restore to fail.**

**Fix.** `NoWarn=NU1507` in `Directory.Build.props`, with the reasoning written there. Source
mapping was rejected: adding it here cannot bind a restore that never reads this file, and adding it to
a consumer's root config changes how every unrelated project in that repository resolves packages — a
large behaviour change to silence an advisory about a hazard this subtree does not have (a consumer on
project references is compiling these from source; a consumer on package references gets them from
wherever it gets everything else, which is its own decision).

**Rules.**
1. *"Self-contained" means "builds when rooted here". It does not mean "imposes its config on a
   consumer".* A `nuget.config`, a `global.json` and a `Directory.Packages.props` in a subtree all
   govern the subtree's own builds; only what lands in the PROJECT files — `NoWarn`,
   `ManagePackageVersionsCentrally`, package references — travels into a consumer's restore.
2. *`TreatWarningsAsErrors` in a library subtree makes every consumer-side NuGet advisory a build
   break for that consumer.* Audit which `NU*` codes can be raised by a restore the consumer drives,
   not just the ones seen while building here.
3. *Verify the way a consumer builds, including from the IDE.* The CLI and Visual Studio do not resolve
   NuGet settings identically, and this class of defect only appears in one of them.

---

## LES-0031 — The first screen ever rendered had three defects, and all three were silent

**Context.** The suite was deployed to an Android emulator for the first time — the step
`09-Progress.md` had been calling the single highest-value remaining task since the extraction. The app
launched, did not crash, logged no unhandled exception, and the login screen was **wrong**: a large
black void where the onboarding carousel should be.

Three separate defects, none of which produced an error anywhere.

### 1. `UseResources(keyPrefix:)` was prefixing the CONSUMER's own keys

`G9Strings.UseResources(resources, keyPrefix: "AppControls")` baked the prefix into the provider
delegate:

```csharp
_provider = (key, culture) => resources.GetString(keyPrefix + key, culture);
```

`Get(G9StringKey)` wants that. **`Resolve(string)` does not** — its own doc says "the key belongs to
the consumer's catalogue, not to the suite". The carousel resolves each slide's `TitleResourceKey`
through `Resolve`, so `"IntroSlide1Title"` was looked up as `"AppControlsIntroSlide1Title"`, found
nothing, and rendered an empty label. The prefix now lives in its own field and is applied only on the
`G9StringKey` path.

**Rule.** *When one hook serves two key spaces, the transformation belongs at the call site that owns
the space, not baked into the shared provider.*

### 2. A nullable enum assigned to a nullable icon slot threw

Icon slots are `G9IconSource?`. The natural consumer expression is
`Icon = icons.ResolveOrNull(name)` — a `MyIcons?` where null means "this thing has no icon". Boxing a
null `MyIcons?` to `Enum` yields null, and `implicit operator G9IconSource(Enum)` called
`ArgumentNullException.ThrowIfNull`. So the ordinary "no icon" case was an exception at paint time, and
the compiler flagged `CS8604` at every such site telling the consumer to guard something the nullable
slot already models. The operator now takes `Enum?` and maps null to `Empty`, which
`G9IconFactory.HasIcon` already treats as absent.

**Rule.** *If a slot is nullable, every implicit conversion into it must accept null and mean "empty".*
A nullable target that throws on null is a contradiction the consumer pays for at runtime.

### 3. Content the package deliberately does not ship, deleted rather than kept

The application's own carousel seeded its slide list from a `CreateDefault()` helper and fell back to it.
`G9IntroCarousel` does neither — correctly, since a slide list is one app's videos and copy, and the
migration guide said so. The helper was deleted anyway, because it lived inside the controls folder, and
the instruction there is "delete whole folders".

**Rule.** *"Delete the whole folder" and "keep this file" are both true, and the second one loses.*
When a guide lists per-file exceptions to a folder-level delete, extract the exceptions BEFORE deleting,
not from the guide afterwards. The failure is invisible: the control renders an empty rectangle.

### What this says about the verification gap

The gallery, four TFMs, Release, `dotnet pack`, and a trimmed publish with zero IL warnings all passed
with every one of these present. **Two of the three are in the same satellite the gallery does not
exercise, and the third only appears when a consumer supplies its own catalogue.** Compiling proves the
least of it, and this is the concrete evidence for a claim that had been theoretical since the
extraction.

---

## LES-0032 — A `BindableProperty` default of the wrong type is a crash at first use, and the compiler is silent

**Symptom.** In the consuming app, one tab out of five simply did not open. The tab bar's selection
moved, and the previous tab's content stayed on screen. No crash, no toast, nothing in logcat — because
the app's tab queue builds content inside a safe-run wrapper with the error popup suppressed, so a
throw there is swallowed by design.

Instrumenting the content factory produced it:

```
TypeInitializationException: The type initializer for 'G9MAUIControls.Controls.G9HeaderActionButton' threw
 ---> ArgumentException: Default value did not match return type.
      Property: G9IconSource G9HeaderActionButton.Icon
      Default value type: G9Glyph          (Parameter 'defaultValue')
   at Microsoft.Maui.Controls.BindableProperty..ctor(...)
```

**Root cause.**

```csharp
BindableProperty.Create(nameof(Icon), typeof(G9IconSource), typeof(G9HeaderActionButton),
    G9Glyph.Menu,                     // <-- boxed as G9Glyph
    propertyChanged: ...);
```

`defaultValue` is declared `object`, so **no implicit conversion is applied at the call site** — the
`implicit operator G9IconSource(G9Glyph)` that makes `Icon = G9Glyph.Menu` work everywhere else is
never considered. MAUI compares the boxed value's type against the declared return type inside the
`BindableProperty` constructor and throws. That constructor runs from the class's **static**
initializer, so the failure is a `TypeInitializationException` the first time anything constructs the
control — and it is permanent for the process.

The fix is one cast: `(G9IconSource)G9Glyph.Menu`. `G9IconView` had it right already
(`default(G9IconSource)`), which is why only this one control was affected.

**Why nothing caught it.** It is invisible to the compiler (`object` accepts anything), invisible to
every build and to `dotnet pack`, and invisible to a trimmed publish. It is only visible when the
control is INSTANTIATED — and `G9Controls.Gallery` does not use `G9HeaderActionButton` on any of its
six pages. The first consumer that put it on a screen hit it immediately.

**Rules.**
1. *Every `BindableProperty.Create` default must be the return type exactly — cast it, or use
   `default(T)`.* An implicit conversion operator does not help through an `object` parameter, which is
   precisely where it is most tempting to rely on one.
2. *A type whose static initializer can throw fails permanently and unhelpfully.* Prefer
   `defaultValueCreator:` for anything non-trivial.
3. **The verification gap this exposes is the important part.** The gallery renders the controls it was
   written around; a control it omits is completely unverified, and "the library builds and packs" says
   nothing about it. Before publishing, assert that **every** public control type appears on at least
   one gallery page — a coverage check, not a visual one, and cheap to automate.

---

## LES-0033 — The extraction left the source product's logo hardcoded inside a control

**Symptom.** A one-line grep, run only because the consumer's owner asked whether the separation was
actually clean:

```
grep -rniE '<source-app-name>|<brand>' G9MAUIControls*/**/*.cs
  G9IntroCarousel.cs:193:  Source = "<brand>_logo.png",
  G9IntroSlideItem.cs:16:  ... (e.g. <c><brand>_logo.png</c>) ...
```

`G9IntroCarousel` builds its header logo with the **source product's asset file name baked in**, and
exposes no way to change it. Every other consumer would render a broken image there, forever, with no
error — MAUI resolves a missing `ImageSource` to nothing.

**Root cause.** The rename sweep during extraction was driven by TYPE and NAMESPACE names
(the application's prefix → `G9*`, `AppPageBase` → `G9PageBase`). A brand asset referenced as a **string literal**
matches none of those patterns, so it travelled through untouched. The `09-Progress.md` claim of "zero
references to the source product in code" had already been corrected once (six handler-mapper keys and
two comments); this was a third category nobody had grepped for, because it does not look like an
identifier.

**Fix.** A `LogoSource` bindable property, defaulting to `null` and hiding the slot — the same shape
every other consumer-supplied asset in the suite already uses. The consumer sets it in one line.

**Rules.**
1. *After an extraction, grep the library for the SOURCE PRODUCT's name in every form — identifiers,
   string literals, XML doc examples, asset names, resource keys — not just for the types you renamed.*
   String literals are the ones that survive a rename pass.
2. *Any asset a control draws that it does not ship is a consumer input.* If the control names a file
   the package does not contain, that name belongs in a property, not in a constructor.
3. The general form, and the reason this kept recurring: **an extraction is finished when the library
   cannot name the app, not when the app compiles against the library.**

---

## LES-0034 — Every directional glyph pointed the wrong way in RTL, because the canvas was mirrored on top of the caller's choice

**Symptom.** Reported from a device by the consuming app's owner, in Persian (RTL): the nav-card
drill-down chevron pointed *right* when it should point *left*, and the sheet header's back affordance
pointed *left* when it should point *right*.

Both are the **opposite** of what the code selects:

```csharp
// G9NavCard          RTL -> ChevronBack (points LEFT)   ... rendered pointing RIGHT
// G9BottomSheetHelper RTL -> ChevronForward (points RIGHT) ... rendered pointing LEFT
```

**Root cause — the flip was applied twice.** `G9IconView` draws vector glyphs into a `GraphicsView`
that inherited `FlowDirection` from its parent. Under RTL the platform mirrors that canvas, so a glyph
authored pointing left is *painted* pointing right. The controls had already chosen the correct
directional glyph for RTL, so the platform's mirror reversed a decision that was already right.

It reads as "the arrow points the wrong way", which sends you to the `IsRtl ? a : b` line — where
everything looks correct, because it is.

**This was never limited to two controls.** Every RTL-dependent glyph in the suite was mirrored:
`G9NavCard`, `G9CascadePanel`, three sites in `G9EdgePanel`, and the sheet header. And every
*non*-directional glyph was mirrored too — the tick, the magnifier, the eye — just less visibly.

**Fix.** Pin both children of `G9IconView` to `FlowDirection.LeftToRight`. An icon is a picture, not
text: it renders as authored, and **direction stays the caller's decision**, because only the caller
knows whether a given glyph is directional at all.

**Prior art in the same codebase, missed.** The source product had already hit this exact double-mirror
on its switch's tick and fixed it the same way — by locking that canvas to `LeftToRight`. The lesson
existed, in that product's QA fix history, and did not travel with the extracted code.

**Rules.**
1. *Any self-drawn visual — `GraphicsView`, `SKCanvasView`, a custom handler — must pin its own
   `FlowDirection` unless it genuinely wants to mirror.* Inheriting it means the platform silently
   rewrites your geometry.
2. *Decide direction once.* Either the caller picks a directional asset, or the canvas mirrors. Doing
   both is a no-op in LTR and a reversal in RTL, so **it always ships**: LTR testing cannot see it.
3. When porting code, port the platform WORKAROUNDS with it — they are the expensive knowledge, and
   they live in fix histories rather than in the code.

---

## LES-0035 — "Back" and "drill down" are different affordances; the extraction collapsed them

The sheet header's back button used a **shafted arrow** (`←`) in the source product. The extraction
replaced it with `ChevronBack` — the same bare chevron nav-card rows use for drill-down — because the
suite ships vector glyphs rather than a font (ADR-0003) and the chevron was the nearest one that
existed.

That is a real regression in meaning, not just in looks: a lone chevron in a header reads as an
expander, not as "go back". The consumer noticed immediately.

**Fix.** Author the missing glyphs rather than reuse the near-miss: `G9Glyph.ArrowBack` /
`ArrowForward` (shaft + head), exposed as overridable `G9Glyphs` slots, and used by the sheet header.

**Rule.** *When an extraction cannot reproduce an asset, that is a gap to fill, not a substitution to
make.* Substituting the closest available glyph is invisible in review — it compiles, it renders, and
only someone who knew the original spots it.

---

## LES-0036 — The packaging icon became a WinUI resource, and broke every consumer's Windows build

**Symptom.** Any consumer of `G9MAUIControls.Persistence.Sqlite` **1.0.0** building a
`net10.0-windows*` target failed outright:

```
error MSB3030: Could not copy the file
"…\g9mauicontrols.persistence.sqlite\1.0.0\lib\net10.0-windows10.0.19041\
 G9MAUIControls.Persistence.Sqlite\icon.png" because it was not found.
```

Reported from AgriPad after it moved to SDK 10.0.400. Android, iOS and Mac Catalyst were unaffected,
which is exactly why it shipped.

**Root cause.** `icon.png` sits at each package's project root only to satisfy `<PackageIcon>`, and
`Directory.Build.props` packs it with an explicit `<None Include="icon.png" Pack="true"
PackagePath="\" />`. But on the Windows target the SDK's **default item globs** also picked it up as
`@(Content)` — confirmed with `-getItem:Content`, whose `DefiningProjectName` was
`Microsoft.NET.Sdk.DefaultItems`, not our props file. `@(Content)` is what MakePri indexes, so
`obj/…/filtered.layout.resfiles` contained exactly one line, `G9MAUIControls.Persistence.Sqlite\icon.png`,
and the shipped `.pri` indexed a path the nupkg does not contain — the icon is packed at the package
ROOT, as `PackageIcon` requires. The consumer's Windows build then resolves a copy item for a file
that cannot exist.

**Why only one of five packages.** The asymmetry was the whole diagnostic. The other four reference
`Microsoft.Maui.Controls`, whose targets sweep root images back out of `@(Content)`; this one is
deliberately **Essentials-only** (a persistence layer must not drag in the UI stack, ADR-0009), so
nothing removed it. `-getItem:Content` reported 1 item for Sqlite and 0 for the other four.

**Fix (1.0.1).** `<DefaultItemExcludes>$(DefaultItemExcludes);icon.png</DefaultItemExcludes>` in
`Directory.Build.props`, family-wide. It is the SDK's own supported "keep this out of the default
globs" knob and it is read BEFORE the default items are created — whereas a `<Content Remove/>` in
that same file would run before the item exists and silently do nothing, because
`Directory.Build.props` is imported ahead of `Microsoft.NET.Sdk.DefaultItems.props`. Excluding it
from the default globs does not unpack it: the explicit `Pack="true"` include is untouched. Verified
by packing all five and inspecting the artifacts — icon at root in every package, and no `.pri`
indexes it any more.

**Rules.**
1. *A file that exists purely as package METADATA must be kept out of the default item globs.* The
   same trap is waiting for any future `PackageReadmeFile`/`PackageLicenseFile`/icon that happens to
   carry an extension a platform treats as a resource.
2. *A packaging defect is only visible to a CONSUMER, so packing successfully proves nothing.* Both
   the pack and every in-repo build were green the whole time this was broken. The gallery builds
   from project references, which never exercises the `lib/` layout a package reference resolves.
   Where a defect of this class is suspected, extract the `.nupkg` and look — or pack under a
   throwaway version, restore a real consumer against it, and build.
3. *Never verify a package fix by packing the version you intend to publish and restoring it
   locally.* That writes an unpublished payload into `~/.nuget/packages` under a version number the
   feed will later serve differently — LES-0025 with a longer fuse. Pack under a distinct throwaway
   version (`1.0.1-localverify`), verify, then delete it from the cache.

---

## LES-0037 — Pinning a control's `FlowDirection` to fix text ORDER silently pinned its ALIGNMENT

**Symptom.** In the consuming app, a tree's planting date sat hard against the RIGHT edge of its
column in **English**, while its own caption ("Date") and the value beside it ("Seedling age") sat
left. In **Persian** the same screen looked correct. Reported as "the date is fine in RTL, in LTR it
gets RTL direction or right alignment" — the reporter's instinct was right, and the cause was not in
the app at all.

**Root cause.** `G9CultureDateTimeLabel` set `FlowDirection = LeftToRight` on itself for every absolute
mode, so that `1403/05/24 - 14:30` would not be re-ordered by the bidi algorithm inside a Persian
screen. `FlowDirection` is the paragraph direction, and `Label.HorizontalTextAlignment` resolves
`Start`/`End` against the **view's own** effective flow direction — so on this one view `End` always
meant physically RIGHT, in both languages. The app had (correctly, for what it could observe) written
`HorizontalTextAlignment="End"` to get the leading edge under Persian, and that value then pointed the
wrong way in English. **No value of that property was correct in both languages**, which is the tell:
when a consumer cannot express an intent, the control has taken the choice away from them.

**Why it hid for so long.** Persian is the consuming app's primary language, so every call site was
tuned there and looked right. The defect is invisible until someone runs the app in the other
language — and it was invisible to the library too, because the control did exactly what its own
comment said it did. It also affected `HorizontalOptions` on the same view, for the same reason,
which is why "just don't set the alignment" was not a fix either.

**Fix (1.0.2).** The order is kept in the STRING — the formatted value is wrapped in a Unicode LTR
embedding (`U+202A` … `U+202C`) under an RTL culture — and the control sets no `FlowDirection`. It is
an ordinary `Label` for layout again, so logical alignment mirrors with everything else on screen.
`Relative` mode is excluded (localized words must read in the culture's own direction) and nothing is
wrapped under an LTR culture. Rule generalised as ADR-0017.

**Rules.**
1. *A control may not set its own `FlowDirection` to solve a TEXT-ORDER problem.* Every logical layout
   value the consumer writes resolves against that property, so the control is silently answering a
   question it was not asked. Use bidi control characters, which apply to the run they wrap and to
   nothing else.
2. *When a consumer has to write a PHYSICAL value (`End` meaning "right") to get a LOGICAL result, a
   control is lying about its contract.* Treat that call site as a bug report against the library, not
   as the consumer's own styling choice — there was one in this suite and it had four of them.
3. *Directional behaviour is verified by switching the language with the screen open*, not by reading
   the code. This one is two taps to reproduce and was not reproducible any other way.

---

## RSK-0001 — Rendered on Android, signed in, driven screen by screen — LARGELY CLOSED 2026-08-14

A consuming app was deployed to an Android emulator, signed in against a real server, synced, and
driven through every tab and several overlays. It found six defects (LES-0029, LES-0031, LES-0032).
What was actually seen is tabulated in `09-Progress.md` → "The honest gap".

**Still undrawn: the whole of iOS, dark theme, toast stacking, and a glyph size sweep.** The
~15 hand-authored vector glyphs, the six-layer overlay z-stack, and the rewritten `CollectionView`
list picker are all unobserved. Tracked as the top follow-up item in `09-Progress.md`.

Rendering it once is cheap and will almost certainly find something. Not rendering it and calling the
extraction finished would be the single worst decision available here.

---

## RSK-0002 — The per-control guides still describe the source product

The 26 `.md` guides came across verbatim. They reference the source application's screens and sync flows,
and link guide files that are not part of this repository, none of which exist for a package consumer. The *technical* content is
accurate and valuable — especially §15's platform crash catalog — but the surrounding prose will
confuse the first outside reader. Prose pass tracked in `09-Progress.md`.

## LES-0038 — A setting the builder accepts, stores, and never reads: `UseCanonicalIdCase` in 1.0.1

**Symptom.** A consumer changed the canonical GUID case from upper to lower, deployed, and nothing
changed. The configuration demonstrably ran — a trace right after the call logged
`configured CanonicalIdCase=Lower` — the app started normally, no exception was raised, and ids kept
coming out UPPER. Roughly an hour went into the consumer's DI order, static initialisation order, and
the freeze-on-first-use guard in `SqliteGuidStringNormalizer`, all of which were innocent.

**Root cause.** The published 1.0.1 package does not contain the wiring at all. `G9SqliteBuilder`
accepts `UseCanonicalIdCase` and stores it on the options; `AddG9Sqlite` never calls
`SqliteGuidStringNormalizer.UseCanonicalCase`, so the normaliser stays hard-coded to upper. The call
that closes the loop exists only in source, i.e. in unpublished 1.0.2. The consumer's repository
carries the library sources alongside the app, which is what made this so easy to miss: reading the
source proved the chain was correct end to end, and the source was NOT what was executing.

**What finally isolated it.** Not more reading — a PROBE. Logging the output of normalising a known
literal (`"11111111-2222-3333-4444-AAAABBBBCCCC"` came back upper) proved the normaliser's state
directly, instead of inferring it from the configuration call that had already been confirmed. Then
one command settled which artifact was to blame:

```bash
# Byte-search the RESOLVED dll for the wiring symbol. Do NOT use `strings` here: it is absent from
# Git Bash on Windows, and `strings ... 2>/dev/null | grep -c` then reports 0 for ANY input - a
# check that cannot fail is worse than no check. (That mistake was made while writing this entry.)
python - <<'EOF'
import glob, os
for f in glob.glob(os.path.expanduser(
        "~/.nuget/packages/g9mauicontrols.persistence.sqlite/*/lib/*/G9MAUIControls.Persistence.Sqlite.dll")):
    b = open(f, "rb").read()
    print(f.split("packages/")[1], "UseCanonicalCase=", b.count(b"UseCanonicalCase"))
EOF
```

Measured across all four TFMs: **1.0.1 = 0, 1.0.2 = 1**. Both versions contain `CanonicalIdCase`
(4 hits) - the PROPERTY shipped in 1.0.1, only the call that reads it did not.

**Lessons.**

1. **A configuration API that can silently do nothing is a defect in the API, not just in that
   release.** `UseCanonicalIdCase` had already been a no-op once before (the property was declared with
   a default nothing read). If a setter cannot take effect, it should throw or warn — an accepted value
   that is never read is indistinguishable from a working one until something downstream is wrong.
2. **When source and package can disagree, verify WHICH ONE is executing before debugging either.**
   `obj/project.assets.json` says `"type": "project"` vs `"package"`, and `strings` on the resolved dll
   settles it in one command. This is the library-level twin of the consumer-side trap where a
   fast-deploy APK ships no assemblies — same failure mode, same cure: check the artifact, not the
   build log.
3. **Probe the state, do not infer it from the call.** "The configure delegate ran" and "the setting is
   in effect" are different claims, and only the second one matters.
4. **A verification command that cannot fail is worse than no verification.** The first attempt at the
   check above used `strings`, which is not present in Git Bash on Windows; with `2>/dev/null` in the
   pipe, `grep -c` dutifully reported `0` - the expected answer - for the broken package AND for the
   fixed one. It briefly "confirmed" the conclusion for the wrong reason. Before trusting a check that
   returns the number you expect, run it against a case you KNOW should return the opposite.

## LES-0039 — A NullReferenceException whose only possible null was a list element

**Symptom.** From production, seen once: `NullReferenceException` at
`G9TabView.<PositionPillNow>b__0(TabCell c)`, i.e. inside
`_cells.FirstOrDefault(c => c.LogicalIndex == effective)`, reached from a `Dispatcher.Dispatch`
queued by `RebuildAll`. The user hit it by closing a bottom sheet that hosted the tab view.

**Reading the frame is the whole diagnosis.** `LogicalIndex` is an `int`, so it cannot throw;
`effective` is a captured `int`, so it cannot throw. The only dereference in that predicate is `c`
itself, and `_cells` is only ever fed non-null cells by `BuildCell`. So the list was handing out a
null ELEMENT — which a correctly-used `List<T>` never does.

**Root cause.** `List<T>.Clear()` nulls the backing array slots for a reference element type.
`RebuildAll` calls `_cells.Clear()` and then re-adds, and it runs on whatever thread raised its
trigger — `OnItemsCollectionChanged` is raised on the thread that mutated the collection. A reader
enumerating on the UI thread has already captured the old `_size`, so it indexes a slot that the
other thread has just nulled. It is a torn read, not a null bug.

**Lessons.**

1. **"Which of these can actually be null?" is a faster question than "where is the null?"** Narrowing
   the predicate to its single reference dereference turned an unreproducible one-off into a
   deterministic explanation in one step, with no repro attempt.
2. **A cross-thread `List<T>` does not always fail loudly.** The textbook symptom is
   `InvalidOperationException("Collection was modified")`, which is why an NRE reads as "impossible"
   here. `Clear()` racing an in-flight enumerator produces a null element instead, and nothing in the
   type system hints at it.
3. **Put the thread guard where the state is mutated, not where it is read.** The fix is the
   `IsDispatchRequired` check at the top of `RebuildAll`; the null-tolerant `FindCell` helper is
   defence in depth for late dispatches. Guarding only the reader would have moved the crash rather
   than removed it.
4. **When the same lookup appears four times, make it one helper before fixing it.** The first patch
   here landed on the wrong method — `AnimatePillToSelected` had a byte-identical preamble — and
   "fixed" a site that was not the reported one while leaving `PositionPillNow` untouched. Routing
   all four call sites through `FindCell` removed both the bug and the class of mistake.

## LES-0043 — A value with two writers has no owner

**Symptom.** `G9SelectionItem.IconTintColor` was ignored by the picker LIST. The fix — make
`G9SelectionSheet.CreateRow` honour it — was correct, shipped, verified in the built assembly, and
changed **nothing** on screen.

**Root cause.** A second writer. Rows are built once by `CreateRow` and then re-styled in place by
`UpdateRowVisuals` (which exists to avoid a rebuild "blink" on every selection toggle), and that
method contained its own copy of the colour rule:

```csharp
mauiIcon.Color = selected ? palette.Primary : palette.TextSecondary;
```

It runs after the build and on every toggle, so whatever it writes is what the user sees. The first
fix was invisible because it was being overwritten microseconds later.

**Rule.** When a rendered property can be set in more than one place, extract the rule into ONE
resolver both call — do not fix the writer you happened to find. The cheap way to find the others
before shipping: grep for the PROPERTY being assigned (`\.Color = `), not for the method you were
told about. Two hits is the bug.

**Diagnostic lesson, worth more than the fix.** When a change that is provably in the binary has no
visible effect, the hypothesis "my change did not deploy" is seductive and was wrong twice here. Cost
a whole round of build/deploy archaeology. Check for a SECOND writer before re-examining the
toolchain: verify the deployed artifact once, and after that treat "compiled but no effect" as
evidence about the CODE, not about the build.

---

## LES-0042 — A direction ternary on alignment is always half wrong

**Symptom.** In a Persian (RTL) app, the combobox's selected value sat against the LEFT edge of the
trigger while its own icon sat against the right — the whole width of the field between a glyph and
the label it introduces.

**Root cause.** One line:

```csharp
HorizontalTextAlignment = flow == FlowDirection.RightToLeft ? TextAlignment.End : TextAlignment.Start,
FlowDirection = flow,
```

`Start` and `End` are already direction-relative, and the label was being given the flow direction on
the very next line. So the ternary did not select the reading edge — it INVERTED it: under RTL,
`End` is the physical left. Under LTR the ternary picked `Start`, which was correct, so the bug was
invisible in half the tests anyone ran.

**Both triggers had it** — `G9ComboBox` and `G9Picker` — which is the tell that it is a habit rather
than a slip: two authors reached for the same ternary because "RTL means align right" sounds true.

**Rule.** Never branch on flow direction to choose `Start`/`End`. If a view carries a
`FlowDirection`, logical alignment is already mirrored for you; a ternary can only undo that. The one
legitimate reason to touch direction is to fix character ORDER inside a string, and that is done by
wrapping the text in a Unicode embedding — never by pinning the view's direction (LES-0037, the
`G9CultureDateTimeLabel` fix, which is the same lesson from the other side).

**How to spot it:** grep for `FlowDirection.RightToLeft ?` and `IsRtl ?` near an alignment property.
Every hit is either this bug or wants a comment saying why it is not.

---

## LES-0041 — A gesture limit derived from the CURRENT state is not a limit

**Symptom.** A bottom sheet opened with `States = [Peek, Medium]` could be dragged to the top of the
window, and on release dropped back to ~75% of the screen leaving a band of empty sheet background
under its last row. A downward drag from that step closed the sheet outright instead of returning it
to the peek.

**Root cause.** `ShouldRestrictMovement` computed its bound from `State`:

```csharp
var endPosition = State switch { HalfExpanded => Height * HalfExpandedRatio, Collapsed => CollapsedHeight, … };
var halfRestricted = State == HalfExpanded && AllowedState == HalfExpanded && updatedHeight > endPosition;
```

While the sheet sat at `Collapsed` neither `halfRestricted` nor `fullRestricted` could be true, so the
only remaining bound was the window itself (`FullExpandedRatio` defaults to 1). **A limit that is only
armed once you are already at the limit is not a limit.** The same shape explains the close: the
control asked `AllowedState != All` to decide "is this a fixed sheet?", and `AllowedState` only
encodes which LARGE detent exists — a two-detent peek→medium sheet answers exactly like a fixed
single-state one.

**Rule.** Derive gesture bounds from the sheet's DECLARED set of resting positions, never from the one
it currently occupies; and when an enum cannot express a distinction the behaviour depends on, add the
bit rather than inferring it (`AllowCollapsedState`). The tell that inference was wrong: the helper
knew the answer all along — it has `options.States` — and was throwing it away at the boundary.

**Two things this uncovered, both worth repeating.**

- *Documented is not implemented.* The guide had stated for a year that the helper applies
  `IsCancelable` and `DragCloseThreshold` to the control in `ApplyOptions`. It never did. It was
  harmless only by luck (every sheet that opts out of cancelling also sets `IsDraggable = false`).
  When a guide asserts a wiring, grep for the assignment before building on it.
- *Measure the finger, snap the sheet.* Drag-to-close had been judged on how far the SHEET moved,
  which is zero for a sheet clamped at its detent — the exact case the gesture is for. The two
  questions need two accumulators: finger travel decides the close, sheet travel decides the snap.

---

## LES-0040 — Extracting a control library substituted the nearest icon, and changed what three controls PROMISED

**Reported as** "the barcode scanner button became a search button". A consumer noticed that the
`ثبت نمونهٔ جدید` sheet's scanner field, which opens the camera, now showed a magnifier.

**Root cause.** Extraction replaced every host `MaterialIcons.X` reference with this package's
built-in vector set. That set is deliberately small (23 glyphs), so several host icons had no
equivalent — and each one was quietly resolved to the nearest available glyph rather than being
recorded as missing. `MaterialIcons.QrCodeScanner` became `G9Glyphs.Search`; the intro carousel's
`MaterialIcons.Language` became a hard-coded `G9Glyph.Info`; the cancelled-sync toast's
`MaterialIcons.CloudOff` became a hard-coded `G9Glyph.Info`. Two more collapsed onto one slot:
`Today` and `DateRange` both resolved to `G9Glyphs.Calendar`.

**Why it survived review.** Every substitution compiles, renders, and looks deliberate. There is no
diagnostic for "this glyph is a stand-in", and the two worst cases bypassed `G9Glyphs` entirely by
assigning `G9Glyph.X` directly, so no consumer could have themed around them even after noticing.

**Lessons.**

1. **An icon is part of a control's contract, not its styling.** A magnifier on a field that opens a
   camera is a wrong promise, and it is exactly as much a defect as a wrong string would be. Audit
   icons when extracting, and treat "no equivalent exists" as a gap to fill, not a value to
   approximate.
2. **A control must never hard-code `G9Glyph`.** Going through `G9Glyphs` is what makes an icon
   themable; `Icon = G9Glyph.Info` is unoverridable by construction. Grepping for `= G9Glyph.` in
   control code (excluding the gallery) is a cheap standing check and is what found the other two.
3. **One slot per affordance, even when the default drawing is shared.** `CalendarToday` and
   `DateRange` both default to the `Calendar` drawing, but they are separate slots so a host with a
   richer font can tell "pick a date", "jump to today" and "choose a range" apart. Collapsing them
   was a silent loss of expressiveness that only the host could see.
4. **Verify each mapping against the original, do not infer it from the name.** `CloudOff` looked
   like an offline-notice icon; in the original it was the CANCELLED-sync toast. The restoration was
   only correct because the pre-extraction source was read rather than reasoned about.
