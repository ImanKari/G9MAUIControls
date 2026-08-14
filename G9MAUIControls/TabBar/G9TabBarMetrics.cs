namespace G9MAUIControls.TabBar;

/// <summary>
///     The tab bar's layout constants.
/// </summary>
/// <remarks>
///     <b>Public because a floating tab bar is a layout fact for the whole page, not just for itself.</b>
///     <see cref="BarHeight" /> plus <see cref="BarBottomGap" /> is exactly the inset a consumer's
///     scrollable content needs at the bottom so its last row can clear the bar — and every consumer
///     that hosts a <see cref="G9TabBar" /> has to compute that. While this was internal the only way
///     to get it was to copy the number, which then drifts silently the first time the bar's height
///     changes. Same reasoning as <c>G9Metrics</c> for the controls (see G9Controls.md §6.7).
/// </remarks>
public static class G9TabBarMetrics
{
    public const int DefaultFabIndex = 2;

    // ── Heights ──
    public const double BarHeight = 62;
    /// <summary>
    ///     Logical pixels of breathing room reserved **above** the bar so the chrome
    ///     drawable's soft drop shadow has somewhere to fade out instead of being
    ///     clipped at the <see cref="Microsoft.Maui.Controls.GraphicsView" /> top edge.
    ///     The visible bar stays anchored to the bottom of the control because the
    ///     component is laid out with <c>VerticalOptions="End"</c>, so growing the
    ///     reserved height by this padding only adds transparent canvas above the bar
    ///     — it never moves the bar visually. Bumped to match <see cref="BarBottomGap"/>
    ///     so the soft halo wraps the top edge symmetrically with the bottom edge.
    /// </summary>
    public const double ChromeShadowPadding = 14;

    /// <summary>
    ///     Transparent gap reserved INSIDE the control, BELOW the visible bar — the bottom
    ///     counterpart to <see cref="ChromeShadowPadding" />. This is the room the SkiaSharp
    ///     drop shadow (<see cref="G9TabBarShadowView" />) needs to render its BOTTOM blur.
    ///     Without it the bottom blur would have to overflow the control's bottom edge via a
    ///     negative margin, which Android clips — the reported "shadow shows left/right/top
    ///     but never at the bottom" bug. The bar is drawn this many DIP above the control's
    ///     bottom; the host page trims its bottom margin by a matching amount so the bar keeps
    ///     its on-screen position.
    /// </summary>
    public const double BarBottomGap = 14;

    /// <summary>
    ///     Transparent gap reserved INSIDE the control on each horizontal side. The horizontal
    ///     analogue of <see cref="BarBottomGap" /> / <see cref="ChromeShadowPadding" />: the bar
    ///     is drawn from <c>x = BarHorizontalGap</c> to <c>x = width - BarHorizontalGap</c>, so
    ///     the SkiaSharp drop shadow's left/right blur renders INSIDE the control instead of
    ///     overflowing past its edges. Without this gap, Android (which clips the
    ///     <c>SKCanvasView</c>'s negative-margin overflow) hides the left/right halo entirely.
    ///     Pair with a matching reduction in the host page's horizontal margin so the bar
    ///     keeps its on-screen position.
    /// </summary>
    public const double BarHorizontalGap = 14;
    public const double CompactControlHeight = BarHeight + ChromeShadowPadding + BarBottomGap;
    public const double FloatingControlHeight = 106 + ChromeShadowPadding + BarBottomGap;
    public const double BottomItemHeight = BarHeight; // fill full bar; content centers itself

    public static double ComputeOpenControlHeight(int subMenuItemCount)
    {
        if (subMenuItemCount <= 0) return FloatingControlHeight;
        var fabProtrusion = FabSize * FabFloatingOverlapRatio;
        return BarHeight + fabProtrusion + SubMenuRowAboveFabGap + SubMenuRowCellHeight + 8d + ChromeShadowPadding + BarBottomGap;
    }

    // ── FAB ── (+10 % from 65 → 72)
    public const double FabSize = 72;
    public const double FabIconSize = 44;
    public const double FabRotationOpenDegrees = 135;
    public const double FabIdleScaleMin = 0.72;
    public const double FabFloatingScaleBoost = 0.28;
    public const double FabIdleVerticalOffset = 20;
    /// <summary>0.5 = FAB center sits exactly at barTop → equal gap all around the semicircle notch.</summary>
    public const double FabFloatingOverlapRatio = 0.5;
    public const double FabHideInvisibleAtCenterProgress = 0.58;

    // ── FAB inner circle (carries the awesome primary gradient + the plus icon) ──
    /// <summary>Inner accent circle = 78 % of the FAB outer surface.</summary>
    public const double FabInnerSizeRatio = 0.78;
    public static double FabInnerSize => FabSize * FabInnerSizeRatio;
    /// <summary>+ glyph arm length relative to the FAB icon-view size (each arm).</summary>
    public const double FabPlusArmRatio = 0.34;
    /// <summary>+ glyph stroke thickness relative to the FAB icon-view size.</summary>
    public const double FabPlusStrokeRatio = 0.14;

    /// <summary>Width of the outer translucent-glass cell that wraps each sub-menu item.</summary>
    public const double SubMenuItemFabRatio = 0.95;
    public static double SubMenuRowItemSize => FabSize * SubMenuItemFabRatio;
    /// <summary>Diameter of the inner green accent circle that holds the sub-menu icon.</summary>
    public const double SubMenuItemInnerRatio = 0.62;
    public static double SubMenuRowItemInnerSize => FabSize * SubMenuItemInnerRatio;
    /// <summary>Corner radius of the outer glass shell — rounded square, not full circle, so labels stay legible at the edges.</summary>
    public const double SubMenuRowOuterCornerRadius = 18;
    /// <summary>Vertical room reserved per sub-menu cell — circle + spacing + label, all inside the glass shell.</summary>
    public const double SubMenuRowLabelHeight = 14;
    public const double SubMenuRowLabelGap = 1;
    public const double SubMenuRowTopPadding = 4;
    public const double SubMenuRowBottomPadding = 7;
    public static double SubMenuRowCellHeight =>
        SubMenuRowTopPadding + SubMenuRowItemInnerSize + SubMenuRowLabelGap + SubMenuRowLabelHeight + SubMenuRowBottomPadding;

    // ── Notch — perfect semicircle derived from FAB ──
    /// <summary>Gap in px between the FAB circle edge and the notch curve.</summary>
    public const double NotchGap = 5;
    /// <summary>Semicircle radius = half FAB + gap. Used for both depth and width.</summary>
    public static float NotchCircleRadius => (float)(FabSize / 2.0 + NotchGap);
    public const float CircleArcKappa = 0.55228475f;
    public const float BarTopRadius = 18f;
    public const float BarBottomRadius = 18f;
    public const float BarShadowOffsetY = 0;
    public const float BarShadowBlur = 0f;
    public const float BarShadowAlpha = 0f;
    public const float BarStrokeSize = 0f;
    /// <summary>Stroke width of the inset top-edge "glass" highlight painted by the chrome drawable.</summary>
    public const float BarTopHighlightStrokeSize = 1f;

    // ── Bottom items ──
    public const double BottomIconSize = 24;
    public const double BottomLabelFontSize = 11;
    public const double SlotItemMaxWidth = 86;
    public const double SlotItemMinWidth = 56;
    public const double SlotItemHorizontalPadding = 6;
    public const double SelectedItemTranslationY = 0;
    public const double CenterItemHiddenTranslationY = 8;
    public const double CenterItemHiddenScaleAmount = 0.14;
    public const double CenterItemFadeInputThreshold = 0.35;
    public const double SelectedReturnRevealStartOpacity = 0.64;
    public const double SelectedReturnRevealStartScale = 0.96;
    public const double SelectedReturnRevealTranslationY = 6;

    // ── Sub-menu horizontal row ──
    public const double SubMenuRowSpacing = 10;
    /// <summary>Gap between FAB top and the row of sub-menu accent circles. Smaller = lower (closer to bar).</summary>
    public const double SubMenuRowAboveFabGap = 4;
    public const double SubMenuRowRevealTravelX = 18;
    public const double SubMenuRowRevealTravelY = 10;
    public const double SubMenuRowStaggerStep = 0.07;
    public const double SubMenuRowStaggerCap = 0.28;
    public const double SubMenuRowActivationThreshold = 0.72;
    public const double SubMenuRowEdgeGuard = 8;
    public const double SubMenuRowIconSizeMax = 26;
    public const double SubMenuRowFontSizeMax = 9.5;

    // ── Indicator ──
    public const double IndicatorWidth = 48;
    public const double IndicatorHeight = 32;
    public const double IndicatorCornerRadius = 13;
    public const double IndicatorTopOffset = 5;
    /// <summary>
    ///     Extra Y offset applied to the selection pill so the green background lines up
    ///     visually centered on the icon rather than reading as "pushed up". Animates with
    ///     the pill, so re-selection slides the pill into the nudged resting Y.
    /// </summary>
    public const double SelectedIndicatorDownNudgeY = 4;
    public const double IndicatorMaxStretch = 0.22;
    public const double IndicatorStretchDistanceDivisor = 240;
    public const double HiddenVisibilityThreshold = 0.001;

    // ── Overflow (>5 items) ──
    /// <summary>Maximum bottom items shown before collapsing extras into an overflow popup.</summary>
    public const int MaxVisibleBottomItems = 5;
    public const double OverflowItemSize = 56;
    public const double OverflowItemSpacing = 8;
    public const double OverflowAboveBarGap = 14;
    public const double OverflowEdgeGuard = 10;
    public const double OverflowItemIconSize = 22;
    public const double OverflowItemFontSize = 9.5;
    public const double OverflowRevealTravelY = 14;
    public const double OverflowRevealStaggerStep = 0.08;
    public const double OverflowRevealStaggerCap = 0.55;
    public const double OverflowActivationThreshold = 0.72;
    public const uint OverflowStateDurationMs = 280;

    // ── Animation ──
    public const uint CenterStateDurationMs = 240;
    /// <summary>Duration of the bouncy open animation. One smooth pass — no multi-stage stitching.</summary>
    public const uint CenterStateBouncyDurationMs = 420;

    /// <summary>
    ///     "Back" coefficient for the FAB pop — controls how far past 1.0 the FAB scale / rise
    ///     overshoots before settling. Standard easeOutBack is 1.70158; lower = subtler bounce.
    /// </summary>
    public const double FabPopBackCoefficient = 1.70158;

    /// <summary>
    ///     "Back" coefficient for the chrome notch dip — bigger than the FAB so the notch hole
    ///     visibly grows deeper than the FAB does, then settles in the same beat.
    /// </summary>
    public const double NotchPopBackCoefficient = 3.20;
    public const uint OpenStateDurationMs = 300;
    public const uint IndicatorMoveDurationMs = 320;
    public const uint SelectedReturnRevealDurationMs = 210;
    public const uint AnimationFrameRate = 16;
    public const bool StartupSelectionRevealEnabled = true;
    public const int StartupSelectionRevealDelayMs = 369;
    public const int ReservedHeightShrinkDelayMs = 110;

    // ── Touch ──
    public const double DefaultRestScale = 1.0;
    public const double DefaultRestOpacity = 1.0;
    public const double ItemHoveredOpacity = 0.86;
    public const double ItemPressedOpacity = 0.78;
    public const int TouchAnimationDurationMs = 110;

    // ── Backdrop (outside-tap-to-close) ──
    /// <summary>Z-index used to slide the invisible tap-catcher under the buttons but above the chrome.</summary>
    public const int BackdropZIndex = 1;
    /// <summary>Top inset between the bar top and the backdrop, so the backdrop only covers the *expanded* zone.</summary>
    public const double BackdropTopInset = 0;

    // ── Names ──
    public const string CenterStateAnimationName = "G9TabBar.CenterState";
    public const string NotchBounceAnimationName = "G9TabBar.NotchBounce";
    public const string OpenAnimationName = "G9TabBar.Open";
    public const string IndicatorAnimationName = "G9TabBar.Indicator";
    public const string BottomSelectionRevealAnimationName = "G9TabBar.BottomSelectionReveal";
    public const string OverflowAnimationName = "G9TabBar.Overflow";
}
