# G9DateTimePicker

`G9DateTimePicker` is the outlined trigger that opens a bottom-sheet drum picker for
date / time / date-and-time. The calendar (Gregorian / Shamsi) is selected automatically
by the active culture. The picker inherits the shared outline + notched-label
architecture from `G9OutlinedFieldBase`.

## When to use

- Date, time, or date-and-time selection -> `G9DateTimePicker` (set `Mode`).
- Selection from a generic list of options -> `G9Picker` / `G9ComboBox`
  (see `G9Picker.md` / `G9ComboBox.md`).

## Modes

| Mode | Drum columns shown |
|---|---|
| `Date` (default) | Day / Month / Year |
| `Time` | Hour / Minute |
| `DateTime` | Day / Month / Year / Hour / Minute |

## Display Formats

| Format | LTR sample | RTL (Shamsi) sample |
|---|---|---|
| `ShortDate` (default) | "14 Sep 2026" | "۲۴ شهریور ۱۴۰۵" |
| `LongDate` | "September 14, 2026" | "۲۴ شهریور ۱۴۰۵" |
| `TimeOnly` | "09:41 AM" | "۰۹:۴۱ ق.ظ" |
| `ShortDateTime` | "14 Sep 2026, 09:41" | "۲۴ شهریور ۱۴۰۵، ۰۹:۴۱" |
| `LongDateTime` | "September 14, 2026, 09:41 AM" | "۲۴ شهریور ۱۴۰۵، ۰۹:۴۱" |
| `Custom` | `FormattedDisplayText` override | `FormattedDisplayText` override |

## Bindable Properties

### Inherited from `G9OutlinedFieldBase`

See `../G9TextEntry/G9TextEntry.md` for the full inherited list (Label,
Placeholder, HelperText, ErrorText, HasError, AlwaysFloat, IsReadOnly, leading/trailing
icons, etc.).

### Specific to `G9DateTimePicker`

| Property | Type | Default | Description |
|---|---|---|---|
| `SelectedDateTime` | `DateTime?` | `null` | Two-way bindable. The current value (always Gregorian DateTime regardless of culture). |
| `Mode` | `G9DateTimePickerMode` | `Date` | Date / Time / DateTime. |
| `DisplayFormat` | `G9DateTimeDisplayFormat` | `ShortDate` | Display string format. |
| `FormattedDisplayText` | `string?` | `null` | Used only when `DisplayFormat == Custom`. |
| `MinDate` | `DateTime?` | `null` | Lower bound for clamping. |
| `MaxDate` | `DateTime?` | `null` | Upper bound for clamping. |
| `TwentyFourHourDisplay` | `bool` | `false` | Hour column shows 0–23 instead of 12-hour AM/PM. |
| `RestoreOnCancel` | `bool` | `true` | Reverts to the previous value when the user cancels the sheet. |
| `ShowTodayButton` | `bool` | `true` | When true, the sheet renders a compact pill chip below the preview that snaps every column to today's date (or "Now" in Time mode) with a parallel animated scroll. |

## Events

- `DateTimeSelected` — fires when the user confirms a new value via the Done button.

## Drum Picker

The bottom sheet hosts the drum columns:

- **Header** — Cancel button + title + Done button. A second row below the title shows a
  live preview of the currently-selected date/time as the user spins the columns.
- **Today / Now chip** — Compact rounded pill chip centered below the preview. Shows
  "Today" calendar icon + label in date / date-time mode, "Now" label in time-only mode.
  Tapping it animates every column in parallel to today's value (no full rebuild —
  see "Today animation" below).
- **Columns** — fixed-height rows with a centered selection band. Tap a row to scroll it
  into the band. Drag / flick to scroll, and the column polls scroll position at 16ms
  intervals to detect when the native fling has settled (2 consecutive identical ticks
  = settled), then snaps to the nearest row.
- **Selected row** — bold + theme primary text color.
- **Selection band** — hairline tint + 1.5px primary outline overlay (non-interactive).

### Settle detection (replaces the time-debounced snap)

Earlier versions used `await Task.Delay(N)` after each `Scrolled` event to debounce the
snap. That was wrong: native fling on Android / iOS / Windows produces continuous
`Scrolled` events for hundreds of ms, so each event reset the debounce and the snap
fired as much as 1–2 seconds late.

The new approach is a single dispatcher timer ticking every 16ms:

1. A `Scrolled` event arms or re-arms the timer with the current `ScrollY`.
2. Each tick compares `ScrollY` against the previous tick.
3. After 2 consecutive identical ticks (~32ms of position-stability), the snap fires —
   PROVIDED the gesture has been observed for at least `MinSettleAgeMs` (120ms).
4. A custom 169ms CubicOut interpolation animates the column to the snap target —
   short enough to feel responsive, long enough to read as a smooth glide.

This works because every platform's native fling produces continuously-decreasing
position changes until it stops; the moment it stops, our timer detects it within 32ms
and snaps. No event stacking, no cancellation races, no `Task.Delay` chains. Live
selection style (bold + primary color) updates DURING the drag — the user sees the
snap target highlighted in real time. The live-highlight update is O(1) per Scrolled
event: it tracks the previously-highlighted index and only flips two items (clear old,
set new) instead of scanning the whole column. With ~30 Scrolled events per fling and
columns up to 101 items long, this avoids ~3000 unnecessary `IsSelected` writes per
gesture.

Two filters guard against spurious settles, especially on short columns (Month with
12 items, Hour with 24, etc.):

- **Minimum gesture age (`MinSettleAgeMs` = 120ms)** — `OnSettleTick` won't declare
  a gesture settled until at least 120ms have passed since the gesture's first
  Scrolled event. Without this, a slow drag that pauses for a single 32ms frame
  would trigger a snap WHILE the finger is still on the screen, forcing the column
  to jump to a wrong row mid-drag. 120ms is well below any deliberate hold but
  comfortably above any human pause inside a continuous gesture.
- **Post-guard residual filter (`PostGuardSettleWindowMs` = 80ms)** — sub-pixel
  Scrolled events (|dy| &lt; 1px) within 80ms after a programmatic snap completes
  are ignored **only when no finger is in contact**. The platform fires 0.3px
  residual reports as it settles after our last animation frame; without
  filtering, those count as a "new gesture" and immediately trigger a
  48ms-old spurious snap. The threshold is well below any real user drag
  (which produces multi-px deltas). Bypassing the filter while the finger is
  down is essential for **slow drags that immediately follow a snap** —
  those produce sub-pixel Scrolled events that are real user input, not
  echoes; without the bypass the highlight would stay stuck on the previous
  selection until the user dragged fast enough to clear the residual window.

### Finger-on-surface freeze

The settle detector polls `ScrollY` and snaps when the position is stable for
≥ 32ms. That definition of "stable" is correct for "finger lifted, fling
finished" but **not** for "finger held still on the surface" — both look
identical from `ScrollY`'s perspective. Without an extra signal the column
would snap *under* the user's finger the moment they paused, then fight the
next drag because the snap had already moved the position.

`G9DrumColumn` tracks finger contact via a passive platform-specific touch
hook attached on `HandlerChanged`:

- **Android** — `View.SetOnTouchListener` on the platform `NestedScrollView`.
  The listener returns `false` on every event so the platform's own scroller
  still consumes the touch and runs its native pan / fling exactly as before;
  we only read `ACTION_DOWN` / `ACTION_MOVE` / `ACTION_UP` / `ACTION_CANCEL`
  to flip the `_isFingerDown` flag. `MOVE` is included because Android's
  dispatch rule sends `ACTION_DOWN` only to the deepest view that claims it
  (e.g. a row cell with its own tap recognizer); the parent ScrollView's
  listener doesn't see DOWN in that case and only starts firing on MOVE
  after the touch slop is exceeded. Treating MOVE as a finger-in-contact
  signal closes that hole — `SetFingerDown(true)` is idempotent so repeated
  MOVEs are cheap.
- **iOS / Mac Catalyst** — a passive `UIPanGestureRecognizer` with
  `CancelsTouchesInView=false` so the native scroll pan still drives
  scrolling. Both `Began` and `Changed` flip `_isFingerDown = true` (same
  rationale as the Android MOVE guard — a touch starting on a child view
  may skip the Began callback on our observer).
- **Windows** — `PointerPressed` / `PointerReleased` / `PointerCanceled` /
  `PointerCaptureLost` routed events on the platform `ScrollViewer`.

Effect on the state machine:

- **Touch down** → cancel any in-flight snap (so the finger lands at the
  position the user *saw*, not at wherever the snap had drifted to), clear
  the `_programmaticScroll` guard, and stop the settle timer so it doesn't
  tick during the hold.
- **While held** — `OnSettleTick` short-circuits if `_isFingerDown` is true,
  resetting the stable-tick counter every frame. The column stays exactly
  where the user left it; subsequent drags resume from that position.
- **Touch up** → reset the gesture clock so `MinSettleAgeMs` measures from
  release rather than first contact, restart the settle timer so the
  platform's native fling can decelerate, then snap to the final row.

### Cancellable per-frame snap animation

The snap is NOT done via `ScrollToAsync(animated: true)`. That call plays a native
deceleration animation that runs ~1000ms on Android and is not cancellable from C# —
we observed it locking the user out of re-touch for the full duration, making the
picker feel stuck on rapid drag-release-redrag sequences.

Instead, the snap uses a custom interpolated animation:

- A single `CancellationTokenSource` (`_snapCts`) gates the animation. A new gesture or
  any subsequent `AnimatedScrollAsync` call cancels the in-flight token immediately.
- Each frame is driven by a `Stopwatch` + `Task.Delay(16)` loop. The eased target Y is
  computed from `Easing.CubicOut.Ease(elapsed / duration)` and written via
  `ScrollToAsync(0, y, false)` — `animated: false` so the platform applies it instantly
  and never runs its own opaque animation. We own every frame.
- The `_lastProgrammaticScrollY` field is updated on every frame. When OnScrolled
  reports a position more than ~20dp away from that value, we know a finger has touched
  the surface (the platform's animated frame would match exactly, our animated frame
  would match exactly, only a real drag can produce that delta). We immediately cancel
  the snap and release the `_programmaticScroll` guard so the gesture is honored from
  the next frame on.
- 20dp is far below the previous 120dp threshold; the per-frame model gives precise
  position control so we can use a tight tolerance, which means the user's first touch
  during a snap is honored within a single frame instead of having to drag 3 row-heights
  before being recognised.

The result: re-touch during a snap is never blocked. Drag → release → drag → release
in rapid succession works smoothly, no "stuck" frames, no swallowed gestures.

### Today animation

When the user taps the Today / Now chip, every column animates in parallel via
`Task.WhenAll` to the new value. Each column uses its own `AnimateToValue(int)` method
which calls the cancellable interpolated scroll with `RollDurationMs` (480ms) instead
of the post-drag `SnapDurationMs` (169ms). The post-drag snap is intentionally fast so
the user feels the gesture is committed instantly; the Today button is a transition
the user wants to *watch*, so it gets a longer, smoother glide.

If today's year falls outside the year column's currently-built range (e.g. user
navigated decades away from the initially-selected year), the year column rebuilds
its items first so `AnimateToValue` has a target row to glide to. Day count
adjustment uses the same `TrimOrExtendItems` path as `ApplySelectionFromColumns` —
incremental, no full rebuild.

This works identically in Gregorian and Persian (Shamsi) calendars. The
`GetDaysInMonth` helper branches on the active culture: `PersianCalendar.GetDaysInMonth`
returns 30/31 for months 1–6, 30 for months 7–11, and 29/30 for Esfand depending on
leap status. The `_dayColumnBuiltForDayCount` cache key is just the integer count, so
it correctly invalidates for both Gregorian leap-Feb (28 ↔ 29) and Persian leap-Esfand
(29 ↔ 30) transitions.

## Calendar Selection

- LTR cultures → Gregorian (Day / Month / Year).
- RTL cultures (Persian) → Shamsi (روز / ماه / سال) using `System.Globalization.PersianCalendar`.
- The persisted `SelectedDateTime` is **always Gregorian**. The Persian conversion is a
  display-and-input concern only, so binding the same value into a database, a service
  call, or a serializer always yields a stable Gregorian timestamp.

## Usage

### Date only

```xml
<newControls:G9DateTimePicker
    Label="Inspection date"
    Placeholder="Pick a date"
    Mode="Date"
    DisplayFormat="LongDate"
    SelectedDateTime="{Binding InspectionDate, Mode=TwoWay}" />
```

### Time only, 24-hour

```xml
<newControls:G9DateTimePicker
    Label="Start time"
    Mode="Time"
    DisplayFormat="TimeOnly"
    TwentyFourHourDisplay="True"
    SelectedDateTime="{Binding StartTime, Mode=TwoWay}" />
```

### Date and time

```xml
<newControls:G9DateTimePicker
    Label="Captured at"
    Mode="DateTime"
    DisplayFormat="ShortDateTime"
    SelectedDateTime="{Binding CapturedAt, Mode=TwoWay}" />
```

### Date with min / max bounds

```xml
<newControls:G9DateTimePicker
    Label="Plan date"
    Mode="Date"
    MinDate="{Binding TodayMidnight}"
    MaxDate="{Binding NextYear}"
    SelectedDateTime="{Binding PlanDate, Mode=TwoWay}" />
```

### Custom display string

```csharp
DateField.DisplayFormat = G9DateTimeDisplayFormat.Custom;
DateField.FormattedDisplayText = $"Week {GetWeek(date)} of {date.Year}";
```

### Read-only display

```xml
<newControls:G9DateTimePicker
    Label="Locked date"
    Mode="Date"
    IsReadOnly="True"
    SelectedDateTime="{Binding HistoricalDate}" />
```

## Behaviour Notes

- The picker calls `this.Unfocus()` before opening the sheet so the parent ScrollView
  doesn't auto-scroll to the picker.
- **Deferred sheet content** — the bottom sheet opens with a centered spinner FIRST
  and constructs the heavy view tree (4–5 drum columns × ~100 rows each = hundreds of
  Label + ContentView allocations) AFTER the open animation has played. Without this
  the user saw a 1–3s tap-to-open lag because everything ran synchronously on the UI
  thread before the sheet could appear. Measured on emulator: tap → ShowAsync 64ms →
  factory invoked +480ms (after open animation) → factory body 336ms → ~816ms total
  perceived time, with the sheet visibly animating in immediately.
- **Crossfade reveal (`FadeDeferredContentIn = true`)** — the drum columns realize
  row-by-row on first paint, so a plain spinner→content swap looked staggered ("one
  part shows, then the next"). The sheet sets `G9BottomSheetOptions.FadeDeferredContentIn`,
  which keeps the spinner on screen while the freshly-built tree lays out hidden
  (`Opacity 0`), then crossfades the whole picker in as **one settled unit**. Because an
  `Opacity 0` child still measures at full size, the fit-to-content sheet has already
  grown to the picker's real height before the fade — no resize jump during the reveal.
  Business logic is untouched: it only changes *when/how* the already-built tree becomes
  visible. See `G9BottomSheetGuide.md` → "Lazy heavy-content rendering" → "Crossfade reveal".
- The drum column uses a `ScrollView` with fixed-height rows. The 16ms-tick settle
  detector (see Drum Picker → "Settle detection" above) decides when to snap; no
  platform `CollectionView` snap points are involved — keeps behaviour identical across
  Android / iOS / macOS / Windows.
- The `Today` chip is centered, `PrimaryContainer`-tinted, with a primary-tinted icon
  and label. A tap pulse (`Scale 0.94 → 1.0`) gives tactile feedback before the
  parallel column animation runs.
- Day count refreshes when month or year changes (e.g. selecting February auto-truncates
  the day list to 28/29). Crucially, the day column is NOT rebuilt unless the day COUNT
  actually changes — the sheet caches `_dayColumnBuiltForDayCount` and only adjusts the
  column when the new month requires a different count (e.g. March 31 → April 30,
  Persian Mordad 31 → Shahrivar 31 stays cached, Aban 30 → Azar 30 stays cached, etc.).
  Year-only changes within the same month leave the count untouched and the day labels
  are just `"01".."31"` with no year/month dependency, so any adjustment was pure waste.

  When the count DOES change, the column uses `G9DrumColumn.TrimOrExtendItems` instead
  of a full `SetItems` rebuild. `SetItems` destroys all 30-31 row Views and runs a full
  measure/arrange pass (~500-950ms on Android). `TrimOrExtendItems` just adds or
  removes 1-3 rows at the end, keeping the rest of the view tree intact — typically
  &lt;5ms. Without this, every Month swipe that crossed a day-count boundary
  (Jan→Feb, Mar→Apr, Aug→Sep, Persian Shahrivar→Mehr, Esfand-leap-flip, etc., which
  is most of them) blocked the UI thread for ~700ms inside the synchronous
  `SelectedValueChanged` handler and silently swallowed the user's next touch.

  An earlier cache key of `(year, month)` was wrong: it invalidated on any year change
  and forced full rebuilds. Switching the cache key to `_dayColumnBuiltForDayCount` and
  the rebuild path to `TrimOrExtendItems` brought month-swipe handler cost from
  500–950ms to **0–4ms**, and year-only swipes already stayed at &lt;5ms. Both
  Gregorian and Persian (Shamsi) calendars are covered: `GetDaysInMonth` branches on
  the active culture and returns the correct count for either, and the day labels are
  formatted with `G9Culture.CurrentCulture` so Persian digits appear in
  Persian mode.
- The header preview formats through the picker's own `FormatValue`, so the live preview
  uses the same display format as the trigger box. They never diverge.
- `MinDate` / `MaxDate` bound the underlying `DateTime`. In Persian mode the same
  Gregorian min/max are converted to Persian year/month/day for the year column range.
- The trailing icon defaults to `MaterialIcons.CalendarMonth` and can be overridden via
  `TrailingMaterialIcon`.
