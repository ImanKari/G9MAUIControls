using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;

namespace G9MAUIControls.Controls;

/// <summary>
///     Linear progress bar with determinate / indeterminate / paused states, optional
///     segment dots and percent label. Drawable lives in <see cref="G9ProgressBarDrawable" />.
///     // TODO (palette step): track / fill colors will be exposed through G9Palette.
/// </summary>
public partial class G9ProgressBar : G9ControlBase
{
    private readonly Grid _root;
    private readonly GraphicsView _barView;
    private readonly Label _label;
    private readonly G9ProgressBarDrawable _drawable = new();
    private bool _indeterminateRunning;

    // Bumped every time the timer is (re)started or stopped. A tick that belongs to an older
    // generation ends itself, so stop-then-start inside one 16 ms window can never leave two
    // timers running.
    private int _indeterminateGeneration;

    private const string ValueAnimationName = "AppProgressValue";

    [AutoBindable(OnChanged = nameof(OnValueChanged))] private double _value;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _trackColor;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _progressColor;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _cornerRadius;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _barHeight;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9ProgressLabelPlacement _labelPlacement;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _isIndeterminate;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _showSegments;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9ProgressType _progressType;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _isPaused;

    public G9ProgressBar()
    {
        _barView = new GraphicsView
        {
            Drawable = _drawable,
            HeightRequest = G9Metrics.ProgressBarHeight,
            MinimumHeightRequest = G9Metrics.ProgressBarHeight
        };

        _label = new Label
        {
            FontSize = 11,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center,
            IsVisible = false
        };

        _root = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            RowSpacing = 6
        };
        _root.Add(_label, 0, 0);
        _root.Add(_barView, 0, 1);

        Content = _root;

        CornerRadius = G9Metrics.RadiusPill;
        BarHeight = G9Metrics.ProgressBarHeight;
        LabelPlacement = G9ProgressLabelPlacement.None;
        ProgressType = G9ProgressType.Primary;
    }

    private void OnVisualChanged() => RequestVisualUpdate();

    private void OnValueChanged()
    {
        // Visual clamp only — never set Value back to itself to avoid re-entrancy.
        AnimateValue();
        RequestVisualUpdate();
    }

    protected override void OnApplyVisuals()
    {
        var palette = G9Palette.Current;
        var progressColor = ProgressColor ?? G9Visuals.ResolveProgressColor(ProgressType);

        Opacity = IsEnabled ? 1 : 0.45;
        _barView.HeightRequest = BarHeight;
        _barView.MinimumHeightRequest = BarHeight;
        _drawable.TrackColor = TrackColor ?? palette.SurfaceVariant;
        _drawable.ProgressColor = progressColor;
        _drawable.CornerRadius = CornerRadius;
        _drawable.BarHeight = BarHeight;
        _drawable.ShowSegments = ShowSegments;
        _drawable.IsIndeterminate = IsIndeterminate;
        _drawable.IsPaused = IsPaused || !IsEnabled;
        // Never while the value animation is in flight. Every Value change both starts
        // AnimateValue and requests this pass, so writing the target here snapped the bar to its
        // end value one dispatcher tick into the animation and the fill never visibly animated
        // (same rule as G9Switch: the animation owns the value while it runs, and lands on the
        // exact target when it finishes). With no animation running this is the plain seed.
        if (!this.AnimationIsRunning(ValueAnimationName))
        {
            _drawable.Value = (float)Math.Clamp(Value, 0, 1);
        }
        _barView.Invalidate();

        _label.Text = string.Create(G9Culture.CurrentCulture, $"{Math.Round(Math.Clamp(Value, 0, 1) * 100):0}%");
        _label.TextColor = progressColor;
        _label.IsVisible = LabelPlacement != G9ProgressLabelPlacement.None;

        SyncIndeterminateTimer();
    }

    /// <inheritdoc />
    protected override void OnAttachedToLiveTree()
    {
        base.OnAttachedToLiveTree();
        SyncIndeterminateTimer();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromLiveTree()
    {
        StopIndeterminate();
        base.OnDetachedFromLiveTree();
    }

    /// <inheritdoc />
    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(IsVisible)) SyncIndeterminateTimer();
    }

    private void AnimateValue()
    {
        if (IsIndeterminate) return;

        var start = _drawable.Value;
        var target = (float)Math.Clamp(Value, 0, 1);

        new Animation(v =>
        {
            _drawable.Value = (float)v;
            _barView.Invalidate();
        }, start, target, Easing.CubicOut).Commit(this, ValueAnimationName, 16, G9Metrics.ProgressValueDurationMs);
    }

    /// <summary>
    ///     The 60 Hz indeterminate timer runs ONLY while there is something to see: the control is
    ///     live (loaded, handler attached), visible and indeterminate. It used to check
    ///     <see cref="IsIndeterminate" /> alone, so after the page was popped it kept ticking —
    ///     repainting a detached GraphicsView sixty times a second and, because the dispatcher
    ///     holds the callback, keeping the control and its whole page in memory.
    /// </summary>
    private void SyncIndeterminateTimer()
    {
        var shouldRun = IsIndeterminate && IsVisible && IsAttachedToLiveTree && Handler is not null;
        if (!shouldRun)
        {
            StopIndeterminate();
            return;
        }

        if (_indeterminateRunning) return;

        _indeterminateRunning = true;
        var generation = ++_indeterminateGeneration;
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(G9Metrics.IndeterminateFrameMs), () =>
        {
            if (generation != _indeterminateGeneration) return false; // superseded or stopped

            // Re-checked every tick as well, so a stop signal that was somehow missed can never
            // leave the timer running for good.
            if (!IsIndeterminate || !IsVisible || !IsAttachedToLiveTree || Handler is null)
            {
                _indeterminateRunning = false;
                return false;
            }

            if (!IsPaused)
            {
                _drawable.IndeterminateOffset += G9Metrics.IndeterminateStep;
                if (_drawable.IndeterminateOffset > 1)
                {
                    _drawable.IndeterminateOffset = 0;
                }
                _barView.Invalidate();
            }

            return true;
        });
    }

    private void StopIndeterminate()
    {
        if (!_indeterminateRunning) return;
        _indeterminateRunning = false;
        _indeterminateGeneration++;
    }
}
