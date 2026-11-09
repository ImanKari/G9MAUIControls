using G9MAUIControls.Helpers;
using G9MAUIControls.Localization;
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
    public bool Logarithmic { get; set; }
    public bool ShowThumbValues { get; set; }

    // The fill gradient depends only on the Primary colour and the direction, yet it used to be
    // rebuilt — paint, two stops, a lightened colour — on every paint, i.e. on every pointer
    // event of a drag. Kept until one of the two inputs changes.
    private LinearGradientPaint? _fillPaint;
    private Color? _fillPaintPrimary;
    private bool _fillPaintIsRtl;

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

        // Single mode fills from the MINIMUM edge — the left in LTR, the right in RTL — not from
        // the physical left, or an inverted slider would fill the "remaining" side instead.
        var startX = Mode == G9RangeSliderMode.Single
            ? XFromValue(Minimum, trackWidth, trackLeft)
            : XFromValue(RangeStart, trackWidth, trackLeft);
        var endX = Mode == G9RangeSliderMode.Single
            ? XFromValue(Value, trackWidth, trackLeft)
            : XFromValue(RangeEnd, trackWidth, trackLeft);

        var fillLeft = Math.Min(startX, endX);
        var fillRight = Math.Max(startX, endX);
        var fillWidth = Math.Max(0, fillRight - fillLeft);

        if (fillWidth > 0)
        {
            canvas.SetFillPaint(ResolveFillPaint(palette.Primary), new RectF(fillLeft, trackCenterY - (TrackHeight / 2), fillWidth, TrackHeight));
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

        if (ShowLabels && ShowThumbValues)
        {
            // The design's readout: each thumb's value above it, always (the edge labels are left out).
            canvas.FontSize = 12;
            canvas.FontColor = G9Palette.Current.TextSecondary;
            var y = trackCenterY - ThumbRadius - LabelGap - 18;
            if (Mode == G9RangeSliderMode.Single)
            {
                canvas.DrawString(FormatValue(Value), endX - 50, y, 100, 18, HorizontalAlignment.Center, VerticalAlignment.Bottom);
            }
            else
            {
                canvas.DrawString(FormatValue(RangeStart), startX - 50, y, 100, 18, HorizontalAlignment.Center, VerticalAlignment.Bottom);
                canvas.DrawString(FormatValue(RangeEnd), endX - 50, y, 100, 18, HorizontalAlignment.Center, VerticalAlignment.Bottom);
            }
        }
        else if (ShowLabels)
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

    private LinearGradientPaint ResolveFillPaint(Color primary)
    {
        if (_fillPaint is not null && ReferenceEquals(_fillPaintPrimary, primary) && _fillPaintIsRtl == IsRtl)
        {
            return _fillPaint;
        }

        _fillPaintPrimary = primary;
        _fillPaintIsRtl = IsRtl;
        // Saturated at the minimum end, lighter towards the value — so the gradient runs the
        // same way the values do in either direction.
        _fillPaint = new LinearGradientPaint
        {
            StartPoint = new Point(IsRtl ? 1 : 0, 0),
            EndPoint = new Point(IsRtl ? 0 : 1, 0),
            GradientStops =
            [
                new PaintGradientStop(0, primary),
                new PaintGradientStop(1, G9ColorHelper.Lighten(primary, 0.22))
            ]
        };
        return _fillPaint;
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
        if (Logarithmic && Minimum > 0 && Maximum > Minimum)
            return Math.Exp(Math.Log(Minimum) + (ratio * (Math.Log(Maximum) - Math.Log(Minimum))));
        return Minimum + (ratio * (Maximum - Minimum));
    }

    private float XFromValue(double value, float trackWidth, float trackLeft)
    {
        // The real span, not Math.Max(1, …): that floor made every range narrower than 1 (0–0.5,
        // a 0–1 opacity) draw its thumb short of the end while ValueFromX — which never had the
        // floor — still mapped the full track, so the thumb and the finger disagreed.
        var range = Maximum - Minimum;
        if (range <= 0) range = 1;
        var ratio = Logarithmic && Minimum > 0 && Maximum > Minimum && value > 0
            ? (float)Math.Clamp((Math.Log(value) - Math.Log(Minimum)) / (Math.Log(Maximum) - Math.Log(Minimum)), 0, 1)
            : (float)Math.Clamp((value - Minimum) / range, 0, 1);
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
            if (ShowLabels && !ShowThumbValues)
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

    internal string FormatValue(double value)
    {
        // The library culture, like every other culture-dependent decision in the suite; the
        // thread culture is only what it falls back to.
        var text = value.ToString(string.IsNullOrWhiteSpace(ValueFormat) ? "0" : ValueFormat, G9Culture.CurrentCulture);
        // The canvas draws with the platform font, not the app's (whose Persian font shapes 0-9 as ۰-۹): a Persian or Arabic
        // culture gets its own digits here, so the slider reads like every label around it (1.4.9).
        var language = G9Culture.CurrentCulture.TwoLetterISOLanguageName;
        if (language is "fa" or "ar")
        {
            var zero = language == "fa" ? '۰' : '٠';
            text = string.Create(text.Length, (text, zero), static (span, state) =>
            {
                for (var i = 0; i < span.Length; i++)
                {
                    var c = state.text[i];
                    span[i] = c is >= '0' and <= '9' ? (char)(state.zero + (c - '0')) : c == ',' ? '٬' : c == '.' ? '٫' : c;
                }
            });
        }

        return text;
    }
}
