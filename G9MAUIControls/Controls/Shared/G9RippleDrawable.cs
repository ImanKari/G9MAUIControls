using Microsoft.Maui.Graphics;

namespace G9MAUIControls.Controls;

internal sealed class G9RippleDrawable : IDrawable
{
    public PointF Center { get; set; } = new(0.5f, 0.5f);
    public float Progress { get; set; }
    public Color Color { get; set; } = Colors.White.WithAlpha(0.18f);

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Progress <= 0) return;

        var maxRadius = MathF.Sqrt((dirtyRect.Width * dirtyRect.Width) + (dirtyRect.Height * dirtyRect.Height));
        var p = Math.Clamp(Progress, 0f, 1f);
        var radius = maxRadius * p;

        canvas.SaveState();
        // Fade through the canvas alpha (which multiplies the colour's own alpha) instead of
        // allocating a new Color per animation frame. Scoped by Save/RestoreState.
        canvas.FillColor = Color;
        canvas.Alpha = 1f - p;
        canvas.FillCircle(
            dirtyRect.Left + (Center.X * dirtyRect.Width),
            dirtyRect.Top + (Center.Y * dirtyRect.Height),
            radius);
        canvas.RestoreState();
    }
}

internal sealed class G9InsetHighlightDrawable : IDrawable
{
    public bool IsVisible { get; set; } = true;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (!IsVisible) return;

        canvas.StrokeSize = 1;
        canvas.StrokeColor = Colors.White.WithAlpha(G9Colors.InsetHighlightTopAlpha);
        canvas.DrawLine(dirtyRect.Left + 1, dirtyRect.Top + 0.5f, dirtyRect.Right - 1, dirtyRect.Top + 0.5f);

        canvas.StrokeColor = Colors.Black.WithAlpha(G9Colors.InsetHighlightBottomAlpha);
        canvas.DrawLine(dirtyRect.Left + 1, dirtyRect.Bottom - 0.5f, dirtyRect.Right - 1, dirtyRect.Bottom - 0.5f);
    }
}
