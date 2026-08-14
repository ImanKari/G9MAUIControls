# G9Button

`G9Button` is the primary push-button of the design system, following the
G9DesignSystem MAUI implementation spec. For UI actions, consumers use
`G9SafeButton` (text) or `G9SafeIconButton` (icon-only) — both inherit `G9Button` /
`G9IconButton` and add the safe-execution layer (throttle / double-tap guard, busy
spinner, error popup, `Command -> SafeCommand` routing). See `G9IconButton.md` for the
icon-only variant.

## When to use

- Text or text+icon action -> `G9SafeButton` (wraps `G9Button`).
- Icon-only action -> `G9SafeIconButton` (wraps `G9IconButton`).
- Shape and size come from `Variant` / `Size`; do not add per-call corner-radius / padding /
  font overrides. The only color escape hatches are `BaseBackgroundColor` and `TextColor`.

## Variants

| Variant | Visual | Use for |
|---|---|---|
| `Primary` | Solid green gradient (flat — no shadow) | Primary CTA |
| `Tonal` | Soft primary-tinted background + 1.5px primary-tinted border | Secondary action with brand presence |
| `Default` | Neutral surface | Generic action |
| `Secondary` | Secondary container | Secondary action |
| `Info` | Solid blue gradient | Informational action |
| `Success` | Solid green gradient | Confirmation |
| `Warning` | Solid orange gradient | Caution |
| `Error` | Solid red gradient | Destructive action |
| `ErrorTonal` | `ErrorContainer` fill + `Error` glyph/text, flat (no gradient) | **Soft** destructive / dismissive control that lives permanently on screen next to a real primary action, where a solid `Error` would out-shout it — e.g. the map multi-selection card's close (X). The foreground is `Error`, deliberately NOT `OnErrorContainer` (near-black maroon, reads as plain dark text on the soft fill). |
| `Surface` | Surface variant | Quiet action |
| `Outline` | Transparent + outline | Tertiary action |
| `Text` | Transparent + label only (with hairline border) | Inline link-like action |

## Sizes

| Size | Padding | Font size | Min height |
|---|---|---|---|
| `Small` | 12, 8 | 12 | 36 |
| `Medium` (default) | 16, 12 | 14 | 44 |
| `Large` | 20, 14 | 15 | 48 |
| `Hero` | 16 | 16 | 52 (full-width) |

The `Min height` above is only the FALLBACK applied when the consumer leaves `HeightRequest`
unset (MAUI default `-1`). An explicit `HeightRequest`/`MinimumHeightRequest` on the button wins
and is left alone by every later `OnApplyVisuals` pass (2026-07 fix — it used to be silently
reset to the `Size` preset on every pass, which clipped a Bold 16sp label's descenders when a
consumer asked for a taller-than-Medium button without also bumping `Size`). Prefer setting
`Size="Large"`/`Size="Hero"` over a raw `HeightRequest` where one of the presets fits; use an
explicit `HeightRequest` for one-off cases (e.g. a prominent CTA that needs to be taller than
`Large` but isn't full-width like `Hero`).

## Bindable Properties

| Property | Type | Default | Description |
|---|---|---|---|
| `Text` | `string?` | `null` | Button label. Optional — icon-only buttons supported. |
| `Variant` | `G9ButtonVariant` | `Primary` | Color scheme. |
| `Size` | `G9ControlSize` | `Medium` | Padding / font / height preset. |
| `LeadingEmoji` | `string?` | `null` | Emoji rendered on the start side. |
| `LeadingMaterialIcon` | `MaterialIcons?` | `null` | Material icon on the start side. |
| `LeadingImagePath` | `string?` | `null` | Image file/uri path on the start side. |
| `LeadingImageSource` | `ImageSource?` | `null` | Direct ImageSource on the start side. |
| `TrailingEmoji` | `string?` | `null` | Emoji on the end side. |
| `TrailingMaterialIcon` | `MaterialIcons?` | `null` | Material icon on the end side. |
| `TrailingImagePath` | `string?` | `null` | Image file/uri path on the end side. |
| `TrailingImageSource` | `ImageSource?` | `null` | Direct ImageSource on the end side. |
| `IsLoading` | `bool` | `false` | Hides leading/trailing icons, shows the spinner, drops opacity to 70%. The spinner appears IMMEDIATELY on tap when the click handler sets this — the press animation runs in parallel and does not block the state change. |
| `TextTruncation` | `bool` | `true` | When true (default) a label too wide for the button is truncated with a trailing ellipsis ("…") instead of overflowing the frame; the cap is re-measured on resize and on icon/text change. Set false to keep the label's natural width (legacy behaviour). The label is always single-line (`MaxLines = 1`). |
| `LoadingText` | `string?` | `null` | Optional text shown next to the spinner while `IsLoading` is true. When null or empty, the button shows the spinner ONLY. When set, the regular `Text` is hidden during loading and `LoadingText` takes its place; `Text` is restored when loading ends. |
| `IconSize` | `double` | `20` | Icon size in dp. |
| `FontSize` | `double` | `14` | Override label font size. Auto-resolved from `Size` when ≤ 0. |
| `FontAttributes` | `FontAttributes` | `Bold` | Label font attributes. |
| `Command` | `ICommand?` | `null` | Executed on tap after the `Clicked` event. |
| `CommandParameter` | `object?` | `null` | Passed to `Command`. |

## Events

- `Clicked` — fires after the press animation completes, before `Command.Execute`.

## Icon Priority (universal)

Per icon slot, the first non-null wins in this order:

1. `LeadingEmoji` / `TrailingEmoji`
2. `LeadingMaterialIcon` / `TrailingMaterialIcon`
3. `LeadingImagePath` / `TrailingImagePath`
4. `LeadingImageSource` / `TrailingImageSource`

The same priority applies to every icon-bearing control in this folder.

## States

| State | Visual |
|---|---|
| Normal | Full color, **flat — no shadow of any kind** (see "Shadows") |
| Disabled | `Opacity = 0.38`, no input |
| Loading | `Opacity = 0.70`, spinner replaces leading icon, no input |
| Pressed | `ScaleTo(0.96, 80, CubicIn)` + ripple |
| Released | `ScaleTo(1.0, 150, CubicOut)` |
| Hover (Windows / macOS) | `TranslateTo(0, -1, 80)` lift |

## Shadows (2026-07-28: none, ever)

**G9Button paints no shadow, and a consumer-set `Shadow` is not supported either.** The app is
shadow-free app-wide — see `../G9Controls.md` §0 and the design guide §12b. The consumer-shadow
forwarding that used to live here (`_consumerShadow` + `ApplyFrameShadow`, which re-cast the
consumer's `Shadow` onto the rounded inner frame so it hugged the corner radius) was **deleted**
along with the built-in variant "glow" and `G9Colors.BuildShadow`.

Setting `Shadow` on a `G9Button` in XAML will now do what MAUI does by default: cast a
**rectangular** shadow from the outer `ContentView`, ignoring the button's corner radius — and, on
Android, do it through the software bitmap-blur path that caused the July 2026 ANR. Don't.

Express emphasis with the `Variant` (fill + gradient) and the frame `Stroke` instead. `G9IconButton`
follows the same rule — its variant drop shadow (`UsesShadow`) was removed at the same time.

## Press → Click → Loading Ordering

The click handler runs **synchronously on touch-up** before the press animation
starts. Setting `IsLoading = true` from the click handler therefore lands on the
same frame as the touch-up event — the spinner appears with zero perceived gap
between finger-up and loading state. The press scale + ripple animation runs in
parallel as a fire-and-forget tactile cue, so it never blocks the loading
transition. Earlier versions awaited the full press animation (~230 ms) before
invoking handlers, which produced a visible "press, then a delay, then the
spinner" sequence.

## Ripple sizing — full-frame fill

The press ripple is drawn by a `GraphicsView` layered behind the text + icon
row inside the `Border`. Breathing room around the row lives on the **row's**
`Margin`, not on the Border's `Padding`. Reason: the `GraphicsView` measures
to its parent rect's interior; with `Border.Padding > 0` that rect was
shrunk by the padding amount, so the ripple's max radius — computed from the
rect's diagonal — only ever reached the inset region. Visually the ripple
covered the full width but the top / bottom of the button was unaffected
(letterbox of unanimated colour). Moving the breathing room onto the row
keeps the GraphicsView measure rect equal to the full Border interior, so
the animation fills the entire button surface in both axes.

## Usage

### XAML

```xml
<newControls:G9Button
    Text="Save"
    Variant="Primary"
    Size="Hero"
    LeadingMaterialIcon="Save"
    Command="{Binding SaveCommand}" />
```

### Code-behind

```csharp
var btn = new G9Button
{
    Text = "Refresh",
    Variant = G9ButtonVariant.Outline,
    LeadingMaterialIcon = MaterialIcons.Refresh
};
btn.Clicked += (_, _) => RefreshData();
```

### Loading state during async work

```csharp
async void OnSaveClicked(object? sender, EventArgs e)
{
    SaveButton.IsLoading = true;
    try
    {
        await _service.SaveAsync();
    }
    finally
    {
        SaveButton.IsLoading = false;
    }
}
```

### Icon-only button

Set `Text` to `null` or empty and provide a leading icon:

```xml
<newControls:G9Button
    Variant="Surface"
    Size="Small"
    LeadingMaterialIcon="MoreVert" />
```

## Behaviour Notes

- The press animation always plays even when no `Command` / `Clicked` handler is wired.
  This gives the user feedback that the tap registered.
- The ripple effect originates at the actual tap point on supported platforms.
- Hero buttons automatically set `HorizontalOptions = Fill`, so they always span the
  parent's width regardless of container layout.
- Hover translation only fires on platforms that report `PointerEntered` /
  `PointerExited` (Windows, macOS Catalyst). Touch platforms ignore it.
- Safe by design: exceptions thrown from `Clicked` / `Command.Execute` are caught and
  swallowed inside the button so a misbehaving handler never crashes the UI thread.
  Wire your safe-command logic at the consumer level if you need error reporting.
- **Label font is culture-resolved automatically (2026-07 fix).** `OnApplyVisuals` sets the
  label's `FontFamily` from `G9Visuals.ResolveCulturalFont()` (Persian face for Fa,
  Latin face for En) — the same helper every other G9 text control
  (`G9Editor`/`G9TextEntry`/`G9PinEntry`/`G9OutlinedFieldBase`) already used.
  `G9Button` was the one holdout that left `FontFamily` unset, so its label fell back to
  MAUI's platform default font on Persian text — visibly mismatched vs. the rest of the UI
  (missing/garbled glyphs on some OEM font stacks). No public property to set — it always
  tracks the active culture, refreshed for free on culture change via
  `G9ControlBase.OnCultureChangedHook`'s default `RequestVisualUpdate()`.

## Text truncation internals — DO NOT regress (2026-07)

The `TextTruncation` cap is hand-rolled because the label sits in a centered
`HorizontalStackLayout` (which offers its children unbounded width, so `LineBreakMode`
alone never truncates). The implementation is deliberately split in three and each part
exists because the obvious alternative deadlocked or ANR'd on-device:

- **`MeasureOverride` only RECORDS the incoming finite width constraint**
  (`_lastFiniteWidthConstraint`). It must never mutate layout-affecting properties
  (e.g. `MaximumWidthRequest`) — doing so re-invalidates measure from inside measure and
  livelocks the UI thread (startup ANR observed on Android).
- **The label's natural width is cached in `OnVisualChanged`** (ClearValue →
  unconstrained `Measure`), i.e. only when text/icon/font actually change — never during
  measure/arrange.
- **`UpdateTextMaxWidth` is pure arithmetic** over those two cached numbers (called from
  `OnSizeAllocated` and `OnVisualChanged`): it clears the cap when the text fits and only
  sets `MaximumWidthRequest` when the value changes by > 0.5 so the loop converges.

Deriving the cap from the button's resolved `Width` instead of the measure constraint is
the classic trap: in an `Auto` Grid column the first measure happens before the bound
text arrives, `Width` stays collapsed, the cap pins the label at ~zero width, and the
button renders with NO visible text (this was a real shipped bug — the map
multi-selection "Continue" button).
