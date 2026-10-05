namespace G9MAUIControls.TabBar;

/// <summary>
///     Receives the bar outline from <see cref="G9TabBarOutline.Build" />. Two adapters exist — the
///     chrome's <c>PathF</c> and the shadow's <c>SKPath</c> — so the painted bar and its Skia shadow are
///     traced by the SAME code.
/// </summary>
internal interface IG9TabBarOutlineSink
{
    void MoveTo(float x, float y);
    void LineTo(float x, float y);
    void QuadTo(float cx, float cy, float x, float y);
    void CubicTo(float c1X, float c1Y, float c2X, float c2Y, float x, float y);
    void Close();
}

/// <summary>
///     The bar's silhouette — a rounded rectangle with the style's notch carved into its top edge.
/// </summary>
/// <remarks>
///     Until 1.4.0 the chrome drawable and the shadow view each carried their own copy of this path
///     ("mirrors … exactly" in both doc comments). A second notch shape would have doubled that
///     duplication, and the two copies drifting apart is precisely the bug the shadow view's doc warns
///     about (a shadow that fills the notch). One builder, two sinks.
/// </remarks>
internal static class G9TabBarOutline
{
    /// <summary>
    ///     Clamps the notch centre so the whole notch (and the corner radius beside it) fits inside the
    ///     bar. Returns <paramref name="centerX" /> unchanged when the bar is too narrow to clamp.
    /// </summary>
    public static float ClampNotchCenter(
        G9TabBarStyleMetrics metrics,
        float barLeft,
        float barRight,
        float centerX,
        float progress)
    {
        var halfWidth = (float)metrics.NotchHalfWidth * progress;
        var min = barLeft + halfWidth + metrics.BarTopRadius + 2f;
        var max = barRight - halfWidth - metrics.BarTopRadius - 2f;
        return min < max ? Math.Clamp(centerX, min, max) : centerX;
    }

    /// <summary>
    ///     Traces the outline into <paramref name="sink" />. <paramref name="progress" /> (0..1) scales the
    ///     notch from nothing (a flat top edge) to its full size; it is clamped here.
    /// </summary>
    /// <returns>The notch centre actually used (after clamping) — the top-edge highlight needs it.</returns>
    public static float Build(
        IG9TabBarOutlineSink sink,
        G9TabBarStyleMetrics metrics,
        float barLeft,
        float barRight,
        float barTop,
        float barBottom,
        float centerX,
        float progress)
    {
        var p = Math.Clamp(progress, 0f, 1f);
        var topR = metrics.BarTopRadius;
        var botR = metrics.BarBottomRadius;

        sink.MoveTo(barLeft, barTop + topR);
        sink.QuadTo(barLeft, barTop, barLeft + topR, barTop);

        var cx = centerX;
        if (p > 0.001f)
        {
            cx = ClampNotchCenter(metrics, barLeft, barRight, centerX, p);
            AppendNotch(sink, metrics, cx, barTop, p);
        }

        sink.LineTo(barRight - topR, barTop);
        sink.QuadTo(barRight, barTop, barRight, barTop + topR);
        sink.LineTo(barRight, barBottom - botR);
        sink.QuadTo(barRight, barBottom, barRight - botR, barBottom);
        sink.LineTo(barLeft + botR, barBottom);
        sink.QuadTo(barLeft, barBottom, barLeft, barBottom - botR);
        sink.Close();
        return cx;
    }

    /// <summary>
    ///     Appends the notch, left to right, starting with a line along the top edge to its left end.
    ///     Shared with the Classic top-edge highlight, which traces the same curve inset by half a stroke.
    /// </summary>
    public static void AppendNotch(
        IG9TabBarOutlineSink sink,
        G9TabBarStyleMetrics metrics,
        float cx,
        float top,
        float progress)
    {
        if (metrics.NotchShape == G9TabBarNotchShape.Cradle)
        {
            AppendCradle(sink, cx, top, progress * metrics.CradleScale);
            return;
        }

        // Classic: a perfect semicircle (one radius for depth and half-width), kappa-approximated.
        var r = (float)metrics.NotchHalfWidth * progress;
        var control = r * G9TabBarMetrics.CircleArcKappa;
        var leftStart = cx - r;
        var rightEnd = cx + r;
        var bottom = top + r;

        sink.LineTo(leftStart, top);
        sink.CubicTo(leftStart, top + control, cx - control, bottom, cx, bottom);
        sink.CubicTo(cx + control, bottom, rightEnd, top + control, rightEnd, top);
    }

    private static void AppendCradle(IG9TabBarOutlineSink sink, float cx, float top, float scale)
    {
        // Both axes scale with progress, so at 0 the cradle collapses onto the top edge (a flat bar)
        // and the open/close animation grows it from the centre — the same feel as the semicircle.
        // The style's CradleScale rides the same factor: the design path is drawn around a 48 dp disc.
        float X(float dx) => cx + dx * scale;
        float Y(float dy) => top + dy * scale;

        const float hw = G9TabBarStyleMetrics.CradleHalfWidth;
        const float shoulderHandle = G9TabBarStyleMetrics.CradleShoulderHandleX;
        const float innerX = G9TabBarStyleMetrics.CradleShoulderInnerX;
        const float innerY = G9TabBarStyleMetrics.CradleShoulderInnerY;
        const float endX = G9TabBarStyleMetrics.CradleShoulderEndX;
        const float endY = G9TabBarStyleMetrics.CradleShoulderEndY;
        const float bowlX = G9TabBarStyleMetrics.CradleBowlHandleX;
        const float bowlY = G9TabBarStyleMetrics.CradleBowlHandleY;

        sink.LineTo(X(-hw), top);
        sink.CubicTo(X(-shoulderHandle), top, X(-innerX), Y(innerY), X(-endX), Y(endY));
        sink.CubicTo(X(-bowlX), Y(bowlY), X(bowlX), Y(bowlY), X(endX), Y(endY));
        sink.CubicTo(X(innerX), Y(innerY), X(shoulderHandle), top, X(hw), top);
    }
}

/// <summary>Adapts <see cref="G9TabBarOutline" /> to a MAUI Graphics <see cref="PathF" />.</summary>
internal sealed class G9TabBarPathFSink(PathF path) : IG9TabBarOutlineSink
{
    public void MoveTo(float x, float y) => path.MoveTo(x, y);
    public void LineTo(float x, float y) => path.LineTo(x, y);
    public void QuadTo(float cx, float cy, float x, float y) => path.QuadTo(cx, cy, x, y);

    public void CubicTo(float c1X, float c1Y, float c2X, float c2Y, float x, float y) =>
        path.CurveTo(c1X, c1Y, c2X, c2Y, x, y);

    public void Close() => path.Close();
}
