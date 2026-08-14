# G9MAUIControls

A dependency-light .NET MAUI control suite, built entirely on public MAUI primitives.

Twenty-five input and feedback controls sharing one outlined-field architecture, a bottom sheet with
real per-platform edge-handoff gestures, a popup system, toasts, an animated tab bar, an edge drawer,
and a ~110-token light/dark theme engine. Android, iOS, Mac Catalyst and Windows from one codebase,
with no `#if` in the control layer.

```
dotnet add package G9MAUIControls
```

---

## What makes it different

**Bring your own icon font — or none at all.** Every icon slot takes a `G9IconSource`, which any
icon-font enum converts to implicitly (font family = the enum's type name, glyph = its
`[Description]`, the convention icon-font generators already emit). The suite's own chrome glyphs are
**vector paths**, not a bundled font, so it looks complete out of the box with zero icon
configuration and zero chance of a tofu box.

**No shadows, anywhere — and that is a measured decision, not a style.** On Android a MAUI `Shadow`
on a view whose platform background isn't a shadow-capable `BorderDrawable` falls back to
`drawShadowViaDispatchDraw`: an `ARGB_8888` bitmap the size of the view, the whole subtree rasterized
into it, then software-blurred through a `BlurMaskFilter` **on the UI thread, every draw pass**. That
produced a hard ANR on real hardware. Elevation here is `Stroke` + surface-tone steps instead. Full
post-mortem in `Controls/G9Controls.md` §0.

**Trim and AOT safe, and verified.** No `Reflection.Emit`, no private-field reads, no runtime code
generation. `IsTrimmable` and `IsAotCompatible` are declared, and a consuming app published with
`-p:AndroidLinkMode=Full -p:PublishTrimmed=true`, with this assembly rooted, completes with **zero** IL
warnings. That check found four real defects the first time it ran, all fixed — a library build cannot see
its dependencies' implementations, so it is the only check that counts.

iOS NativeAOT is not yet verified; the claim above is trimming on Android.

**RTL is real, not a `FlowDirection` you set and hope.** Outlined fields physically swap their icon
columns; every drawable that mirrors does so itself with its canvas pinned to LTR (a canvas either
mirrors itself or the drawable mirrors — never both, or ticks come out backwards).

**Three runtime dependencies**, and each one earns its place: `CommunityToolkit.Mvvm`
(`ObservableObject` for the palette, `IRelayCommand` for the safe buttons), `Microsoft.Maui.Controls`,
and `SkiaSharp.Views.Maui.Controls` — used *only* for the tab bar's concave FAB-notch silhouette,
which needs a genuinely blurred path on the render thread.

---

## Quick start

```csharp
// MauiProgram.cs
public static MauiApp CreateMauiApp()
{
    var builder = MauiApp.CreateBuilder();
    builder.UseMauiApp<App>()
           .UseG9SheetView();      // registers the bottom-sheet handler

    // Optional: point the culture facade at your app's language state.
    G9Culture.Configure(
        currentCulture: () => LocalizationManager.CurrentCulture,
        isRtl:          () => LocalizationManager.CurrentCulture.TextInfo.IsRightToLeft);
    G9Culture.LtrFontFamily = "OpenSansRegular";
    G9Culture.RtlFontFamily = "Yekan";

    // Optional: translate the ~50 strings the controls display.
    G9Strings.UseResources(AppDictionary.ResourceManager, keyPrefix: "G9");

    // Optional: use your own icon font everywhere.
    G9IconFonts.Register<MyIcons>(isDefault: true);
    G9Glyphs.Chevron = MyIcons.ExpandMore;

    return builder.Build();
}
```

Then derive pages from `G9PageBase` (it applies the control template that hosts the overlay layers)
and use the controls:

```xml
<g9:G9TextEntry Label="Device name" LeadingIcon="Search" />
<g9:G9ComboBox  Label="Relay channel" ItemsSource="{Binding Channels}" />
<g9:G9Switch    IsOn="{Binding IsEnabled}" />
<g9:G9Button    Text="Connect" Variant="Primary" LeadingIcon="{x:Static my:MyIcons.Bluetooth}" />
```

```csharp
await G9ToastHelper.ShowToastAsync("Saved", G9ToastType.Success);
var ok = await G9PopupHelper.ShowConfirmAsync("Turn off all relays?", type: G9PopupType.Warning);
G9BottomSheetHelper.ShowG9BottomSheet(content, G9BottomSheetOptions.FitToContentOptions());
```

---

## What's in it

| Area | Contents |
|---|---|
| **Inputs** | `G9TextEntry` `G9Editor` `G9SearchEntry` `G9PinEntry` `G9Picker` `G9ComboBox` `G9DateTimePicker` `G9TimeSpanPicker` `G9RangeSlider` `G9Switch` `G9ChipGroup` |
| **Actions** | `G9Button` `G9IconButton` `G9SafeButton` `G9SafeIconButton` `G9PlusButton` `G9NavCard` `G9SwipeView` |
| **Structure** | `G9TabView` `G9Expander` `G9CascadePanel` `G9Separator` `G9TitleWithLine` |
| **Feedback** | `G9ProgressBar` `G9Shimmer` `G9ActivityIndicator` |
| **Overlays** | `G9BottomSheetHelper` (+ stacking, morph, fit-to-content, list picker) · `G9PopupHelper` (types, input forms, confirm, non-modal draggable) · `G9ToastHelper` (typed toasts, loaders, progress) |
| **Navigation** | `G9TabBar` (animated bottom bar + FAB notch) · `G9EdgePanel` (edge drawer) |
| **Theming** | `G9Theme` `G9Palette` (~110 tokens) `G9LayoutMetrics` · `{theme:G9Color …}` markup extension |
| **Hosting** | `G9PageBase` `G9ContentViewBase` `G9PageTemplate` (the six-layer overlay z-stack) |

Every control folder ships its own `.md` guide next to the `.cs`. Start with
`Controls/G9Controls.md` — the architecture guide, including the platform crash-and-pitfall catalog
(§15) that documents every WinUI stowed exception, Android gesture-interception and iOS focus quirk
this suite has already paid for.

---

## Optional integration hooks

Nothing below is required; each one lights up a feature that a library genuinely cannot do alone.

| Hook | Enables |
|---|---|
| `G9Culture.Configure` / `NotifyChanged` | live language + RTL switching, correct typeface per script |
| `G9Strings.UseResources` / `UseProvider` | translating the controls' own strings |
| `G9IconFonts.Register<T>` / `G9Glyphs.*` | your icon font throughout the suite |
| `G9ImageFactory.Factory` | routing bitmap icons through a caching image control |
| `G9Speech.Provider` | the search entry's microphone |
| `G9AndroidHost.*` | tap-outside-to-dismiss-keyboard, safe-area re-measure on rotation |
| `G9Preferences.Store` | redirecting persisted theme + learned sheet heights to your own store |
| `G9SafeCommand.DiagnosticsHandler` | a "More details" button on error popups |
| `G9SafeCommand.DiagnosticsAvailable` | gating that button on a runtime setting (a developer mode) |
| `G9BottomSheetHeightSeeds.Seed` | first-open heights for fit-to-content sheets |

Building a control of your own beside the suite? These are public for exactly that:
`G9Metrics`, `G9Colors`, `G9Visuals` and `G9TabBarMetrics` for its measurements;
`G9ColorExtension.ResolveColor` + `G9PaletteSubscriptions` to paint from the palette and repaint on a
theme flip; and `G9OverlayHosts.TryGetCurrent(out var host)` for the current page's layers —
`host.OverlayLayer` (sheet level), `host.ToastLayer` (above sheets and popups), `host.DevLayer`
(topmost), and `host.Page` for its safe-area insets.

---

## License

MIT.
