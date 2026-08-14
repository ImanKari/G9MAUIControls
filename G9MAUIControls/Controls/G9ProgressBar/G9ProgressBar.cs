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
        _drawable.Value = (float)Math.Clamp(Value, 0, 1);
        _barView.Invalidate();

        _label.Text = $"{Math.Round(Value * 100):0}%";
        _label.TextColor = progressColor;
        _label.IsVisible = LabelPlacement != G9ProgressLabelPlacement.None;

        StartIndeterminateIfNeeded();
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
        }, start, target, Easing.CubicOut).Commit(this, "AppProgressValue", 16, G9Metrics.ProgressValueDurationMs);
    }

    private void StartIndeterminateIfNeeded()
    {
        if (!IsIndeterminate || _indeterminateRunning) return;

        _indeterminateRunning = true;
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(G9Metrics.IndeterminateFrameMs), () =>
        {
            if (!_indeterminateRunning || !IsIndeterminate)
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
}
