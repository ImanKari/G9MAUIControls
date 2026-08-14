# 02 - Project

# G9MAUIControls
## Project Definition & Requirements

---

# Overview

One deliverable: the package family. `G9Controls.Gallery` sits beside it as the app that
verifies it — not a second product, and not a sample.

## `G9MAUIControls` — a redistributable MAUI control suite

A NuGet package containing twenty-five input and feedback controls, a bottom sheet, a popup system,
toasts, an animated tab bar, an edge drawer, and a light/dark theme engine. Every control is
hand-rolled on public MAUI primitives (`Border`, `Grid`, `Label`, `Entry`, `Editor`, `GraphicsView`)
with no per-platform XAML and no vendor control library.

The code is not new — it was authored by the owner inside another product and proved there over a long
period, including a number of platform failures paid for the hard way and documented in
`Controls/G9Controls.md` §15. **The work here is extraction and decoupling, not authoring.** That
distinction sets the priorities: preserve behaviour exactly, and replace every host-app dependency
with something a stranger can wire up in five lines.

# Requirements — the library

## Must

- **Four platforms from one codebase**: `net10.0-android`, `-ios`, `-maccatalyst`,
  `-windows10.0.19041.0`. No `#if` in the control layer; platform code confined to handlers.
- **Dependency-light.** Every runtime dependency must be justified in an ADR. Currently three, and
  one of those (`SkiaSharp`) serves exactly one feature.
- **Bring-your-own icon font.** No icon package may be required, and the suite must look complete with
  none configured. See ADR-0002 / ADR-0003.
- **Trim and AOT safe in our OWN code, always. Claimed only where the dependency closure allows it.**
  No `Reflection.Emit`, no private-field reads, no runtime codegen, anywhere. `IsAotCompatible` is then
  declared only by packages whose dependencies are also annotated — the core and `.ProgressOverlay`. The
  other three set `EnableTrimAnalyzer` alone and make no promise, because a consumer publishes one app,
  not five libraries, and a claim that a dependency breaks is worse than no claim (ADR-0011 has the
  per-package table). A consumer's Release Android publish with `AndroidLinkMode=Full` must work in every
  case — that is verified, and it is the requirement that actually matters.
- **No MAUI `Shadow`, ever.** A hard rule with a measured ANR behind it —
  `Controls/G9Controls.md` §0.
- **RTL correct, not merely flipped.** Outlined fields physically swap icon columns; every
  self-mirroring drawable pins its canvas to LTR so it cannot double-flip.
- **Warnings are errors**, and every suppression carries a written reason for why the rule is wrong
  here.
- **Optional integration.** Culture, strings, icons, storage, images, voice, Android host hooks and
  diagnostics are all opt-in. The one exception is the page host — the overlay layers require
  `G9PageBase` + `G9PageTemplate` (ADR-0004).

## Must not

- Contain anything belonging to the source product: brand fonts, resource keys, domain types, screen
  names, sync contracts, or its developer/QA tooling.
- Require a base `Application`, `Activity`, or `AppDelegate`.
- Reach for a third-party package to solve something a few dozen lines of MAUI primitives can.

## Explicitly out of scope

- Charts, maps, media playback, camera, barcode scanning. Each needs a heavy native dependency and
  belongs in a separate companion package if ever wanted (ADR-0006 covers the media case).
- A visual designer, a Blazor variant, or a WPF/Uno port.

---

# Definition of success

**Library**

✓ Four TFMs build clean, warnings-as-errors, every suppression justified — **done**

✓ `dotnet pack` produces the package — **done**

✓ Every control rendered and eyeballed in light + dark, LTR + RTL — **not done, top priority**

✓ Zero references to the source product in code or public API — **done in code; the per-control
guides' prose still needs a pass**

✓ Consumed by a real app without reaching back into it for anything — **done twice**: by
`G9Controls.Gallery` here, and by a BLE client app that has since moved out of this subtree. Between
them those two consumers found six defects, none of which were findable from the library side

**Verification app**

✓ Every control on screen, both themes, both directions — **not done, top priority**

✓ Both consumption modes exercised: project references (the API-boundary test) and package
references (the packaging test), via `-p:UseG9Packages=true`
