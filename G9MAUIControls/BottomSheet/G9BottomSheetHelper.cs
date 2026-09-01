using G9MAUIControls.Controls;
using G9MAUIControls.Popup;
using G9MAUIControls.Toast;
using G9MAUIControls.Hosting;
using G9MAUIControls.Helpers;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Controls.Shapes;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using DeferredContentView = G9MAUIControls.Hosting.DeferredContentView;
using Thickness = Microsoft.Maui.Thickness;
using View = Microsoft.Maui.Controls.View;
// G9SheetView replaces the previously-vendored Syncfusion `CustomizedSfG9BottomSheet`. The
// helper kept its old `Sf*` aliases through the migration so the body of the file (which is
// large and full of references to these types) doesn't need a sweeping rename. Only the
// underlying namespace switched from `VendoredSyncfusionG9BottomSheet` to the new
// `G9SheetView` folder.
using CustomizedSfG9BottomSheet = G9MAUIControls.BottomSheet.G9SheetView;
using SfG9BottomSheetAllowedState = G9MAUIControls.BottomSheet.G9SheetViewAllowedState;
using SfG9BottomSheetContentWidthMode = G9MAUIControls.BottomSheet.G9SheetViewContentWidthMode;
using SfG9BottomSheetState = G9MAUIControls.BottomSheet.G9SheetViewState;
using SfStateChangedEventArgs = G9MAUIControls.BottomSheet.G9SheetViewStateChangedEventArgs;
using SfPositionChangedEventArgs = G9MAUIControls.BottomSheet.G9SheetViewPositionChangedEventArgs;
#if ANDROID
using AndroidView = Android.Views.View;
using AndroidViewGroup = Android.Views.ViewGroup;
#endif

using G9MAUIControls.Icons;

namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Helper for presenting Syncfusion bottom sheet overlays on the current visible
///     <c>G9PageBase</c>.
///     Supports stacking multiple bottom sheets on top of each other.
/// </summary>
public static class G9BottomSheetHelper
{
    #region Fields And Properties

    private static readonly Lock StackLock = new();
    private static readonly ConditionalWeakTable<Grid, CustomizedSfG9BottomSheet?> PrimarySheets = new();
    private static readonly ConditionalWeakTable<Grid, Stack<CustomizedSfG9BottomSheet>> StackedSheets = new();
    private static readonly ConditionalWeakTable<Grid, PrimarySheetHostState> PrimarySheetStates = new();
    private static readonly ConditionalWeakTable<CustomizedSfG9BottomSheet, SheetBehaviorState> SheetBehaviorStates = new();
    private static readonly ConditionalWeakTable<CustomizedSfG9BottomSheet, Grid> SheetOverlayHosts = new();
    private static readonly ConditionalWeakTable<CustomizedSfG9BottomSheet, BoxView> ModalOverlays = new();
    private static readonly ConditionalWeakTable<CustomizedSfG9BottomSheet, EventHandler<SfPositionChangedEventArgs>> PositionListeners = new();
    private static readonly ConditionalWeakTable<CustomizedSfG9BottomSheet, EventHandler<G9SheetViewBackRequestedEventArgs>> BackRequestedListeners = new();
    private static readonly ConditionalWeakTable<CustomizedSfG9BottomSheet, BackdropCardBinding> BackdropCardBindings = new();
    private static readonly ConditionalWeakTable<CustomizedSfG9BottomSheet, FitToContentSizeTracker> FitToContentSizeTrackers = new();
    private static G9BottomSheetSettings Settings { get; set; } = G9BottomSheetSettings.Default;

    private const string FitContentResizeAnimationName = "G9BottomSheetFitToContentResize";
    private const string ModalOverlayAnimationName = "G9BottomSheetModalOverlay";
    private const int CloseAnimationTimeoutMs = 1200;
    private const int CloseCleanupGraceMs = 48;
    private const double DragCloseThreshold = 72;
    private const double DragCloseMaxTranslation = 96;
    private const double SheetStateDragThreshold = 56;
    private const int SheetContentDragResetDelayMs = 220;
    private const uint FitContentResizeAnimationRate = 16;
    // Debounce window for fit-to-content remeasures driven by content MeasureInvalidated bursts.
    private const int FitContentRemeasureDebounceMs = 48;

    // Absolute lower bound for a fit-to-content sheet. Applied INSTEAD of the loading floor
    // (G9LayoutMetrics.FitContentLoadingMinHeight) when the resolved height is authoritative —
    // a caller-supplied placeholder, a height-memo hit, or a real measure of loaded content. Those
    // may legitimately resolve below the 180dp loading floor (e.g. a two-row menu) and must be
    // honored; this only prevents a degenerate sliver sheet whose header/grabber band would clip.
    private const double FitContentAbsoluteMinHeight = 80;

    // The height memo — settled BODY heights of fit-to-content sheet bodies, keyed by body
    // identity + width + culture + font scale, PERSISTED across restarts (version-stamped,
    // device-local) so even the first open per session starts near the real height. All access
    // goes through G9BottomSheetHeightMemoStore; values are only ever a better opening guess (the
    // settle passes / tracker still correct with an animated resize). Main-thread only.

    // Close-duration multiplier used when a queued replacement is waiting behind the closing
    // sheet (see ShowAutoG9BottomSheetCore / ReplaceG9BottomSheet): the close runs at half speed cost
    // so the dead close→open gap between chained sheets is halved.
    private const double QueuedReplaceCloseDurationScale = 0.5;

    // Stacked "parent recede": while a stacked child is open, its parent sheet sinks by this
    // fraction of its visible height and fades to invisible (G9BottomSheetOptions.RecedeParentOnStack),
    // then slides back in when the child closes — the parent stays ALIVE (rendering + state kept),
    // so back-navigation has no rebuild, no spinner, and no glyph re-realization.
    private static readonly ConditionalWeakTable<CustomizedSfG9BottomSheet, CustomizedSfG9BottomSheet> StackedRecededParents = new();
    private const double StackParentRecedeHeightFraction = 0.25;
    private const uint StackParentRecedeDurationMs = 220;

    #endregion

    #region Methods

    /// <summary>
    ///     Shows content in the bottom sheet of the current visible page.
    ///     <para>
    ///         Stacking is automatic: when another sheet is already open the new sheet is opened
    ///         stacked on top of it; otherwise it opens as the primary sheet. Callers no longer
    ///         pick primary-vs-stacked explicitly. To replace the current sheet rather than stack
    ///         on it, close the current sheet first.
    ///     </para>
    /// </summary>
    public static void ShowG9BottomSheet(
        View content,
        G9BottomSheetOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var request = SheetContentRequest.FromContent(content);
        ShowAutoG9BottomSheet(request, options);
    }

    /// <summary>
    ///     Shows content created from a factory in the bottom sheet of the current visible page.
    ///     The content factory is hosted by <see cref="DeferredContentView" /> so expensive content is created after open.
    ///     <para>Stacking is automatic — see <see cref="ShowG9BottomSheet(View, G9BottomSheetOptions?)" />.</para>
    /// </summary>
    public static void ShowG9BottomSheet(
        Func<View> contentFactory,
        G9BottomSheetOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(contentFactory);
        var request = SheetContentRequest.FromFactory(contentFactory);
        ShowAutoG9BottomSheet(request, options);
    }

    /// <summary>
    ///     Replaces the CURRENT sheet with new content — the step-transition primitive for chained
    ///     sheet flows (tree/pot operations, bulk selection). When the open sheet and the
    ///     replacement are both modal fit-to-content sheets (and nothing is stacked), the body is
    ///     swapped <b>in place</b>: the old body fades out, the new body fades in, and the sheet
    ///     height animates between the two — no close/open cycle, no dead time, no spinner between
    ///     steps. Otherwise it falls back to the classic close-then-open path, with the close sped
    ///     up because a replacement is already waiting (<c>MarkQuickCloseForQueuedReplace</c>).
    ///     <para>
    ///         On the in-place path the replaced step's <c>ClosingCommand</c>/<c>ClosedCommand</c>
    ///         do <b>not</b> run — a replace is an ADVANCE, not a close (identical to the
    ///         <c>advancing = true</c> convention the map flows already use around the fallback
    ///         path). The new options' commands take over for the eventual real close.
    ///     </para>
    /// </summary>
    public static void ReplaceG9BottomSheet(
        View content,
        G9BottomSheetOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(content);
        var request = SheetContentRequest.FromContent(content);
        var resolvedOptions = options ?? G9BottomSheetOptions.FitToContentOptions();

        // Same single throttle window as ShowG9BottomSheet so a rapid double-tap on a menu row
        // produces one transition. The fallback below goes through ShowAutoG9BottomSheetCore
        // (un-throttled) for exactly this reason.
        if (!G9SafeCommand.TryThrottle(nameof(ShowG9BottomSheet)))
        {
            return;
        }

        var host = G9ModalHostRegistry.GetCurrentHostOrThrow();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var currentSheet = GetPrimarySheet(host.OverlayHost);
            bool hasStackedSheets;
            lock (StackLock)
            {
                hasStackedSheets = StackedSheets.GetValue(host.OverlayHost, static _ => new Stack<CustomizedSfG9BottomSheet>()).Count > 0;
            }

            if (currentSheet is not null &&
                !hasStackedSheets &&
                IsSheetAlive(currentSheet) &&
                currentSheet.IsOpen &&
                !IsSheetClosing(currentSheet) &&
                CanMorphSheet(currentSheet, resolvedOptions))
            {
                MorphPrimarySheet(currentSheet, request, resolvedOptions);
                return;
            }


            // Fallback: classic replace — close whatever is on top (runs the outgoing step's
            // Closing/Closed commands exactly like the legacy CloseTopG9BottomSheet + ShowG9BottomSheet
            // pattern) and let the show pipeline queue the new sheet behind the sped-up close.
            var topSheet = GetTopSheet(host);
            if (topSheet is not null)
            {
                MarkQuickCloseForQueuedReplace(topSheet);
                HandleBackRequest(topSheet, G9BottomSheetBackRequestSource.ToolbarButton);
            }

            ShowAutoG9BottomSheetCore(request, resolvedOptions);
        });
    }

    // In-place replace is only safe when the sheet geometry model doesn't change: both steps are
    // modal fit-to-content sheets. Full-screen (States) steps keep the queued close-then-open
    // path — morphing across sizing models would need state remaps, top-padding rewires, and
    // grabber changes that aren't worth the risk for the ~200 ms they'd save.
    private static bool CanMorphSheet(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions newOptions)
    {
        if (!SheetBehaviorStates.TryGetValue(sheet, out var behavior) || behavior is null)
        {
            return false;
        }

        var oldOptions = behavior.Options;
        return oldOptions.SizeMode == G9BottomSheetSizeMode.FitToContent &&
               newOptions.SizeMode == G9BottomSheetSizeMode.FitToContent &&
               oldOptions.IsModal &&
               newOptions.IsModal &&
               !newOptions.UseFullScreenLoadingPlaceholder;
    }

    private static void MorphPrimarySheet(
        CustomizedSfG9BottomSheet sheet,
        SheetContentRequest request,
        G9BottomSheetOptions options)
    {
        var previousBehavior = SheetBehaviorStates.GetValue(
            sheet,
            static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));
        var previousHeight = previousBehavior.LastFitContentHeight > 0
            ? previousBehavior.LastFitContentHeight
            : sheet.CollapsedHeight;
        var oldRoot = sheet.G9BottomSheetContent;

        // Tear down the old step's sizing machinery and pending loads. The old step's
        // Closing/Closed commands intentionally do NOT run here (replace = advance).
        DetachFitToContentSizeTracking(sheet);
        CancelDeferredLoad(sheet);
        sheet.AbortAnimation(FitContentResizeAnimationName);


        // The new body is prebuilt (the caller constructed it before calling), so there is no
        // heavy build to hide behind a spinner and no open animation to keep it off — deferring
        // here would only insert a between-steps spinner flash.
        var morphOptions = options with { DeferContent = false };

        // Fresh behavior state for the new step. LastFitContentHeight carries over so the first
        // resolve animates FROM the current height; IsFitContentSettled=true marks the sheet live
        // (post-open) so every correction animates.
        var behavior = new SheetBehaviorState(morphOptions)
        {
            LastFitContentHeight = previousHeight,
            IsFitContentSettled = true,
            FitHeightMemoKey = BuildFitHeightMemoKey(request.Content, sheet, morphOptions)
        };
        SheetBehaviorStates.AddOrUpdate(sheet, behavior);

        ApplyOptions(sheet, morphOptions);
        var handle = new G9BottomSheetHandleImpl(sheet);
        var newRoot = CreateSheetContentRoot(sheet, request, handle, morphOptions);
        newRoot.Opacity = 0;

        G9SafeCommand.RunSafe(
            () => RunMorphTransitionAsync(sheet, behavior, oldRoot, newRoot, morphOptions, previousHeight),
            new G9SafeCommandOptions
            {
                Source = nameof(G9BottomSheetHelper),
                ShowErrorG9Popup = false,
                EnableThrottle = false,
                RunActionOnMainThread = true,
                ThrottleKey = $"{nameof(G9BottomSheetHelper)}.{nameof(MorphPrimarySheet)}"
            });
    }

    private static async Task RunMorphTransitionAsync(
        CustomizedSfG9BottomSheet sheet,
        SheetBehaviorState behavior,
        View? oldRoot,
        View newRoot,
        G9BottomSheetOptions morphOptions,
        double previousHeight)
    {
        try
        {
            if (oldRoot is not null)
            {
                oldRoot.InputTransparent = true;
                await oldRoot.FadeToAsync(0, 90, Easing.CubicIn).ConfigureAwait(true);
            }

            if (!IsSheetAlive(sheet) || IsSheetClosing(sheet))
            {
                return;
            }

            sheet.G9BottomSheetContent = newRoot;
            RunCommand(morphOptions.OpeningCommand, morphOptions.OpeningCommandParameter);

            // Resolve the new height up-front when a memo from a previous visit exists — the
            // height tween then runs together with the fade-in. Without a memo the sheet holds
            // its current height (the measure tier's cold-measure hold) and the settle passes
            // animate to the real height as soon as the platform can measure the new body.
            var fullHeight = ResolveFullScreenHeight();
            if (fullHeight > 0 &&
                behavior.FitHeightMemoKey is { } memoKey &&
                G9BottomSheetHeightMemoStore.TryGet(memoKey, out var memoBody))
            {
                var ratioCap = Math.Clamp(morphOptions.MaxFitToContentHeightRatio, 0.2, 1.0);
                var targetHeight = Math.Clamp(
                    memoBody + ResolveHelperChromeHeight(sheet, morphOptions),
                    FitContentAbsoluteMinHeight,
                    fullHeight * ratioCap);

                if (Math.Abs(targetHeight - previousHeight) > 1)
                {
                    ApplyFitToContentMetrics(sheet, previousHeight, targetHeight, fullHeight, morphOptions, animate: true);
                    behavior.LastFitContentHeight = targetHeight;
                }

                UpdateModalOverlayBackground(sheet, morphOptions, ratioOverride: targetHeight / fullHeight, animated: false);
            }

            // Re-arm the sizing machinery for the new body (mirrors ConfigureSheetContent).
            ScheduleFitToContentRefresh(sheet, newRoot, morphOptions);
            ScheduleFitToContentRefresh(sheet, newRoot, morphOptions, delayMs: 160);
            ScheduleFitToContentRefresh(sheet, newRoot, morphOptions, delayMs: 380);
            AttachFitToContentSizeTracking(sheet, newRoot, morphOptions);

            await newRoot.FadeToAsync(1, 140, Easing.CubicOut).ConfigureAwait(true);

            // From the user's perspective the new step is now fully open.
            RunCommand(morphOptions.OpenedCommand, morphOptions.OpenedCommandParameter);
            TriggerDeferredLoad(sheet, behavior);
        }
        finally
        {
            newRoot.Opacity = 1;
        }
    }

    /// <summary>
    ///     Opens a bottom sheet <b>instantly</b> showing only a spinner (zero work on the tap),
    ///     then — after the open animation — runs <paramref name="buildAsync" /> to load data and
    ///     construct the real content off the critical open frame, then swaps it in. If
    ///     <paramref name="buildAsync" /> throws, <paramref name="onError" /> runs; when that is
    ///     <c>null</c> the default behavior shows an error popup and closes the sheet.
    ///     <para>
    ///         This is the recommended entry point for heavy sheet bodies (e.g. the sampling
    ///         target sheet / block-info sheet) whose synchronous construction would otherwise
    ///         delay the open by hundreds of milliseconds and let a rapid double-tap open two
    ///         sheets. Because the sheet opens immediately, the single-key open throttle still
    ///         collapses a double-tap into one sheet.
    ///     </para>
    ///     <para>
    ///         <b>Threading:</b> <paramref name="buildAsync" /> may await data work, but the
    ///         returned <see cref="View" /> must be constructed on the UI thread — see
    ///         <see cref="ProcessingSheetContentView" />. Return <c>null</c> to close the sheet
    ///         quietly (target gone / nothing to show).
    ///     </para>
    ///     <para>Stacking is automatic — see <see cref="ShowG9BottomSheet(View, G9BottomSheetOptions?)" />.</para>
    /// </summary>
    /// <param name="buildAsync">Async builder for the real content, run after the sheet is visible.</param>
    /// <param name="options">Sheet options. Defaults to <see cref="G9BottomSheetOptions.FitToContentOptions" />.</param>
    /// <param name="onError">
    ///     Optional failure handler (receives the exception and the sheet handle). When <c>null</c>,
    ///     the default shows an error popup and closes the sheet.
    /// </param>
    /// <param name="loadingHeight">Spinner placeholder height for fit-to-content sheets (default 160).</param>
    public static void ShowProcessingG9BottomSheet(
        Func<IG9BottomSheetHandle, CancellationToken, Task<View?>> buildAsync,
        G9BottomSheetOptions? options = null,
        Func<Exception, IG9BottomSheetHandle, Task>? onError = null,
        double loadingHeight = 160)
    {
        ArgumentNullException.ThrowIfNull(buildAsync);

        // The processing view paints its own spinner and is an IDeferredSheetLoad, so the helper
        // opens it immediately and drives buildAsync after the open animation (no DeferredContentView
        // double-spinner). Force DeferContent off so the content path does not wrap it.
        var resolvedOptions = (options ?? G9BottomSheetOptions.FitToContentOptions()) with { DeferContent = false };
        var processingView = new ProcessingSheetContentView(buildAsync, onError, loadingHeight);
        var request = SheetContentRequest.FromContent(processingView);
        ShowAutoG9BottomSheet(request, resolvedOptions);
    }

    /// <summary>
    ///     Shows a full-screen modal-style bottom sheet. If another sheet is already open,
    ///     the new one is stacked above it.
    /// </summary>
    public static Task<IG9BottomSheetHandle> ShowFullScreenAsync(
        VisualElement parentElement,
        View content,
        G9BottomSheetOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(parentElement);
        ArgumentNullException.ThrowIfNull(content);
        var request = SheetContentRequest.FromContent(content);
        return ShowFullScreenAsync(request, options);
    }

    /// <summary>
    ///     Shows a full-screen modal-style bottom sheet with deferred content construction.
    /// </summary>
    public static Task<IG9BottomSheetHandle> ShowFullScreenAsync(
        VisualElement parentElement,
        Func<View> contentFactory,
        G9BottomSheetOptions? options = null,
        Action<View>? onContentCreated = null)
    {
        ArgumentNullException.ThrowIfNull(parentElement);
        ArgumentNullException.ThrowIfNull(contentFactory);
        var request = SheetContentRequest.FromFactory(contentFactory, onContentCreated);
        return ShowFullScreenAsync(request, options);
    }

    private static Task<IG9BottomSheetHandle> ShowFullScreenAsync(
        SheetContentRequest contentRequest,
        G9BottomSheetOptions? options)
    {
        var resolvedOptions = options ?? G9BottomSheetOptions.FullScreenModalOptions();

        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            var host = G9ModalHostRegistry.GetCurrentHostOrThrow();
            var hasOpenSheet = GetOpenSheetCount() > 0;

            if (hasOpenSheet)
            {
                return OpenStackedSheet(host, contentRequest, resolvedOptions);
            }

            return OpenPrimarySheet(host.OverlayHost,
                new PendingPrimarySheetRequest(contentRequest, resolvedOptions, host.Page.FlowDirection));
        });
    }

    private static string Sanitize(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "-";
        }

        return value.Replace('|', '/').Replace('\n', ' ').Replace('\r', ' ');
    }

    // Unified sheet presentation. Automatic stacking lives here: a single show entry point
    // detects whether a sheet is already open and either stacks on top of it or opens a fresh
    // primary sheet. This replaces the old split between ShowPrimaryG9BottomSheet (always replaced
    // the primary) and the removed ShowStackedG9BottomSheet (always created a stacked sheet, even
    // when nothing was open — which produced orphan stacked sheets with no backdrop owner). The
    // factory full-screen path (ShowFullScreenAsync) uses the same detection.
    private static void ShowAutoG9BottomSheet(SheetContentRequest contentRequest, G9BottomSheetOptions? options)
    {
        // Throttle synchronously at the entry point (not inside the dispatch below) so a rapid
        // double-tap collapses to a single open. If the check ran after the open-sheet decision,
        // two taps queued before the first sheet opened would race: the first would open the
        // primary and the second would find it open and stack a duplicate on top. Guarding here
        // also protects stacked opens (e.g. double-tapping "New sample" must not open two).
        var resolvedOptions = options ?? G9BottomSheetOptions.DefaultOptions();

        if (!G9SafeCommand.TryThrottle(nameof(ShowG9BottomSheet)))
        {
            return;
        }

        ShowAutoG9BottomSheetCore(contentRequest, resolvedOptions);
    }

    // The un-throttled show pipeline. Split out so ReplaceG9BottomSheet (which throttles once at
    // its own entry) can fall back to the classic queued open without consuming a second
    // throttle window (which would silently drop the show).
    private static void ShowAutoG9BottomSheetCore(SheetContentRequest contentRequest, G9BottomSheetOptions resolvedOptions)
    {
        var host = G9ModalHostRegistry.GetCurrentHostOrThrow();

        MainThread.BeginInvokeOnMainThread(() =>
        {

            // Automatic stacking: when any sheet (primary or stacked) is currently open, the new
            // sheet is opened stacked on top of it. To replace the current sheet instead, the
            // caller closes it first.
            if (GetOpenSheetCount() > 0)
            {
                OpenStackedSheet(host, contentRequest, resolvedOptions);
                return;
            }

            var state = GetPrimarySheetState(host.OverlayHost);
            var request = new PendingPrimarySheetRequest(contentRequest, resolvedOptions, host.Page.FlowDirection);

            lock (StackLock)
            {
                if (state.IsTransitioning)
                {
                    state.PendingRequest = request;
                    return;
                }

                // GetOpenSheetCount() == 0 but a previous primary sheet can still be alive while
                // its close animation runs (Close() flips IsOpen false synchronously). Queue the
                // new request and let that sheet's cleanup open it so the two never overlap.
                var currentSheet = GetPrimarySheet(host.OverlayHost);
                if (currentSheet is not null && IsSheetAlive(currentSheet))
                {
                    state.PendingRequest = request;
                    state.IsTransitioning = true;
                    MarkQuickCloseForQueuedReplace(currentSheet);
                    CloseSheet(currentSheet);
                    return;
                }
            }

            OpenPrimarySheet(host.OverlayHost, request);
        });
    }

    /// <summary>
    ///     Closes the topmost stacked bottom sheet. If no stacked sheets exist, closes the primary sheet.
    /// </summary>
    public static void CloseTopG9BottomSheet()
    {
        if (!G9ModalHostRegistry.TryGetCurrentHost(out var host))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var target = GetTopSheet(host);
            if (target is not null)
            {
                HandleBackRequest(target, G9BottomSheetBackRequestSource.ToolbarButton);
            }
        });
    }

    /// <summary>
    ///     Closes the current bottom sheet.
    /// </summary>
    public static void CloseG9BottomSheet()
    {
        if (!G9ModalHostRegistry.TryGetCurrentHost(out var host))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(() =>
        {
            // Close all stacked sheets first
            CloseAllStackedSheets(host.OverlayHost);

            var primarySheet = GetPrimarySheet(host.OverlayHost);
            CloseSheet(primarySheet ?? host.G9BottomSheet);
        });
    }

    /// <summary>
    ///     Gets the current page bottom sheet, if available.
    /// </summary>
    public static CustomizedSfG9BottomSheet? GetCurrentG9BottomSheet()
    {
        if (!G9ModalHostRegistry.TryGetCurrentHost(out var host))
        {
            return null;
        }

        return GetPrimarySheet(host.OverlayHost) ?? host.G9BottomSheet;
    }

    internal static bool TryGetCurrentSheetOverlayHost(out Layout overlayHost, out G9PageBase? page)
    {
        overlayHost = null!;
        page = null;

        if (!G9ModalHostRegistry.TryGetCurrentHost(out var host))
        {
            return false;
        }

        var sheet = GetTopSheet(host);
        if (sheet is null || !sheet.IsOpen)
        {
            return false;
        }

        if (!SheetOverlayHosts.TryGetValue(sheet, out var sheetOverlayHost))
        {
            return false;
        }

        overlayHost = sheetOverlayHost;
        page = host.Page;
        return true;
    }

    /// <summary>
    ///     Returns a handle for controlling the current page bottom sheet.
    /// </summary>
    public static IG9BottomSheetHandle InitG9BottomSheet()
    {
        return new G9BottomSheetHandleImpl();
    }

    /// <summary>
    ///     Applies app-wide bottom-sheet behavior defaults. Call once during app startup.
    /// </summary>
    public static void Configure(G9BottomSheetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Settings = settings.Normalize();
    }

    /// <summary>
    ///     Returns the current app-wide backdrop card color (used to paint the area exposed by
    ///     the page recede effect). Called by <c>G9PageBase</c> when applying
    ///     its template so light-theme pages stay dark behind the receded card.
    /// </summary>
    public static Color GetBackdropCardColor()
    {
        return Settings.BackdropCardColor;
    }

    // Resolvers for the effective open/close animation durations on a per-sheet basis:
    //   - Per-sheet G9BottomSheetOptions.{Open,Close}AnimationDurationMs wins when set (non-null).
    //   - Otherwise the app-wide G9BottomSheetSettings.{Open,Close}AnimationDurationMs is used.
    // Both helper-side animations (modal-overlay fade, close-completion wait, opened-command
    // delay, fit-to-content resize) and the vendored sheet's AnimateG9BottomSheet route through
    // these resolvers so a single G9BottomSheetHelper.Configure(...) call at startup retunes
    // every sheet across all four MAUI targets at once.
    //
    // Direction is chosen by the caller — the open resolver feeds sheet-rising / expanding
    // motions (overlay fade-in, grow-fit, opened-command delay), the close resolver feeds
    // sheet-receding / collapsing motions (overlay fade-out, close-cleanup wait, shrink-fit).
    // The vendored sheet's AnimationDurationProvider (see ApplyOptions) picks the direction
    // dynamically per motion so drag-snap animations land on the correct side too.
    private static double ResolveOpenAnimationDurationMs(G9BottomSheetOptions options)
    {
        return options.OpenAnimationDurationMs ?? Settings.OpenAnimationDurationMs;
    }

    private static double ResolveCloseAnimationDurationMs(G9BottomSheetOptions options)
    {
        return options.CloseAnimationDurationMs ?? Settings.CloseAnimationDurationMs;
    }

    // Computes the animation duration for a single sheet motion (called once per motion by
    // the vendored CustomizedSfG9BottomSheet.AnimateG9BottomSheet through AnimationDurationProvider).
    // Picks Open vs Close from the direction of travel, then optionally size-scales by the
    // fraction of full screen height the sheet will traverse — so Hidden→Half takes ~half the
    // time of Hidden→Full, mirroring the iOS native sheet velocity.
    private static int ResolveSheetMotionDurationMs(
        G9BottomSheetOptions options,
        double currentTranslation,
        double targetTranslation,
        double height)
    {
        // Sheet rising (target < current) = expanding/opening; falling = collapsing/closing.
        // The Syncfusion sheet sits below the visible area when Hidden (TranslationY == Height)
        // and at TranslationY == 0 when FullExpanded, so "target < current" is unambiguous.
        var isOpening = targetTranslation < currentTranslation;
        var baseDuration = isOpening
            ? ResolveOpenAnimationDurationMs(options)
            : ResolveCloseAnimationDurationMs(options);

        if (!Settings.SizeScaledAnimationDuration || height <= 0)
        {
            return (int)Math.Round(Math.Max(0, baseDuration));
        }

        var delta = Math.Abs(targetTranslation - currentTranslation);
        var scale = Math.Clamp(delta / height, 0d, 1d);
        return (int)Math.Round(Math.Max(0, baseDuration * scale));
    }

    // Picks the effective LoadDelayMs for a DeferredContentView wrapping a full-screen sheet
    // body. The DeferredContentView shows a centered spinner for LoadDelayMs and *then* runs
    // the heavy ContentFactory() / Content = newContent swap on the UI thread. If that swap
    // lands while AnimateG9BottomSheet is still tweening TranslationY (typical when the user
    // raises the configured open duration above the default LoadDelayMs of 369 ms), the
    // resulting measure+layout pass eats 1–3 frames on Android — the visible "small stop"
    // mid-rise users report on full-screen opens.
    //
    // Fix: for full-screen presentations we ensure the spinner stays painted for at least the
    // *configured* open animation duration (size-scaling is irrelevant here since a full-screen
    // sheet always traverses the entire screen height, so scale == 1), plus a 2-frame buffer
    // for the vendored sheet's finished-callback (UpdateG9BottomSheetHeight + final
    // RaisePositionChanged) to settle before the layout-heavy swap runs. For partial sheets
    // the original LoadDelayMs is preserved so we don't introduce a "spinner lingers after
    // settle" delay on half/collapsed initial opens — partial sheets size-scale their motion
    // and rarely run long enough to clash with LoadDelayMs anyway. If a programmer explicitly
    // raises options.LoadDelayMs above the computed floor (e.g. to mask a slow factory) we
    // honor their value via Math.Max.
    private static int ResolveDeferredContentLoadDelayMs(G9BottomSheetOptions options)
    {
        if (!IsFullScreenPresentation(options))
        {
            return options.LoadDelayMs;
        }

        // ~2 frames at 60 fps. Covers AnimateG9BottomSheet's finished-callback work
        // (UpdateG9BottomSheetHeight + RaisePositionChanged) before the spinner swaps out.
        const int settleBufferMs = 32;
        var animationDuration = (int)Math.Ceiling(ResolveOpenAnimationDurationMs(options));
        return Math.Max(options.LoadDelayMs, animationDuration + settleBufferMs);
    }

    /// <summary>
    ///     Returns the number of currently open sheets (primary + stacked).
    /// </summary>
    public static int GetOpenSheetCount()
    {
        if (!G9ModalHostRegistry.TryGetCurrentHost(out var host))
        {
            return 0;
        }

        var primarySheet = GetPrimarySheet(host.OverlayHost);
        var count = primarySheet?.IsOpen == true
            ? 1
            : host.G9BottomSheet.IsOpen
                ? 1
                : 0;

        lock (StackLock)
        {
            var stack = StackedSheets.GetValue(host.OverlayHost, static _ => new Stack<CustomizedSfG9BottomSheet>());
            count += stack.Count;
        }

        return count;
    }

    private static CustomizedSfG9BottomSheet CreateG9BottomSheet(G9BottomSheetOptions options)
    {
        var sheet = new CustomizedSfG9BottomSheet
        {
            IsOpen = false,
            IsVisible = true,
            Content = CreateTransparentSheetContent()
        };

        ApplyOptions(sheet, options);
        return sheet;
    }

    private static View CreateTransparentSheetContent()
    {
        return new Grid
        {
            InputTransparent = true,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
    }

    private static void OnPrimarySheetStateChanged(object? sender, SfStateChangedEventArgs e)
    {
        if (sender is not CustomizedSfG9BottomSheet sheet)
        {
            return;
        }


        ApplyStateAwareTopPadding(sheet, animated: true);
        UpdateModalOverlayBackground(sheet, animated: true);
        CloseFixedFullScreenSheetFromNativeDismissState(sheet, e.NewState);

        if (e.NewState != SfG9BottomSheetState.Hidden)
        {
            return;
        }

        SchedulePrimarySheetCleanup(sheet);
        ScheduleBackdropCardTransformReset(sheet, "primaryHidden");
    }

    private static void OnStackedSheetStateChanged(object? sender, SfStateChangedEventArgs e)
    {
        if (sender is not CustomizedSfG9BottomSheet sheet)
        {
            return;
        }


        ApplyStateAwareTopPadding(sheet, animated: true);
        UpdateModalOverlayBackground(sheet, animated: true);
        CloseFixedFullScreenSheetFromNativeDismissState(sheet, e.NewState);

        if (e.NewState != SfG9BottomSheetState.Hidden)
        {
            return;
        }

        // Slide the receded parent back in, in parallel with this child's close animation.
        RestoreRecededParent(sheet);
        ScheduleStackedSheetCleanup(sheet);
        ScheduleBackdropCardTransformReset(sheet, "stackedHidden");
    }

    private static void CloseFixedFullScreenSheetFromNativeDismissState(
        CustomizedSfG9BottomSheet sheet,
        SfG9BottomSheetState newState)
    {
        if (newState is SfG9BottomSheetState.Hidden or SfG9BottomSheetState.FullExpanded)
        {
            return;
        }

        var behavior = SheetBehaviorStates.GetValue(
            sheet,
            static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));

        if (!IsFullScreenPresentation(behavior.Options) || !behavior.Options.IsDraggable)
        {
            return;
        }

        G9SafeCommand.RunSafe(
            () => CloseFixedFullScreenSheetFromNativeDismissStateAsync(sheet, behavior.Options),
            new G9SafeCommandOptions
            {
                Source = nameof(G9BottomSheetHelper),
                ShowErrorG9Popup = false,
                EnableThrottle = false,
                ThrottleKey = $"{nameof(G9BottomSheetHelper)}.{nameof(CloseFixedFullScreenSheetFromNativeDismissState)}"
            });
    }

    private static async Task CloseFixedFullScreenSheetFromNativeDismissStateAsync(
        CustomizedSfG9BottomSheet sheet,
        G9BottomSheetOptions options)
    {
        if (options.OnBackRequested is not null)
        {
            var action = await options.OnBackRequested(G9BottomSheetBackRequestSource.ToolbarButton).ConfigureAwait(true);
            if (action == G9BottomSheetBackAction.Close)
            {
                await CloseSheetAsync(sheet).ConfigureAwait(false);
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (IsSheetAlive(sheet) && sheet.IsOpen)
                {
                    sheet.State = SfG9BottomSheetState.FullExpanded;
                }
            }).ConfigureAwait(false);
            return;
        }

        await CloseSheetAsync(sheet).ConfigureAwait(false);
    }

    private static void CleanupStackedSheet(CustomizedSfG9BottomSheet sheet)
    {
        sheet.StateChanged -= OnStackedSheetStateChanged;

        // Safety net for close paths that bypass the Hidden state handler (cleanup fallbacks):
        // no-op when the Hidden handler already restored the parent.
        RestoreRecededParent(sheet, instant: true);

        Grid? parentGrid = null;
        if (sheet.Parent is Grid parent)
        {
            parentGrid = parent;
            lock (StackLock)
            {
                var stack = StackedSheets.GetValue(parent, static _ => new Stack<CustomizedSfG9BottomSheet>());
                if (stack.Count > 0 && ReferenceEquals(stack.Peek(), sheet))
                {
                    stack.Pop();
                }
                else
                {
                    // Rebuild stack without this sheet (rare case: not on top)
                    var remaining = new Stack<CustomizedSfG9BottomSheet>(stack.Where(s => !ReferenceEquals(s, sheet)).Reverse());
                    stack.Clear();
                    foreach (var s in remaining)
                    {
                        stack.Push(s);
                    }
                }
            }
        }

        RunClosedCommandIfNeeded(sheet);
        CleanupSheetVisuals(sheet, parentGrid);
    }

    private static void CleanupPrimarySheet(CustomizedSfG9BottomSheet sheet)
    {
        sheet.StateChanged -= OnPrimarySheetStateChanged;

        Grid? overlayHost = null;
        if (sheet.Parent is Grid parentGrid)
        {
            overlayHost = parentGrid;
            SetPrimarySheet(parentGrid, null);
        }

        RunClosedCommandIfNeeded(sheet);
        CleanupSheetVisuals(sheet, overlayHost);
        ResetBackdropCardTransformForHost(overlayHost);

        if (overlayHost is null)
        {
            return;
        }

        PendingPrimarySheetRequest? pendingRequest = null;
        lock (StackLock)
        {
            var state = GetPrimarySheetState(overlayHost);
            state.IsTransitioning = false;
            pendingRequest = state.PendingRequest;
            state.PendingRequest = null;
        }

        if (pendingRequest is not null)
        {
            OpenPrimarySheet(overlayHost, pendingRequest);
        }
    }

    private static void SchedulePrimarySheetCleanup(CustomizedSfG9BottomSheet sheet)
    {
        ScheduleSheetCleanup(sheet, () => CleanupPrimarySheet(sheet));
    }

    private static void ScheduleStackedSheetCleanup(CustomizedSfG9BottomSheet sheet)
    {
        ScheduleSheetCleanup(sheet, () => CleanupStackedSheet(sheet));
    }

    private static void ScheduleSheetCleanup(CustomizedSfG9BottomSheet sheet, Action cleanup)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var delayMs = ResolveCloseCleanupDelayMs(sheet);
            if (delayMs > 0)
            {
                await Task.Delay(delayMs).ConfigureAwait(true);
            }

            if (ShouldCleanupClosedSheet(sheet))
            {
                cleanup();
            }
        });
    }

    private static void ScheduleBackdropCardTransformReset(CustomizedSfG9BottomSheet sheet, string source)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var delayMs = ResolveCloseCleanupDelayMs(sheet) + CloseCleanupGraceMs;
            if (delayMs > 0)
            {
                await Task.Delay(delayMs).ConfigureAwait(true);
            }

            ResetBackdropCardTransformForCurrentHost(source);
        });
    }

    private static void ResetBackdropCardTransformForCurrentHost(string source)
    {
        if (G9ModalHostRegistry.TryGetCurrentHost(out var host))
        {
            ResetBackdropCardTransform(host.Page.ContentHost);
        }

    }

    private static void ScheduleClosedSheetCleanupFallback(CustomizedSfG9BottomSheet sheet)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var delayMs = ResolveCloseCleanupDelayMs(sheet) + CloseCleanupGraceMs;
            if (delayMs > 0)
            {
                await Task.Delay(delayMs).ConfigureAwait(true);
            }

            if (!ShouldCleanupClosedSheet(sheet) || sheet.Parent is not Grid parentGrid)
            {
                return;
            }

            if (ReferenceEquals(GetPrimarySheet(parentGrid), sheet))
            {
                CleanupPrimarySheet(sheet);
                return;
            }

            CleanupStackedSheet(sheet);
        });
    }

    private static bool ShouldCleanupClosedSheet(CustomizedSfG9BottomSheet sheet)
    {
        return IsSheetAlive(sheet) &&
               (!sheet.IsOpen || sheet.State == SfG9BottomSheetState.Hidden);
    }

    private static void CloseAllStackedSheets(Grid overlayHost)
    {
        lock (StackLock)
        {
            var stack = StackedSheets.GetValue(overlayHost, static _ => new Stack<CustomizedSfG9BottomSheet>());
            while (stack.Count > 0)
            {
                var sheet = stack.Pop();
                sheet.StateChanged -= OnStackedSheetStateChanged;
                // Whole-stack teardown: the parents are closing too — drop the recede link
                // WITHOUT the restore animation (a fade-in on a sheet that is about to close
                // would ghost). Their transforms leave the tree with them.
                StackedRecededParents.Remove(sheet);
                CloseSheet(sheet);
                RunClosedCommandIfNeeded(sheet);
                CleanupSheetVisuals(sheet, overlayHost);
            }
        }
    }

    private static void CloseSheet(CustomizedSfG9BottomSheet sheet)
    {
        var closeRequested = false;

        try
        {
            if (!IsSheetAlive(sheet))
            {
                return;
            }

            if (sheet.IsOpen || sheet.State != SfG9BottomSheetState.Hidden)
            {
                RunClosingCommandIfNeeded(sheet);
                AbortSheetAnimationsForClose(sheet);
                sheet.Close();
                closeRequested = true;
            }
            else
            {
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (InvalidOperationException)
        {
            sheet.IsOpen = false;
            sheet.State = SfG9BottomSheetState.Hidden;
            closeRequested = true;
        }

        if (closeRequested)
        {
            ScheduleForceClosedStateFallback(sheet);
            ScheduleClosedSheetCleanupFallback(sheet);
        }
    }

    private static void ScheduleForceClosedStateFallback(CustomizedSfG9BottomSheet sheet)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            var delayMs = ResolveCloseCleanupDelayMs(sheet) + CloseCleanupGraceMs;
            if (delayMs > 0)
            {
                await Task.Delay(delayMs).ConfigureAwait(true);
            }

            if (!IsSheetAlive(sheet) || (!sheet.IsOpen && sheet.State == SfG9BottomSheetState.Hidden))
            {
                return;
            }

            // This firing is a defect signal (the close animation or state machine stalled).
            sheet.IsOpen = false;
            sheet.State = SfG9BottomSheetState.Hidden;
            ScheduleClosedSheetCleanupFallback(sheet);
        });
    }

    private static void AbortSheetAnimationsForClose(CustomizedSfG9BottomSheet sheet)
    {
        sheet.AbortAnimation(FitContentResizeAnimationName);
    }

    private static async Task CloseSheetAsync(CustomizedSfG9BottomSheet sheet)
    {
        TaskCompletionSource<bool>? closedSignal = null;
        EventHandler<SfStateChangedEventArgs>? onClosed = null;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (!IsSheetAlive(sheet) || (!sheet.IsOpen && sheet.State == SfG9BottomSheetState.Hidden))
            {
                return;
            }

            closedSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            onClosed = (_, e) =>
            {
                if (e.NewState == SfG9BottomSheetState.Hidden)
                {
                    closedSignal.TrySetResult(true);
                }
            };
            sheet.StateChanged += onClosed;
            CloseSheet(sheet);
        }).ConfigureAwait(false);

        if (closedSignal is null || onClosed is null)
        {
            return;
        }

        await Task.WhenAny(closedSignal.Task, Task.Delay(CloseAnimationTimeoutMs)).ConfigureAwait(false);

        var delayMs = ResolveCloseCleanupDelayMs(sheet);
        if (delayMs > 0)
        {
            await Task.Delay(delayMs).ConfigureAwait(false);
        }

        await MainThread.InvokeOnMainThreadAsync(() => sheet.StateChanged -= onClosed).ConfigureAwait(false);
    }

    private static int ResolveCloseCleanupDelayMs(CustomizedSfG9BottomSheet sheet)
    {
        var behavior = SheetBehaviorStates.GetValue(
            sheet,
            static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));

        // The cleanup delay covers the worst-case close animation length, so we use the
        // (non-scaled) Close duration — even a full FullExpanded → Hidden close must finish
        // before we tear down visuals. CloseDurationScale (< 1 when a queued replacement is
        // waiting) shortens both the motion and this wait in lockstep.
        return Math.Clamp(
            (int)Math.Ceiling(ResolveCloseAnimationDurationMs(behavior.Options) * behavior.CloseDurationScale),
            0,
            CloseAnimationTimeoutMs);
    }

    // Marks a closing sheet as "a replacement is already queued behind you": its close motion and
    // close-cleanup wait run at QueuedReplaceCloseDurationScale so the next sheet opens sooner.
    // The scale is per-sheet state, so ordinary closes keep the configured feel.
    private static void MarkQuickCloseForQueuedReplace(CustomizedSfG9BottomSheet sheet)
    {
        var behavior = SheetBehaviorStates.GetValue(
            sheet,
            static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));
        behavior.CloseDurationScale = QueuedReplaceCloseDurationScale;
    }

    /// <summary>
    ///     Handles Android hardware/system back before the app page receives it.
    /// </summary>
    public static bool HandleHardwareBackPressed()
    {
        if (!G9ModalHostRegistry.TryGetCurrentHost(out var host))
        {
            return false;
        }

        var sheet = GetTopSheet(host);
        if (sheet is null || !sheet.IsOpen)
        {
            return false;
        }

        HandleBackRequest(sheet, G9BottomSheetBackRequestSource.HardwareButton);
        return true;
    }

    private static CustomizedSfG9BottomSheet? GetTopSheet(ModalHost host)
    {
        lock (StackLock)
        {
            var stack = StackedSheets.GetValue(host.OverlayHost, static _ => new Stack<CustomizedSfG9BottomSheet>());
            while (stack.Count > 0)
            {
                var candidate = stack.Peek();
                if (IsSheetAlive(candidate) && candidate.IsOpen)
                {
                    return candidate;
                }

                stack.Pop();
            }
        }

        var primarySheet = GetPrimarySheet(host.OverlayHost);
        if (primarySheet?.IsOpen == true)
        {
            return primarySheet;
        }

        return host.G9BottomSheet.IsOpen ? host.G9BottomSheet : null;
    }

    private static void HandleBackRequest(CustomizedSfG9BottomSheet sheet, G9BottomSheetBackRequestSource source)
    {
        G9SafeCommand.RunSafe(
            () => HandleBackRequestAsync(sheet, source),
            new G9SafeCommandOptions
            {
                Source = nameof(G9BottomSheetHelper),
                ShowErrorG9Popup = false,
                EnableThrottle = true,
                ThrottleKey = $"{nameof(G9BottomSheetHelper)}.{nameof(HandleBackRequest)}"
            });
    }

    private static async Task HandleBackRequestAsync(CustomizedSfG9BottomSheet sheet, G9BottomSheetBackRequestSource source)
    {
        var behavior = SheetBehaviorStates.GetValue(sheet, static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));

        // toolbar/close button, overlay tap, drag-to-close).

        if (source == G9BottomSheetBackRequestSource.OverlayTap && !behavior.Options.IsCancelable)
        {
            return;
        }

        if (behavior.Options.OnBackRequested is not null)
        {
            var action = await behavior.Options.OnBackRequested(source).ConfigureAwait(true);
            if (action == G9BottomSheetBackAction.Close)
            {
                await CloseSheetAsync(sheet).ConfigureAwait(false);
            }

            return;
        }

        if (behavior.Options.HardwareBackCloses ||
            source is G9BottomSheetBackRequestSource.ToolbarButton or G9BottomSheetBackRequestSource.OverlayTap)
        {
            await CloseSheetAsync(sheet).ConfigureAwait(false);
            return;
        }

    }

    private static CustomizedSfG9BottomSheet? GetPrimarySheet(Grid overlayHost)
    {
        lock (StackLock)
        {
            return PrimarySheets.GetValue(overlayHost, static _ => null);
        }
    }

    private static void SetPrimarySheet(Grid overlayHost, CustomizedSfG9BottomSheet? sheet)
    {
        lock (StackLock)
        {
            PrimarySheets.AddOrUpdate(overlayHost, sheet);
        }
    }

    private static bool IsFullScreenPresentation(G9BottomSheetOptions options)
    {
        return options.SizeMode == G9BottomSheetSizeMode.States &&
               options.States.Count == 1 &&
               options.States[0] == G9BottomSheetState.Large &&
               options.CurrentState == G9BottomSheetState.Large &&
               !options.HasHandle;
    }

    /// <summary>
    ///     True when the LARGEST detent of a multi-detent States sheet is sized from its content
    ///     rather than from a ratio — see <see cref="G9BottomSheetOptions.ExpandedFitsContent" />.
    ///     Requires more than one distinct state; on a single-state sheet there is no "expanded"
    ///     detent distinct from the resting one, so the flag has nothing to size.
    /// </summary>
    private static bool UsesExpandedFitsContent(G9BottomSheetOptions options)
    {
        return options.ExpandedFitsContent &&
               options.SizeMode == G9BottomSheetSizeMode.States &&
               options.States.Distinct().Count() > 1;
    }

    /// <summary>
    ///     True when the sheet's height comes from MEASURING its body — either the whole sheet
    ///     (<see cref="G9BottomSheetSizeMode.FitToContent" />) or just its top detent
    ///     (<see cref="UsesExpandedFitsContent" />). The settle passes, the MeasureInvalidated
    ///     tracker and the measure-target unwrapping are shared by both.
    /// </summary>
    private static bool RequiresContentMeasurement(G9BottomSheetOptions options)
    {
        return options.SizeMode == G9BottomSheetSizeMode.FitToContent || UsesExpandedFitsContent(options);
    }

    private static bool HasFixedAllowedState(G9BottomSheetOptions options)
    {
        return options.SizeMode == G9BottomSheetSizeMode.FitToContent ||
               options.States.Distinct().Count() <= 1;
    }

    private static bool ShouldShowSheetGrabber(G9BottomSheetOptions options)
    {
        return options.HasHandle &&
               options.IsDraggable &&
               !HasFixedAllowedState(options);
    }

    private static bool SupportsStateAwareTopPadding(G9BottomSheetOptions options)
    {
        return options.SizeMode == G9BottomSheetSizeMode.States &&
               options.States.Contains(G9BottomSheetState.Large) &&
               (options.UseTopSafeAreaPadding ||
                options.AdditionalTopSafeAreaPadding > 0 ||
                options.TopSafeAreaPaddingOverride is > 0);
    }

    private static void RegisterTopPaddingTarget(
        CustomizedSfG9BottomSheet sheet,
        VisualElement owner,
        Action<double> applyPadding)
    {
        var behavior = SheetBehaviorStates.GetValue(
            sheet,
            static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));

        behavior.TopPaddingTargets.Add(new SheetTopPaddingTarget(owner, applyPadding));
    }

    private static void ApplyStateAwareTopPadding(CustomizedSfG9BottomSheet sheet, bool animated)
    {
        var behavior = SheetBehaviorStates.GetValue(
            sheet,
            static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));

        if (behavior.TopPaddingTargets.Count == 0)
        {
            return;
        }

        var applyFull = ShouldApplyFullStateTopPadding(sheet, behavior.Options);
        var targetPadding = applyFull
            ? ResolveStateAwareTopPadding(sheet, behavior.Options)
            : 0;


        ApplyTopPaddingTargets(behavior, targetPadding, animated);
    }

    private static void ApplyStateAwareTopPaddingForRatio(
        CustomizedSfG9BottomSheet sheet,
        G9BottomSheetOptions options,
        double heightRatio)
    {
        if (!SupportsStateAwareTopPadding(options))
        {
            return;
        }

        var behavior = SheetBehaviorStates.GetValue(
            sheet,
            static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));

        if (behavior.TopPaddingTargets.Count == 0)
        {
            return;
        }

        var progress = ResolveLargeStateProgress(sheet, options, heightRatio);
        var targetPadding = ResolveStateAwareTopPadding(sheet, options) * progress;
        ApplyTopPaddingTargets(behavior, targetPadding, animated: false);
    }

    private static void ApplyTopPaddingTargets(SheetBehaviorState behavior, double targetPadding, bool animated)
    {
        foreach (var target in behavior.TopPaddingTargets)
        {
            target.Apply(targetPadding, animated);
        }
    }

    private static bool ShouldApplyFullStateTopPadding(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        if (!SupportsStateAwareTopPadding(options))
        {
            return false;
        }

        return sheet.State == SfG9BottomSheetState.FullExpanded ||
               IsFullScreenPresentation(options);
    }

    private static double ResolveStateAwareTopPadding(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        var padding = ResolveFullScreenTopPadding(options);
        if (options.ShowToolbar || !ShouldShowSheetGrabber(options))
        {
            return padding;
        }

        var grabberAreaHeight = sheet.GrabberAreaHeight > 0
            ? sheet.GrabberAreaHeight
            : G9LayoutMetrics.G9BottomSheetDragHandleTouchHeight;

        return Math.Max(0, padding - grabberAreaHeight);
    }

    private static void ApplyFullScreenHeight(
        VisualElement element,
        G9BottomSheetOptions options,
        double? heightOverride = null)
    {
        if (!IsFullScreenPresentation(options))
        {
            return;
        }

        var height = heightOverride ?? ResolveFullScreenHeight();
        if (height <= 0)
        {
            return;
        }

        element.MinimumHeightRequest = height;
        element.HeightRequest = height;
    }

    private static double ResolveFullScreenContentHeight(G9BottomSheetOptions options)
    {
        var height = ResolveFullScreenHeight();
        if (height <= 0 || options.ShowToolbar)
        {
            return height;
        }

        return Math.Max(1, height - ResolveFullScreenTopPadding(options));
    }

    private static double ResolveFullScreenHeight()
    {
        if (G9ModalHostRegistry.TryGetCurrentHost(out var host) && host.Page.Height > 0)
        {
            return host.Page.Height;
        }

        var display = DeviceDisplay.MainDisplayInfo;
        return display.Density > 0 ? display.Height / display.Density : 0;
    }

    private static void ConfigureSheetContent(
        CustomizedSfG9BottomSheet sheet,
        SheetContentRequest contentRequest,
        IG9BottomSheetHandle handle,
        G9BottomSheetOptions options)
    {
        var behaviorState = new SheetBehaviorState(options);
        InitializeFitPlaceholder(sheet, behaviorState, contentRequest, options);
        SheetBehaviorStates.AddOrUpdate(sheet, behaviorState);

        // non-deferred content). A large buildMs here is a tap→open delay the user feels.

        var contentRoot = CreateSheetContentRoot(sheet, contentRequest, handle, options);
        sheet.G9BottomSheetContent = contentRoot;

        SeedExpandedFitsContentDetent(sheet, options);
        ApplyStateAwareTopPadding(sheet, animated: false);
        ApplySheetContentSizing(sheet, contentRoot, options);
        UpdateModalOverlayBackground(sheet, animated: false);
        ScheduleFitToContentRefresh(sheet, contentRoot, options);
        // Late settle re-measures. The MeasureInvalidated tracker can't see a content tree whose
        // growth is absorbed by an inner ScrollView/list (a top-level scroller's own desired size
        // doesn't change when its content grows), so a freshly-built tall body that measures short
        // on the first frame would never re-measure. These two delayed passes catch the settled
        // height. They are instant (no animation) and cheap no-ops for provider/short content.
        ScheduleFitToContentRefresh(sheet, contentRoot, options, delayMs: 160);
        ScheduleFitToContentRefresh(sheet, contentRoot, options, delayMs: 380);
        AttachFitToContentSizeTracking(sheet, contentRoot, options);
        AttachExpandedBodyScrollRewind(sheet, options);
    }

    /// <summary>
    ///     Rewinds a helper-owned body viewport whenever the sheet leaves its top detent.
    /// </summary>
    /// <remarks>
    ///     Dragging the BODY down already implies a scroll offset of zero (the scroller has to be at
    ///     its top edge before the drag reaches the sheet), but the grabber and the sheet chrome are
    ///     outside the scroller and can collapse it from anywhere. Without this, a sheet collapsed
    ///     that way opens its peek onto the middle of the content — with scrolling disabled at that
    ///     detent, and therefore no way back to the top except expanding again.
    /// </remarks>
    private static void AttachExpandedBodyScrollRewind(
        CustomizedSfG9BottomSheet sheet,
        G9BottomSheetOptions options)
    {
        if (!UsesExpandedFitsContent(options))
        {
            return;
        }

        if (!SheetBehaviorStates.TryGetValue(sheet, out var behavior) ||
            behavior?.BodyScrollViewport is not { } viewport)
        {
            return;
        }

        var allowedStates = NormalizeAllowedStates(options.States);
        if (allowedStates.Count == 0)
        {
            return;
        }

        var topState = MapState(allowedStates[^1]);

        sheet.StateChanged += (_, e) =>
        {
            if (e.NewState == topState || viewport.ScrollY <= 0.5)
            {
                return;
            }

            _ = viewport.ScrollToAsync(0, 0, false);
        };
    }

    private static IG9BottomSheetHandle OpenPrimarySheet(Grid overlayHost, PendingPrimarySheetRequest request)
    {
        try
        {
            var sheet = CreateG9BottomSheet(request.Options);
            sheet.FlowDirection = request.FlowDirection;
            var handle = new G9BottomSheetHandleImpl(sheet);
            ConfigureSheetContent(sheet, request.ContentRequest, handle, request.Options);
            sheet.StateChanged += OnPrimarySheetStateChanged;

            AttachModalOverlay(sheet, overlayHost, request.Options);
            overlayHost.Children.Add(sheet);
            SetPrimarySheet(overlayHost, sheet);

            OpenSheet(sheet, request.Options);

            return handle;
        }
        catch
        {
            lock (StackLock)
            {
                var state = GetPrimarySheetState(overlayHost);
                state.IsTransitioning = false;
            }

            throw;
        }
    }

    private static IG9BottomSheetHandle OpenStackedSheet(
        ModalHost host,
        SheetContentRequest contentRequest,
        G9BottomSheetOptions options)
    {
        // Captured BEFORE the push so it is the sheet the user is currently looking at (the
        // stacked top, or the primary) — the one that recedes behind the new child.
        var parentSheet = GetTopSheet(host);

        var sheet = CreateG9BottomSheet(options);
        sheet.FlowDirection = host.Page.FlowDirection;
        var handle = new G9BottomSheetHandleImpl(sheet);
        ConfigureSheetContent(sheet, contentRequest, handle, options);

        int stackDepth;
        lock (StackLock)
        {
            var stack = StackedSheets.GetValue(host.OverlayHost, static _ => new Stack<CustomizedSfG9BottomSheet>());
            stack.Push(sheet);
            stackDepth = stack.Count;
        }

        sheet.StateChanged += OnStackedSheetStateChanged;
        AttachModalOverlay(sheet, host.OverlayHost, options);
        host.OverlayHost.Children.Add(sheet);

        // Parent recede runs in PARALLEL with the child's open animation (no sequencing) — the
        // parent sinks + fades while the child rises.
        ApplyStackParentRecede(parentSheet, sheet, options);
        OpenSheet(sheet, options);

        return handle;
    }

    /// <summary>
    ///     Sinks + fades the <paramref name="parent" /> sheet behind a newly-stacked
    ///     <paramref name="child" /> (see <see cref="G9BottomSheetOptions.RecedeParentOnStack" />).
    ///     The parent stays alive with its state and rendered tree intact;
    ///     <see cref="RestoreRecededParent" /> slides it back when the child closes.
    /// </summary>
    private static void ApplyStackParentRecede(
        CustomizedSfG9BottomSheet? parent,
        CustomizedSfG9BottomSheet child,
        G9BottomSheetOptions childOptions)
    {
        if (parent is null || !childOptions.RecedeParentOnStack || !IsSheetAlive(parent))
        {
            return;
        }

        var parentBehavior = SheetBehaviorStates.GetValue(
            parent,
            static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));

        // A FULL-SCREEN parent is the backdrop of its whole flow (e.g. the sampling task screen):
        // hiding it would expose the page underneath around a smaller child — much worse than the
        // child simply sitting on top of it. Recede is for PARTIAL-height parents, where a smaller
        // child overlapping a taller visible parent is the bad view.
        if (IsFullScreenPresentation(parentBehavior.Options))
        {
            return;
        }

        StackedRecededParents.AddOrUpdate(child, parent);

        // Sink distance: a fraction of the parent's VISIBLE height (its fit height when known,
        // otherwise the host height for partial state sheets), so short sheets nudge and tall
        // sheets sink visibly — both read as "moving to the back".
        var visibleHeight = parentBehavior.LastFitContentHeight > 0
            ? parentBehavior.LastFitContentHeight
            : parent.Height;
        if (parent.Height > 0)
        {
            visibleHeight = Math.Min(visibleHeight, parent.Height);
        }

        var offset = Math.Max(24, visibleHeight * StackParentRecedeHeightFraction);


        G9SafeCommand.RunSafe(
            () => Task.WhenAll(
                parent.TranslateToAsync(0, offset, StackParentRecedeDurationMs, Easing.CubicOut),
                parent.FadeToAsync(0, StackParentRecedeDurationMs, Easing.CubicOut)),
            new G9SafeCommandOptions
            {
                Source = nameof(G9BottomSheetHelper),
                ShowErrorG9Popup = false,
                EnableThrottle = false,
                ThrottleKey = $"{nameof(G9BottomSheetHelper)}.{nameof(ApplyStackParentRecede)}"
            });
    }

    /// <summary>
    ///     Slides a receded parent back in as its stacked <paramref name="child" /> closes.
    ///     Idempotent (the child→parent link is consumed on first call); pass
    ///     <paramref name="instant" /> from cleanup fallbacks where an animation would land after
    ///     the visuals are already gone.
    /// </summary>
    private static void RestoreRecededParent(CustomizedSfG9BottomSheet child, bool instant = false)
    {
        if (!StackedRecededParents.TryGetValue(child, out var parent) || parent is null)
        {
            return;
        }

        StackedRecededParents.Remove(child);

        if (!IsSheetAlive(parent) || IsSheetClosing(parent) || !parent.IsOpen)
        {
            // Parent is going away too (whole-flow close) — silently reset so no transform leaks
            // if the instance is ever reused before cleanup removes it.
            parent.TranslationY = 0;
            parent.Opacity = 1;
            return;
        }


        if (instant)
        {
            parent.TranslationY = 0;
            parent.Opacity = 1;
            return;
        }

        G9SafeCommand.RunSafe(
            () => Task.WhenAll(
                parent.TranslateToAsync(0, 0, StackParentRecedeDurationMs, Easing.CubicOut),
                parent.FadeToAsync(1, StackParentRecedeDurationMs, Easing.CubicOut)),
            new G9SafeCommandOptions
            {
                Source = nameof(G9BottomSheetHelper),
                ShowErrorG9Popup = false,
                EnableThrottle = false,
                ThrottleKey = $"{nameof(G9BottomSheetHelper)}.{nameof(RestoreRecededParent)}"
            });
    }

    private static void LogSheetCreated(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options, string role)
    {
    }

    private static void OpenSheet(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        RunCommand(options.OpeningCommand, options.OpeningCommandParameter);

        // t here is the true "tap → sheet starts opening" latency.

        try
        {
            sheet.Show();
        }
        catch (InvalidOperationException)
        {
            sheet.IsOpen = true;
        }


        AttachPositionTracking(sheet, options);
        RunOpenedCommandLater(sheet);
    }

    private static View CreateSheetContentRoot(
        CustomizedSfG9BottomSheet sheet,
        SheetContentRequest contentRequest,
        IG9BottomSheetHandle handle,
        G9BottomSheetOptions options)
    {
        HorizontalStackLayout? toolbarActionsHost = null;
        IReadOnlyList<ToolbarItem>? toolbarItems = ResolveInitialToolbarItems(contentRequest, options);

        var shouldSizeContentAsFullScreen = false;
        View contentHost = contentRequest.ContentFactory is not null
            ? CreateFactoryHost(
                sheet,
                contentRequest,
                handle,
                options,
                shouldSizeContentAsFullScreen,
                toolbarActionsHostAccessor: () => toolbarActionsHost,
                immediateToolbarItemsSetter: items => toolbarItems ??= items)
            : CreateContentHost(sheet, contentRequest, handle, options, shouldSizeContentAsFullScreen);

        var renderFooter = ShouldRenderFooter(options);

        if (!options.ShowToolbar)
        {
            var bodyOnlyRoot = CreateFullScreenSizingHost(sheet, contentHost, options);
            AttachSheetOverlayHost(sheet, bodyOnlyRoot, options);

            if (!renderFooter)
            {
                return WrapBodyOnlyRootForDrag(sheet, bodyOnlyRoot, options);
            }

            return WrapBodyOnlyRootWithFooter(sheet, bodyOnlyRoot, options);
        }

        var sheetOverlayHost = CreateSheetOverlayHost();
        var showDragHandle = ShouldRenderCustomDragHandle(options);
        var root = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            FlowDirection = sheet.FlowDirection,
            BackgroundColor = options.BackgroundColor ?? G9Palette.Current.Background
        };

        var showStateAwareTopPadding = SupportsStateAwareTopPadding(options);
        if (showStateAwareTopPadding)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        if (showDragHandle)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Star });

        if (renderFooter)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        ApplyFullScreenHeight(root, options);

        var contentRow = 0;
        if (showStateAwareTopPadding)
        {
            var topPaddingHost = new Grid
            {
                HeightRequest = 0,
                MinimumHeightRequest = 0,
                InputTransparent = true,
                BackgroundColor = options.BackgroundColor ?? G9Palette.Current.Background
            };

            RegisterTopPaddingTarget(sheet, topPaddingHost, value =>
            {
                topPaddingHost.HeightRequest = value;
                topPaddingHost.MinimumHeightRequest = value;
            });

            root.Children.Add(topPaddingHost);
            contentRow++;
        }

        if (showDragHandle)
        {
            var dragHandle = CreateDragHandleSurface(sheet, options);
            dragHandle.ZIndex = 11;
            root.Children.Add(dragHandle);
            contentRow++;
        }

        var header = BuildSheetHeader(sheet, options, toolbarItems, out toolbarActionsHost);
        var toolbar = WrapHeaderWithBottomDivider(header, options);
        AttachDragToCloseGesture(sheet, toolbar, options);
        toolbar.ZIndex = 10;
        root.Children.Add(toolbar);
        Grid.SetRow(toolbar, contentRow);

        // STANDARD body-top gap, owned by the HELPER for every toolbar sheet: the header band's
        // own bottom padding ends AT the hairline, so without this the first body element sits
        // flush against the divider. One place, every sheet — bodies keep adding no top gap of
        // their own (design guide §3b), and ResolveHelperChromeHeight accounts for it.
        // Opt-out: G9BottomSheetOptions.UseStandardBodyTopGap = false, for a body that OPENS with its
        // own chrome band (tab strip / segmented control) which must read as attached to the header.
        if (options.UseStandardBodyTopGap)
        {
            contentHost.Margin = new Thickness(
                contentHost.Margin.Left,
                contentHost.Margin.Top + G9LayoutMetrics.SheetHeaderVerticalGap,
                contentHost.Margin.Right,
                contentHost.Margin.Bottom);
        }

        root.Children.Add(contentHost);
        AttachSheetContentDragGesture(sheet, contentHost, options);

        Grid.SetRow(contentHost, contentRow + 1);

        if (renderFooter)
        {
            var footer = BuildSheetFooter(sheet, options);
            if (footer is not null)
            {
                root.Children.Add(footer);
                Grid.SetRow(footer, contentRow + 2);
            }
        }

        root.Children.Add(sheetOverlayHost);
        Grid.SetRow(sheetOverlayHost, 0);
        Grid.SetRowSpan(sheetOverlayHost, root.RowDefinitions.Count);
        sheetOverlayHost.ZIndex = int.MaxValue - 1;
        SheetOverlayHosts.AddOrUpdate(sheet, sheetOverlayHost);

        return root;
    }

    private static bool ShouldRenderFooter(G9BottomSheetOptions options)
    {
        return options.FooterView is not null ||
               options.FooterButtons is { Count: > 0 };
    }

    // Body-only (no helper toolbar) variant that still hosts the shared footer below the content.
    // Used by sheets that own their own header (e.g. G9BottomSheetListPickerModal) but want the
    // shared equal-width footer-button layout under the body.
    private static View WrapBodyOnlyRootWithFooter(
        CustomizedSfG9BottomSheet sheet,
        View bodyOnlyRoot,
        G9BottomSheetOptions options)
    {
        var root = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            FlowDirection = sheet.FlowDirection,
            BackgroundColor = options.BackgroundColor ?? G9Palette.Current.Background,
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        ApplyFullScreenHeight(root, options);

        root.Children.Add(bodyOnlyRoot);
        Grid.SetRow(bodyOnlyRoot, 0);
        AttachSheetContentDragGesture(sheet, bodyOnlyRoot, options);

        var footer = BuildSheetFooter(sheet, options);
        if (footer is not null)
        {
            root.Children.Add(footer);
            Grid.SetRow(footer, 1);
        }

        return root;
    }

    private static View CreateContentHost(
        CustomizedSfG9BottomSheet sheet,
        SheetContentRequest contentRequest,
        IG9BottomSheetHandle handle,
        G9BottomSheetOptions options,
        bool useFullScreenSizing)
    {
        var content = contentRequest.Content ?? throw new InvalidOperationException("Bottom sheet content is missing.");
        PrepareSheetContent(content, handle, options);

        // "Open then fill" content paints its own loading/preview state and loads after the sheet
        // is visible (see IDeferredSheetLoad). It must NOT be wrapped in a DeferredContentView —
        // that would stack a second (timer-based) spinner in front of the view's own one and delay
        // the very paint we want immediate. Register it so RunOpenedCommandLater drives the load.
        if (content is IDeferredSheetLoad)
        {
            RegisterDeferredLoad(sheet, content);
            return useFullScreenSizing
                ? CreateFullScreenSizingHost(sheet, content, options)
                : CreateFillHost(sheet, content, options);
        }

        if (!options.DeferContent)
        {
            return useFullScreenSizing
                ? CreateFullScreenSizingHost(sheet, content, options)
                : CreateFillHost(sheet, content, options);
        }

        var deferred = new DeferredContentView
        {
            DeferredContent = content,
            AutoLoad = true,
            LoadDelayMs = ResolveDeferredContentLoadDelayMs(options),
            FadeContentIn = options.FadeDeferredContentIn,
            LoadingView = CreateLoadingSkeletonView(options),
            OnContentCreated = loadedContent =>
            {
                contentRequest.OnContentCreated?.Invoke(loadedContent);
            }
        };
        AttachDeferredContentLoadedRefresh(sheet, deferred, options);
        ApplyDeferredLoadingMetrics(sheet, deferred, options);

        return useFullScreenSizing
            ? CreateFullScreenSizingHost(sheet, deferred, options)
            : CreateFillHost(sheet, deferred, options, bodyProbe: content);
    }

    private static View CreateFactoryHost(
        CustomizedSfG9BottomSheet sheet,
        SheetContentRequest contentRequest,
        IG9BottomSheetHandle handle,
        G9BottomSheetOptions options,
        bool useFullScreenSizing,
        Func<HorizontalStackLayout?> toolbarActionsHostAccessor,
        Action<IReadOnlyList<ToolbarItem>> immediateToolbarItemsSetter)
    {
        if (!options.DeferContent)
        {
            var content = contentRequest.ContentFactory?.Invoke()
                          ?? throw new InvalidOperationException("Bottom sheet content factory returned null.");
            PrepareSheetContent(content, handle, options);
            contentRequest.OnContentCreated?.Invoke(content);

            RegisterDeferredLoad(sheet, content);

            if (options.ToolbarItemsFactory?.Invoke(content) is { Count: > 0 } items)
            {
                if (toolbarActionsHostAccessor() is { } toolbarActionsHost)
                {
                    AddToolbarItems(toolbarActionsHost, items);
                }
                else
                {
                    immediateToolbarItemsSetter(items);
                }
            }

            return useFullScreenSizing
                ? CreateFullScreenSizingHost(sheet, content, options)
                : CreateFillHost(sheet, content, options);
        }

        var deferred = new DeferredContentView
        {
            ContentFactory = () =>
            {
                var content = contentRequest.ContentFactory?.Invoke()
                              ?? throw new InvalidOperationException("Bottom sheet content factory returned null.");
                PrepareSheetContent(content, handle, options);
                return content;
            },
            AutoLoad = true,
            LoadDelayMs = ResolveDeferredContentLoadDelayMs(options),
            FadeContentIn = options.FadeDeferredContentIn,
            LoadingView = CreateLoadingSkeletonView(options),
            OnContentCreated = content =>
            {
                contentRequest.OnContentCreated?.Invoke(content);

                if (options.ToolbarItemsFactory?.Invoke(content) is { Count: > 0 } items &&
                    toolbarActionsHostAccessor() is { } toolbarActionsHost)
                {
                    AddToolbarItems(toolbarActionsHost, items);
                }
            }
        };
        AttachDeferredContentLoadedRefresh(sheet, deferred, options);
        ApplyDeferredLoadingMetrics(sheet, deferred, options);

        return useFullScreenSizing
            ? CreateFullScreenSizingHost(sheet, deferred, options)
            : CreateFillHost(sheet, deferred, options);
    }

    // Loading placeholder for the deferred loading window (G9BottomSheetOptions.LoadingSkeleton).
    // Skeleton shapes come from G9Shimmer; the None case still builds a spinner view when the
    // sheet has a custom BackgroundColor, so the placeholder's OPAQUE cover (which also hides the
    // content during the crossfade reveal) always matches the sheet body instead of the theme
    // default. Null keeps DeferredContentView's stock spinner.
    private static View? CreateLoadingSkeletonView(G9BottomSheetOptions options)
    {
        if (options.LoadingSkeleton != G9BottomSheetLoadingSkeleton.None)
        {
            return G9Shimmer.CreateSheetSkeleton(
                options.LoadingSkeleton,
                options.LoadingSkeletonRowCount,
                options.BackgroundColor);
        }

        if (options.BackgroundColor is null)
        {
            return null;
        }

        return new ContentView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            SafeAreaEdges = SafeAreaEdges.None,
            BackgroundColor = options.BackgroundColor,
            Content = new G9ActivityIndicator
            {
                IsRunning = true,
                Color = G9Palette.Current.Primary,
                HeightRequest = 42,
                WidthRequest = 42,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
    }

    private static void ApplyDeferredLoadingMetrics(
        CustomizedSfG9BottomSheet sheet,
        DeferredContentView deferred,
        G9BottomSheetOptions options)
    {
        if (IsFullScreenPresentation(options))
        {
            if (!options.ShowToolbar)
            {
                ApplyFullScreenHeight(deferred, options, ResolveFullScreenContentHeight(options));
            }

            return;
        }

        if (options.SizeMode != G9BottomSheetSizeMode.FitToContent)
        {
            return;
        }

        if (!options.UseFullScreenLoadingPlaceholder)
        {
            // When the final BODY height is known up-front (caller placeholder or height memo),
            // open the spinner at that height so the swap-in doesn't resize the sheet (no jump).
            // The engine still clamps the applied height to the cap, so an over-estimate opens
            // at the cap and scrolls.
            var placeholderBodyHeight = SheetBehaviorStates.TryGetValue(sheet, out var behavior) && behavior is not null
                ? behavior.PlaceholderBodyHeight
                : 0;
            deferred.MinimumHeightRequest = placeholderBodyHeight > 0
                ? placeholderBodyHeight
                : G9LayoutMetrics.FitContentLoadingMinHeight;
            return;
        }

        var height = ResolveFullScreenHeight();
        if (height <= 0)
        {
            deferred.MinimumHeightRequest = G9LayoutMetrics.FitContentLoadingMinHeight;
            return;
        }

        deferred.MinimumHeightRequest = height;
        deferred.HeightRequest = height;
    }

    /// <summary>
    ///     One entry point for both content-sizing modes. Each half no-ops for the mode it does not
    ///     own, so every settle pass, tracker callback and provider event can call this rather than
    ///     each learning which engine applies.
    /// </summary>
    private static void ApplySheetContentSizing(
        CustomizedSfG9BottomSheet sheet,
        View content,
        G9BottomSheetOptions options,
        bool animate = false)
    {
        ApplyFitToContentHeight(sheet, content, options, animate);
        ApplyExpandedFitsContentHeight(sheet, content, options);
    }

    /// <summary>
    ///     Sizes the LARGEST detent of a multi-detent States sheet to its content, capped at
    ///     <see cref="G9BottomSheetOptions.MaxFitToContentHeightRatio" /> — Material's
    ///     <c>fitToContents</c> applied to the top detent. See
    ///     <see cref="G9BottomSheetOptions.ExpandedFitsContent" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         It writes a RATIO, not a height, because that is the only vocabulary the control has
    ///         for its two large detents — whichever of <c>HalfExpandedRatio</c> /
    ///         <c>FullExpandedRatio</c> corresponds to this sheet's top detent. The control clamps
    ///         the half ratio to 0.9, so a sheet whose top detent must reach further has to declare
    ///         <c>Large</c> among its states.
    ///     </para>
    ///     <para>
    ///         A cold measure (Android measures a not-yet-laid-out tree as 0 — see the sizing-engine
    ///         section of the guide) is DISCARDED rather than applied: the peek detent is a fixed
    ///         height and is already correct, so there is nothing to hold and no reason to write a
    ///         wrong top detent. The scheduled settle passes and the MeasureInvalidated tracker
    ///         re-run this as soon as the platform can measure.
    ///     </para>
    /// </remarks>
    private static void ApplyExpandedFitsContentHeight(
        CustomizedSfG9BottomSheet sheet,
        View content,
        G9BottomSheetOptions options)
    {
        if (!UsesExpandedFitsContent(options))
        {
            return;
        }

        void Apply()
        {
            var behavior = SheetBehaviorStates.GetValue(
                sheet,
                static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));
            if (behavior.IsClosing || IsSheetClosing(sheet))
            {
                return;
            }

            var fullHeight = ResolveFullScreenHeight();
            if (fullHeight <= 0)
            {
                return;
            }

            var cap = fullHeight * Math.Clamp(options.MaxFitToContentHeightRatio, 0.2, 1.0);
            EnsureHeightProviderSubscribed(sheet, behavior, content, options);

            // A loading visual measures nothing useful (see the fit engine's loading-window rule).
            // The peek detent is unaffected, so simply wait for the settle pass that follows the
            // reveal rather than writing a top detent sized to a spinner.
            if (ContainsLoadingDeferredContent(content))
            {
                return;
            }

            double naturalHeight;
            if (behavior.HeightProvider is { } provider)
            {
                naturalHeight = provider.GetDesiredG9BottomSheetContentHeight(ResolveMeasureWidth(sheet, options), cap)
                                + ResolveHelperChromeHeight(sheet, options);
            }
            else if (IsRootGreedyScroller(ResolveExpandedMeasureRoot(behavior, content)))
            {
                // The CALLER's body is itself a scroller, which reports its viewport rather than its
                // content — unmeasurable, so the honest top detent is the cap. (The helper's OWN
                // viewport is unwrapped by ResolveExpandedMeasureRoot and does not land here.)
                naturalHeight = cap;
            }
            else
            {
                naturalHeight = MeasureContentHeight(content, sheet, options, out var usedMeasureFallback);
                if (usedMeasureFallback)
                {
                    return;
                }
            }

            var peekHeight = ResolveExpandedFitsPeekHeight(sheet, options);
            var targetHeight = Math.Clamp(naturalHeight, Math.Max(FitContentAbsoluteMinHeight, peekHeight), cap);

            // The peek follows the content DOWN but never up: a site whose groups are filtered away
            // must not open with a band of dead space, and a caller asking to "open short" must not
            // be talked out of it by tall content.
            if (naturalHeight > FitContentAbsoluteMinHeight && naturalHeight < peekHeight - 0.5)
            {
                sheet.CollapsedHeight = naturalHeight;
                targetHeight = naturalHeight;
            }

            ApplyExpandedDetentRatio(sheet, options, targetHeight / fullHeight);
        }

        if (MainThread.IsMainThread)
        {
            Apply();
            return;
        }

        MainThread.BeginInvokeOnMainThread(Apply);
    }

    /// <summary>
    ///     Opens an <see cref="G9BottomSheetOptions.ExpandedFitsContent" /> sheet's top detent at the
    ///     CAP until the content has been measured.
    /// </summary>
    /// <remarks>
    ///     Without this the top detent starts at whatever ratio the caller's preset carried — 0.5 for
    ///     <c>DefaultOptions()</c> — and on a tall screen that is BELOW the peek height, so
    ///     <c>ResolveMaximumDetentHeight</c> resolves the peek as the maximum and the sheet cannot be
    ///     dragged open at all. In practice the settle passes land long before a finger does, so this
    ///     is invisible; it exists so that a body which never measures (the cold-measure case the fit
    ///     engine is built around) degrades to "drag to the cap and scroll" rather than to a sheet
    ///     stuck at its peek. The seed is never visible on open — the sheet opens at its peek.
    /// </remarks>
    private static void SeedExpandedFitsContentDetent(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        if (!UsesExpandedFitsContent(options))
        {
            return;
        }

        ApplyExpandedDetentRatio(sheet, options, Math.Clamp(options.MaxFitToContentHeightRatio, 0.2, 1.0));
    }

    /// <summary>
    ///     The body as the sizing tiers should see it: the helper's own scroll viewport is
    ///     transparent for measurement (it exists so a CAPPED top detent can scroll), a caller's
    ///     scroller is not.
    /// </summary>
    private static View ResolveExpandedMeasureRoot(SheetBehaviorState behavior, View content)
    {
        return behavior.BodyScrollViewport is { } viewport &&
               ReferenceEquals(viewport, content) &&
               viewport.Content is { } inner
            ? inner
            : content;
    }

    /// <summary>
    ///     Writes the resolved content ratio onto whichever detent is this sheet's largest.
    /// </summary>
    private static void ApplyExpandedDetentRatio(
        CustomizedSfG9BottomSheet sheet,
        G9BottomSheetOptions options,
        double ratio)
    {
        if (options.States.Contains(G9BottomSheetState.Large))
        {
            var clamped = Math.Clamp(ratio, 0.1, 1);
            if (Math.Abs(sheet.FullExpandedRatio - clamped) > 0.001)
            {
                sheet.FullExpandedRatio = clamped;
            }

            return;
        }

        var half = Math.Clamp(ratio, 0.1, 0.9);
        if (Math.Abs(sheet.HalfExpandedRatio - half) > 0.001)
        {
            sheet.HalfExpandedRatio = half;
        }
    }

    private static double ResolveExpandedFitsPeekHeight(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        var peek = options.PeekHeight ?? options.CollapsedHeight ?? sheet.CollapsedHeight;
        return peek > 0 ? peek : 0;
    }

    private static void ApplyFitToContentHeight(
        CustomizedSfG9BottomSheet sheet,
        View content,
        G9BottomSheetOptions options,
        bool animate = false)
    {
        if (options.SizeMode != G9BottomSheetSizeMode.FitToContent)
        {
            return;
        }

        void Apply()
        {
            var behavior = SheetBehaviorStates.GetValue(
                sheet,
                static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));
            if (behavior.IsClosing)
            {
                return;
            }

            // Suppress the MeasureInvalidated tracker for the duration of this apply: the
            // InvalidateMeasure calls below are our own and must not re-trigger a remeasure
            // (that would loop). Genuine content growth raises MeasureInvalidated after this
            // returns, when the flag is back to false. See AttachFitToContentSizeTracking.
            behavior.IsApplyingFitContent = true;
            try
            {
                var fullHeight = ResolveFullScreenHeight();
                if (fullHeight <= 0)
                {
                    fullHeight = G9LayoutMetrics.FitContentLoadingMinHeight;
                }

                // Cap: fit-to-content grows only up to MaxFitToContentHeightRatio of the screen
                // (default 75%). Beyond that the body stays at the cap and the content scrolls
                // inside its (now bounded) viewport. See IG9BottomSheetContentHeightProvider.
                var ratioCap = Math.Clamp(options.MaxFitToContentHeightRatio, 0.2, 1.0);
                var cap = fullHeight * ratioCap;

                // Lazily resolve + subscribe the content's own height provider (count/tab aware).
                // Prebuilt deferred bodies are resolvable BEFORE the spinner swap (see
                // TryResolveHeightProvider), so provider sheets open at their final height.
                EnsureHeightProviderSubscribed(sheet, behavior, content, options);

                var previousHeight = behavior.LastFitContentHeight;

                double naturalHeight;
                // The floor differs per tier: authoritative heights (placeholder/memo, a real
                // measure of loaded content, a hold of the previous height) may legitimately sit
                // below the 180dp loading floor and are only clamped to the absolute minimum;
                // the loading floor applies just to the unknown-height loading window.
                var minHeight = G9LayoutMetrics.FitContentLoadingMinHeight;
                string tier;
                if (behavior.UseDeferredPlaceholderHeight && behavior.PlaceholderBodyHeight > 0)
                {
                    // The caller (or the session height memo) knows the final BODY height — hold
                    // it stably through the whole loading window (no measuring the moving
                    // spinner/DeferredContentView). The helper adds its own chrome (header band,
                    // grabber, padding) because callers cannot know it. Cleared on ContentLoaded,
                    // after which we measure the real content once.
                    naturalHeight = behavior.PlaceholderBodyHeight + ResolveHelperChromeHeight(sheet, options);
                    minHeight = FitContentAbsoluteMinHeight;
                    tier = "placeholder";
                }
                else if (options.UseFullScreenLoadingPlaceholder && ContainsLoadingDeferredContent(content))
                {
                    naturalHeight = fullHeight;
                    tier = "fullScreenPlaceholder";
                }
                else if (behavior.HeightProvider is { } provider)
                {
                    // Providers return the BODY height (everything below the helper chrome); the
                    // helper adds the chrome it renders itself. For today's provider consumers
                    // (toolbar-less sheets that draw their own headers) the chrome is zero, so
                    // this is behavior-neutral for them.
                    naturalHeight = provider.GetDesiredG9BottomSheetContentHeight(ResolveMeasureWidth(sheet, options), cap)
                                    + ResolveHelperChromeHeight(sheet, options);
                    minHeight = FitContentAbsoluteMinHeight;
                }
                else if (IsRootGreedyScroller(content))
                {
                    tier = "rootScrollerCap";
                    // The body IS a scroller (ScrollView / CollectionView / VirtualScrollView).
                    // Measuring it is unreliable — a scroller reports its viewport, not its
                    // content, and on a cold first open Android hasn't measured the inner
                    // children's text yet (label height = 0 until rendered), so the natural
                    // height comes back far too small and never settles within the open. A
                    // scroller as the whole sheet body inherently means "this content scrolls",
                    // so we cap to the max ratio and let the inner scroller handle overflow.
                    // For exact-fit use a height provider or non-scrolling content instead.
                    naturalHeight = cap;
                }
                else
                {
                    naturalHeight = MeasureContentHeight(content, sheet, options, out var usedMeasureFallback);
                    if (usedMeasureFallback && previousHeight > 0)
                    {
                        // Cold measure (raw 0/NaN — the platform hasn't laid the tree out yet)
                        // while the sheet already has a height: HOLD the current height instead
                        // of collapsing to the loading floor. The settle passes / tracker apply
                        // the real height (animated) once the platform can measure. This is what
                        // keeps a replace-in-place body swap from dipping to 180dp.
                        naturalHeight = previousHeight;
                        minHeight = FitContentAbsoluteMinHeight;
                        tier = "measureHold";
                    }
                    else if (!usedMeasureFallback && !ContainsLoadingDeferredContent(content))
                    {
                        // A real measure of the LOADED content is authoritative: a genuinely short
                        // body (e.g. a 125dp two-row menu) must not be inflated to the loading
                        // floor. Record it in the persisted height memo so the next open of this
                        // body starts at the right height.
                        minHeight = FitContentAbsoluteMinHeight;
                        tier = "measure";
                        RecordFitHeightMemo(sheet, behavior, options, naturalHeight);
                    }
                    else
                    {
                        tier = usedMeasureFallback ? "measureFallback" : "measureLoading";

                        // A loading visual is on screen (spinner / skeleton / crossfade window).
                        // NEVER track its measured size — a 3-row skeleton measuring taller (or
                        // shorter) than the real content would drag the sheet around during the
                        // load. Hold the current height; the settle passes apply the real height
                        // (animated) once the loading window ends.
                        if (previousHeight > 0)
                        {
                            naturalHeight = previousHeight;
                            minHeight = FitContentAbsoluteMinHeight;
                        }
                        else
                        {
                            naturalHeight = G9LayoutMetrics.FitContentLoadingMinHeight;
                        }
                    }
                }

                var contentHeight = Math.Clamp(naturalHeight, minHeight, cap);

                // No-op early-out: once settled, an identical re-resolve (typical for most of the
                // scheduled settle passes) skips the metric re-apply and the triple
                // InvalidateMeasure — and, critically, does NOT abort a resize animation that is
                // currently tweening toward this same height.
                if (behavior.IsFitContentSettled && Math.Abs(contentHeight - previousHeight) < 0.5)
                {
                    return;
                }

                behavior.LastFitContentHeight = contentHeight;

                // Animate every post-open change — provider-driven data/tab changes (animate=true)
                // AND layout-settling growth after the sheet is open (tracker / settle passes /
                // deferred-content load). Only the opening passes snap instantly, so the sheet
                // never pops once it is on screen. ApplyFitToContentMetrics additionally requires
                // sheet.IsOpen and a real height delta before animating.
                var shouldAnimate = (animate || sheet.IsOpen) && behavior.IsFitContentSettled;

                ApplyFitToContentMetrics(
                    sheet,
                    previousHeight,
                    contentHeight,
                    fullHeight,
                    behavior.Options,
                    shouldAnimate);
                behavior.IsFitContentSettled = true;
                UpdateModalOverlayBackground(sheet, behavior.Options, ratioOverride: contentHeight / fullHeight, animated: false);

                content.InvalidateMeasure();
                sheet.G9BottomSheetContent?.InvalidateMeasure();
                sheet.InvalidateMeasure();
            }
            finally
            {
                behavior.IsApplyingFitContent = false;
            }
        }

        if (MainThread.IsMainThread)
        {
            Apply();
            return;
        }

        MainThread.BeginInvokeOnMainThread(Apply);
    }

    // Walks the (possibly deferred) content tree for the first IG9BottomSheetContentHeightProvider
    // and subscribes once. Provider-driven height changes (tab switch / async data) trigger a
    // single debounced, ANIMATED resize — distinct from the instant layout-settling remeasures.
    private static void EnsureHeightProviderSubscribed(
        CustomizedSfG9BottomSheet sheet,
        SheetBehaviorState behavior,
        View content,
        G9BottomSheetOptions options)
    {
        if (behavior.HeightProvider is not null)
        {
            return;
        }

        var provider = TryResolveHeightProvider(content)
                       ?? TryResolveHeightProvider(sheet.G9BottomSheetContent);
        if (provider is null)
        {
            return;
        }


        EventHandler handler = (_, _) =>
        {
            if (behavior.IsClosing || behavior.IsProviderRefreshScheduled)
            {
                return;
            }

            behavior.IsProviderRefreshScheduled = true;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(FitContentRemeasureDebounceMs).ConfigureAwait(true);
                behavior.IsProviderRefreshScheduled = false;
                if (IsSheetAlive(sheet) && !IsSheetClosing(sheet))
                {
                    ApplySheetContentSizing(sheet, sheet.G9BottomSheetContent ?? content, options, animate: true);
                }
            });
        };

        provider.G9BottomSheetContentHeightChanged += handler;
        behavior.HeightProvider = provider;
        behavior.HeightProviderHandler = handler;
    }

    private static IG9BottomSheetContentHeightProvider? TryResolveHeightProvider(View? view, int depth = 0)
    {
        if (view is null || depth > 8)
        {
            return null;
        }

        if (view is IG9BottomSheetContentHeightProvider provider)
        {
            return provider;
        }

        switch (view)
        {
            // A prebuilt deferred body isn't parented until the spinner swap, but its provider is
            // pure math (count × rowHeight) — resolving it through DeferredContent up-front lets
            // tier-1 sizing run from the very first pass, so the sheet opens at its final height
            // instead of the loading floor. Falls through to the live Content (spinner → loaded
            // view) when no prebuilt body is pending.
            case DeferredContentView deferredView:
                return TryResolveHeightProvider(deferredView.DeferredContent, depth + 1)
                       ?? TryResolveHeightProvider(deferredView.Content, depth + 1);
            case ContentView { Content: { } cv }:
                return TryResolveHeightProvider(cv, depth + 1);
            case Border { Content: { } bc }:
                return TryResolveHeightProvider(bc, depth + 1);
            case ScrollView { Content: { } sc }:
                return TryResolveHeightProvider(sc, depth + 1);
            case Layout layout:
                foreach (var child in layout.Children.OfType<View>())
                {
                    var found = TryResolveHeightProvider(child, depth + 1);
                    if (found is not null)
                    {
                        return found;
                    }
                }
                return null;
            default:
                return null;
        }
    }

    private static void ApplyFitToContentMetrics(
        CustomizedSfG9BottomSheet sheet,
        double previousHeight,
        double targetHeight,
        double fullHeight,
        G9BottomSheetOptions options,
        bool animate)
    {
        if (IsSheetClosing(sheet))
        {
            sheet.AbortAnimation(FitContentResizeAnimationName);
            return;
        }

        // Animate ONLY genuine post-open changes (caller passes animate=true for provider-driven
        // tab-switch / data-load resizes). Opening + layout-settling remeasures snap instantly so
        // the sheet never shows a small→large blink.
        if (animate &&
            previousHeight > 0 &&
            sheet.IsOpen &&
            Math.Abs(previousHeight - targetHeight) > 1)
        {
            AnimateFitToContentHeight(sheet, previousHeight, targetHeight, fullHeight, options);
            return;
        }


        sheet.AbortAnimation(FitContentResizeAnimationName);
        ApplyFitToContentMetricsNow(sheet, targetHeight, fullHeight);
    }

    private static void AnimateFitToContentHeight(
        CustomizedSfG9BottomSheet sheet,
        double previousHeight,
        double targetHeight,
        double fullHeight,
        G9BottomSheetOptions options)
    {
        sheet.AbortAnimation(FitContentResizeAnimationName);

        // Fit-to-content can grow (content got bigger) or shrink (content got smaller). Match
        // the user-facing direction so a content shrink reads as a "close-like" motion and a
        // grow reads as "open-like". Not size-scaled here — the resize span is content height,
        // not screen height, so the configured duration represents the resize itself.
        var baseDurationMs = targetHeight >= previousHeight
            ? ResolveOpenAnimationDurationMs(options)
            : ResolveCloseAnimationDurationMs(options);
        var duration = (uint)Math.Clamp(
            (int)Math.Ceiling(baseDurationMs),
            1,
            CloseAnimationTimeoutMs);


        var animation = new Animation(
            value => ApplyFitToContentMetricsNow(sheet, value, fullHeight),
            previousHeight,
            targetHeight,
            Easing.CubicOut);

        sheet.Animate(
            FitContentResizeAnimationName,
            animation,
            FitContentResizeAnimationRate,
            duration);
    }

    private static void ApplyFitToContentMetricsNow(CustomizedSfG9BottomSheet sheet, double contentHeight, double fullHeight)
    {
        if (IsSheetClosing(sheet))
        {
            return;
        }

        var ratio = Math.Clamp(contentHeight / fullHeight, 0.1, 1);
        var halfExpandedRatio = Math.Clamp(ratio, 0.1, 0.9);
        sheet.FullExpandedRatio = ratio;
        sheet.HalfExpandedRatio = halfExpandedRatio;
        sheet.CollapsedHeight = contentHeight;
        sheet.AllowedState = SfG9BottomSheetAllowedState.All;
        sheet.State = SfG9BottomSheetState.Collapsed;
    }

    private static void AttachDeferredContentLoadedRefresh(
        CustomizedSfG9BottomSheet sheet,
        DeferredContentView deferred,
        G9BottomSheetOptions options)
    {
        deferred.ContentLoaded += (_, _) =>
        {
            // t here is the tap → content-visible latency the user actually perceives.

            // Real content is now in the tree — stop holding the fixed placeholder height and
            // measure the actual content from here on.
            if (SheetBehaviorStates.TryGetValue(sheet, out var behavior) && behavior is not null)
            {
                behavior.UseDeferredPlaceholderHeight = false;
            }

            ResetDeferredFitContentLoadingMetrics(deferred, options);
            var contentRoot = sheet.G9BottomSheetContent ?? deferred;
            ScheduleFitToContentRefresh(sheet, contentRoot, options);
        };

        // The final corrective resize. The engine HOLDS its height through the whole loading +
        // covered-reveal window, and settling no longer re-parents the content (no native
        // re-attach → no MeasureInvalidated of its own), so this event is the only reliable
        // trigger for measuring the real body and animating to its height.
        deferred.RevealSettled += (_, _) =>
        {
            var contentRoot = sheet.G9BottomSheetContent ?? deferred;
            ScheduleFitToContentRefresh(sheet, contentRoot, options);
        };
    }

    private static void ResetDeferredFitContentLoadingMetrics(DeferredContentView deferred, G9BottomSheetOptions options)
    {
        if (options.SizeMode != G9BottomSheetSizeMode.FitToContent || IsFullScreenPresentation(options))
        {
            return;
        }

        deferred.ClearValue(VisualElement.HeightRequestProperty);
        deferred.ClearValue(VisualElement.MinimumHeightRequestProperty);
    }

    private static void ScheduleFitToContentRefresh(
        CustomizedSfG9BottomSheet sheet,
        View content,
        G9BottomSheetOptions options,
        int delayMs = 16)
    {
        if (!RequiresContentMeasurement(options))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs).ConfigureAwait(true);
            }

            if (IsSheetAlive(sheet) && !IsSheetClosing(sheet))
            {
                ApplySheetContentSizing(sheet, content, options);
            }
        });
    }

    // Keeps a fit-to-content sheet sized to its content even when that content reports a small
    // natural height on the first measure and grows a few frames later. This is the common case
    // for the block-info and pot/tree sheets opened from the sampling map: their bodies render
    // heavy UI (images, charts, attachment thumbnails) and/or fill asynchronously from their own
    // loaders, so a single one-shot measure right after open captures a too-small height and the
    // sheet stays stuck small. By listening to the content root's MeasureInvalidated — the signal
    // MAUI raises whenever a descendant changes size or a child is added — we remeasure (debounced)
    // until the content settles, on every platform, without polling.
    private static void AttachFitToContentSizeTracking(
        CustomizedSfG9BottomSheet sheet,
        View contentRoot,
        G9BottomSheetOptions options)
    {
        if (!RequiresContentMeasurement(options))
        {
            return;
        }

        if (FitToContentSizeTrackers.TryGetValue(sheet, out _))
        {
            return;
        }

        EventHandler handler = (_, _) =>
        {
            var behavior = SheetBehaviorStates.GetValue(
                sheet,
                static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));

            // When the content publishes its own height (IG9BottomSheetContentHeightProvider) we
            // rely solely on its change event for resizes — layout MeasureInvalidated would race
            // it and reintroduce jitter. Ignore our own apply (IsApplyingFitContent), closing
            // sheets, and coalesce bursts via IsFitContentRefreshScheduled.
            if (behavior.HeightProvider is not null ||
                behavior.IsApplyingFitContent ||
                behavior.IsClosing ||
                behavior.IsFitContentRefreshScheduled)
            {
                return;
            }

            behavior.IsFitContentRefreshScheduled = true;
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(FitContentRemeasureDebounceMs).ConfigureAwait(true);
                behavior.IsFitContentRefreshScheduled = false;

                if (IsSheetAlive(sheet) && !IsSheetClosing(sheet))
                {
                    ApplySheetContentSizing(sheet, sheet.G9BottomSheetContent ?? contentRoot, options);
                }
            });
        };

        contentRoot.MeasureInvalidated += handler;
        FitToContentSizeTrackers.AddOrUpdate(sheet, new FitToContentSizeTracker(contentRoot, handler));
    }

    private static void DetachFitToContentSizeTracking(CustomizedSfG9BottomSheet sheet)
    {
        if (SheetBehaviorStates.TryGetValue(sheet, out var behavior) && behavior is not null)
        {
            // Unsubscribe the content height provider, if any.
            if (behavior.HeightProvider is { } provider && behavior.HeightProviderHandler is { } providerHandler)
            {
                provider.G9BottomSheetContentHeightChanged -= providerHandler;
                behavior.HeightProvider = null;
                behavior.HeightProviderHandler = null;
            }

            // Unsubscribe a loadable body's IsLoading window handler (a load that never finished
            // before the sheet closed).
            if (behavior.LoadableBody is { } loadable && behavior.LoadableBodyHandler is { } loadableHandler)
            {
                loadable.PropertyChanged -= loadableHandler;
                behavior.LoadableBody = null;
                behavior.LoadableBodyHandler = null;
            }
        }

        if (!FitToContentSizeTrackers.TryGetValue(sheet, out var tracker) || tracker is null)
        {
            return;
        }

        tracker.Target.MeasureInvalidated -= tracker.Handler;
        FitToContentSizeTrackers.Remove(sheet);
    }

    // True while the sheet body is still in a LOADING visual state: a DeferredContentView whose
    // spinner hasn't been swapped yet, OR an "open then fill" LoadableSheetContentView body whose
    // IsLoading is still set. While true, generic measures read the placeholder/loading UI (not
    // the final content), so the engine keeps the loading floor / placeholder height stable and
    // never records the loading UI's height into the memo. This is what stops the old
    // "open at 180 → shrink to the 125dp loading skeleton → grow to the real 480" dance on the
    // state-machine sheets.
    private static bool ContainsLoadingDeferredContent(View view)
    {
        // IsRevealSettled is the VISUAL "final content is the sole thing on screen" signal. It
        // covers the whole loading window: the spinner/skeleton phase (NOT IsContentLoaded —
        // that flips at load START, mid-spinner, and once poisoned this check into measuring the
        // placeholder as real content) AND the crossfade window where the placeholder is stacked
        // over the content (measuring then returns max of the two).
        if (view is DeferredContentView { IsRevealSettled: false })
        {
            return true;
        }

        if (view is LoadableSheetContentView { IsLoading: true })
        {
            return true;
        }

        return view switch
        {
            Layout layout => layout.Children.OfType<View>().Any(ContainsLoadingDeferredContent),
            ContentView { Content: { } content } => ContainsLoadingDeferredContent(content),
            Border { Content: { } content } => ContainsLoadingDeferredContent(content),
            ScrollView { Content: { } content } => ContainsLoadingDeferredContent(content),
            _ => false
        };
    }

    private static double MeasureContentHeight(
        View content,
        CustomizedSfG9BottomSheet sheet,
        G9BottomSheetOptions options,
        out bool usedFallback)
    {
        var width = ResolveMeasureWidth(sheet, options);
        var measureTarget = ResolveFitToContentMeasureTarget(content, options);
        var measuredHeight = measureTarget.Measure(width, double.PositiveInfinity).Height;
        usedFallback = false;

        if (double.IsNaN(measuredHeight) || measuredHeight <= 0)
        {
            // Cold measure — the platform hasn't laid the tree out yet. The allocated Height
            // fallback is self-referential for a fill-hosted body (it just reads the current
            // bounded viewport), so callers treat BOTH fallbacks as "no real measure" and hold
            // the sheet's current height instead of collapsing to the loading floor.
            usedFallback = true;
            measuredHeight = measureTarget.Height > 0
                ? measureTarget.Height
                : G9LayoutMetrics.FitContentLoadingMinHeight;
        }

        measuredHeight += options.Padding * 2;

        if (ShouldShowSheetGrabber(options))
        {
            measuredHeight += sheet.GrabberAreaHeight;
        }

        // Android open is the classic "sheet opened too small" root cause.

        return measuredHeight;
    }

    /// <summary>
    ///     The vertical chrome the HELPER itself renders around a fit-to-content body: the shared
    ///     header band + its bottom hairline (<see cref="G9BottomSheetOptions.ShowToolbar" />), the
    ///     grabber area, the custom drag-handle row, and the sheet's content padding.
    ///     Caller-supplied heights (<see cref="G9BottomSheetOptions.DeferredLoadingPlaceholderHeight" />,
    ///     the session height memo, <see cref="IG9BottomSheetContentHeightProvider" /> results) are
    ///     BODY heights — callers cannot know the helper chrome — so the sizing tiers add this on
    ///     top. This is what removes the constant ~65dp under-estimate selection sheets used to
    ///     settle through (their estimate omitted the shared header band).
    /// </summary>
    private static double ResolveHelperChromeHeight(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        var chrome = options.Padding * 2;

        if (options.ShowToolbar)
        {
            // Header band + its bottom hairline + the helper-owned standard body-top gap below
            // the hairline (see CreateSheetContentRoot). The gap term must track the same
            // UseStandardBodyTopGap opt-out the layout uses, or a fit-to-content sheet that
            // suppressed it would reserve height for a gap it never draws.
            chrome += G9LayoutMetrics.SheetHeaderMinHeight
                      + G9LayoutMetrics.G9BottomSheetFooterTopBorderThickness;

            if (options.UseStandardBodyTopGap)
            {
                chrome += G9LayoutMetrics.SheetHeaderVerticalGap;
            }
        }

        if (ShouldShowSheetGrabber(options))
        {
            chrome += sheet.GrabberAreaHeight > 0
                ? sheet.GrabberAreaHeight
                : G9LayoutMetrics.G9BottomSheetDragHandleTouchHeight;
        }

        if (ShouldRenderCustomDragHandle(options))
        {
            chrome += G9LayoutMetrics.G9BottomSheetDragHandleTouchHeight;
        }

        return chrome;
    }

    /// <summary>
    ///     Seeds the loading-window height for a deferred fit-to-content sheet: the caller's
    ///     <see cref="G9BottomSheetOptions.DeferredLoadingPlaceholderHeight" /> (a BODY height) when
    ///     provided, otherwise the session height memo recorded the last time this body type
    ///     settled. Covers two body shapes:
    ///     <list type="bullet">
    ///         <item>prebuilt content wrapped in a <c>DeferredContentView</c> — the window clears
    ///         on <c>ContentLoaded</c>;</item>
    ///         <item>"open then fill" <see cref="LoadableSheetContentView" /> bodies — the window
    ///         clears when their <c>IsLoading</c> flips false (subscribed here).</item>
    ///     </list>
    ///     Factory content is excluded (no type to key on before it is built), and
    ///     <see cref="ProcessingSheetContentView" /> is excluded (it sizes itself through its own
    ///     height provider / loading height).
    /// </summary>
    private static void InitializeFitPlaceholder(
        CustomizedSfG9BottomSheet sheet,
        SheetBehaviorState behavior,
        SheetContentRequest contentRequest,
        G9BottomSheetOptions options)
    {
        if (options.SizeMode != G9BottomSheetSizeMode.FitToContent ||
            options.UseFullScreenLoadingPlaceholder ||
            contentRequest.Content is ProcessingSheetContentView)
        {
            return;
        }

        var loadableBody = contentRequest.Content as LoadableSheetContentView;
        if (loadableBody is null &&
            (!options.DeferContent || contentRequest.Content is IDeferredSheetLoad))
        {
            return;
        }

        behavior.FitHeightMemoKey = BuildFitHeightMemoKey(contentRequest.Content, sheet, options);

        var bodyHeight = options.DeferredLoadingPlaceholderHeight ?? 0;
        if (bodyHeight <= 0 &&
            behavior.FitHeightMemoKey is { } key &&
            G9BottomSheetHeightMemoStore.TryGet(key, out var memoBody))
        {
            bodyHeight = memoBody;
        }

        if (bodyHeight <= 0)
        {
            return;
        }

        behavior.PlaceholderBodyHeight = bodyHeight;
        behavior.UseDeferredPlaceholderHeight = true;

        if (loadableBody is not null && loadableBody.IsLoading)
        {
            // An "open then fill" body has no DeferredContentView.ContentLoaded — its IsLoading
            // flip is the placeholder window's end signal. One-shot subscription; also detached
            // on cleanup through the behavior state (DetachFitToContentSizeTracking).
            PropertyChangedEventHandler handler = null!;
            handler = (_, e) =>
            {
                if (e.PropertyName != nameof(LoadableSheetContentView.IsLoading) || loadableBody.IsLoading)
                {
                    return;
                }

                loadableBody.PropertyChanged -= handler;
                if (SheetBehaviorStates.TryGetValue(sheet, out var current) && current is not null)
                {
                    current.UseDeferredPlaceholderHeight = false;
                    current.LoadableBody = null;
                    current.LoadableBodyHandler = null;
                }

                ScheduleFitToContentRefresh(sheet, sheet.G9BottomSheetContent ?? loadableBody, options);
            };

            loadableBody.PropertyChanged += handler;
            behavior.LoadableBody = loadableBody;
            behavior.LoadableBodyHandler = handler;
        }
        else if (loadableBody is not null)
        {
            // Already loaded (reused view) — no window to hold.
            behavior.UseDeferredPlaceholderHeight = false;
        }
    }

    private static string? BuildFitHeightMemoKey(View? content, CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        // Explicit caller key first (the only way FACTORY content can be memoized — it has no
        // type before it is built); prebuilt bodies key on their type automatically. Width-dp,
        // culture AND the platform font scale are part of the key because the memo is PERSISTED:
        // any of them can change between sessions and would otherwise turn remembered heights
        // into systematically-wrong guesses (they'd still animate-correct, but why start wrong).
        var identity = options.HeightMemoKey ?? content?.GetType().FullName;
        if (string.IsNullOrEmpty(identity))
        {
            return null;
        }

        var width = (int)Math.Round(ResolveMeasureWidth(sheet, options));
        return $"{identity}|w{width}|{CultureInfo.CurrentUICulture.Name}|fs{G9BottomSheetHeightMemoStore.ResolveFontScaleKeyComponent()}";
    }

    // Records the settled BODY height for a memoizable body (called from the "measure" tier once
    // real, loaded content produced a genuine measurement). Values are total-minus-chrome so the
    // read side shares one code path with caller-supplied placeholder heights.
    private static void RecordFitHeightMemo(
        CustomizedSfG9BottomSheet sheet,
        SheetBehaviorState behavior,
        G9BottomSheetOptions options,
        double naturalTotalHeight)
    {
        if (behavior.FitHeightMemoKey is not { } key)
        {
            return;
        }

        var bodyHeight = naturalTotalHeight - ResolveHelperChromeHeight(sheet, options);
        if (bodyHeight <= 0)
        {
            return;
        }

        G9BottomSheetHeightMemoStore.Record(key, bodyHeight);
    }

    private static View ResolveFitToContentMeasureTarget(View view, G9BottomSheetOptions options)
    {
        if (!RequiresContentMeasurement(options))
        {
            return view;
        }

        if (view is DeferredContentView { IsRevealSettled: false })
        {
            return view;
        }

        if (view is ScrollView { Content: View scrollContent })
        {
            return ResolveFitToContentMeasureTarget(scrollContent, options);
        }

        if (view is ContentView { Content: View content })
        {
            return ResolveFitToContentMeasureTarget(content, options);
        }

        if (view is Border { Content: View borderContent })
        {
            return ResolveFitToContentMeasureTarget(borderContent, options);
        }

        if (view is Layout layout && IsTransparentFitToContentSizingWrapper(layout))
        {
            var visibleChildren = layout.Children
                .OfType<View>()
                .Where(child => child.IsVisible)
                .Take(2)
                .ToList();

            if (visibleChildren.Count == 1)
            {
                return ResolveFitToContentMeasureTarget(visibleChildren[0], options);
            }
        }

        return view;
    }

    // Returns true when the sheet body, after unwrapping transparent wrappers (single-cell
    // grids, ContentView/Border shells, a loaded DeferredContentView), is itself a vertical
    // scroller (ScrollView / CollectionView / CarouselView / ListView). Such a body can't be
    // measured reliably for fit-to-content (a scroller reports its viewport, and on a cold
    // open Android hasn't measured the inner children yet), so the caller caps it instead.
    // We deliberately do NOT descend into the scroller.
    private static bool IsRootGreedyScroller(View? view, int depth = 0)
    {
        if (view is null || depth > 8)
        {
            return false;
        }

        switch (view)
        {
            case ScrollView:
            case ItemsView: // CollectionView / CarouselView / ListView
                return true;
            case DeferredContentView { IsRevealSettled: false }:
                return false; // placeholder still on screen — not a scroller yet
            case ContentView { Content: View contentChild }: // also covers a loaded DeferredContentView
                return IsRootGreedyScroller(contentChild, depth + 1);
            case Border { Content: View borderChild }:
                return IsRootGreedyScroller(borderChild, depth + 1);
            case Layout layout when IsTransparentFitToContentSizingWrapper(layout):
                var visibleChildren = layout.Children
                    .OfType<View>()
                    .Where(child => child.IsVisible)
                    .Take(2)
                    .ToList();
                return visibleChildren.Count == 1 && IsRootGreedyScroller(visibleChildren[0], depth + 1);
        }

        return false;
    }

    private static bool IsTransparentFitToContentSizingWrapper(Layout layout)
    {
        if (layout is not Grid grid)
        {
            return false;
        }

        if (layout.Padding.Left > 0 ||
            layout.Padding.Top > 0 ||
            layout.Padding.Right > 0 ||
            layout.Padding.Bottom > 0)
        {
            return false;
        }

        return grid.RowDefinitions.Count <= 1 &&
               grid.ColumnDefinitions.Count <= 1;
    }

    private static double ResolveMeasureWidth(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        if (options.ContentWidthMode == SfG9BottomSheetContentWidthMode.Custom &&
            options.G9BottomSheetContentWidth is > 0)
        {
            return options.G9BottomSheetContentWidth.Value;
        }

        if (G9ModalHostRegistry.TryGetCurrentHost(out var host) && host.Page.Width > 0)
        {
            return host.Page.Width;
        }

        if (sheet.Width > 0)
        {
            return sheet.Width;
        }

        var display = DeviceDisplay.MainDisplayInfo;
        return display.Density > 0
            ? display.Width / display.Density
            : 480;
    }

    private static void RunOpenedCommandLater(CustomizedSfG9BottomSheet sheet)
    {
        var behavior = SheetBehaviorStates.GetValue(sheet, static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));

        // The opened work (OpenedCommand + the deferred "open then fill" load) starts the moment
        // the open motion ACTUALLY finishes, via the sheet's OpenMotionCompleted event. The open
        // animation is size-scaled, so a fit-to-content open often completes in ~80 ms — the old
        // fixed (non-scaled) Open-duration timer delayed deferred loads by ~300 ms on every
        // partial-height sheet, pushing the content's real height past the scheduled settle
        // passes. The timer is kept only as a fallback for opens that never animate (the
        // InvalidOperationException `IsOpen = true` path) or whose finished callback is lost.
        var delayMs = Math.Max(0, (int)ResolveOpenAnimationDurationMs(behavior.Options));

        var ran = false;

        void RunOpenedWorkOnce(string trigger)
        {
            if (ran)
            {
                return;
            }

            if (!IsSheetAlive(sheet))
            {
                ran = true;
                sheet.OpenMotionCompleted -= OnOpenMotionCompleted;
                return;
            }

            if (!sheet.IsOpen)
            {
                // Not open yet (a deferred show is still waiting for the host to allocate a
                // height). Leave `ran` unset — the OpenMotionCompleted event fires once the real
                // open runs and will complete the work then.
                return;
            }

            ran = true;
            sheet.OpenMotionCompleted -= OnOpenMotionCompleted;

            RunCommand(behavior.Options.OpenedCommand, behavior.Options.OpenedCommandParameter);
            TriggerDeferredLoad(sheet, behavior);
        }

        void OnOpenMotionCompleted(object? s, EventArgs e)
        {
            RunOpenedWorkOnce("animationFinished");
        }

        sheet.OpenMotionCompleted += OnOpenMotionCompleted;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs).ConfigureAwait(true);
            }

            RunOpenedWorkOnce("timerFallback");
        });
    }

    /// <summary>
    ///     Records "open then fill" content so <see cref="TriggerDeferredLoad" /> can drive its
    ///     load after the sheet is visible. No-op for content that isn't an
    ///     <see cref="IDeferredSheetLoad" />.
    /// </summary>
    private static void RegisterDeferredLoad(CustomizedSfG9BottomSheet sheet, View content)
    {
        if (content is not IDeferredSheetLoad deferredLoad)
        {
            return;
        }

        var behavior = SheetBehaviorStates.GetValue(
            sheet,
            static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));
        behavior.DeferredLoad = deferredLoad;
    }

    /// <summary>
    ///     Invokes the registered <see cref="IDeferredSheetLoad" /> once, after the open animation,
    ///     under a CTS that is cancelled when the sheet tears down. The load is fire-and-forget and
    ///     fully guarded so a data-fetch failure can never crash the open path.
    /// </summary>
    private static void TriggerDeferredLoad(CustomizedSfG9BottomSheet sheet, SheetBehaviorState behavior)
    {
        var deferredLoad = behavior.DeferredLoad;
        if (deferredLoad is null || behavior.DeferredLoadCts is not null || behavior.IsClosing)
        {
            if (deferredLoad is not null)
            {
            }

            return;
        }

        var cts = new CancellationTokenSource();
        behavior.DeferredLoadCts = cts;

        // after the sheet is already visible.

        G9SafeCommand.RunSafe(
            () => deferredLoad.LoadDeferredAsync(cts.Token),
            G9SafeCommand.CreateFireAndForgetOperationOptions(
                nameof(G9BottomSheetHelper),
                $"{nameof(G9BottomSheetHelper)}.{nameof(TriggerDeferredLoad)}"));
    }

    private static void CancelDeferredLoad(CustomizedSfG9BottomSheet sheet)
    {
        if (!SheetBehaviorStates.TryGetValue(sheet, out var behavior) || behavior is null)
        {
            return;
        }

        var cts = behavior.DeferredLoadCts;
        behavior.DeferredLoadCts = null;
        behavior.DeferredLoad = null;
        if (cts is null)
        {
            return;
        }

        try
        {
            cts.Cancel();
            cts.Dispose();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }
    }

    private static void RunClosingCommandIfNeeded(CustomizedSfG9BottomSheet sheet)
    {
        var behavior = SheetBehaviorStates.GetValue(sheet, static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));
        behavior.IsClosing = true;
        if (behavior.IsClosingCommandInvoked)
        {
            return;
        }

        behavior.IsClosingCommandInvoked = true;
        RunCommand(behavior.Options.ClosingCommand, behavior.Options.ClosingCommandParameter);
    }

    private static void RunClosedCommandIfNeeded(CustomizedSfG9BottomSheet sheet)
    {
        var behavior = SheetBehaviorStates.GetValue(sheet, static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));
        if (!behavior.IsClosingCommandInvoked)
        {
            RunClosingCommandIfNeeded(sheet);
        }

        if (behavior.IsClosedCommandInvoked)
        {
            return;
        }

        behavior.IsClosedCommandInvoked = true;
        RunCommand(behavior.Options.ClosedCommand, behavior.Options.ClosedCommandParameter);
    }

    private static bool IsSheetClosing(CustomizedSfG9BottomSheet sheet)
    {
        var behavior = SheetBehaviorStates.GetValue(
            sheet,
            static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));

        return behavior.IsClosing;
    }

    private static void RunCommand(ICommand? command, object? parameter)
    {
        if (command?.CanExecute(parameter) == true)
        {
            command.Execute(parameter);
        }
    }

    /// <summary>
    ///     Hosts a non-full-screen body inside the sheet, optionally inside a helper-owned vertical
    ///     scroll viewport (<see cref="G9BottomSheetOptions.ExpandedFitsContent" />).
    /// </summary>
    /// <param name="sheet">The sheet the body belongs to; owns the viewport reference.</param>
    /// <param name="content">The body (or the deferred wrapper standing in for it) to host.</param>
    /// <param name="options">The options this sheet was opened with.</param>
    /// <param name="bodyProbe">
    ///     The REAL body when <paramref name="content" /> is a deferred wrapper around it — the
    ///     "is this already a scroller?" test has to see the eventual tree, not the spinner.
    /// </param>
    private static View CreateFillHost(
        CustomizedSfG9BottomSheet sheet,
        View content,
        G9BottomSheetOptions options,
        View? bodyProbe = null)
    {
        // Fit-to-content used to host its body Top-aligned (Start) so it wouldn't stretch. But
        // when the content is capped at MaxFitToContentHeightRatio (a long list), the body must be
        // a BOUNDED viewport so the inner scroller can scroll — that requires Fill. For content
        // that fits under the cap, the body height equals the content's natural height, so Fill is
        // a no-op (no stretch). This is what makes "grow to 75% then scroll" work.
        content.HorizontalOptions = LayoutOptions.Fill;
        content.VerticalOptions = LayoutOptions.Fill;

        var host = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            FlowDirection = ResolveCurrentFlowDirection(),
            BackgroundColor = options.BackgroundColor ?? G9Palette.Current.Background
        };
        host.Children.Add(content);

        if (!ShouldHostBodyInScrollViewport(options, bodyProbe ?? content))
        {
            return host;
        }

        // An ExpandedFitsContent sheet promises "grow to the content, and if the content is taller
        // than the cap, scroll inside it" — which needs a scroller the caller did not have to
        // write. Natural-height (Start) inside the viewport: Fill would let the body stretch to the
        // viewport and there would be nothing to scroll.
        content.VerticalOptions = LayoutOptions.Start;
        host.VerticalOptions = LayoutOptions.Start;

        var viewport = new ScrollView
        {
            Content = host,
            Orientation = ScrollOrientation.Vertical,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            FlowDirection = ResolveCurrentFlowDirection(),
            BackgroundColor = options.BackgroundColor ?? G9Palette.Current.Background
        };

        SheetBehaviorStates
            .GetValue(sheet, static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()))
            .BodyScrollViewport = viewport;

        return viewport;
    }

    /// <summary>
    ///     Whether the helper should wrap this body in its own scroll viewport. Never for a body
    ///     that already scrolls — a list inside a ScrollView is the classic MAUI measurement trap
    ///     (the outer scroller hands the inner one infinite height and virtualization dies).
    /// </summary>
    private static bool ShouldHostBodyInScrollViewport(G9BottomSheetOptions options, View body)
    {
        return UsesExpandedFitsContent(options) && !IsRootGreedyScroller(body);
    }

    private static View WrapBodyOnlyRootForDrag(CustomizedSfG9BottomSheet sheet, View contentRoot, G9BottomSheetOptions options)
    {
        AttachSheetContentDragGesture(sheet, contentRoot, options);
        return contentRoot;
    }

    private static bool ShouldRenderCustomDragHandle(G9BottomSheetOptions options)
    {
        return options.ShowToolbar &&
               options.ShowFullScreenDragHandle &&
               ShouldShowSheetGrabber(options) &&
               ShouldEnableDragToClose(options);
    }

    private static View CreateDragHandleSurface(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        var surface = new Grid
        {
            HeightRequest = G9LayoutMetrics.G9BottomSheetDragHandleTouchHeight,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Start,
            BackgroundColor = options.BackgroundColor ?? G9Palette.Current.Background,
            InputTransparent = false
        };

        var handle = new Border
        {
            WidthRequest = G9LayoutMetrics.G9BottomSheetDragHandleWidth,
            HeightRequest = G9LayoutMetrics.G9BottomSheetDragHandleHeight,
            StrokeThickness = 0,
            BackgroundColor = G9Palette.Current.OnSurfaceVariant.WithAlpha(0.35f),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            StrokeShape = new RoundRectangle
            {
                CornerRadius = new CornerRadius(G9LayoutMetrics.G9BottomSheetDragHandleHeight / 2)
            },
            InputTransparent = true
        };

        surface.Children.Add(handle);
        AttachDragToCloseGesture(sheet, surface, options);
        return surface;
    }

    private static void AttachSheetContentDragGesture(CustomizedSfG9BottomSheet sheet, View dragSurface, G9BottomSheetOptions options)
    {
        // Intentional no-op. The G9SheetView control owns its drag-state machine on every
        // platform — Android (`G9SheetViewBorder.Android.cs`), iOS / Mac Catalyst
        // (`G9SheetViewBorder.iOS.cs`), and Windows (`G9SheetViewBorder.Windows.cs`)
        // each install a per-platform handler that intercepts vertical drags on the body and
        // forwards them to `G9SheetView.OnHandleTouch`, which drives `TranslationY` /
        // `HeightRequest` and snaps to the right state on release.
        //
        // The previous helper-side `PanGestureRecognizer` / `SwipeGestureRecognizer` setup was
        // doing the same job at a higher level (translating `target.TranslationY` of the
        // content host) and the two animators raced each other on Windows — visible as a blink
        // every drag tick. On Android the MAUI pan gesture barely fired over Border children,
        // so the body would never move at all. Both bugs disappeared as soon as the helper
        // stopped racing with the control.
        //
        // The helper still listens to `G9SheetView.PositionChanged` (in
        // `AttachPositionTracking`) for modal-overlay alpha and the backdrop card recede, and
        // to `G9SheetView.BackRequested` for drag-to-close routing through `OnBackRequested`
        // / `IsCancelable`. See `AttachBackRequestedRouting` below.
        _ = sheet;
        _ = dragSurface;
        _ = options;
    }

    private static void AttachDragToCloseGesture(CustomizedSfG9BottomSheet sheet, View dragSurface, G9BottomSheetOptions options)
    {
        // Intentional no-op — see comment on `AttachSheetContentDragGesture`. Drag-to-close is
        // raised by the control itself via `G9SheetView.BackRequested` and routed through
        // `HandleBackRequest` in `OnSheetBackRequested`.
        _ = sheet;
        _ = dragSurface;
        _ = options;
    }

    private static void AttachDragToStateGesture(CustomizedSfG9BottomSheet sheet, View dragSurface, G9BottomSheetOptions options)
    {
        // Intentional no-op — see comment on `AttachSheetContentDragGesture`. State drags are
        // driven by the control's own per-platform handler.
        _ = sheet;
        _ = dragSurface;
        _ = options;
    }

    private static bool TryApplyContentDragSheetState(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options, double totalY)
    {
        if (totalY > 0 && HasFixedAllowedState(options))
        {
            if (options.IsCancelable)
            {
                CloseSheet(sheet);
                return true;
            }

            return false;
        }

        if (options.SizeMode != G9BottomSheetSizeMode.States)
        {
            return false;
        }

        var allowedStates = NormalizeAllowedStates(options.States);
        if (allowedStates.Count == 0)
        {
            return false;
        }

        var currentState = MapState(sheet.State);
        var currentIndex = allowedStates.IndexOf(currentState);
        if (currentIndex < 0)
        {
            currentIndex = Math.Max(0, allowedStates.IndexOf(options.CurrentState));
        }

        var nextIndex = totalY < 0
            ? Math.Min(allowedStates.Count - 1, currentIndex + 1)
            : Math.Max(0, currentIndex - 1);

        if (nextIndex == currentIndex)
        {
            if (totalY > 0 && options.IsCancelable)
            {
                CloseSheet(sheet);
                return true;
            }

            return false;
        }

        sheet.State = MapState(allowedStates[nextIndex]);
        return true;
    }

    private static List<G9BottomSheetState> NormalizeAllowedStates(IEnumerable<G9BottomSheetState> states)
    {
        var distinct = states.Distinct().ToHashSet();
        var ordered = new List<G9BottomSheetState>(3);

        if (distinct.Contains(G9BottomSheetState.Peek))
        {
            ordered.Add(G9BottomSheetState.Peek);
        }

        if (distinct.Contains(G9BottomSheetState.Medium))
        {
            ordered.Add(G9BottomSheetState.Medium);
        }

        if (distinct.Contains(G9BottomSheetState.Large))
        {
            ordered.Add(G9BottomSheetState.Large);
        }

        return ordered;
    }

    private static G9BottomSheetState MapState(SfG9BottomSheetState state)
    {
        return state switch
        {
            SfG9BottomSheetState.Collapsed => G9BottomSheetState.Peek,
            SfG9BottomSheetState.FullExpanded => G9BottomSheetState.Large,
            SfG9BottomSheetState.HalfExpanded => G9BottomSheetState.Medium,
            _ => G9BottomSheetState.Medium
        };
    }

    private static double? ResolveInteractiveDragHeightRatio(
        CustomizedSfG9BottomSheet sheet,
        G9BottomSheetOptions options,
        double totalY)
    {
        if (options.SizeMode != G9BottomSheetSizeMode.States)
        {
            return null;
        }

        var fullHeight = ResolveFullScreenHeight();
        if (fullHeight <= 0)
        {
            return null;
        }

        var startRatio = ResolveStateHeightRatio(sheet, options, sheet.State);
        var approximateRatio = startRatio - (totalY / fullHeight);
        return Math.Clamp(approximateRatio, ResolveMinimumStateHeightRatio(sheet, options), ResolveFullStateHeightRatio(options));
    }

    private static double ResolveStateHeightRatio(
        CustomizedSfG9BottomSheet sheet,
        G9BottomSheetOptions options,
        SfG9BottomSheetState state)
    {
        return state switch
        {
            SfG9BottomSheetState.Collapsed => ResolveCollapsedHeightRatio(sheet, options),
            SfG9BottomSheetState.HalfExpanded => ResolveHalfStateHeightRatio(options),
            SfG9BottomSheetState.FullExpanded => ResolveFullStateHeightRatio(options),
            _ => ResolveMinimumStateHeightRatio(sheet, options)
        };
    }

    private static double ResolveMinimumStateHeightRatio(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        if (options.SizeMode == G9BottomSheetSizeMode.FitToContent)
        {
            return ResolveFitContentHeightRatio(sheet);
        }

        if (options.States.Contains(G9BottomSheetState.Peek))
        {
            return ResolveCollapsedHeightRatio(sheet, options);
        }

        if (options.States.Contains(G9BottomSheetState.Medium))
        {
            return ResolveHalfStateHeightRatio(options);
        }

        return ResolveFullStateHeightRatio(options);
    }

    private static double ResolveCollapsedHeightRatio(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        var fullHeight = ResolveFullScreenHeight();
        if (fullHeight <= 0)
        {
            return 0.1;
        }

        var collapsedHeight = options.CollapsedHeight
                              ?? options.PeekHeight
                              ?? sheet.CollapsedHeight;

        return collapsedHeight > 0
            ? Math.Clamp(collapsedHeight / fullHeight, 0.1, 1)
            : 0.1;
    }

    private static double ResolveHalfStateHeightRatio(G9BottomSheetOptions options)
    {
        var ratio = options.AndroidHalfExpandedRatio.HasValue
            ? options.AndroidHalfExpandedRatio.Value
            : options.HalfExpandedRatio ?? 0.5;

        return Math.Clamp(ratio, 0.1, 0.9);
    }

    private static double ResolveFullStateHeightRatio(G9BottomSheetOptions options)
    {
        return Math.Clamp(options.FullExpandedRatio ?? 1, 0.1, 1);
    }

    private static double ResolveFitContentHeightRatio(CustomizedSfG9BottomSheet sheet)
    {
        var behavior = SheetBehaviorStates.GetValue(
            sheet,
            static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));

        var fullHeight = ResolveFullScreenHeight();
        if (fullHeight <= 0 || behavior.LastFitContentHeight <= 0)
        {
            return 0.1;
        }

        return Math.Clamp(behavior.LastFitContentHeight / fullHeight, 0.1, 1);
    }

    private static double ResolveLargeStateProgress(
        CustomizedSfG9BottomSheet sheet,
        G9BottomSheetOptions options,
        double heightRatio)
    {
        var fullRatio = ResolveFullStateHeightRatio(options);
        var baselineRatio = options.States.Contains(G9BottomSheetState.Medium)
            ? ResolveHalfStateHeightRatio(options)
            : ResolveMinimumStateHeightRatio(sheet, options);

        if (Math.Abs(fullRatio - baselineRatio) < 0.01)
        {
            return 1;
        }

        return Math.Clamp((heightRatio - baselineRatio) / (fullRatio - baselineRatio), 0, 1);
    }

    private static void AttachNativeContentDragGesture(
        CustomizedSfG9BottomSheet sheet,
        View dragSurface,
        G9BottomSheetOptions options)
    {
        // Intentional no-op — superseded by the G9SheetView platform handlers. The Android
        // ContentViewGroup we install in `G9SheetViewBorder.Android.cs` already intercepts
        // vertical drags at the right level (above MAUI's measure pass and the Border child
        // handlers that swallowed direct `view.Touch` deltas), so the previous walk-the-tree
        // recursive `Touch += ...` attachment is no longer needed and would cause double-fire
        // on Android 14+.
        _ = sheet;
        _ = dragSurface;
        _ = options;
    }

#if ANDROID
    // The previously-recursive `view.Touch +=` walker has been removed — see the comment on
    // `AttachNativeContentDragGesture`. The Android handler in `G9SheetViewBorder.Android.cs`
    // now intercepts vertical drags at the right level and forwards them to
    // `G9SheetView.OnHandleTouch`, eliminating both the double-fire and the platform-specific
    // attachment-walking complexity.
#endif

    private static bool ShouldEnableDragToClose(G9BottomSheetOptions options)
    {
        return options.IsDraggable && IsFullScreenPresentation(options);
    }

    private static bool ShouldEnableStateContentDrag(G9BottomSheetOptions options)
    {
        return options.IsDraggable &&
               !IsFullScreenPresentation(options) &&
               (options.SizeMode == G9BottomSheetSizeMode.States || options.SizeMode == G9BottomSheetSizeMode.FitToContent);
    }

    private static bool CanScrollableContentConsumeDrag(View view, double totalY)
    {
        if (Math.Abs(totalY) < 1)
        {
            return false;
        }

        if (!ContainsScrollableContent(view))
        {
            return false;
        }

#if ANDROID
        if (CanAndroidScrollableContentConsumeDrag(view, totalY))
        {
            return true;
        }
#endif

        return CanMauiScrollableContentConsumeDrag(view, totalY);
    }

    private static bool ContainsScrollableContent(View view)
    {
        if (IsScrollableContentView(view))
        {
            return true;
        }

        return view switch
        {
            Layout layout => layout.Children.OfType<View>().Any(ContainsScrollableContent),
            ContentView { Content: { } content } => ContainsScrollableContent(content),
            Border { Content: { } content } => ContainsScrollableContent(content),
            _ => false
        };
    }

    private static bool IsScrollableContentView(View view)
    {
        if (view is ScrollView or CollectionView or CarouselView)
        {
            return true;
        }

        var typeName = view.GetType().Name;
        return typeName.Contains("VirtualScroll", StringComparison.Ordinal) ||
               typeName.Equals("ListView", StringComparison.Ordinal);
    }

    private static bool CanMauiScrollableContentConsumeDrag(View view, double totalY)
    {
        return view switch
        {
            ScrollView scrollView => CanScrollViewConsumeDrag(scrollView, totalY),
            Layout layout => layout.Children.OfType<View>()
                .Any(child => CanMauiScrollableContentConsumeDrag(child, totalY)),
            ContentView { Content: { } content } => CanMauiScrollableContentConsumeDrag(content, totalY),
            Border { Content: { } content } => CanMauiScrollableContentConsumeDrag(content, totalY),
            _ => false
        };
    }

    private static bool CanScrollViewConsumeDrag(ScrollView scrollView, double totalY)
    {
        if (scrollView.Orientation is ScrollOrientation.Horizontal or ScrollOrientation.Neither)
        {
            return false;
        }

        var contentHeight = scrollView.Content?.Height ?? 0;
        var viewportHeight = scrollView.Height;
        var maxScrollY = Math.Max(0, contentHeight - viewportHeight);

        return totalY > 0
            ? scrollView.ScrollY > 1
            : scrollView.ScrollY < maxScrollY - 1;
    }

#if ANDROID
    private static bool CanAndroidScrollableContentConsumeDrag(View view, double totalY)
    {
        if (view.Handler?.PlatformView is not Android.Views.View nativeView)
        {
            return false;
        }

        var direction = totalY > 0 ? -1 : 1;
        return CanAndroidViewScrollVertically(nativeView, direction);
    }

    private static bool CanAndroidViewScrollVertically(Android.Views.View view, int direction)
    {
        if (view.CanScrollVertically(direction))
        {
            return true;
        }

        if (view is not Android.Views.ViewGroup viewGroup)
        {
            return false;
        }

        for (var index = 0; index < viewGroup.ChildCount; index++)
        {
            if (viewGroup.GetChildAt(index) is { } child &&
                CanAndroidViewScrollVertically(child, direction))
            {
                return true;
            }
        }

        return false;
    }
#endif

    private static View CreateFullScreenSizingHost(CustomizedSfG9BottomSheet sheet, View content, G9BottomSheetOptions options)
    {
        content.HorizontalOptions = LayoutOptions.Fill;
        content.VerticalOptions = LayoutOptions.Fill;

        if (!IsFullScreenPresentation(options) && !SupportsStateAwareTopPadding(options))
        {
            return content;
        }

        var host = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            FlowDirection = ResolveCurrentFlowDirection(),
            BackgroundColor = options.BackgroundColor ?? G9Palette.Current.Background,
            Padding = Thickness.Zero
        };
        host.Children.Add(content);

        RegisterTopPaddingTarget(sheet, host, value => host.Padding = new Thickness(0, value, 0, 0));
        ApplyFullScreenHeight(host, options);
        return host;
    }

    private static void AttachSheetOverlayHost(CustomizedSfG9BottomSheet sheet, View bodyOnlyRoot, G9BottomSheetOptions options)
    {
        if (!IsFullScreenPresentation(options))
        {
            return;
        }

        if (bodyOnlyRoot is not Grid root)
        {
            return;
        }

        var sheetOverlayHost = CreateSheetOverlayHost();
        root.Children.Add(sheetOverlayHost);
        sheetOverlayHost.ZIndex = int.MaxValue - 1;
        SheetOverlayHosts.AddOrUpdate(sheet, sheetOverlayHost);
    }

    private static double ResolveFullScreenTopPadding(G9BottomSheetOptions options)
    {
        return G9LayoutMetrics.ResolveFullScreenSheetTopPadding(
            options.UseTopSafeAreaPadding ? ResolveTopSafeAreaInset() : 0,
            options.UseTopSafeAreaPadding,
            options.AdditionalTopSafeAreaPadding,
            options.TopSafeAreaPaddingOverride);
    }

    private static double ResolveTopSafeAreaInset()
    {
        return G9ModalHostRegistry.TryGetCurrentHost(out var host)
            ? host.Page.TopSafeAreaInset
            : 0;
    }

    private static FlowDirection ResolveCurrentFlowDirection()
    {
        if (G9ModalHostRegistry.TryGetCurrentHost(out var host))
        {
            return host.Page.FlowDirection;
        }

        if (Application.Current?.Resources.TryGetValue("CurrentFlowDirection", out var value) == true &&
            value is FlowDirection flowDirection)
        {
            return flowDirection;
        }

        return CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
    }

    private static Grid CreateSheetOverlayHost()
    {
        return new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            CascadeInputTransparent = false
        };
    }

    private static IReadOnlyList<ToolbarItem>? ResolveInitialToolbarItems(
        SheetContentRequest contentRequest,
        G9BottomSheetOptions options)
    {
        if (options.ToolbarItems is { Count: > 0 } toolbarItems)
        {
            return toolbarItems;
        }

        return contentRequest.Content is not null
            ? options.ToolbarItemsFactory?.Invoke(contentRequest.Content)
            : null;
    }

    private static void PrepareSheetContent(
        View content,
        IG9BottomSheetHandle handle,
        G9BottomSheetOptions options)
    {
        content.HorizontalOptions = LayoutOptions.Fill;

        if (IsFullScreenPresentation(options))
        {
            content.VerticalOptions = LayoutOptions.Fill;
            if (!options.ShowToolbar)
            {
                ApplyFullScreenHeight(content, options, ResolveFullScreenContentHeight(options));
            }
        }

        if (content is IG9BottomSheetAwareView bottomSheetAwareView)
        {
            bottomSheetAwareView.G9BottomSheetHandle = handle;
        }


        var sizedHeight = -1d;
        if (content is IG9BottomSheetSizedView sizedView)
        {
            var height = IsFullScreenPresentation(options)
                ? ResolveFullScreenContentHeight(options)
                : content.HeightRequest;

            sizedHeight = height;
            sizedView.ApplyG9BottomSheetHeight(height);
        }

    }

    // ---------------------------
    // Shared 3-slot header (leading | title | trailing)
    // ---------------------------
    // Defaults: leading = back button (when ShowCloseButton = true),
    //           title   = options.Title centered (or near-back when configured),
    //           trailing = toolbar items stack.
    // Programmer overrides (in priority order):
    //   1. HeaderView                        -> replaces all three slots.
    //   2. HeaderLeadingAndTitleView         -> spans columns 0..1; trailing slot still built.
    //      HeaderTitleAndTrailingView        -> spans columns 1..2; leading slot still built.
    //   3. HeaderLeadingView / HeaderTitleView / HeaderTrailingView -> per-slot overrides.
    // Column-width strategy:
    //   - Symmetric (Star / Auto / Star): used when the title is centered and no spans are in
    //     play. Both side slots reserve equal Star widths so the centered title is visually
    //     centered on screen even when only one side has content (the common back + title
    //     case). Opt-out via G9BottomSheetOptions.ReserveEmptyHeaderSlots = false.
    //   - Asymmetric (Auto / Star / Auto): legacy layout used for NearBack titles, spanned
    //     header views, and when the caller explicitly disables ReserveEmptyHeaderSlots.
    // All other layouts (padding, column spacing, icon sizes) come from G9LayoutMetrics so
    // every sheet that opts into the template shares the same appearance on Android, iOS,
    // Mac Catalyst, and Windows.
    private static Grid BuildSheetHeader(
        CustomizedSfG9BottomSheet sheet,
        G9BottomSheetOptions options,
        IReadOnlyList<ToolbarItem>? toolbarItems,
        out HorizontalStackLayout? toolbarActionsHost)
    {
        var flowDirection = ResolveCurrentFlowDirection();
        var headerHostBackground = options.BackgroundColor ?? G9Palette.Current.Background;


        // The header owns the vertical rhythm around itself: the SAME gap above and below its
        // items. The top-padding band above it is exactly the status-bar inset (see
        // G9LayoutMetrics.ResolveFullScreenSheetTopPadding), so the top gap is measured from
        // directly under the status bar; the bottom gap is the only separation to the body, which
        // is why sheet bodies must not add a top margin of their own (ModalBaseBodyMargin).
        var headerHostPadding = new Thickness(
            G9LayoutMetrics.EdgeSpacing,
            G9LayoutMetrics.SheetHeaderVerticalGap,
            G9LayoutMetrics.EdgeSpacing,
            G9LayoutMetrics.SheetHeaderVerticalGap);

        // 1. Full custom header -> single-column host wrapping the user's view.
        if (options.HeaderView is { } fullHeaderView)
        {
            toolbarActionsHost = null;
            return BuildSingleSlotHeaderHost(fullHeaderView, headerHostBackground, headerHostPadding, flowDirection);
        }

        var hasLeadingSpan = options.HeaderLeadingAndTitleView is not null;
        var hasTrailingSpan = options.HeaderTitleAndTrailingView is not null;

        // Whether the header has — or may later receive via ToolbarItemsFactory — trailing
        // content (toolbar icons / a custom trailing view). The symmetric Star/Auto/Star layout
        // truly screen-centers the title, but its Auto middle column GROWS with a long title and
        // starves the Star side columns to zero width — which squeezes the trailing toolbar icons
        // out of view (the reported bug on long transfer titles). So we only use the symmetric
        // layout when there is no trailing affordance at all; otherwise we fall back to the
        // bounded Auto/Star/Auto layout where the middle Star column constrains the title (it
        // truncates with "…") and the trailing icons keep their natural width and stay visible.
        // ToolbarItemsFactory is included because deferred sheets graft their toolbar items in
        // after the header is built, so the passed-in toolbarItems list can still be empty here.
        var mayHaveTrailingContent =
            options.HeaderTrailingView is not null ||
            options.ToolbarItems is { Count: > 0 } ||
            options.ToolbarItemsFactory is not null ||
            toolbarItems is { Count: > 0 };

        var useSymmetricColumns =
            options.ReserveEmptyHeaderSlots &&
            options.HeaderTitlePlacement == G9BottomSheetHeaderTitlePlacement.Center &&
            !hasLeadingSpan &&
            !hasTrailingSpan &&
            !mayHaveTrailingContent;

        var sideColumnWidth = useSymmetricColumns ? GridLength.Star : GridLength.Auto;
        var middleColumnWidth = useSymmetricColumns ? GridLength.Auto : GridLength.Star;

        var header = new Grid
        {
            // A single Star row so the header band can be taller than its content (see
            // SheetHeaderMinHeight) with every slot centered on one line: the Star row fills the
            // min-height and each Center-aligned child (back/close, title, trailing icons) centres
            // within it.
            RowDefinitions = { new RowDefinition { Height = GridLength.Star } },
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = sideColumnWidth },
                new ColumnDefinition { Width = middleColumnWidth },
                new ColumnDefinition { Width = sideColumnWidth }
            },
            ColumnSpacing = G9LayoutMetrics.ModalHeaderIconTitleSpacing,
            Padding = headerHostPadding,
            MinimumHeightRequest = G9LayoutMetrics.SheetHeaderMinHeight,
            FlowDirection = flowDirection,
            BackgroundColor = headerHostBackground
        };

        // 2a. Spanned leading + title (columns 0..1).
        if (hasLeadingSpan)
        {
            var leadingTitleView = options.HeaderLeadingAndTitleView!;
            PrepareHeaderSlotView(leadingTitleView, alignToStart: flowDirection == FlowDirection.LeftToRight);
            header.Children.Add(leadingTitleView);
            Grid.SetColumn(leadingTitleView, 0);
            Grid.SetColumnSpan(leadingTitleView, 2);

            toolbarActionsHost = AddTrailingSlot(header, sheet, options, toolbarItems, useSymmetricColumns);
            return header;
        }

        // 2b. Spanned title + trailing (columns 1..2).
        if (hasTrailingSpan)
        {
            var titleTrailingView = options.HeaderTitleAndTrailingView!;
            var spannedBackButton = AddLeadingSlot(header, sheet, options, flowDirection, useSymmetricColumns);

            PrepareHeaderSlotView(titleTrailingView, alignToEnd: true);
            header.Children.Add(titleTrailingView);
            Grid.SetColumn(titleTrailingView, 1);
            Grid.SetColumnSpan(titleTrailingView, 2);

            AttachHeaderBackButtonHitSlop(header, sheet, spannedBackButton);
            toolbarActionsHost = null;
            return header;
        }

        // 3. Per-slot template.
        var backButton = AddLeadingSlot(header, sheet, options, flowDirection, useSymmetricColumns);
        AddTitleSlot(header, options);
        toolbarActionsHost = AddTrailingSlot(header, sheet, options, toolbarItems, useSymmetricColumns);
        AttachHeaderBackButtonHitSlop(header, sheet, backButton);

        return header;
    }

    /// <summary>
    ///     Wraps the shared header with a subtle bottom hairline. The header and the body share the
    ///     sheet background, so a vertically-centered title reads as "too high" against the perceived
    ///     header+body region — the eye can't see where the header band ends. A theme-aware bottom
    ///     border (mirroring the footer's TOP border: <c>OutlineVariant</c> @ 0.35α, hairline
    ///     thickness) delineates the band so the centering is apparent, without a heavy divider look.
    ///     Only reached for <c>ShowToolbar</c> sheets (headerless / diagnostics-chrome sheets return
    ///     earlier and never get this).
    /// </summary>
    private static Grid WrapHeaderWithBottomDivider(Grid header, G9BottomSheetOptions options)
    {
        var container = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            },
            BackgroundColor = options.BackgroundColor ?? G9Palette.Current.Background,
            FlowDirection = ResolveCurrentFlowDirection()
        };

        container.Children.Add(header);
        Grid.SetRow(header, 0);

        var divider = new BoxView
        {
            HeightRequest = G9LayoutMetrics.G9BottomSheetFooterTopBorderThickness,
            Color = G9Palette.Current.OutlineVariant.WithAlpha(0.35f),
            HorizontalOptions = LayoutOptions.Fill,
            InputTransparent = true
        };
        container.Children.Add(divider);
        Grid.SetRow(divider, 1);

        return container;
    }

    private static Grid BuildSingleSlotHeaderHost(
        View slotView,
        Color backgroundColor,
        Thickness padding,
        FlowDirection flowDirection)
    {
        var host = new Grid
        {
            // Same taller, centered header band as the slotted header (see BuildSheetHeader):
            // a Star row + SheetHeaderMinHeight floor so the custom header view centres vertically.
            RowDefinitions = { new RowDefinition { Height = GridLength.Star } },
            Padding = padding,
            MinimumHeightRequest = G9LayoutMetrics.SheetHeaderMinHeight,
            FlowDirection = flowDirection,
            BackgroundColor = backgroundColor
        };
        PrepareHeaderSlotView(slotView);
        host.Children.Add(slotView);
        return host;
    }

    /// <returns>
    ///     The STANDARD back/close button when one was rendered (so the caller can widen its hit
    ///     region — see <see cref="AttachHeaderBackButtonHitSlop" />), otherwise <c>null</c>: a
    ///     caller-supplied <c>HeaderLeadingView</c> owns its own gesture and hit area.
    /// </returns>
    private static View? AddLeadingSlot(
        Grid header,
        CustomizedSfG9BottomSheet sheet,
        G9BottomSheetOptions options,
        FlowDirection flowDirection,
        bool symmetricColumns)
    {
        if (options.HeaderLeadingView is { } customLeading)
        {
            PrepareHeaderSlotView(customLeading, alignToStart: true);
            header.Children.Add(customLeading);
            Grid.SetColumn(customLeading, 0);
            return null;
        }

        if (!options.ShowCloseButton)
        {
            return null;
        }

        var closeButton = CreateHeaderBackButton(sheet, flowDirection, options.UseCloseIcon);
        // In symmetric mode the leading column is Star, so pin the back button to Start to
        // keep it visually flush with the page edge instead of stretching across the column.
        if (symmetricColumns)
        {
            closeButton.HorizontalOptions = LayoutOptions.Start;
        }
        header.Children.Add(closeButton);
        Grid.SetColumn(closeButton, 0);
        return closeButton;
    }

    /// <summary>
    ///     Gives the header back/close button a VIRTUAL hit area — a bigger tappable square that
    ///     costs NOTHING in layout.
    ///     <para>
    ///         The button paints (and measures) <c>ModalHeaderIconButtonSize</c> (30) around a
    ///         <c>ToolbarIconSize</c> (24) glyph. 30 dp is under the app's own 44 dp touch floor
    ///         (design guide §9b/§10), which is exactly the "the close button sometimes doesn't
    ///         work" class of defect: a press landing a few dp off the paint falls through to the
    ///         header behind it.
    ///     </para>
    ///     <para>
    ///         The fix must not cost a single dp of layout — the button's slot, the gap to the
    ///         title, and the band height all stay as they are — so it is NOT bought with padding,
    ///         margin, or a bigger <c>Minimum*Request</c> (every one of those would widen the Auto
    ///         column and push the title away; guide §9b explicitly forbids the padding trick).
    ///         Instead the HEADER carries one tap recognizer that hit-tests the tap position against
    ///         the button's bounds INFLATED by <see cref="ResolveHeaderBackButtonHitSlop" />. Nothing
    ///         is added to the visual tree and nothing is measured.
    ///     </para>
    ///     <para>
    ///         <b>Two gesture owners, disjoint regions — deliberately.</b> The button keeps its own
    ///         recognizer so a direct hit behaves EXACTLY as before on every platform, and this
    ///         handler serves only the ring OUTSIDE the button's real bounds. The inside-bounds
    ///         early-return is what makes that safe: on platforms that bubble a handled tap to the
    ///         parent (iOS/WinUI can raise both), handling it here too would fire
    ///         <see cref="HandleBackRequest" /> twice and close two stacked sheets on one press.
    ///     </para>
    /// </summary>
    private static void AttachHeaderBackButtonHitSlop(Grid header, CustomizedSfG9BottomSheet sheet, View? backButton)
    {
        if (backButton is null)
        {
            return;
        }

        var slop = ResolveHeaderBackButtonHitSlop();
        if (slop <= 0)
        {
            return;
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, e) =>
        {
            // Relative to the BUTTON, so the test is flow-direction agnostic: in RTL the button
            // arranges on the trailing edge and the returned point mirrors with it.
            if (e.GetPosition(backButton) is not { } point)
            {
                return;
            }

            var width = backButton.Width;
            var height = backButton.Height;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            // Inside the real bounds -> the button's own recognizer owns this press.
            if (point.X >= 0 && point.X <= width && point.Y >= 0 && point.Y <= height)
            {
                return;
            }

            if (point.X < -slop || point.X > width + slop ||
                point.Y < -slop || point.Y > height + slop)
            {
                return;
            }

            HandleBackRequest(sheet, G9BottomSheetBackRequestSource.ToolbarButton);
        };

        header.GestureRecognizers.Add(tap);
    }

    /// <summary>
    ///     How far past its painted bounds the header back/close button is tappable, per side.
    ///     <para>
    ///         The target is the LARGEST square the header can host: as tall as the band's item row
    ///         (<c>SheetHeaderMinHeight</c> − 2 × <c>SheetHeaderVerticalGap</c> = 64 − 20 = 44 dp)
    ///         and equally wide, giving 7 dp of slop per side around the 30 dp button — a 44 × 44
    ///         region, 2.15× the old touch area and exactly the §10 accessibility floor.
    ///     </para>
    ///     <para>
    ///         Sized from the band rather than picked: 44 is the tallest square that still sits
    ///         inside the header, and 7 dp per side keeps the leading edge clear of the sheet's own
    ///         edge padding (<c>EdgeSpacing</c> = 10) while reaching only ~4 dp into a
    ///         <c>NearBack</c> title's leading edge — a region the title label never consumes.
    ///         Derived live from the tokens, so a metric edit (or the Developer Tools editor) keeps
    ///         the hit region in step with the band.
    ///     </para>
    /// </summary>
    private static double ResolveHeaderBackButtonHitSlop()
    {
        var hitTargetSize = G9LayoutMetrics.SheetHeaderMinHeight
                            - (G9LayoutMetrics.SheetHeaderVerticalGap * 2);

        return Math.Max(0, (hitTargetSize - G9LayoutMetrics.ModalHeaderIconButtonSize) / 2);
    }

    private static void AddTitleSlot(Grid header, G9BottomSheetOptions options)
    {
        if (options.HeaderTitleView is { } customTitle)
        {
            PrepareHeaderSlotView(
                customTitle,
                alignToStart: options.HeaderTitlePlacement == G9BottomSheetHeaderTitlePlacement.NearBack,
                alignToCenter: options.HeaderTitlePlacement == G9BottomSheetHeaderTitlePlacement.Center);
            header.Children.Add(customTitle);
            Grid.SetColumn(customTitle, 1);
            return;
        }

        if (string.IsNullOrWhiteSpace(options.Title))
        {
            return;
        }

        var titleLabel = new Label
        {
            Text = options.Title,
            TextColor = G9Palette.Current.Secondary,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = options.HeaderTitlePlacement == G9BottomSheetHeaderTitlePlacement.Center
                ? LayoutOptions.Center
                : LayoutOptions.Start,
            HorizontalTextAlignment = options.HeaderTitlePlacement == G9BottomSheetHeaderTitlePlacement.Center
                ? TextAlignment.Center
                : TextAlignment.Start,
            FontSize = G9LayoutMetrics.SheetHeaderTitleFontSize,
            FontAttributes = FontAttributes.Bold,
            MaxLines = 1,
            LineBreakMode = LineBreakMode.TailTruncation
        };

        header.Children.Add(titleLabel);
        Grid.SetColumn(titleLabel, 1);
    }

    private static HorizontalStackLayout? AddTrailingSlot(
        Grid header,
        CustomizedSfG9BottomSheet sheet,
        G9BottomSheetOptions options,
        IReadOnlyList<ToolbarItem>? toolbarItems,
        bool symmetricColumns)
    {
        // The unused 'sheet' parameter is kept on the signature so future trailing-slot
        // helpers (e.g. busy-state badge that toggles based on sheet state) have direct
        // access without changing call sites.
        _ = sheet;

        if (options.HeaderTrailingView is { } customTrailing)
        {
            PrepareHeaderSlotView(customTrailing, alignToEnd: true);
            header.Children.Add(customTrailing);
            Grid.SetColumn(customTrailing, 2);
            return null;
        }

        var trailing = new HorizontalStackLayout
        {
            Spacing = G9LayoutMetrics.EdgeSpacing,
            // In symmetric mode the trailing column is Star, so right-anchor the stack so an
            // empty trailing slot still reserves its symmetric width while the icons (when
            // present) stay flush with the page edge.
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Center,
            Margin = Thickness.Zero
        };

        if (toolbarItems is { Count: > 0 })
        {
            AddToolbarItems(trailing, toolbarItems);
        }
        else if (symmetricColumns)
        {
            // Star column already reserves width; ensure the empty stack itself reports a
            // matching minimum so layout passes that hand-off to the platform measure step
            // don't collapse the column on some platforms (mainly Windows where Star can fall
            // back to Auto when every direct child reports zero desired size).
            trailing.MinimumWidthRequest = G9LayoutMetrics.ModalHeaderIconButtonSize;
        }

        header.Children.Add(trailing);
        Grid.SetColumn(trailing, 2);
        return trailing;
    }

    /// <summary>
    ///     The leading back/close button. Its painted + MEASURED box stays
    ///     <c>ModalHeaderIconButtonSize</c> (30) — do not grow it to make it easier to press; the
    ///     column is <c>Auto</c>, so any extra size here pushes the title away. The bigger tap
    ///     region is virtual and lives in <see cref="AttachHeaderBackButtonHitSlop" />.
    /// </summary>
    private static Border CreateHeaderBackButton(
        CustomizedSfG9BottomSheet sheet,
        FlowDirection flowDirection,
        bool useCloseIcon = false)
    {
        // A close (×) glyph for a top-level, dismissable sheet; otherwise the RTL-aware back arrow
        // for a stacked sheet the user navigates back from.
        //
        // A SHAFTED arrow, not a chevron. "Go back" and "drill down" are different affordances and
        // the header is the one place the distinction is load-bearing — a lone chevron here reads as
        // an expander. The extraction briefly used ChevronBack/Forward, which is what nav-card rows
        // use, and the header stopped looking like a back button.
        var glyph = useCloseIcon
            ? G9Glyphs.Clear
            : flowDirection == FlowDirection.RightToLeft
                ? G9Glyphs.ArrowForward
                : G9Glyphs.ArrowBack;

        var closeButton = new Border
        {
            StrokeThickness = 0,
            MinimumHeightRequest = G9LayoutMetrics.ModalHeaderIconButtonSize,
            MinimumWidthRequest = G9LayoutMetrics.ModalHeaderIconButtonSize,
            // Center the button on the header's centre line (the header band is taller than the
            // button — SheetHeaderMinHeight); without this it would stretch (Grid default Fill).
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = Colors.Transparent,
            Content = new G9IconView {
                Icon = glyph,
                Color = G9Palette.Current.Secondary,
                Size = G9LayoutMetrics.ToolbarIconSize,
                InputTransparent = true,
                VerticalOptions = LayoutOptions.Center,
                HorizontalOptions = LayoutOptions.Start
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => HandleBackRequest(sheet, G9BottomSheetBackRequestSource.ToolbarButton);
        closeButton.GestureRecognizers.Add(tap);
        return closeButton;
    }

    // Header slots that are filled by programmer-supplied views may arrive with explicit
    // HorizontalOptions/VerticalOptions; only override them when the caller asked us to
    // bias the slot toward a specific edge.
    private static void PrepareHeaderSlotView(
        View slotView,
        bool alignToStart = false,
        bool alignToEnd = false,
        bool alignToCenter = false)
    {
        slotView.VerticalOptions = LayoutOptions.Center;
        if (alignToStart)
        {
            slotView.HorizontalOptions = LayoutOptions.Start;
        }
        else if (alignToEnd)
        {
            slotView.HorizontalOptions = LayoutOptions.End;
        }
        else if (alignToCenter)
        {
            slotView.HorizontalOptions = LayoutOptions.Center;
        }
    }

    // ---------------------------
    // Shared footer template
    // ---------------------------
    // Default: when FooterButtons is set, render rows of equal-width buttons (max
    // FooterMaxButtonsPerRow buttons per row). Rows wrap when more buttons are supplied;
    // every row keeps the same column widths so each row of equal-width buttons keeps its
    // visual rhythm. FooterView replaces this entire block with a user-owned view.
    private static View? BuildSheetFooter(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        // sheet kept on the signature so future variations (footer drag/swipe, busy indicators,
        // command bindings that need to close the sheet) have first-class access to it without
        // touching call sites.
        _ = sheet;

        if (options.FooterView is { } footerView)
        {
            return WrapFooterRoot(footerView, options);
        }

        if (options.FooterButtons is { Count: > 0 } buttons)
        {
            return WrapFooterRoot(BuildFooterButtonGrid(buttons, options), options);
        }

        return null;
    }

    private static View WrapFooterRoot(View footerContent, G9BottomSheetOptions options)
    {
        var footerRoot = new Grid
        {
            BackgroundColor = options.BackgroundColor ?? G9Palette.Current.Background,
            HorizontalOptions = LayoutOptions.Fill,
            FlowDirection = ResolveCurrentFlowDirection(),
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Auto }
            }
        };

        var divider = new BoxView
        {
            HeightRequest = G9LayoutMetrics.G9BottomSheetFooterTopBorderThickness,
            Color = G9Palette.Current.OutlineVariant.WithAlpha(0.35f),
            HorizontalOptions = LayoutOptions.Fill
        };
        footerRoot.Children.Add(divider);
        Grid.SetRow(divider, 0);

        var contentHost = new Grid
        {
            Padding = G9LayoutMetrics.G9BottomSheetFooterPadding,
            HorizontalOptions = LayoutOptions.Fill,
            FlowDirection = ResolveCurrentFlowDirection()
        };
        contentHost.Children.Add(footerContent);
        footerRoot.Children.Add(contentHost);
        Grid.SetRow(contentHost, 1);

        return footerRoot;
    }

    private static Grid BuildFooterButtonGrid(IReadOnlyList<View> buttons, G9BottomSheetOptions options)
    {
        var maxPerRow = Math.Max(1, options.FooterMaxButtonsPerRow);
        var columnsPerRow = Math.Min(maxPerRow, buttons.Count);
        var rowCount = (int)Math.Ceiling(buttons.Count / (double)columnsPerRow);

        var grid = new Grid
        {
            ColumnSpacing = G9LayoutMetrics.G9BottomSheetFooterButtonSpacing,
            RowSpacing = G9LayoutMetrics.G9BottomSheetFooterButtonSpacing,
            HorizontalOptions = LayoutOptions.Fill,
            FlowDirection = ResolveCurrentFlowDirection()
        };

        for (var c = 0; c < columnsPerRow; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        }

        for (var r = 0; r < rowCount; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var index = 0; index < buttons.Count; index++)
        {
            var button = buttons[index];
            button.HorizontalOptions = LayoutOptions.Fill;
            if (button.MinimumHeightRequest <= 0)
            {
                button.MinimumHeightRequest = G9LayoutMetrics.G9BottomSheetFooterButtonHeight;
            }

            grid.Children.Add(button);
            Grid.SetColumn(button, index % columnsPerRow);
            Grid.SetRow(button, index / columnsPerRow);
        }

        return grid;
    }

    private static void AddToolbarItems(HorizontalStackLayout toolbarActionsHost, IEnumerable<ToolbarItem> items)
    {
        foreach (var item in items)
        {
            toolbarActionsHost.Children.Add(CreateToolbarActionButton(item));
        }
    }

    private static View CreateToolbarActionButton(ToolbarItem item)
    {
        var button = new G9HeaderActionButton { VerticalOptions = LayoutOptions.Center };

        // Concurrency guard key, UNIQUE PER BUTTON. G9SafeCommand defaults
        // PreventConcurrentExecution to true and, with no explicit key, derives one from the caller
        // file + member — which is this method, i.e. the SAME key for every toolbar button in the
        // app. A sheet opened FROM a toolbar action keeps that action awaiting for as long as it is
        // open (the AsyncAction awaits the close), so the shared key was still held and every
        // toolbar button on the STACKED sheet was silently skipped: the site selector's sort/filter
        // sheets are opened that way, and their header reset buttons did nothing, while the same
        // sheets opened from the Tasks page (a plain header control, no toolbar action outstanding)
        // worked. Per-button keys keep the real protection — a double-tap on the SAME button is
        // still guarded — without one header action muting another.
        var guardKey = $"{nameof(G9BottomSheetHelper)}.Toolbar.{Guid.NewGuid():N}";

        void ApplyVisualState()
        {
            var sheetItem = item as G9BottomSheetToolbarItem;
            var isBusy = sheetItem is { ShowBusyIndicator: true, IsBusy: true };
            var isDisabledByBusy = sheetItem is { DisableWhileBusy: true } && isBusy;
            var canInteract = item.IsEnabled && !isDisabledByBusy;

            button.IsBusy = isBusy;
            button.IsActive = sheetItem?.IsActive == true;
            button.ShowActiveBadge = sheetItem?.ShowActiveBadge == true;
            button.AnimatePress = sheetItem?.AnimatePress ?? true;
            button.IsEnabled = canInteract;

            // Custom per-action icon size (0 = use the shared toolbar icon size default).
            if (sheetItem is { IconSize: > 0 } sizedItem)
            {
                button.IconSize = sizedItem.IconSize;
            }

            if (sheetItem?.Icon is { } materialIcon)
            {
                button.Icon = materialIcon;
                button.IconImageSource = null;
                button.ButtonText = string.Empty;
                return;
            }

            if (item.IconImageSource is not null)
            {
                button.IconImageSource = item.IconImageSource;
                button.ButtonText = string.Empty;
                return;
            }

            button.IconImageSource = null;
            button.ButtonText = item.Text ?? string.Empty;
        }

        PropertyChangedEventHandler propertyChanged = (_, _) =>
        {
            MainThread.BeginInvokeOnMainThread(ApplyVisualState);
        };

        item.PropertyChanged += propertyChanged;

        EventHandler? unloaded = null;
        unloaded = (_, _) =>
        {
            item.PropertyChanged -= propertyChanged;
            button.Unloaded -= unloaded;
        };
        button.Unloaded += unloaded;

        ApplyVisualState();

        button.Command = new AsyncRelayCommand(
            () => G9SafeCommand.RunAsync(
                () => ExecuteToolbarActionAsync(button, item),
                new G9SafeCommandOptions
                {
                    Source = nameof(G9BottomSheetHelper),
                    ShowErrorG9Popup = true,
                    EnableThrottle = false,
                    ThrottleKey = guardKey
                }));

        return button;
    }

    private static async Task ExecuteToolbarActionAsync(G9HeaderActionButton button, ToolbarItem item)
    {
        var sheetItem = item as G9BottomSheetToolbarItem;
        var isBusy = sheetItem is { ShowBusyIndicator: true, IsBusy: true };
        if (!item.IsEnabled || isBusy)
        {
            return;
        }

        var shouldSetBusy = sheetItem?.ShowBusyIndicator == true;
        if (shouldSetBusy)
        {
            sheetItem!.IsBusy = true;
            await MainThread.InvokeOnMainThreadAsync(() => button.IsBusy = true).ConfigureAwait(true);
        }

        try
        {
            if (sheetItem?.AsyncAction is not null)
            {
                await sheetItem.AsyncAction().ConfigureAwait(true);
                return;
            }

            if (item.Command?.CanExecute(item.CommandParameter) == true)
            {
                item.Command.Execute(item.CommandParameter);
            }
        }
        finally
        {
            if (shouldSetBusy)
            {
                sheetItem!.IsBusy = false;
                await MainThread.InvokeOnMainThreadAsync(() => button.IsBusy = false).ConfigureAwait(true);
            }
        }
    }

    private static void DetachFromParent(View content)
    {
        switch (content.Parent)
        {
            case Layout layout:
                layout.Remove(content);
                break;
            case ContentView contentView when ReferenceEquals(contentView.Content, content):
                contentView.Content = null;
                break;
            case Border border when ReferenceEquals(border.Content, content):
                border.Content = null;
                break;
            case ScrollView scrollView when ReferenceEquals(scrollView.Content, content):
                scrollView.Content = null;
                break;
            case ContentPage page when ReferenceEquals(page.Content, content):
                page.Content = null;
                break;
        }
    }

    private static PrimarySheetHostState GetPrimarySheetState(Grid overlayHost)
    {
        lock (StackLock)
        {
            return PrimarySheetStates.GetValue(overlayHost, static _ => new PrimarySheetHostState());
        }
    }

    private static bool IsSheetAlive(CustomizedSfG9BottomSheet? sheet)
    {
        if (sheet is null)
        {
            return false;
        }

        return sheet.Handler is not null || sheet.Parent is not null || sheet.IsOpen;
    }

    private static void CleanupSheetVisuals(CustomizedSfG9BottomSheet sheet, Grid? parentGrid)
    {
        CleanupSheetVisualsNow(sheet, parentGrid);
    }

    private static void CleanupSheetVisualsNow(CustomizedSfG9BottomSheet sheet, Grid? parentGrid)
    {
        // counters summarise how much size/position churn it went through.

        DetachPositionTracking(sheet);
        DetachFitToContentSizeTracking(sheet);
        DetachModalOverlay(sheet, parentGrid);

        if (parentGrid is not null && ReferenceEquals(sheet.Parent, parentGrid))
        {
            parentGrid.Children.Remove(sheet);
        }

        CancelDeferredLoad(sheet);
        SheetBehaviorStates.Remove(sheet);
        SheetOverlayHosts.Remove(sheet);
        sheet.G9BottomSheetContent = CreateTransparentSheetContent();
        sheet.Content = CreateTransparentSheetContent();
        TryDisconnectHandler(sheet);
    }

    private static void TryDisconnectHandler(CustomizedSfG9BottomSheet sheet)
    {
#if ANDROID
        return;
#else
        try
        {
            sheet.Handler?.DisconnectHandler();
        }
        catch (ObjectDisposedException)
        {
        }
#endif
    }

    #endregion

    #region Apply Options

    /// <summary>Applies <see cref="G9BottomSheetOptions" /> to a Syncfusion <see cref="CustomizedSfG9BottomSheet" />.</summary>
    public static void ApplyOptions(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        ArgumentNullException.ThrowIfNull(options);

        // We always run with Syncfusion's overlay disabled and provide our own modal overlay
        // (see AttachModalOverlay). Syncfusion's overlay caps Opacity at 0.5 and is removed
        // entirely at Collapsed/Hidden, neither of which matches the app's modal contract.
        sheet.IsModal = false;
        sheet.EnableSwiping = ShouldEnableNativeStateSwiping(options);
        sheet.ShowGrabber = ShouldShowSheetGrabber(options);
        sheet.ContentPadding = new Thickness(options.Padding);
        sheet.CornerRadius = new CornerRadius(options.CornerRadius, options.CornerRadius, 0, 0);
        sheet.ContentWidthMode = options.ContentWidthMode;
        // The static AnimationDuration is a fallback for any motion that runs before the
        // provider below has been consulted (e.g. early property-changed handlers). We seed
        // it with the resolved Open duration so a first-show with no provider invocation
        // still feels right. The AnimationDurationProvider below is what actually drives
        // every motion: it picks Open vs Close from the direction of travel and optionally
        // size-scales by the fraction of screen height traversed.
        sheet.AnimationDuration = Math.Max(0, ResolveOpenAnimationDurationMs(options));
        sheet.AnimationDurationProvider = (current, target, height) =>
        {
            var duration = ResolveSheetMotionDurationMs(options, current, target, height);

            // Falling motions (closes) speed up when a queued replacement is waiting behind this
            // sheet (see MarkQuickCloseForQueuedReplace) so chained close→open transitions lose
            // half their dead time. Rising motions always keep the configured feel.
            if (target >= current &&
                SheetBehaviorStates.TryGetValue(sheet, out var behaviorState) &&
                behaviorState is not null &&
                behaviorState.CloseDurationScale < 1)
            {
                duration = (int)Math.Round(duration * behaviorState.CloseDurationScale);
            }

            return duration;
        };
        sheet.CollapseOnOverlayTap = false;

        if (options.BackgroundColor is not null)
        {
            sheet.Background = new SolidColorBrush(options.BackgroundColor);
        }

        if (options.G9BottomSheetContentWidth is > 0)
        {
            sheet.ContentWidthMode = SfG9BottomSheetContentWidthMode.Custom;
            sheet.G9BottomSheetContentWidth = options.G9BottomSheetContentWidth.Value;
        }

        if (options.WindowsMaxWidth is > 0)
        {
            sheet.ContentWidthMode = SfG9BottomSheetContentWidthMode.Custom;
            sheet.G9BottomSheetContentWidth = options.WindowsMaxWidth.Value;
        }

        if (options.AndroidHalfExpandedRatio is { } ratio)
        {
            sheet.HalfExpandedRatio = Math.Clamp(ratio, 0.1, 0.9);
        }

        // Drag-to-close, at the control level. Left at the control's default until 2026-09, which
        // meant a sheet that opted OUT of cancelling could still raise a close request from a body
        // drag — harmless only because every such sheet in practice also sets IsDraggable = false.
        // The threshold is passed through for the same reason: the two belong together, and the
        // guide has always described both as helper-applied.
        sheet.IsCancelable = options.IsCancelable;
        sheet.DragCloseThreshold = DragCloseThreshold;

        // Detent vocabulary the control cannot infer from AllowedState alone (which only says
        // WHICH large detent exists, never whether a peek step sits under it). Set for every sizing
        // mode: a fit-to-content sheet has exactly one detent, and saying so is what lets a downward
        // drag on it read as a dismissal rather than as a step to a smaller state that isn't there.
        sheet.AllowCollapsedState = HasPeekDetent(options);
        sheet.ScrollingExpandsSheet = options.ScrollingExpandsSheet;

        if (options.SizeMode == G9BottomSheetSizeMode.FitToContent)
        {
            sheet.AllowedState = SfG9BottomSheetAllowedState.All;
            ApplyModalOverlayBackground(sheet, options);
            return;
        }

        sheet.FullExpandedRatio = Math.Clamp(options.FullExpandedRatio ?? 1, 0.1, 1);
        sheet.HalfExpandedRatio = Math.Clamp(options.HalfExpandedRatio ?? 0.5, 0.1, 0.9);

        if (options.PeekHeight is > 0)
        {
            sheet.CollapsedHeight = options.PeekHeight.Value;
        }

        if (options.CollapsedHeight is > 0)
        {
            sheet.CollapsedHeight = options.CollapsedHeight.Value;
        }

        sheet.AllowedState = MapAllowedState(options.States);
        sheet.State = MapState(options.CurrentState);
        ApplyModalOverlayBackground(sheet, options);
    }

    private static void AttachModalOverlay(CustomizedSfG9BottomSheet sheet, Grid overlayHost, G9BottomSheetOptions options)
    {
        if (!options.IsModal)
        {
            return;
        }

        if (ModalOverlays.TryGetValue(sheet, out var existing) && existing is not null)
        {
            if (!ReferenceEquals(existing.Parent, overlayHost))
            {
                if (existing.Parent is Layout previousParent)
                {
                    previousParent.Children.Remove(existing);
                }

                overlayHost.Children.Add(existing);
            }

            return;
        }

        var overlay = new BoxView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Color = Colors.Transparent,
            InputTransparent = false
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => OnModalOverlayTapped(sheet);
        overlay.GestureRecognizers.Add(tap);

        ModalOverlays.AddOrUpdate(sheet, overlay);
        overlayHost.Children.Add(overlay);

        // Apply the initial color now that the overlay exists; earlier calls during
        // sheet/content configuration were no-ops because the overlay wasn't created yet.
        overlay.Color = ResolveModalOverlayColor(sheet, options, ratioOverride: null);
    }

    private static void DetachModalOverlay(CustomizedSfG9BottomSheet sheet, Grid? overlayHost)
    {
        if (!ModalOverlays.TryGetValue(sheet, out var overlay) || overlay is null)
        {
            return;
        }

        sheet.AbortAnimation(ModalOverlayAnimationName);

        if (overlayHost is not null && ReferenceEquals(overlay.Parent, overlayHost))
        {
            overlayHost.Children.Remove(overlay);
        }
        else if (overlay.Parent is Layout parent)
        {
            parent.Children.Remove(overlay);
        }

        ModalOverlays.Remove(sheet);
    }

    private static void OnModalOverlayTapped(CustomizedSfG9BottomSheet sheet)
    {

        if (!IsSheetAlive(sheet))
        {
            return;
        }

        var behavior = SheetBehaviorStates.GetValue(
            sheet,
            static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));

        if (!behavior.Options.IsCancelable && behavior.Options.OnBackRequested is null)
        {
            return;
        }

        HandleBackRequest(sheet, G9BottomSheetBackRequestSource.OverlayTap);
    }

    private static void ApplyDragCloseOverlay(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options, double totalY)
    {
        if (!options.IsModal)
        {
            return;
        }

        if (!ModalOverlays.TryGetValue(sheet, out var overlay) || overlay is null)
        {
            return;
        }

        var fullHeight = ResolveFullScreenHeight();
        if (fullHeight <= 0)
        {
            return;
        }

        var maxOpacity = options.WindowBackgroundColor?.Alpha ?? Settings.ModalOverlayMaximumOpacity;
        var dragProgress = Math.Clamp(totalY / fullHeight, 0, 1);
        var alpha = (float)Math.Max(0, maxOpacity * (1 - dragProgress));

        var baseColor = options.WindowBackgroundColor ?? Settings.ModalOverlayColor;
        sheet.AbortAnimation(ModalOverlayAnimationName);
        overlay.Color = baseColor.WithAlpha(alpha);
    }

    // Subscribes to the G9SheetView control's PositionChanged event so the modal overlay
    // alpha and the backdrop card-recede transform both track the live drag/snap animation
    // position. Also subscribes to BackRequested so drag-to-close gestures from the body
    // route through the helper's HandleBackRequest path (and therefore through any caller's
    // OnBackRequested callback / IsCancelable rules).
    private static void AttachPositionTracking(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        var wantsBackdropCard =
            Settings.EnableBackdropCardEffect &&
            options.EnableBackdropCardEffect &&
            options.SizeMode != G9BottomSheetSizeMode.FitToContent;

        var wantsModalOverlayTracking =
            options.IsModal &&
            options.IsDraggable &&
            options.SizeMode != G9BottomSheetSizeMode.FitToContent;

        if (wantsBackdropCard)
        {
            AttachBackdropCardBinding(sheet, options);
        }

        // BackRequested wiring is independent of position tracking — even fit-to-content
        // sheets that don't want backdrop card / modal-overlay updates still need drag-to-close
        // routed through HandleBackRequest. We register the listener once per sheet and let
        // the helper's IsCancelable / OnBackRequested rules decide what happens.
        AttachBackRequestedRouting(sheet, options);

        if (!wantsBackdropCard && !wantsModalOverlayTracking)
        {
            return;
        }

        if (PositionListeners.TryGetValue(sheet, out _))
        {
            return;
        }


        EventHandler<SfPositionChangedEventArgs> handler = (s, e) =>
        {
            if (s is not CustomizedSfG9BottomSheet target)
            {
                return;
            }

            UpdateOverlayFromPositionEvent(target, e, options);
        };

        sheet.PositionChanged += handler;
        PositionListeners.AddOrUpdate(sheet, handler);
    }

    private static void DetachPositionTracking(CustomizedSfG9BottomSheet sheet)
    {
        DetachBackdropCardBinding(sheet);
        DetachBackRequestedRouting(sheet);

        if (!PositionListeners.TryGetValue(sheet, out var handler) || handler is null)
        {
            return;
        }

        sheet.PositionChanged -= handler;
        PositionListeners.Remove(sheet);
    }

    // ---------------------------
    // BackRequested routing
    // ---------------------------
    // The G9SheetView control raises BackRequested when the user drags the body down past
    // the close threshold and releases (DragToClose) or taps the in-control overlay (OverlayTap,
    // rare — the helper renders its own page-level overlay sibling and uses IsModal = false on
    // every sheet, so this code path is mostly defensive). We translate the reason to a
    // G9BottomSheetBackRequestSource and route through HandleBackRequest, which honors the
    // caller's OnBackRequested callback / IsCancelable rules.
    private static void AttachBackRequestedRouting(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        if (!options.IsDraggable)
        {
            // No drag-to-close → no need to listen. The toolbar back button already fires
            // G9BottomSheetBackRequestSource.ToolbarButton through its own handler.
            return;
        }

        if (BackRequestedListeners.TryGetValue(sheet, out _))
        {
            return;
        }

        EventHandler<G9SheetViewBackRequestedEventArgs> handler = (s, e) =>
        {
            if (s is not CustomizedSfG9BottomSheet target || !IsSheetAlive(target))
            {
                return;
            }

            var source = e.Reason switch
            {
                G9SheetViewBackRequestReason.OverlayTap => G9BottomSheetBackRequestSource.OverlayTap,
                _ => G9BottomSheetBackRequestSource.ToolbarButton
            };

            HandleBackRequest(target, source);
        };

        sheet.BackRequested += handler;
        BackRequestedListeners.AddOrUpdate(sheet, handler);
    }

    private static void DetachBackRequestedRouting(CustomizedSfG9BottomSheet sheet)
    {
        if (!BackRequestedListeners.TryGetValue(sheet, out var handler) || handler is null)
        {
            return;
        }

        sheet.BackRequested -= handler;
        BackRequestedListeners.Remove(sheet);
    }

    private static void UpdateOverlayFromPositionEvent(
        CustomizedSfG9BottomSheet sheet,
        SfPositionChangedEventArgs e,
        G9BottomSheetOptions options)
    {
        if (!IsSheetAlive(sheet))
        {
            return;
        }

        var fullHeight = e.FullHeight > 0 ? e.FullHeight : ResolveFullScreenHeight();
        if (fullHeight <= 0)
        {
            return;
        }

        var visibleRatio = Math.Clamp(e.VisibleHeight / fullHeight, 0, 1);

        // Backdrop card transform tracks the live position through the entire lifecycle. The
        // G9SheetView's Close() sets State = Hidden synchronously before running its close
        // animation, so position events during the close tween arrive with state == Hidden.
        // We deliberately keep updating the ContentHost transform on those ticks so the page
        // recede animates back to identity in lockstep with the sheet's CubicOut close —
        // without this the transform would stay "stuck" at the last value and snap to identity
        // only when the cleanup delay (~AnimationDurationMs) expires.
        UpdateBackdropCardEffect(sheet, visibleRatio);

        // State-aware top padding follows the live drag too so a Medium → Large drag reveals
        // the safe-area gap progressively instead of snapping at the end. The previous
        // helper-side PanGestureRecognizer drove this from `ApplyStateAwareTopPaddingForRatio`;
        // routing it through PositionChanged keeps the same behavior on every platform now
        // that the recogniser is gone.
        ApplyStateAwareTopPaddingForRatio(sheet, options, visibleRatio);

        if (sheet.State == SfG9BottomSheetState.Hidden)
        {
            // Modal overlay alpha is driven by OnPrimarySheetStateChanged once the sheet hides
            // (animated fade to transparent). Bail here so the position-driven update doesn't
            // race against the state-aware animated fade.
            return;
        }

        UpdateModalOverlayBackground(sheet, options, ratioOverride: visibleRatio, animated: false);
    }

    // ---------------------------
    // Backdrop "card recede" effect
    // ---------------------------
    // The helper applies Scale + TranslationY to the host page's ContentHost when the sheet
    // visible-height ratio crosses BackdropCardEffectThreshold (default 0.75). Both transforms
    // map directly to native compositor matrices (Android RenderNode, iOS CALayer, WinUI
    // CompositionTransform) so they cost nothing to animate during a drag — there is no
    // re-measure / re-layout on the page content. Spec mirrors iOS native modal sheet behavior
    // where the underlying page appears to slide back into the screen as the sheet expands.

    private static void AttachBackdropCardBinding(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        // App-wide kill switch wins: when G9BottomSheetSettings.EnableBackdropCardEffect is
        // false the per-sheet option cannot opt back in. Per-sheet false still disables the
        // effect locally while leaving the global default alone for other sheets.
        if (!Settings.EnableBackdropCardEffect ||
            !options.EnableBackdropCardEffect ||
            options.SizeMode == G9BottomSheetSizeMode.FitToContent)
        {
            return;
        }

        if (!G9ModalHostRegistry.TryGetCurrentHost(out var host))
        {
            return;
        }

        // Only the primary sheet drives the page recede so a stacked sheet doesn't double-up
        // the transform on the same ContentHost. The primary sheet is registered before
        // OpenSheet runs (see OpenPrimarySheet), so the lookup is reliable here.
        var primary = GetPrimarySheet(host.OverlayHost);
        if (primary is null || !ReferenceEquals(primary, sheet))
        {
            return;
        }

        var contentHost = host.Page.ContentHost;
        if (contentHost is null)
        {
            return;
        }

        if (BackdropCardBindings.TryGetValue(sheet, out var existing) && existing is not null)
        {
            existing.UpdateOptions(options);
            return;
        }

        BackdropCardBindings.AddOrUpdate(sheet, new BackdropCardBinding(contentHost, options));
    }

    private static void DetachBackdropCardBinding(CustomizedSfG9BottomSheet sheet)
    {
        if (!BackdropCardBindings.TryGetValue(sheet, out var binding) || binding is null)
        {
            return;
        }

        binding.ResetTransform();
        BackdropCardBindings.Remove(sheet);
    }

    private static void ResetBackdropCardTransformForHost(Grid? overlayHost)
    {
        if (overlayHost is null || !G9ModalHostRegistry.TryGetCurrentHost(out var host))
        {
            return;
        }

        if (!ReferenceEquals(host.OverlayHost, overlayHost))
        {
            return;
        }

        ResetBackdropCardTransform(host.Page.ContentHost);
    }

    private static void ResetBackdropCardTransform(Grid? contentHost)
    {
        if (contentHost is null)
        {
            return;
        }

        contentHost.Scale = 1;
        contentHost.TranslationY = 0;

#if ANDROID
        if (contentHost.Handler?.PlatformView is AndroidView nativeView)
        {
            // Two sweeps, not three: the source app added a third that reached into its own
            // MainActivity to null transforms on the MAUI root, as belt-and-braces for its
            // navigation stack. These two already walk the native tree from ContentHost, which is
            // where the recede writes, so the extra activity-level pass had nothing left to clear.
            ResetBackdropCardNativeTree(nativeView);
            ResetBackdropCardNativeContentRoot(nativeView);
        }
#endif
    }

#if ANDROID
    private static int ResetBackdropCardNativeTree(AndroidView root)
    {
        var visited = 0;
        var reset = 0;
        var rootHeight = root.Height;

        ResetBackdropCardNativeTree(
            root,
            depth: 0,
            rootHeight,
            ref visited,
            ref reset);

        if (reset > 0)
        {
            root.RequestLayout();
        }

        return reset;
    }

    private static int ResetBackdropCardNativeContentRoot(AndroidView scopedRoot)
    {
        var contentRoot = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?
            .Window?
            .DecorView?
            .FindViewById(Android.Resource.Id.Content) as AndroidView;

        if (contentRoot is null || ReferenceEquals(contentRoot, scopedRoot))
        {
            return 0;
        }

        return ResetBackdropCardNativeTree(contentRoot);
    }

    private static void ResetBackdropCardNativeTree(
        AndroidView view,
        int depth,
        int rootHeight,
        ref int visited,
        ref int reset)
    {
        if (visited >= 96 || depth > 7)
        {
            return;
        }

        visited++;

        if (TryResetBackdropCardNativeView(view, depth, rootHeight))
        {
            reset++;
        }

        if (view is not AndroidViewGroup group)
        {
            return;
        }

        for (var i = 0; i < group.ChildCount && visited < 96; i++)
        {
            if (group.GetChildAt(i) is { } child)
            {
                ResetBackdropCardNativeTree(
                    child,
                    depth + 1,
                    rootHeight,
                    ref visited,
                    ref reset);
            }
        }
    }

    private static bool TryResetBackdropCardNativeView(
        AndroidView view,
        int depth,
        int rootHeight)
    {
        if (!ShouldResetBackdropCardNativeView(view, depth, rootHeight))
        {
            return false;
        }

        view.ScaleX = 1;
        view.ScaleY = 1;
        view.TranslationX = 0;
        view.TranslationY = 0;
        view.RequestLayout();
        return true;
    }

    private static bool ShouldResetBackdropCardNativeView(AndroidView view, int depth, int rootHeight)
    {
        var hasTransform =
            Math.Abs(view.ScaleX - 1) > 0.0005f ||
            Math.Abs(view.ScaleY - 1) > 0.0005f ||
            Math.Abs(view.TranslationX) > 0.1f ||
            Math.Abs(view.TranslationY) > 0.1f;

        if (!hasTransform)
        {
            return false;
        }

        if (depth == 0)
        {
            return true;
        }

        var className = view.Class?.SimpleName;
        if (className != "LayoutViewGroup" && className != "ContentViewGroup")
        {
            return false;
        }

        return rootHeight <= 0 || view.Height >= rootHeight - 2;
    }
#endif

    private static void UpdateBackdropCardEffect(CustomizedSfG9BottomSheet sheet, double visibleRatio)
    {
        if (!BackdropCardBindings.TryGetValue(sheet, out var binding) || binding is null)
        {
            return;
        }

        // Only the primary sheet drives the page recede — stacked sheets should not stomp on
        // the primary's transform. (Stacked sheets register their own behavior state but never
        // call AttachBackdropCardBinding.)
        binding.ApplyForRatio(visibleRatio);
    }

    private sealed class BackdropCardBinding
    {
        private readonly WeakReference<Grid> _contentHostRef;
        private double _threshold;

        public BackdropCardBinding(Grid contentHost, G9BottomSheetOptions options)
        {
            _contentHostRef = new WeakReference<Grid>(contentHost);
            UpdateOptions(options);
        }

        public void UpdateOptions(G9BottomSheetOptions options)
        {
            _threshold = Math.Clamp(options.BackdropCardEffectThreshold, 0, 0.999);
        }

        public void ApplyForRatio(double visibleRatio)
        {
            if (!_contentHostRef.TryGetTarget(out var contentHost))
            {
                return;
            }

            if (visibleRatio <= _threshold)
            {
                ResetTransform(contentHost);
                return;
            }

            var rawProgress = Math.Clamp((visibleRatio - _threshold) / (1 - _threshold), 0, 1);

            // Compensate for the sheet's CubicOut motion so the recede plays linearly in time
            // during programmatic open/close animations. Without this, the perceived backdrop
            // recede "rushes then stalls" near the end (typical full-screen open: ~50% of the
            // recede happens in the first ~13% of animation time after crossing the threshold,
            // and the remaining ~50% drags through the last ~50% of time). The vendored
            // AnimateG9BottomSheet uses Easing.CubicOut on TranslationY, which means
            // visibleRatio(t) = 1 - (1 - t)^3 in the post-threshold range. Mapping that linearly
            // into our progress is what produces the visible "small stop" near the end of a
            // full-screen open. Inverting CubicOut algebraically gives the exact compensation:
            //
            //     visibleRatio(t) = 1 - (1 - t)^3
            //       rawProgress    = (visibleRatio - threshold) / (1 - threshold)
            //       progress(t)    = 1 - (1 - rawProgress)^(1/3)
            //
            // …which simplifies to a clean linear progression of `progress` against animation
            // time during state-change animations. During interactive drags `visibleRatio` is
            // linear in finger position (Syncfusion ticks visibleHeight per frame with no
            // easing), so this same formula reads as a gentle InCubic feel — the backdrop
            // "commits" as the user approaches full open, mirroring native iOS modal behavior.
            // The recede still tracks the finger smoothly because Math.Pow is called once per
            // frame and is hardware-accelerated; this is purely a perceptual reshape.
            var progress = 1 - Math.Pow(1 - rawProgress, 1.0 / 3.0);

            var targetScale = 1 - ((1 - G9LayoutMetrics.G9BottomSheetBackdropCardMinScale) * progress);
            var targetTranslationY = G9LayoutMetrics.G9BottomSheetBackdropCardTranslationY * progress;

            if (Math.Abs(contentHost.Scale - targetScale) > 0.0005)
            {
                contentHost.Scale = targetScale;
            }
            if (Math.Abs(contentHost.TranslationY - targetTranslationY) > 0.1)
            {
                contentHost.TranslationY = targetTranslationY;
            }
        }

        public void ResetTransform()
        {
            if (_contentHostRef.TryGetTarget(out var contentHost))
            {
                ResetTransform(contentHost);
            }
        }

        private static void ResetTransform(Grid contentHost)
        {
            ResetBackdropCardTransform(contentHost);
        }
    }

    private static void ApplyModalOverlayBackground(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        UpdateModalOverlayBackground(sheet, options, animated: false);
    }

    private static void UpdateModalOverlayBackground(CustomizedSfG9BottomSheet sheet, bool animated)
    {
        var behavior = SheetBehaviorStates.GetValue(
            sheet,
            static _ => new SheetBehaviorState(G9BottomSheetOptions.DefaultOptions()));

        UpdateModalOverlayBackground(sheet, behavior.Options, animated: animated);
    }

    private static void UpdateModalOverlayBackground(
        CustomizedSfG9BottomSheet sheet,
        G9BottomSheetOptions options,
        double? ratioOverride = null,
        bool animated = false)
    {
        if (!ModalOverlays.TryGetValue(sheet, out var overlay) || overlay is null)
        {
            return;
        }

        if (!options.IsModal)
        {
            sheet.AbortAnimation(ModalOverlayAnimationName);
            overlay.Color = Colors.Transparent;
            return;
        }

        var targetColor = ResolveModalOverlayColor(sheet, options, ratioOverride);

        // Once the sheet is closing/closed, fade the overlay fully out. The state-change
        // handler runs animated:true so this runs in lockstep with the sheet's close animation.
        var sheetIsHidingOrClosed = ratioOverride is null &&
            (sheet.State == SfG9BottomSheetState.Hidden || IsSheetClosing(sheet));
        if (sheetIsHidingOrClosed)
        {
            targetColor = targetColor.WithAlpha(0);
        }

        // As soon as the close starts, stop blocking input on the helper-owned modal overlay.
        // Without this flip the BoxView keeps consuming taps for the full close-cleanup window
        // (animation duration + grace + DetachModalOverlay round-trip — typically ~700 ms),
        // which on Android shows up as page content "ignoring" taps for nearly a second after
        // the sheet visually disappears. The visual alpha fade still plays out below; only
        // hit-testing is changed.
        overlay.InputTransparent = sheetIsHidingOrClosed;
        if (!animated)
        {
            sheet.AbortAnimation(ModalOverlayAnimationName);
            overlay.Color = targetColor;
            return;
        }

        var currentColor = overlay.Color ?? Colors.Transparent;
        if (Math.Abs(currentColor.Alpha - targetColor.Alpha) < 0.01 &&
            ColorsEqualIgnoringAlpha(currentColor, targetColor))
        {
            sheet.AbortAnimation(ModalOverlayAnimationName);
            overlay.Color = targetColor;
            return;
        }

        // The modal overlay fade tracks the sheet's direction: fading toward 0 alpha (close)
        // uses the close duration, fading toward the visible alpha (open / expand) uses the
        // open duration. We pick from sheet.State rather than computing a real direction — by
        // the time this runs from a StateChanged handler the sheet has already snapped to its
        // new state, and during drag updates the caller passes animated:false so the duration
        // is unused anyway. Not size-scaled (per G9BottomSheetSettings doc) — overlay fades
        // always use the full configured duration so they read cleanly even for short snaps.
        var isFadingOut = sheet.State == SfG9BottomSheetState.Hidden || targetColor.Alpha <= 0.001f;
        var overlayDurationMs = isFadingOut
            ? ResolveCloseAnimationDurationMs(options)
            : ResolveOpenAnimationDurationMs(options);

        sheet.AbortAnimation(ModalOverlayAnimationName);
        sheet.Animate(
            ModalOverlayAnimationName,
            value => overlay.Color = targetColor.WithAlpha((float)value),
            currentColor.Alpha,
            targetColor.Alpha,
            FitContentResizeAnimationRate,
            (uint)Math.Clamp((int)Math.Ceiling(overlayDurationMs), 1, CloseAnimationTimeoutMs),
            Easing.CubicOut,
            (_, _) => overlay.Color = targetColor);
    }

    private static bool ColorsEqualIgnoringAlpha(Color a, Color b)
    {
        return Math.Abs(a.Red - b.Red) < 0.01 &&
               Math.Abs(a.Green - b.Green) < 0.01 &&
               Math.Abs(a.Blue - b.Blue) < 0.01;
    }

    private static Color ResolveModalOverlayColor(
        CustomizedSfG9BottomSheet sheet,
        G9BottomSheetOptions options,
        double? ratioOverride)
    {
        if (options.WindowBackgroundColor is { } explicitColor)
        {
            return explicitColor;
        }

        var opacity = ResolveModalOverlayOpacity(sheet, options, ratioOverride);
        return Settings.ModalOverlayColor.WithAlpha((float)opacity);
    }

    private static double ResolveModalOverlayOpacity(
        CustomizedSfG9BottomSheet sheet,
        G9BottomSheetOptions options,
        double? ratioOverride)
    {
        var minimumOpacity = Settings.ModalOverlayMinimumOpacity;
        var maximumOpacity = Settings.ModalOverlayMaximumOpacity;
        var heightRatio = ratioOverride ?? ResolveCurrentSheetHeightRatio(sheet, options);
        var minimumRatio = ResolveMinimumStateHeightRatio(sheet, options);
        var maximumRatio = ResolveFullStateHeightRatio(options);

        var isSingleStateRange = Math.Abs(maximumRatio - minimumRatio) < 0.01;
        // Alpha that applies at the smallest allowed state. Multi-state sheets start at the
        // configured minimum; single-state (e.g. full-screen) sheets start at the maximum
        // because they have no smaller state to interpolate down to.
        var baseAlpha = isSingleStateRange ? maximumOpacity : minimumOpacity;

        // Drag-below-minimum: the user is pulling the sheet smaller than its smallest allowed
        // state (DefaultOptions dragged down past Medium, or full-screen dragged toward close).
        // Fade alpha from baseAlpha down to 0 proportional to how far below minimum we are, so
        // the dim disappears alongside the sheet body instead of sitting at a flat value.
        if (heightRatio < minimumRatio && minimumRatio > 0)
        {
            var fadeProgress = Math.Clamp(heightRatio / minimumRatio, 0, 1);
            return baseAlpha * fadeProgress;
        }

        if (isSingleStateRange)
        {
            return maximumOpacity;
        }

        var progress = Math.Clamp((heightRatio - minimumRatio) / (maximumRatio - minimumRatio), 0, 1);
        return minimumOpacity + ((maximumOpacity - minimumOpacity) * progress);
    }

    private static double ResolveCurrentSheetHeightRatio(CustomizedSfG9BottomSheet sheet, G9BottomSheetOptions options)
    {
        if (options.SizeMode == G9BottomSheetSizeMode.FitToContent)
        {
            return ResolveFitContentHeightRatio(sheet);
        }

        return ResolveStateHeightRatio(sheet, options, sheet.State);
    }

    private static SfG9BottomSheetState MapState(G9BottomSheetState state)
    {
        return state switch
        {
            G9BottomSheetState.Peek => SfG9BottomSheetState.Collapsed,
            G9BottomSheetState.Medium => SfG9BottomSheetState.HalfExpanded,
            G9BottomSheetState.Large => SfG9BottomSheetState.FullExpanded,
            _ => SfG9BottomSheetState.HalfExpanded
        };
    }

    private static bool ShouldEnableNativeStateSwiping(G9BottomSheetOptions options)
    {
        if (!options.IsDraggable ||
            options.OnBackRequested is not null ||
            options.SizeMode == G9BottomSheetSizeMode.FitToContent)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    ///     Whether Peek is a real DETENT of this sheet — i.e. the caller declared it alongside at
    ///     least one larger state. A single-state sheet has no peek step even when its one state is
    ///     Peek, and a fit-to-content sheet expresses its single height AS the collapsed height, so
    ///     neither may claim one: for both, a downward drag is a dismissal, not a step down.
    /// </summary>
    private static bool HasPeekDetent(G9BottomSheetOptions options)
    {
        return options.SizeMode == G9BottomSheetSizeMode.States &&
               options.States.Contains(G9BottomSheetState.Peek) &&
               options.States.Distinct().Count() > 1;
    }

    private static SfG9BottomSheetAllowedState MapAllowedState(IList<G9BottomSheetState> states)
    {
        var hasMedium = states.Contains(G9BottomSheetState.Medium);
        var hasLarge = states.Contains(G9BottomSheetState.Large);

        return (hasMedium, hasLarge) switch
        {
            (true, false) => SfG9BottomSheetAllowedState.HalfExpanded,
            (false, true) => SfG9BottomSheetAllowedState.FullExpanded,
            _ => SfG9BottomSheetAllowedState.All
        };
    }

    #endregion

    #region List Picker

    /// <summary>
    ///     Shows a reusable selectable list picker in a bottom sheet.
    ///     If another sheet is currently open, the picker is shown as a stacked sheet.
    ///     Returns selected item(s) when the picker closes.
    /// </summary>
    public static Task<IReadOnlyList<G9BottomSheetListItem>> ShowListG9BottomSheetAsync(
        string title,
        IEnumerable<G9BottomSheetListItem> items,
        IEnumerable<G9BottomSheetListItem>? selectedItems = null,
        bool allowMultipleSelection = false,
        bool closeOnSingleSelection = true,
        G9BottomSheetOptions? options = null,
        string? searchPlaceholder = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(items);

        var itemList = items.ToList();
        var selectedItemList = selectedItems?.ToList();

        var tcs = new TaskCompletionSource<IReadOnlyList<G9BottomSheetListItem>>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        G9BottomSheetListPickerModal? modal = null;

        void OnCompleted(object? sender, IReadOnlyList<G9BottomSheetListItem> selection)
        {
            if (modal is not null)
            {
                modal.Completed -= OnCompleted;
            }

            tcs.TrySetResult(selection);
        }

        View CreateModal()
        {
            modal = new G9BottomSheetListPickerModal(
                title,
                itemList,
                selectedItemList,
                allowMultipleSelection,
                closeOnSingleSelection,
                searchPlaceholder);
            modal.Completed += OnCompleted;
            return modal;
        }

        var baseOptions = options ?? G9BottomSheetOptions.FullScreenWithoutHandleOptions();
        var effectiveOptions = baseOptions with
        {
            // Use the SHARED bottom-sheet header (back + title) rather than the modal's own header
            // row, so the list picker matches every other sheet. The modal fills the content area
            // below it (it no longer computes an explicit height).
            ShowToolbar = true,
            ShowCloseButton = true,
            Title = title,
            HeaderTitlePlacement = G9BottomSheetHeaderTitlePlacement.NearBack,
            ClosedCommand = BuildListPickerClosedCommand(baseOptions, () => modal, tcs),
            SizeMode = G9BottomSheetSizeMode.States,
            CurrentState = G9BottomSheetState.Large,
            States = [G9BottomSheetState.Large],
            HasHandle = false,
            IsDraggable = true,
            DeferContent = true,
            UseFullScreenLoadingPlaceholder = true
        };


        // ShowG9BottomSheet now stacks automatically when another sheet is already open, so the
        // picker no longer branches on the open-sheet count itself.
        ShowG9BottomSheet(CreateModal, effectiveOptions);

        return tcs.Task;
    }

    private static ICommand BuildListPickerClosedCommand(
        G9BottomSheetOptions sourceOptions,
        Func<G9BottomSheetListPickerModal?> modalAccessor,
        TaskCompletionSource<IReadOnlyList<G9BottomSheetListItem>> tcs)
    {
        return new Command(() =>
        {
            if (modalAccessor() is { } modal)
            {
                modal.CompleteFromClose();
            }
            else
            {
                tcs.TrySetResult([]);
            }

            if (sourceOptions.ClosedCommand?.CanExecute(sourceOptions.ClosedCommandParameter) == true)
            {
                sourceOptions.ClosedCommand.Execute(sourceOptions.ClosedCommandParameter);
            }
        });
    }

    #endregion

    #region Private Internal Types

    private sealed class G9BottomSheetHandleImpl : IG9BottomSheetHandle
    {
        private readonly WeakReference<CustomizedSfG9BottomSheet>? _ownerSheet;

        public G9BottomSheetHandleImpl()
        {
        }

        public G9BottomSheetHandleImpl(CustomizedSfG9BottomSheet ownerSheet)
        {
            _ownerSheet = new WeakReference<CustomizedSfG9BottomSheet>(ownerSheet);
        }

        public void Close()
        {
            if (_ownerSheet?.TryGetTarget(out var ownerSheet) == true)
            {
                MainThread.BeginInvokeOnMainThread(() => CloseSheet(ownerSheet));
                return;
            }

            CloseG9BottomSheet();
        }

        public Task CloseAsync()
        {
            if (_ownerSheet?.TryGetTarget(out var ownerSheet) == true)
            {
                return CloseSheetAsync(ownerSheet);
            }

            var currentSheet = GetCurrentG9BottomSheet();
            return currentSheet is not null
                ? CloseSheetAsync(currentSheet)
                : Task.CompletedTask;
        }

        public void Show(View content, G9BottomSheetOptions? options = null)
        {
            ShowG9BottomSheet(content, options);
        }

        public void CloseTop()
        {
            CloseTopG9BottomSheet();
        }
    }

    private sealed class PrimarySheetHostState
    {
        public bool IsTransitioning { get; set; }

        public PendingPrimarySheetRequest? PendingRequest { get; set; }
    }

    private sealed record PendingPrimarySheetRequest(
        SheetContentRequest ContentRequest,
        G9BottomSheetOptions Options,
        FlowDirection FlowDirection);

    private sealed record SheetContentRequest(
        View? Content,
        Func<View>? ContentFactory,
        Action<View>? OnContentCreated)
    {
        public static SheetContentRequest FromContent(View content, Action<View>? onContentCreated = null)
        {
            return new SheetContentRequest(content, null, onContentCreated);
        }

        public static SheetContentRequest FromFactory(Func<View> contentFactory, Action<View>? onContentCreated = null)
        {
            return new SheetContentRequest(null, contentFactory, onContentCreated);
        }
    }

    private sealed class SheetBehaviorState(G9BottomSheetOptions options)
    {
        public G9BottomSheetOptions Options { get; } = options;

        public List<SheetTopPaddingTarget> TopPaddingTargets { get; } = [];

        public bool IsClosingCommandInvoked { get; set; }

        public bool IsClosedCommandInvoked { get; set; }

        public bool IsClosing { get; set; }

        // "Open then fill" content (IDeferredSheetLoad). Recorded when the sheet body is prepared
        // and invoked once, after the open animation completes (RunOpenedCommandLater), so the
        // heavy data fetch never blocks the tap → open path. The CTS is cancelled when the sheet
        // tears down so a late-completing load can't apply onto a dead view.
        public IDeferredSheetLoad? DeferredLoad { get; set; }

        public CancellationTokenSource? DeferredLoadCts { get; set; }

        public double LastFitContentHeight { get; set; }

        // While true, a deferred fit-to-content sheet whose final height is known up-front holds
        // that height STABLY through the whole loading window instead of measuring the
        // placeholder/spinner. Cleared when the real content swaps in
        // (DeferredContentView.ContentLoaded). This avoids the open→grow→shrink→grow flapping
        // that came from measuring the DeferredContentView (whose IsContentLoaded flips early,
        // mid-spinner, and re-measures the tiny spinner). Set by InitializeFitPlaceholder from
        // the caller's DeferredLoadingPlaceholderHeight or the session height memo.
        public bool UseDeferredPlaceholderHeight { get; set; }

        // The estimated BODY height (below the helper chrome) used while a deferred fit sheet is
        // loading — the caller's DeferredLoadingPlaceholderHeight when provided, otherwise the
        // session height memo for this body type. 0 = unknown (loading floor applies).
        public double PlaceholderBodyHeight { get; set; }

        // Height-memo key for this sheet's prebuilt body (null = not memoizable: factory
        // content, non-fit sheets).
        public string? FitHeightMemoKey { get; set; }

        // "Open then fill" body whose IsLoading flip ends the placeholder window (see
        // InitializeFitPlaceholder); handler kept so cleanup can unsubscribe a load that never
        // finished before the sheet closed.
        public LoadableSheetContentView? LoadableBody { get; set; }

        public PropertyChangedEventHandler? LoadableBodyHandler { get; set; }

        // 1.0 normally; lowered while this sheet closes to make room for an already-queued
        // replacement, so the dead close→open gap between chained sheets shrinks. Scales the
        // sheet's close motion and the close-cleanup wait.
        public double CloseDurationScale { get; set; } = 1.0;

        // Set while ApplyFitToContentHeight is running so the MeasureInvalidated tracker ignores
        // the InvalidateMeasure calls the apply itself issues (otherwise it would re-schedule
        // forever). See AttachFitToContentSizeTracking.
        public bool IsApplyingFitContent { get; set; }

        // Coalesces a burst of content MeasureInvalidated events into a single debounced
        // fit-to-content remeasure.
        public bool IsFitContentRefreshScheduled { get; set; }

        // True once the fit-to-content sheet has applied its first real (post-open / post-load)
        // size. Until then, layout-driven remeasures snap instantly (no animation) so the open
        // never shows a small→large jump. After it's set, genuine content changes animate.
        public bool IsFitContentSettled { get; set; }

        // The content's own height provider (count-aware/tab-aware), if it implements
        // IG9BottomSheetContentHeightProvider. Resolved lazily after deferred content loads.
        public IG9BottomSheetContentHeightProvider? HeightProvider { get; set; }

        public EventHandler? HeightProviderHandler { get; set; }

        // Coalesces provider-driven (tab switch / data load) height changes into one animated resize.
        public bool IsProviderRefreshScheduled { get; set; }

        // The vertical scroll viewport the HELPER wrapped around an ExpandedFitsContent body (null
        // when the body already scrolls itself, or for every other sizing mode). Kept because two
        // things need to tell it apart from a caller-owned scroller: the sizing engine measures
        // THROUGH it (a caller's scroller is capped instead, being unmeasurable), and the sheet
        // rewinds it when it leaves its top detent.
        public ScrollView? BodyScrollViewport { get; set; }
    }

    // Tracks the content view whose MeasureInvalidated event we listen to so a fit-to-content
    // sheet keeps growing as heavy / asynchronously-populated content settles. Stored per sheet
    // so the handler can be detached on cleanup.
    private sealed class FitToContentSizeTracker(View target, EventHandler handler)
    {
        public View Target { get; } = target;

        public EventHandler Handler { get; } = handler;
    }

    private sealed class SheetTopPaddingTarget(VisualElement owner, Action<double> applyPadding)
    {
        private const string AnimationName = "G9BottomSheetTopPadding";
        private const uint AnimationRate = 16;
        private const uint AnimationLength = 140;

        private double _currentValue;

        public void Apply(double value, bool animated)
        {
            value = Math.Max(0, value);

            if (Math.Abs(_currentValue - value) < 0.5)
            {
                applyPadding(value);
                _currentValue = value;
                return;
            }

            owner.AbortAnimation(AnimationName);

            if (!animated)
            {
                applyPadding(value);
                _currentValue = value;
                return;
            }

            var startValue = _currentValue;
            owner.Animate(
                AnimationName,
                current => applyPadding(current),
                startValue,
                value,
                AnimationRate,
                AnimationLength,
                Easing.CubicOut,
                (_, _) =>
                {
                    applyPadding(value);
                    _currentValue = value;
                });
        }
    }

    #endregion
}
