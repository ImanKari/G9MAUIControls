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
    private G9TabBarStyleMetrics _metrics = G9TabBarStyleMetrics.Classic;
    private float _layoutHeightDip;
    private float _notchCenterX;
    private float _centerProgress;
    private SKColor _shadowColor = new(0, 0, 0, 130);
    private float _shadowOffsetX;
    private float _shadowOffsetY;
    private float _fabCenterX;
    private float _fabCenterY;
    private float _fabRadius;
    private float _fabVisibility;

    // Set by any setter below whose value really changed; consumed by InvalidateIfChanged. Starts true
    // so the first request always paints.
    private bool _isDirty = true;

    // Skia objects are native handles. They used to be created inside every OnPaintSurface — a new
    // SKPath, two SKPaints and two SKMaskFilters per paint — and the two blur filters were never
    // disposed at all (disposing an SKPaint does not dispose the MaskFilter assigned to it), so each
    // repaint left two native objects for the finalizer. They are now created once, reused, and
    // released in ReleaseSkiaResources when the handler goes away.
    private SKMaskFilter? _blurFilter;
    private SKPaint? _barPaint;
    private SKPaint? _fabPaint;
    private SKPath? _barPath;
    private BarPathKey _barPathKey;

    /// <summary>
    ///     The style whose silhouette is shadowed. Also carries the blur sigma (Classic ~5.5 dp for a soft
    ///     ~11 dp spread; Sculpted 4 dp, the Figma "blur 8"). Set by <see cref="G9TabBar" />.
    /// </summary>
    public G9TabBarStyleMetrics Metrics
    {
        get => _metrics;
        set
        {
            if (ReferenceEquals(_metrics, value))
            {
                return;
            }

            var sigmaChanged = !_metrics.ShadowBlurSigma.Equals(value.ShadowBlurSigma);
            _metrics = value;
            _isDirty = true;

            if (sigmaChanged)
            {
                // The blur filter bakes its sigma in; the paints holding it are rebuilt with it.
                ReleaseSkiaResources();
            }
        }
    }

    /// <summary>Control (bar host) reserved height in DIP — the bar bottom sits at this Y.</summary>
    public float LayoutHeightDip
    {
        get => _layoutHeightDip;
        set => Set(ref _layoutHeightDip, value);
    }

    /// <summary>FAB / notch center X in the bar's own (control) coordinate space, DIP.</summary>
    public float NotchCenterX
    {
        get => _notchCenterX;
        set => Set(ref _notchCenterX, value);
    }

    /// <summary>Notch open progress 0..1 (mirrors the chrome's notch progress).</summary>
    public float CenterProgress
    {
        get => _centerProgress;
        set => Set(ref _centerProgress, value);
    }

    public SKColor ShadowColor
    {
        get => _shadowColor;
        set
        {
            if (_shadowColor == value)
            {
                return;
            }

            _shadowColor = value;
            _isDirty = true;
        }
    }

    /// <summary>
    ///     Directional offset (DIP) applied to the blurred silhouette. Default (0,0) keeps the
    ///     shadow centered directly under the bar so the soft halo wraps every border evenly
    ///     instead of dropping toward one side. Set non-zero only if a directional cast is wanted.
    /// </summary>
    public float ShadowOffsetX
    {
        get => _shadowOffsetX;
        set => Set(ref _shadowOffsetX, value);
    }

    public float ShadowOffsetY
    {
        get => _shadowOffsetY;
        set => Set(ref _shadowOffsetY, value);
    }

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
    public float FabCenterX
    {
        get => _fabCenterX;
        set => Set(ref _fabCenterX, value);
    }

    public float FabCenterY
    {
        get => _fabCenterY;
        set => Set(ref _fabCenterY, value);
    }

    public float FabRadius
    {
        get => _fabRadius;
        set => Set(ref _fabRadius, value);
    }

    /// <summary>FAB visibility 0..1 — scales the circle's alpha so the shadow fades with the FAB.</summary>
    public float FabVisibility
    {
        get => _fabVisibility;
        set => Set(ref _fabVisibility, value);
    }

    /// <summary>
    ///     Repaints only if a property that feeds the picture changed since the last request.
    ///     <para>
    ///         The tab bar pushes its geometry here from every layout pass and every animation frame,
    ///         most of which move nothing the shadow draws (the sub-menu open animation, for one, leaves
    ///         the bar, the notch and the FAB exactly where they were). Each repaint is two blurred Skia
    ///         fills, so an unconditional <c>InvalidateSurface()</c> per frame was real work for an
    ///         identical result. A size change still repaints on its own — <c>SKCanvasView</c> does that.
    ///     </para>
    /// </summary>
    public void InvalidateIfChanged()
    {
        if (!_isDirty)
        {
            return;
        }

        _isDirty = false;
        InvalidateSurface();
    }

    private void Set(ref float field, float value)
    {
        // Exact comparison is the point: "the same number was pushed again" is the case being filtered.
        if (field.Equals(value))
        {
            return;
        }

        field = value;
        _isDirty = true;
    }

    /// <inheritdoc />
    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        base.OnHandlerChanging(args);

        if (args.NewHandler is null)
        {
            ReleaseSkiaResources();

            // A later handler starts from an empty surface.
            _isDirty = true;
        }
    }

    private void ReleaseSkiaResources()
    {
        _barPath?.Dispose();
        _barPath = null;
        _barPathKey = default;

        _barPaint?.Dispose();
        _barPaint = null;

        _fabPaint?.Dispose();
        _fabPaint = null;

        // After the paints that reference it.
        _blurFilter?.Dispose();
        _blurFilter = null;
    }

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
        var barTop = MathF.Max(0f, barBottom - (float)_metrics.BarHeight);
        var centerX = _notchCenterX > 0f ? _notchCenterX : (barLeft + barRight) / 2f;

        var progress = Math.Clamp(_centerProgress, 0f, 1f);

        // Same recipe as before — one Normal blur at the style's sigma shared by both fills — built once.
        _blurFilter ??= SKMaskFilter.CreateBlur(SKBlurStyle.Normal, _metrics.ShadowBlurSigma);
        _barPaint ??= new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            MaskFilter = _blurFilter
        };
        _barPaint.Color = _shadowColor;

        // The silhouette only changes with the bar's size or the notch, not with the FAB circle, so it
        // is rebuilt only when one of its inputs does.
        var pathKey = new BarPathKey(_metrics.Style, barLeft, barRight, barTop, barBottom, centerX, progress);
        if (_barPath is null || pathKey != _barPathKey)
        {
            _barPath?.Dispose();
            _barPath = new SKPath();
            G9TabBarOutline.Build(
                new SkPathSink(_barPath), _metrics, barLeft, barRight, barTop, barBottom, centerX, progress);
            _barPathKey = pathKey;
        }

        canvas.DrawPath(_barPath, _barPaint);

        // FAB circle shadow — same blur recipe as the bar so the two halos read as one system.
        // Drawn as its own pass because its alpha follows the FAB's visibility fade; while the
        // FAB is floating it sits in the notch HOLE of the bar path, so the two blurred fills
        // barely overlap and never read as double-darkened. Classic keeps it centred (no extra
        // drop) — the old downward-offset MAUI shadow is exactly what collapsed into a bottom
        // crescent on tight-blur devices; Sculpted drops it a little further, which Skia renders
        // the same on every device. Its opacity is the style's own: the Sculpted disc needs more
        // weight than the bar's 12 % to lift off a map.
        var fabAlpha = _metrics.DrawsFabShadow ? Math.Clamp(FabVisibility, 0f, 1f) : 0f;
        if (fabAlpha > 0.01f && FabRadius > 0.5f)
        {
            _fabPaint ??= new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                MaskFilter = _blurFilter
            };
            _fabPaint.Color = _shadowColor.WithAlpha((byte)(_metrics.FabShadowAlpha * 255f * fabAlpha));

            canvas.DrawCircle(FabCenterX, FabCenterY + _metrics.FabShadowOffsetY, FabRadius, _fabPaint);
        }
    }

    private readonly record struct BarPathKey(
        G9TabBarStyle Style,
        float Left,
        float Right,
        float Top,
        float Bottom,
        float CenterX,
        float Progress);

    /// <summary>
    ///     Adapts <see cref="G9TabBarOutline" /> to an <see cref="SKPath" />, so the shadow traces the
    ///     very same silhouette the chrome paints (same radii, same notch, same insets).
    /// </summary>
    private sealed class SkPathSink(SKPath path) : IG9TabBarOutlineSink
    {
        public void MoveTo(float x, float y) => path.MoveTo(x, y);
        public void LineTo(float x, float y) => path.LineTo(x, y);
        public void QuadTo(float cx, float cy, float x, float y) => path.QuadTo(cx, cy, x, y);

        public void CubicTo(float c1X, float c1Y, float c2X, float c2Y, float x, float y) =>
            path.CubicTo(c1X, c1Y, c2X, c2Y, x, y);

        public void Close() => path.Close();
    }
}

#pragma warning restore CS0618
