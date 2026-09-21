using G9MAUIControls.Localization;
using G9MAUIControls.Toast;
using G9MAUIControls.Helpers;
using G9MAUIControls.Theming;
using Microsoft.Extensions.Logging;
using Microsoft.Maui.Controls.Shapes;
using G9PageBase = G9MAUIControls.Hosting.G9PageBase;
using System.Globalization;
using G9MAUIControls.Controls;
using G9MAUIControls.BottomSheet;

// G9ToastHelper lives in the Toast component folder; its namespace follows the folder path
// (Components.Toast). See ToastGuide.md "File Structure" for the toast component layout.
using G9MAUIControls.Icons;

namespace G9MAUIControls.Toast;

/// <summary>
///     Centralized helper for showing typed toasts (Information / Success / Warning / Error)
///     and loading overlays. Every visual is built from public MAUI primitives mounted as
///     inline overlays into the page-level layout — no Syncfusion SfG9Popup dependency, no
///     extra modal page push. Cross-platform: Android, iOS, macOS, and Windows.
///     <para>
///         Three loading modes are provided:
///         <list type="bullet">
///             <item><description><see cref="ShowLoadingAsync" /> — full-screen blocking scrim + centered card with a busy indicator.</description></item>
///             <item><description><see cref="ShowLoadingToastAsync" /> — compact positioned spinner that does NOT auto-dismiss.</description></item>
///             <item><description><see cref="ShowProgressToastAsync" /> — bottom-anchored progress card with a fill-to-100% animation.</description></item>
///         </list>
///     </para>
///     The busy indicator is the app's <see cref="G9ActivityIndicator" /> (a plain
///     MAUI <c>ActivityIndicator</c>); there is no third-party spinner dependency. If a future
///     flow needs a different spinner kind, build the inline view directly.
/// </summary>
public static class G9ToastHelper
{
    // Edge gap from the screen sides AND top/bottom is kept EQUAL (design rule): a top toast sits the
    // same distance below the safe-area top as it does from the left/right edges, and a bottom toast
    // the same distance above the bottom inset — so toasts never look "stuck" to one edge.
    private const double HorizontalGap = 16;
    private const double VerticalGap = 16;
    private const double EstimatedToastHeight = 72;
    private const double ToastStackGap = 8;
    private const uint EnterAnimDurationMs = 250;
    private const uint ExitAnimDurationMs = 200;
    private const string InlineToastFillAnimationName = "G9ToastHelper.InlineToastFill";
    private const double MobileBottomInsetFallback = 0;
    private const double MobileBottomInsetExtraGap = 8;
    private const double EstimatedSyncProgressHeight = 72;

    // Most toasts one (parent, position) stack shows at once. A loop that raises a toast per item
    // used to build a column that climbed off the screen and took minutes to drain; past this many
    // the OLDEST is dismissed to make room, so the newest news is always the one on screen.
    private const int MaxToastsPerStack = 4;

    // A toast action is consumer code run from a tap. Neither guard of G9SafeCommand fits it: the
    // key is shared by every toast, so throttling or the concurrency guard would silently drop the
    // "Undo" of a second toast tapped while the first one's action is still running.
    private static readonly G9SafeCommandOptions ToastActionOptions = new()
    {
        Source = nameof(G9ToastHelper),
        ThrottleKey = "G9ToastHelper.ToastAction",
        EnableThrottle = false,
        PreventConcurrentExecution = false,
        ShowErrorG9Popup = true
    };

    // ZIndex is no longer set on individual toast / loader / progress visuals because the
    // helper mounts everything into the dedicated ToastHost grid in G9PageTemplate, which
    // already paints above OverlayHost (popup + sheet) via document order. See the layer
    // contract at the top of G9PageTemplate.xaml.

    private static G9InlineToastHandle? _activeToast;
    private static readonly List<G9InlineToastHandle> _activeToasts = [];
    private static InlineFullScreenLoadingHandle? _activeLoading;

    // One entry per outstanding ShowLoadingAsync / BeginLoadingAsync, oldest first. UI thread only.
    // The blocker stays up while this is non-empty — see ShowLoadingAsync for why it is counted.
    private static readonly List<LoadingRequest> _loadingRequests = [];
    private static G9InlineToastHandle? _activeLoadingToast;
    private static ProgressToastState? _activeProgressToast;

    #region Dismiss All

    /// <summary>
    ///     Dismisses every active overlay (toast, loading, loading-toast).
    /// </summary>
    public static async Task DismissAllAsync()
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var allToasts = _activeToasts.ToArray();
            _activeToasts.Clear();
            foreach (var toast in allToasts)
            {
                DismissG9InlineToastHandleImmediate(toast);
            }

            _activeToast = null;

            // "Dismiss ALL" outranks the loading ref-count: it is the escape hatch for a blocker whose
            // owner never released it. Leases released afterwards find nothing to remove (no-op).
            _loadingRequests.Clear();
            DismissFullScreenLoadingImmediate();
            DismissInlineToast(ref _activeLoadingToast);
            DismissProgressToast();
        });
    }

    #endregion

    #region Build — Full-Screen Loading

    /// <summary>
    ///     Builds the inline full-screen loading overlay used by
    ///     <see cref="ShowLoadingAsync" />. The overlay covers the entire host with a scrim
    ///     <see cref="BoxView" /> (theme.Scrim) and centers a card with a busy indicator + a
    ///     label. Built from public MAUI primitives plus <see cref="G9ActivityIndicator" />
    ///     for the spinner.
    /// </summary>
    private static InlineFullScreenLoadingHandle BuildFullScreenLoadingOverlay(
        Layout parent,
        string text)
    {
        var theme = G9Palette.Current;
        var font = ResolveCulturalFont();

        // Held by the handle: a second ShowLoadingAsync re-labels the overlay that is already up
        // instead of tearing it down and fading a new one in.
        var textLabel = new Label
        {
            Text = text,
            FontSize = 15,
            FontFamily = font,
            TextColor = theme.InverseOnSurface,
            HorizontalTextAlignment = TextAlignment.Center
        };

        var card = new Border
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            BackgroundColor = theme.InverseSurface,
            Stroke = theme.OutlineBorder,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Padding = new Thickness(0),
            Content = new VerticalStackLayout
            {
                Spacing = 14,
                Padding = new Thickness(32, 28),
                HorizontalOptions = LayoutOptions.Center,
                Children =
                {
                    new G9ActivityIndicator
                    {
                        IsRunning = true,
                        Color = theme.InverseOnSurface,
                        WidthRequest = 50,
                        HeightRequest = 50,
                        HorizontalOptions = LayoutOptions.Center
                    },
                    textLabel
                }
            }
        };

        // The scrim BoxView blocks all input on the page beneath the overlay (input-opaque on
        // purpose; tapping the overlay must not dismiss the loader).
        var overlay = new Grid
        {
            BackgroundColor = theme.Scrim.WithAlpha(0.55f),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = false,
            CascadeInputTransparent = false,
            Opacity = 0
        };
        overlay.Children.Add(card);

        // Swallow taps on the scrim so they don't fall through to whatever is underneath.
        overlay.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => { /* swallow */ })
        });

        return new InlineFullScreenLoadingHandle(parent, overlay, textLabel);
    }

    #endregion

    #region Type Visuals

    private static (Color accent, Color background, Color textColor, G9IconSource icon) ResolveTypeVisuals(
        G9ToastType type,
        G9Palette theme,
        G9IconSource? iconOverride)
    {
        return type switch
        {
            G9ToastType.Success => (
                theme.OnSuccess,
                theme.Success,
                theme.OnSuccess,
                iconOverride ?? G9Glyphs.Success),
            G9ToastType.Warning => (
                theme.OnWarning,
                theme.Warning,
                theme.OnWarning,
                iconOverride ?? G9Glyphs.Warning),
            G9ToastType.Error => (
                theme.OnError,
                theme.Error,
                theme.OnError,
                iconOverride ?? G9Glyphs.Error),
            _ => (
                theme.OnInfo,
                theme.Info,
                theme.OnInfo,
                iconOverride ?? G9Glyphs.Info)
        };
    }

    #endregion

    #region Build — Toast

    private static (View root, VisualElement fillLayer) BuildInlineToastView(
        string message,
        G9ToastType type,
        G9ToastOptions opts,
        Action onDismiss)
    {
        var theme = G9Palette.Current;
        var font = ResolveCulturalFont();
        var (foreground, background, textColor, iconEnum) = ResolveTypeVisuals(type, theme, opts.Icon);
        var hasAction = !string.IsNullOrWhiteSpace(opts.ActionText);

        var contentGrid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            ],
            ColumnSpacing = 0,
            Padding = new Thickness(14, 12)
        };

        contentGrid.Add(new G9IconView {
            Icon = iconEnum,
            Color = foreground,
            Size = 20,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, 0, 8, 0)
        });

        var label = new Label
        {
            Text = message,
            FontSize = 14,
            FontFamily = font,
            TextColor = textColor,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };
        Grid.SetColumn(label, 1);
        contentGrid.Add(label);

        if (hasAction)
        {
            var button = new Button
            {
                Text = opts.ActionText,
                FontSize = 13,
                FontFamily = font,
                TextColor = foreground,
                BackgroundColor = foreground.WithAlpha(0.16f),
                CornerRadius = 6,
                Padding = new Thickness(10, 6),
                Margin = new Thickness(10, 0, 0, 0),
                VerticalOptions = LayoutOptions.Center
            };
            var actionInvoked = 0;
            button.Clicked += (_, _) =>
            {
                // The toast stays tappable through its 200 ms exit animation, so a quick second tap
                // used to run the action twice. One toast, one action.
                if (Interlocked.Exchange(ref actionInvoked, 1) != 0)
                {
                    return;
                }

                onDismiss();

                if (opts.Action is not { } action)
                {
                    return;
                }

                // This was `async (_, _) => await opts.Action()` — an async void event handler, so a
                // throwing action was an unhandled exception on the UI thread: the app crashed
                // because an "Undo" failed. RunSafe catches it, logs it and tells the user.
                G9SafeCommand.RunSafe(action, ToastActionOptions);
            };
            Grid.SetColumn(button, 2);
            contentGrid.Add(button);
        }

        var fillLayer = new Border
        {
            AnchorX = G9Culture.IsRtl ? 1d : 0d,
            BackgroundColor = foreground.WithAlpha(0.14f),
            HorizontalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            ScaleX = 0d,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            StrokeThickness = 0,
            VerticalOptions = LayoutOptions.Fill
        };

        var rootGrid = new Grid { fillLayer, contentGrid };

        var border = new Border
        {
            InputTransparent = false,
            Padding = new Thickness(0),
            BackgroundColor = background,
            Stroke = foreground.WithAlpha(0.35f),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = rootGrid
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onDismiss();
        border.GestureRecognizers.Add(tap);

        return (border, fillLayer);
    }

    private static View BuildInlineLoadingToastView(string text, Action onDismiss)
    {
        var theme = G9Palette.Current;
        var font = ResolveCulturalFont();

        var grid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(new GridLength(36)),
                new ColumnDefinition(GridLength.Star)
            ],
            ColumnSpacing = 10
        };

        grid.Add(new G9ActivityIndicator
        {
            IsRunning = true,
            Color = theme.Primary,
            WidthRequest = 30,
            HeightRequest = 30,
            VerticalOptions = LayoutOptions.Center
        });

        var label = new Label
        {
            Text = text,
            FontSize = 14,
            FontFamily = font,
            TextColor = theme.InverseOnSurface,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.WordWrap
        };
        Grid.SetColumn(label, 1);
        grid.Add(label);

        var border = new Border
        {
            InputTransparent = false,
            Padding = new Thickness(14, 10),
            BackgroundColor = theme.InverseSurface,
            Stroke = theme.OutlineBorder,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            Content = grid
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onDismiss();
        border.GestureRecognizers.Add(tap);

        return border;
    }

    // Visual style mirrors the bottom-anchored sync overlay so the toast reads correctly in
    // both light and dark themes (SurfaceContainerHigh + OnSurface) and renders reliably
    // inside bottom sheets (plain ActivityIndicator instead of SfBusyIndicator, which has
    // rendering issues in some nested-layout contexts).
    private static ProgressToastVisual BuildInlineProgressToastView(
        string title,
        string? detail,
        double progress,
        Action onDismiss)
    {
        var theme = G9Palette.Current;
        var font = ResolveCulturalFont();

        var contentGrid = new Grid
        {
            ColumnDefinitions =
            [
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            ],
            ColumnSpacing = 10,
            Padding = new Thickness(14, 12)
        };

        contentGrid.Add(new G9ActivityIndicator
        {
            IsRunning = true,
            Color = theme.Primary,
            WidthRequest = 18,
            HeightRequest = 18,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        });

        var titleLabel = new Label
        {
            Text = title,
            FontSize = 13,
            FontFamily = font,
            FontAttributes = FontAttributes.Bold,
            TextColor = theme.OnSurface,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

        var detailLabel = new Label
        {
            Text = detail,
            FontSize = 11,
            FontFamily = font,
            TextColor = theme.OnSurfaceVariant,
            IsVisible = !string.IsNullOrWhiteSpace(detail),
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

        var textStack = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { titleLabel, detailLabel }
        };
        Grid.SetColumn(textStack, 1);
        contentGrid.Add(textStack);

        var percentLabel = new Label
        {
            FontSize = 14,
            FontFamily = font,
            FontAttributes = FontAttributes.Bold,
            TextColor = theme.OnPrimary,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };

        var percentPill = new Border
        {
            BackgroundColor = theme.Primary.WithAlpha(0.98f),
            Padding = new Thickness(10, 4),
            StrokeShape = new RoundRectangle { CornerRadius = 10 },
            StrokeThickness = 0,
            VerticalOptions = LayoutOptions.Center,
            Content = percentLabel
        };
        Grid.SetColumn(percentPill, 2);
        contentGrid.Add(percentPill);

        // Full-content fill layer (matches SyncProgressToastView "ExpandedFillLayer"):
        // a Primary-tinted overlay whose ScaleX animates with progress to give the
        // visual "filling" effect underneath the text/spinner/pill.
        var fillLayer = new Border
        {
            AnchorX = G9Culture.IsRtl ? 1d : 0d,
            BackgroundColor = theme.Primary.WithAlpha(0.34f),
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            ScaleX = Math.Clamp(progress, 0d, 1d),
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            StrokeThickness = 0
        };

        var innerGrid = new Grid { fillLayer, contentGrid };

        var border = new Border
        {
            InputTransparent = false,
            Padding = new Thickness(0),
            BackgroundColor = theme.SurfaceContainerHigh,
            Stroke = theme.OutlineVariant.WithAlpha(0.52f),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 14 },
            Content = innerGrid
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onDismiss();
        border.GestureRecognizers.Add(tap);

        var visual = new ProgressToastVisual(border, titleLabel, detailLabel, percentLabel, fillLayer);
        ApplyProgressToastProgress(visual, progress);
        return visual;
    }

    private static void ApplyInlineG9ToastPosition(View toastView, G9PageBase? page, G9ToastPosition position)
    {
        var topInset = ResolveTopInset(page);
        var bottomInset = ResolveBottomInset(page, position);
        var vertical = position switch
        {
            G9ToastPosition.TopLeft or G9ToastPosition.TopCenter or G9ToastPosition.TopRight => LayoutOptions.Start,
            G9ToastPosition.MiddleLeft or G9ToastPosition.MiddleCenter or G9ToastPosition.MiddleRight => LayoutOptions.Center,
            _ => LayoutOptions.End
        };

        toastView.HorizontalOptions = LayoutOptions.Fill;
        toastView.VerticalOptions = vertical;
        toastView.Margin = vertical.Alignment switch
        {
            LayoutAlignment.Start => new Thickness(HorizontalGap, topInset + VerticalGap, HorizontalGap, bottomInset),
            LayoutAlignment.End => new Thickness(HorizontalGap, topInset, HorizontalGap, bottomInset + VerticalGap),
            _ => new Thickness(HorizontalGap, topInset, HorizontalGap, bottomInset)
        };
    }

    private static async Task AutoDismissInlineToastAsync(G9InlineToastHandle handle, int durationMs)
    {
        if (handle.AutoDismissCts is null)
        {
            return;
        }

        try
        {
            await Task.Delay(durationMs, handle.AutoDismissCts.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() => _ = DismissG9InlineToastHandleAsync(handle, true));
    }

    private static void StartInlineToastFillAnimation(G9InlineToastHandle handle, int durationMs)
    {
        if (handle.FillLayer is null || durationMs <= 0)
        {
            return;
        }

        handle.FillLayer.AnchorX = G9Culture.IsRtl ? 1d : 0d;
        handle.FillLayer.ScaleX = 0d;

        var animation = new Animation(value => handle.FillLayer.ScaleX = value, 0d, 1d, Easing.Linear);
        animation.Commit(
            handle.FillLayer,
            InlineToastFillAnimationName,
            rate: 16,
            length: (uint)Math.Clamp(durationMs, 250, 60000));
    }

    #endregion

    #region Toast

    /// <summary>
    ///     Shows a typed, auto-dismissing toast with icon and optional action button.
    /// </summary>
    public static async Task ShowToastAsync(
        string message,
        G9ToastType type = G9ToastType.Information,
        G9ToastOptions? options = null)
    {
        var context = ResolveHostContext();
        if (context is null)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var opts = options ?? G9ToastOptions.Default;
            var position = opts.Position ?? DefaultPosition();
            var hasAction = !string.IsNullOrWhiteSpace(opts.ActionText);

            // The same message again, while its toast is still the newest in the stack: give that
            // toast a fresh lifetime instead of stacking an identical copy on top of it.
            if (!hasAction &&
                TryRefreshDuplicateToast(context.Value.Parent, position, message, type, opts.DurationMs))
            {
                return;
            }

            G9InlineToastHandle? handle = null;
            var toastVisual = BuildInlineToastView(message, type, opts, () =>
            {
                if (handle is not null)
                {
                    _ = DismissG9InlineToastHandleAsync(handle, true);
                }
            });
            var toastView = toastVisual.root;
            PrepareInlineOverlayPlacement(context.Value.Parent, toastView);
            ApplyInlineG9ToastPosition(toastView, context.Value.Page, position);
            toastView.Opacity = 0;
            toastView.TranslationY = ResolveEnterOffset(position);
            context.Value.Parent.Add(toastView);

            handle = new G9InlineToastHandle(context.Value.Parent, toastView, position)
            {
                FillLayer = toastVisual.fillLayer,
                Message = message,
                Type = type,
                HasAction = hasAction
            };
            _activeToasts.Add(handle);
            _activeToast = handle;

            // Before the reflow: a trimmed toast is flagged IsDismissing synchronously, so the reflow
            // below already lays the stack out without it.
            TrimToastStack(context.Value.Parent, position);
            Observe(ReflowToastStackAsync(context.Value.Parent, position, handle, true), "toast stack reflow");

            ArmAutoDismiss(handle, opts.DurationMs);
        });
    }

    /// <summary>
    ///     Collapses "the same toast again". Only the NEWEST live toast of the stack is a candidate —
    ///     consecutive duplicates, not "this was said at some point" — and never one with an action
    ///     button (see <see cref="G9InlineToastHandle.HasAction" />).
    /// </summary>
    private static bool TryRefreshDuplicateToast(
        Layout parent,
        G9ToastPosition position,
        string message,
        G9ToastType type,
        int durationMs)
    {
        var newest = _activeToasts.LastOrDefault(x =>
            !x.IsDismissing && ReferenceEquals(x.Parent, parent) && x.Position == position);

        if (newest is null ||
            newest.HasAction ||
            newest.Type != type ||
            newest.Layer.Parent is null ||
            !string.Equals(newest.Message, message, StringComparison.Ordinal))
        {
            return false;
        }

        _activeToast = newest;
        ArmAutoDismiss(newest, durationMs);
        return true;
    }

    /// <summary>(Re)starts a toast's lifetime: the auto-dismiss timer and the fill bar that shows it.</summary>
    private static void ArmAutoDismiss(G9InlineToastHandle handle, int durationMs)
    {
        handle.AutoDismissCts?.Cancel();
        handle.AutoDismissCts?.Dispose();
        handle.AutoDismissCts = null;

        if (durationMs <= 0)
        {
            // Sticky. Also reached when a duplicate is re-shown as sticky: stop the old countdown bar.
            handle.FillLayer?.AbortAnimation(InlineToastFillAnimationName);
            return;
        }

        handle.AutoDismissCts = new CancellationTokenSource();

        try
        {
            StartInlineToastFillAnimation(handle, durationMs);
        }
        catch (Exception ex)
        {
            // The bar is decoration; the timer below is what actually removes the toast.
            LogErrorSafe(ex, "G9ToastHelper: toast fill animation failed.");
        }

        Observe(AutoDismissInlineToastAsync(handle, durationMs), "toast auto-dismiss");
    }

    /// <summary>Dismisses the oldest toasts of a stack once it exceeds <see cref="MaxToastsPerStack" />.</summary>
    private static void TrimToastStack(Layout parent, G9ToastPosition position)
    {
        var live = _activeToasts
            .Where(x => !x.IsDismissing && ReferenceEquals(x.Parent, parent) && x.Position == position)
            .ToList();

        for (var i = 0; i < live.Count - MaxToastsPerStack; i++)
        {
            _ = DismissG9InlineToastHandleAsync(live[i], true);
        }
    }

    /// <summary>
    ///     Programmatically dismisses the active toast.
    /// </summary>
    public static async Task DismissToastAsync()
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (_activeToast is not null)
            {
                DismissInlineToast(ref _activeToast);
                return;
            }

            var fallback = _activeToasts.LastOrDefault();
            if (fallback is not null)
            {
                _ = DismissG9InlineToastHandleAsync(fallback, true);
            }
        });
    }

    #endregion

    #region Full-Screen Loading

    /// <summary>
    ///     Shows a full-screen loading overlay with a busy indicator and text.
    ///     Blocks interaction until <see cref="DismissLoadingAsync" /> is called.
    ///     <para>
    ///         <b>Reference-counted.</b> There is one blocker, shared by the whole app, and it stays
    ///         up until every <c>ShowLoadingAsync</c> has been matched by a
    ///         <see cref="DismissLoadingAsync" />. It used to be a single slot: operation A's dismiss
    ///         removed the blocker operation B had just raised, and the user could tap through B's
    ///         "please wait". Pair the calls with <c>try / finally</c> — or use
    ///         <see cref="BeginLoadingAsync" />, which cannot be left unpaired.
    ///         <see cref="DismissAllAsync" /> clears the blocker regardless of the count.
    ///     </para>
    /// </summary>
    public static Task ShowLoadingAsync(string text)
    {
        return AcquireLoadingAsync(text, false);
    }

    /// <summary>
    ///     Lease form of <see cref="ShowLoadingAsync" />: the blocker is held until the returned lease
    ///     is disposed, and disposing it twice is harmless.
    ///     <code>
    ///     await using (await G9ToastHelper.BeginLoadingAsync("Signing in..."))
    ///     {
    ///         await SignInAsync();
    ///     }
    ///     </code>
    ///     A full-screen, input-opaque overlay whose release depends on a hand-written
    ///     <c>finally</c> is one missed code path away from a dead app; the lease makes the release
    ///     structural. It shares the count with <see cref="ShowLoadingAsync" />, so the two forms mix.
    /// </summary>
    public static async Task<IAsyncDisposable> BeginLoadingAsync(string text)
    {
        var request = await AcquireLoadingAsync(text, true).ConfigureAwait(false);
        return new LoadingLease(request);
    }

    private static async Task<LoadingRequest> AcquireLoadingAsync(string text, bool isLeased)
    {
        var request = new LoadingRequest(text, isLeased);

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            // Counted even when there is no host to show it on: the caller's dismiss WILL come, and
            // an uncounted show would let that dismiss take down somebody else's blocker.
            _loadingRequests.Add(request);

            var context = ResolveHostContext();
            if (context is null)
            {
                return;
            }

            // Already up on this host: re-label it (the newest request's text wins, as it did when
            // every show replaced the overlay) rather than rebuilding and fading it in again.
            var existing = _activeLoading;
            if (existing is not null &&
                ReferenceEquals(existing.Parent, context.Value.Parent) &&
                existing.Layer.Parent is not null)
            {
                existing.TextLabel.Text = text;
                return;
            }

            // None yet, or it belongs to a page that is no longer the host.
            DismissFullScreenLoadingImmediate();

            var handle = BuildFullScreenLoadingOverlay(context.Value.Parent, text);
            PrepareInlineOverlayPlacement(context.Value.Parent, handle.Layer);
            context.Value.Parent.Add(handle.Layer);
            _activeLoading = handle;

            try
            {
                // Single compound animation (opacity 0 -> 1) gives a clean fade-in at 200 ms,
                // matching the rest of the toast animation feel.
                await handle.Layer.FadeToAsync(1, 200, Easing.SinOut);
            }
            catch
            {
                // The animator refused (no MauiContext yet). The overlay is input-opaque from the
                // moment it is added, so it must not stay at opacity 0 — an invisible blocker is the
                // worst of both worlds. Only if it is still ours: a dismiss may have raced us.
                if (ReferenceEquals(_activeLoading, handle))
                {
                    handle.Layer.Opacity = 1;
                }
            }
        }).ConfigureAwait(false);

        return request;
    }

    /// <summary>
    ///     Dismisses the full-screen loading overlay — or, while other <see cref="ShowLoadingAsync" />
    ///     calls are still outstanding, releases this caller's hold on it.
    /// </summary>
    public static Task DismissLoadingAsync()
    {
        return ReleaseLoadingAsync(null);
    }

    /// <param name="request">
    ///     The lease's own entry, or <c>null</c> for the anonymous <see cref="DismissLoadingAsync" />,
    ///     which releases the most recent non-leased hold.
    /// </param>
    private static Task ReleaseLoadingAsync(LoadingRequest? request)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (request is not null)
            {
                if (!_loadingRequests.Remove(request))
                {
                    // Already cleared by DismissAllAsync. Nothing of ours is left to release.
                    return;
                }
            }
            else
            {
                var index = _loadingRequests.FindLastIndex(x => !x.IsLeased);
                if (index >= 0)
                {
                    _loadingRequests.RemoveAt(index);
                }
            }

            var handle = _activeLoading;
            if (_loadingRequests.Count > 0)
            {
                // Someone else still needs the blocker. Show what is still running, not what finished.
                if (handle is not null)
                {
                    handle.TextLabel.Text = _loadingRequests[^1].Text;
                }

                return;
            }

            if (handle is null)
            {
                return;
            }

            _activeLoading = null;

            try
            {
                await handle.Layer.FadeToAsync(0, 180, Easing.SinIn);
            }
            catch
            {
                // Swallow — animation can be aborted by the next ShowLoadingAsync.
            }

            if (handle.Layer.Parent is Layout parent)
            {
                parent.Remove(handle.Layer);
            }
        });
    }

    #endregion

    #region Compact Loading Toast

    /// <summary>
    ///     Shows a compact, positioned loading indicator (toast-like).
    ///     Does not auto-dismiss — call <see cref="DismissLoadingToastAsync" />.
    /// </summary>
    public static async Task ShowLoadingToastAsync(string text, G9ToastPosition? position = null)
    {
        var context = ResolveHostContext();
        if (context is null)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            DismissInlineToast(ref _activeLoadingToast);
            var pos = position ?? DefaultPosition();

            var loadingToastView =
                BuildInlineLoadingToastView(text, () => DismissInlineToast(ref _activeLoadingToast));
            PrepareInlineOverlayPlacement(context.Value.Parent, loadingToastView);
            ApplyInlineG9ToastPosition(loadingToastView, context.Value.Page, pos);
            context.Value.Parent.Add(loadingToastView);

            _activeLoadingToast = new G9InlineToastHandle(context.Value.Parent, loadingToastView, pos);
        });
    }

    /// <summary>
    ///     Dismisses the compact loading toast.
    /// </summary>
    public static async Task DismissLoadingToastAsync()
    {
        await MainThread.InvokeOnMainThreadAsync(() => DismissInlineToast(ref _activeLoadingToast));
    }

    #endregion

    #region Progress Toast

    public static async Task ShowProgressToastAsync(
        string title,
        string? detail = null,
        double progress = 0d,
        G9ToastPosition? position = null)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var context = ResolveHostContext();
            if (context is null)
            {
                return;
            }

            DismissProgressToast();
            var pos = position ?? DefaultPosition();

            var visual = BuildInlineProgressToastView(
                title,
                detail,
                progress,
                DismissProgressToast);
            PrepareInlineOverlayPlacement(context.Value.Parent, visual.Root);
            ApplyInlineG9ToastPosition(visual.Root, context.Value.Page, pos);
            visual.Root.Opacity = 0d;
            visual.Root.TranslationY = ResolveEnterOffset(pos);
            context.Value.Parent.Add(visual.Root);

            var handle = new G9InlineToastHandle(context.Value.Parent, visual.Root, pos);
            _activeProgressToast = new ProgressToastState(handle, visual);
            Observe(AnimateToastEnterAsync(handle, 0d), "progress toast enter animation");
        });
    }

    public static async Task UpdateProgressToastAsync(
        string? title = null,
        string? detail = null,
        double? progress = null)
    {
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var state = _activeProgressToast;
            if (state is null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(title))
            {
                state.Visual.TitleLabel.Text = title;
            }

            state.Visual.DetailLabel.Text = detail;
            state.Visual.DetailLabel.IsVisible = !string.IsNullOrWhiteSpace(detail);

            if (progress.HasValue)
            {
                ApplyProgressToastProgress(state.Visual, progress.Value);
            }
        });
    }

    public static async Task DismissProgressToastAsync()
    {
        await MainThread.InvokeOnMainThreadAsync(DismissProgressToast);
    }

    #endregion

    #region Lifecycle

    private static void DismissFullScreenLoadingImmediate()
    {
        var handle = _activeLoading;
        if (handle is null)
        {
            return;
        }

        _activeLoading = null;
        handle.Layer.CancelAnimations();

        if (handle.Layer.Parent is Layout parent)
        {
            parent.Remove(handle.Layer);
        }
    }

    private static void DismissInlineToast(ref G9InlineToastHandle? tracker)
    {
        if (tracker is null)
        {
            return;
        }

        var handle = tracker;
        tracker = null;
        _ = DismissG9InlineToastHandleAsync(handle, true);
    }

    private static void DismissProgressToast()
    {
        var state = _activeProgressToast;
        if (state is null)
        {
            return;
        }

        _activeProgressToast = null;
        _ = DismissG9InlineToastHandleAsync(state.Handle, true);
    }

    /// <summary>
    ///     Dismisses one toast. <b>Never throws</b> — every caller discards the task (a tap handler, a
    ///     timer), so anything that escaped would be an unobserved exception nobody ever saw.
    /// </summary>
    private static async Task DismissG9InlineToastHandleAsync(G9InlineToastHandle handle, bool animate)
    {
        if (handle.IsDismissing)
        {
            return;
        }

        handle.IsDismissing = true;

        try
        {
            handle.AutoDismissCts?.Cancel();
            handle.AutoDismissCts?.Dispose();
            handle.AutoDismissCts = null;
            handle.FillLayer?.CancelAnimations();

            var wasStacked = _activeToasts.Remove(handle);

            if (ReferenceEquals(_activeToast, handle))
            {
                _activeToast = _activeToasts.LastOrDefault(x =>
                                   !x.IsDismissing && ReferenceEquals(x.Parent, handle.Parent) &&
                                   x.Position == handle.Position)
                               ?? _activeToasts.LastOrDefault(x => !x.IsDismissing);
            }

            try
            {
                if (animate)
                {
                    await AnimateToastExitAsync(handle);
                }
            }
            finally
            {
                // In a finally because the exit animation can throw (a host torn down under it), and
                // the toast is input-opaque: skipping the removal left an invisible, opacity-0 view in
                // the tree that kept swallowing taps on whatever was behind it.
                DismissG9InlineToastHandleImmediate(handle);
            }

            if (wasStacked)
            {
                await ReflowToastStackAsync(handle.Parent, handle.Position, null, true);
            }
        }
        catch (Exception ex)
        {
            LogErrorSafe(ex, "G9ToastHelper: dismissing a toast failed.");
        }
    }

    private static void DismissG9InlineToastHandleImmediate(G9InlineToastHandle handle)
    {
        handle.AutoDismissCts?.Cancel();
        handle.AutoDismissCts?.Dispose();
        handle.AutoDismissCts = null;
        handle.FillLayer?.CancelAnimations();
        _activeToasts.Remove(handle);

        if (handle.Layer.Parent is Layout parent)
        {
            parent.Remove(handle.Layer);
        }
    }

    private static async Task ReflowToastStackAsync(
        Layout parent,
        G9ToastPosition position,
        G9InlineToastHandle? entering,
        bool animate)
    {
        var handles = _activeToasts
            .Where(x => !x.IsDismissing && ReferenceEquals(x.Parent, parent) && x.Position == position)
            .ToList();
        if (handles.Count == 0)
        {
            return;
        }

        var bottomAnchored =
            position is G9ToastPosition.BottomLeft or G9ToastPosition.BottomCenter or G9ToastPosition.BottomRight;
        var runningDistance = bottomAnchored ? ResolveBottomOverlayStackOffset(parent, handles) : 0d;
        var shiftTasks = new List<Task>(handles.Count);

        foreach (var toast in handles)
        {
            var targetOffset = bottomAnchored ? -runningDistance : runningDistance;
            var toastHeight = ResolveToastHeight(toast);
            runningDistance += toastHeight + ToastStackGap;

            toast.StackOffset = targetOffset;

            if (!animate)
            {
                toast.Layer.TranslationY = targetOffset;
                toast.Layer.Opacity = 1;
                continue;
            }

            if (ReferenceEquals(toast, entering))
            {
                shiftTasks.Add(AnimateToastEnterAsync(toast, targetOffset));
                continue;
            }

            shiftTasks.Add(toast.Layer.TranslateToAsync(0, targetOffset, EnterAnimDurationMs, Easing.SinOut));
        }

        if (shiftTasks.Count > 0)
        {
            await Task.WhenAll(shiftTasks);
        }
    }

    /// <summary>
    ///     Re-lays out the toast stack in <paramref name="parent" />, so bottom-anchored toasts sit clear
    ///     of whatever else is anchored there.
    ///     <para>
    ///         <b>Public because <see cref="G9MAUIControls.Toast.IG9BottomAnchoredOverlay" /> is only half a contract without
    ///         it.</b> That interface lets an external overlay declare "toasts should stack above me", and
    ///         the helper honours it on every show and dismiss — but it cannot know when the overlay's own
    ///         height changes. An overlay that grows (a detail line appears) or shrinks (it minimises to a
    ///         bubble) has to say so, or the toast above it is left floating in the wrong place until the
    ///         next unrelated toast happens to trigger a reflow.
    ///     </para>
    ///     <para>
    ///         Safe to call from any thread and safe to call when no toast is showing — it becomes a no-op.
    ///         Call it after your overlay's size settles, not during the animation, or you pay a reflow per
    ///         frame.
    ///     </para>
    /// </summary>
    /// <param name="parent">
    ///     The layer the overlay is mounted in — normally <see cref="G9MAUIControls.Hosting.IG9OverlayHost.ToastLayer" />.
    /// </param>
    /// <param name="animate">
    ///     Animate the toasts to their new offsets. Pass <c>false</c> while the overlay is itself
    ///     animating, so the two do not visibly fight.
    /// </param>
    public static Task ReflowInlineToastsForHostAsync(Layout parent, bool animate = true)
    {
        if (MainThread.IsMainThread)
        {
            return ReflowInlineToastsForHostCoreAsync(parent, animate);
        }

        return MainThread.InvokeOnMainThreadAsync(() => ReflowInlineToastsForHostCoreAsync(parent, animate));
    }

    private static async Task ReflowInlineToastsForHostCoreAsync(Layout parent, bool animate)
    {
        var positions = _activeToasts
            .Where(x => !x.IsDismissing && ReferenceEquals(x.Parent, parent))
            .Select(x => x.Position)
            .Distinct()
            .ToArray();

        foreach (var position in positions)
        {
            await ReflowToastStackAsync(parent, position, null, animate);
        }
    }

    private static async Task AnimateToastEnterAsync(G9InlineToastHandle handle, double targetOffset)
    {
        var startOffset = targetOffset + ResolveEnterOffset(handle.Position);
        handle.Layer.Opacity = 0;
        handle.Layer.TranslationY = startOffset;

        try
        {
            await Task.WhenAll(
                handle.Layer.FadeToAsync(1, EnterAnimDurationMs, Easing.SinOut),
                handle.Layer.TranslateToAsync(0, targetOffset, EnterAnimDurationMs, Easing.SinOut));
        }
        catch
        {
            // Same rule as the exit: the toast is already in the tree and input-opaque, so a failed
            // enter animation must not strand it at the opacity 0 set above. Snap it into place.
            handle.Layer.Opacity = 1;
            handle.Layer.TranslationY = targetOffset;
            throw;
        }
    }

    private static async Task AnimateToastExitAsync(G9InlineToastHandle handle)
    {
        var endOffset = handle.Layer.TranslationY + ResolveExitOffset(handle.Position);
        await Task.WhenAll(
            handle.Layer.FadeToAsync(0, ExitAnimDurationMs, Easing.SinIn),
            handle.Layer.TranslateToAsync(0, endOffset, ExitAnimDurationMs, Easing.SinIn));
    }

    private static double ResolveToastHeight(G9InlineToastHandle handle)
    {
        if (handle.Layer.Height > 0)
        {
            return handle.Layer.Height;
        }

        var parentWidth = handle.Parent.Width > 0 ? handle.Parent.Width : 400;
        var width = ResolveToastWidth(parentWidth);
        var measured = handle.Layer.Measure(width, double.PositiveInfinity);
        if (measured.Height > 0)
        {
            return measured.Height;
        }

        return EstimatedToastHeight;
    }

    private static double ResolveBottomOverlayStackOffset(Layout parent, IReadOnlyList<G9InlineToastHandle> handles)
    {
        if (handles.Count == 0)
        {
            return 0;
        }

        var syncOverlay = parent.Children
            .OfType<IG9BottomAnchoredOverlay>()
            .OfType<View>()
            .LastOrDefault(view =>
                ReferenceEquals(view.Parent, parent) &&
                view.VerticalOptions.Alignment == LayoutAlignment.End &&
                view.IsVisible);

        if (syncOverlay is null)
        {
            return 0;
        }

        var overlayHeight = ResolveViewHeight(syncOverlay, parent, EstimatedSyncProgressHeight);
        if (overlayHeight <= 0)
        {
            return 0;
        }

        var overlayBottom = Math.Max(0, syncOverlay.Margin.Bottom);
        var toastBottom = Math.Max(0, handles[0].Layer.Margin.Bottom);
        var desiredBottom = overlayBottom + overlayHeight + ToastStackGap;

        return Math.Max(0, desiredBottom - toastBottom);
    }

    private static double ResolveViewHeight(View view, Layout parent, double fallback)
    {
        if (view.Height > 0)
        {
            return view.Height;
        }

        var parentWidth = parent.Width > 0 ? parent.Width : 400;
        var horizontalMargins = Math.Max(0, view.Margin.Left) + Math.Max(0, view.Margin.Right);
        var availableWidth = Math.Max(120, parentWidth - horizontalMargins);
        var measured = view.Measure(availableWidth, double.PositiveInfinity);

        return measured.Height > 0 ? measured.Height : fallback;
    }

    private static double ResolveEnterOffset(G9ToastPosition position)
    {
        return position is G9ToastPosition.TopLeft or G9ToastPosition.TopCenter or G9ToastPosition.TopRight ? -30 : 30;
    }

    private static double ResolveExitOffset(G9ToastPosition position)
    {
        return position is G9ToastPosition.TopLeft or G9ToastPosition.TopCenter or G9ToastPosition.TopRight ? -18 : 18;
    }

    #endregion

    #region Positioning

    private static G9ToastPosition DefaultPosition()
    {
        return G9Culture.IsRtl ? G9ToastPosition.BottomLeft : G9ToastPosition.BottomRight;
    }

    #endregion

    #region Helpers

    private readonly record struct ToastHostContext(Layout Parent, G9PageBase? Page);

    private static ToastHostContext? ResolveHostContext()
    {
        // Mount on the dedicated ToastHost grid that G9PageTemplate paints ABOVE OverlayHost
        // (popup + bottom sheet). This is what guarantees the app-wide z-stack contract:
        // toasts paint above any open popup or sheet, and a toast started inside a sheet keeps
        // showing after the sheet closes — see G9PageTemplate.xaml for the full layer order.
        // ToastHost itself is part of the control template, so it outlives every sheet / popup
        // / page-content swap; its lifetime is tied to G9PageBase.OnApplyTemplate / detach.
        if (G9ModalHostRegistry.TryGetCurrentHost(out var host))
        {
            return new ToastHostContext(host.ToastHost, host.Page);
        }

        // Fallback path — only reachable during the brief startup window before
        // OnApplyTemplate runs on the active G9PageBase. No popup or sheet exists at that
        // point, so anchoring on the page Content layout doesn't violate the z-stack.
        var page = ResolveVisiblePage(Application.Current?.Windows
            .Where(window => window.Page is not null)
            .Select(window => window.Page)
            .FirstOrDefault());

        if (page is ContentPage contentPage && contentPage.Content is Layout layout)
        {
            return new ToastHostContext(layout, page as G9PageBase);
        }

        return null;
    }

    private static Page? ResolveVisiblePage(Page? page)
    {
        if (page is null)
        {
            return null;
        }

        if (page.Navigation?.ModalStack is { Count: > 0 } modalStack &&
            !ReferenceEquals(modalStack[^1], page))
        {
            return ResolveVisiblePage(modalStack[^1]);
        }

        return page;
    }

    private static double ResolveToastWidth(double parentWidth)
    {
        var availableWidth = parentWidth - (HorizontalGap * 2);
        if (availableWidth <= 0)
        {
            return parentWidth;
        }

        return Math.Max(120, availableWidth);
    }

    private static double ResolveTopInset(G9PageBase? page)
    {
        if (!IsMobilePlatform())
        {
            return 0;
        }

        return Math.Max(0, page?.TopSafeAreaInset ?? 0);
    }

    private static double ResolveBottomInset(G9PageBase? page, G9ToastPosition position)
    {
        var isBottom = position is G9ToastPosition.BottomLeft or G9ToastPosition.BottomCenter or G9ToastPosition.BottomRight;

        // Tab-bar clearance: bottom-anchored toasts on MainPage must float ABOVE the managed
        // bottom tab bar — but ONLY while no bottom sheet is open. In this single-page app every
        // non-tab screen opens as a bottom sheet that covers the tab bar, so when a sheet is up
        // the tab bar isn't visible and the toast falls back to the normal safe-area gap.
        // BottomSafeAreaWithTabBar adds the reserved tab-bar band (bar height + 12dp margin) over
        // BottomSafeAreaInset only on MainPage; it equals BottomSafeAreaInset on every other host
        // (login / error pages), so the delta is a no-op there.
        var tabBarClearance = 0d;
        if (isBottom && page is not null && G9BottomSheetHelper.GetOpenSheetCount() == 0)
        {
            tabBarClearance = Math.Max(0, page.BottomSafeAreaWithTabBar - page.BottomSafeAreaInset);
        }

        if (!IsMobilePlatform())
        {
            // Desktop has no OS safe-area inset, but the managed tab bar still needs clearing.
            return isBottom && tabBarClearance > 0 ? tabBarClearance + MobileBottomInsetExtraGap : 0;
        }

        var bottomInset = page?.BottomSafeAreaInset ?? 0;

        if (bottomInset <= 0)
        {
            bottomInset = MobileBottomInsetFallback;
        }

        bottomInset += tabBarClearance;

        return isBottom
            ? bottomInset + MobileBottomInsetExtraGap
            : bottomInset;
    }

    private static bool IsMobilePlatform()
    {
        return DeviceInfo.Platform == DevicePlatform.Android || DeviceInfo.Platform == DevicePlatform.iOS;
    }

    private static void PrepareInlineOverlayPlacement(Layout parent, View view)
    {
        if (parent is not Grid grid)
        {
            return;
        }

        var rowSpan = Math.Max(1, grid.RowDefinitions.Count);
        var columnSpan = Math.Max(1, grid.ColumnDefinitions.Count);

        Grid.SetRow(view, 0);
        Grid.SetColumn(view, 0);
        Grid.SetRowSpan(view, rowSpan);
        Grid.SetColumnSpan(view, columnSpan);
    }

    private static void ApplyProgressToastProgress(ProgressToastVisual visual, double progress)
    {
        var normalizedProgress = Math.Clamp(progress, 0d, 1d);
        visual.FillLayer.AnchorX = G9Culture.IsRtl ? 1d : 0d;
        visual.FillLayer.ScaleX = normalizedProgress;
        visual.PercentLabel.Text = normalizedProgress.ToString("P0", CultureInfo.CurrentCulture);
    }

    private static string ResolveCulturalFont()
    {
        return G9Culture.ResolveAppFont("CulturalFont", G9Culture.RtlFontFamily);
    }

    /// <summary>
    ///     Observes a task this helper starts and does not await (a reflow, an enter animation, the
    ///     auto-dismiss timer), so a fault is logged instead of vanishing as an unobserved exception.
    ///     Same shape as <c>G9SafeCommand.SafeFireAndForget</c>.
    /// </summary>
    private static async void Observe(Task task, string operation)
    {
        try
        {
            await task;
        }
        catch (Exception ex)
        {
            LogErrorSafe(ex, $"G9ToastHelper: {operation} failed.");
        }
    }

    private static void LogErrorSafe(Exception ex, string message)
    {
        try
        {
            // GetServiceNullable throws while G9ServiceProvider is uninitialized; a toast must work
            // (and fail) quietly before the host has wired logging up.
            G9ServiceProvider.GetServiceNullable<ILoggerFactory>()
                ?.CreateLogger("G9ToastHelper")
                .LogError(ex, "{Message}", message);
        }
        catch
        {
            // Logging is best-effort by definition.
        }
    }

    private sealed record ProgressToastVisual(
        View Root,
        Label TitleLabel,
        Label DetailLabel,
        Label PercentLabel,
        VisualElement FillLayer);

    private sealed record ProgressToastState(G9InlineToastHandle Handle, ProgressToastVisual Visual);

    private sealed record InlineFullScreenLoadingHandle(Layout Parent, View Layer, Label TextLabel);

    /// <summary>One outstanding request for the full-screen blocker. See <see cref="ShowLoadingAsync" />.</summary>
    private sealed class LoadingRequest(string text, bool isLeased)
    {
        public string Text { get; } = text;

        /// <summary>
        ///     True when a <see cref="BeginLoadingAsync" /> lease owns this entry. The anonymous
        ///     <see cref="DismissLoadingAsync" /> never removes a leased entry — it cannot know whose it
        ///     is, and taking one would leave that lease's own release with nothing to remove and the
        ///     blocker up for good.
        /// </summary>
        public bool IsLeased { get; } = isLeased;
    }

    private sealed class LoadingLease(LoadingRequest request) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            // Interlocked: a lease disposed twice must not release someone else's hold.
            return Interlocked.Exchange(ref _released, 1) != 0
                ? ValueTask.CompletedTask
                : new ValueTask(ReleaseLoadingAsync(request));
        }
    }

    #endregion
}
