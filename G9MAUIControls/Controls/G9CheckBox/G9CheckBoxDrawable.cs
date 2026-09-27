using G9MAUIControls.Theming;
using Microsoft.Maui.Graphics;

namespace G9MAUIControls.Controls;

/// <summary>
///     Check box painter: an M3 rounded square that fills with <see cref="G9Palette.Primary" />
///     while its mark draws itself in, plus a soft circular press halo behind it.
///     <para>
///         <b>Everything is a function of three floats</b> — <see cref="Progress" /> (the checked
///         timeline, see <see cref="G9CheckBoxMath" />), <see cref="Morph" /> (tick ↔ bar) and
///         <see cref="PressProgress" /> (halo) — which the control animates and this class only reads.
///         Colours are read from <see cref="G9Palette.Current" /> on every paint, so a theme switch
///         is a repaint and nothing else.
///     </para>
///     <para>
///         <b>No allocation per frame.</b> Fades go through <c>ICanvas.Alpha</c> (it multiplies the
///         colour's own alpha) instead of building a new <see cref="Color" /> per frame, the mark is
///         two <c>DrawLine</c> calls instead of a <c>PathF</c>, and the geometry types are structs.
///     </para>
///     <para>
///         <b>The canvas is pinned LTR by the control</b> and this drawable does no direction math at
///         all: the square and the halo are symmetric, and the tick is a glyph that must never mirror
///         (<c>G9Controls.md</c> §9). Direction only moves WHERE the box sits relative to its label,
///         which is layout, not paint.
///     </para>
///     <para>
///         No shadow, and no <c>SetShadow</c>: the suite is flat by policy (<c>G9Controls.md</c> §0).
///         The halo is a plain alpha fill.
///     </para>
/// </summary>
internal sealed class G9CheckBoxDrawable : IDrawable
{
    /// <summary>
    ///     Halo radius at the very start of a press, as a fraction of its full radius. The halo grows
    ///     from here to full size as it fades in, so a press reads as something spreading out from
    ///     under the finger rather than a disc switching on.
    /// </summary>
    private const float HaloRestScale = 0.7f;

    /// <summary>
    ///     Size of the fill square when it starts to appear, as a fraction of the box. It grows to
    ///     the full box while fading in, so checking reads as a small "pop" into place instead of a
    ///     cross-fade.
    /// </summary>
    private const float FillStartScale = 0.78f;

    /// <summary>Checked timeline, 0 (empty outline) … 1 (filled, mark drawn).</summary>
    public float Progress { get; set; }

    /// <summary>Mark shape, 0 = tick, 1 = indeterminate bar.</summary>
    public float Morph { get; set; }

    /// <summary>Press halo, 0 (none) … 1 (full).</summary>
    public float PressProgress { get; set; }

    /// <summary>Disabled paints the M3 disabled tints and suppresses the halo.</summary>
    public bool IsEnabled { get; set; } = true;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var palette = G9Palette.Current;
        var progress = G9CheckBoxMath.Clamp01(Progress);
        var press = G9CheckBoxMath.Clamp01(PressProgress);
        var centerX = dirtyRect.Center.X;
        var centerY = dirtyRect.Center.Y;

        canvas.SaveState();
        canvas.Antialias = true;

        // ── Press halo ───────────────────────────────────────────────────────────────────────
        if (press > 0.001f && IsEnabled)
        {
            var fullRadius = (float)(G9Metrics.CheckBoxHaloDiameter / 2);
            canvas.FillColor = palette.Primary;
            canvas.Alpha = G9Colors.CheckBoxHaloAlpha * press;
            canvas.FillCircle(centerX, centerY, fullRadius * (HaloRestScale + ((1f - HaloRestScale) * press)));
        }

        var size = (float)G9Metrics.CheckBoxSize;
        var corner = (float)G9Metrics.CheckBoxCornerRadius;
        var left = centerX - (size / 2f);
        var top = centerY - (size / 2f);
        var fill = G9CheckBoxMath.FillAmount(progress);

        // ── Unchecked outline — fades out as the fill arrives ───────────────────────────────
        if (fill < 1f)
        {
            // The stroke is centred on its path, so inset by half of it: the OUTER edge of the
            // stroke then sits exactly on the 20dp square the fill covers, and the two states have
            // the same footprint (no 1dp "grow" when the box fills).
            var stroke = (float)G9Metrics.CheckBoxStrokeThickness;
            var inset = stroke / 2f;
            canvas.StrokeColor = palette.Outline;
            canvas.StrokeSize = stroke;
            canvas.Alpha = (1f - fill) * (IsEnabled ? 1f : G9Colors.CheckBoxDisabledOutlineAlpha);
            canvas.DrawRoundedRectangle(
                left + inset, top + inset, size - stroke, size - stroke, Math.Max(0f, corner - inset));
        }

        // ── Fill — pops in from slightly smaller than the box ───────────────────────────────
        if (fill > 0f)
        {
            var scale = FillStartScale + ((1f - FillStartScale) * fill);
            var scaled = size * scale;
            canvas.FillColor = IsEnabled ? palette.Primary : palette.TextDisabled;
            canvas.Alpha = fill;
            canvas.FillRoundedRectangle(
                centerX - (scaled / 2f), centerY - (scaled / 2f), scaled, scaled, corner * scale);
        }

        // ── Mark — the tick (or bar) draws itself in by length ──────────────────────────────
        var markAmount = G9CheckBoxMath.MarkAmount(progress);
        if (markAmount > 0f)
        {
            var mark = G9CheckMarkGeometry.Resolve(Morph);
            var trim = mark.Trim(markAmount);
            if (!trim.IsEmpty)
            {
                canvas.Alpha = 1f;
                // Disabled: M3 paints the mark in the surface colour on the disabled container.
                canvas.StrokeColor = IsEnabled ? G9Colors.CheckBoxMark(palette) : palette.Surface;
                canvas.StrokeSize = (float)G9Metrics.CheckBoxMarkStrokeThickness;
                canvas.StrokeLineCap = LineCap.Round;
                canvas.StrokeLineJoin = LineJoin.Round;

                // Two lines with round caps meet cleanly at the elbow — a PathF would allocate per
                // frame for the same picture.
                canvas.DrawLine(
                    left + (mark.Ax * size), top + (mark.Ay * size),
                    left + (trim.FirstEndX * size), top + (trim.FirstEndY * size));

                if (trim.HasSecond)
                {
                    canvas.DrawLine(
                        left + (mark.Bx * size), top + (mark.By * size),
                        left + (trim.SecondEndX * size), top + (trim.SecondEndY * size));
                }
            }
        }

        canvas.RestoreState();
    }
}
