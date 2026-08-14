#pragma warning disable CS0618 // SkiaSharp 4.x deprecated the mutable SKPath API in favour of
                              // SKPathBuilder. The concave FAB-notch silhouette below was tuned
                              // by eye against this path construction; porting it is a deliberate
                              // follow-up (see AiGuides/09-Progress.md), not a blind rewrite.
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using static G9MAUIControls.TabBar.G9TabBarMetrics;

namespace G9MAUIControls.TabBar;

/// <summary>
///     Renders the drop shadow for <see cref="G9TabBar" /> as a SkiaSharp blurred fill of
///     the EXACT bar silhouette — including the FAB notch — instead of a native elevation
///     shadow.
///     <para>
///         Why SkiaSharp: the bar is a transparent <c>GraphicsView</c> with a concave FAB
///         notch carved into its top edge. Native elevation shadows (Android
///         <c>View.setOutlineProvider</c>, iOS <c>CALayer</c> corner-radius shadow, MAUI
///         <c>Shadow</c>, and Sharpnado.Shadows alike) can only follow a CONVEX
///         rounded-rect outline, so they either (a) drop the shadow when a transparent shape
///         view re-lays-out, or (b) plug the notch with shadow fill. SkiaSharp's
///         <c>SKMaskFilter.CreateBlur</c> blurs the actual path we draw, and because
///         we draw the same notched path the chrome draws, the notch stays a hole in the
///         shadow (the scoop is preserved) and a soft inner shadow even wraps the notch
///         curve. SkiaSharp renders identically on Android / iOS / Windows / MacCatalyst, so
///         the shadow is reliable everywhere and survives relayout.
///     </para>
///     <para>
///         The bar silhouette is drawn INSIDE the control on every side — the four metric
///         insets <see cref="G9TabBarMetrics.ChromeShadowPadding" /> (top),
///         <see cref="G9TabBarMetrics.BarBottomGap" /> (bottom), and
///         <see cref="G9TabBarMetrics.BarHorizontalGap" /> (left/right) reserve room
///         for the soft halo to render in-bounds. We do NOT use a negative outer margin to
///         bleed past the control because Android's <c>SKCanvasView</c> host clips its
///         children's negative-margin overflow on every platform we ship to.
///     </para>
/// </summary>
internal sealed class G9TabBarShadowView : SKCanvasView
{
    /// <summary>Gaussian blur sigma (DIP). ~5.5 gives a soft ~11 dp spread.</summary>
    private const float BlurSigmaDip = 5.5f;

    private float _layoutHeightDip;
    private float _notchCenterX;
    private float _centerProgress;
    private SKColor _shadowColor = new(0, 0, 0, 130);

    /// <summary>Control (bar host) reserved height in DIP — the bar bottom sits at this Y.</summary>
    public float LayoutHeightDip
    {
        get => _layoutHeightDip;
        set => _layoutHeightDip = value;
    }

    /// <summary>FAB / notch center X in the bar's own (control) coordinate space, DIP.</summary>
    public float NotchCenterX
    {
        get => _notchCenterX;
        set => _notchCenterX = value;
    }

    /// <summary>Notch open progress 0..1 (mirrors the chrome's notch progress).</summary>
    public float CenterProgress
    {
        get => _centerProgress;
        set => _centerProgress = value;
    }

    public SKColor ShadowColor
    {
        get => _shadowColor;
        set => _shadowColor = value;
    }

    /// <summary>
    ///     Directional offset (DIP) applied to the blurred silhouette. Default (0,0) keeps the
    ///     shadow centered directly under the bar so the soft halo wraps every border evenly
    ///     instead of dropping toward one side. Set non-zero only if a directional cast is wanted.
    /// </summary>
    public float ShadowOffsetX { get; set; }
    public float ShadowOffsetY { get; set; }

    /// <summary>
    ///     FAB geometry, mirrored from <c>G9TabBar.LayoutFabButton</c> every time the FAB is
    ///     laid out. The FAB's drop shadow is drawn HERE — a blurred circle in the same Skia pass
    ///     as the bar silhouette — instead of a MAUI <c>Shadow</c> on the FAB's Border.
    ///     <para>
    ///         Why: MAUI's Android shadow renders differently per device (open upstream:
    ///         dotnet/maui #15565, #16311, #29958), and the FAB's old
    ///         <c>Shadow { Offset = (0,8), Radius = 18 }</c> collapsed into a hard dark crescent
    ///         UNDER the FAB on devices that render the blur tight (reported 2026-07 on a
    ///         Pixel 9 Pro XL while a 420dpi device looked fine). Skia renders identically
    ///         everywhere — same rationale as the bar shadow itself (see class doc).
    ///     </para>
    /// </summary>
    public float FabCenterX { get; set; }
    public float FabCenterY { get; set; }
    public float FabRadius { get; set; }

    /// <summary>FAB visibility 0..1 — scales the circle's alpha so the shadow fades with the FAB.</summary>
    public float FabVisibility { get; set; }

    public G9TabBarShadowView()
    {
        InputTransparent = true;
        IgnorePixelScaling = false;
        // The shadow blur fits inside the control thanks to the four metric insets — no
        // negative margin needed (Android's SKCanvasView host clipped the old bleed
        // overflow, killing the left/right/top halo).
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        canvas.Clear();

        var dipWidth = (float)Width;
        var dipHeight = (float)Height;
        if (dipWidth <= 0f || dipHeight <= 0f)
        {
            return;
        }

        // SKCanvasView reports the surface in pixels; map our DIP geometry onto it.
        var scale = e.Info.Width / dipWidth;
        canvas.Scale(scale);
        // Optional directional offset. Defaults to (0,0) so the blurred silhouette stays
        // centered under the bar and the soft halo wraps every border evenly.
        if (ShadowOffsetX != 0f || ShadowOffsetY != 0f)
        {
            canvas.Translate(ShadowOffsetX, ShadowOffsetY);
        }

        // Bar coordinates inside the control — match G9TabBarChromeDrawable exactly.
        var controlHeight = _layoutHeightDip > 0f ? _layoutHeightDip : dipHeight;
        var barLeft = (float)BarHorizontalGap;
        var barRight = dipWidth - (float)BarHorizontalGap;
        var barBottom = MathF.Max(0f, controlHeight - (float)BarBottomGap);
        var barTop = MathF.Max(0f, barBottom - (float)BarHeight);
        var centerX = _notchCenterX > 0f ? _notchCenterX : (barLeft + barRight) / 2f;

        var progress = Math.Clamp(_centerProgress, 0f, 1f);
        var r = NotchCircleRadius * progress;

        using var path = BuildBarPath(barLeft, barRight, barTop, barBottom, centerX, ref r, progress);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = _shadowColor,
            MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, BlurSigmaDip)
        };

        canvas.DrawPath(path, paint);

        // FAB circle shadow — same blur recipe as the bar so the two halos read as one system.
        // Drawn as its own pass because its alpha follows the FAB's visibility fade; while the
        // FAB is floating it sits in the notch HOLE of the bar path, so the two blurred fills
        // barely overlap and never read as double-darkened. Centered (no offset) so the halo
        // wraps the FAB evenly — the old downward-offset MAUI shadow is exactly what collapsed
        // into a bottom crescent on tight-blur devices.
        var fabAlpha = Math.Clamp(FabVisibility, 0f, 1f);
        if (fabAlpha > 0.01f && FabRadius > 0.5f)
        {
            using var fabPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Color = _shadowColor.WithAlpha((byte)(_shadowColor.Alpha * fabAlpha)),
                MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, BlurSigmaDip)
            };

            canvas.DrawCircle(FabCenterX, FabCenterY, FabRadius, fabPaint);
        }
    }

    /// <summary>
    ///     Builds the bar silhouette (rounded rect + optional FAB notch) as an
    ///     <see cref="SKPath" />. Mirrors <c>G9TabBarChromeDrawable.BuildBarPath</c> exactly
    ///     (same corner radii, same kappa-based notch curve, same horizontal/vertical insets)
    ///     so the shadow lines up pixel-for-pixel with the painted bar.
    /// </summary>
    private static SKPath BuildBarPath(
        float left,
        float right,
        float barTop,
        float barBottom,
        float centerX,
        ref float r,
        float progress)
    {
        var path = new SKPath();
        var topR = BarTopRadius;
        var botR = BarBottomRadius;

        path.MoveTo(left, barTop + topR);
        path.QuadTo(left, barTop, left + topR, barTop);

        if (progress > 0.001f)
        {
            // Clamp the notch center so the full semicircle fits inside the bar (matches chrome).
            var minCX = left + r + topR + 2f;
            var maxCX = right - r - topR - 2f;
            if (minCX < maxCX)
            {
                centerX = Math.Clamp(centerX, minCX, maxCX);
            }

            var leftStart = centerX - r;
            var rightEnd = centerX + r;
            var notchBottom = barTop + r;
            var control = r * CircleArcKappa;

            path.LineTo(leftStart, barTop);
            path.CubicTo(
                leftStart, barTop + control,
                centerX - control, notchBottom,
                centerX, notchBottom);
            path.CubicTo(
                centerX + control, notchBottom,
                rightEnd, barTop + control,
                rightEnd, barTop);
        }

        path.LineTo(right - topR, barTop);
        path.QuadTo(right, barTop, right, barTop + topR);
        path.LineTo(right, barBottom - botR);
        path.QuadTo(right, barBottom, right - botR, barBottom);
        path.LineTo(left + botR, barBottom);
        path.QuadTo(left, barBottom, left, barBottom - botR);
        path.Close();
        return path;
    }
}

#pragma warning restore CS0618
