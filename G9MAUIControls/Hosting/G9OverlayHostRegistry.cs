namespace G9MAUIControls.Hosting;

/// <summary>
///     The registry behind <see cref="G9OverlayHosts" />, and the adapter that narrows the internal
///     <c>ModalHost</c> down to the public <see cref="IG9OverlayHost" />.
///     <para>
///         Kept internal on purpose. The internal <c>ModalHost</c> record carries the popup and
///         bottom-sheet control instances as well, which external code must not touch — it should go
///         through the helpers that own the queueing and animation contracts. This type is the wall between
///         the two views of the same registration.
///     </para>
///     <para>
///         Same most-recent-first stack as the internal registry (<see cref="G9PageHostStack{THost}" />),
///         driven from the same call sites in <c>G9PageBase</c>, so the two views resolve the same page.
///     </para>
/// </summary>
internal static class G9OverlayHostRegistry
{
    private static readonly G9PageHostStack<PublicHost> Stack = new();

    /// <summary>
    ///     Raised when the RESOLVED host changes: a page became current, or the current page went away — in
    ///     which case the argument is the page underneath that is current again, or <c>null</c> when none
    ///     is left. Not raised when a page merely re-asserts a registration that is already current.
    /// </summary>
    public static event EventHandler<IG9OverlayHost?>? CurrentChanged;

    /// <summary>
    ///     Makes <paramref name="page" /> the current host (top of the stack, on screen). Called from
    ///     <c>G9PageBase</c> alongside the internal registry assignment, so the two never disagree about
    ///     which page is current.
    /// </summary>
    public static void Set(G9PageBase page, Layout toastLayer, Layout devLayer, Layout overlayLayer)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(toastLayer);
        ArgumentNullException.ThrowIfNull(devLayer);
        ArgumentNullException.ThrowIfNull(overlayLayer);

        if (Stack.Activate(page, new PublicHost(page, toastLayer, devLayer, overlayLayer), out var current))
        {
            Raise(current);
        }
    }

    /// <summary>
    ///     Construct-time registration from <c>G9PageBase.OnApplyTemplate</c>: adds the page without
    ///     letting it displace a page that is on screen. See <c>G9ModalHostRegistry.Register</c>.
    /// </summary>
    public static void Register(G9PageBase page, Layout toastLayer, Layout devLayer, Layout overlayLayer)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(toastLayer);
        ArgumentNullException.ThrowIfNull(devLayer);
        ArgumentNullException.ThrowIfNull(overlayLayer);

        if (Stack.Register(page, new PublicHost(page, toastLayer, devLayer, overlayLayer), out var current))
        {
            Raise(current);
        }
    }

    /// <summary>Marks <paramref name="page" /> as no longer on screen without dropping its entry.</summary>
    public static void MarkOffScreen(G9PageBase page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (Stack.MarkOffScreen(page, out var current))
        {
            Raise(current);
        }
    }

    /// <summary>
    ///     Removes <paramref name="page" /> when it detaches. If it was the current host, the entry beneath
    ///     it becomes current again and <see cref="CurrentChanged" /> reports it; removing a page that is
    ///     not current changes nothing, so an out-of-order teardown cannot blank a newer registration.
    /// </summary>
    public static void Clear(G9PageBase page)
    {
        ArgumentNullException.ThrowIfNull(page);

        if (Stack.Remove(page, out var current))
        {
            Raise(current);
        }
    }

    /// <summary>Returns the active host, or <c>false</c> when none is registered.</summary>
    public static bool TryGet(out IG9OverlayHost host)
    {
        var found = Stack.TryGetCurrent(out var current);
        host = current!;
        return found;
    }

    private static void Raise(IG9OverlayHost? host)
    {
        // Subscribers re-parent views, so this has to be on the main thread. Registration already happens
        // there (OnApplyTemplate / HandlerChanging), but a consumer's teardown path might not, and a
        // cross-thread visual-tree mutation fails in a way that is very hard to trace back to here.
        if (MainThread.IsMainThread)
        {
            CurrentChanged?.Invoke(null, host);
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => CurrentChanged?.Invoke(null, host));
    }

    private sealed record PublicHost(G9PageBase Page, Layout ToastLayer, Layout DevLayer, Layout OverlayLayer)
        : IG9OverlayHost;
}
