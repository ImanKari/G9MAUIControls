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
///         control is collapsed inside a hidden parent, we don't run any work; we set a
///         flag and apply once on next <see cref="VisualElement.Loaded" />.</item>
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
    ///     Set to <c>true</c> when a theme or culture change arrived while this control
    ///     was off-screen (window null, not <see cref="VisualElement.IsLoaded" />, or any
    ///     ancestor with <see cref="VisualElement.IsVisible" /> == <c>false</c>). On the
    ///     next <see cref="VisualElement.Loaded" /> we run a single
    ///     <see cref="RequestVisualUpdate" /> so the just-shown control catches up to
    ///     the latest palette / culture state.
    /// </summary>
    private bool _themeOrCultureChangedWhileHidden;

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

        // Catch up if we missed a theme / culture change while off-screen. The flag
        // is set inside the change handlers when the control wasn't rendered; we
        // apply once here so the just-shown control matches the current palette /
        // culture without having to re-run the global event.
        _themeOrCultureChangedWhileHidden = false;
        RequestVisualUpdate();
    }

    private void OnUnloaded(object? sender, EventArgs e)
    {
        if (!_attached) return;

        _attached = false;
        G9Palette.Current.PropertyChanged -= _themeHandler;
        G9Culture.CultureChanged -= _cultureHandler;
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
            if (_attached)
            {
                _attached = false;
                G9Palette.Current.PropertyChanged -= _themeHandler;
                G9Culture.CultureChanged -= _cultureHandler;
            }
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
            _themeOrCultureChangedWhileHidden = true;
            return;
        }
        OnPaletteChanged();
    }

    private void OnCultureChanged(object? sender, G9CultureEventArgs e)
    {
        if (_isDestroyed || !ShouldRefreshOnGlobalEvent())
        {
            _themeOrCultureChangedWhileHidden = true;
            return;
        }
        OnCultureChangedHook();
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
        else if (propertyName == nameof(IsVisible) && !IsVisible)
        {
            // Becoming invisible (e.g. host tab toggled off) — give subclasses a chance to
            // drop platform focus on their inner content. This prevents WinUI's "bring focused
            // element into view" from scrolling the parent ScrollView to a hidden field when
            // the user later interacts with another tab.
            OnVisibilityLost();
        }
    }

    /// <summary>Override to release platform focus on inner content when the control hides.</summary>
    protected virtual void OnVisibilityLost() { }
}
