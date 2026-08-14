using Microsoft.Maui.Graphics;

namespace G9MAUIControls.Controls;

/// <summary>
///     Material-3 style outlined-field outline drawable.
///     <para>
///         Paints a rounded-rectangle stroke with an optional notch (gap) along the top
///         edge so a floating label can sit on the border without needing a parent-matching
///         background fill. The same control therefore renders correctly on any parent
///         background — no <c>LabelBackgroundColor</c> tuning required.
///     </para>
///     <para>
///         For focus / error / status emphasis we thicken the SAME outline stroke. Drawing
///         an outer ring caused two visible problems:
///         <list type="bullet">
///             <item><description>The ring extended past the canvas rect and got clipped by
///             the parent <see cref="GraphicsView" />, leaving a thin sliver hugging the
///             inside of the box.</description></item>
///             <item><description>The clipped sliver appeared as a small horizontal line
///             below the field on focus / status, especially on Windows.</description></item>
///         </list>
///         A thicker outline avoids both — it stays inside the rect at all sizes and
///         matches the Material 3 focus emphasis pattern (Material specifies a 2 dp focus
///         outline vs the 1 dp resting outline). A focused field can also paint a soft
///         halo on the inside of the same path, so it still looks like a two-stroke focus
///         state without exceeding the GraphicsView bounds.
///     </para>
/// </summary>
internal sealed class G9OutlinedFieldDrawable : IDrawable
{
    public Color StrokeColor { get; set; } = Colors.Gray;
    public float StrokeThickness { get; set; } = 1.5f;
    public float CornerRadius { get; set; } = 12;
    public bool ShowNotch { get; set; }
    public float NotchLeft { get; set; }
    public float NotchRight { get; set; }
    public Color? HaloStrokeColor { get; set; }
    public float HaloStrokeThickness { get; set; }

    /// <summary>
    ///     When set to a non-zero positive value, the outline stroke is rendered at this
    ///     thickness instead of <see cref="StrokeThickness" />. Used by the host to apply
    ///     a focus / error / status emphasis without painting a separate ring.
    ///     <para>
    ///         Setting this back to <c>0</c> reverts to the regular stroke.
    ///     </para>
    /// </summary>
    public float EmphasisStrokeThickness { get; set; }

    public void Draw(ICanvas canvas, RectF rect)
    {
        canvas.SaveState();
        canvas.Antialias = true;
        canvas.StrokeLineCap = LineCap.Butt;
        canvas.StrokeLineJoin = LineJoin.Round;

        if (HaloStrokeColor is not null && HaloStrokeThickness > 0.5f)
        {
            DrawOutlineStroke(canvas, rect, HaloStrokeColor, HaloStrokeThickness);
        }

        // Resolve the stroke thickness once — emphasis (focus / error / status) overrides
        // the resting thickness. Both share the same colour (StrokeColor) so the host can
        // simply tint the whole outline with the state color.
        var strokeSize = EmphasisStrokeThickness > 0.5f ? EmphasisStrokeThickness : StrokeThickness;
        DrawOutlineStroke(canvas, rect, StrokeColor, strokeSize);

        canvas.RestoreState();
    }

    private void DrawOutlineStroke(ICanvas canvas, RectF rect, Color strokeColor, float strokeSize)
    {
        canvas.StrokeColor = strokeColor;
        canvas.StrokeSize = strokeSize;
        var inset = strokeSize / 2f;
        var x1 = rect.Left + inset;
        var y1 = rect.Top + inset;
        var x2 = rect.Right - inset;
        var y2 = rect.Bottom - inset;
        var r = Math.Min(CornerRadius, Math.Min((x2 - x1) / 2f, (y2 - y1) / 2f));

        if (!ShowNotch || NotchRight - NotchLeft <= 0)
        {
            canvas.DrawRoundedRectangle(x1, y1, x2 - x1, y2 - y1, r);
            return;
        }

        // Clamp the notch to the straight portion of the top edge so we never start the notch
        // inside a corner arc. The notch ends are extended by ~3px for visual breathing room.
        var notchStart = Math.Max(x1 + r + 2, NotchLeft);
        var notchEnd = Math.Min(x2 - r - 2, NotchRight);

        if (notchEnd <= notchStart)
        {
            canvas.DrawRoundedRectangle(x1, y1, x2 - x1, y2 - y1, r);
            return;
        }

        // Build a single open path that traces the outline, leaving a gap on the top edge.
        // Drawing as a path (instead of separate segments) keeps the line joins crisp at the
        // corners.
        var path = new PathF();
        path.MoveTo(notchEnd, y1);
        path.LineTo(x2 - r, y1);
        // Top-right corner: 90° → 0° clockwise around the top-right rounded corner.
        path.AddArc(x2 - 2 * r, y1, x2, y1 + 2 * r, 90, 0, true);
        path.LineTo(x2, y2 - r);
        // Bottom-right corner: 0° → -90° clockwise.
        path.AddArc(x2 - 2 * r, y2 - 2 * r, x2, y2, 0, -90, true);
        path.LineTo(x1 + r, y2);
        // Bottom-left corner: -90° → -180° clockwise.
        path.AddArc(x1, y2 - 2 * r, x1 + 2 * r, y2, -90, -180, true);
        path.LineTo(x1, y1 + r);
        // Top-left corner: 180° → 90° clockwise.
        path.AddArc(x1, y1, x1 + 2 * r, y1 + 2 * r, 180, 90, true);
        path.LineTo(notchStart, y1);

        canvas.DrawPath(path);
    }
}
