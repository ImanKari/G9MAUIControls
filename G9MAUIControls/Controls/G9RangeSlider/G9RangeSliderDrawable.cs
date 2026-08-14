using G9MAUIControls.Helpers;
using G9MAUIControls.Theming;
using Microsoft.Maui.Graphics;

namespace G9MAUIControls.Controls;

internal enum G9RangeSliderThumb
{
    None,
    Start,
    End,
    Single
}

internal sealed class G9RangeSliderDrawable : IDrawable
{
    private const float TrackY = (float)G9Metrics.SliderTrackY;
    private const float TrackHeight = (float)G9Metrics.SliderTrackHeight;
    private const float ThumbRadius = (float)G9Metrics.SliderThumbRadius;
    private const float HorizontalInset = (float)G9Metrics.SliderHorizontalInset;
    private const float LabelGap = (float)G9Metrics.SliderLabelGap;

    public double Minimum { get; set; }
    public double Maximum { get; set; } = 100;
    public double Value { get; set; }
    public double RangeStart { get; set; }
    public double RangeEnd { get; set; }
    public G9RangeSliderMode Mode { get; set; }
    public bool ShowLabels { get; set; }
    public string? ValueFormat { get; set; }
    public bool IsRtl { get; set; }
    public bool IsEnabled { get; set; } = true;
    public G9RangeSliderThumb ActiveThumb { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var palette = G9Palette.Current;
        var trackLeft = HorizontalInset;
        var trackRight = dirtyRect.Width - HorizontalInset;
        var trackWidth = Math.Max(1, trackRight - trackLeft);

        // When labels are visible we keep the painted track at the original
        // SliderTrackY anchor (52 dp) so the thumb sits in the upper half of the
        // tall canvas, leaving room for the active drag tooltip above and the
        // min / max labels below. When labels are hidden the canvas is shorter
        // (just thumb + small bottom gap) and we center the track vertically so
        // the row visually balances.
        var trackCenterY = ShowLabels ? TrackY : dirtyRect.Height / 2f;

        canvas.SaveState();
        canvas.FillColor = palette.SurfaceVariant;
        canvas.FillRoundedRectangle(trackLeft, trackCenterY - (TrackHeight / 2), trackWidth, TrackHeight, TrackHeight / 2);

        var startX = Mode == G9RangeSliderMode.Single
            ? trackLeft
            : XFromValue(RangeStart, trackWidth, trackLeft);
        var endX = Mode == G9RangeSliderMode.Single
            ? XFromValue(Value, trackWidth, trackLeft)
            : XFromValue(RangeEnd, trackWidth, trackLeft);

        var fillLeft = Math.Min(startX, endX);
        var fillRight = Math.Max(startX, endX);
        var fillWidth = Math.Max(0, fillRight - fillLeft);

        if (fillWidth > 0)
        {
            var fillPaint = new LinearGradientPaint
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                [
                    new PaintGradientStop(0, palette.Primary),
                    new PaintGradientStop(1, G9ColorHelper.Lighten(palette.Primary, 0.22))
                ]
            };
            canvas.SetFillPaint(fillPaint, new RectF(fillLeft, trackCenterY - (TrackHeight / 2), fillWidth, TrackHeight));
            canvas.FillRoundedRectangle(fillLeft, trackCenterY - (TrackHeight / 2), fillWidth, TrackHeight, TrackHeight / 2);
        }

        if (Mode == G9RangeSliderMode.Single)
        {
            DrawThumb(canvas, endX, trackCenterY, Value, ActiveThumb == G9RangeSliderThumb.Single);
        }
        else
        {
            DrawThumb(canvas, startX, trackCenterY, RangeStart, ActiveThumb == G9RangeSliderThumb.Start);
            DrawThumb(canvas, endX, trackCenterY, RangeEnd, ActiveThumb == G9RangeSliderThumb.End);
        }

        if (ShowLabels)
        {
            // Place labels well below the thumb circle so they don't kiss the edge.
            var labelY = trackCenterY + ThumbRadius + LabelGap;
            canvas.FontSize = 12;
            canvas.FontColor = palette.TextTertiary;
            // Min label sits at the start (visual) edge; max at the end. RTL mirrors below.
            var minText = FormatValue(Minimum);
            var maxText = FormatValue(Maximum);
            if (IsRtl)
            {
                canvas.DrawString(maxText, trackLeft, labelY, 100, 18, HorizontalAlignment.Left, VerticalAlignment.Top);
                canvas.DrawString(minText, trackRight - 100, labelY, 100, 18, HorizontalAlignment.Right, VerticalAlignment.Top);
            }
            else
            {
                canvas.DrawString(minText, trackLeft, labelY, 100, 18, HorizontalAlignment.Left, VerticalAlignment.Top);
                canvas.DrawString(maxText, trackRight - 100, labelY, 100, 18, HorizontalAlignment.Right, VerticalAlignment.Top);
            }
        }

        canvas.RestoreState();
    }

    public G9RangeSliderThumb ResolveNearestThumb(float x, float width)
    {
        var trackWidth = Math.Max(1, width - (HorizontalInset * 2));
        var trackLeft = HorizontalInset;

        if (Mode == G9RangeSliderMode.Single) return G9RangeSliderThumb.Single;

        var start = XFromValue(RangeStart, trackWidth, trackLeft);
        var end = XFromValue(RangeEnd, trackWidth, trackLeft);
        return Math.Abs(x - start) <= Math.Abs(x - end) ? G9RangeSliderThumb.Start : G9RangeSliderThumb.End;
    }

    public double ValueFromX(float x, float width)
    {
        var trackWidth = Math.Max(1, width - (HorizontalInset * 2));
        var ratio = Math.Clamp((x - HorizontalInset) / trackWidth, 0, 1);
        if (IsRtl) ratio = 1 - ratio;
        return Minimum + (ratio * (Maximum - Minimum));
    }

    private float XFromValue(double value, float trackWidth, float trackLeft)
    {
        var range = Math.Max(1, Maximum - Minimum);
        var ratio = (float)Math.Clamp((value - Minimum) / range, 0, 1);
        if (IsRtl) ratio = 1 - ratio;
        return trackLeft + (trackWidth * ratio);
    }

    private void DrawThumb(ICanvas canvas, float x, float centerY, double value, bool active)
    {
        var palette = G9Palette.Current;
        if (active)
        {
            canvas.FillColor = palette.Primary.WithAlpha(G9Colors.SliderTooltipAlphaActive);
            canvas.FillCircle(x, centerY, 26);
            // Drag-state value tooltip pinned to the top of the canvas. We only have
            // the vertical room for it when ShowLabels is on (the canvas is tall in
            // that mode). In compact mode (ShowLabels=false) the canvas is just the
            // thumb circle plus a small bottom gap — there's no space above the
            // thumb for a tooltip, and the consumer is expected to bind an external
            // Label to Value / RangeStart / RangeEnd for the live readout.
            if (ShowLabels)
            {
                DrawTooltip(canvas, x, value);
            }
        }

        // No shadow on the thumb — see G9SwitchDrawable for the rationale. The 2.5px primary
        // stroke already separates the thumb from the track, which is why dropping the 10%-alpha
        // shadow is visually near-invisible here.
        canvas.FillColor = palette.Surface;
        canvas.StrokeColor = palette.Primary;
        canvas.StrokeSize = 2.5f;
        canvas.FillCircle(x, centerY, ThumbRadius);
        canvas.DrawCircle(x, centerY, ThumbRadius);
    }

    private void DrawTooltip(ICanvas canvas, float x, double value)
    {
        var palette = G9Palette.Current;
        var text = FormatValue(value);
        var width = Math.Max(42, text.Length * 9 + 16);
        var left = x - (width / 2);
        var tooltipHeight = 28f;
        // Pinned to the top of the canvas — the canvas is tall enough for this in
        // labels-on mode (SliderHeight = 104 dp; thumb sits at 52 dp center, so the
        // top 24 dp are reserved for the tooltip plus a small gap before the active
        // ring under the thumb starts).
        var tooltipY = 0f;

        canvas.FillColor = palette.InverseSurface;
        canvas.FillRoundedRectangle(left, tooltipY, width, tooltipHeight, 8);
        canvas.FontSize = 12;
        canvas.FontColor = palette.InverseOnSurface;
        canvas.DrawString(text, left, tooltipY, width, tooltipHeight, HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    private string FormatValue(double value)
    {
        return value.ToString(string.IsNullOrWhiteSpace(ValueFormat) ? "0" : ValueFormat);
    }
}
