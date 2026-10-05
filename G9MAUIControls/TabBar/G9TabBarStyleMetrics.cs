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
///         — not the Classic constant <see cref="G9TabBarMetrics.BarHeight" />, which is 6 dp short of a
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
        ShadowOffsetY = 0f,
        // The glass shell IS the FAB's silhouette, so its shadow is the box, at the bar's own alpha.
        FabShadowDiameter = G9TabBarMetrics.FabSize,
        FabShadowAlpha = 0.5f,
        FabShadowOffsetY = 0f
    };

    /// <summary>
    ///     «Bottom Navigation / Sculpted» — Figma AgriPad → UI Kit → «Bottom Navigation / Sculpted —
    ///     بازطراحی دوم». The component is a 336 × 64 bar with 24 dp corners, a 56 dp FAB box holding a
    ///     48 dp disc whose centre is 8 dp below the bar's top edge, 24 dp icons, 12 pt labels and items
    ///     64 dp wide inside an 8 dp side padding.
    ///     <para>
    ///         <b>Deliberately larger than the component</b>, from two rounds of the owner's device review
    ///         (2026-10-05). Round 1, "the circle is very small … the bar can be a little taller": the bar
    ///         became 68 dp and the CRADLE grew by 56 / 48 (<see cref="CradleScale" />), so its S-curve
    ///         keeps Figma's proportions. Round 2, "the space around the circle must be half": the cradle
    ///         stayed and the DISC grew into it — 66 dp in a 74 dp box, centre still 9 dp below the top
    ///         edge — which takes the tightest disc-to-bowl clearance from 10.8 dp to 5.8 dp.
    ///     </para>
    ///     <para>
    ///         ⛔ The disc and the cradle are sized INDEPENDENTLY on purpose. To change the gap, change
    ///         <c>SculptedFabDisc</c>; to change the cradle, change <c>SculptedCradleScale</c>; never edit
    ///         the <c>Cradle*</c> design constants for a size change.
    ///     </para>
    /// </summary>
    public static G9TabBarStyleMetrics Sculpted { get; } = new(G9TabBarStyle.Sculpted)
    {
        BarHeight = 68d,
        BarTopRadius = 24f,
        BarBottomRadius = 24f,
        ItemsHorizontalPadding = 8d,
        // The tap box is the disc plus 4 dp all round, as in the component (48 in 56).
        FabSize = SculptedFabDisc + 8d,
        FabInnerSize = SculptedFabDisc,
        // Figma's 24 dp «+» on a 48 dp disc is half the disc; 30 on 66 keeps it a little lighter than
        // that, so the bigger disc does not read as a bigger, louder glyph.
        FabIconSize = 30d,
        // The design's + is two 24 × 2.7 bars. The drawable adds round caps (half a stroke at each end),
        // so the arm is (24 − 2.7) / 2 to land on the same 24 dp overall length.
        FabPlusArmRatio = 0.444f,
        FabPlusStrokeRatio = 0.1125f,
        // Figma's 8 dp sink, scaled with the cradle — the disc grows around this centre, so the extra
        // size is shared between the bowl below and the air above.
        FabCenterBelowBarTop = 9d,
        // The FAB box rises 28 dp above the bar (74 / 2 − 9); 9 dp more keeps the idle-to-floating
        // scale-up clear of the control's top edge.
        FloatingHeadroom = 37d,
        NotchShape = G9TabBarNotchShape.Cradle,
        CradleScale = (float)SculptedCradleScale,
        NotchHalfWidth = CradleHalfWidth * SculptedCradleScale,
        BottomIconSize = 24d,
        BottomLabelFontSize = 12d,
        BottomContentSpacing = 4d,
        ShowsSelectionIndicator = false,
        SelectedItemNudgeY = 0d,
        DrawsTopHighlight = false,
        // Figma: DROP_SHADOW blur 8, y 1, black 12 %. A Gaussian sigma is about half the CSS-style blur.
        ShadowAlpha = 0.12f,
        ShadowBlurSigma = 4f,
        ShadowOffsetY = 1f,
        // The disc gets its own lift (owner, 2026-10-05: "the circle must have a shadow"). Figma drew it
        // flat, which on a map reads as a sticker. The FAB box around the disc is TRANSPARENT here, so the
        // shadow traces the DISC — a box-sized one would be a grey ring 4 dp outside the green. Darker
        // than the bar's 12 % and dropped 2 dp further (3 dp in all), Material's resting-FAB recipe; the
        // same Skia blur as the bar, so the two halos read as one light source.
        DrawsFabShadow = true,
        FabShadowDiameter = SculptedFabDisc,
        FabShadowAlpha = 0.3f,
        FabShadowOffsetY = 2f
    };

    // The Sculpted disc, and how much larger than Figma's the cradle is drawn (its path below was drawn
    // around a 48 dp disc). Independent — see the remarks on Sculpted.
    private const double SculptedFabDisc = 66d;
    private const double SculptedCradleScale = 56d / 48d;

    // ── The Sculpted cradle, in DESIGN dp from the notch centre (x) and the bar's top edge (y) ──
    // Drawn around a 48 dp disc; the outline multiplies every value by CradleScale.
    // Lifted from the Sculpted surface's vector path (centre 168 on a 336-wide bar):
    //   L 111 0 · C 126 0 129 10 136 23 · C 149 47 187 47 200 23 · C 207 10 210 0 225 0
    // i.e. a shoulder that leaves the top edge 57 dp out, rolls over to (32, 23), and a bowl whose
    // control points sit 47 dp down (the curve itself bottoms out at ~41 dp).

    /// <summary>Half the cradle's opening along the bar's top edge, in design dp (before <see cref="CradleScale" />).</summary>
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

    /// <summary>Multiplier on the cradle's design constants (1 = the Figma path as drawn). Cradle only.</summary>
    internal float CradleScale { get; private init; } = 1f;

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

    /// <summary>The diameter the FAB's shadow traces: Classic its glass box, Sculpted the coloured disc.</summary>
    internal double FabShadowDiameter { get; private init; }

    /// <summary>The FAB shadow's opacity at full visibility (independent of the bar's).</summary>
    internal float FabShadowAlpha { get; private init; }

    /// <summary>Extra downward drop of the FAB shadow, on top of <see cref="ShadowOffsetY" />.</summary>
    internal float FabShadowOffsetY { get; private init; }

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
