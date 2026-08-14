using G9MAUIControls.Helpers;
using Microsoft.Maui.Graphics;

namespace G9MAUIControls.Controls;

internal sealed class G9ProgressBarDrawable : IDrawable
{
    public float Value { get; set; }
    public Color TrackColor { get; set; } = Colors.LightGray;
    public Color ProgressColor { get; set; } = Colors.Green;
    public double CornerRadius { get; set; } = G9Metrics.RadiusPill;
    public double BarHeight { get; set; } = G9Metrics.ProgressBarHeight;
    public bool ShowSegments { get; set; }
    public bool IsIndeterminate { get; set; }
    public bool IsPaused { get; set; }
    public float IndeterminateOffset { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var height = (float)Math.Min(BarHeight, dirtyRect.Height);
        if (height <= 0) return;

        var y = dirtyRect.Center.Y - (height / 2);
        var radius = (float)Math.Min(CornerRadius, height / 2);

        canvas.SaveState();
        canvas.FillColor = TrackColor;
        canvas.FillRoundedRectangle(0, y, dirtyRect.Width, height, radius);

        var fillRect = IsIndeterminate
            ? ResolveIndeterminateRect(dirtyRect.Width, y, height)
            : new RectF(0, y, dirtyRect.Width * Math.Clamp(Value, 0, 1), height);

        if (fillRect.Width > 0)
        {
            var gradient = new LinearGradientPaint
            {
                StartPoint = new Point(0, 0),
                EndPoint = new Point(1, 0),
                GradientStops =
                [
                    new PaintGradientStop(0, ProgressColor),
                    new PaintGradientStop(1, G9ColorHelper.Lighten(ProgressColor, 0.24))
                ]
            };
            canvas.SetFillPaint(gradient, fillRect);
            canvas.FillRoundedRectangle(fillRect, radius);

            if (!IsPaused)
            {
                DrawStripes(canvas, fillRect);
            }
        }

        if (ShowSegments)
        {
            canvas.FillColor = Colors.White.WithAlpha(0.50f);
            for (var i = 1; i < 5; i++)
            {
                canvas.FillCircle(dirtyRect.Width * i / 5f, dirtyRect.Center.Y, 1.5f);
            }
        }

        canvas.RestoreState();
    }

    private RectF ResolveIndeterminateRect(float width, float y, float height)
    {
        var fillWidth = width * 0.20f;
        var travel = width - fillWidth;
        var x = travel * IndeterminateOffset;
        return new RectF(x, y, fillWidth, height);
    }

    private void DrawStripes(ICanvas canvas, RectF rect)
    {
        canvas.SaveState();
        canvas.ClipRectangle(rect);
        canvas.StrokeColor = Colors.White.WithAlpha(0.16f);
        canvas.StrokeSize = 5;

        for (var x = rect.Left - rect.Height * 2; x < rect.Right + rect.Height * 2; x += 16)
        {
            canvas.DrawLine(x, rect.Bottom, x + 12, rect.Top);
        }

        canvas.RestoreState();
    }
}
