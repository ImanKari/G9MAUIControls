using System.Runtime.CompilerServices;

namespace G9MAUIControls.Hosting;

/// <summary>
///     The ordering both host registries share: a small, most-recent-first stack of the
///     <see cref="G9PageBase" /> instances that have applied their template, from which "the current
///     host" is resolved.
///     <para>
///         <b>Why a stack and not a slot.</b> The registries used to be one static slot, claimed when a
///         page was CONSTRUCTED (setting <c>ControlTemplate</c> runs <c>OnApplyTemplate</c> synchronously)
///         and cleared with no fallback. Two failures followed. A page that was built but never shown
///         stole every popup and toast from the page actually on screen; and when a pushed or modal
///         <c>G9PageBase</c> was torn down the slot went null while the page underneath was never
///         re-registered — after which every bottom sheet threw "No active modal host".
///     </para>
///     <para>
///         <b>Resolution rule.</b> The current host is the most recent entry whose page is on screen; when
///         no entry is on screen (app start before the first <c>Loaded</c>, or a transient Android unload
///         of the only page) it is simply the most recent entry. A construct-time registration therefore
///         can never displace a visible page, and removing the top entry hands "current" back to the one
///         beneath it.
///     </para>
///     <para>
///         <b>Why pages are held weakly.</b> A page that is constructed and then dropped never gets a
///         handler, so it never gets a teardown to deregister in. The slot holds a
///         <see cref="WeakReference{T}" /> and the host payload lives in a
///         <see cref="ConditionalWeakTable{TKey,TValue}" /> keyed by the page — an ephemeron, so the
///         payload's own references back to the page (its template children) do not keep it alive.
///     </para>
///     Every mutator reports whether the RESOLVED host changed, so the public registry can raise
///     <c>CurrentChanged</c> exactly when a subscriber would have to re-parent, and not on the routine
///     re-assertions that <c>Loaded</c> / <c>Appearing</c> produce.
/// </summary>
internal sealed class G9PageHostStack<THost> where THost : class
{
    private readonly Lock _sync = new();

    // Most-recent-first. Small by construction: one entry per live templated page.
    private readonly List<Slot> _slots = [];
    private readonly ConditionalWeakTable<G9PageBase, THost> _hosts = new();

    /// <summary>
    ///     Construct-time registration. Adds <paramref name="page" /> WITHOUT marking it on screen, below
    ///     every entry that is — so it becomes current only when nothing visible outranks it. A page that
    ///     is already registered keeps its position and on-screen state and only has its payload replaced
    ///     (a control template applied a second time).
    /// </summary>
    public bool Register(G9PageBase page, THost host, out THost? current)
    {
        lock (_sync)
        {
            var before = ResolveCurrent();

            _hosts.AddOrUpdate(page, host);
            if (IndexOf(page) < 0)
            {
                var index = 0;
                for (var i = 0; i < _slots.Count; i++)
                {
                    if (_slots[i].OnScreen)
                    {
                        index = i + 1;
                    }
                }

                _slots.Insert(index, new Slot(page));
            }

            current = ResolveCurrent();
            return !EqualityComparer<THost>.Default.Equals(before, current);
        }
    }

    /// <summary>Pushes <paramref name="page" /> (or moves it) to the top and marks it on screen.</summary>
    public bool Activate(G9PageBase page, THost host, out THost? current)
    {
        lock (_sync)
        {
            var before = ResolveCurrent();

            _hosts.AddOrUpdate(page, host);
            var existing = IndexOf(page);
            if (existing >= 0)
            {
                _slots.RemoveAt(existing);
            }

            _slots.Insert(0, new Slot(page) { OnScreen = true });

            current = ResolveCurrent();
            return !EqualityComparer<THost>.Default.Equals(before, current);
        }
    }

    /// <summary>
    ///     Marks <paramref name="page" /> as no longer on screen but keeps its entry, so a page that is
    ///     only transiently unloaded (Android detaches and re-attaches views) is still resolvable when
    ///     nothing else is visible.
    /// </summary>
    public bool MarkOffScreen(G9PageBase page, out THost? current)
    {
        lock (_sync)
        {
            var before = ResolveCurrent();

            var index = IndexOf(page);
            if (index >= 0)
            {
                _slots[index].OnScreen = false;
            }

            current = ResolveCurrent();
            return !EqualityComparer<THost>.Default.Equals(before, current);
        }
    }

    /// <summary>Removes <paramref name="page" />; the entry beneath it becomes resolvable again.</summary>
    public bool Remove(G9PageBase page, out THost? current)
    {
        lock (_sync)
        {
            var before = ResolveCurrent();

            var index = IndexOf(page);
            if (index >= 0)
            {
                _slots.RemoveAt(index);
            }

            _hosts.Remove(page);

            current = ResolveCurrent();
            return !EqualityComparer<THost>.Default.Equals(before, current);
        }
    }

    public bool TryGetCurrent(out THost? host)
    {
        lock (_sync)
        {
            host = ResolveCurrent();
            return host is not null;
        }
    }

    // Caller holds _sync. Walks the WHOLE list (no early return) and prunes collected pages as it goes,
    // so dead slots cannot accumulate beneath a long-lived top entry. The list is a handful of items.
    private THost? ResolveCurrent()
    {
        THost? onScreen = null;
        THost? fallback = null;

        for (var i = 0; i < _slots.Count; i++)
        {
            var slot = _slots[i];
            if (!slot.Page.TryGetTarget(out var page) || !_hosts.TryGetValue(page, out var host))
            {
                _slots.RemoveAt(i--);
                continue;
            }

            if (slot.OnScreen)
            {
                onScreen ??= host;
            }

            fallback ??= host;
        }

        return onScreen ?? fallback;
    }

    // Caller holds _sync.
    private int IndexOf(G9PageBase page)
    {
        for (var i = 0; i < _slots.Count; i++)
        {
            if (_slots[i].Page.TryGetTarget(out var candidate) && ReferenceEquals(candidate, page))
            {
                return i;
            }
        }

        return -1;
    }

    private sealed class Slot(G9PageBase page)
    {
        public WeakReference<G9PageBase> Page { get; } = new(page);

        public bool OnScreen { get; set; }
    }
}
