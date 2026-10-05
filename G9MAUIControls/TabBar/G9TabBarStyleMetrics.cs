using static G9MAUIControls.TabBar.G9TabBarMetrics;

namespace G9MAUIControls.TabBar;

/// <summary>
///     The geometry that differs between the two <see cref="G9TabBarStyle" />s. Everything the styles
///     SHARE — the four transparent insets that give the Skia shadow room
///     (<see cref="G9TabBarMetrics.ChromeShadowPadding" />, <see cref="G9TabBarMetrics.BarBottomGap" />,
///     <see cref="G9TabBarMetrics.BarHorizontalGap" />), the sub-menu row, the overflow column, the
///     animation timings — stays in <see cref="G9TabBarMetrics" />.
/// </summary>
/// <remarks>
///     <para>
///         <b>Public for the same reason <see cref="G9TabBarMetrics" /> is:</b> a floating bar's height is a
///         layout fact for the whole page. A host that reserves the bar's height at the bottom of its
///         content must read <see cref="BarHeight" /> from the style it actually shows —
///         <c>G9TabBarStyleMetrics.For(bar.BarStyle).BarHeight</c>, or <see cref="G9TabBar.CurrentMetrics" />
///         — not the Classic constant <see cref="G9TabBarMetrics.BarHeight" />, which is 2 dp short of a
///         Sculpted bar.
///     </para>
///     <para>
///         <see cref="Classic" /> reproduces the pre-1.4.0 constants exactly; a bar that never sets
///         <see cref="G9TabBar.BarStyle" /> renders byte-for-byte as before.
///     </para>
/// </remarks>
public sealed class G9TabBarStyleMetrics
{
    private G9TabBarStyleMetrics(G9TabBarStyle style)
    {
        Style = style;
    }

    /// <summary>The original glass bar — the values <see cref="G9TabBarMetrics" /> has always carried.</summary>
    public static G9TabBarStyleMetrics Classic { get; } = new(G9TabBarStyle.Classic)
    {
        BarHeight = G9TabBarMetrics.BarHeight,
        BarTopRadius = G9TabBarMetrics.BarTopRadius,
        BarBottomRadius = G9TabBarMetrics.BarBottomRadius,
        ItemsHorizontalPadding = 0d,
        FabSize = G9TabBarMetrics.FabSize,
        FabInnerSize = G9TabBarMetrics.FabInnerSize,
        FabIconSize = G9TabBarMetrics.FabIconSize,
        FabPlusArmRatio = (float)G9TabBarMetrics.FabPlusArmRatio,
        FabPlusStrokeRatio = (float)G9TabBarMetrics.FabPlusStrokeRatio,
        FabCenterBelowBarTop = 0d,
        FloatingHeadroom = 44d,
        NotchShape = G9TabBarNotchShape.Semicircle,
        NotchHalfWidth = G9TabBarMetrics.FabSize / 2d + NotchGap,
        BottomIconSize = G9TabBarMetrics.BottomIconSize,
        BottomLabelFontSize = G9TabBarMetrics.BottomLabelFontSize,
        BottomContentSpacing = 3d,
        ShowsSelectionIndicator = true,
        SelectedItemNudgeY = SelectedIndicatorDownNudgeY,
        DrawsTopHighlight = true,
        DrawsFabShadow = true,
        ShadowAlpha = 0.5f,
        ShadowBlurSigma = 5.5f,
        ShadowOffsetY = 0f
    };

    /// <summary>
    ///     «Bottom Navigation / Sculpted» — Figma AgriPad → UI Kit → «Bottom Navigation / Sculpted —
    ///     بازطراحی دوم». Every number below is read off that component (a 336 × 64 bar, 24 dp corners,
    ///     a 56 dp FAB box holding a 48 dp disc whose centre is 8 dp below the bar's top edge, 24 dp icons,
    ///     12 pt labels, items 64 dp wide inside an 8 dp side padding).
    /// </summary>
    public static G9TabBarStyleMetrics Sculpted { get; } = new(G9TabBarStyle.Sculpted)
    {
        BarHeight = 64d,
        BarTopRadius = 24f,
        BarBottomRadius = 24f,
        ItemsHorizontalPadding = 8d,
        FabSize = 56d,
        FabInnerSize = 48d,
        FabIconSize = 24d,
        // The design's + is two 24 × 2.7 bars. The drawable adds round caps (half a stroke at each end),
        // so the arm is (24 − 2.7) / 2 to land on the same 24 dp overall length.
        FabPlusArmRatio = 0.444f,
        FabPlusStrokeRatio = 0.1125f,
        FabCenterBelowBarTop = 8d,
        // The FAB box rises 20 dp above the bar (28 − 8); 8 dp more keeps the idle-to-floating scale-up
        // clear of the control's top edge.
        FloatingHeadroom = 28d,
        NotchShape = G9TabBarNotchShape.Cradle,
        NotchHalfWidth = CradleHalfWidth,
        BottomIconSize = 24d,
        BottomLabelFontSize = 12d,
        BottomContentSpacing = 4d,
        ShowsSelectionIndicator = false,
        SelectedItemNudgeY = 0d,
        DrawsTopHighlight = false,
        DrawsFabShadow = false,
        // Figma: DROP_SHADOW blur 8, y 1, black 12 %. A Gaussian sigma is about half the CSS-style blur.
        ShadowAlpha = 0.12f,
        ShadowBlurSigma = 4f,
        ShadowOffsetY = 1f
    };

    // ── The Sculpted cradle, in dp from the notch centre (x) and the bar's top edge (y) ──
    // Lifted from the Sculpted surface's vector path (centre 168 on a 336-wide bar):
    //   L 111 0 · C 126 0 129 10 136 23 · C 149 47 187 47 200 23 · C 207 10 210 0 225 0
    // i.e. a shoulder that leaves the top edge 57 dp out, rolls over to (32, 23), and a bowl whose
    // control points sit 47 dp down (the curve itself bottoms out at ~41 dp).

    /// <summary>Half the cradle's opening along the bar's top edge.</summary>
    public const float CradleHalfWidth = 57f;

    internal const float CradleShoulderHandleX = 42f;
    internal const float CradleShoulderInnerX = 39f;
    internal const float CradleShoulderInnerY = 10f;
    internal const float CradleShoulderEndX = 32f;
    internal const float CradleShoulderEndY = 23f;
    internal const float CradleBowlHandleX = 19f;
    internal const float CradleBowlHandleY = 47f;

    /// <summary>Returns the metrics of <paramref name="style" />.</summary>
    public static G9TabBarStyleMetrics For(G9TabBarStyle style)
    {
        return style == G9TabBarStyle.Sculpted ? Sculpted : Classic;
    }

    public G9TabBarStyle Style { get; }

    // ── Bar ──

    /// <summary>Height of the visible bar, in dp — what a host reserves under its scrolling content.</summary>
    public double BarHeight { get; private init; }

    public float BarTopRadius { get; private init; }
    public float BarBottomRadius { get; private init; }

    /// <summary>Inset between the bar's side edges and the first/last item slot.</summary>
    public double ItemsHorizontalPadding { get; private init; }

    // ── FAB ──

    /// <summary>The FAB's tap box (and, in Classic, its glass shell).</summary>
    public double FabSize { get; private init; }

    /// <summary>The coloured disc that carries the + glyph.</summary>
    public double FabInnerSize { get; private init; }

    public double FabIconSize { get; private init; }
    public float FabPlusArmRatio { get; private init; }
    public float FabPlusStrokeRatio { get; private init; }

    /// <summary>
    ///     How far below the bar's top edge the floating FAB's centre sits. Classic: 0 (the centre is ON
    ///     the edge). Sculpted: 8 — the disc sinks into the cradle.
    /// </summary>
    public double FabCenterBelowBarTop { get; private init; }

    /// <summary>How far the floating FAB's box rises above the bar's top edge.</summary>
    public double FabProtrusionAboveBar => FabSize / 2d - FabCenterBelowBarTop;

    /// <summary>Room reserved above the bar while the FAB floats (FAB protrusion plus breathing room).</summary>
    public double FloatingHeadroom { get; private init; }

    // ── Notch ──

    internal G9TabBarNotchShape NotchShape { get; private init; }

    /// <summary>Half the notch's opening along the top edge at full progress.</summary>
    public double NotchHalfWidth { get; private init; }

    // ── Items ──

    public double BottomIconSize { get; private init; }
    public double BottomLabelFontSize { get; private init; }
    public double BottomContentSpacing { get; private init; }

    /// <summary>Classic paints a primary-tinted pill behind the selected item; Sculpted does not.</summary>
    public bool ShowsSelectionIndicator { get; private init; }

    /// <summary>How far the selected item slides down (Classic centres it on its pill).</summary>
    public double SelectedItemNudgeY { get; private init; }

    // ── Surface ──

    internal bool DrawsTopHighlight { get; private init; }
    internal bool DrawsFabShadow { get; private init; }
    internal float ShadowAlpha { get; private init; }
    internal float ShadowBlurSigma { get; private init; }
    internal float ShadowOffsetY { get; private init; }

    // ── Reserved control heights ──

    /// <summary>The control's height while the FAB rests inline.</summary>
    public double CompactControlHeight => BarHeight + ChromeShadowPadding + BarBottomGap;

    /// <summary>The control's height while the FAB floats.</summary>
    public double FloatingControlHeight => BarHeight + FloatingHeadroom + ChromeShadowPadding + BarBottomGap;

    /// <summary>The control's height while the FAB's sub-menu row is open.</summary>
    public double ComputeOpenControlHeight(int subMenuItemCount)
    {
        if (subMenuItemCount <= 0)
        {
            return FloatingControlHeight;
        }

        return BarHeight + FabProtrusionAboveBar + SubMenuRowAboveFabGap + SubMenuRowCellHeight + 8d +
               ChromeShadowPadding + BarBottomGap;
    }
}

/// <summary>The shape carved into the bar's top edge under a floating FAB.</summary>
internal enum G9TabBarNotchShape
{
    /// <summary>A semicircle of radius FAB/2 + <see cref="G9TabBarMetrics.NotchGap" /> (Classic).</summary>
    Semicircle,

    /// <summary>A wide cradle with S-curved shoulders (Sculpted).</summary>
    Cradle
}
