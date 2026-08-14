namespace G9MAUIControls.Hosting;

/// <summary>
///     The single slot behind <see cref="G9OverlayHosts" />, and the adapter that narrows the internal
///     <c>ModalHost</c> down to the public <see cref="IG9OverlayHost" />.
///     <para>
///         Kept internal on purpose. The internal <c>ModalHost</c> record carries the popup and
///         bottom-sheet control instances as well, which external code must not touch — it should go
///         through the helpers that own the queueing and animation contracts. This type is the wall between
///         the two views of the same registration.
///     </para>
/// </summary>
internal static class G9OverlayHostRegistry
{
    private static readonly Lock SyncRoot = new();
    private static PublicHost? _current;

    /// <summary>Raised after <see cref="Set" /> or <see cref="Clear" /> changes the active host.</summary>
    public static event EventHandler<IG9OverlayHost?>? CurrentChanged;

    /// <summary>
    ///     Publishes the active page's layers. Called from <c>G9PageBase.OnApplyTemplate</c>, alongside the
    ///     internal registry assignment, so the two never disagree about which page is current.
    /// </summary>
    public static void Set(G9PageBase page, Layout toastLayer, Layout devLayer, Layout overlayLayer)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(toastLayer);
        ArgumentNullException.ThrowIfNull(devLayer);
        ArgumentNullException.ThrowIfNull(overlayLayer);

        PublicHost host = new(page, toastLayer, devLayer, overlayLayer);
        lock (SyncRoot)
        {
            _current = host;
        }

        Raise(host);
    }

    /// <summary>
    ///     Clears the slot when <paramref name="page" /> detaches. Ignores a page that is not the current
    ///     one, so an out-of-order teardown cannot blank a newer page's registration.
    /// </summary>
    public static void Clear(G9PageBase page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var cleared = false;
        lock (SyncRoot)
        {
            if (_current is { } current && ReferenceEquals(current.Page, page))
            {
                _current = null;
                cleared = true;
            }
        }

        if (cleared)
        {
            Raise(null);
        }
    }

    /// <summary>Returns the active host, or <c>false</c> when none is registered.</summary>
    public static bool TryGet(out IG9OverlayHost host)
    {
        lock (SyncRoot)
        {
            host = _current!;
            return _current is not null;
        }
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
