using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using System.Windows.Input;

namespace G9MAUIControls.Controls;

/// <summary>
///     Material-3 check box: a 20dp rounded square that fills with <see cref="G9Palette.Primary" />
///     while a tick draws itself in, an optional indeterminate state, a soft circular press halo,
///     and an optional wrapping label beside it. Replaces the platform <c>CheckBox</c>, which renders
///     three different native widgets on three platforms and follows none of the suite's palette.
///     <para>
///         <b>Hit area vs. paint.</b> The square is 20dp; the control never measures below
///         <see cref="G9LayoutMetrics.MinTouchTarget" /> (48dp) — the box is centred in a 48dp slot,
///         the slop is invisible, and with a <see cref="Text" /> the WHOLE ROW (box and label) is one
///         tap target (<c>G9Controls.md</c> §10b). Every child is <c>InputTransparent</c>; the one
///         gesture owner is the row.
///     </para>
///     <para>
///         <b>Direction.</b> The box sits on the START side and the label follows it, by ordinary
///         <see cref="VisualElement.FlowDirection" /> inheritance — in RTL the box is on the right.
///         The canvas itself is pinned LTR so the tick can never come out mirrored (§9).
///     </para>
///     <para>
///         <b>Animation.</b> One MAUI <see cref="Animation" /> per concern writes a float into the
///         drawable and invalidates the canvas — no layout pass, no allocation per frame. The checked
///         timeline, the tick ↔ bar morph and the press halo are separate named animations, so any of
///         them can be reversed mid-flight from where it is. See ADR-0028.
///     </para>
/// </summary>
public partial class G9CheckBox : G9ControlBase
{
    private const string ProgressAnimationName = "G9CheckBoxProgress";
    private const string MorphAnimationName = "G9CheckBoxMorph";
    private const string HaloAnimationName = "G9CheckBoxHalo";

    /// <summary>
    ///     A tap that arrives within this long of a reported pointer-press already showed its halo
    ///     through the press / release pair. Longer than any tap (the platform tap timeout is ~500ms)
    ///     so the two are recognised as one gesture.
    /// </summary>
    private const int PointerPressWindowMs = 700;

    /// <summary>
    ///     A press whose release never arrives (a parent <c>ScrollView</c> took the gesture over and
    ///     the platform delivered no release / exit) must not leave the halo painted forever. After
    ///     this long the halo lets go by itself; a genuine long hold just sees it fade.
    /// </summary>
    private const int HaloWatchdogMs = 1200;

    private readonly Grid _root;
    private readonly GraphicsView _boxView;
    private readonly G9CheckBoxDrawable _drawable = new();
    private readonly Label _label;

    // Per-frame animation callbacks, built once: an animation is created per toggle, but the
    // delegate it drives each frame does not need to be.
    private readonly Action<double> _progressFrame;
    private readonly Action<double> _morphFrame;
    private readonly Action<double> _haloFrame;

    private bool _initialized;
    private long _lastPointerPressTick = -1;

    /// <summary>
    ///     Bumped by every press, release and pulse. A deferred halo release (the watchdog, the end
    ///     of a pulse) only acts if nothing newer has happened since it was scheduled.
    /// </summary>
    private int _haloGeneration;

    /// <summary>
    ///     Whether the box is ticked. Two-way by default. Changing it — from a tap, from code or
    ///     through a binding — raises <see cref="CheckedChanged" /> and animates the box; only a USER
    ///     toggle (a tap, or <see cref="Toggle" />) also runs <see cref="Command" />.
    /// </summary>
    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnIsCheckedChanged))]
    private bool _isChecked;

    /// <summary>
    ///     Optional third state — "some of them" — drawn as a bar on a filled box. It takes
    ///     precedence over <see cref="IsChecked" /> visually while it is <c>true</c>. Only code (or a
    ///     binding) can set it: a tap on an indeterminate box clears it and CHECKS the box, the
    ///     convention every platform follows. Two-way by default, so a view model sees that clear.
    /// </summary>
    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnIsIndeterminateChanged))]
    private bool _isIndeterminate;

    /// <summary>
    ///     Optional label beside the box, on its END side. Wraps; uses the suite's cultural typeface
    ///     (<see cref="G9Visuals.ResolveCulturalFont" />). Tapping it toggles the box. Also becomes the
    ///     screen-reader name of the control unless <c>SemanticProperties.Description</c> is set.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))]
    private string? _text;

    /// <summary>
    ///     Executed after a USER toggle (tap or <see cref="Toggle" />), once <see cref="IsChecked" /> /
    ///     <see cref="IsIndeterminate" /> already hold the new state — so a command that reads the
    ///     bound view-model value sees the new one. <c>CanExecute</c> gates the command, not the
    ///     toggle. Not executed for changes made by code or a binding.
    /// </summary>
    [AutoBindable]
    private ICommand? _command;

    /// <summary>Passed to <see cref="Command" />.</summary>
    [AutoBindable]
    private object? _commandParameter;

    /// <summary>
    ///     <c>true</c> (default): the control reserves a <c>G9LayoutMetrics.MinTouchTarget</c> (48dp)
    ///     slot around its 20dp box, so a bare check box is comfortably tappable. <c>false</c>: the
    ///     control is exactly the box (plus its label) — for a check box that sits INSIDE a row which
    ///     owns the tap (the list-picker shape, <c>InputTransparent="True"</c>), where the row already
    ///     is the touch target and a 48dp slot only inflates every row.
    /// </summary>
    /// <remarks>
    ///     Added after the first device look (2026-09-27): in the attribute picker the 48dp slot plus the
    ///     card padding made each row ~72dp tall with a wide dead gap before the label — "too much
    ///     height and gap, wasted space". The press halo is drawn inside the canvas, so a compact box
    ///     has none; it needs none, because the row gives the press feedback.
    /// </remarks>
    [AutoBindable(DefaultValue = "true", OnChanged = nameof(OnReserveTouchTargetChanged))]
    private bool _reserveTouchTarget;

    public G9CheckBox()
    {
        var touchTarget = ResolveSlotSize(reserveTouchTarget: true);

        _boxView = new GraphicsView
        {
            Drawable = _drawable,
            WidthRequest = touchTarget,
            HeightRequest = touchTarget,
            // Top, not centre: with a label that wraps, the box stays level with the FIRST line
            // (the label's vertical padding centres that line on the slot) instead of floating to
            // the middle of a paragraph. A single line is centred on the slot either way.
            VerticalOptions = LayoutOptions.Start,
            HorizontalOptions = LayoutOptions.Center,
            BackgroundColor = Colors.Transparent,
            InputTransparent = true,
            // The tick is a glyph, not a layout: an inherited RTL direction would mirror the whole
            // canvas and paint it backwards (G9Controls.md §9, the G9Switch bug).
            FlowDirection = FlowDirection.LeftToRight
        };

        _label = new Label
        {
            FontSize = G9Metrics.CheckBoxLabelFontSize,
            LineBreakMode = LineBreakMode.WordWrap,
            HorizontalTextAlignment = TextAlignment.Start,
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
            Padding = new Thickness(0, G9Metrics.CheckBoxLabelVerticalPadding),
            InputTransparent = true,
            IsVisible = false
        };

        _root = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Auto), new ColumnDefinition(GridLength.Star) },
            ColumnSpacing = 0,
            // Start until there is a label (OnApplyVisuals): a bare box must not turn the whole
            // width of its parent into a tap target.
            HorizontalOptions = LayoutOptions.Start,
            MinimumHeightRequest = touchTarget
        };
        _root.Add(_boxView, 0);
        _root.Add(_label, 1);

        // ONE gesture owner — the row (§10b). The pointer recognizer only drives the halo; the
        // toggle itself happens on Tapped, so a press that turns into a scroll never toggles.
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        _root.GestureRecognizers.Add(tap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerPressed += OnPointerPressed;
        pointer.PointerReleased += OnPointerReleased;
        pointer.PointerExited += OnPointerExited;
        _root.GestureRecognizers.Add(pointer);

        _progressFrame = v =>
        {
            _drawable.Progress = (float)v;
            _boxView.Invalidate();
        };
        _morphFrame = v =>
        {
            _drawable.Morph = (float)v;
            _boxView.Invalidate();
        };
        _haloFrame = v =>
        {
            _drawable.PressProgress = (float)v;
            _boxView.Invalidate();
        };

        Content = _root;
    }

    /// <summary>
    ///     Raised whenever <see cref="IsChecked" /> changes — from a tap, from code or through a
    ///     binding — with the new value. Same argument type as the platform <c>CheckBox</c>, so a
    ///     handler moves across unchanged.
    /// </summary>
    public event EventHandler<CheckedChangedEventArgs>? CheckedChanged;

    /// <summary>
    ///     Does what a tap does: an indeterminate box becomes checked, otherwise the checked state
    ///     flips; then <see cref="Command" /> runs. Ignored while the control is disabled — it is the
    ///     USER's action, for callers that stand in for the user (keyboard handling, UI automation).
    ///     To set a state from code, assign <see cref="IsChecked" /> / <see cref="IsIndeterminate" />.
    /// </summary>
    public void Toggle()
    {
        if (!IsEnabled) return;

        var (isChecked, isIndeterminate) = G9CheckBoxMath.NextOnTap(IsChecked, IsIndeterminate);

        // Clear the third state FIRST, so a CheckedChanged handler (raised by the IsChecked write)
        // already sees a settled, two-state box.
        IsIndeterminate = isIndeterminate;
        IsChecked = isChecked;

        // Guard 0: every tap on a check box is a real toggle — swallowing a quick second tap would
        // leave the box in the state the user just tapped away from. G9Press still gives the
        // command the "never crash" half of its contract.
        G9Press.Invoke(this, null, Command, CommandParameter, feedback: null, guardMs: 0);
    }

    private void OnVisualChanged() => RequestVisualUpdate();

    private void OnIsCheckedChanged()
    {
        UpdateMark(_initialized);
        CheckedChanged?.Invoke(this, new CheckedChangedEventArgs(IsChecked));
        RequestVisualUpdate();
    }

    private void OnIsIndeterminateChanged()
    {
        UpdateMark(_initialized);
        RequestVisualUpdate();
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled) return;

        // Pointer events are not delivered for every input on every platform. When this tap came
        // without a press we saw, flash the halo so the touch still reads as registered.
        var pressSeen = _lastPointerPressTick >= 0
                        && Environment.TickCount64 - _lastPointerPressTick < PointerPressWindowMs;
        if (!pressSeen)
        {
            PulseHalo();
        }

        Toggle();
    }

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        if (!IsEnabled) return;

        _lastPointerPressTick = Environment.TickCount64;
        var generation = ++_haloGeneration;
        AnimateHalo(1f, G9Metrics.PressDurationMs, null);

        // Watchdog — see HaloWatchdogMs.
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(HaloWatchdogMs), () =>
        {
            if (generation == _haloGeneration)
            {
                ReleaseHalo();
            }
        });
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e) => ReleaseHalo();

    private void OnPointerExited(object? sender, PointerEventArgs e) => ReleaseHalo();

    private void ReleaseHalo()
    {
        _haloGeneration++;
        AnimateHalo(0f, G9Metrics.ReleaseDurationMs, null);
    }

    private void PulseHalo()
    {
        var generation = ++_haloGeneration;
        AnimateHalo(1f, G9Metrics.PressDurationMs, () =>
        {
            if (generation == _haloGeneration)
            {
                AnimateHalo(0f, G9Metrics.ReleaseDurationMs, null);
            }
        });
    }

    /// <summary>
    ///     Moves the drawable's checked timeline and mark shape to where the current state says they
    ///     rest — animated once the control has painted, snapped before that (and whenever there is
    ///     no handler to run an animation on).
    /// </summary>
    private void UpdateMark(bool animate)
    {
        var targetProgress = G9CheckBoxMath.TargetProgress(IsChecked, IsIndeterminate);
        var targetMorph = G9CheckBoxMath.TargetMorph(IsIndeterminate);

        if (!animate || Handler is null)
        {
            SnapMark(targetProgress, targetMorph);
            return;
        }

        // The mark's SHAPE only animates while it is visible and staying visible. When it is not
        // showing yet there is nothing to morph: snap it, so unchecked → indeterminate draws the
        // bar in rather than a tick that bends into one. When it is going away, leave the shape
        // alone: a disappearing bar should not turn into a tick on its way out.
        if (_drawable.Progress < 0.01f)
        {
            this.AbortAnimation(MorphAnimationName);
            _drawable.Morph = targetMorph;
        }
        else if (targetProgress > 0f)
        {
            Animate(MorphAnimationName, _morphFrame, _drawable.Morph, targetMorph,
                G9Metrics.CheckBoxMorphDurationMs, Easing.CubicInOut, null);
        }

        Animate(ProgressAnimationName, _progressFrame, _drawable.Progress, targetProgress,
            G9Metrics.CheckBoxToggleDurationMs, Easing.CubicOut, null);
    }

    private void SnapMark(float progress, float morph)
    {
        this.AbortAnimation(ProgressAnimationName);
        this.AbortAnimation(MorphAnimationName);
        _drawable.Progress = progress;
        _drawable.Morph = morph;
        _boxView.Invalidate();
    }

    private void AnimateHalo(float target, uint durationMs, Action? completed)
    {
        if (Handler is null)
        {
            this.AbortAnimation(HaloAnimationName);
            _drawable.PressProgress = 0f;
            _boxView.Invalidate();
            return;
        }

        Animate(HaloAnimationName, _haloFrame, _drawable.PressProgress, target, durationMs, Easing.CubicOut, completed);
    }

    /// <summary>
    ///     Runs one named animation from where the value IS to where it should be, replacing any
    ///     in-flight animation of the same name — which is what makes every transition reversible
    ///     mid-way. The duration scales with the distance left, so turning around half-way takes
    ///     half the time instead of replaying a full-length curve over a short path.
    /// </summary>
    private void Animate(
        string name,
        Action<double> frame,
        float from,
        float to,
        uint fullDurationMs,
        Easing easing,
        Action? completed)
    {
        this.AbortAnimation(name);

        var distance = Math.Abs(to - from);
        if (distance < 0.001f)
        {
            frame(to);
            completed?.Invoke();
            return;
        }

        var length = (uint)Math.Max(1d, Math.Round(fullDurationMs * (double)Math.Min(1f, distance)));

        // A superseded animation reports cancelled = true; only a completed one hands over.
        Action<double, bool>? finished = null;
        if (completed is not null)
        {
            finished = (_, cancelled) =>
            {
                if (!cancelled) completed();
            };
        }

        new Animation(frame, from, to, easing).Commit(this, name, rate: 16, length: length, finished: finished);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     The canvas reads <see cref="G9Palette.Current" /> on every paint, so a theme switch costs a
    ///     repaint plus one label colour — never a full <see cref="OnApplyVisuals" /> pass.
    /// </remarks>
    protected override void OnPaletteChanged()
    {
        if (Handler is null) return;

        ApplyLabelColor(G9Palette.Current);
        _boxView.Invalidate();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromLiveTree()
    {
        // Nothing may keep animating a page that has gone, and a halo caught mid-press must not
        // be waiting on screen when the control comes back (tab swap, recycled cell).
        _haloGeneration++;
        this.AbortAnimation(HaloAnimationName);
        _drawable.PressProgress = 0f;
        SnapMark(
            G9CheckBoxMath.TargetProgress(IsChecked, IsIndeterminate),
            G9CheckBoxMath.TargetMorph(IsIndeterminate));
        base.OnDetachedFromLiveTree();
    }

    protected override void OnApplyVisuals()
    {
        var palette = G9Palette.Current;

        _drawable.IsEnabled = IsEnabled;

        // Seed the timeline on the very first pass only, so a later pass (theme, culture, text)
        // never yanks a running animation to its end — the G9Switch rule (§8).
        if (!_initialized)
        {
            _initialized = true;
            SnapMark(
                G9CheckBoxMath.TargetProgress(IsChecked, IsIndeterminate),
                G9CheckBoxMath.TargetMorph(IsIndeterminate));
        }

        var hasText = !string.IsNullOrWhiteSpace(Text);
        var labelText = hasText ? Text! : string.Empty;
        if (!string.Equals(_label.Text, labelText, StringComparison.Ordinal)) _label.Text = labelText;
        if (_label.IsVisible != hasText) _label.IsVisible = hasText;

        var font = G9Visuals.ResolveCulturalFont();
        if (!string.Equals(_label.FontFamily, font, StringComparison.Ordinal)) _label.FontFamily = font;

        ApplyLabelColor(palette);

        // With a label the row is the tap target (and wraps inside the parent's width); without
        // one the control is just its 48dp slot.
        var rowOptions = hasText ? LayoutOptions.Fill : LayoutOptions.Start;
        if (_root.HorizontalOptions != rowOptions) _root.HorizontalOptions = rowOptions;
        var spacing = hasText ? G9Metrics.CheckBoxLabelSpacing : 0;
        if (Math.Abs(_root.ColumnSpacing - spacing) > 0.01) _root.ColumnSpacing = spacing;

        _boxView.Invalidate();

        // Custom-drawn, so a screen reader sees nothing unless we say it: the label is the name and
        // the state is always announced — including "not checked", which a check box (unlike a
        // selected row) must say out loud, or the user cannot tell an empty box from a missing one.
        ApplySemantics(hasText ? Text : null, G9Strings.Get(ResolveStateKey()));
    }

    private G9StringKey ResolveStateKey()
    {
        if (IsIndeterminate) return G9StringKey.PartiallyChecked;
        return IsChecked ? G9StringKey.Checked : G9StringKey.NotChecked;
    }

    private void ApplyLabelColor(G9Palette palette)
    {
        var color = IsEnabled ? palette.TextPrimary : palette.TextDisabled;
        if (_label.TextColor != color) _label.TextColor = color;
    }

    /// <summary>The square the box is drawn in: the 48dp touch slot, or just the box.</summary>
    private static double ResolveSlotSize(bool reserveTouchTarget) =>
        reserveTouchTarget ? G9LayoutMetrics.MinTouchTarget : G9Metrics.CheckBoxSize;

    private void OnReserveTouchTargetChanged()
    {
        var slot = ResolveSlotSize(ReserveTouchTarget);
        _boxView.WidthRequest = slot;
        _boxView.HeightRequest = slot;
        _root.MinimumHeightRequest = slot;

        // A compact box is level with a one-line label by centring, not by the label's 12dp padding
        // (that padding exists to centre the first line on a 48dp slot).
        _boxView.VerticalOptions = ReserveTouchTarget ? LayoutOptions.Start : LayoutOptions.Center;
        _label.Padding = ReserveTouchTarget
            ? new Thickness(0, G9Metrics.CheckBoxLabelVerticalPadding)
            : Thickness.Zero;

        _boxView.Invalidate();
    }
}
