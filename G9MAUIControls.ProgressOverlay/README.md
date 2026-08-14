# G9MAUIControls.ProgressOverlay

Staged progress overlay with cancel and retry, for [`G9MAUIControls`](https://www.nuget.org/packages/G9MAUIControls).

```
dotnet add package G9MAUIControls.ProgressOverlay
```

## What it adds

A bottom-anchored overlay for a long operation that has stages, can fail, and can be cancelled — the case
a plain progress toast does not cover.

- **One state machine**: `Running` → `Canceling` → `Success` | `Error`, plus an orthogonal minimized flag.
- **No minimize button.** While running, tapping the card body minimizes it to a draggable 72×72 bubble;
  tapping the bubble restores it. A small-travel pan counts as a tap, not a drag.
- **Cancel is a 44×44 target** that switches instantly to `Canceling…` — frozen bar, spinner kept — then
  closes and reports one neutral "cancelled" result. **A cancellation never routes through the error
  terminal**: a user who cancelled did not hit a failure and must not be shown one.
- **A terminal state always forces the full card**, even from minimized. A failure that arrives while
  minimized auto-expands, so it cannot be missed.
- Mounts above the toast stack through the core's `IG9BottomAnchoredOverlay` seam, so ordinary toasts lift
  clear of it instead of covering it.

## Why this is a separate package

It adds **no dependency** beyond the core — the split is for API-surface hygiene, not dependency weight.
This is an *opinionated* component: it encodes one specific workflow (four states, a cancel contract, a
retry affordance, a message-driven progress source). The core ships only the generic seam and lets the
opinion live out here, so the core's public surface stays small and this can iterate without moving the
core's version.

## Usage

```csharp
// The handle is a LEASE, not the overlay. Several concurrent operations can each hold one; the overlay
// tears down when the last is disposed, so they share one visual instead of stacking.
await using var progress = await G9ProgressOverlayHelper.ShowAsync(
    "Uploading samples",
    G9ProgressOverlayPosition.Bottom);

// Progress goes in through the helper, not through the handle: the code reporting progress is usually
// nowhere near the code that opened the overlay, and this keeps the transport an implementation detail.
G9ProgressOverlayHelper.Report(ratio: 0.048, stage: "Uploading", detail: "12 of 250");
G9ProgressOverlayHelper.Report(G9ProgressReport.Indeterminate("Connecting"));   // total unknown
G9ProgressOverlayHelper.ReportQueued(3);                                        // "+3 waiting" badge

// Terminal states act on whichever overlay is currently mounted.
await G9ProgressOverlayHelper.TryShowCurrentSuccessAsync("250 samples uploaded");
await G9ProgressOverlayHelper.TryShowCurrentFailureAsync(
    "Server unreachable", retryText: "Retry", retryAction: () => uploader.RetryAsync());

// A failure with nothing on screen — a background sync, a retry replay.
await G9ProgressOverlayHelper.ShowStandaloneFailureAsync(
    "Background sync failed", "Retry", () => uploader.RetryAsync());
```

**Cancellation is an event, not a callback.** Because one overlay is shared, it cannot know which operation
the user meant to cancel — so it broadcasts and the application decides:

```csharp
G9ProgressOverlayHelper.CancelRequested += (_, _) => uploader.Cancel();
```

Handlers are held strongly; unsubscribe on teardown. A static event is still right here — cancellation has
to reach code that outlives any one page.

**There is deliberately no options object.** Configuration is the context text plus the position; retry is
supplied per failure; the terminal dwell times are fixed (a shared overlay whose dwell changed per caller
would flicker between concurrent operations); minimize is always available while running. An earlier draft
published an options type describing six knobs the overlay does not have, and it was removed before 1.0
rather than shipped as a promise it could not keep.

Every gesture and animation handler is exception-guarded on purpose: this overlay is on screen during the
most failure-prone moments in an app, so a fault inside it must never take the UI loop down with it.

## Requirements

.NET 10 · `net10.0-android`, `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows10.0.19041.0`

## License

MIT
