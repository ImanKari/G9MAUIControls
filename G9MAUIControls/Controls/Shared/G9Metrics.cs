namespace G9MAUIControls.Controls;

/// <summary>
///     Shared visual metric defaults for the new app controls. Numbers are kept here so the
///     per-control files only consume the named constants. Colors live in
///     <see cref="G9Colors" />; the palette step will move both sets of tokens into
///     the global G9Palette.
/// </summary>
/// <remarks>
///     <b>Public because <see cref="G9ControlBase" /> is public.</b> A consumer — or a satellite package —
///     authoring a control on that base needs the same radii, heights, paddings, icon sizes and animation
///     durations, or their control looks adjacent to the suite rather than part of it. Exposing the tokens
///     is what makes deriving from the base actually useful.
///     <para>
///         These are <c>const</c>, so a consumer's build inlines them. That is intentional (the values are
///         a compile-time design contract), but it means a package upgrade that retunes a token requires
///         the consumer to rebuild — not merely to restore.
///     </para>
/// </remarks>
public static class G9Metrics
{
    // ── Shared radii ──
    public const double RadiusXs = 6;
    public const double RadiusSm = 8;
    public const double RadiusMd = 12;
    public const double RadiusLg = 16;
    public const double RadiusXl = 20;
    public const double RadiusPill = 999;

    // ── Box / control heights ──
    public const double ControlHeight = 52;
    public const double ButtonHeightSmall = 36;
    public const double ButtonHeightMedium = 44;
    public const double ButtonHeightLarge = 48;
    public const double ButtonHeightHero = 52;

    // ── Padding / icon slots ──
    public const double InputHorizontalPadding = 14;
    /// <summary>
    ///     Logical icon glyph size in pixels. The icon host's column is auto-sized to
    ///     <c>OuterMargin + InputIconSize + InnerMargin</c> so the configured margins
    ///     appear on each side of the glyph independently.
    /// </summary>
    public const double InputIconSize = 20;

    // ── Leading icon margins (separate outer / inner) ──
    /// <summary>
    ///     Gap (logical px) between the BOX WALL and the LEADING icon glyph edge.
    ///     In LTR this is the left-side gap; in RTL it maps to the right-side gap
    ///     (the code handles the swap). Increase for more breathing room between the
    ///     icon and the outline stroke.
    /// </summary>
    public const double LeadingIconOuterMargin = 12;
    /// <summary>
    ///     Gap (logical px) between the LEADING icon glyph edge and the INNER TEXT.
    ///     In LTR this is the right-side gap of the leading icon; in RTL it maps to the
    ///     left-side gap. This directly controls how close the typed text / placeholder
    ///     sits to the leading icon.
    /// </summary>
    public const double LeadingIconInnerMargin = 8;

    // ── Trailing icon margins (separate outer / inner) ──
    /// <summary>
    ///     Gap (logical px) between the TRAILING icon glyph edge and the BOX WALL.
    ///     In LTR this is the right-side gap; in RTL it maps to the left-side gap.
    /// </summary>
    public const double TrailingIconOuterMargin = 12;
    /// <summary>
    ///     Gap (logical px) between the INNER TEXT and the TRAILING icon glyph edge.
    ///     In LTR this is the left-side gap of the trailing icon; in RTL it maps to
    ///     the right-side gap.
    /// </summary>
    public const double TrailingIconInnerMargin = 8;

    // ── Derived slot widths (auto-computed) ──
    /// <summary>Total column width occupied by the leading icon slot.</summary>
    public const double LeadingIconSlotWidth = LeadingIconOuterMargin + InputIconSize + LeadingIconInnerMargin;
    /// <summary>Total column width occupied by the trailing icon slot.</summary>
    public const double TrailingIconSlotWidth = TrailingIconOuterMargin + InputIconSize + TrailingIconInnerMargin;

    /// <summary>
    ///     Horizontal offset (in logical pixels) the floating label is pushed away from
    ///     the corner when a leading icon is present, so the rest-state label sits over
    ///     the inner text (after the icon column) instead of over the icon glyph itself.
    ///     <para>
    ///         The label is anchored at the corner inset (<see cref="InputHorizontalPadding" />
    ///         from the box edge). The inner text starts where the icon column ends — the
    ///         icon column width is <c>LeadingIconSlotWidth</c>. So the slide is the
    ///         icon-column width minus the corner padding.
    ///     </para>
    /// </summary>
    public const double InputLabelLeadingIconOffset = LeadingIconSlotWidth - InputHorizontalPadding;
    /// <summary>
    ///     Extra horizontal offset (in logical pixels) added to the floating label when it
    ///     sits in its floated position. Shifts the entire label-and-notch unit further
    ///     from the corner curvature so the cut-out reads as deliberate, not crowded.
    ///     Applied via <c>TranslationX</c> in the leading direction (positive in LTR,
    ///     negative in RTL) so it interpolates smoothly with the rest-state slide.
    ///     <para>
    ///         If you want extra breathing room <b>inside</b> the notch (between the
    ///         outline cut and the visible text), tune <see cref="OutlineNotchTextGap" />
    ///         instead — that is the gap that wraps the text on every side.
    ///     </para>
    /// </summary>
    public const double FloatingLabelFloatedExtraX = 8;
    /// <summary>
    ///     Horizontal gap (in logical pixels) between the visible floating-label text and
    ///     the cut edges of the outline notch. The notch is widened by this amount on
    ///     each side of the measured text width so the text doesn't touch the stroke.
    ///     <para>
    ///         Tune this to widen / tighten how much empty outline shows on either side
    ///         of the placeholder text inside the notch.
    ///     </para>
    /// </summary>
    public const double OutlineNotchTextGap = 8;

    // ── Floating label ──
    public const double FloatingLabelFontSizeFloat = 11;
    public const double FloatingLabelFontSizeRest = 15;
    public const double FloatingLabelHeight = 22;
    /// <summary>
    ///     Vertical translation applied to the floating label when it sits on the border edge.
    ///     With <see cref="FloatingLabelHeight" /> = 22 and the label using <c>VerticalOptions=Start</c>,
    ///     the un-translated label center lives at Y=11. To anchor the floated label center on
    ///     the top edge of the box (Y=0) — Material 3 outlined-field convention, where the
    ///     notch cuts through the middle of the label — we translate by -11.
    /// </summary>
    public const double FloatingLabelFloatedY = -11;
    /// <summary>Scale applied to the rest-state label so it animates smoothly to the floated state.</summary>
    public const double FloatingLabelRestScale = 1.0;
    public const double FloatingLabelFloatedScale = 0.78;
    public const uint FloatingLabelDurationMs = 160;

    /// <summary>
    ///     Top clearance an outlined field reserves inside its OWN bounds (as top padding on the
    ///     field root) when <c>ReserveFloatingLabelClearance</c> is on, so the floated label — which
    ///     overhangs the box top by a few dp (it sits centered on the top border, scaled by
    ///     <see cref="FloatingLabelFloatedScale" /> = 0.78) — renders WITHIN the field rather than
    ///     spilling above it and being covered/clipped by whatever sits directly on top (a bottom-sheet
    ///     header, a card edge). 6 comfortably contains the overhang (verified on device); more just
    ///     adds dead space above the field.
    /// </summary>
    public const double FloatingLabelClearance = 6;

    // ── Helper / counter ──
    public const double HelperFontSize = 11.5;
    public const double CounterFontSize = 11;

    // ── Outlined field strokes ──
    public const float OutlinedFieldStrokeThickness = 1.5f;
    public const float OutlinedFieldEmphasisStrokeThickness = 2.5f;
    public const float OutlinedFieldFocusHaloThickness = 6f;
    public const float FocusRingOpacity = 0.22f;

    // ── Button fonts ──
    public const double ButtonFontSmall = 12;
    public const double ButtonFontMedium = 14;
    public const double ButtonFontLarge = 15;
    public const double ButtonFontHero = 16;

    // ── Switch ──
    public const double SwitchWidth = 56;
    public const double SwitchHeight = 32;
    public const double SwitchThumb = 24;
    public const double SwitchThumbPressed = 30;

    // ── Range slider ──
    public const double SliderHeight = 104;
    /// <summary>
    ///     Compact slider height used when <c>ShowLabels=false</c>. Sized so the canvas
    ///     just contains the thumb (radius 14 = diameter 28) plus a small bottom gap
    ///     (~6 dp) so consecutive sliders don't visually merge in a tight stack.
    /// </summary>
    public const double SliderHeightCompact = 36;
    public const double SliderTrackY = 52;
    public const double SliderTrackHeight = 8;
    public const double SliderThumbRadius = 14;
    public const double SliderHorizontalInset = 16;
    /// <summary>Vertical gap (logical pixels) between the track and the min/max edge labels.</summary>
    public const double SliderLabelGap = 18;

    // ── PIN entry ──
    /// <summary>Default per-cell width (logical pixels). Matches Material 3 OTP guidance (40 dp).</summary>
    public const double PinCellWidth = 44;
    /// <summary>Default per-cell height. Matches the cell width for square cells.</summary>
    public const double PinCellHeight = 52;
    /// <summary>Border thickness on a resting cell.</summary>
    public const double PinCellStrokeThickness = 1.4;
    /// <summary>Border thickness on a focused cell — slightly thicker so focus is obvious.</summary>
    public const double PinCellStrokeThicknessFocused = 2;
    /// <summary>Corner radius on each cell.</summary>
    public const double PinCellCornerRadius = 10;
    /// <summary>Horizontal gap between adjacent cells inside the same group.</summary>
    public const double PinCellSpacing = 8;
    /// <summary>Horizontal gap between a cell and an adjacent group separator label.</summary>
    public const double PinSeparatorSpacing = 6;
    /// <summary>Font size of the digit/letter typed into a cell.</summary>
    public const double PinCellFontSize = 22;
    /// <summary>Font size of the separator character displayed between groups.</summary>
    public const double PinSeparatorFontSize = 22;

    // ── Progress ──
    public const double ProgressBarHeight = 10;

    // ── Chip ──
    public const double ChipHeight = 36;
    public const double ChipHorizontalPadding = 14;
    public const double ChipFontSize = 13;
    public const double ChipIconSize = 16;
    /// <summary>
    ///     Horizontal + vertical gap between chips in a wrapping group. Tightened from
    ///     8dp to 6dp to match the Material 3 chip-group default. The previous 8dp
    ///     value left chips visibly floating apart on dense forms.
    /// </summary>
    public const double ChipSpacing = 6;
    public const uint ChipSelectionAnimationMs = 140;

    /// <summary>
    ///     Width-and-opacity animation duration of the leading "selected" checkmark
    ///     that slides in when a chip becomes selected. Material 3 filter-chip's
    ///     hallmark interaction. Slightly faster than the color crossfade so the
    ///     check arrives just before the color settles, signalling "selected" first.
    /// </summary>
    public const uint ChipCheckmarkAnimationMs = 120;

    // ── Tab view ──
    /// <summary>
    ///     Outer height of the tab BAR (the rounded segmented strip). Increased from 46
    ///     to 52 to accommodate the inner pill padding without cramping text vertically.
    /// </summary>
    public const double TabBarHeight = 52;

    /// <summary>
    ///     Padding inside the bar around the inner row of tab cells. Creates the inset
    ///     between the active pill and the bar's rounded edge. Material 3 secondary tab
    ///     uses ~4dp.
    /// </summary>
    public const double TabBarInnerPadding = 4;

    /// <summary>
    ///     Corner radius of the OUTER bar. Pairs with TabPillRadius to give a concentric
    ///     "pill in a pill" look. Bar = 26 → cell = 22 (4dp inset).
    /// </summary>
    public const double TabBarRadius = 26;

    /// <summary>
    ///     Vertical inset applied to the active pill on top of the bar's inner padding.
    ///     The pill height is computed as
    ///     <c>TabHeight − TabBarInnerPadding × 2 − TabPillVerticalInset × 2</c>.
    ///     With the default 52 / 4 / 2 recipe this gives a 40dp pill inside a 44dp inner
    ///     area — 2dp clearance top and bottom for the stroke + colored shadow halo to
    ///     render without being clipped at the bar's rounded corners.
    ///     <para>
    ///         Reduced from 4 to 2 to make the pill visually taller and easier to read
    ///         against multi-icon tab content. With 4dp inset the pill landed too thin
    ///         and the asymmetric drop shadow made the gap below the pill look bigger
    ///         than the gap above. 2dp keeps a hairline of breathing room without
    ///         starving the pill height.
    ///     </para>
    /// </summary>
    public const double TabPillVerticalInset = 2;

    /// <summary>
    ///     Corner radius of the SLIDING pill that sits behind the active tab.
    ///     Reduced from 22 to 18 to match the smaller pill height after applying the
    ///     <see cref="TabPillVerticalInset" />. The bar's outer radius (26) and pill
    ///     radius (18) keep the concentric "pill in a pill" relationship intact.
    /// </summary>
    public const double TabPillRadius = 18;

    /// <summary>
    ///     Horizontal padding inside each tab cell.
    /// </summary>
    public const double TabCellHorizontalPadding = 16;

    /// <summary>
    ///     Vertical gap between the tab bar and the content panel below it. Visually
    ///     separates the segmented control from its content.
    /// </summary>
    public const double TabBarToContentGap = 8;

    public const double TabFontSize = 13;
    public const double TabIconSize = 16;
    public const double TabBadgeHeight = 18;
    public const double TabContentRadius = 16;

    /// <summary>
    ///     Thickness of the underline indicator on <c>G9TabStyle.Underlined</c>
    ///     tabs. The animated bar at the bottom of the active cell uses this height,
    ///     deliberately a touch thicker (2.5 dp) than the bar's bottom edge line
    ///     (1 dp) so the active state is visible against the bottom rule.
    /// </summary>
    public const double TabUnderlineThickness = 2.5;

    /// <summary>
    ///     Edge thickness of the top and bottom horizontal rules drawn across the bar
    ///     in <c>G9TabStyle.Underlined</c>. The active underline uses
    ///     <see cref="TabUnderlineThickness"/> instead and sits on top of the bottom
    ///     rule for the segment of the active cell.
    /// </summary>
    public const double TabBarEdgeLineThickness = 1;

    /// <summary>
    ///     Width of the vertical <c>|</c> separator placed between consecutive cells in
    ///     <c>G9TabStyle.Underlined</c>. 1 dp.
    /// </summary>
    public const double TabSeparatorThickness = 1;

    /// <summary>
    ///     Vertical inset (top + bottom) for the cell separator in
    ///     <c>G9TabStyle.Underlined</c>, i.e. how much shorter the separator is
    ///     than the bar height. Defaults to 12 dp on each side, so the separator's
    ///     height = <c>TabHeight − 24</c>. Keeps the separator from touching the
    ///     top / bottom rules.
    /// </summary>
    public const double TabSeparatorVerticalInset = 12;

    /// <summary>
    ///     Default inset applied inside <c>G9TabView</c>'s content panel. Consumers
    ///     can override per-instance via <c>G9TabView.ContentPadding</c> (e.g. set to
    ///     0 for full-bleed map content).
    /// </summary>
    public const double TabContentDefaultPadding = 20;

    // ── Drum picker ──
    public const double DrumColumnHeight = 220;
    public const double DrumRowHeight = 44;
    public const double DrumLabelFontSize = 10;
    public const double DrumSelectedFontSize = 17;
    public const double DrumUnselectedFontSize = 16;

    // ── Selection sheet ──
    public const double SelectionRowHeight = 52;
    // Horizontal inset for a flat, borderless selection row's content (icon → text → check).
    // The rows are full-bleed (no card frame); this is the only side gap, kept in the 16–20dp
    // list-row band. Shared by G9SelectionSheet and the ShowListG9BottomSheetAsync picker.
    public const double SelectionRowHorizontalPadding = 16;
    public const double SelectionMaxHeight = 520;
    public const double SelectionMinHeight = 220;
    public const double SelectionRowFontSize = 14;
    public const double SelectionIconSize = 18;
    public const double SelectionCheckBoxSize = 20;
    public const double SelectionCheckRadius = 6;

    // ── Nav card ──
    public const double NavCardIconBadgeSize = 38;
    public const double NavCardPaddingX = 14;
    public const double NavCardPaddingY = 12;
    public const double NavCardTitleFontSize = 14;
    public const double NavCardSubtitleFontSize = 11.5;
    public const double NavCardValueFontSize = 13;
    public const double NavCardChevronSize = 24;

    // The coming-soon badge is a passive ROADMAP marker, not an action — at the nav-card
    // title scale it out-weighed the title it sat beside. These are the title/badge values
    // scaled to ~70% (font 14 -> 10, padding 10,5 -> 7,3.5, radius 16 -> 11) so the badge
    // reads as a quiet annotation. Keep the ratio if you retune the nav-card type scale.
    public const double NavCardComingSoonFontSize = 10;
    public const double NavCardComingSoonPaddingX = 7;
    public const double NavCardComingSoonPaddingY = 3.5;
    public const double NavCardComingSoonRadius = 11;

    // ── Separator ──
    public const double SeparatorTitleFontSize = 10;
    public const double SeparatorIconSize = 12;

    // ── Expander (collapsible section header + content) ──
    /// <summary>
    ///     Minimum height of the tappable expander header row (icon + title + arrow).
    ///     The row never measures shorter than this so the tap target stays ≥ the 44 dp
    ///     accessibility floor regardless of content. Matches the standard control row
    ///     height so a stack of expanders reads with the same rhythm as other G9 rows.
    /// </summary>
    public const double ExpanderHeaderHeight = 48;
    /// <summary>Font size of the expander header title.</summary>
    public const double ExpanderTitleFontSize = 15;
    /// <summary>Glyph size of the leading header icon.</summary>
    public const double ExpanderIconSize = 20;
    /// <summary>Glyph size of the trailing expand/collapse chevron.</summary>
    public const double ExpanderChevronSize = 22;
    /// <summary>Gap between the leading icon and the header title text.</summary>
    public const double ExpanderHeaderSpacing = 10;
    /// <summary>
    ///     Vertical gap inserted between the header row and the revealed content when the
    ///     expander is open. Applied as a top margin on the content host, so it costs
    ///     nothing while collapsed (an invisible view contributes no margin to layout).
    /// </summary>
    public const double ExpanderContentTopGap = 8;
    /// <summary>Horizontal padding inside the header row when the expander is framed.</summary>
    public const double ExpanderHeaderPaddingX = 14;
    /// <summary>Duration (ms) of the chevron rotation between the collapsed and expanded states.</summary>
    public const uint ExpanderChevronRotationMs = 220;
    /// <summary>Duration (ms) of the content reveal / hide height animation. Kept in lockstep with the chevron rotation.</summary>
    public const uint ExpanderContentAnimationMs = 220;

    // ── Cascade panel (nested drill-down stack) ──
    /// <summary>
    ///     Duration (ms) of a nested-panel push / pop slide. Tuned to feel quick but
    ///     readable; the same value drives the parallax of the panel beneath so the two
    ///     stay in lockstep.
    /// </summary>
    public const uint CascadePanelAnimationMs = 280;
    /// <summary>Height of the optional built-in panel header (back button + title row).</summary>
    public const double CascadePanelHeaderHeight = 48;
    /// <summary>Font size of the panel header title.</summary>
    public const double CascadePanelHeaderFontSize = 15;
    /// <summary>Glyph size of the header back-navigation chevron.</summary>
    public const double CascadePanelBackIconSize = 22;
    /// <summary>Horizontal padding inside the panel header row.</summary>
    public const double CascadePanelHeaderPadding = 8;
    /// <summary>
    ///     Fraction of the panel dimension the underneath panel travels during a push, in
    ///     the same direction as the incoming travel — the iOS-style depth parallax. The
    ///     underneath panel slides back to identity on pop.
    /// </summary>
    public const double CascadePanelParallaxFraction = 0.18;
    /// <summary>Opacity the underneath panel fades to while covered (depth dimming).</summary>
    public const double CascadePanelUnderlayDimOpacity = 0.55;

    // ── Animation ──
    public const uint PressDurationMs = 80;
    public const uint ReleaseDurationMs = 150;
    public const uint HoverDurationMs = 80;
    public const uint RippleDurationMs = 350;

    /// <summary>
    ///     Scale target for the icon-host press dip in <see cref="G9OutlinedFieldBase" />.
    ///     Soft enough to feel like a press without the icon visibly "punching in" — the
    ///     ripple does the heavier lifting on touch feedback now, so the scale just adds
    ///     a subtle tactile cue. The earlier 0.78 value was too aggressive, made the icon
    ///     look like it was about to disappear into the field on every tap.
    /// </summary>
    public const double IconPressScaleTo = 0.92;
    public const uint TabIndicatorDurationMs = 240;
    public const uint TabContentTransitionMs = 160;
    public const uint SwitchToggleDurationMs = 240;
    public const uint ProgressValueDurationMs = 300;
    public const uint IndeterminateFrameMs = 16;
    public const float IndeterminateStep = 0.012f;

    // ── Intro carousel (login onboarding) ──
    public const double IntroOverlayOpacity = 0.52;
    public const double IntroOverlayTopOpacityRatio = 0.4;
    public const int IntroStartupCoverFadeDurationMs = 320;
    public const int IntroInitialVideoSeekTimeoutMs = 900;
    public const int IntroMediaFadeInDurationMs = 420;
    public const int IntroMediaFadeOutDurationMs = 420;
    public const double IntroLogoWidth = 192;
    public const double IntroLogoHeight = 34;
    public const double IntroTitleFontSize = 18;
    public const double IntroSubtitleFontSize = 14;
    public const double IntroNavFontSize = 14;
    public const double IntroStepActiveWidth = 28;
    public const double IntroStepInactiveWidth = 20;
    public const double IntroStepHeight = 4;
    public const double IntroStepSpacing = 6;
    public const double IntroChromePadding = 20;
    public const double IntroHeaderTopPadding = 16;
    public const double IntroLogoTopMargin = 48;
    public const double IntroTextBottomMargin = 12;
    public const double IntroNavMinTouchHeight = 48;
    public const double IntroNavMinTouchWidth = 72;
    public const double IntroContentBottomPadding = 24;
}
