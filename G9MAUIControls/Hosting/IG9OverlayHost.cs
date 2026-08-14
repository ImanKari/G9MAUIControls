namespace G9MAUIControls.Hosting;

/// <summary>
///     The active page's overlay layers, as much of them as anything outside this assembly needs.
///     <para>
///         <b>Why this exists.</b> The suite's own overlays (popup, sheet, toast) resolve their host
///         through an internal registry, which was fine while everything lived in one assembly. Once the
///         family split into packages that stopped working: a satellite — or a consumer writing their own
///         always-on overlay — has no way to find the layer to mount into. This interface is that seam.
///     </para>
///     <para>
///         <b>Why it is this narrow.</b> The internal registry also carries the popup and bottom-sheet
///         control instances, and those are deliberately NOT here. An external overlay has no business
///         reaching into another overlay's control — it should call <c>G9PopupHelper</c> /
///         <c>G9BottomSheetHelper</c>, which own the queueing and animation contracts. What an external
///         overlay genuinely needs is a layer to parent itself to, and the page for safe-area insets.
///     </para>
///     <para>
///         The alternative — <c>[assembly: InternalsVisibleTo]</c> — was rejected: it would make every
///         satellite a friend of the core's entire internals, a far larger commitment than the two
///         members needed, and it would do nothing at all for a third-party consumer.
///     </para>
/// </summary>
public interface IG9OverlayHost
{
    /// <summary>
    ///     The page owning these layers. Read it for safe-area insets
    ///     (<see cref="G9PageBase.TopSafeAreaInset" /> and friends) when positioning against a screen edge.
    /// </summary>
    G9PageBase Page { get; }

    /// <summary>
    ///     The layer for transient, always-on-top visuals — toasts, loaders, progress overlays.
    ///     <para>
    ///         This is the <b>second-highest</b> persistent layer in the template's z-stack, above both the
    ///         sheet layer and the popup layer. Sibling order in
    ///         <c>Hosting/G9PageTemplate.xaml</c> IS the z-order, which is what makes "a toast opened
    ///         inside a sheet keeps showing after the sheet closes" work with no per-toast tracking.
    ///     </para>
    ///     <para>
    ///         Mount here for anything that must paint above an open sheet or popup. The layer is
    ///         input-transparent with <c>CascadeInputTransparent = false</c>, so empty areas pass touches
    ///         through to the page while your own child opts back in.
    ///     </para>
    /// </summary>
    Layout ToastLayer { get; }

    /// <summary>
    ///     The topmost persistent layer, above <see cref="ToastLayer" />, and intentionally empty in the
    ///     shipped template.
    ///     <para>
    ///         This is where a permanently-available floating affordance belongs — a developer FAB, a QA
    ///         capture pill, an accessibility helper. Being the last sibling means it both paints above
    ///         everything and, crucially, <b>reliably receives touch</b>: hosting such a control inside the
    ///         page content instead is the mistake that makes taps fall through to whatever is behind it.
    ///     </para>
    ///     <para>Only the transient startup cover sits above this.</para>
    /// </summary>
    Layout DevLayer { get; }

    /// <summary>
    ///     The layer the suite mounts bottom sheets and their backdrop into — <b>below</b>
    ///     <see cref="ToastLayer" /> and below the popup layer, above the page content.
    ///     <para>
    ///         Mount here for an app-owned modal that should behave like a sheet: cover the content, but
    ///         still let a popup or a toast paint over it. A diagnostics or support modal is the typical
    ///         case. Anything that must stay visible above an open sheet belongs in
    ///         <see cref="ToastLayer" /> instead.
    ///     </para>
    ///     <para>
    ///         <b>Sharing a layer with the sheet stack is the trade.</b> The suite adds and removes its own
    ///         children here, so add yours last and remove it yourself; do not reorder or clear the layer.
    ///     </para>
    /// </summary>
    Layout OverlayLayer { get; }
}

/// <summary>
///     Finds the <see cref="IG9OverlayHost" /> for the page currently on screen.
///     <para>
///         The registry behind this stays internal; this is the whole of its public surface. A
///         <c>G9PageBase</c> registers itself when its control template is applied and deregisters when its
///         handler is torn down, so the answer follows the visible page with no bookkeeping from callers.
///     </para>
///     <example>
///         <code>
///         if (G9OverlayHosts.TryGetCurrent(out var host))
///         {
///             host.ToastLayer.Add(myOverlay);          // paints above sheets and popups
///             myOverlay.Margin = new Thickness(16, 0, 16, host.Page.BottomSafeAreaInset + 16);
///         }
///         </code>
///     </example>
/// </summary>
public static class G9OverlayHosts
{
    /// <summary>
    ///     Returns the active host, or <c>false</c> when no <c>G9PageBase</c> has applied its template yet.
    ///     <para>
    ///         <b>Handle the false case; do not assume it cannot happen.</b> It is the real state during the
    ///         window between app start and the first page's template being applied, and again while a page
    ///         is being torn down. Code that mounts an overlay from a background task or a startup hook will
    ///         hit it.
    ///     </para>
    /// </summary>
    public static bool TryGetCurrent(out IG9OverlayHost host) => G9OverlayHostRegistry.TryGet(out host);

    /// <summary>
    ///     The active host, or throws. Use only where the absence of a host is genuinely a programming
    ///     error — inside a user-gesture handler, for instance, where a page must already be on screen.
    /// </summary>
    /// <exception cref="InvalidOperationException">No page has registered a host.</exception>
    public static IG9OverlayHost GetCurrent() =>
        TryGetCurrent(out var host)
            ? host
            : throw new InvalidOperationException(
                "No active G9 overlay host. The visible page must derive from G9PageBase and have applied " +
                "its control template. If you are calling this during startup or teardown, use " +
                "TryGetCurrent and handle the false case.");

    /// <summary>
    ///     Raised when the active host changes — a new page registered, or the last one detached (in which
    ///     case the argument is <c>null</c>).
    ///     <para>
    ///         A long-lived overlay should re-parent itself here rather than caching a layer: the cached
    ///         one belongs to a page that may already be gone, and adding a child to a detached layout is a
    ///         silent no-op that reads as "my overlay stopped appearing".
    ///     </para>
    /// </summary>
    public static event EventHandler<IG9OverlayHost?>? CurrentChanged
    {
        add => G9OverlayHostRegistry.CurrentChanged += value;
        remove => G9OverlayHostRegistry.CurrentChanged -= value;
    }
}
