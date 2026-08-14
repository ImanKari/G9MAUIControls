# 01 - AI Guide

# G9MAUIControls
## AI Entry Point

---

# Read this first

This guide set governs **this repository** only: the `G9MAUIControls` package family and
`G9Controls.Gallery`, the app that exists to verify it.

**This tree is self-contained and is the unit you copy.** It carries its own `Directory.Build.props`,
`Directory.Packages.props`, `nuget.config` and `global.json` precisely so it can be vendored into another
repository and still build. Nothing outside it is required to build, pack or verify the packages.
See `README.md` for the layout.

If this guide set conflicts with another set in a host repository about anything in this tree,
**this set wins**.

---

# What this is

**`G9MAUIControls`** — a redistributable .NET MAUI control suite, extracted from an unrelated product
(authored by the owner) and decoupled from it. Published as a NuGet package family. Twenty-five
controls, a bottom sheet, popups, toasts, a tab bar, an edge drawer, a theme engine, and a SQLite
persistence layer that depends on none of the above.

**`G9Controls.Gallery`** — the verification app. Six pages, each written around what actually breaks
rather than what is easy to show. It is not a sample; it is the test surface, and it is the only
consumer in this subtree.

---

# Mandatory reading order

1. **01-AIGuide** (this file)
2. **09-Progress** — where things actually stand, including what has NOT been run
3. **10-Decisions** — the fifteen ADRs; several are non-obvious and expensive to re-litigate
4. `G9MAUIControls/Controls/G9Controls.md` — the control architecture, and §0 / §15 in particular

Then load only what the task needs.

| Task | Also read |
|---|---|
| Any control change | `Controls/G9Controls.md` + that control's own `.md` |
| Anything with a shadow, blur or elevation | `Controls/G9Controls.md` **§0** — non-negotiable |
| Platform handler, gesture, or native interop | `Controls/G9Controls.md` **§15** (crash catalog) |
| Bottom sheet | `BottomSheet/G9BottomSheetGuide.md` |
| Popup | `Popup/G9PopupGuide.md` |
| Toast | `Toast/G9ToastGuide.md` |
| Icons | `Icons/` sources — `G9IconSource`, `G9IconFonts`, `G9Glyphs` |
| SQLite persistence | `04-SqlitePersistence.md` |
| Packaging / versioning | `03-PackageArchitecture.md`, `Directory.Build.props`, `Directory.Packages.props` |
| Debugging on a device | `06-AndroidDebugLoop.md` |
| Moving a consuming app onto the packages | see each package README, and `10-Decisions.md` for the API shape |

---

# Two rules that override instinct

**1. Never add a MAUI `Shadow`.** Not on a control, a `Border`, a `Grid`, a `Label`, a sheet, a
popup, a toast. Not in XAML, not in C#. On Android it can fall back to a software bitmap blur on the
UI thread every draw pass, which produced a measured ANR on real hardware. The full mechanism and
post-mortem are in `Controls/G9Controls.md` §0, along with the four sanctioned ways to express
elevation instead. There is no approved exception.

**2. A green build says nothing about what it built.** Four TFMs × two configurations × `dotnet pack`
found none of the last six real defects. What found them: a trimmed publish, a package-reference
build, and running the app. Before calling anything done, check *which artifact* you exercised — for
package-mode runs that means reading `obj/project.assets.json`, not the exit code (LES-0025).

---

# Workflow

Understand → Verify (read the control guide, not the summary) → Implement →
Build all four TFMs → **Publish the gallery trimmed** → **Run it and look at it** → Document → Stop.

Two steps in that chain are the ones that actually find things, and both are easy to skip:

- **Publish the gallery with `PublishTrimmed`.** `dotnet build` on four TFMs, in both configurations,
  plus `dotnet pack`, found none of the last six real defects. A trimmed publish of
  `G9Controls.Gallery` found four (ADR-0011). Run:

  ```pwsh
  dotnet publish G9Controls.Gallery/G9Controls.Gallery.csproj -c Release `
      -f net10.0-android -p:AndroidLinkMode=Full -p:PublishTrimmed=true
  ```

- **Run it and look at it.** Still the project's largest gap: the gallery builds, publishes, and
  survives a full trim, and **no pixel has been inspected by a human** — see `09-Progress.md` →
  "The honest gap" for the checklist of what to look at and why each item is on it.

A change that only compiles is not done.

---

# Stop conditions

Stop and explain if: a build fails on any TFM; a control renders wrong and the cause is not obvious;
a platform handler needs native code you cannot verify; a change would add a runtime dependency to a
published package; requirements are unclear.

---

# Documentation update rules

- Every completed task updates **09-Progress**.
- A decision with a trade-off gets an **ADR in 10-Decisions** — including the alternatives rejected
  and why. An ADR that only records the choice is half an ADR.
- A control change updates **both** `Controls/G9Controls.md` (if the change is architectural) **and**
  that control's own `.md`. A code change that leaves either stale is incomplete.
- A package's public surface changes → its **`README.md`** changes, because that is its nuget.org
  page. Verify every identifier in it against the source; four of the five READMEs once carried
  samples that did not compile, because they were written from the design rather than read out of the
  code (LES-0024).
- A mistake worth not repeating goes in **11-EngineeringLog** with the symptom, the root cause and
  the rule. The test: *would a competent engineer, doing this again in three weeks with no memory of
  today, plausibly repeat it?*

---

# Final rule

A successful task improves the source, the documentation, and the repository's health together.
For this subtree there is one addition: **it must not add a dependency to a published package**
without an ADR saying why the alternative was worse.
