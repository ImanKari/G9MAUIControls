using static G9MAUIControls.TabBar.G9TabBarMetrics;

namespace G9MAUIControls.TabBar;

/// <summary>
///     Paints the bar surface (in the active <see cref="G9TabBarStyle" />), its outline, the platform
///     <c>SetShadow</c> drop, and — Classic only — a 1px inset top-edge "glass" highlight so the bar
///     reads as a lit, separated plane.
///     Vertical breathing room for the upward soft shadow comes from
///     <see cref="G9TabBarMetrics.ChromeShadowPadding" />, which grows the reserved
///     canvas height above the bar without moving the visible bar (the bar is bottom-
///     anchored in the control via <c>VerticalOptions="End"</c>).
/// </summary>
internal sealed class G9TabBarChromeDrawable : IDrawable
{
    public float LayoutWidth { get; set; }
    public float LayoutHeight { get; set; }
    public float CenterProgress { get; set; }
    public float OpenProgress { get; set; }
    public float NotchCenterX { get; set; }

    public Color BarColor { get; set; } = Colors.Black;
    public Color BarStrokeColor { get; set; } = Colors.White.WithAlpha(0.08f);
    /// <summary>
    ///     Set by <see cref="G9TabBar.ApplyTheme" /> to the theme-aware highlight color.
    ///     Skipped when fully transparent.
    /// </summary>
    public Color BarTopHighlightColor { get; set; } = Colors.White.WithAlpha(0f);
    public Color ShadowColor { get; set; } = Colors.Black;

    /// <summary>The style whose bar, notch and radii are drawn. Set by <see cref="G9TabBar" />.</summary>
    public G9TabBarStyleMetrics Metrics { get; set; } = G9TabBarStyleMetrics.Classic;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var width = LayoutWidth > 0 ? LayoutWidth : dirtyRect.Width;
        var height = LayoutHeight > 0 ? LayoutHeight : dirtyRect.Height;
        if (width <= 0f || height <= 0f) return;

        // The visible bar is inset INSIDE the control on all four sides so the SkiaSharp
        // drop shadow has in-bounds room to render its blur on every edge (see
        // G9TabBarMetrics.BarBottomGap / ChromeShadowPadding / BarHorizontalGap).
        // barLeft/barRight/barTop/barBottom are therefore the drawn bar's edges, NOT the
        // control's edges.
        var barLeft = (float)BarHorizontalGap;
        var barRight = width - (float)BarHorizontalGap;
        var barBottom = MathF.Max(0f, height - (float)BarBottomGap);
        var barTop = MathF.Max(0f, barBottom - (float)Metrics.BarHeight);
        var centerX = NotchCenterX > 0f ? NotchCenterX : (barLeft + barRight) / 2f;

        DrawBar(canvas, barLeft, barRight, barBottom, barTop, centerX);
    }

    private void DrawBar(ICanvas canvas, float barLeft, float barRight, float barBottom, float barTop, float centerX)
    {
        var progress = Math.Clamp(CenterProgress, 0f, 1f);

        // One outline for both styles and for the Skia shadow (G9TabBarOutline) — the notch shape
        // (semicircle / cradle) comes from the style's metrics.
        var path = new PathF();
        var clampedCenterX = G9TabBarOutline.Build(
            new G9TabBarPathFSink(path), Metrics, barLeft, barRight, barTop, barBottom, centerX, progress);

        canvas.SaveState();
        canvas.SetShadow(new SizeF(0f, BarShadowOffsetY), BarShadowBlur, ShadowColor.WithAlpha(BarShadowAlpha));
        canvas.FillColor = BarColor;
        canvas.FillPath(path);
        canvas.SetShadow(new SizeF(0f, 0f), 0f, Colors.Transparent);

        canvas.StrokeColor = BarStrokeColor;
        canvas.StrokeSize = BarStrokeSize;
        canvas.DrawPath(path);
        canvas.RestoreState();

        // 1px inset hairline along the top edge (and notch curve when open). Skipped if
        // fully transparent so consumers can opt out by setting alpha to 0. Classic only:
        // the Sculpted bar is an opaque, un-lit surface.
        if (Metrics.DrawsTopHighlight && BarTopHighlightColor.Alpha > 0.001f)
        {
            var r = (float)Metrics.NotchHalfWidth * progress;
            DrawTopHighlight(canvas, barLeft, barRight, barTop, clampedCenterX, r, progress);
        }
    }

    /// <summary>
    ///     Draws a 1px inset hairline along the top of the bar — including the notch
    ///     curve when the FAB is floating. Inset by half the stroke width so the
    ///     highlight sits *inside* the filled bar and reads as a lit upper edge.
    ///     Classic (semicircle) only — see <see cref="G9TabBarStyleMetrics.DrawsTopHighlight" />.
    /// </summary>
    private void DrawTopHighlight(
        ICanvas canvas,
        float barLeft,
        float barRight,
        float barTop,
        float centerX,
        float r,
        float progress)
    {
        var topR = Metrics.BarTopRadius;
        var inset = BarTopHighlightStrokeSize * 0.5f + 0.25f;
        var topY = barTop + inset;

        var highlight = new PathF();
        highlight.MoveTo(barLeft + inset, barTop + topR);
        highlight.QuadTo(barLeft + inset, topY, barLeft + topR, topY);

        if (progress > 0.001f)
        {
            var leftStart = centerX - r;
            var rightEnd = centerX + r;
            var notchBottom = barTop + r;
            var control = r * CircleArcKappa;

            highlight.LineTo(leftStart, topY);
            highlight.CurveTo(
                leftStart, topY + control,
                centerX - control, notchBottom,
                centerX, notchBottom);
            highlight.CurveTo(
                centerX + control, notchBottom,
                rightEnd, topY + control,
                rightEnd, topY);
        }

        highlight.LineTo(barRight - topR, topY);
        highlight.QuadTo(barRight - inset, topY, barRight - inset, barTop + topR);

        canvas.SaveState();
        canvas.StrokeColor = BarTopHighlightColor;
        canvas.StrokeSize = BarTopHighlightStrokeSize;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawPath(highlight);
        canvas.RestoreState();
    }
}
