namespace G9MAUIControls.EdgePanel;

/// <summary>
///     Centralised sizing constants for <see cref="G9EdgePanel" />.
///     Mirrors the pattern established by <c>G9TabBarMetrics</c>.
/// </summary>
internal static class G9EdgePanelMetrics
{
    // ── Tab (collapsed floating handle) ──
    public const double TabCollapsedWidth = 48;
    public const double TabCollapsedHeight = 64;
    public const double TabExpandedSize = 40;
    public const double TabCornerRadius = 18;

    // ── Panel card ──
    public const double DefaultWidthRatio = 0.35;
    public const double DefaultTopGap = 112;

    /// <summary>
    ///     Default maximum panel height as a fraction of the parent height (0–1). Absolute
    ///     <see cref="G9EdgePanel.MaxPanelHeight" /> still caps lower when set (&gt; 0).
    /// </summary>
    public const double DefaultMaxPanelHeightRatio = 0.69;

    /// <summary>Legacy absolute cap (dp) when callers set <see cref="G9EdgePanel.MaxPanelHeight" /> &gt; 0.</summary>
    public const double DefaultMaxPanelHeightAbsoluteDp = 560;

    public const double PanelCornerRadius = 24;
    public const double PanelStrokeThickness = 1;

    // ── Animation ──
    public const uint DefaultOpenAnimationDurationMs = 450;
    public const uint DefaultCloseAnimationDurationMs = 450;

    /// <summary>First-time collapsed tab slide-in after attach (ms).</summary>
    public const uint TabEntranceDurationMs = 320;

    public const double CollapsedExtraOffset = 10;
    public const int OverlayZIndex = 1000;

    /// <summary>
    ///     How many dp the panel card and collapsed tab extend past the screen wall when fully open/closed.
    ///     Hides the stroke hairline on the wall-facing edge so panel and wall look seamlessly flush.
    /// </summary>
    public const double WallOverlapDp = 2;

    /// <summary>
    ///     Fraction of the open animation over which the tab fades in (and reverses on close).
    ///     Keeps the tab from popping in instantly when the panel is first attached.
    /// </summary>
    public const double TabFadeInProgressFraction = 0.30;

    /// <summary>
    ///     Debounce window for tab/backdrop taps. Prevents Android from firing the same logical tap
    ///     twice (which would close-then-reopen the panel during the close animation).
    /// </summary>
    public const int InteractionDebounceMs = 220;

    /// <summary>
    ///     Offset applied to the tab's layout X when the panel is fully expanded.
    ///     The tab floats near the inner edge of the panel.
    /// </summary>
    public const double ExpandedTabInset = 56;

    /// <summary>Top inset of the tab relative to panel top (visual breathing room).</summary>
    public const double TabTopInset = 12;

    // ── Tab chevron / close icon ──
    public const double TabIconSize = 20;

    // ── Menu item list ──
    public const double MenuItemHeight = 52;
    public const double MenuItemIconSize = 22;
    public const double MenuItemEmojiSize = 20;
    public const double MenuItemImageSize = 28;
    public const double MenuItemSpacing = 8;
    public const double MenuItemCornerRadius = 12;

    // ── Breadcrumb / back item ──
    public const double BackItemHeight = 44;

    /// <summary>Cross-fade duration when navigating between menu levels (ms).</summary>
    public const uint MenuTransitionDurationMs = 220;

    /// <summary>Layout settle delay before measuring target height after menu swap (ms).</summary>
    public const int MenuHeightSettleDelayMs = 48;

    /// <summary>Duration when smoothing panel card height after content changes (ms).</summary>
    public const uint PanelHeightAnimationDurationMs = 240;

    /// <summary>Minimum height (dp) of the spinner container shown during the first panel open.</summary>
    public const double SpinnerMinHeightDp = 72;

    /// <summary>
    ///     Fraction of the close animation duration over which the panel inner content fades to
    ///     transparent. Hiding the menu content early in the close lets the GPU composite a
    ///     plain rectangle as the panel slides toward the wall — eliminates frame-drop judder
    ///     near the end of close where Easing.CubicIn drives the panel at peak speed.
    /// </summary>
    public const double ContentFadeOnCloseFraction = 0.35;

    /// <summary>Minimum content fade-out duration when closing (ms). Floor for very short close durations.</summary>
    public const uint ContentFadeOnCloseMinMs = 90;

    // ── Animation names ──
    public const string PanelAnimationName = "G9EdgePanel.SlideAnim";
    public const string TabMorphAnimationName = "G9EdgePanel.TabMorph";
    public const string TabEntranceAnimationName = "G9EdgePanel.TabEntrance";
    public const string PanelHeightAnimationName = "G9EdgePanel.HeightSmooth";
    public const string MenuContentAnimationName = "G9EdgePanel.MenuContent";
    public const string PanelContentFadeAnimationName = "G9EdgePanel.ContentFade";
}
