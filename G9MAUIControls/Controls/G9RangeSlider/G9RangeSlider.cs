using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;

namespace G9MAUIControls.Controls;

/// <summary>
///     Single- or two-thumb slider, RTL-aware. Replaces SfRangeSlider.
///     RTL note: the host <see cref="GraphicsView" /> is forced to <c>FlowDirection.LeftToRight</c>
///     so canvas pixel coordinates do not mirror; we then handle RTL inversion ourselves
///     when mapping value → x and x → value. This is the only way to get consistent drag
///     behavior across MAUI Windows / Android / iOS — without this fix, Windows mirrors
///     the canvas which combined with our manual inversion produced reversed dragging.
///     // TODO (palette step): track / fill / tooltip colors will move to G9Palette.
/// </summary>
public partial class G9RangeSlider : G9ControlBase
{
    private readonly GraphicsView _view;
    private readonly G9RangeSliderDrawable _drawable = new();
    private G9RangeSliderThumb _activeThumb = G9RangeSliderThumb.None;
    private bool _normalizing;
    private bool _normalizeScheduled;

    [AutoBindable(OnChanged = nameof(OnBoundsChanged))] private double _minimum;
    [AutoBindable(OnChanged = nameof(OnBoundsChanged))] private double _maximum;
    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnValueChanged))] private double _value;
    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnValueChanged))] private double _rangeStart;
    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnValueChanged))] private double _rangeEnd;
    [AutoBindable(OnChanged = nameof(OnBoundsChanged))] private double _step;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9RangeSliderMode _mode;
    /// <summary>
    ///     Toggles the min / max edge labels rendered below the track. When false the
    ///     slider collapses its canvas height to just the thumb circle plus a small
    ///     bottom gap — useful in tightly-stacked rows or when an external Label binds
    ///     to <see cref="Value" /> / <see cref="RangeStart" /> / <see cref="RangeEnd" />
    ///     and the min / max are not user-relevant.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnShowLabelsChanged))] private bool _showLabels;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _valueFormat;

    public G9RangeSlider()
    {
        _view = new GraphicsView
        {
            Drawable = _drawable,
            // Force LTR on the canvas so pixel x=0 always means the visual left edge,
            // regardless of the parent's FlowDirection. Touch X-coordinates from
            // GraphicsView interaction events are in canvas-local pixels, which under
            // an inherited RTL parent get mirrored relative to the painted content
            // unless we pin the FlowDirection here. Forcing LTR keeps the touch X
            // and the painted geometry in the same coordinate system on every platform.
            FlowDirection = FlowDirection.LeftToRight
        };

        _view.StartInteraction += OnStartInteraction;
        _view.DragInteraction += OnDragInteraction;
        _view.EndInteraction += OnEndInteraction;
        _view.CancelInteraction += OnCancelInteraction;

        Content = _view;

        Maximum = 100;
        Step = 1;
        Mode = G9RangeSliderMode.Range;
        ShowLabels = true;
        ValueFormat = "0";
        ApplyCanvasHeight();
    }

    public event EventHandler<double>? ValueChanged;

    private void OnVisualChanged() => RequestVisualUpdate();

    /// <summary>
    ///     Recomputes the GraphicsView's HeightRequest when <see cref="ShowLabels" />
    ///     toggles. With the labels visible the canvas keeps the original
    ///     <see cref="G9Metrics.SliderHeight" /> (room for the thumb + active
    ///     drag bubble + min/max edge labels). Without them it collapses to
    ///     <see cref="G9Metrics.SliderHeightCompact" /> — just the thumb circle
    ///     plus a small bottom gap — which keeps stacked sliders tight.
    /// </summary>
    private void OnShowLabelsChanged()
    {
        ApplyCanvasHeight();
        RequestVisualUpdate();
    }

    private void ApplyCanvasHeight()
    {
        var height = ShowLabels
            ? G9Metrics.SliderHeight
            : G9Metrics.SliderHeightCompact;
        _view.HeightRequest = height;
        _view.MinimumHeightRequest = height;
    }

    /// <summary>Minimum / Maximum / Step moved: the stored values may no longer fit them.</summary>
    private void OnBoundsChanged()
    {
        ScheduleNormalize();
        RequestVisualUpdate();
    }

    private void OnValueChanged()
    {
        ScheduleNormalize();
        // Report what the slider SHOWS. The raw property may briefly hold a value the bounds
        // have not caught up with (see ScheduleNormalize); handlers never see that.
        ValueChanged?.Invoke(this, Mode == G9RangeSliderMode.Single ? CoerceValue(Value) : CoerceRange().End);
        RequestVisualUpdate();
    }

    /// <inheritdoc />
    protected override void OnAttachedToLiveTree()
    {
        base.OnAttachedToLiveTree();
        ScheduleNormalize();
    }

    /// <summary>
    ///     The bounds used for every calculation. <see cref="Maximum" /> itself is NEVER corrected:
    ///     an inverted or empty range used to be "fixed" with <c>Maximum = Minimum + 1</c>, which
    ///     overwrote — and, being a local value, detached — the consumer's binding on it.
    /// </summary>
    private (double Min, double Max) EffectiveBounds =>
        Maximum > Minimum ? (Minimum, Maximum) : (Minimum, Minimum + 1);

    private double CoerceValue(double value)
    {
        var (min, max) = EffectiveBounds;
        return Snap(Math.Clamp(value, min, max));
    }

    private (double Start, double End) CoerceRange()
    {
        var start = CoerceValue(RangeStart);
        var end = CoerceValue(RangeEnd);
        return start <= end ? (start, end) : (end, start);
    }

    /// <summary>
    ///     Queues the write-back of clamped / snapped / reordered values for the NEXT dispatcher
    ///     turn instead of doing it inside the property-changed callback.
    ///     <para>
    ///         Bindings are applied one property at a time, in XAML order. Normalizing inline meant
    ///         that when <see cref="Value" /> arrived before <see cref="Maximum" /> it was clamped
    ///         against the default 0–100 and — the property being two-way — the clamped number was
    ///         written straight back into the view model, silently destroying the stored value.
    ///         A whole binding pass runs in one call stack, so by the next turn every bound has
    ///         arrived and the clamp is against the real range. Painting does not wait for any of
    ///         this: <see cref="OnApplyVisuals" /> always coerces for display.
    ///     </para>
    /// </summary>
    private void ScheduleNormalize()
    {
        if (_normalizing || _normalizeScheduled || !NeedsNormalization()) return;

        _normalizeScheduled = true;
        Dispatcher.Dispatch(() =>
        {
            _normalizeScheduled = false;
            NormalizeValues();
        });
    }

    /// <summary>Pure arithmetic — keeps a drag (whose values are already normal) from queuing work per pointer event.</summary>
    private bool NeedsNormalization()
    {
        if (CoerceValue(Value) != Value) return true;
        var (start, end) = CoerceRange();
        return start != RangeStart || end != RangeEnd;
    }

    /// <summary>
    ///     Reorders / clamps / snaps stored values without recursing through the OnChanged callback.
    ///     Only ever writes while the control is live and the bounds describe a real range — until
    ///     then the consumer's values are left exactly as they set them.
    /// </summary>
    private void NormalizeValues()
    {
        if (_normalizing) return;
        if (!IsAttachedToLiveTree) return;
        if (!(Maximum > Minimum)) return;

        _normalizing = true;
        try
        {
            var snappedValue = CoerceValue(Value);
            if (snappedValue != Value) Value = snappedValue;

            var (snappedStart, snappedEnd) = CoerceRange();
            if (snappedStart != RangeStart) RangeStart = snappedStart;
            if (snappedEnd != RangeEnd) RangeEnd = snappedEnd;
        }
        finally
        {
            _normalizing = false;
        }
    }

    private double Snap(double value)
    {
        var (min, max) = EffectiveBounds;
        if (Step <= 0) return value;

        var snapped = min + (Math.Round((value - min) / Step) * Step);
        return Math.Clamp(snapped, min, max);
    }

    protected override void OnApplyVisuals()
    {
        Opacity = IsEnabled ? 1 : 0.45;

        // The drawable only ever sees coerced numbers, so the thumbs, the tooltip and the edge
        // labels are always consistent with each other even while the stored properties are
        // waiting for their deferred normalization.
        var (min, max) = EffectiveBounds;
        var (start, end) = CoerceRange();
        var value = CoerceValue(Value);
        _drawable.Minimum = min;
        _drawable.Maximum = max;
        _drawable.Value = value;
        _drawable.RangeStart = start;
        _drawable.RangeEnd = end;
        _drawable.Mode = Mode;
        _drawable.ShowLabels = ShowLabels;
        _drawable.ValueFormat = ValueFormat;
        // The GraphicsView is pinned to LeftToRight in the constructor, so the canvas never
        // mirrors by itself and the drawable is the single owner of direction — exactly like
        // G9Switch. (This line used to force `false` under a comment claiming the canvas inherited
        // the parent's flow direction; it does not, so the slider simply never inverted in RTL
        // although this control's guide and G9Controls.md §9 both promise that it does.)
        _drawable.IsRtl = G9Visuals.IsRtl;
        _drawable.IsEnabled = IsEnabled;
        _view.Invalidate();

        // Custom-drawn: no role, no value for a screen reader unless we say it. The slider has no
        // label of its own, so the name stays with the consumer; the VALUE is ours to report.
        ApplySemantics(
            null,
            Mode == G9RangeSliderMode.Single
                ? _drawable.FormatValue(value)
                : $"{_drawable.FormatValue(start)} – {_drawable.FormatValue(end)}");
    }

    private void OnStartInteraction(object? sender, TouchEventArgs e)
    {
        if (!IsEnabled || e.Touches.Length == 0) return;

        var point = e.Touches[0];
        _activeThumb = _drawable.ResolveNearestThumb(point.X, (float)_view.Width);
        _drawable.ActiveThumb = _activeThumb;
        // Lock the parent ScrollView (or any other panning ancestor) out of the touch
        // sequence on Android, so the user can drag the thumb left / right with a
        // small amount of vertical drift without the parent claiming the gesture.
        // Without this, MAUI's ScrollView intercepts the move events as soon as the
        // touch path tilts a few degrees off horizontal and the slider drag aborts
        // back to its starting value.
        SetParentDisallowIntercept(true);
        UpdateValueFromPoint(point.X);
    }

    private void OnDragInteraction(object? sender, TouchEventArgs e)
    {
        if (!IsEnabled || _activeThumb == G9RangeSliderThumb.None || e.Touches.Length == 0) return;
        UpdateValueFromPoint(e.Touches[0].X);
    }

    private void OnEndInteraction(object? sender, TouchEventArgs e) => ClearActiveThumb();

    private void OnCancelInteraction(object? sender, EventArgs e) => ClearActiveThumb();

    private void ClearActiveThumb()
    {
        _activeThumb = G9RangeSliderThumb.None;
        _drawable.ActiveThumb = G9RangeSliderThumb.None;
        SetParentDisallowIntercept(false);
        _view.Invalidate();
    }

    /// <summary>
    ///     Tells the platform's parent (a ScrollView, RecyclerView, or any other
    ///     <c>ViewGroup</c> that overrides <c>onInterceptTouchEvent</c>) to keep its
    ///     hands off the current touch sequence on Android. The standard pattern from
    ///     <see href="https://developer.android.com/develop/ui/views/touch-and-input/gestures/scroll" />.
    ///     iOS / Mac Catalyst / Windows handle this themselves — gesture priority on
    ///     those platforms is resolved per-touch, so a custom drag inside a scrolling
    ///     container doesn't need an explicit disallow signal.
    /// </summary>
    private void SetParentDisallowIntercept(bool disallow)
    {
#if ANDROID
        if (_view.Handler?.PlatformView is Android.Views.View nativeView)
        {
            // Walk every ancestor up to the root so even nested scrolling
            // containers (a CollectionView inside a ScrollView, a Border inside
            // a tab content view, etc.) all yield to the slider drag.
            var parent = nativeView.Parent;
            while (parent is not null)
            {
                parent.RequestDisallowInterceptTouchEvent(disallow);
                parent = parent.Parent;
            }
        }
#endif
    }

    private void UpdateValueFromPoint(float x)
    {
        var width = Math.Max(1, _view.Width);
        var value = Snap(_drawable.ValueFromX(x, (float)width));

        if (Mode == G9RangeSliderMode.Single)
        {
            if (Math.Abs(Value - value) > double.Epsilon) Value = value;
        }
        else if (_activeThumb == G9RangeSliderThumb.Start)
        {
            // Against the DISPLAYED other thumb, which is what the finger is being kept off.
            var clamped = Math.Min(value, CoerceRange().End);
            if (Math.Abs(RangeStart - clamped) > double.Epsilon) RangeStart = clamped;
        }
        else if (_activeThumb == G9RangeSliderThumb.End)
        {
            var clamped = Math.Max(value, CoerceRange().Start);
            if (Math.Abs(RangeEnd - clamped) > double.Epsilon) RangeEnd = clamped;
        }

        _view.Invalidate();
    }
}
