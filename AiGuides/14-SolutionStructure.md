# 14 - Solution Structure

# G9MAUIControls
## Repository Layout

---

```text
G9MAUIControls/
├── G9MAUIControls.slnx           # solution (new-format)
├── Directory.Build.props         # STOPS the walk to the repo root — see ADR-0005
├── Directory.Packages.props      # central package versions for THIS subtree only
├── AiGuides/                     # this guide set
│
├── G9MAUIControls/               # ── the redistributable package ──
│   ├── G9MAUIControls.csproj     # 4 TFMs, NuGet metadata, documented suppressions
│   ├── README.md                 # ships as the NuGet readme
│   │
│   ├── Icons/                    # the icon system (ADR-0002, ADR-0003)
│   │   ├── G9IconSource.cs       #   one value type for any icon, + XAML TypeConverter
│   │   ├── G9IconFonts.cs        #   font registry + resolve-by-name
│   │   ├── G9Glyph.cs            #   the built-in glyph set (enum)
│   │   ├── G9GlyphDrawable.cs    #   those glyphs as PathF geometry on a 24x24 grid
│   │   ├── G9Glyphs.cs           #   overridable defaults for the controls' own chrome
│   │   └── G9IconView.cs         #   renders either kind; + G9IconFactory, G9ImageFactory
│   │
│   ├── Localization/             # the culture + string seams
│   │   ├── G9Culture.cs          #   IsRtl, CurrentCulture, fonts, CultureChanged
│   │   ├── G9Strings.cs          #   ~50 strings, English defaults, pluggable
│   │   └── IG9SpeechToText.cs    #   optional voice provider (+ G9Speech)
│   │
│   ├── Storage/G9Preferences.cs  # theme + learned sheet heights; pluggable store
│   │
│   ├── Theming/                  # G9Palette (~110 tokens), G9Theme, G9LayoutMetrics,
│   │                             # {theme:G9Color} markup extension, light/dark dictionaries
│   │
│   ├── Hosting/                  # ── NOT optional for the overlays (ADR-0004) ──
│   │   ├── G9PageTemplate.xaml   #   the six-layer z-stack; sibling order IS z-order
│   │   ├── G9PageBase.cs         #   safe areas, lifecycle, template child capture
│   │   ├── G9ContentViewBase.cs  #   activation lifecycle for tab-hosted content
│   │   ├── G9AndroidHost.cs      #   the activity hook (touch stream, window changes)
│   │   ├── G9PageLoadingOverlay.cs
│   │   └── DeferredContentView.cs, LoadableSheetContentView.cs, ProcessingSheetContentView.cs
│   │
│   ├── Controls/                 # 25 controls, one folder each, each with its own .md
│   │   ├── G9Controls.md         #   THE architecture guide. §0 = no shadows. §15 = crash catalog.
│   │   ├── Shared/               #   G9ControlBase, G9OutlinedFieldBase, drawables, metrics,
│   │   │                         #   colours, G9SelectionSheet, G9PressFeedbackBehavior
│   │   └── G9Button/ … G9TabView/
│   │
│   ├── BottomSheet/              # G9BottomSheetHelper + options/handles/list picker
│   │   └── G9SheetView/          #   the control + 4 per-platform gesture handlers
│   │
│   ├── Popup/                    # G9PopupHelper + types/buttons/input forms
│   │   └── G9PopupView/          #   the control + open options
│   │
│   ├── Toast/                    # G9ToastHelper, options, IG9BottomAnchoredOverlay
│   ├── TabBar/                   # G9TabBar + drawables + Skia FAB-notch silhouette
│   ├── EdgePanel/                # G9EdgePanel + items/header/metrics
│   ├── Helpers/                  # G9SafeCommand, G9ColorHelper, G9ModalHostRegistry, …
│   └── Platforms/Android/Resources/   # shimmer AVD drawables (g9_shimmer_*)
│
├── G9MauiLibrary.props           # the shared MAUI-library shape; imported EXPLICITLY (LES-0007)
├── assets/icon.png               # package icon (placeholder — needs a real logo before publish)
│
├── G9MAUIControls.Barcode/       # ── satellite: camera scanning ──
│   └── Controls/                 #   G9BarcodeTextEntry + its two enums (moved here, LES-0014)
├── G9MAUIControls.IntroCarousel/ # ── satellite: onboarding carousel (video ⇒ MediaElement) ──
├── G9MAUIControls.ProgressOverlay/  # ── satellite: the shared progress overlay ──
│   ├── G9ProgressContracts.cs    #   report / state / position / queued-count
│   ├── G9ProgressOverlayHelper.cs  # ShowAsync → lease, Report, ReportQueued, CancelRequested
│   └── Views/G9ProgressOverlayView.xaml
├── G9MAUIControls.Persistence.Sqlite/  # ── satellite: SQLite. NO dependency on the core ──
│   ├── Abstractions/             #   clock, user, locator, interceptors, descriptors, migrations
│   ├── Configuration/            #   G9SqliteBuilder + AddG9Sqlite
│   └── Sqlite/                   #   the ported repository, query builders, DTO cache
│
└── G9Controls.Gallery/           # ── the VERIFICATION app; not a sample ──
    ├── Pages/                    #   Glyphs, Inputs, Actions, Overlays, Navigation, Satellites
    └── Platforms/Android/MainActivity.cs   # the reference G9AndroidHost wiring
```

**Why the gallery is in the subtree at all.** A control library nobody has consumed is a control library
whose API has never been tested. The gallery is a *breadth* check — every control on screen, both themes,
both directions — and it is the trim/AOT consumer of ADR-0011 and the package-reference consumer
(`-p:UseG9Packages=true`) that catches packaging defects.

A second consumer, a BLE client app, was a *depth* check — state, navigation, a transport and persistence
composed into something a person uses. Between them the two found six defects no library-side build could
(LES-0012 … LES-0015). **It moved out of this subtree on 2026-08-14** because it is a product app, not part
of the library deliverable; this repository is now purely the packages and the thing that verifies them. The lesson survives the move: **build a consumer as early as the API allows, and a
second, different consumer after that.**

---

# Namespace map

Folder path and namespace agree, with one deliberate exception noted below.

| Folder | Namespace |
|---|---|
| `Controls/**` (including `Shared/`) | `G9MAUIControls.Controls` |
| `Icons/` | `G9MAUIControls.Icons` |
| `Localization/` | `G9MAUIControls.Localization` |
| `Storage/` | `G9MAUIControls.Storage` |
| `Theming/` | `G9MAUIControls.Theming` |
| `Hosting/` | `G9MAUIControls.Hosting` |
| `BottomSheet/` **and** `BottomSheet/G9SheetView/` | `G9MAUIControls.BottomSheet` |
| `Popup/` **and** `Popup/G9PopupView/` | `G9MAUIControls.Popup` |
| `Toast/` | `G9MAUIControls.Toast` |
| `TabBar/` | `G9MAUIControls.TabBar` |
| `EdgePanel/` | `G9MAUIControls.EdgePanel` |
| `Helpers/` | `G9MAUIControls.Helpers` |

**The exception, and why it is intentional:** the control sub-folders (`G9SheetView/`,
`G9PopupView/`, and every `G9Xxx/` under `Controls/`) do NOT add a namespace segment. The folder
expresses *ownership* — "everything about this control lives here" — while the namespace expresses
the *stable public API surface*. Decoupling the two means a control can be split across more files,
or moved, without breaking a consumer's `using`.

---

# Naming conventions

| Kind | Pattern | Example |
|---|---|---|
| Control | `G9<Name>` | `G9ComboBox` |
| Control base | `G9<Family>Base` | `G9OutlinedFieldBase` |
| Drawable | `G9<Name>Drawable` | `G9SwitchDrawable` |
| Static entry point | `G9<Area>Helper` | `G9BottomSheetHelper` |
| Per-call options | `G9<Area>Options` | `G9ToastOptions` |
| Consumer hook (static) | `G9<Thing>` with settable members | `G9Glyphs`, `G9Speech` |
| Consumer hook (interface) | `IG9<Thing>` | `IG9SpeechToText` |
| Per-control guide | `<ControlName>.md`, beside the `.cs` | `G9ComboBox.md` |

---

# Build

```pwsh
# one framework, fast iteration
dotnet build G9MAUIControls/G9MAUIControls.csproj -f net10.0-windows10.0.19041.0

# every framework — required before calling a change done
dotnet build G9MAUIControls/G9MAUIControls.csproj

# the package (Release also runs XAML compilation, which Debug skips —
# several XAML errors are only visible here)
dotnet pack G9MAUIControls/G9MAUIControls.csproj -c Release
```

**Debug does not compile XAML.** `MauiStrictXamlCompilation` only bites in Release, so an unresolved
markup extension or namespace in a `.xaml` file passes Debug and fails `pack`. Run `pack` before
believing a XAML change.
