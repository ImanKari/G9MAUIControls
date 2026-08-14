namespace G9MAUIControls.TabBar;

/// <summary>
///     Bold, smooth + glyph drawn with rounded line caps so the FAB icon stays
///     crisp at any size. Rotation (FAB → close) is applied on the host
///     <see cref="GraphicsView" />, so this drawable only renders the +.
/// </summary>
internal sealed class G9FabPlusIconDrawable : IDrawable
{
    public Color Color { get; set; } = Colors.White;
    public float ArmRatio { get; set; } = (float)G9TabBarMetrics.FabPlusArmRatio;
    public float StrokeRatio { get; set; } = (float)G9TabBarMetrics.FabPlusStrokeRatio;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var w = dirtyRect.Width;
        var h = dirtyRect.Height;
        if (w <= 0f || h <= 0f) return;

        var size = MathF.Min(w, h);
        var cx = w / 2f;
        var cy = h / 2f;
        var armLen = size * ArmRatio;
        var stroke = MathF.Max(2f, size * StrokeRatio);

        canvas.Antialias = true;
        canvas.StrokeColor = Color;
        canvas.StrokeSize = stroke;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;

        canvas.DrawLine(cx - armLen, cy, cx + armLen, cy);
        canvas.DrawLine(cx, cy - armLen, cx, cy + armLen);
    }
}
