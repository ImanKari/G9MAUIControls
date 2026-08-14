namespace G9MAUIControls.Theming;

/// <summary>
///     Shared layout metrics (spacing, sizes, radii, paddings).
///     <para>
///         <b>Single source of truth.</b> The authoritative values live in
///         <c>Resources/Styles/LayoutMetrics.xaml</c> (merged into <c>App.xaml</c>). XAML
///         consumes them via <c>{DynamicResource Key}</c>; this class READS the same keyed
///         resources at runtime so code-behind that builds views in C# uses the identical
///         value — no duplicated literals to drift. The constants below are only fallbacks
///         used when <see cref="Application.Current" /> resources are not yet available
///         (very early startup) or a key is missing.
///     </para>
///     <para>
///         Because the lookup is live, a XAML Hot Reload edit to <c>LayoutMetrics.xaml</c>
///         is reflected the next time a member is read (note: views already built in C#
///         are not re-run by Hot Reload, so chrome constructed once at open time keeps its
///         value until rebuilt — XAML-declared usages update immediately).
///     </para>
///     <para>
///         <b>Exceptions that stay <c>const</c>:</b>
///         <list type="bullet">
///             <item><see cref="DeferredContentLoadDelayMs" /> — a timing constant that two
///             other <c>const</c> fields depend on (<c>DeferredContentView.DefaultLoadDelayMs</c>,
///             <c>UserAvatarGroup.AvatarImageLoadingDelay</c>); it is mirrored (not read) in
///             the XAML dictionary.</item>
///             <item>The drag-handle / footer-border / backdrop-card / private toolbar tokens
///             below — they are consumed only from C# (never referenced in XAML), so there is
///             no duplication to eliminate and they remain compile-time constants.</item>
///         </list>
///     </para>
/// </summary>
public static class G9LayoutMetrics
{
    // ---------------------------------------------------------------
    // Resource readers — resolve a keyed value from the merged app
    // ResourceDictionary (LayoutMetrics.xaml), falling back to a literal.
    // ---------------------------------------------------------------
    private static bool TryGetResource<T>(string key, out T value)
    {
        if (Application.Current?.Resources is { } resources
            && resources.TryGetValue(key, out var raw)
            && raw is T typed)
        {
            value = typed;
            return true;
        }

        value = default!;
        return false;
    }

    private static double D(string key, double fallback) =>
        TryGetResource<double>(key, out var v) ? v : fallback;

    private static Thickness Th(string key, Thickness fallback) =>
        TryGetResource<Thickness>(key, out var v) ? v : fallback;

    private static CornerRadius Cr(string key, CornerRadius fallback) =>
        TryGetResource<CornerRadius>(key, out var v) ? v : fallback;

    /// <summary>
    ///     Seeds the resource keys the suite's own XAML binds with <c>DynamicResource</c>, so a consumer
    ///     does not have to know they exist.
    ///     <para>
    ///         <b>Why this is needed even though every reader above has a fallback.</b> The C# readers fall
    ///         back to a literal, so code paths are safe with no resources at all. XAML
    ///         <c>{DynamicResource …}</c> has no such fallback: an absent key leaves the property
    ///         <i>unset</i>, silently — a corner radius of zero, a margin of zero. Square cards and
    ///         edge-to-edge sheet bodies, with nothing in the build output to explain it.
    ///     </para>
    ///     <para>
    ///         Called automatically by <see cref="G9Theme.Init" />, so most consumers never see it. Call it
    ///         directly only if you apply theme dictionaries yourself without going through
    ///         <see cref="G9Theme" />.
    ///     </para>
    ///     <para>
    ///         Existing keys are <b>not</b> overwritten: an app that has already defined one of these — to
    ///         retune the suite — keeps its value. Seeding fills gaps; it never fights the consumer.
    ///     </para>
    /// </summary>
    /// <param name="application">The application whose resources to seed. Null is a no-op.</param>
    public static void InstallDefaults(Application? application)
    {
        var resources = application?.Resources;
        if (resources is null)
        {
            return;
        }

        // Only the three keys the suite's XAML actually binds. Every other metric is read from C# with a
        // literal fallback, so seeding it would add dictionary entries nothing looks up.
        SeedIfAbsent(resources, nameof(ControlCornerRadiusValue), new CornerRadius(9));
        SeedIfAbsent(resources, nameof(EdgeHorizontalThickness), new Thickness(10, 0, 10, 0));
        SeedIfAbsent(resources, nameof(ModalBaseBodyMargin), new Thickness(10, 0, 10, 10));
    }

    private static void SeedIfAbsent(ResourceDictionary resources, string key, object value)
    {
        if (!resources.TryGetValue(key, out _))
        {
            resources[key] = value;
        }
    }

    // ---------------------------------------------------------------
    // Scalars (mirror of LayoutMetrics.xaml)
    // ---------------------------------------------------------------
    public static double EdgeSpacing => D(nameof(EdgeSpacing), 10);
    public static double ControlCornerRadius => D(nameof(ControlCornerRadius), 9);
    public static double ToolbarButtonSize => D(nameof(ToolbarButtonSize), 42);
    public static double ToolbarIconSize => D(nameof(ToolbarIconSize), 24);

    /// <summary>
    ///     Deferred-content spinner delay (ms). Intentionally a compile-time <c>const</c>
    ///     because <c>DeferredContentView.DefaultLoadDelayMs</c> and
    ///     <c>UserAvatarGroup.AvatarImageLoadingDelay</c> consume it in <c>const</c> contexts.
    ///     Its value is mirrored in <c>LayoutMetrics.xaml</c> for the XAML consumers.
    /// </summary>
    public const int DeferredContentLoadDelayMs = 369;

    public static double FitContentLoadingMinHeight => D(nameof(FitContentLoadingMinHeight), 180);
    public static double ModalHeaderIconButtonSize => D(nameof(ModalHeaderIconButtonSize), 30);
    public static double ModalHeaderIconTitleSpacing => D(nameof(ModalHeaderIconTitleSpacing), 3);

    /// <summary>
    ///     Symmetric gap above AND below the bottom-sheet header row. The sheet chrome owns the
    ///     whole rhythm around the header: <see cref="ResolveFullScreenSheetTopPadding" /> reserves
    ///     exactly the status-bar inset, so this gap starts directly under the status bar, and the
    ///     same value separates the header from the body below. Sheet bodies must NOT add a top gap
    ///     of their own — they use <see cref="ModalBaseBodyMargin" /> (sides + bottom only).
    /// </summary>
    public static double SheetHeaderVerticalGap => D(nameof(SheetHeaderVerticalGap), 10);

    /// <summary>
    ///     Minimum height of the shared bottom-sheet header band (title + leading/trailing slots),
    ///     enforced as a floor on the header grid regardless of how short its content is. A close
    ///     button (30) + <see cref="SheetHeaderVerticalGap" /> ×2 alone left the band ~50dp, which
    ///     read as cramped; this gives every standard sheet header the same, comfortably-tall band
    ///     with its items exactly centered. The header still GROWS past this for taller content
    ///     (multi-line custom title, etc.) — it is a minimum, not a fixed height.
    /// </summary>
    public static double SheetHeaderMinHeight => D(nameof(SheetHeaderMinHeight), 64);

    /// <summary>
    ///     Font size of the helper-rendered bottom-sheet header title (18 × 0.9 = 16.2 — the title
    ///     read as oversized against the compact rows below it; 14.4 was tried first and undershot,
    ///     the title stopped reading as the band's heading). Changing it can never move the
    ///     header band: the band is pinned by <see cref="SheetHeaderMinHeight" /> (64) and the title
    ///     is the SHORTEST item in it — the close button (30) and any trailing action button (42)
    ///     both measure taller. Sheets must not render their own title label; they set
    ///     <c>G9BottomSheetOptions.Title</c> and inherit this (design guide §6).
    /// </summary>
    public static double SheetHeaderTitleFontSize => D(nameof(SheetHeaderTitleFontSize), 16.2);

    public static double ModalInnerIconSize => D(nameof(ModalInnerIconSize), 39);
    public static double ModalSheetHandleWidth => D(nameof(ModalSheetHandleWidth), 90);
    public static double ModalSheetHandleHeight => D(nameof(ModalSheetHandleHeight), 5);
    public static double ModalTargetHeaderHeight => D(nameof(ModalTargetHeaderHeight), 64);
    public static double ModalTargetTabBarHeight => D(nameof(ModalTargetTabBarHeight), 56);

    // Natural rendered height of one SampleCategoryItemView row: 12+12 padding + the ~39 dp
    // inner action icons (ModalInnerIconSize). Kept slightly generous so the fit-to-content
    // estimate never opens the target sheet a few dp short (which surfaced as a premature
    // inner scrollbar on the categories list). See SamplingTargetContentView.EstimateActiveTabsHeight.
    public static double ModalTargetCategoryRowHeight => D(nameof(ModalTargetCategoryRowHeight), 66);
    public static double ModalTargetFooterButtonHeight => D(nameof(ModalTargetFooterButtonHeight), 52);

    // ---------------------------------------------------------------
    // Semantic spacing tokens (new screens use these; never hardcode)
    // ---------------------------------------------------------------
    public static double SpacingXSmall => D(nameof(SpacingXSmall), 2);
    public static double SpacingSmall => D(nameof(SpacingSmall), 4);
    public static double SpacingMedium => D(nameof(SpacingMedium), 8);
    public static double SpacingLarge => D(nameof(SpacingLarge), 12);
    public static double SpacingXLarge => D(nameof(SpacingXLarge), 16);

    // Shared control-cell height (search box / action button cell rhythm).
    public static double ControlCellHeight => D(nameof(ControlCellHeight), 52);

    /// <summary>
    ///     Minimum tappable size of an interactive control (design guide §10 floor = 44; 48 gives real
    ///     finger slop). A control's hit target is its MEASURED bounds, not what it paints — see
    ///     <c>G9IconButton.MinimumTouchTarget</c>.
    /// </summary>
    public static double MinTouchTarget => D(nameof(MinTouchTarget), 48);

    /// <summary>
    ///     Boundary-preview thumbnail on a site card — the tallest thing in that row, so it alone drives
    ///     the card height (<c>SiteThumbnailSize + 2 × ListCardPadding</c>). Tune the card by tuning this.
    /// </summary>
    public static double SiteThumbnailSize => D(nameof(SiteThumbnailSize), 58);

    // ---------------------------
    // Shared bottom-sheet footer template (C#-only; not referenced from XAML)
    // ---------------------------
    public const double G9BottomSheetFooterTopBorderThickness = 1;
    public const int G9BottomSheetFooterMaxButtonsPerRow = 3;

    // Derived from resource-backed members so they track the single source.
    public static double G9BottomSheetFooterButtonHeight => ModalTargetFooterButtonHeight;
    public static double G9BottomSheetFooterButtonSpacing => EdgeSpacing;

    public static Thickness G9BottomSheetFooterPadding => new(EdgeSpacing);

    // ---------------------------
    // Bottom-sheet drag handle + backdrop "card recede" (C#-only; not in XAML)
    // ---------------------------
    public const double G9BottomSheetDragHandleTouchHeight = 30;
    public const double G9BottomSheetDragHandleWidth = 32;
    public const double G9BottomSheetDragHandleHeight = 4;
    public const double G9BottomSheetBackdropCardThreshold = 0.75;
    public const double G9BottomSheetBackdropCardMinScale = 0.93;
    public const double G9BottomSheetBackdropCardTranslationY = 12;

    // ---------------------------------------------------------------
    // Corner radius + thicknesses (mirror of LayoutMetrics.xaml)
    // ---------------------------------------------------------------
    public static CornerRadius ControlCornerRadiusValue => Cr(nameof(ControlCornerRadiusValue), new CornerRadius(9));

    public static Thickness EdgeHorizontalThickness => Th(nameof(EdgeHorizontalThickness), new Thickness(10, 0, 10, 0));

    public static Thickness BodyPadding => Th(nameof(BodyPadding), new Thickness(10));

    public static Thickness BodyElementMargin => Th(nameof(BodyElementMargin), new Thickness(10, 10, 10, 0));

    public static Thickness BodyElementMarginWithBottomGap => Th(nameof(BodyElementMarginWithBottomGap), new Thickness(10, 10, 10, 10));

    public static Thickness BodyTopMargin => Th(nameof(BodyTopMargin), new Thickness(0, 10, 0, 0));

    public static Thickness ItemBottomMargin => Th(nameof(ItemBottomMargin), new Thickness(0, 0, 0, 10));

    /// <summary>
    ///     The standard inset for a bottom-sheet BODY (and for a sticky footer): sides + bottom,
    ///     and deliberately NO top — the gap up to the sheet header is owned by the header itself
    ///     (<see cref="SheetHeaderVerticalGap" />). A body that adds its own top margin doubles that
    ///     gap. Valid as either a Margin or a Padding.
    /// </summary>
    public static Thickness ModalBaseBodyMargin => Th(nameof(ModalBaseBodyMargin), new Thickness(10, 0, 10, 10));

    public static Thickness SearchFieldPadding => Th(nameof(SearchFieldPadding), new Thickness(14, 0, 12, 0));

    public static Thickness CardPadding => Th(nameof(CardPadding), new Thickness(14, 12));

    /// <summary>
    ///     The ONE inset every virtualized list card uses — equal on all four sides, so a row's content
    ///     sits the same distance from every edge. Consumed by <c>ListCard</c>; never re-pick a padding
    ///     per list.
    /// </summary>
    public static Thickness ListCardPadding => Th(nameof(ListCardPadding), new Thickness(12));

    // Extra top space so an outlined input placed at the very top of a clipping container (a card
    // Border, or a ScrollView viewport edge) has room for its floating label, which sits ~half its
    // height ABOVE the field box (see G9Metrics.FloatingLabelFloatedY = -11). Without this the
    // first field's floating label is clipped to a half-glyph.
    public static Thickness OutlinedCardPadding => Th(nameof(OutlinedCardPadding), new Thickness(14, 22, 14, 14));

    public static Thickness OutlinedInputTopGap => Th(nameof(OutlinedInputTopGap), new Thickness(0, 16, 0, 0));

    /// <summary>
    ///     Side inset every floating map control keeps from the screen edge (the map itself is
    ///     edge-to-edge — design guide §12). The zoom rail and the tool-button stack read it, so a new
    ///     map control lines up with them by reading it too instead of picking its own number.
    /// </summary>
    public static double MapChromeEdgeSpacing => D(nameof(MapChromeEdgeSpacing), 12);

    /// <summary>
    ///     Margin of a bottom-anchored floating map card (the multi-selection bar):
    ///     <see cref="MapChromeEdgeSpacing" /> on ALL three free edges, so it floats over the map like
    ///     every other map control instead of sitting on the screen edge — which is also what keeps its
    ///     1dp semi-transparent stroke off the screen's last pixel row (the map used to show through it
    ///     as a hairline).
    /// </summary>
    public static Thickness MapBottomBarMargin =>
        Th(nameof(MapBottomBarMargin),
            new Thickness(MapChromeEdgeSpacing, 0, MapChromeEdgeSpacing, MapChromeEdgeSpacing));

    /// <summary>Corner radius of a floating map card — all four corners, since it is anchored to no edge.</summary>
    public static CornerRadius MapChromeCornerRadius =>
        Cr(nameof(MapChromeCornerRadius), new CornerRadius(24));

    /// <summary>
    ///     Top padding band reserved above a full-screen sheet's content. It is exactly the
    ///     status-bar (safe-area) inset — no extra padding — so the sheet's first row (the header)
    ///     starts directly under the status bar and the visible gap above the header items is
    ///     purely <see cref="SheetHeaderVerticalGap" />, matching the gap below them.
    /// </summary>
    public static double ResolveFullScreenSheetTopPadding(
        double safeAreaInset,
        bool useTopSafeAreaPadding,
        double additionalTopSafeAreaPadding,
        double? topSafeAreaPaddingOverride)
    {
        if (topSafeAreaPaddingOverride is { } overrideValue)
        {
            return Math.Max(0, overrideValue);
        }

        var safeAreaPadding = useTopSafeAreaPadding ? Math.Max(0, safeAreaInset) : 0;
        return Math.Max(0, safeAreaPadding + additionalTopSafeAreaPadding);
    }
}
