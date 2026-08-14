using G9MAUIControls.Theming;
using Microsoft.Maui.Graphics;

namespace G9MAUIControls.Controls;

/// <summary>
///     Paints the centered selection band and the top/bottom fade overlays of an
///     <see cref="G9DrumColumn" />. Pure overlay — fully <c>InputTransparent</c> so it
///     never blocks the underlying tap gesture on the rows.
///     // TODO (palette step): selection band / fade colors will move to G9Palette.
/// </summary>
internal sealed class G9DrumColumnDrawable : IDrawable
{
    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var palette = G9Palette.Current;
        var bandHeight = (float)G9Metrics.DrumRowHeight;
        var bandTop = dirtyRect.Center.Y - (bandHeight / 2f);

        canvas.SaveState();

        // Top + bottom fade so out-of-band rows fade to surface, hiding the abrupt edge.
        var fade = new LinearGradientPaint
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops =
            [
                new PaintGradientStop(0.00f, palette.Surface),
                new PaintGradientStop(0.30f, palette.Surface.WithAlpha(0)),
                new PaintGradientStop(0.70f, palette.Surface.WithAlpha(0)),
                new PaintGradientStop(1.00f, palette.Surface)
            ]
        };
        canvas.SetFillPaint(fade, dirtyRect);
        canvas.FillRectangle(dirtyRect);

        // Selection band — hairline tint + outline only, no fill that would mask the row.
        canvas.FillColor = palette.Primary.WithAlpha(0.06f);
        canvas.FillRoundedRectangle(dirtyRect.Left + 8, bandTop, dirtyRect.Width - 16, bandHeight, 12);
        canvas.StrokeColor = palette.Primary.WithAlpha(0.55f);
        canvas.StrokeSize = 1.2f;
        canvas.DrawRoundedRectangle(dirtyRect.Left + 8, bandTop, dirtyRect.Width - 16, bandHeight, 12);

        canvas.RestoreState();
    }
}
