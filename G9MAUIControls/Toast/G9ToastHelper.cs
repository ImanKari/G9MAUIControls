using G9MAUIControls.Localization;
using G9MAUIControls.Toast;
using G9MAUIControls.Helpers;
using G9MAUIControls.Theming;
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

    // ZIndex is no longer set on individual toast / loader / progress visuals because the
    // helper mounts everything into the dedicated ToastHost grid in G9PageTemplate, which
    // already paints above OverlayHost (popup + sheet) via document order. See the layer
    // contract at the top of G9PageTemplate.xaml.

    private static G9InlineToastHandle? _activeToast;
    private static readonly List<G9InlineToastHandle> _activeToasts = [];
    private static InlineFullScreenLoadingHandle? _activeLoading;
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
                    new Label
                    {
                        Text = text,
                        FontSize = 15,
                        FontFamily = font,
                        TextColor = theme.InverseOnSurface,
                        HorizontalTextAlignment = TextAlignment.Center
                    }
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

        return new InlineFullScreenLoadingHandle(parent, overlay);
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
            button.Clicked += async (_, _) =>
            {
                onDismiss();
                if (opts.Action is not null)
                {
                    await opts.Action();
                }
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
                FillLayer = toastVisual.fillLayer
            };
            _activeToasts.Add(handle);
            _activeToast = handle;
            _ = ReflowToastStackAsync(context.Value.Parent, position, handle, true);

            if (opts.DurationMs > 0)
            {
                handle.AutoDismissCts = new CancellationTokenSource();
                StartInlineToastFillAnimation(handle, opts.DurationMs);
                _ = AutoDismissInlineToastAsync(handle, opts.DurationMs);
            }
        });
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
    /// </summary>
    public static async Task ShowLoadingAsync(string text)
    {
        var context = ResolveHostContext();
        if (context is null)
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
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
                // Animation aborted (e.g. another DismissAllAsync ran before this completed).
                // The handle is already tracked — DismissFullScreenLoadingImmediate cleans up.
            }
        });
    }

    /// <summary>
    ///     Dismisses the full-screen loading overlay.
    /// </summary>
    public static async Task DismissLoadingAsync()
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var handle = _activeLoading;
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
            _ = AnimateToastEnterAsync(handle, 0d);
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

    private static async Task DismissG9InlineToastHandleAsync(G9InlineToastHandle handle, bool animate)
    {
        if (handle.IsDismissing)
        {
            return;
        }

        handle.IsDismissing = true;
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

        if (animate)
        {
            await AnimateToastExitAsync(handle);
        }

        DismissG9InlineToastHandleImmediate(handle);

        if (wasStacked)
        {
            await ReflowToastStackAsync(handle.Parent, handle.Position, null, true);
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

        await Task.WhenAll(
            handle.Layer.FadeToAsync(1, EnterAnimDurationMs, Easing.SinOut),
            handle.Layer.TranslateToAsync(0, targetOffset, EnterAnimDurationMs, Easing.SinOut));
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

    private sealed record ProgressToastVisual(
        View Root,
        Label TitleLabel,
        Label DetailLabel,
        Label PercentLabel,
        VisualElement FillLayer);

    private sealed record ProgressToastState(G9InlineToastHandle Handle, ProgressToastVisual Visual);

    private sealed record InlineFullScreenLoadingHandle(Layout Parent, View Layer);

    #endregion
}
