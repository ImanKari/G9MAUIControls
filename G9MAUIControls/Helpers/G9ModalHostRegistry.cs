using G9MAUIControls.BottomSheet;
using G9MAUIControls.Popup;
using G9MAUIControls.Hosting;
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
///     Tracks the live <c>G9PageBase</c> instances and their overlay infrastructure (popup, bottom
///     sheet, overlay grid, toast grid), and resolves which one is current. Used by every overlay helper
///     in the app — <c>G9PopupHelper</c>, <c>G9ToastHelper</c>, <c>SyncProgressOverlayHelper</c>,
///     <c>G9BottomSheetHelper</c>, <c>BarcodeScanService</c>, <c>AdminDiagnosticsModalService</c> — to
///     find the correct host for the active page without each helper duplicating the window-walk +
///     visible-page resolution logic.
///     <para>
///         This was a single static slot, which held only while exactly one <c>G9PageBase</c> was ever
///         alive. It is now a most-recent-first stack (<see cref="G9PageHostStack{THost}" />, which also
///         documents the resolution rule and why pages are held weakly): a page that is built but never
///         shown cannot displace the visible one, and tearing down a pushed / modal page hands "current"
///         back to the page underneath instead of leaving nothing registered.
///     </para>
/// </summary>
internal static class G9ModalHostRegistry
{
    private static readonly G9PageHostStack<ModalHostEntry> Stack = new();

    /// <summary>
    ///     Makes <paramref name="page" /> the current host: pushes it (or moves it) to the top of the
    ///     stack and marks it on screen. Called by <see cref="G9PageBase" /> whenever the page becomes
    ///     visible (<c>Loaded</c> / <c>OnAppearing</c>), so a page the user returns to is current again.
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

        Stack.Activate(page, new ModalHostEntry(page, popup, bottomSheet, overlayHost, toastHost), out _);
    }

    /// <summary>
    ///     Construct-time registration, called by <see cref="G9PageBase.OnApplyTemplate" /> after the
    ///     control template's named children have been resolved. Unlike <see cref="Assign" /> it does NOT
    ///     claim the top: setting <c>ControlTemplate</c> in the page constructor runs
    ///     <c>OnApplyTemplate</c> synchronously, so this fires for pages that are not — and may never be —
    ///     on screen. The page becomes current immediately only when no visible page outranks it, which is
    ///     what keeps app start (first page built, nothing loaded yet) working as before.
    /// </summary>
    public static void Register(
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

        Stack.Register(page, new ModalHostEntry(page, popup, bottomSheet, overlayHost, toastHost), out _);
    }

    /// <summary>
    ///     Marks the page as no longer on screen (<c>Unloaded</c>) without dropping it, so the next
    ///     visible page underneath resolves as current while a transiently-unloaded only-page still does.
    /// </summary>
    public static void MarkOffScreen(G9PageBase page)
    {
        ArgumentNullException.ThrowIfNull(page);

        Stack.MarkOffScreen(page, out _);
    }

    /// <summary>
    ///     Removes the registration when the page detaches (handler is being torn down). The previous
    ///     entry in the stack becomes current again.
    /// </summary>
    public static void Remove(G9PageBase page)
    {
        ArgumentNullException.ThrowIfNull(page);

        Stack.Remove(page, out _);
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

    /// <summary>Returns the current host, or false if none is registered.</summary>
    public static bool TryGetCurrentHost(out ModalHost host)
    {
        host = default;

        if (!Stack.TryGetCurrent(out var entry) || entry is null)
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
