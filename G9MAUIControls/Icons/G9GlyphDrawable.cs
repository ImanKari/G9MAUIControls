using Microsoft.Maui.Graphics;

namespace G9MAUIControls.Icons;

/// <summary>
///     Paints one <see cref="G9Glyph" /> as vector geometry on an <see cref="ICanvas" />.
///     <para>
///         Every path is authored inside a <b>24 × 24 design box</b> and scaled to the
///         drawable's bounds, exactly like a Material / Fluent icon grid. Authoring at a
///         fixed grid is what lets a caller ask for the same glyph at 14 dp on a helper row
///         and 42 dp on a FAB and get the same optical weight at both.
///     </para>
///     <para>
///         Strokes use <see cref="LineCap.Round" /> / <see cref="LineJoin.Round" /> and a
///         thickness proportional to the requested size, so a glyph never goes hairline-thin
///         at 40 dp nor blobby at 12 dp. <see cref="StrokeUnits" /> is the stroke width in
///         design-box units; 2.0 matches the weight of a Material Rounded 24 dp icon.
///     </para>
/// </summary>
public sealed class G9GlyphDrawable : IDrawable
{
    /// <summary>The design box every path below is authored in. Do not change.</summary>
    private const float Box = 24f;

    /// <summary>Stroke width in design-box units. 2.0 ≈ Material Rounded 24 dp weight.</summary>
    public const float StrokeUnits = 2.0f;

    /// <summary>Which glyph to paint.</summary>
    public G9Glyph Glyph { get; set; }

    /// <summary>The stroke / fill colour.</summary>
    public Color Color { get; set; } = Colors.Black;

    /// <summary>
    ///     Stroke width multiplier. 1.0 is the standard weight; raise it for a bolder glyph
    ///     (e.g. a FAB's plus) or lower it for a lighter one.
    /// </summary>
    public float WeightScale { get; set; } = 1f;

    /// <inheritdoc />
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        if (Glyph == G9Glyph.None || dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
        {
            return;
        }

        // Fit the 24x24 design box into the available rect, centred, preserving aspect.
        var scale = Math.Min(dirtyRect.Width, dirtyRect.Height) / Box;
        var offsetX = dirtyRect.X + ((dirtyRect.Width - (Box * scale)) / 2f);
        var offsetY = dirtyRect.Y + ((dirtyRect.Height - (Box * scale)) / 2f);

        canvas.SaveState();
        try
        {
            canvas.Translate(offsetX, offsetY);
            canvas.Scale(scale, scale);

            canvas.StrokeColor = Color;
            canvas.FillColor = Color;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.StrokeLineJoin = LineJoin.Round;
            canvas.StrokeSize = StrokeUnits * WeightScale;
            canvas.Antialias = true;

            Paint(canvas);
        }
        finally
        {
            canvas.RestoreState();
        }
    }

    private void Paint(ICanvas canvas)
    {
        switch (Glyph)
        {
            case G9Glyph.ChevronDown: Chevron(canvas, 90f); break;
            case G9Glyph.ChevronUp: Chevron(canvas, 270f); break;
            case G9Glyph.ChevronLeft: Chevron(canvas, 180f); break;
            case G9Glyph.ChevronRight: Chevron(canvas, 0f); break;

            case G9Glyph.ArrowBack: Arrow(canvas, 180f); break;
            case G9Glyph.ArrowForward: Arrow(canvas, 0f); break;

            case G9Glyph.Close:
                canvas.DrawLine(6f, 6f, 18f, 18f);
                canvas.DrawLine(18f, 6f, 6f, 18f);
                break;

            case G9Glyph.Search:
                canvas.DrawCircle(10.5f, 10.5f, 5.5f);
                canvas.DrawLine(14.6f, 14.6f, 19f, 19f);
                break;

            case G9Glyph.Eye: Eye(canvas, struck: false); break;
            case G9Glyph.EyeOff: Eye(canvas, struck: true); break;

            case G9Glyph.Check: Tick(canvas); break;

            case G9Glyph.CheckCircle:
                canvas.DrawCircle(12f, 12f, 9f);
                canvas.StrokeSize = StrokeUnits * WeightScale * 0.9f;
                canvas.DrawLine(7.8f, 12.3f, 10.6f, 15.1f);
                canvas.DrawLine(10.6f, 15.1f, 16.2f, 9.3f);
                break;

            case G9Glyph.Warning:
            {
                var path = new PathF();
                path.MoveTo(12f, 3.6f);
                path.LineTo(21.6f, 20.4f);
                path.LineTo(2.4f, 20.4f);
                path.Close();
                canvas.DrawPath(path);
                canvas.DrawLine(12f, 9.4f, 12f, 14.6f);
                Dot(canvas, 12f, 17.6f);
                break;
            }

            case G9Glyph.ErrorCircle:
                canvas.DrawCircle(12f, 12f, 9f);
                canvas.DrawLine(8.8f, 8.8f, 15.2f, 15.2f);
                canvas.DrawLine(15.2f, 8.8f, 8.8f, 15.2f);
                break;

            case G9Glyph.Info:
                canvas.DrawCircle(12f, 12f, 9f);
                canvas.DrawLine(12f, 11f, 12f, 16.4f);
                Dot(canvas, 12f, 7.8f);
                break;

            case G9Glyph.Calendar:
                canvas.DrawRoundedRectangle(3.4f, 5.4f, 17.2f, 15.2f, 2.4f);
                canvas.DrawLine(3.4f, 10f, 20.6f, 10f);
                canvas.DrawLine(8.2f, 3f, 8.2f, 6.4f);
                canvas.DrawLine(15.8f, 3f, 15.8f, 6.4f);
                break;

            case G9Glyph.Clock:
                canvas.DrawCircle(12f, 12f, 8.8f);
                canvas.DrawLine(12f, 7.2f, 12f, 12f);
                canvas.DrawLine(12f, 12f, 15.6f, 14.2f);
                break;

            case G9Glyph.Mic: Mic(canvas, struck: false); break;
            case G9Glyph.MicOff: Mic(canvas, struck: true); break;

            case G9Glyph.Plus:
                canvas.DrawLine(12f, 5f, 12f, 19f);
                canvas.DrawLine(5f, 12f, 19f, 12f);
                break;

            case G9Glyph.Minus:
                canvas.DrawLine(5f, 12f, 19f, 12f);
                break;

            case G9Glyph.Menu:
                canvas.DrawLine(4f, 7f, 20f, 7f);
                canvas.DrawLine(4f, 12f, 20f, 12f);
                canvas.DrawLine(4f, 17f, 20f, 17f);
                break;

            case G9Glyph.Refresh:
                // An OPEN arc plus an arrow head. A closed circle would read as a "record" dot, and
                // the gap is what makes the glyph legible as "go round again" at 16 dp.
                canvas.DrawArc(3.6f, 3.6f, 16.8f, 16.8f, 55f, -230f, clockwise: true, closed: false);
                canvas.DrawLine(16.6f, 3.2f, 17.6f, 8.4f);
                canvas.DrawLine(17.6f, 8.4f, 12.4f, 7.6f);
                break;

            case G9Glyph.Delete:
                canvas.DrawLine(4f, 7f, 20f, 7f);
                canvas.DrawLine(9.6f, 7f, 10.2f, 4.2f);
                canvas.DrawLine(10.2f, 4.2f, 13.8f, 4.2f);
                canvas.DrawLine(13.8f, 4.2f, 14.4f, 7f);
                var can = new PathF();
                can.MoveTo(6f, 7f);
                can.LineTo(7.2f, 20.4f);
                can.LineTo(16.8f, 20.4f);
                can.LineTo(18f, 7f);
                canvas.DrawPath(can);
                canvas.DrawLine(10.2f, 10.6f, 10.6f, 17f);
                canvas.DrawLine(13.8f, 10.6f, 13.4f, 17f);
                break;

            case G9Glyph.None:
            default:
                break;
        }
    }

    /// <summary>
    ///     One chevron path, rotated about the design box centre. Authoring the glyph once and
    ///     rotating it is what guarantees all four directions carry identical optical weight —
    ///     four hand-authored variants drift.
    /// </summary>
    /// <summary>
    ///     A shafted arrow: a full-width horizontal stroke with a chevron head, pointing right at
    ///     0° and left at 180°.
    /// </summary>
    /// <remarks>
    ///     The head is drawn slightly shorter than <see cref="Chevron" />'s so the glyph reads as one
    ///     arrow rather than a line with a chevron parked on it; the shaft stops at the head's apex so
    ///     a round stroke cap does not poke past the tip.
    /// </remarks>
    private static void Arrow(ICanvas canvas, float degrees)
    {
        canvas.SaveState();
        try
        {
            canvas.Translate(12f, 12f);
            canvas.Rotate(degrees);
            canvas.Translate(-12f, -12f);

            canvas.DrawLine(5f, 12f, 18.5f, 12f);

            var head = new PathF();
            head.MoveTo(12.5f, 6f);
            head.LineTo(18.5f, 12f);
            head.LineTo(12.5f, 18f);
            canvas.DrawPath(head);
        }
        finally
        {
            canvas.RestoreState();
        }
    }

    private static void Chevron(ICanvas canvas, float degrees)
    {
        canvas.SaveState();
        try
        {
            canvas.Translate(12f, 12f);
            canvas.Rotate(degrees);
            canvas.Translate(-12f, -12f);

            var path = new PathF();
            path.MoveTo(9.5f, 5.5f);
            path.LineTo(16f, 12f);
            path.LineTo(9.5f, 18.5f);
            canvas.DrawPath(path);
        }
        finally
        {
            canvas.RestoreState();
        }
    }

    private static void Tick(ICanvas canvas)
    {
        var path = new PathF();
        path.MoveTo(5f, 12.8f);
        path.LineTo(9.6f, 17.4f);
        path.LineTo(19f, 7f);
        canvas.DrawPath(path);
    }

    private static void Eye(ICanvas canvas, bool struck)
    {
        // A lens as two mirrored quadratic curves; the pupil is a stroked circle so the
        // glyph keeps the same weight as its neighbours instead of reading as a filled blob.
        var lens = new PathF();
        lens.MoveTo(2.6f, 12f);
        lens.QuadTo(12f, 3.6f, 21.4f, 12f);
        lens.QuadTo(12f, 20.4f, 2.6f, 12f);
        lens.Close();
        canvas.DrawPath(lens);
        canvas.DrawCircle(12f, 12f, 3.1f);

        if (struck)
        {
            canvas.DrawLine(4.4f, 20.2f, 19.6f, 3.8f);
        }
    }

    private static void Mic(ICanvas canvas, bool struck)
    {
        canvas.DrawRoundedRectangle(9.2f, 2.6f, 5.6f, 11.4f, 2.8f);
        var cradle = new PathF();
        cradle.MoveTo(5.4f, 11f);
        cradle.QuadTo(5.4f, 18.2f, 12f, 18.2f);
        cradle.QuadTo(18.6f, 18.2f, 18.6f, 11f);
        canvas.DrawPath(cradle);
        canvas.DrawLine(12f, 18.2f, 12f, 21.4f);

        if (struck)
        {
            canvas.DrawLine(4.4f, 20.2f, 19.6f, 3.8f);
        }
    }

    /// <summary>A filled dot — used for the "i" tittle and the warning bang's point.</summary>
    private static void Dot(ICanvas canvas, float x, float y)
    {
        canvas.FillCircle(x, y, StrokeUnits * 0.62f);
    }
}
