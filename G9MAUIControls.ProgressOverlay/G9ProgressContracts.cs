namespace G9MAUIControls.ProgressOverlay;

/// <summary>
///     One progress update for the overlay.
///     <para>
///         <b>Why <see cref="Stage" /> is a string and not an enum.</b> The component this was extracted
///         from carried a <c>G9ProgressStage</c> enum whose members were that app's workflow —
///         <c>GettingData</c>, <c>ApplyingData</c>, <c>GeoDatabase</c>. A package cannot own that list: the
///         next consumer's stages are different, and an enum forces either a wrong vocabulary or a
///         permanent <c>Other</c> member that carries no information.
///     </para>
///     <para>
///         So the caller supplies an already-localized stage label. That also puts localization where it
///         belongs — the app knows its resource keys and its culture; the overlay only renders text.
///     </para>
/// </summary>
/// <param name="Ratio">
///     Completion from 0 to 1, clamped. Pass <c>null</c> for indeterminate work, which renders a
///     continuous bar rather than a fill — the honest visual when total work is unknown.
/// </param>
/// <param name="Stage">
///     Localized label for the current stage, e.g. "Uploading". Shown as the primary line.
/// </param>
/// <param name="Detail">
///     Optional secondary line — a count ("12 of 250"), a filename, a rate. Kept separate from
///     <paramref name="Stage" /> so the overlay can truncate it independently on a narrow screen.
/// </param>
// A sealed record, not a record struct: these travel through WeakReferenceMessenger, whose
// Register<TMessage>/Unregister<TMessage> constrain TMessage to a reference type (CS0452).
public sealed record G9ProgressReport(double? Ratio, string? Stage = null, string? Detail = null)
{
    /// <summary>The clamped ratio, or <c>null</c> when indeterminate.</summary>
    public double? ClampedRatio => Ratio.HasValue ? Math.Clamp(Ratio.Value, 0d, 1d) : null;

    /// <summary>True when total work is unknown and the bar should run continuously.</summary>
    public bool IsIndeterminate => !Ratio.HasValue;

    /// <summary>Convenience for a determinate report.</summary>
    public static G9ProgressReport At(double ratio, string? stage = null, string? detail = null) =>
        new(ratio, stage, detail);

    /// <summary>Convenience for work whose total is not known.</summary>
    public static G9ProgressReport Indeterminate(string? stage = null, string? detail = null) =>
        new(null, stage, detail);
}

/// <summary>
///     The overlay's lifecycle. ONE state machine, plus an orthogonal minimized flag that is deliberately
///     NOT a state here — minimizing does not change what the operation is doing.
/// </summary>
public enum G9ProgressOverlayState
{
    /// <summary>Work in flight. The only state in which the card body responds to a tap (to minimize).</summary>
    Running,

    /// <summary>
    ///     Cancellation requested and not yet finished. The bar freezes, the spinner stays, and the cancel
    ///     button hides so a second tap cannot queue a second cancel.
    /// </summary>
    Canceling,

    /// <summary>Finished successfully. Terminal.</summary>
    Success,

    /// <summary>Failed. Terminal, and the only state that offers retry.</summary>
    Error
}

/// <summary>Which screen edge the overlay anchors to.</summary>
public enum G9ProgressOverlayPosition
{
    /// <summary>
    ///     Bottom edge. The default, and the one that participates in toast stacking through
    ///     <c>IG9BottomAnchoredOverlay</c>.
    /// </summary>
    Bottom,

    /// <summary>Top edge. Use when the bottom is occupied by a tab bar the overlay would obscure.</summary>
    Top
}

// DELIBERATELY ABSENT: a `G9ProgressOverlayOptions` configuration object, and a `G9ProgressOutcome`
// closing report.
//
// Both existed here in an earlier draft, written from the design notes before the ported implementation
// was re-pointed at this package. Between them they advertised eight things the overlay does not do:
// a persistent title distinct from the context text, per-session OnCancel/OnRetry delegates, tunable
// success/error lingers, an AllowMinimize opt-out, and an OnClosed callback carrying an outcome.
//
// What the implementation actually offers:
//   * context text + position ...... `G9ProgressOverlayHelper.ShowAsync(contextText, position)`
//   * progress in ................... `G9ProgressOverlayHelper.Report(...)` / `ReportQueued(...)`
//   * cancel out ................... `G9ProgressOverlayHelper.CancelRequested`, because a shared overlay
//                                     cannot route cancellation to one owner — see that event's remarks
//   * retry ........................ supplied per failure, to `TryShowCurrentFailureAsync(...)`
//   * lingers ...................... fixed in the view (a shared overlay whose dwell time changed per
//                                     caller would flicker between concurrent operations)
//   * minimize ..................... always available while running
//
// A public options type nobody consumes is worse than no options type: it is a promise the package
// cannot keep, and shipping it in 1.0 would freeze that promise. Add members here only alongside the
// session behaviour that honours them. See LES-0012.

/// <summary>
///     How many further runs are waiting behind the active one.
///     <para>
///         Rendered as a small badge rather than by mounting a second overlay — two overlays competing for
///         the same screen edge is never the right answer, and the queued runs have no progress of their
///         own to show yet.
///     </para>
/// </summary>
/// <param name="Count">Runs queued behind the active one. Negative values are treated as zero.</param>
public sealed record G9ProgressQueuedCount(int Count)
{
    /// <summary>The count, floored at zero.</summary>
    public int Value => Math.Max(0, Count);

    /// <summary>True when a badge should be shown.</summary>
    public bool HasQueue => Value > 0;
}

/// <summary>
///     Raised when the user taps cancel on the overlay.
///     <para>
///         <b>Why cancellation is a broadcast and not a per-session delegate.</b> One overlay is shared by
///         every concurrent operation through leases, so at cancel time the overlay cannot know which
///         operation the user meant, and a single delegate would belong to whichever caller happened to
///         mount it first. Broadcasting hands that decision to the application, which is the only layer
///         that knows what is in flight.
///     </para>
///     <para>
///         Subscribe through <see cref="G9ProgressOverlayHelper.CancelRequested" /> rather than to the
///         messenger directly — the transport is an implementation detail. The overlay deliberately does not
///         know <i>what</i> is being cancelled, and enters
///         <see cref="G9ProgressOverlayState.Canceling" /> immediately so the user gets feedback even when
///         unwinding is slow.
///     </para>
/// </summary>
public sealed record G9ProgressCancelRequested;
