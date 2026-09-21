using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;

namespace G9MAUIControls.Controls;

/// <summary>
///     Animated on/off switch (replaces Switch / SfSwitch) with optional form-row layout
///     (title + description + trailing toggle), required-mode shake-on-attempted-off,
///     and a single-selection group helper.
///     The progress value is owned exclusively by the toggle animation; <see cref="OnApplyVisuals" />
///     never resets it mid-animation, so the thumb glides smoothly from off to on / on to off.
///     // TODO (palette step): track / thumb colors will be exposed through G9Palette.
/// </summary>
public partial class G9Switch : G9ControlBase
{
    private readonly GraphicsView _switchView;
    private readonly G9SwitchDrawable _drawable = new();
    private readonly Grid _formRow;
    private readonly VerticalStackLayout _textHost;
    private readonly Label _titleLabel;
    private readonly Label _descriptionLabel;
    private bool _initialized;

    // Membership in G9SwitchGroupRegistry, held only while the switch is live AND names a group.
    private IDisposable? _groupRegistration;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnIsOnChanged))]
    private bool _isOn;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _isRequired;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _selectionGroup;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _isInFormRow;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _title;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _description;

    public G9Switch()
    {
        _switchView = new GraphicsView
        {
            Drawable = _drawable,
            WidthRequest = G9Metrics.SwitchWidth,
            HeightRequest = G9Metrics.SwitchHeight,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
            // Pin the canvas to LTR so pixel x=0 always means the visual LEFT edge, exactly as
            // G9RangeSlider does. Under an INHERITED RTL flow direction the platform mirrors the
            // whole canvas, which double-flipped this drawable: it already does its own RTL math
            // (G9SwitchDrawable.IsRtl), so the thumb landed back on the LTR side AND the track
            // check mark was painted as a BACKWARDS tick — a check is a glyph, not a layout, and must
            // never mirror. With the canvas pinned, the drawable's IsRtl branch is the single source
            // of direction and the tick reads identically in both cultures.
            FlowDirection = FlowDirection.LeftToRight
        };

        _titleLabel = new Label
        {
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center
        };
        _descriptionLabel = new Label
        {
            FontSize = 12,
            VerticalTextAlignment = TextAlignment.Center,
            IsVisible = false
        };

        _textHost = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false,
            Children = { _titleLabel, _descriptionLabel }
        };

        _formRow = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            ColumnSpacing = 12,
            HorizontalOptions = LayoutOptions.Center
        };
        _formRow.Add(_textHost, 0);
        _formRow.Add(_switchView, 1);

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);

        Content = _formRow;
    }

    public event EventHandler? Toggled;

    private void OnVisualChanged() => RequestVisualUpdate();

    /// <inheritdoc />
    protected override void OnAttachedToLiveTree()
    {
        base.OnAttachedToLiveTree();
        SyncGroupRegistration();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromLiveTree()
    {
        // The base has already disposed the tracked registration; forget it so the next
        // attach registers afresh.
        _groupRegistration = null;
        base.OnDetachedFromLiveTree();
    }

    /// <summary>
    ///     Keeps the registry entry in step with "live and grouped". Registration used to happen
    ///     only as a side effect of <see cref="OnApplyVisuals" /> and was never undone, so a switch
    ///     on a page that had been left stayed a sibling of every later page's switches for as
    ///     long as the GC let it live.
    /// </summary>
    private void SyncGroupRegistration()
    {
        var wanted = IsAttachedToLiveTree && !string.IsNullOrWhiteSpace(SelectionGroup);
        if (wanted == (_groupRegistration is not null)) return;

        if (wanted)
        {
            _groupRegistration = G9SwitchGroupRegistry.Register(this);
            TrackSubscription(_groupRegistration);
        }
        else
        {
            _groupRegistration?.Dispose();
            _groupRegistration = null;
        }
    }

    private void OnIsOnChanged()
    {
        if (IsOn && !string.IsNullOrWhiteSpace(SelectionGroup))
        {
            G9SwitchGroupRegistry.TurnOffSiblings(this);
        }

        Toggled?.Invoke(this, EventArgs.Empty);
        (Parent as G9SwitchGroup)?.RefreshStatus();
        AnimateToggle(_initialized);
        RequestVisualUpdate();
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled) return;

        if (IsOn && IsRequired)
        {
            await ShakeAsync().ConfigureAwait(true);
            return;
        }

        if (IsOn && Parent is G9SwitchGroup { MinOneActive: true } group && group.ActiveSwitchCount <= 1)
        {
            await ShakeAsync().ConfigureAwait(true);
            return;
        }

        IsOn = !IsOn;
    }

    private void AnimateToggle(bool animate)
    {
        var target = IsOn ? 1f : 0f;

        if (!animate)
        {
            this.AbortAnimation("AppSwitchToggle");
            _drawable.Progress = target;
            _switchView.Invalidate();
            return;
        }

        var start = _drawable.Progress;
        if (Math.Abs(start - target) < 0.0001f)
        {
            return;
        }

        this.AbortAnimation("AppSwitchToggle");
        new Animation(v =>
        {
            _drawable.Progress = (float)v;
            _switchView.Invalidate();
        }, start, target, Easing.SpringOut).Commit(this, "AppSwitchToggle", 16, G9Metrics.SwitchToggleDurationMs);
    }

    private async Task ShakeAsync()
    {
        try
        {
            await this.TranslateToAsync(-4, 0, 60, Easing.CubicInOut).ConfigureAwait(true);
            await this.TranslateToAsync(4, 0, 60, Easing.CubicInOut).ConfigureAwait(true);
            await this.TranslateToAsync(-3, 0, 60, Easing.CubicInOut).ConfigureAwait(true);
            await this.TranslateToAsync(3, 0, 60, Easing.CubicInOut).ConfigureAwait(true);
            await this.TranslateToAsync(0, 0, 70, Easing.CubicInOut).ConfigureAwait(true);
        }
        catch
        {
        }
    }

    protected override void OnApplyVisuals()
    {
        var palette = G9Palette.Current;

        Opacity = IsEnabled ? 1 : 0.45;
        _drawable.IsEnabled = IsEnabled;
        _drawable.IsRtl = G9Visuals.IsRtl;

        // Only seed the progress on the very first apply so subsequent OnIsOn animations
        // are never overridden mid-flight. After init, OnIsOnChanged owns the value.
        if (!_initialized)
        {
            _drawable.Progress = IsOn ? 1 : 0;
            _switchView.Invalidate();
            _initialized = true;
        }

        _titleLabel.Text = Title ?? string.Empty;
        _titleLabel.TextColor = palette.TextPrimary;
        _descriptionLabel.Text = Description ?? string.Empty;
        _descriptionLabel.TextColor = palette.TextTertiary;
        _descriptionLabel.IsVisible = !string.IsNullOrWhiteSpace(Description);

        _textHost.IsVisible = IsInFormRow;
        _formRow.Padding = IsInFormRow ? new Thickness(16, 12) : new Thickness(0);
        _formRow.HorizontalOptions = IsInFormRow ? LayoutOptions.Fill : LayoutOptions.Center;
        _formRow.ColumnDefinitions[0].Width = IsInFormRow ? GridLength.Star : new GridLength(0);

        // SelectionGroup changes arrive here (it is a visual-changed property).
        SyncGroupRegistration();

        // The toggle is painted on a canvas, so a screen reader sees neither a role nor a state.
        // Name = the switch's own title; state = the catalogue's "selected" while on. The
        // catalogue has no dedicated on / off pair yet, so the off state is simply not announced.
        ApplySemantics(
            string.IsNullOrWhiteSpace(Title) ? null : Title,
            IsOn ? G9Strings.Get(G9StringKey.Selected) : null);
    }
}

public partial class G9SwitchGroup : VerticalStackLayout
{
    private readonly Label _statusLabel;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _minOneActive;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _showLiveStatus;

    public G9SwitchGroup()
    {
        Spacing = 0;
        _statusLabel = new Label
        {
            FontSize = 11,
            TextColor = G9Palette.Current.TextTertiary,
            Margin = new Thickness(2, 6, 2, 0)
        };
    }

    public int ActiveSwitchCount => Children.OfType<G9Switch>().Count(static s => s.IsOn);

    internal void RefreshStatus()
    {
        if (!ShowLiveStatus)
        {
            Children.Remove(_statusLabel);
            return;
        }

        if (!Children.Contains(_statusLabel))
        {
            Children.Add(_statusLabel);
        }

        var total = Children.OfType<G9Switch>().Count();
        var active = ActiveSwitchCount;
        _statusLabel.Text = string.Create(G9Culture.CurrentCulture, $"{active} / {total}");
        _statusLabel.TextColor = MinOneActive && active <= 1 ? G9Palette.Current.Warning : G9Palette.Current.TextTertiary;
    }

    private void OnVisualChanged() => RefreshStatus();
}

/// <summary>
///     Finds the other switches a grouped <see cref="G9Switch" /> must turn off.
///     <para>
///         <b>A group name is only unique within one page.</b> The registry is process-wide, but
///         it used to match on the name alone — so two live pages that both used, say,
///         <c>SelectionGroup="unit"</c> shared one group: turning a switch on in one page turned
///         the other page's switch off and, because <see cref="G9Switch.IsOn" /> binds two-way,
///         wrote <c>false</c> into that page's view model. Siblings are now additionally required
///         to share the same scope (see <see cref="ResolveScope" />).
///     </para>
/// </summary>
internal static class G9SwitchGroupRegistry
{
    private static readonly List<WeakReference<G9Switch>> Items = [];
    private static readonly Lock Sync = new();

    /// <summary>Adds <paramref name="sw" />; dispose the result to remove it again.</summary>
    public static IDisposable Register(G9Switch sw)
    {
        lock (Sync)
        {
            Items.RemoveAll(static r => !r.TryGetTarget(out _));
            if (!Items.Any(r => r.TryGetTarget(out var target) && ReferenceEquals(target, sw)))
            {
                Items.Add(new WeakReference<G9Switch>(sw));
            }
        }

        return new Registration(sw);
    }

    private static void Unregister(G9Switch sw)
    {
        lock (Sync)
        {
            Items.RemoveAll(r => !r.TryGetTarget(out var target) || ReferenceEquals(target, sw));
        }
    }

    public static void TurnOffSiblings(G9Switch sw)
    {
        var group = sw.SelectionGroup;
        if (string.IsNullOrWhiteSpace(group)) return;

        List<G9Switch> candidates;
        lock (Sync)
        {
            Items.RemoveAll(static r => !r.TryGetTarget(out _));
            candidates = Items
                .Select(r => r.TryGetTarget(out var t) ? t : null)
                .Where(t => t is not null && t.SelectionGroup == group && !ReferenceEquals(t, sw))
                .Cast<G9Switch>()
                .ToList();
        }

        if (candidates.Count == 0) return;

        // Resolved NOW rather than at registration: a switch is often registered before it has
        // a parent chain (templated cells, off-screen pre-build), and may be re-parented later.
        var scope = ResolveScope(sw);

        foreach (var sibling in candidates)
        {
            if (sibling.IsOn && ReferenceEquals(ResolveScope(sibling), scope))
            {
                sibling.IsOn = false;
            }
        }
    }

    /// <summary>
    ///     The boundary a group name is unique within: the nearest <see cref="Page" /> ancestor,
    ///     or — for content hosted outside any page, such as a sheet on the window overlay — the
    ///     <see cref="Window" />. Null (not in a tree at all) only ever matches null.
    /// </summary>
    private static object? ResolveScope(G9Switch sw)
    {
        Element? cursor = sw.Parent;
        while (cursor is not null)
        {
            if (cursor is Page page) return page;
            cursor = cursor.Parent;
        }

        return sw.Window;
    }

    private sealed class Registration(G9Switch owner) : IDisposable
    {
        private G9Switch? _owner = owner;

        public void Dispose()
        {
            var target = Interlocked.Exchange(ref _owner, null);
            if (target is not null) Unregister(target);
        }
    }
}
