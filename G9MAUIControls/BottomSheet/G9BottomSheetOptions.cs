using System.Windows.Input;
using G9MAUIControls.Theming;
using Thickness = Microsoft.Maui.Thickness;
using G9MAUIControls.Hosting;
// SfG9BottomSheetContentWidthMode was the alias used while we vendored Syncfusion's
// SfG9BottomSheet. The vendored copy was removed in favor of G9SheetView which exposes the
// equivalent enum (Full / Custom). Keeping the alias here avoids touching every public
// surface that referenced the old name; new code should use G9SheetViewContentWidthMode
// directly.
using SfG9BottomSheetContentWidthMode = G9MAUIControls.BottomSheet.G9SheetViewContentWidthMode;

namespace G9MAUIControls.BottomSheet;

/// <summary>
///     App-level bottom sheet sizing mode. Kept independent from the rendered control.
/// </summary>
public enum G9BottomSheetSizeMode
{
    States,
    FitToContent
}

/// <summary>
///     App-level bottom sheet state contract used by <see cref="G9BottomSheetOptions" />.
/// </summary>
public enum G9BottomSheetState
{
    Peek,
    Medium,
    Large
}

/// <summary>
///     Where the helper-rendered title sits inside the 3-slot bottom sheet header.
/// </summary>
public enum G9BottomSheetHeaderTitlePlacement
{
    /// <summary>
    ///     Title is centered inside the middle slot (default for the shared template).
    /// </summary>
    Center,

    /// <summary>
    ///     Title sits immediately next to the leading slot (legacy behavior, useful when the
    ///     title length is large and the trailing slot has multiple tool icons).
    /// </summary>
    NearBack
}

/// <summary>
///     App-wide defaults for bottom sheets rendered by <see cref="G9BottomSheetHelper" />.
/// </summary>
public sealed record G9BottomSheetSettings
{
    public static G9BottomSheetSettings Default { get; } = new();

    /// <summary>
    ///     Base overlay color used when <see cref="G9BottomSheetOptions.WindowBackgroundColor" /> is not set.
    ///     The helper applies state-based opacity to this color.
    /// </summary>
    public Color ModalOverlayColor { get; init; } = Colors.Black;

    /// <summary>
    ///     Overlay opacity for the smallest configured modal state.
    /// </summary>
    public double ModalOverlayMinimumOpacity { get; init; } = 0.3;

    /// <summary>
    ///     Overlay opacity for the full-screen modal state.
    /// </summary>
    public double ModalOverlayMaximumOpacity { get; init; } = 0.9;

    /// <summary>
    ///     App-wide <em>open</em> animation duration in milliseconds — the time a sheet takes
    ///     to rise from <c>Hidden</c> all the way to <c>FullExpanded</c>. Defaults to
    ///     <c>300 ms</c> — a tuned value, not a default: the <c>150 ms</c> linear curve this
    ///     implementation started from felt abrupt. When <see cref="SizeScaledAnimationDuration" /> is
    ///     <c>true</c> (the default), partial openings (e.g. <c>Hidden → HalfExpanded</c>,
    ///     <c>HalfExpanded → FullExpanded</c>) consume a proportional slice of this value so
    ///     every sheet motion shares the same visual velocity. Per-sheet
    ///     <see cref="G9BottomSheetOptions.OpenAnimationDurationMs" /> overrides this when set.
    /// </summary>
    public double OpenAnimationDurationMs { get; init; } = 300;

    /// <summary>
    ///     App-wide <em>close</em> animation duration in milliseconds — the time a sheet takes
    ///     to recede from <c>FullExpanded</c> all the way to <c>Hidden</c>. Defaults to
    ///     <c>300 ms</c>. Same size-scaling rules apply as
    ///     <see cref="OpenAnimationDurationMs" />: a partial close (e.g. <c>HalfExpanded →
    ///     Hidden</c>) consumes a proportional slice. Per-sheet
    ///     <see cref="G9BottomSheetOptions.CloseAnimationDurationMs" /> overrides this when set.
    /// </summary>
    public double CloseAnimationDurationMs { get; init; } = 300;

    /// <summary>
    ///     When <c>true</c> (default), the resolved open/close duration is treated as the
    ///     time for a <em>full-height</em> traversal (<c>Hidden ↔ FullExpanded</c>) and
    ///     partial traversals consume a proportional slice. This keeps the sheet's visual
    ///     velocity constant — a <c>Hidden → HalfExpanded</c> open takes roughly half the time
    ///     of a <c>Hidden → FullExpanded</c> open, mirroring the iOS native sheet feel. Only
    ///     the sheet motion is size-scaled; the modal-overlay fade, state-aware top padding,
    ///     and backdrop card recede still use the configured duration unchanged so they stay
    ///     well-defined across short and long state transitions. Set to <c>false</c> to make
    ///     every sheet motion last the full configured duration regardless of distance.
    /// </summary>
    public bool SizeScaledAnimationDuration { get; init; } = true;

    /// <summary>
    ///     App-wide master switch for the backdrop "card recede" effect (the iOS-style page
    ///     scale/translate behind the sheet). Defaults to <c>true</c>. Set to <c>false</c> in
    ///     <c>MauiProgram.cs</c> to disable the effect across every page in the app — per-sheet
    ///     <see cref="G9BottomSheetOptions.EnableBackdropCardEffect" /> still acts as an extra
    ///     opt-out, but it cannot opt back in once the app-wide switch is off.
    /// </summary>
    public bool EnableBackdropCardEffect { get; init; } = true;

    /// <summary>
    ///     Color painted behind the page <c>ContentHost</c> so the area exposed by the backdrop
    ///     card recede stays dark regardless of the active theme — matching the iOS native
    ///     bottom-sheet look. The color is applied to the always-present <c>BackdropHost</c>
    ///     <see cref="BoxView" /> in <c>G9PageTemplate.xaml</c>; <see cref="Colors.Black" />
    ///     is the default and matches the iOS native sheet's recessed-page color.
    /// </summary>
    public Color BackdropCardColor { get; init; } = Colors.Black;

    internal G9BottomSheetSettings Normalize()
    {
        var minimumOpacity = Math.Clamp(ModalOverlayMinimumOpacity, 0, 1);
        var maximumOpacity = Math.Clamp(ModalOverlayMaximumOpacity, 0, 1);

        if (maximumOpacity < minimumOpacity)
        {
            (minimumOpacity, maximumOpacity) = (maximumOpacity, minimumOpacity);
        }

        // Clamp the animation durations to non-negative values. Zero is legal (instant snap,
        // primarily used by tests); negative values would either break Math.Ceiling chains or
        // produce a "wraparound" uint downstream when fed to MAUI's animation engine.
        var openMs = Math.Max(0, OpenAnimationDurationMs);
        var closeMs = Math.Max(0, CloseAnimationDurationMs);

        return this with
        {
            ModalOverlayColor = ModalOverlayColor,
            ModalOverlayMinimumOpacity = minimumOpacity,
            ModalOverlayMaximumOpacity = maximumOpacity,
            OpenAnimationDurationMs = openMs,
            CloseAnimationDurationMs = closeMs,
            SizeScaledAnimationDuration = SizeScaledAnimationDuration,
            EnableBackdropCardEffect = EnableBackdropCardEffect,
            BackdropCardColor = BackdropCardColor
        };
    }
}

/// <summary>
///     Options for app bottom sheets rendered by the current bottom sheet implementation.
/// </summary>
public sealed record G9BottomSheetOptions
{
    // ---------------------------
    // State / behavior
    // ---------------------------

    /// <summary>
    ///     Initial/current state when opening. Default is <see cref="G9BottomSheetState.Medium" />.
    /// </summary>
    public G9BottomSheetState CurrentState { get; init; } = G9BottomSheetState.Medium;

    /// <summary>
    ///     Allowed states (e.g. Peek, Medium, Large). Default is Medium and Large.
    /// </summary>
    public IList<G9BottomSheetState> States { get; init; } = [G9BottomSheetState.Medium, G9BottomSheetState.Large];

    /// <summary>
    ///     Size mode (fixed states or fit to content). Default is <see cref="G9BottomSheetSizeMode.States" />.
    /// </summary>
    public G9BottomSheetSizeMode SizeMode { get; init; } = G9BottomSheetSizeMode.States;

    /// <summary>
    ///     Whether the sheet is modal (blocks interaction with content behind). Default is true.
    /// </summary>
    public bool IsModal { get; init; } = true;

    /// <summary>
    ///     Allow user to close via gestures or background tap. Default is true.
    /// </summary>
    public bool IsCancelable { get; init; } = true;

    /// <summary>
    ///     Enable drag gestures. Default is true.
    /// </summary>
    public bool IsDraggable { get; init; } = true;

    /// <summary>
    ///     Show the drag handle. Default is true.
    /// </summary>
    public bool HasHandle { get; init; } = true;

    /// <summary>
     ///     Height when in Peek state (platform-dependent). Optional.
     /// </summary>
    public double? PeekHeight { get; init; }

    /// <summary>
    ///     Optional height used when mapped to a collapsed Syncfusion state.
    /// </summary>
    public double? CollapsedHeight { get; init; }

    /// <summary>
    ///     Optional ratio for the half-expanded state. Defaults to 0.5.
    /// </summary>
    public double? HalfExpandedRatio { get; init; }

    /// <summary>
    ///     Optional ratio for the full-expanded state. Defaults to 1.
    /// </summary>
    public double? FullExpandedRatio { get; init; }

    // ---------------------------
    // Layout / visual
    // ---------------------------

    /// <summary>
    ///     Internal padding. Default is 0 (content owns its spacing).
    /// </summary>
    public double Padding { get; init; }

    /// <summary>
    ///     Whether the helper inserts the STANDARD gap between the header's bottom hairline and the
    ///     first body element. Default <c>true</c> — that gap is the app-wide sheet standard and bodies
    ///     must NOT add a top gap of their own (design guide §3b), so leave this alone unless the body
    ///     genuinely must butt against the header.
    ///     <para>
    ///         Set it <c>false</c> only for a body whose FIRST element is itself a chrome band that has
    ///         to read as part of the header — a tab strip, a segmented control, a sticky filter bar.
    ///         For those, the standard gap leaves the band visually orphaned between two rules instead
    ///         of attached to the header. <b>Always comment the call site with why the sheet is an
    ///         exception</b>; a body that simply "looks tighter without it" is not one.
    ///     </para>
    ///     <para>
    ///         The flag drives BOTH the layout (the body-top margin in <c>CreateSheetContentRoot</c>)
    ///         and the measurement (<c>ResolveHelperChromeHeight</c>), so a fit-to-content sheet still
    ///         sizes correctly with the gap suppressed.
    ///     </para>
    /// </summary>
    public bool UseStandardBodyTopGap { get; init; } = true;

    /// <summary>
    ///     Top corner radius. Default is 12.
    /// </summary>
    public float CornerRadius { get; init; } = 12f;

    /// <summary>
    ///     Sheet body background color. Null keeps control defaults.
    /// </summary>
    public Color? BackgroundColor { get; init; } = G9Palette.Current.SurfaceContainer;

    /// <summary>
    ///     Window/dimming background color (modal mode). Null uses the app default overlay.
    /// </summary>
    public Color? WindowBackgroundColor { get; init; }

    /// <summary>
    ///     Optional content width mode for Syncfusion bottom sheets.
    /// </summary>
    public SfG9BottomSheetContentWidthMode ContentWidthMode { get; init; } = SfG9BottomSheetContentWidthMode.Full;

    /// <summary>
    ///     Optional bottom sheet content width when <see cref="ContentWidthMode" /> is Custom.
    /// </summary>
    public double? G9BottomSheetContentWidth { get; init; }

    /// <summary>
    ///     Optional per-sheet override for the <em>open</em> animation duration in milliseconds
    ///     (sheet rising / expanding toward a larger state). When <c>null</c> (default), the
    ///     helper falls back to <see cref="G9BottomSheetSettings.OpenAnimationDurationMs" /> so a
    ///     single <c>G9BottomSheetHelper.Configure(...)</c> call drives every sheet in the app.
    ///     When <see cref="G9BottomSheetSettings.SizeScaledAnimationDuration" /> is on (the
    ///     default), this value is treated as the duration for a full-height open from
    ///     <c>Hidden</c> to <c>FullExpanded</c>; partial openings (e.g. to <c>HalfExpanded</c>)
    ///     consume a proportional slice so the sheet's visual velocity stays constant across
    ///     state changes.
    /// </summary>
    public double? OpenAnimationDurationMs { get; init; }

    /// <summary>
    ///     Optional per-sheet override for the <em>close</em> animation duration in
    ///     milliseconds (sheet receding / collapsing toward a smaller state). When <c>null</c>
    ///     (default), the helper falls back to
    ///     <see cref="G9BottomSheetSettings.CloseAnimationDurationMs" />. Size-scaling rules are
    ///     symmetric with <see cref="OpenAnimationDurationMs" /> — this value represents the
    ///     duration of a full-height close from <c>FullExpanded</c> to <c>Hidden</c>; partial
    ///     closes consume a proportional slice.
    /// </summary>
    public double? CloseAnimationDurationMs { get; init; }

    /// <summary>
    ///     When true, full-screen sheets reserve the current page top safe-area inset above their header/body.
    /// </summary>
    public bool UseTopSafeAreaPadding { get; init; } = true;

    /// <summary>
    ///     Extra top padding added to full-screen sheets after the safe-area inset and default gap.
    /// </summary>
    public double AdditionalTopSafeAreaPadding { get; init; }

    /// <summary>
    ///     Explicit full-screen top padding. When set, this replaces the safe-area/default-gap calculation.
    /// </summary>
    public double? TopSafeAreaPaddingOverride { get; init; }

    /// <summary>
    ///     Renders the helper-owned drag handle for full-screen toolbar sheets.
    /// </summary>
    public bool ShowFullScreenDragHandle { get; init; } = true;

    // ---------------------------
    // Backdrop "card recede" effect
    // ---------------------------
    // When the visible sheet height crosses a configurable threshold (default 75% of the
    // full-screen height), the helper pushes the page behind the sheet inward — scaling it
    // down and translating it down a few logical pixels — so the open sheet looks like a card
    // that has been laid over a recessed page. The effect is purely visual: it only adjusts
    // ContentHost.Scale and ContentHost.TranslationY, both of which map to native compositor
    // transforms on every supported MAUI target (Android, iOS, Mac Catalyst, Windows) and are
    // hardware-accelerated, so nothing is re-laid out or re-measured while dragging.

    /// <summary>
    ///     When true (default), the page <c>ContentHost</c> behind the sheet recedes (scales
    ///     down + translates down slightly) once the sheet visible-height ratio crosses
    ///     <see cref="BackdropCardEffectThreshold" />. Set to <c>false</c> to keep the
    ///     background page perfectly still even when the sheet is fully expanded.
    /// </summary>
    public bool EnableBackdropCardEffect { get; init; } = true;

    /// <summary>
    ///     Sheet visible-height ratio (0..1) at which the backdrop card effect starts. Below
    ///     this ratio the page behind the sheet stays untouched; from this ratio up to 1.0 the
    ///     scale and translation interpolate to their maximum values. Defaults to 0.75.
    /// </summary>
    public double BackdropCardEffectThreshold { get; init; } =
        G9LayoutMetrics.G9BottomSheetBackdropCardThreshold;

    // ---------------------------
    // Commands
    // ---------------------------

    public ICommand? OpeningCommand { get; init; }

    public object? OpeningCommandParameter { get; init; }

    public ICommand? OpenedCommand { get; init; }

    public object? OpenedCommandParameter { get; init; }

    public ICommand? ClosingCommand { get; init; }

    public object? ClosingCommandParameter { get; init; }

    public ICommand? ClosedCommand { get; init; }

    public object? ClosedCommandParameter { get; init; }

    // ---------------------------
    // Android-specific
    // ---------------------------

    /// <summary>
    ///     Optional Android theme resource id for the bottom sheet dialog.
    /// </summary>
    public int? AndroidTheme { get; init; }

    /// <summary>
    ///     Optional Android max width.
    /// </summary>
    public int? AndroidMaxWidth { get; init; }

    /// <summary>
    ///     Optional Android max height.
    /// </summary>
    public int? AndroidMaxHeight { get; init; }

    /// <summary>
    ///     Optional Android margin.
    /// </summary>
    public Thickness? AndroidMargin { get; init; }

    /// <summary>
    ///     Optional Android half-expanded ratio.
    /// </summary>
    public float? AndroidHalfExpandedRatio { get; init; }

    /// <summary>
    ///     Optional Android expanded-corners behavior.
    /// </summary>
    public bool? AndroidShouldRemoveExpandedCorners { get; init; }

    // ---------------------------
    // Windows-specific
    // ---------------------------

    public double? WindowsMaxWidth { get; init; }

    public double? WindowsMaxHeight { get; init; }

    public double? WindowsMinWidth { get; init; }

    public double? WindowsMinHeight { get; init; }

    // ---------------------------
    // iOS-specific
    // ---------------------------

    /// <summary>
    ///     When true (iOS only), animates the overlay color during open/close/large-state transitions.
    /// </summary>
    public bool AnimateOverlayToColorOnIos { get; init; } = true;

    // ---------------------------
    // Deferred loading
    // ---------------------------

    /// <summary>
    ///     When true (default), bottom sheet content is wrapped in a <c>DeferredContentView</c>
    ///     that shows a centered spinner placeholder until the sheet open animation completes.
    /// </summary>
    public bool DeferContent { get; init; } = true;

    /// <summary>
    ///     Delay in milliseconds before deferred content is constructed or swapped in.
    ///     Defaults to <see cref="G9LayoutMetrics.DeferredContentLoadDelayMs" />.
    /// </summary>
    public int LoadDelayMs { get; init; } = DeferredContentView.DefaultLoadDelayMs;

    /// <summary>
    ///     When true, deferred loading placeholders in fit-content sheets use the full
    ///     available page height. Use for modal-like pickers that resolve to full-screen content.
    /// </summary>
    public bool UseFullScreenLoadingPlaceholder { get; init; }

    /// <summary>
    ///     Known final content height (logical px) for a deferred fit-to-content sheet whose size
    ///     is predictable up-front (e.g. a selection list = rows × rowHeight + chrome). When set,
    ///     the loading-spinner placeholder uses this height so the sheet opens directly at its final
    ///     size — the spinner and the swapped-in content share one height, eliminating the
    ///     spinner→content resize "jump". The value is still clamped to
    ///     <see cref="MaxFitToContentHeightRatio" />, so an over-estimate just opens at the cap and
    ///     scrolls. Ignored when <see cref="UseFullScreenLoadingPlaceholder" /> is set or the sheet
    ///     isn't deferred fit-to-content.
    /// </summary>
    public double? DeferredLoadingPlaceholderHeight { get; init; }

    /// <summary>
    ///     When true (default, and <see cref="DeferContent" /> is on), the deferred content is
    ///     revealed with a covered crossfade instead of an instant swap: the loading placeholder
    ///     (spinner or skeleton — both paint an OPAQUE background) stays on screen for
    ///     ~120 ms while the freshly-built tree lays out hidden underneath, then the content fades
    ///     in as one settled unit. This masks piecemeal native realization AND the first-frame
    ///     font-glyph race (icons flashing as empty rectangles before their icon font applies).
    ///     Set <c>false</c> for an instant swap.
    /// </summary>
    public bool FadeDeferredContentIn { get; init; } = true;

    /// <summary>
    ///     Shape of the loading placeholder shown while deferred content builds. Default
    ///     <see cref="G9BottomSheetLoadingSkeleton.None" /> keeps the centered spinner. The other
    ///     values render a <c>G9Shimmer</c> skeleton (gray shape rows swept by a highlight
    ///     band whose animation runs on the platform render/compositor thread, so it keeps moving
    ///     even while the UI thread builds the heavy body — see <c>G9Shimmer.md</c>).
    ///     Pair with <see cref="LoadingSkeletonRowCount" /> for row-based skeletons.
    /// </summary>
    public G9BottomSheetLoadingSkeleton LoadingSkeleton { get; init; } = G9BottomSheetLoadingSkeleton.None;

    /// <summary>
    ///     Row count for row-based <see cref="LoadingSkeleton" /> shapes (clamped to a sane range
    ///     by the skeleton builder). 0 lets the builder pick its default.
    /// </summary>
    public int LoadingSkeletonRowCount { get; init; }

    /// <summary>
    ///     When this sheet opens STACKED on top of another sheet (automatic stacking), the parent
    ///     sheet visually recedes while this one opens: it sinks by ~25% of its height and fades
    ///     to invisible, simultaneously with this sheet's open animation — so a smaller child
    ///     never sits awkwardly on a taller parent, and the parent (still alive, state intact)
    ///     slides back in the moment this sheet closes. Full-screen parents never recede (they are
    ///     their flow's backdrop). Default <c>true</c>. Set <c>false</c> for stacked panels that
    ///     should render over a still-visible partial-height parent.
    /// </summary>
    public bool RecedeParentOnStack { get; init; } = true;

    /// <summary>
    ///     Explicit key for the session height memo. Prebuilt bodies are memoized automatically by
    ///     their type name; FACTORY content has no type before it is built, so a factory sheet that
    ///     should open at its remembered height must supply a stable key (include whatever varies
    ///     the height, e.g. a title or row count). Ignored for non-fit / full-screen sheets.
    /// </summary>
    public string? HeightMemoKey { get; init; }

    // ---------------------------
    // Shared header template (3-slot: leading / title / trailing)
    // ---------------------------
    // The helper builds a 3-column header (leading | title | trailing) whenever
    // ShowToolbar = true. By default:
    //   leading  = RTL-aware back button (when ShowCloseButton = true)
    //   title    = Title text (centered or near-back, see HeaderTitlePlacement)
    //   trailing = ToolbarItems (or ToolbarItemsFactory output)
    // Each slot can be overridden with a custom View, two adjacent slots can be merged
    // into a single spanning view, or the entire header can be replaced via HeaderView.
    // Spacing, padding, icon button sizes, and footer button sizes all come from
    // G9LayoutMetrics so every sheet shares the same look.
    // ---------------------------

    /// <summary>
    ///     When true, the helper renders the shared 3-slot header above the sheet content.
    ///     Default is false.
    /// </summary>
    public bool ShowToolbar { get; init; }

    /// <summary>
    ///     When true and <see cref="ShowToolbar" /> is true, the default leading slot renders an
    ///     RTL-aware back arrow that closes the sheet (or invokes <see cref="OnBackRequested" />).
    ///     Ignored when <see cref="HeaderLeadingView" />, <see cref="HeaderLeadingAndTitleView" />,
    ///     or <see cref="HeaderView" /> overrides the leading area.
    /// </summary>
    public bool ShowCloseButton { get; init; }

    /// <summary>
    ///     When true, the default leading button (see <see cref="ShowCloseButton" />) renders a
    ///     CLOSE (×) glyph instead of the RTL-aware back arrow. Use for a top-level sheet that the
    ///     user dismisses (not a stacked sub-sheet they navigate back from). The tap behaviour is
    ///     identical — it closes the sheet / invokes <see cref="OnBackRequested" />. Ignored when a
    ///     custom leading view overrides the leading area.
    /// </summary>
    public bool UseCloseIcon { get; init; }

    /// <summary>
    ///     Title text shown in the middle slot when <see cref="ShowToolbar" /> is true and no
    ///     custom title view is provided.
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    ///     Optional toolbar items rendered inside the trailing slot. Use
    ///     <see cref="G9BottomSheetToolbarItem" /> for icons, busy state, and active badge support.
    /// </summary>
    public IReadOnlyList<ToolbarItem>? ToolbarItems { get; init; }

    /// <summary>
    ///     Optional factory for toolbar items that depend on the lazily-created content view.
    ///     Invoked on the UI thread after the content factory has created the view.
    /// </summary>
    public Func<View, IReadOnlyList<ToolbarItem>>? ToolbarItemsFactory { get; init; }

    /// <summary>
    ///     Where to anchor the default title label inside the middle slot. Defaults to
    ///     <see cref="G9BottomSheetHeaderTitlePlacement.Center" />, which is the most common case
    ///     across the app. Use <see cref="G9BottomSheetHeaderTitlePlacement.NearBack" /> when the
    ///     title is long or the trailing slot has multiple action icons.
    /// </summary>
    public G9BottomSheetHeaderTitlePlacement HeaderTitlePlacement { get; init; } =
        G9BottomSheetHeaderTitlePlacement.Center;

    /// <summary>
    ///     When <see cref="HeaderTitlePlacement" /> is
    ///     <see cref="G9BottomSheetHeaderTitlePlacement.Center" />, the helper renders the header
    ///     as a 3-column grid where the leading and trailing slots are equally sized (Star
    ///     widths). That keeps the centered title visually centered on the screen even when one
    ///     of the side slots is empty (e.g. back arrow on the left, no toolbar icons on the
    ///     right). Set this to <c>false</c> to fall back to the legacy Auto / Star / Auto layout
    ///     where an empty trailing slot collapses to zero width and the centered title is no
    ///     longer perfectly centered. Has no effect when <see cref="HeaderTitlePlacement" /> is
    ///     <see cref="G9BottomSheetHeaderTitlePlacement.NearBack" /> or when a spanned header view
    ///     (<see cref="HeaderLeadingAndTitleView" /> / <see cref="HeaderTitleAndTrailingView" />)
    ///     or full <see cref="HeaderView" /> override is in use.
    /// </summary>
    public bool ReserveEmptyHeaderSlots { get; init; } = true;

    /// <summary>
    ///     Replaces the entire header (all three slots) with the given view. When set, the other
    ///     <c>Header*View</c>, <see cref="ShowCloseButton" />, <see cref="Title" />, and
    ///     <see cref="ToolbarItems" /> properties are ignored.
    /// </summary>
    public View? HeaderView { get; init; }

    /// <summary>
    ///     Custom view for the leading slot (column 0). Overrides the auto-built back button.
    /// </summary>
    public View? HeaderLeadingView { get; init; }

    /// <summary>
    ///     Custom view for the middle slot (column 1). Overrides the auto-built title label and
    ///     <see cref="HeaderTitlePlacement" />.
    /// </summary>
    public View? HeaderTitleView { get; init; }

    /// <summary>
    ///     Custom view for the trailing slot (column 2). Overrides the auto-built toolbar items
    ///     stack.
    /// </summary>
    public View? HeaderTrailingView { get; init; }

    /// <summary>
    ///     Custom view that spans the leading and middle slots (columns 0..1). When set, the
    ///     trailing slot still renders (custom <see cref="HeaderTrailingView" /> or default
    ///     toolbar items). <see cref="HeaderLeadingView" /> and <see cref="HeaderTitleView" />
    ///     are ignored.
    /// </summary>
    public View? HeaderLeadingAndTitleView { get; init; }

    /// <summary>
    ///     Custom view that spans the middle and trailing slots (columns 1..2). When set, the
    ///     leading slot still renders (custom <see cref="HeaderLeadingView" /> or default back
    ///     button). <see cref="HeaderTitleView" /> and <see cref="HeaderTrailingView" /> are
    ///     ignored.
    /// </summary>
    public View? HeaderTitleAndTrailingView { get; init; }

    // ---------------------------
    // Shared footer template
    // ---------------------------

    /// <summary>
    ///     Replaces the entire footer area with the given view. When set, <see cref="FooterButtons" />
    ///     is ignored.
    /// </summary>
    public View? FooterView { get; init; }

    /// <summary>
    ///     Action buttons rendered in the shared footer area. Buttons are sized equally and
    ///     laid out in rows of <see cref="FooterMaxButtonsPerRow" /> (default 3); if there are
    ///     more buttons than fit on one row, the extras wrap to a new row using the same equal
    ///     widths and spacing (driven by <see cref="G9LayoutMetrics" />).
    /// </summary>
    public IReadOnlyList<View>? FooterButtons { get; init; }

    /// <summary>
    ///     Maximum number of buttons rendered in a single footer row. Defaults to
    ///     <see cref="G9LayoutMetrics.G9BottomSheetFooterMaxButtonsPerRow" />.
    /// </summary>
    public int FooterMaxButtonsPerRow { get; init; } =
        G9LayoutMetrics.G9BottomSheetFooterMaxButtonsPerRow;

    /// <summary>
    ///     When true (default), the device hardware/software back button closes the sheet.
     ///     When false, hardware back is ignored unless <see cref="OnBackRequested" /> handles it.
    /// </summary>
    public bool HardwareBackCloses { get; init; } = true;

    /// <summary>
    ///     Optional callback invoked on a back-navigation request (toolbar back button or hardware key).
    ///     Receives the source and returns whether the sheet should close. When set, the sheet's
    ///     <c>IsCancelable</c> is forced to false so the callback can intercept hardware back.
    /// </summary>
    public Func<G9BottomSheetBackRequestSource, Task<G9BottomSheetBackAction>>? OnBackRequested { get; init; }

    /// <summary>
    ///     Upper bound, as a fraction of the screen height, that a <see cref="G9BottomSheetSizeMode.FitToContent" />
    ///     sheet may grow to before it stops growing and lets its content scroll inside the now
    ///     bounded body. Default <c>0.75</c> (75%). Short content still fits exactly; only tall
    ///     content (a long list) hits this cap. Ignored for non-fit-to-content sheets.
    /// </summary>
    public double MaxFitToContentHeightRatio { get; init; } = 0.75;

    // ---------------------------
    // Presets
    // ---------------------------

    /// <summary>
    ///     Default options for the Farms and Greenhouses bottom sheet: half-expanded (Medium), can expand to Large.
    /// </summary>
    public static G9BottomSheetOptions DefaultOptions()
    {
        return new G9BottomSheetOptions
        {
            CurrentState = G9BottomSheetState.Medium,
            States = [G9BottomSheetState.Medium, G9BottomSheetState.Large],
            IsModal = true,
            IsCancelable = true,
            HasHandle = true,
            IsDraggable = true,
            SizeMode = G9BottomSheetSizeMode.States,
            HalfExpandedRatio = 0.5,
            FullExpandedRatio = 1
        };
    }

    /// <summary>
    ///     Options for a bottom sheet that auto-sizes to fit its content.
    ///     FitToContent is a helper sizing mode; do not combine it with explicit States/CurrentState/PeekHeight.
    /// </summary>
    public static G9BottomSheetOptions FitToContentOptions()
    {
        return new G9BottomSheetOptions
        {
            SizeMode = G9BottomSheetSizeMode.FitToContent,
            IsCancelable = true,
            HasHandle = true,
            IsDraggable = true,
            DeferContent = true
        };
    }

    /// <summary>
    ///     Full-screen sheet without a drag handle (legacy alias for picker flows that need a
    ///     bottom-sheet that fully covers the screen without a toolbar).
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Drag is disabled by default for full-screen sheets.</b> Full-screen sheets
    ///         have only one allowed state (<see cref="G9BottomSheetState.Large" />), so
    ///         drag-to-resize cannot do anything useful, and the helper convention is that
    ///         full-screen flows close through their own toolbar / footer buttons (or hardware
    ///         back) rather than a drag-down gesture. Set <c>IsDraggable = true</c> on a
    ///         specific call site if a flow genuinely needs the drag-down close gesture.
    ///     </para>
    /// </remarks>
    public static G9BottomSheetOptions FullScreenWithoutHandleOptions()
    {
        return new G9BottomSheetOptions
        {
            CurrentState = G9BottomSheetState.Large,
            States = [G9BottomSheetState.Large],
            IsCancelable = false,
            IsDraggable = false,
            HasHandle = false,
            DeferContent = true
        };
    }

    /// <summary>
    ///     Modal-replacement preset: a full-screen sheet with the built-in toolbar
    ///     (back arrow + title) on top.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>Drag is disabled by default for full-screen sheets.</b> Full-screen sheets
    ///         have only one allowed state (<see cref="G9BottomSheetState.Large" />), so
    ///         drag-to-resize cannot do anything useful, and the helper convention is that
    ///         full-screen flows close through the toolbar back button / hardware back rather
    ///         than a drag-down gesture. Set <c>IsDraggable = true</c> on a specific call site
    ///         if a flow genuinely needs the drag-down close gesture.
    ///     </para>
    /// </remarks>
    public static G9BottomSheetOptions FullScreenModalOptions(
        string? title = null,
        bool showCloseButton = true,
        bool hardwareBackCloses = true,
        IReadOnlyList<ToolbarItem>? toolbarItems = null)
    {
        return new G9BottomSheetOptions
        {
            CurrentState = G9BottomSheetState.Large,
            States = [G9BottomSheetState.Large],
            SizeMode = G9BottomSheetSizeMode.States,
            IsCancelable = false,
            HasHandle = false,
            IsDraggable = false,
            CornerRadius = 0f,
            BackgroundColor = G9Palette.Current.Background,
            ShowToolbar = true,
            ShowCloseButton = showCloseButton,
            Title = title,
            ToolbarItems = toolbarItems,
            HardwareBackCloses = hardwareBackCloses
        };
    }

    /// <summary>
    ///     Full-screen preset for a view that paints its OWN full-bleed header /
    ///     coloured background — no built-in toolbar and no top safe-area band. The
    ///     view's content reaches the very top edge (the camera / cutout overlays it),
    ///     so the hosted view MUST set <c>SafeAreaEdges="None"</c> on its root or a
    ///     band of sheet background would show above its header.
    /// </summary>
    /// <remarks>
    ///     This is the shared "diagnostics chrome" recipe used by every diagnostics
    ///     surface that draws its own header/background instead of the standard
    ///     toolbar: Admin Diagnostics (<c>AdminDiagnosticsG9BottomSheetOptions.FullScreen</c>),
    ///     the Live Diagnostic overlay, and the Send / Save full-diagnostic-report
    ///     sheet. Combines <c>ShowToolbar=false</c> with
    ///     <c>UseTopSafeAreaPadding=false</c> + <c>TopSafeAreaPaddingOverride=0</c> +
    ///     <c>AdditionalTopSafeAreaPadding=0</c> so no helper padding is reserved above
    ///     the view. See <c>Views/Pages/AdminDiagnostics/AdminDiagnostics.md</c> and the
    ///     G9BottomSheet guide → "Full-Screen Safe Area".
    /// </remarks>
    public static G9BottomSheetOptions FullScreenEdgeToEdgeModalOptions(bool hardwareBackCloses = true)
    {
        return FullScreenModalOptions(showCloseButton: false, hardwareBackCloses: hardwareBackCloses) with
        {
            ShowToolbar = false,
            UseTopSafeAreaPadding = false,
            TopSafeAreaPaddingOverride = 0,
            AdditionalTopSafeAreaPadding = 0
        };
    }
}
