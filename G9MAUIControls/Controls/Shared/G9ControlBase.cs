using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using System.ComponentModel;

namespace G9MAUIControls.Controls;

/// <summary>
///     Lightweight base for the new app controls. It centralises:
///     <list type="bullet">
///         <item>Theme palette change subscriptions (attached on <c>Loaded</c>, detached on <c>Unloaded</c>).</item>
///         <item>Culture change subscriptions (same lifecycle).</item>
///         <item>A single <see cref="OnApplyVisuals" /> hook that runs on the UI thread.</item>
///         <item>Re-entrancy guarding around the visuals hook so setters that fire visual updates
///         can never recursively re-enter and freeze the UI thread.</item>
///         <item>Dispatcher coalescing so N synchronous property writes (e.g. a language toggle
///         re-applying every label) collapse into ONE apply pass per control instead of N.</item>
///         <item>Lightweight <see cref="OnPaletteChanged" /> / <see cref="OnCultureChangedHook" />
///         hooks so heavy controls can update colours / culture-driven visuals in place
///         without paying for a full <see cref="OnApplyVisuals" /> pass.</item>
///         <item>Off-screen skip — when a global theme / culture event arrives while the
///         control is collapsed inside a hidden parent, we don't run any work; we record
///         what was missed and replay it the moment the control is effectively visible again
///         (see <see cref="ReplayMissedGlobalChanges" />).</item>
///         <item>A small lifetime facility (<see cref="OnAttachedToLiveTree" /> /
///         <see cref="OnDetachedFromLiveTree" /> / <see cref="TrackSubscription" />) so a
///         subclass's external subscriptions and timers live exactly as long as the control is
///         loaded — the same window the palette / culture subscriptions above use.</item>
///     </list>
/// </summary>
public abstract class G9ControlBase : ContentView
{
    private readonly PropertyChangedEventHandler _themeHandler;
    private readonly EventHandler<G9CultureEventArgs> _cultureHandler;
    private bool _attached;
    private bool _applyingVisuals;
    private bool _pendingApply;

    /// <summary>
    ///     Coalescing flag: <c>true</c> while a dispatcher tick is queued to run
    ///     <see cref="ApplyVisualsCore" />. Subsequent calls to
    ///     <see cref="RequestVisualUpdate" /> within the same tick are no-ops; the
    ///     queued tick will pick up everything in one pass. Resets when the queued
    ///     work begins to run.
    ///     <para>
    ///         Without this flag, code paths like <c>ApplyControlsTabTexts</c> on a
    ///         language toggle — which writes ~10 bindable properties on every control
    ///         in sequence — would trigger a full apply pass per write (200+ passes
    ///         per control measured on a dense input showcase). With it, every
    ///         control runs a single apply pass that picks up the latest values.
    ///     </para>
    /// </summary>
    private bool _visualUpdateScheduled;

    /// <summary>
    ///     Set to <c>true</c> when MAUI nulls out the platform handler. Once true the
    ///     control treats itself as torn down: queued visual passes exit immediately and
    ///     no platform property writes happen. Prevents <see cref="ObjectDisposedException" />
    ///     from <c>IServiceProvider</c> when MAUI handlers try to resolve services after
    ///     the host page / window has been disposed.
    /// </summary>
    private bool _isDestroyed;

    /// <summary>
    ///     Set when a theme (resp. culture) change arrived while this control was off-screen
    ///     (window null, not <see cref="VisualElement.IsLoaded" />, or any ancestor with
    ///     <see cref="VisualElement.IsVisible" /> == <c>false</c>). Consumed by
    ///     <see cref="ReplayMissedGlobalChanges" />, which re-runs exactly the hook that was
    ///     skipped once the control can be seen again.
    ///     <para>
    ///         These used to be one write-only flag whose only catch-up was
    ///         <see cref="VisualElement.Loaded" />. Loaded does NOT fire when an ANCESTOR turns
    ///         visible again — an inactive <c>G9TabItem</c>'s content, a collapsed
    ///         <c>G9Expander</c>'s content — so switching theme or language on one tab and
    ///         returning to another left that tab in the old colours and the old reading
    ///         direction until the page was rebuilt.
    ///     </para>
    /// </summary>
    private bool _missedPaletteChange;

    /// <inheritdoc cref="_missedPaletteChange" />
    private bool _missedCultureChange;

    /// <summary>
    ///     The hidden ancestor whose <see cref="VisualElement.IsVisible" /> we are waiting on while
    ///     a global change is pending. Null whenever nothing is pending — so a control that was
    ///     never hidden during a theme / culture change never subscribes to anything.
    /// </summary>
    private VisualElement? _watchedHiddenAncestor;

    /// <summary>Subscriptions handed to <see cref="TrackSubscription" />; disposed on detach.</summary>
    private List<IDisposable>? _trackedSubscriptions;

    // Stamps holding the last semantic description / hint THIS library wrote, so ApplySemantics
    // can tell its own value from one the consumer set and never overwrite the latter.
    private static readonly BindableProperty LibrarySemanticDescriptionProperty =
        BindableProperty.CreateAttached("G9LibrarySemanticDescription", typeof(string), typeof(G9ControlBase), null);

    private static readonly BindableProperty LibrarySemanticHintProperty =
        BindableProperty.CreateAttached("G9LibrarySemanticHint", typeof(string), typeof(G9ControlBase), null);

    protected G9ControlBase()
    {
        _themeHandler = OnThemeChanged;
        _cultureHandler = OnCultureChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        HandlerChanging += OnHandlerChanging;
    }

    /// <summary>
    ///     Override to refresh the visual tree. Always invoked on the UI thread.
    ///     Implementations must not allocate the visual tree here — only push values
    ///     onto pre-built children.
    /// </summary>
    protected abstract void OnApplyVisuals();

    /// <summary>
    ///     Lightweight hook fired specifically when the global theme palette changes.
    ///     Default behaviour is to schedule a full <see cref="OnApplyVisuals" /> pass,
    ///     which is correct but expensive. Subclasses with a heavy
    ///     <see cref="OnApplyVisuals" /> (rebuilding view trees, recreating icons,
    ///     mutating dozens of platform properties) can override and only push the new
    ///     palette colours onto cached children, leaving the rest of the visual state
    ///     untouched. Per-control savings on a theme switch range from 30 ms (small
    ///     icon button) to 250 ms (16-cell PIN entry).
    ///     <para>
    ///         If a subclass overrides this and does NOT call
    ///         <see cref="RequestVisualUpdate" />, it takes full responsibility for
    ///         applying every theme-driven change. <see cref="OnApplyVisuals" /> still
    ///         runs whenever the control's own bindable properties change.
    ///     </para>
    /// </summary>
    protected virtual void OnPaletteChanged()
    {
        RequestVisualUpdate();
    }

    /// <summary>
    ///     Lightweight hook fired specifically when the active culture changes (RTL ↔
    ///     LTR flip, language toggle). Same rationale as <see cref="OnPaletteChanged" /> —
    ///     controls whose visuals genuinely depend on culture (RTL-driven layouts,
    ///     localised glyphs) override and do the minimum work; the rest fall through to
    ///     a coalesced <see cref="RequestVisualUpdate" />.
    /// </summary>
    protected virtual void OnCultureChangedHook()
    {
        RequestVisualUpdate();
    }

    /// <summary>
    ///     Schedule a visuals refresh. Safe to call from any thread; safe against
    ///     re-entrancy from inside <see cref="OnApplyVisuals" />.
    ///     <para>
    ///         The pass is always deferred to a dispatcher tick (even when already on
    ///         the main thread) so multiple synchronous setters coalesce into a single
    ///         apply. See <see cref="_visualUpdateScheduled" /> for the rationale.
    ///     </para>
    /// </summary>
    protected void RequestVisualUpdate()
    {
        if (_isDestroyed) return;

        if (_applyingVisuals)
        {
            _pendingApply = true;
            return;
        }

        if (_visualUpdateScheduled) return;
        _visualUpdateScheduled = true;

        if (MainThread.IsMainThread)
        {
            // Defer to the next dispatcher tick so multiple synchronous setters in
            // the same call stack collapse into one apply pass.
            Dispatcher.Dispatch(RunScheduledApply);
            return;
        }

        MainThread.BeginInvokeOnMainThread(RunScheduledApply);
    }

    private void RunScheduledApply()
    {
        _visualUpdateScheduled = false;
        ApplyVisualsCore();
    }

    private void ApplyVisualsCore()
    {
        if (_applyingVisuals) return;

        // The dispatch may have been queued before Unloaded / HandlerChanging fired.
        // Bail out if the control has been torn down so we don't write platform
        // properties whose mapper would resolve services from a disposed provider.
        if (_isDestroyed || Handler is null) return;

        _applyingVisuals = true;
        try
        {
            do
            {
                _pendingApply = false;
                if (_isDestroyed || Handler is null) break;
                OnApplyVisuals();
            } while (_pendingApply);
        }
        catch (ObjectDisposedException)
        {
            // The handler was disposed mid-pass (page closing). Swallow — the next
            // instance of this control on the next page will start fresh.
        }
        finally
        {
            _applyingVisuals = false;
        }
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        if (_attached) return;

        _attached = true;
        G9Palette.Current.PropertyChanged += _themeHandler;
        G9Culture.CultureChanged += _cultureHandler;

        // While unloaded we were not subscribed at all, so ANY number of theme / culture
        // changes may have gone by. A full apply is the only correct catch-up here, and it
        // supersedes whatever the hidden-state bookkeeping had recorded.
        ClearMissedGlobalChanges();
        RequestVisualUpdate();

        OnAttachedToLiveTree();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        DetachFromLiveTree();
    }

    /// <summary>
    ///     Shared tear-down for <see cref="VisualElement.Unloaded" /> and a nulled handler — the
    ///     two ways a control stops being live. Idempotent: whichever fires first wins.
    /// </summary>
    private void DetachFromLiveTree()
    {
        if (!_attached) return;

        _attached = false;
        G9Palette.Current.PropertyChanged -= _themeHandler;
        G9Culture.CultureChanged -= _cultureHandler;
        StopWatchingHiddenAncestor();

        if (_trackedSubscriptions is { Count: > 0 } tracked)
        {
            // Detach the list first: a Dispose that re-enters TrackSubscription must not
            // mutate the collection being walked.
            _trackedSubscriptions = null;
            foreach (var subscription in tracked)
            {
                try
                {
                    subscription.Dispose();
                }
                catch (Exception)
                {
                    // One failing release must not strand the ones after it — that is the
                    // leak this facility exists to prevent.
                }
            }
        }

        OnDetachedFromLiveTree();
    }

    /// <summary>
    ///     Called once each time the control becomes live (its <see cref="VisualElement.Loaded" />
    ///     fired). Create external subscriptions and timers here and hand them to
    ///     <see cref="TrackSubscription" />; they are released on the matching detach and this
    ///     runs again on the next load, so a control that is unloaded and re-loaded (tab swap,
    ///     scroll recycle) re-subscribes instead of staying deaf.
    /// </summary>
    protected virtual void OnAttachedToLiveTree() { }

    /// <summary>
    ///     Called once each time the control stops being live — on
    ///     <see cref="VisualElement.Unloaded" /> or when its handler is nulled, whichever comes
    ///     first — AFTER every tracked subscription has been disposed. Stop anything that must not
    ///     outlive the page here (a dictation session, a frame timer).
    /// </summary>
    protected virtual void OnDetachedFromLiveTree() { }

    /// <summary><c>true</c> between <see cref="OnAttachedToLiveTree" /> and <see cref="OnDetachedFromLiveTree" />.</summary>
    protected bool IsAttachedToLiveTree => _attached;

    /// <summary>
    ///     Ties <paramref name="subscription" /> to the current live period: it is disposed on the
    ///     next detach. Use it for anything that holds a reference back to this control from
    ///     something longer-lived (a view-model command's <c>CanExecuteChanged</c>, a static
    ///     event, a consumer-owned collection) — those are what keep a popped page in memory.
    ///     <para>
    ///         Only meaningful while attached. Called at any other time the subscription is
    ///         disposed immediately, because there would be no detach left to release it.
    ///     </para>
    /// </summary>
    protected void TrackSubscription(IDisposable subscription)
    {
        ArgumentNullException.ThrowIfNull(subscription);

        if (!_attached)
        {
            subscription.Dispose();
            return;
        }

        (_trackedSubscriptions ??= []).Add(subscription);
    }

    /// <summary>
    ///     Mark the control as torn down right before MAUI nulls out its platform handler.
    ///     <para>
    ///         <see cref="VisualElement.Unloaded" /> alone is not a reliable signal — the
    ///         framework also raises Unloaded when a control temporarily leaves the visual
    ///         tree (e.g. tab content swap, scroll-recycle), and we still want subsequent
    ///         re-loads to repaint. <c>HandlerChanging</c> with a null new handler fires
    ///         only when the platform view is being disconnected for good (host page
    ///         disposing). At that moment the window's <c>IServiceProvider</c> may already
    ///         be disposed, and any subsequent call to <see cref="OnApplyVisuals" /> would
    ///         crash when the platform mapper tries to resolve services.
    ///     </para>
    /// </summary>
    private void OnHandlerChanging(object? sender, HandlerChangingEventArgs e)
    {
        if (e.NewHandler is null)
        {
            _isDestroyed = true;
            DetachFromLiveTree();
            return;
        }

        // Handler can be re-created after off-screen pre-build or a temporary detach.
        // Without clearing the flag, OnApplyVisuals stays skipped and chrome never paints.
        _isDestroyed = false;
    }

    private void OnThemeChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isDestroyed || !ShouldRefreshOnGlobalEvent())
        {
            _missedPaletteChange = true;
            WatchForReveal();
            return;
        }
        OnPaletteChanged();
    }

    private void OnCultureChanged(object? sender, G9CultureEventArgs e)
    {
        if (_isDestroyed || !ShouldRefreshOnGlobalEvent())
        {
            _missedCultureChange = true;
            WatchForReveal();
            return;
        }
        OnCultureChangedHook();
    }

    /// <summary>
    ///     Re-runs the palette / culture hooks that were skipped while the control could not be
    ///     seen. A no-op unless something is actually pending AND the control is visible now, so
    ///     it is safe — and cheap — to call from every "might have just been revealed" signal.
    /// </summary>
    private void ReplayMissedGlobalChanges()
    {
        if (!_missedPaletteChange && !_missedCultureChange) return;

        if (_isDestroyed || !ShouldRefreshOnGlobalEvent())
        {
            // Still hidden — possibly behind a DIFFERENT ancestor than the one that just
            // turned visible (a collapsed expander inside an inactive tab). Re-aim the watch.
            WatchForReveal();
            return;
        }

        var palette = _missedPaletteChange;
        var culture = _missedCultureChange;
        ClearMissedGlobalChanges();

        if (palette) OnPaletteChanged();
        if (culture) OnCultureChangedHook();
    }

    private void ClearMissedGlobalChanges()
    {
        _missedPaletteChange = false;
        _missedCultureChange = false;
        StopWatchingHiddenAncestor();
    }

    /// <summary>
    ///     Subscribes to the nearest hidden ANCESTOR on the way to the root so we hear about the
    ///     reveal. Revealing an ancestor raises nothing on its descendants — no
    ///     <see cref="VisualElement.Loaded" />, no own <c>IsVisible</c> change and, when the
    ///     revealed subtree is arranged at the bounds it already had, no size change either — so
    ///     the ancestor's own property change is the one reliable signal.
    ///     <para>
    ///         The subscription exists only while a change is pending and is dropped on replay
    ///         and on detach. The ancestor lives in the same visual tree as this control, so it
    ///         cannot extend the control's lifetime past the page's.
    ///     </para>
    /// </summary>
    private void WatchForReveal()
    {
        // Not attached → Loaded will run a full apply; nothing to wait for.
        if (!_attached) return;

        VisualElement? hidden = null;
        Element? cursor = this;
        while (cursor is not null)
        {
            if (cursor is VisualElement { IsVisible: false } ve)
            {
                hidden = ve;
                break;
            }
            cursor = cursor.Parent;
        }

        // Own IsVisible is already observed through OnPropertyChanged; and with nothing hidden
        // in the chain the control is merely windowless, which Loaded / the first size
        // allocation cover.
        if (ReferenceEquals(hidden, this)) hidden = null;

        if (ReferenceEquals(hidden, _watchedHiddenAncestor)) return;

        StopWatchingHiddenAncestor();
        if (hidden is null) return;

        _watchedHiddenAncestor = hidden;
        hidden.PropertyChanged += OnWatchedAncestorPropertyChanged;
    }

    private void StopWatchingHiddenAncestor()
    {
        if (_watchedHiddenAncestor is null) return;
        _watchedHiddenAncestor.PropertyChanged -= OnWatchedAncestorPropertyChanged;
        _watchedHiddenAncestor = null;
    }

    private void OnWatchedAncestorPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IsVisible)) return;
        ReplayMissedGlobalChanges();
    }

    /// <summary>
    ///     Returns <c>true</c> when the control is currently rendered to the user. We
    ///     walk the parent chain checking <see cref="VisualElement.IsVisible" /> on every
    ///     ancestor — the framework's own <see cref="VisualElement.IsLoaded" /> only
    ///     reports whether this element is attached to the platform tree, not whether
    ///     a parent has it collapsed (the inactive <c>G9TabItem</c> case sets
    ///     <c>TabContent.IsVisible = false</c>, which leaves children's own
    ///     <c>IsVisible</c> at <c>true</c> but means they're never drawn).
    ///     <para>
    ///         Walking the chain on every theme / culture event is N hops × ~10 ns —
    ///         fast compared to the 30+ ms each control would otherwise spend in
    ///         <see cref="OnApplyVisuals" />.
    ///     </para>
    /// </summary>
    private bool ShouldRefreshOnGlobalEvent()
    {
        if (_isDestroyed) return false;
        if (Window is null) return false;
        if (!IsLoaded) return false;

        Element? cursor = this;
        while (cursor is not null)
        {
            if (cursor is VisualElement ve && !ve.IsVisible) return false;
            cursor = cursor.Parent;
        }
        return true;
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == nameof(IsEnabled) || propertyName == nameof(FlowDirection))
        {
            RequestVisualUpdate();
        }
        else if (propertyName == nameof(IsVisible))
        {
            if (IsVisible)
            {
                // Shown again: catch up on a theme / culture change that arrived meanwhile.
                ReplayMissedGlobalChanges();
            }
            else
            {
                // Becoming invisible (e.g. host tab toggled off) — give subclasses a chance to
                // drop platform focus on their inner content. This prevents WinUI's "bring focused
                // element into view" from scrolling the parent ScrollView to a hidden field when
                // the user later interacts with another tab.
                OnVisibilityLost();
            }
        }
    }

    /// <summary>
    ///     Belt and braces for <see cref="ReplayMissedGlobalChanges" />: a subtree revealed for
    ///     the FIRST time goes from no size to a real one, which also covers the case the
    ///     ancestor watch cannot see (the control had no window yet when the change arrived).
    ///     Costs two boolean reads per allocation when nothing is pending.
    /// </summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        if ((_missedPaletteChange || _missedCultureChange) && width > 0 && height > 0)
        {
            ReplayMissedGlobalChanges();
        }
    }

    /// <summary>Override to release platform focus on inner content when the control hides.</summary>
    protected virtual void OnVisibilityLost() { }

    /// <summary>
    ///     Gives this control an accessible name (and optional state / usage hint) for screen
    ///     readers. Almost every control in the suite is custom-drawn or gesture-driven, so
    ///     without this TalkBack / VoiceOver / Narrator see an anonymous, role-less view.
    ///     <para>
    ///         <b>Never overrides the consumer.</b> A value is only written while the slot is
    ///         empty or still holds what this method wrote last time, so a
    ///         <c>SemanticProperties.Description="…"</c> set in XAML always wins. Unchanged values
    ///         are not re-written, which keeps this safe to call from every
    ///         <see cref="OnApplyVisuals" /> pass.
    ///     </para>
    /// </summary>
    protected void ApplySemantics(string? description, string? hint = null) =>
        ApplySemantics(this, description, hint);

    /// <summary>
    ///     <see cref="ApplySemantics(string?, string?)" /> for an inner element, when the
    ///     interactive part of a control is not its root (the <c>G9Expander</c> header).
    /// </summary>
    protected static void ApplySemantics(BindableObject target, string? description, string? hint = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        WriteSemantic(target, SemanticProperties.DescriptionProperty, LibrarySemanticDescriptionProperty, description);
        WriteSemantic(target, SemanticProperties.HintProperty, LibrarySemanticHintProperty, hint);
    }

    private static void WriteSemantic(
        BindableObject target, BindableProperty property, BindableProperty stampProperty, string? value)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? null : value;
        var current = (string?)target.GetValue(property);
        var lastWritten = (string?)target.GetValue(stampProperty);

        // Something other than this method put a value there → it belongs to the consumer.
        if (!string.IsNullOrEmpty(current) && !string.Equals(current, lastWritten, StringComparison.Ordinal))
        {
            return;
        }

        if (string.Equals(current, normalized, StringComparison.Ordinal)) return;

        target.SetValue(property, normalized);
        target.SetValue(stampProperty, normalized);
    }
}
