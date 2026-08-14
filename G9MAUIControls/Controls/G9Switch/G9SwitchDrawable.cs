using G9MAUIControls.Helpers;
using G9MAUIControls.Theming;
using Microsoft.Maui.Graphics;

namespace G9MAUIControls.Controls;

/// <summary>
///     Switch painter. The thumb shape "morphs" during the toggle — it stretches horizontally
///     mid-flight, briefly turning into a longer pill before settling back to a circle on the
///     opposite side. The morph is driven by an inverted-parabola applied to the progress
///     value so it peaks at 0.5 and returns to 0 at the endpoints.
/// </summary>
internal sealed class G9SwitchDrawable : IDrawable
{
    public bool IsEnabled { get; set; } = true;
    public bool IsPressed { get; set; }
    public bool IsRtl { get; set; }
    public float Progress { get; set; }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var palette = G9Palette.Current;
        var progress = Math.Clamp(Progress, 0f, 1f);

        var offTrack = G9Colors.SwitchOffTrack(palette);
        var onTrack = G9Colors.SwitchOnTrack(palette);
        var thumbOff = G9Colors.SwitchThumbOff(palette);
        var thumbOn = G9Colors.SwitchThumbOn(palette);

        var trackColor = G9ColorHelper.Mix(offTrack, onTrack, progress);
        var thumbColor = G9ColorHelper.Mix(thumbOff, thumbOn, progress);

        canvas.SaveState();
        canvas.Antialias = true;

        // Track.
        canvas.FillColor = trackColor;
        canvas.FillRoundedRectangle(0, 0, dirtyRect.Width, dirtyRect.Height, 16);

        canvas.StrokeSize = 1.5f;
        canvas.StrokeColor = Colors.Black.WithAlpha(0.06f);
        canvas.DrawRoundedRectangle(0.75f, 0.75f, dirtyRect.Width - 1.5f, dirtyRect.Height - 1.5f, 16);

        DrawTrackMark(canvas, dirtyRect, progress);

        // Thumb morph: peak stretch at progress=0.5, no stretch at 0 or 1.
        // morph in [0..1] — shape factor.
        var morph = 1f - (float)Math.Abs(2 * progress - 1); // triangle wave: 0→1→0
        // Press adds a bit of extra stretch so a held thumb visibly grows.
        var press = IsPressed ? 1f : 0f;
        var stretch = morph * 0.55f + press * 0.20f;

        var baseWidth = (float)G9Metrics.SwitchThumb;
        var maxWidth = (float)G9Metrics.SwitchThumbPressed;
        var thumbWidth = baseWidth + (maxWidth - baseWidth) * stretch;
        var thumbHeight = (float)G9Metrics.SwitchThumb;

        // Thumb anchor — when stretched, the leading edge of the thumb stays anchored on the
        // direction of motion so the thumb appears to "reach" forward like a drop shape.
        // Map progress so the right edge in LTR (or left edge in RTL) tracks linearly.
        var marginX = 4f;
        var maxLeading = (float)dirtyRect.Width - marginX - thumbWidth; // when fully ON in LTR
        var leadingX = IsRtl
            ? marginX + (maxLeading - marginX) * (1 - progress)
            : marginX + (maxLeading - marginX) * progress;

        // Vertical center.
        var thumbY = ((float)dirtyRect.Height - thumbHeight) / 2f;
        var radius = thumbHeight / 2f;

        // No shadow on the thumb. The app is shadow-free by policy (G9Controls.md §0) and
        // `ICanvas.SetShadow` is `Paint.setShadowLayer` on Android's hardware-accelerated
        // GraphicsView, which this repo already found unreliable (the same reason
        // G9TabBarChromeDrawable's SetShadow is zeroed via BarShadow* = 0). The thumb reads
        // against the track on fill contrast alone.
        canvas.FillColor = thumbColor;
        canvas.FillRoundedRectangle(leadingX, thumbY, thumbWidth, thumbHeight, radius);

        canvas.RestoreState();
    }

    private void DrawTrackMark(ICanvas canvas, RectF rect, float progress)
    {
        var markX = IsRtl ? rect.Width - 15 : 15;
        var markY = rect.Center.Y;

        canvas.SaveState();
        if (progress < 0.5f)
        {
            canvas.FillColor = Colors.Black.WithAlpha(0.22f * (1 - progress));
            canvas.FillCircle(markX, markY, 3);
        }
        else
        {
            canvas.StrokeColor = Colors.White.WithAlpha(0.75f * progress);
            canvas.StrokeSize = 2.5f;
            canvas.StrokeLineCap = LineCap.Round;
            canvas.DrawLine(markX - 5, markY, markX - 1, markY + 4);
            canvas.DrawLine(markX - 1, markY + 4, markX + 6, markY - 5);
        }
        canvas.RestoreState();
    }
}
