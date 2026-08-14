namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Implemented by bottom-sheet content that loads its data <b>after</b> the sheet is already
///     visible ("open then fill"). <see cref="G9BottomSheetHelper" /> opens the sheet immediately —
///     the content paints its own loading/preview state — and then calls
///     <see cref="LoadDeferredAsync" /> once the open animation has completed. This keeps the open
///     snappy (no synchronous data fetch blocks the tap → open path) and, because the sheet opens
///     instantly, the helper's single-key <c>ShowG9BottomSheet</c> throttle still collapses a rapid
///     double-tap into one sheet (heavy pre-open work would otherwise push the second open past
///     the throttle window and surface a duplicate sheet).
///     <para>
///         The helper invokes this exactly once per open, on the UI thread, wrapped so a failure
///         can never propagate. The supplied <see cref="CancellationToken" /> is cancelled when the
///         sheet closes, so implementations must stop applying results once it is signalled.
///     </para>
///     <para>
///         Prefer deriving from <c>LoadableSheetContentView&lt;TData&gt;</c>, which implements this
///         contract together with the loading-state, cancellation, and main-thread-apply
///         boilerplate. Implement the interface directly only when a view cannot use that base.
///     </para>
/// </summary>
public interface IDeferredSheetLoad
{
    /// <summary>
    ///     Runs the deferred data load. Called by <see cref="G9BottomSheetHelper" /> on the UI thread
    ///     after the sheet's open animation completes. <paramref name="cancellationToken" /> is
    ///     cancelled when the owning sheet closes.
    /// </summary>
    Task LoadDeferredAsync(CancellationToken cancellationToken);
}
