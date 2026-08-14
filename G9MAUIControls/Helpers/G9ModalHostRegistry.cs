using G9MAUIControls.BottomSheet;
using G9MAUIControls.Popup;
using G9PageBase = G9MAUIControls.Hosting.G9PageBase;

namespace G9MAUIControls.Helpers;

/// <summary>
///     Snapshot of the active page's overlay infrastructure. Carried by every helper that
///     needs to mount UI above the page (popup, bottom sheet, toast, sync overlay, admin
///     diagnostics modal). The four hosts mirror the four-layer stack defined in
///     <c>G9PageTemplate.xaml</c> — see the comment at the top of that file for the
///     exact bottom-to-top order. <see cref="ToastHost" /> is positioned ABOVE
///     <see cref="OverlayHost" /> in document order so toast / loader visuals paint over
///     any popup or bottom sheet that's open.
/// </summary>
internal readonly record struct ModalHost(
    G9PageBase Page,
    G9PopupView G9Popup,
    G9SheetView G9BottomSheet,
    Grid OverlayHost,
    Grid ToastHost);

/// <summary>
///     Single-slot helper that tracks the currently-attached <c>G9PageBase</c> and its
///     overlay infrastructure (popup, bottom sheet, overlay grid, toast grid). Used by every
///     overlay helper in the app — <c>G9PopupHelper</c>, <c>G9ToastHelper</c>,
///     <c>SyncProgressOverlayHelper</c>, <c>G9BottomSheetHelper</c>, <c>BarcodeScanService</c>,
///     <c>AdminDiagnosticsModalService</c> — to find the correct host for the active page
///     without each helper duplicating the window-walk + visible-page resolution logic.
///     <para>
///         In the single-page model only one <c>G9PageBase</c> (typically
///         <c>MainPage</c>) is alive at a time, so book-keeping like activation order, weak
///         references, and resolution fall-backs aren't needed — the single slot is replaced on
///         <see cref="Assign" /> and cleared on <see cref="Remove" />.
///     </para>
/// </summary>
internal static class G9ModalHostRegistry
{
    private static readonly Lock SyncRoot = new();
    private static ModalHostEntry? _current;

    /// <summary>
    ///     Registers (or replaces) the currently-attached host. Called by
    ///     <see cref="G9PageBase.OnApplyTemplate" /> after the control template's named children
    ///     have been resolved.
    /// </summary>
    public static void Assign(
        G9PageBase page,
        G9PopupView popup,
        G9SheetView bottomSheet,
        Grid overlayHost,
        Grid toastHost)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(popup);
        ArgumentNullException.ThrowIfNull(bottomSheet);
        ArgumentNullException.ThrowIfNull(overlayHost);
        ArgumentNullException.ThrowIfNull(toastHost);

        lock (SyncRoot)
        {
            _current = new ModalHostEntry(page, popup, bottomSheet, overlayHost, toastHost);
        }
    }

    /// <summary>
    ///     Removes the registration when the page detaches (handler is being torn down).
    /// </summary>
    public static void Remove(G9PageBase page)
    {
        ArgumentNullException.ThrowIfNull(page);

        lock (SyncRoot)
        {
            if (_current is { } entry && ReferenceEquals(entry.Page, page))
            {
                _current = null;
            }
        }
    }

    /// <summary>Throws if no host is registered. Used by must-have callers.</summary>
    public static ModalHost GetCurrentHostOrThrow()
    {
        if (TryGetCurrentHost(out var host))
        {
            return host;
        }

        throw new InvalidOperationException(
            "No active modal host found. Ensure the current page inherits G9PageBase and is visible.");
    }

    /// <summary>Returns the registered host, or false if none is attached.</summary>
    public static bool TryGetCurrentHost(out ModalHost host)
    {
        host = default;

        ModalHostEntry? entry;
        lock (SyncRoot)
        {
            entry = _current;
        }

        if (entry is null)
        {
            return false;
        }

        host = entry.ToModalHost();
        return true;
    }

    private sealed record ModalHostEntry(
        G9PageBase Page,
        G9PopupView G9Popup,
        G9SheetView G9BottomSheet,
        Grid OverlayHost,
        Grid ToastHost)
    {
        public ModalHost ToModalHost()
        {
            return new ModalHost(Page, G9Popup, G9BottomSheet, OverlayHost, ToastHost);
        }
    }
}
