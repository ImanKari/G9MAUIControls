using G9MAUIControls.Controls;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Maui.BindableProperty.Generator.Core;
using Microsoft.Maui.Controls.Shapes;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;
/// <summary>
///     Full-screen login onboarding carousel: looping background video (or image),
///     dark overlay, logo, localized title/subtitle, step indicator, and navigation.
///     Uses a single MediaElement; slide changes swap the source, not the player.
/// </summary>
public partial class G9IntroCarousel : G9ControlBase
{
    /// <summary>Matches bootstrap / native splash so the screen is never solid black before video.</summary>
    private static readonly Color IntroBackdropColor = Color.FromArgb("#2E7D32");

    private readonly Grid _root;
    private readonly Grid _mediaHost;
    private readonly MediaElement _mediaElement;
    private readonly Image _fallbackImage;
    private readonly Image _startupCoverImage;
    private readonly BoxView _backdrop;
    private readonly GraphicsView _overlay;
    private readonly IntroOverlayDrawable _overlayDrawable = new();
    private readonly Grid _chrome;
    private readonly Grid _headerRow;
    private readonly Image _logo;
    private readonly G9SafeIconButton _languageButton;
    private readonly BoxView _swipePassthrough;
    private readonly VerticalStackLayout _textBlock;
    private readonly Label _titleLabel;
    private readonly Label _subtitleLabel;
    private readonly Grid _navRow;
    private readonly Border _nextTapTarget;
    private readonly Border _prevTapTarget;
    private readonly HorizontalStackLayout _nextHost;
    private readonly HorizontalStackLayout _prevHost;
    private readonly Label _nextLabel;
    private readonly Label _prevLabel;
    private readonly G9IconView _nextIcon;
    private readonly G9IconView _prevIcon;
    private readonly HorizontalStackLayout _stepHost;
    private readonly G9SafeButton _signInButton;
    private readonly ActivityIndicator _mediaSpinner;
    private readonly List<Border> _stepPills = [];
    private IList<G9IntroSlideItem> _resolvedSlides = [];
    private bool _presentationStarted;
    private bool _initialVideoSeekApplied;
    private bool _startupCoverDismissed;
    private bool _startupCoverMediaReady;
    private DateTimeOffset _startupCoverShownAt;
    private int _scheduledMediaIndex = -1;
    private int _loadedMediaIndex = -1;
    private bool _loadedMediaIsVideo;
    private bool _isMediaLoadActive;
    private bool _firstContentReadyRaised;
    private bool _firstSlideInitialFadeHandled;
    private bool _videoLoopEndFadeStarted;
    private TimeSpan _lastVideoPosition;
    private double _panTotalX;
    private CancellationTokenSource? _mediaCts;
    private CancellationTokenSource? _startupCoverCts;
    // Tracks the last layout direction applied to the nav row so ApplyNavIconLayout
    // can skip the Children.Clear()+re-add when the direction hasn't changed.
    private bool? _lastNavIsRtl;
    /// <summary>
    ///     The brand logo shown above the slide text. <c>null</c> (the default) hides it.
    /// </summary>
    /// <remarks>
    ///     A logo is the consuming app's asset, so the carousel ships without one and shows nothing
    ///     until this is set. Sized by <c>G9Metrics.IntroLogoWidth</c> / <c>IntroLogoHeight</c>.
    /// </remarks>
    [AutoBindable(OnChanged = nameof(OnLogoSourceChanged))]
    private ImageSource? _logoSource;

    [AutoBindable(OnChanged = nameof(OnSlidesChanged))]
    private IList<G9IntroSlideItem>? _slides;
    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnIndexChanged))]
    private int _currentIndex;
    [AutoBindable(OnChanged = nameof(OnOverlayVisualChanged))] private double _overlayOpacity = G9Metrics.IntroOverlayOpacity;
    [AutoBindable(OnChanged = nameof(OnOverlayVisualChanged))] private bool _useGradientOverlay;
    [AutoBindable(OnChanged = nameof(OnOverlayVisualChanged))] private double _overlayTopOpacityRatio = G9Metrics.IntroOverlayTopOpacityRatio;
    [AutoBindable(OnChanged = nameof(OnOverlayVisualChanged))] private double _overlayTopOpacity = double.NaN;
    [AutoBindable(OnChanged = nameof(OnOverlayVisualChanged))] private Color _overlayColor = Colors.Black;
    [AutoBindable] private bool _useMediaFadeTransitions = true;
    [AutoBindable] private bool _useVideoLoopFade = true;
    [AutoBindable] private bool _skipFirstSlideInitialFadeIn = true;
    [AutoBindable] private int _mediaFadeInDurationMs = G9Metrics.IntroMediaFadeInDurationMs;
    [AutoBindable] private int _mediaFadeOutDurationMs = G9Metrics.IntroMediaFadeOutDurationMs;
    // NOTE: the UseChromeShadow / ChromeShadowOpacity / ChromeShadowRadius / ChromeShadowOffsetY
    // properties were removed with the app-wide shadow ban. They set a MAUI `Shadow` on the logo
    // Image and four Labels — none of which have a BorderDrawable background, so every one of them
    // took MAUI's software bitmap-blur path on Android. Legibility over the hero media now comes
    // from the gradient overlay (UseGradientOverlay / OverlayOpacity). See G9Controls.md.
    [AutoBindable(OnChanged = nameof(OnStartupCoverChanged))] private string? _startupCoverImageSource;
    [AutoBindable(OnChanged = nameof(OnStartupCoverChanged))] private int _startupCoverDurationMs;
    [AutoBindable] private int _startupCoverFadeDurationMs = G9Metrics.IntroStartupCoverFadeDurationMs;
    [AutoBindable] private double _initialVideoStartSeconds;
    [AutoBindable] private ICommand? _languageCommand;
    [AutoBindable] private ICommand? _completeCommand;

    /// <summary>
    ///     Raised once, on the main thread, the first time the carousel has real content to show —
    ///     the first slide's video frame is revealed, its fallback image is shown, or the media
    ///     settled with nothing left to load. Hosts (e.g. <c>LoginPage</c>) use this to dismiss a
    ///     loading splash only after the first frame is painted, so the user never sees a black /
    ///     empty player. Never fires more than once per control lifetime.
    /// </summary>
    public event EventHandler? FirstContentReady;

    public G9IntroCarousel()
    {
        _mediaElement = new MediaElement
        {
            Aspect = Aspect.AspectFill,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            ShouldAutoPlay = true,
            ShouldLoopPlayback = true,
            ShouldShowPlaybackControls = false,
            ShouldMute = true,
            // Keep IsVisible=true so MAUI creates the native handler immediately.
            // MAUI defers platform handler creation for IsVisible=false elements, which
            // prevents HandlerChanged from ever firing. Use Opacity=0 to hide visually.
            IsVisible = true,
            Opacity = 0,
            // Transparent MAUI-level BackgroundColor ensures the native FrameLayout's background
            // is set to transparent before the first render, so the gray Material theme default
            // surface color never shows even for the single frame before OnMediaElementHandlerReady.
            BackgroundColor = Colors.Transparent,
            // SurfaceView draws above sibling MAUI views on Android; TextureView respects z-order.
            AndroidViewType = AndroidViewType.TextureView
        };
        _mediaElement.MediaFailed += OnMediaFailed;
        _mediaElement.MediaOpened += OnMediaOpened;
        _mediaElement.PositionChanged += OnMediaPositionChanged;
        // HandlerChanged on the media element fires when its native ExoPlayer view is ready.
        // ContentView (this) has no native handler on Android, so we use the child's event.
        _mediaElement.HandlerChanged += OnMediaElementHandlerReady;
        // HandlerChanging on the media element fires right BEFORE MAUI calls DisconnectHandler
        // on it (which disposes the native MauiMediaElement). We stop ExoPlayer here to release
        // its surface before the FrameLayout is torn down.
        _mediaElement.HandlerChanging += OnMediaElementHandlerChanging;
        _fallbackImage = new Image
        {
            Aspect = Aspect.AspectFill,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            IsVisible = false
        };
        _startupCoverImage = new Image
        {
            Aspect = Aspect.AspectFill,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            IsVisible = false,
            Opacity = 0
        };
        _overlay = new GraphicsView
        {
            Drawable = _overlayDrawable,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true,
            IsVisible = false
        };
        ApplyOverlayVisual();
        _mediaSpinner = new ActivityIndicator
        {
            Color = Colors.White,
            IsRunning = false,
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        // Solid-color backdrop that sits at z=0 inside _mediaHost.
        // The native MediaElement FrameLayout briefly exposes the Android theme's
        // gray window surface color even when Opacity=0 is set (one-frame native
        // compositing lag). This BoxView is a pure MAUI view with no native surface
        // complexity, so it reliably paints the desired background color every frame.
        _backdrop = new BoxView
        {
            Color = IntroBackdropColor,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        _mediaHost = new Grid();
        _mediaHost.Add(_backdrop);            // z=0 — always-painted solid backdrop
        _mediaHost.Add(_fallbackImage);       // z=1
        _mediaHost.Add(_mediaElement);        // z=2
        _mediaHost.Add(_startupCoverImage);   // z=3 — optional first-paint poster
        _mediaHost.Add(_overlay);             // z=4
        _mediaHost.Add(_mediaSpinner);        // z=5
        _logo = new Image
        {
            // No Source here on purpose: the logo is the CONSUMER's brand asset, supplied through
            // LogoSource. This shipped hardcoded to one app's file name, which every other consumer
            // would have rendered as a broken image.
            IsVisible = false,
            Aspect = Aspect.AspectFit,
            WidthRequest = G9Metrics.IntroLogoWidth,
            HeightRequest = G9Metrics.IntroLogoHeight,
            HorizontalOptions = LayoutOptions.Center,
            Margin = new Thickness(0, G9Metrics.IntroLogoTopMargin, 0, 0)
        };
        _languageButton = new G9SafeIconButton
        {
            IsGhost = true,
            ButtonSize = 40,
            IconSize = 24,
            Icon = G9Glyphs.Language,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            FlowDirection = FlowDirection.LeftToRight
        };
        _headerRow = new Grid
        {
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Thickness(G9Metrics.IntroChromePadding, G9Metrics.IntroHeaderTopPadding,
                G9Metrics.IntroChromePadding, 0),
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            },
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            }
        };
        _headerRow.Add(_languageButton, 1, 0);
        _headerRow.Add(_logo, 0, 1);
        Grid.SetColumnSpan(_logo, 2);
        _titleLabel = new Label
        {
            FontSize = G9Metrics.IntroTitleFontSize,
            FontAttributes = FontAttributes.Bold,
            TextColor = Colors.White,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap,
            MaxLines = 4
        };
        _subtitleLabel = new Label
        {
            FontSize = G9Metrics.IntroSubtitleFontSize,
            TextColor = Colors.White,
            Opacity = 0.92,
            HorizontalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.WordWrap,
            MaxLines = 5
        };
        _textBlock = new VerticalStackLayout
        {
            Spacing = 12,
            InputTransparent = true,
            IsVisible = false,
            Padding = new Thickness(G9Metrics.IntroChromePadding, 0),
            Margin = new Thickness(0, 0, 0, G9Metrics.IntroTextBottomMargin),
            Children = { _titleLabel, _subtitleLabel }
        };
        _swipePassthrough = new BoxView
        {
            Color = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = false
        };
        _nextLabel = CreateNavLabel();
        _prevLabel = CreateNavLabel();
        _nextIcon = CreateNavIcon(G9Glyphs.ChevronForward);
        _prevIcon = CreateNavIcon(G9Glyphs.ChevronBack);
        _nextHost = new HorizontalStackLayout
        {
            Spacing = 6,
            FlowDirection = FlowDirection.LeftToRight,
            VerticalOptions = LayoutOptions.Center,
            Children = { _nextLabel, _nextIcon }
        };
        _prevHost = new HorizontalStackLayout
        {
            Spacing = 6,
            FlowDirection = FlowDirection.LeftToRight,
            VerticalOptions = LayoutOptions.Center,
            Children = { _prevIcon, _prevLabel }
        };
        _nextTapTarget = CreateNavTapTarget(_nextHost, GoNext);
        _prevTapTarget = CreateNavTapTarget(_prevHost, GoPrevious);
        // Hidden until OnApplyVisuals runs — prevents both arrows flashing on first frame.
        _nextTapTarget.IsVisible = false;
        _prevTapTarget.IsVisible = false;
        _stepHost = new HorizontalStackLayout
        {
            Spacing = G9Metrics.IntroStepSpacing,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        _navRow = new Grid
        {
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Thickness(G9Metrics.IntroChromePadding, 8),
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 8
        };
        _navRow.Add(_prevTapTarget, 0, 0);
        _navRow.Add(_stepHost, 1, 0);
        _navRow.Add(_nextTapTarget, 2, 0);
        _signInButton = new G9SafeButton
        {
            ButtonType = G9SafeButtonType.Primary,
            Size = G9ControlSize.Hero,
            HorizontalOptions = LayoutOptions.Fill,
            IsVisible = false,
            Margin = new Thickness(G9Metrics.IntroChromePadding, 0, G9Metrics.IntroChromePadding,
                G9Metrics.IntroContentBottomPadding),
            EnableSafeExecution = false
        };
        _signInButton.Clicked += OnSignInClicked;
        _chrome = new Grid
        {
            ZIndex = 1,
            // Explicit Transparent prevents the Android native GridLayout from inheriting
            // any default background from the Material theme window surface.
            BackgroundColor = Colors.Transparent,
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Auto)
            }
        };
        _chrome.Add(_headerRow, 0, 0);
        _chrome.Add(_swipePassthrough, 0, 1);
        _chrome.Add(_textBlock, 0, 2);
        _chrome.Add(_navRow, 0, 3);
        _chrome.Add(_signInButton, 0, 4);
        _root = new Grid { BackgroundColor = IntroBackdropColor };
        _root.Add(_mediaHost);
        _root.Add(_chrome);

        AttachSwipePanGesture(_swipePassthrough);

        HandlerChanging += (_, e) =>
        {
            if (e.NewHandler is null)
            {
                // Cancel pending media work so no async callback touches the handler
                // after it is gone.
                _mediaCts?.Cancel();
                _startupCoverCts?.Cancel();
                StopMedia();
                // DO NOT call _mediaElement.Handler?.DisconnectHandler() here.
                //
                // In .NET MAUI 10, G9IntroCarousel.HandlerChanging fires BEFORE
                // WindowOverlay.DeinitializePlatformDependencies() runs. Calling
                // DisconnectHandler() here disposes the native MauiMediaElement too
                // early; the WindowOverlay then tries to access the already-disposed
                // Java object and throws ObjectDisposedException.
                //
                // Instead we let MAUI's normal lifecycle call DisconnectHandler() at
                // the right time (after WindowOverlay cleanup). ExoPlayer is stopped
                // cleanly in OnMediaElementHandlerChanging, which fires just before
                // MAUI disposes the native view.
            }
        };
        // HandlerChanged fires right after Handler becomes non-null. We use this (not Loaded, which
        // fires asynchronously after layout on Android) to retry media when BeginPresentation was
        // called before the handler was ready.
        HandlerChanged += OnHandlerReady;
        Loaded += OnIntroLoaded;
        Unloaded += (_, _) =>
        {
            StopMedia();
            _mediaCts?.Cancel();
            _mediaCts?.Dispose();
            _mediaCts = null;
            _startupCoverCts?.Cancel();
            _startupCoverCts?.Dispose();
            _startupCoverCts = null;
            _mediaElement.HandlerChanged -= OnMediaElementHandlerReady;
            _mediaElement.HandlerChanging -= OnMediaElementHandlerChanging;
            _mediaElement.PositionChanged -= OnMediaPositionChanged;
        };
        Content = _root;
        // No default slides, deliberately: an unconfigured carousel is EMPTY rather than showing the
        // source app's onboarding content. Set Slides before the control is measured.
        Slides = [];
    }
    protected override void OnApplyVisuals()
    {
        var isRtl = G9Culture.IsRtl;
        FlowDirection = isRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        _languageButton.Command = LanguageCommand;
        _languageButton.TextColor = Colors.White;
        _signInButton.Text = ResolveString("IntroSignIn", "Sign in to your account");
        _signInButton.ButtonType = G9SafeButtonType.Primary;
        _signInButton.Size = G9ControlSize.Hero;
        _nextLabel.Text = ResolveString("IntroNext", "Next");
        _prevLabel.Text = ResolveString("IntroPrevious", "Previous");
        _textBlock.IsVisible = true;
        _signInButton.IsVisible = true;
        _stepHost.FlowDirection = isRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        ApplyNavIconLayout(isRtl);
        ApplySlideTexts();
        ApplyStepIndicator();
        ApplyNavVisibility();
    }
    protected override void OnCultureChangedHook()
    {
        base.OnCultureChangedHook();
        RequestVisualUpdate();
    }
    protected override void OnPaletteChanged()
    {
        base.OnPaletteChanged();
        RequestVisualUpdate();
    }
    protected override void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(BackgroundColor))
        {
            var c = BackgroundColor ?? IntroBackdropColor;
            _root.BackgroundColor = c;
            _mediaHost.BackgroundColor = c;
            _backdrop.Color = c;
            return;
        }
        if (propertyName != nameof(IsVisible))
        {
            return;
        }
        if (!IsVisible)
        {
            _mediaCts?.Cancel();
            _startupCoverCts?.Cancel();
            HideStartupCoverImmediately();
            StopMedia();
        }
        else if (IsLoaded)
        {
            BeginPresentation();
        }
    }
    private void ApplyChromeForCurrentSlide()
    {
        RequestVisualUpdate();
    }

    private void OnOverlayVisualChanged() => ApplyOverlayVisual();

    private void ApplyOverlayVisual()
    {
        var bottomOpacity = Clamp01(OverlayOpacity);
        _overlayDrawable.OverlayColor = OverlayColor;
        _overlayDrawable.BottomOpacity = bottomOpacity;
        _overlayDrawable.TopOpacity = ResolveOverlayTopOpacity(bottomOpacity);
        _overlayDrawable.UseGradient = UseGradientOverlay;
        _overlay.Invalidate();
    }

    private double ResolveOverlayTopOpacity(double bottomOpacity)
    {
        if (!double.IsNaN(OverlayTopOpacity) && OverlayTopOpacity >= 0)
        {
            return Clamp01(OverlayTopOpacity);
        }

        return bottomOpacity * Clamp01(OverlayTopOpacityRatio);
    }

    private void OnStartupCoverChanged()
    {
        if (!IsStartupCoverConfigured())
        {
            HideStartupCoverImmediately();
            return;
        }

        _startupCoverImage.Source = StartupCoverImageSource;
    }

    private bool IsStartupCoverConfigured() =>
        !string.IsNullOrWhiteSpace(StartupCoverImageSource) && StartupCoverDurationMs > 0;

    private void ShowStartupCoverIfNeeded()
    {
        _startupCoverCts?.Cancel();
        _startupCoverCts?.Dispose();
        _startupCoverCts = null;
        _startupCoverDismissed = false;
        _startupCoverMediaReady = false;

        if (!IsStartupCoverConfigured())
        {
            HideStartupCoverImmediately();
            return;
        }

        _startupCoverImage.Source = StartupCoverImageSource;
        _startupCoverImage.Opacity = 1;
        _startupCoverImage.IsVisible = true;
        _startupCoverShownAt = DateTimeOffset.UtcNow;
        UpdateMediaLayerVisibility();
    }

    private void MarkStartupCoverMediaReady()
    {
        if (_startupCoverDismissed || !_startupCoverImage.IsVisible)
        {
            return;
        }

        _startupCoverMediaReady = true;
        ScheduleStartupCoverDismissalIfReady();
    }

    private void ScheduleStartupCoverDismissalIfReady()
    {
        if (!_startupCoverMediaReady || _startupCoverDismissed || !_startupCoverImage.IsVisible)
        {
            return;
        }

        _startupCoverCts?.Cancel();
        _startupCoverCts?.Dispose();
        _startupCoverCts = new CancellationTokenSource();
        var token = _startupCoverCts.Token;
        var elapsedMs = (int)Math.Max(0, (DateTimeOffset.UtcNow - _startupCoverShownAt).TotalMilliseconds);
        var remainingMs = Math.Max(0, StartupCoverDurationMs - elapsedMs);

        Dispatcher.Dispatch(async () =>
        {
            try
            {
                if (remainingMs > 0)
                {
                    await Task.Delay(remainingMs, token).ConfigureAwait(true);
                }

                if (token.IsCancellationRequested || Handler is null || !_startupCoverImage.IsVisible)
                {
                    return;
                }

                var fadeMs = Math.Max(0, StartupCoverFadeDurationMs);
                if (fadeMs > 0)
                {
                    await _startupCoverImage.FadeToAsync(0, (uint)fadeMs, Easing.CubicOut).ConfigureAwait(true);
                }
                else
                {
                    _startupCoverImage.Opacity = 0;
                }

                if (token.IsCancellationRequested)
                {
                    return;
                }

                _startupCoverDismissed = true;
                _startupCoverImage.IsVisible = false;
                UpdateMediaLayerVisibility();
            }
            catch (OperationCanceledException)
            {
                // Expected when a slide/media reload or page disappear cancels the cover.
            }
        });
    }

    private void HideStartupCoverImmediately()
    {
        _startupCoverCts?.Cancel();
        _startupCoverDismissed = true;
        _startupCoverMediaReady = false;
        _startupCoverImage.Opacity = 0;
        _startupCoverImage.IsVisible = false;
        UpdateMediaLayerVisibility();
    }

    private bool ShouldApplyInitialVideoSeek(int targetIndex) =>
        !_initialVideoSeekApplied && targetIndex == 0 && InitialVideoStartSeconds > 0;

    private async Task RevealAfterInitialVideoSeekAsync(int targetIndex, CancellationToken cancellationToken)
    {
        var seekTo = TimeSpan.FromSeconds(Math.Max(0, InitialVideoStartSeconds));

        try
        {
            var seekTask = MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (cancellationToken.IsCancellationRequested
                    || _mediaElement.Handler is null
                    || targetIndex != CurrentIndex)
                {
                    return;
                }

                using var seekCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                seekCts.CancelAfter(G9Metrics.IntroInitialVideoSeekTimeoutMs);
                await _mediaElement.SeekTo(seekTo, seekCts.Token).ConfigureAwait(true);
            });

            _ = seekTask.ContinueWith(static t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

            var timeoutTask = Task.Delay(G9Metrics.IntroInitialVideoSeekTimeoutMs, cancellationToken);
            await Task.WhenAny(seekTask, timeoutTask).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Best-effort. The player is already playing, so a seek failure must not keep
            // the intro black.
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested
                || _mediaElement.Handler is null
                || targetIndex != CurrentIndex)
            {
                return;
            }

            _ = RevealMediaLayerAsync(targetIndex, isVideo: true, cancellationToken);
        }).ConfigureAwait(false);
    }

    private bool ShouldSkipInitialFadeIn(int targetIndex)
    {
        if (targetIndex != 0 || _firstSlideInitialFadeHandled)
        {
            return false;
        }

        _firstSlideInitialFadeHandled = true;
        return SkipFirstSlideInitialFadeIn;
    }

    private int FadeInDurationMs => Math.Max(0, MediaFadeInDurationMs);

    private int FadeOutDurationMs => Math.Max(0, MediaFadeOutDurationMs);

    private bool HasVisibleMediaLayer() =>
        _mediaElement.Opacity > 0 || (_fallbackImage.IsVisible && _fallbackImage.Opacity > 0);

    private async Task FadeVisibleMediaOutForTransitionAsync(CancellationToken cancellationToken)
    {
        if (!UseMediaFadeTransitions || FadeOutDurationMs <= 0 || !HasVisibleMediaLayer())
        {
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (cancellationToken.IsCancellationRequested || Handler is null)
            {
                return;
            }

            var fadeTasks = new List<Task>(2);
            if (_mediaElement.Opacity > 0)
            {
                fadeTasks.Add(FadeElementToAsync(_mediaElement, 0, FadeOutDurationMs, Easing.CubicIn, cancellationToken));
            }

            if (_fallbackImage.IsVisible && _fallbackImage.Opacity > 0)
            {
                fadeTasks.Add(FadeElementToAsync(_fallbackImage, 0, FadeOutDurationMs, Easing.CubicIn, cancellationToken));
            }

            if (fadeTasks.Count > 0)
            {
                await Task.WhenAll(fadeTasks).ConfigureAwait(true);
            }

            UpdateMediaLayerVisibility();
        }).ConfigureAwait(false);
    }

    private async Task FadeElementToAsync(
        VisualElement element,
        double opacity,
        int durationMs,
        Easing easing,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        element.AbortAnimation("FadeTo");

        if (durationMs <= 0)
        {
            element.Opacity = opacity;
            return;
        }

        try
        {
            await element.FadeToAsync(opacity, (uint)durationMs, easing).ConfigureAwait(true);
        }
        catch
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                element.Opacity = opacity;
            }
        }
    }

    private static double Clamp01(double value) =>
        double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 1);

    private void OnLogoSourceChanged()
    {
        _logo.Source = LogoSource;
        _logo.IsVisible = LogoSource is not null;
    }

    private void OnSlidesChanged()
    {
        _scheduledMediaIndex = -1;
        _loadedMediaIndex = -1;
        _loadedMediaIsVideo = false;
        _isMediaLoadActive = false;
        _firstSlideInitialFadeHandled = false;
        _videoLoopEndFadeStarted = false;
        _lastVideoPosition = TimeSpan.Zero;
        // Empty is a legitimate state — the consumer has not supplied slides yet (or is loading them).
        // The chrome renders with no media rather than substituting content the package invented.
        _resolvedSlides = Slides is { Count: > 0 } list ? list.ToList() : [];
        ClampIndex();
        ApplyChromeForCurrentSlide();
        ScheduleMediaForCurrentSlideDeferred();
    }
    private void OnIndexChanged()
    {
        ClampIndex();
        ApplyChromeForCurrentSlide();
        ScheduleMediaForCurrentSlideDeferred();
        _ = PreloadAdjacentVideosAsync();
    }
    /// <summary>Starts intro media/chrome after login is on screen (post-bootstrap).</summary>
    public void BeginPresentation()
    {
        if (_presentationStarted)
        {
            RequestVisualUpdate();
            return;
        }

        _presentationStarted = true;
        ShowStartupCoverIfNeeded();
        RequestVisualUpdate();
        if (_mediaElement.Handler is not null)
        {
            ScheduleMediaForCurrentSlideDeferred();
            _ = PreloadAdjacentVideosAsync();
        }
        // OnMediaElementHandlerReady fires when _mediaElement gets its handler — handles the
        // common case where the handler isn't ready yet at the time BeginPresentation is called.
    }

    // HandlerChanged on the ContentView itself never fires on Android (ContentView has no native
    // view). We handle startup via OnMediaElementHandlerReady (media element's own HandlerChanged).
    private void OnHandlerReady(object? sender, EventArgs e) { }

    // Fired by _mediaElement.HandlerChanged — reliable signal that the native player is ready.
    private void OnMediaElementHandlerReady(object? sender, EventArgs e)
    {
        if (_mediaElement.Handler is null)
        {
            return;
        }

#if ANDROID
        if (_mediaElement.Handler.PlatformView is Android.Views.ViewGroup mauiMediaEl)
        {
            // Force the outer MauiMediaElement FrameLayout to transparent so the MAUI
            // BackgroundColor=Transparent request is guaranteed at the native layer too.
            mauiMediaEl.SetBackgroundColor(Android.Graphics.Color.Transparent);

            // Paint ExoPlayer's shutter view black.
            //
            // ExoPlayer's PlayerView contains an "AspectRatioFrameLayout" whose child [1]
            // is a plain View that acts as the "shutter" — an opaque overlay shown BEFORE
            // the first decoded video frame reaches the TextureView surface. Because TextureView
            // is a hardware-composited layer it renders ABOVE all sibling Views at the GPU
            // level, but only once a frame is available. During the window between
            // OnMediaOpened (player ready) and the first GPU-composited frame the shutter is
            // visible. Its default color is WHITE (#FFFFFF), which the user sees as a flash.
            // Setting it to black makes it invisible against our black backdrop.
            //
            // Native path: MauiMediaElement → RelativeLayout[0] → PlayerView[0]
            //              → AspectRatioFrameLayout[0] → View[1] (shutter)
            FixExoPlayerShutterColor(mauiMediaEl);
        }
#endif

        if (_presentationStarted)
        {
            // BeginPresentation ran before the media element handler was ready — start now.
            ScheduleMediaForCurrentSlideDeferred();
            _ = PreloadAdjacentVideosAsync();
        }
    }

    /// <summary>
    /// Proactively stops media and disconnects the MediaElement's native handler.
    /// Must be called from the host page's <c>OnDisappearing</c> override, which fires
    /// during the Android fragment's <c>onPause</c> — BEFORE <c>onDestroyView</c>.
    ///
    /// Background: on <c>window.Page = newPage</c> navigation MAUI tears down the old
    /// fragment's view hierarchy (<c>onDestroyView</c> → auto-DisconnectHandler) BEFORE
    /// the new fragment's <c>onViewCreated</c> triggers
    /// <c>WindowOverlay.DeinitializePlatformDependencies()</c>.
    /// If the native <c>MauiMediaElement</c> is disposed inside <c>onDestroyView</c>,
    /// the subsequent WindowOverlay cleanup calls <c>RemoveView</c> on the already-disposed
    /// Java object and crashes with <c>ObjectDisposedException</c>.
    ///
    /// Calling <c>DisconnectHandler()</c> here — while the fragment is still alive — lets
    /// CTMAUI properly unregister the <c>MauiMediaElement</c> from the WindowOverlay's
    /// internal tracking list. The <c>onViewCreated</c> cleanup then finds nothing to remove.
    /// </summary>
    public void StopAndReleaseMedia()
    {
        _mediaCts?.Cancel();
        StopMedia();

        // WHY the pre-deinitialize step is required (MAUI 10 / CTMAUI 9.x):
        //
        // CTMAUI 9.x adds MauiMediaElement (a CoordinatorLayout) as the FIRST child of the
        // root CoordinatorLayout so ExoPlayer renders at full z-order.
        // WindowOverlay.Initialize() calls rootManager.RootView.GetFirstChildOfType<ViewGroup>()
        // which returns MauiMediaElement, then adds its PlatformGraphicsView inside it
        // (_nativeLayer = MauiMediaElement, _graphicsView = transparent overlay canvas).
        //
        // When window.Page swaps (login → main):
        //   1. Old fragment onPause  → Disappearing fires → this method runs
        //   2. Old fragment onDestroyView → MAUI auto-DisconnectHandler → MauiMediaElement disposed
        //   3. New fragment onViewCreated → WindowHandler.OnRootViewChanged
        //      → VisualDiagnosticsOverlay.Deinitialize()
        //      → _nativeLayer.RemoveView(_graphicsView)   ← _nativeLayer = disposed MauiMediaElement
        //      → ObjectDisposedException  💥
        //
        // Fix: call VisualDiagnosticsOverlay.Deinitialize() HERE (step 1, MauiMediaElement still
        // alive) so _graphicsView is cleanly removed and IsPlatformViewInitialized → false.
        // In step 3, OnRootViewChanged sees IsPlatformViewInitialized=false, skips Deinitialize(),
        // and just calls Initialize() for the new page → no crash.
        var diagnosticsOverlay =
            (Application.Current?.Windows.FirstOrDefault() as Microsoft.Maui.IWindow)
            ?.VisualDiagnosticsOverlay;
        if (diagnosticsOverlay?.IsPlatformViewInitialized == true)
        {
            try { diagnosticsOverlay.Deinitialize(); } catch { }
        }

        try
        {
            _mediaElement.Handler?.DisconnectHandler();
        }
        catch
        {
            // Best-effort; handler may already be disconnected.
        }
    }

    // Fired by _mediaElement.HandlerChanging when MAUI is about to call DisconnectHandler()
    // on the MediaElement (disposing the native MauiMediaElement FrameLayout). We stop
    // ExoPlayer here so the surface is released before the native view is torn down.
    // This is a safety-net: StopAndReleaseMedia() should have already run from the host
    // page's OnDisappearing, so these calls are usually no-ops caught in try-catch.
    private void OnMediaElementHandlerChanging(object? sender, HandlerChangingEventArgs e)
    {
        if (e.NewHandler is not null)
            return;

        try { _mediaElement.Stop(); } catch { }
        try { _mediaElement.Source = null; } catch { }
    }

    private void OnIntroLoaded(object? sender, EventArgs e)
    {
        if (_presentationStarted)
        {
            ScheduleMediaForCurrentSlideDeferred();
        }
    }

    private async Task PreloadAdjacentVideosAsync()
    {
        if (_resolvedSlides.Count == 0)
        {
            return;
        }

        var indices = new HashSet<int> { CurrentIndex };
        if (CurrentIndex + 1 < _resolvedSlides.Count)
        {
            indices.Add(CurrentIndex + 1);
        }

        if (CurrentIndex - 1 >= 0)
        {
            indices.Add(CurrentIndex - 1);
        }

        try
        {
            var paths = indices
                .Select(i => _resolvedSlides[i].VideoAssetPath)
                .Where(static p => !string.IsNullOrWhiteSpace(p));
            await G9IntroMediaResolver.PreloadAllAsync(paths).ConfigureAwait(false);
        }
        catch
        {
            // Best-effort cache warm-up for current / neighbor slides only.
        }
    }

    private void OnMediaFailed(object? sender, MediaFailedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => _ = ShowImageFallbackAsync());
    }

    private void OnMediaPositionChanged(object? sender, MediaPositionChangedEventArgs e)
    {
        if (!UseMediaFadeTransitions
            || !UseVideoLoopFade
            || !_loadedMediaIsVideo
            || _mediaElement.Source is null
            || FadeOutDurationMs <= 0)
        {
            _lastVideoPosition = e.Position;
            return;
        }

        var duration = _mediaElement.Duration;
        if (duration <= TimeSpan.Zero || e.Position < TimeSpan.Zero || e.Position >= duration)
        {
            _lastVideoPosition = e.Position;
            return;
        }

        var loopRestarted = _lastVideoPosition > TimeSpan.Zero
                            && e.Position + TimeSpan.FromMilliseconds(300) < _lastVideoPosition;
        _lastVideoPosition = e.Position;

        if (loopRestarted || e.Position <= TimeSpan.FromMilliseconds(250))
        {
            var shouldFadeInAfterLoop = _videoLoopEndFadeStarted || loopRestarted;
            _videoLoopEndFadeStarted = false;
            if (shouldFadeInAfterLoop && _mediaElement.Opacity < 1 && !_mediaSpinner.IsVisible)
            {
                _ = FadeElementToAsync(
                    _mediaElement,
                    1,
                    FadeInDurationMs,
                    Easing.CubicOut,
                    _mediaCts?.Token ?? CancellationToken.None);
            }

            return;
        }

        if (_videoLoopEndFadeStarted)
        {
            return;
        }

        var remaining = duration - e.Position;
        if (remaining <= TimeSpan.FromMilliseconds(FadeOutDurationMs))
        {
            _videoLoopEndFadeStarted = true;
            _ = FadeElementToAsync(
                _mediaElement,
                0,
                FadeOutDurationMs,
                Easing.CubicIn,
                _mediaCts?.Token ?? CancellationToken.None);
        }
    }

    private void OnMediaOpened(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var targetIndex = _loadedMediaIndex;
            if (_mediaElement.Handler is null || targetIndex != CurrentIndex)
            {
                return;
            }

            if (ShouldApplyInitialVideoSeek(targetIndex))
            {
                _initialVideoSeekApplied = true;
                var token = _mediaCts?.Token ?? CancellationToken.None;
                _ = RevealAfterInitialVideoSeekAsync(targetIndex, token);
                return;
            }

            _ = RevealMediaLayerAsync(targetIndex, isVideo: true, _mediaCts?.Token ?? CancellationToken.None);
        });
    }

    private async Task RevealMediaLayerAsync(int targetIndex, bool isVideo, CancellationToken cancellationToken)
    {
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (cancellationToken.IsCancellationRequested
                || Handler is null
                || targetIndex != CurrentIndex)
            {
                return;
            }

            var target = isVideo ? (VisualElement)_mediaElement : _fallbackImage;
            if (!isVideo)
            {
                _fallbackImage.IsVisible = true;
            }

            _overlay.IsVisible = true;
            SetMediaLoading(false);
            MarkStartupCoverMediaReady();
            RaiseFirstContentReadyOnce();

            var isFirstSlideInitialReveal = targetIndex == 0 && !_firstSlideInitialFadeHandled;
            var skipFade = !UseMediaFadeTransitions
                           || FadeInDurationMs <= 0
                           || ShouldSkipInitialFadeIn(targetIndex);

            if (skipFade)
            {
                target.AbortAnimation("FadeTo");
                target.Opacity = 1;
                UpdateMediaLayerVisibility();
                return;
            }

            if (isFirstSlideInitialReveal)
            {
                _firstSlideInitialFadeHandled = true;
            }

            await FadeElementToAsync(target, 1, FadeInDurationMs, Easing.CubicOut, cancellationToken)
                .ConfigureAwait(true);

            if (!cancellationToken.IsCancellationRequested && targetIndex == CurrentIndex)
            {
                target.Opacity = 1;
                UpdateMediaLayerVisibility();
            }
        }).ConfigureAwait(false);
    }

    private void UpdateMediaLayerVisibility()
    {
        var hasMedia = _mediaElement.Source is not null || _fallbackImage.IsVisible || _startupCoverImage.IsVisible;
        _overlay.IsVisible = hasMedia;
    }

    // Fires FirstContentReady exactly once, on the main thread. Called from every code path that
    // settles the first slide's visual (video reveal, fallback image, or media-with-nothing-to-show)
    // so a host splash can dismiss only after the first frame is up — never on an empty/black player.
    private void RaiseFirstContentReadyOnce()
    {
        if (_firstContentReadyRaised)
        {
            return;
        }

        _firstContentReadyRaised = true;

        if (MainThread.IsMainThread)
        {
            FirstContentReady?.Invoke(this, EventArgs.Empty);
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => FirstContentReady?.Invoke(this, EventArgs.Empty));
    }
    private void OnSignInClicked(object? sender, EventArgs e)
    {
        if (CompleteCommand?.CanExecute(null) == true)
        {
            CompleteCommand.Execute(null);
        }
    }
    private void AttachSwipePanGesture(View view)
    {
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnSwipePanUpdated;
        view.GestureRecognizers.Add(pan);
    }

    private void OnSwipePanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panTotalX = 0;
                break;
            case GestureStatus.Running:
                _panTotalX = e.TotalX;
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                ApplySwipeFromPan(_panTotalX);
                _panTotalX = 0;
                break;
        }
    }

    private void ApplySwipeFromPan(double totalX)
    {
        if (G9Culture.IsRtl)
        {
            totalX = -totalX;
        }

        const double threshold = 48;
        if (totalX <= -threshold)
        {
            GoNext();
        }
        else if (totalX >= threshold)
        {
            GoPrevious();
        }
    }

    private void GoNext()
    {
        if (CurrentIndex < _resolvedSlides.Count - 1)
        {
            CurrentIndex++;
        }
    }
    private void GoPrevious()
    {
        if (CurrentIndex > 0)
        {
            CurrentIndex--;
        }
    }
    private void ClampIndex()
    {
        if (_resolvedSlides.Count == 0)
        {
            CurrentIndex = 0;
            return;
        }
        if (CurrentIndex < 0)
        {
            CurrentIndex = 0;
        }
        else if (CurrentIndex >= _resolvedSlides.Count)
        {
            CurrentIndex = _resolvedSlides.Count - 1;
        }
    }
    private void ApplySlideTexts()
    {
        if (_resolvedSlides.Count == 0 || CurrentIndex < 0 || CurrentIndex >= _resolvedSlides.Count)
        {
            _titleLabel.Text = string.Empty;
            _subtitleLabel.Text = string.Empty;
            return;
        }
        var slide = _resolvedSlides[CurrentIndex];
        _titleLabel.Text = ResolveString(slide.TitleResourceKey, string.Empty);
        _subtitleLabel.Text = ResolveString(slide.SubtitleResourceKey, string.Empty);
    }
    private void ApplyNavVisibility()
    {
        _prevTapTarget.IsVisible = CurrentIndex > 0;
        _prevTapTarget.IsEnabled = CurrentIndex > 0;
        _nextTapTarget.IsVisible = CurrentIndex < _resolvedSlides.Count - 1;
        _nextTapTarget.IsEnabled = CurrentIndex < _resolvedSlides.Count - 1;
    }
    /// <summary>
    ///     In LTR: Previous in column 0 (left), Next in column 2 (right).
    ///     In RTL: positions are swapped — Next in column 0 (left), Previous in column 2 (right) —
    ///     because "next" navigates in the leftward reading direction. Icon/label order within each
    ///     button is unchanged so the arrows stay consistent with their visual meaning.
    /// </summary>
    private void ApplyNavIconLayout(bool isRtl)
    {
        // The icon/label order never changes mid-session — only when the culture direction
        // flips. Skipping the Children.Clear()+re-add on every slide change eliminates the
        // one-frame flash where the nav labels disappear and reappear.
        if (_lastNavIsRtl == isRtl)
        {
            return;
        }

        _lastNavIsRtl = isRtl;
        _nextHost.Children.Clear();
        _prevHost.Children.Clear();
        _nextHost.FlowDirection = FlowDirection.LeftToRight;
        _prevHost.FlowDirection = FlowDirection.LeftToRight;

        if (isRtl)
        {
            // Swap columns: Next moves to the physical left, Previous to the physical right.
            Grid.SetColumn(_nextTapTarget, 0);
            Grid.SetColumn(_prevTapTarget, 2);
            _nextTapTarget.HorizontalOptions = LayoutOptions.Start;
            _prevTapTarget.HorizontalOptions = LayoutOptions.End;
            _nextIcon.Icon = G9Glyphs.ChevronBack;
            _prevIcon.Icon = G9Glyphs.ChevronForward;
            _nextHost.Children.Add(_nextIcon);
            _nextHost.Children.Add(_nextLabel);
            _prevHost.Children.Add(_prevLabel);
            _prevHost.Children.Add(_prevIcon);
        }
        else
        {
            // Default: Previous on left (column 0), Next on right (column 2).
            Grid.SetColumn(_prevTapTarget, 0);
            Grid.SetColumn(_nextTapTarget, 2);
            _prevTapTarget.HorizontalOptions = LayoutOptions.Start;
            _nextTapTarget.HorizontalOptions = LayoutOptions.End;
            _nextIcon.Icon = G9Glyphs.ChevronForward;
            _prevIcon.Icon = G9Glyphs.ChevronBack;
            _nextHost.Children.Add(_nextLabel);
            _nextHost.Children.Add(_nextIcon);
            _prevHost.Children.Add(_prevIcon);
            _prevHost.Children.Add(_prevLabel);
        }
    }
    private void ApplyStepIndicator()
    {
        if (_resolvedSlides.Count == 0)
        {
            _stepHost.Children.Clear();
            _stepPills.Clear();
            return;
        }

        // Rebuild pills only when the slide count changes — avoids destroying and
        // recreating all Border elements on every slide navigation, which caused a
        // one-frame blink as Children.Clear() emptied the row before re-adding pills.
        if (_stepPills.Count != _resolvedSlides.Count)
        {
            _stepHost.Children.Clear();
            _stepPills.Clear();
            for (var i = 0; i < _resolvedSlides.Count; i++)
            {
                var pill = new Border
                {
                    HeightRequest = G9Metrics.IntroStepHeight,
                    WidthRequest = G9Metrics.IntroStepInactiveWidth,
                    BackgroundColor = Color.FromRgba(255, 255, 255, 0.35),
                    StrokeThickness = 0,
                    StrokeShape = new RoundRectangle { CornerRadius = G9Metrics.IntroStepHeight / 2 },
                    VerticalOptions = LayoutOptions.Center
                };
                _stepPills.Add(pill);
                _stepHost.Children.Add(pill);
            }
        }

        // Update only the visual properties (width + color) on the existing pill instances.
        var activeColor = G9Palette.Current.Primary;
        var inactiveColor = Color.FromRgba(255, 255, 255, 0.35);
        for (var i = 0; i < _stepPills.Count; i++)
        {
            var isActive = i == CurrentIndex;
            _stepPills[i].WidthRequest = isActive
                ? G9Metrics.IntroStepActiveWidth
                : G9Metrics.IntroStepInactiveWidth;
            _stepPills[i].BackgroundColor = isActive ? activeColor : inactiveColor;
        }
    }
    private void ScheduleMediaForCurrentSlideDeferred()
    {
        // ContentView has no native handler on Android — gate on the media element's handler instead.
        if (_mediaElement.Handler is null)
        {
            return;
        }

        var targetIndex = CurrentIndex;
        if (_isMediaLoadActive && _scheduledMediaIndex == targetIndex)
        {
            return;
        }

        if (_loadedMediaIndex == targetIndex && (_mediaElement.Source is not null || _fallbackImage.IsVisible))
        {
            return;
        }

        _mediaCts?.Cancel();
        _mediaCts?.Dispose();
        _mediaCts = new CancellationTokenSource();
        var token = _mediaCts.Token;
        _scheduledMediaIndex = targetIndex;
        _isMediaLoadActive = true;

        Dispatcher.Dispatch(async () =>
        {
            await Task.Yield();

            if (token.IsCancellationRequested || Handler is null || targetIndex != CurrentIndex)
            {
                if (_scheduledMediaIndex == targetIndex)
                {
                    _isMediaLoadActive = false;
                }
                return;
            }

            try
            {
                if (_loadedMediaIndex >= 0 && _loadedMediaIndex != targetIndex)
                {
                    await FadeVisibleMediaOutForTransitionAsync(token).ConfigureAwait(false);
                }

                if (token.IsCancellationRequested || Handler is null || targetIndex != CurrentIndex)
                {
                    return;
                }

                await LoadMediaForSlideAsync(targetIndex, token).ConfigureAwait(false);
            }
            finally
            {
                if (_scheduledMediaIndex == targetIndex)
                {
                    _isMediaLoadActive = false;
                }
            }
        });
    }
    private void SetMediaLoading(bool isLoading)
    {
        _mediaSpinner.IsVisible = isLoading;
        _mediaSpinner.IsRunning = isLoading;
    }
    private async Task LoadMediaForSlideAsync(int targetIndex, CancellationToken cancellationToken)
    {
        if (_resolvedSlides.Count == 0 || targetIndex < 0 || targetIndex >= _resolvedSlides.Count)
        {
            return;
        }

        var slide = _resolvedSlides[targetIndex];
        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested
                || _mediaElement.Handler is null
                || targetIndex != CurrentIndex)
            {
                return;
            }

            SetMediaLoading(true);
        });
        if (slide.HasVideo)
        {
            try
            {
                var path = await G9IntroMediaResolver
                    .ResolveVideoFileAsync(slide.VideoAssetPath!, cancellationToken)
                    .ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested || _mediaElement.Handler is null || path is null)
                {
                    await ShowImageFallbackAsync(slide, targetIndex, cancellationToken).ConfigureAwait(false);
                    return;
                }
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested
                        || _mediaElement.Handler is null
                        || targetIndex != CurrentIndex)
                    {
                        return;
                    }
                    _fallbackImage.IsVisible = false;
                    _fallbackImage.Opacity = 0;
                    _mediaElement.Opacity = 0;  // Hidden until OnMediaOpened confirms first frame
                    _loadedMediaIsVideo = true;
                    _videoLoopEndFadeStarted = false;
                    _lastVideoPosition = TimeSpan.Zero;
                    _loadedMediaIndex = targetIndex;
                    UpdateMediaLayerVisibility();
                    _mediaElement.Source = MediaSource.FromFile(path);
                });

                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (cancellationToken.IsCancellationRequested
                        || _mediaElement.Handler is null
                        || targetIndex != CurrentIndex)
                    {
                        return;
                    }
                    _mediaElement.Play();
                });
            }
            catch (Exception)
            {
                await ShowImageFallbackAsync(slide, targetIndex, cancellationToken).ConfigureAwait(false);
            }

            return;
        }

        await ShowImageFallbackAsync(slide, targetIndex, cancellationToken).ConfigureAwait(false);
    }
    private Task ShowImageFallbackAsync(
        G9IntroSlideItem? slide = null,
        int targetIndex = -1,
        CancellationToken cancellationToken = default)
    {
        targetIndex = targetIndex >= 0 ? targetIndex : CurrentIndex;
        slide ??= _resolvedSlides.Count > targetIndex && targetIndex >= 0
            ? _resolvedSlides[targetIndex]
            : null;
        return MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (cancellationToken.IsCancellationRequested
                || _mediaElement.Handler is null
                || targetIndex != CurrentIndex)
            {
                return;
            }
            StopMedia();
            SetMediaLoading(false);

            if (slide is not null && !string.IsNullOrWhiteSpace(slide.ImageSource))
            {
                _fallbackImage.Source = slide.ImageSource;
                _fallbackImage.Opacity = 0;
                _fallbackImage.IsVisible = true;
                _loadedMediaIndex = targetIndex;
                _loadedMediaIsVideo = false;
                _mediaElement.Opacity = 0;
                UpdateMediaLayerVisibility();
                _ = RevealMediaLayerAsync(targetIndex, isVideo: false, cancellationToken);
                return;
            }

            _fallbackImage.IsVisible = false;
            _mediaElement.Opacity = 0;
            _loadedMediaIsVideo = false;
            UpdateMediaLayerVisibility();
            MarkStartupCoverMediaReady();
            RaiseFirstContentReadyOnce();
        });
    }

    private void StopMedia()
    {
        try
        {
            _scheduledMediaIndex = -1;
            _loadedMediaIndex = -1;
            _loadedMediaIsVideo = false;
            _isMediaLoadActive = false;
            _videoLoopEndFadeStarted = false;
            _lastVideoPosition = TimeSpan.Zero;
            _mediaElement.AbortAnimation("FadeTo");
            _fallbackImage.AbortAnimation("FadeTo");
            _mediaElement.Stop();
            _mediaElement.Source = null;
            _mediaElement.Opacity = 0;
            _fallbackImage.Opacity = 0;
            UpdateMediaLayerVisibility();
        }
        catch
        {
            // Handler may already be torn down.
        }
    }
    private static Border CreateNavTapTarget(Layout layout, Action onTapped)
    {
        var target = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            MinimumHeightRequest = G9Metrics.IntroNavMinTouchHeight,
            MinimumWidthRequest = G9Metrics.IntroNavMinTouchWidth,
            Padding = new Thickness(12, 8),
            VerticalOptions = LayoutOptions.Center,
            Content = layout
        };
        var recognizer = new TapGestureRecognizer();
        recognizer.Tapped += (_, _) => onTapped();
        target.GestureRecognizers.Add(recognizer);
        return target;
    }
    private static Label CreateNavLabel() =>
        new()
        {
            FontSize = G9Metrics.IntroNavFontSize,
            TextColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        };
    private static G9IconView CreateNavIcon(G9IconSource icon) =>
        new()
        {
            Icon = icon,
            Color = Colors.White,
            Size = 18,
            VerticalOptions = LayoutOptions.Center
        };
    private static string ResolveString(string key, string fallback)
    {
        // G9IntroSlideItem carries RESOURCE KEYS, so the lookup targets the consumer's catalogue, not
        // the suite's. G9Strings.Resolve is the arbitrary-key seam for exactly that; a key with no entry
        // falls back to the literal supplied by the caller.
        return G9Strings.Resolve(key) ?? fallback;
    }

    private sealed class IntroOverlayDrawable : IDrawable
    {
        public Color OverlayColor { get; set; } = Colors.Black;

        public double BottomOpacity { get; set; }

        public double TopOpacity { get; set; }

        public bool UseGradient { get; set; }

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            var bottomOpacity = (float)Math.Clamp(BottomOpacity, 0, 1);
            if (bottomOpacity <= 0 || dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            {
                return;
            }

            canvas.SaveState();

            if (UseGradient)
            {
                var topOpacity = (float)Math.Clamp(TopOpacity, 0, 1);
                var overlayPaint = new LinearGradientPaint
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1),
                    GradientStops =
                    [
                        new PaintGradientStop(0f, OverlayColor.WithAlpha(topOpacity)),
                        new PaintGradientStop(1f, OverlayColor.WithAlpha(bottomOpacity))
                    ]
                };

                canvas.SetFillPaint(overlayPaint, dirtyRect);
            }
            else
            {
                canvas.FillColor = OverlayColor.WithAlpha(bottomOpacity);
            }

            canvas.FillRectangle(dirtyRect);
            canvas.RestoreState();
        }
    }

#if ANDROID
    // ExoPlayer shutter fix — paints the shutter view black so it is invisible against
    // the black backdrop and never shows the white flash before the first video frame.
    //
    // ExoPlayer's PlayerView contains an "AspectRatioFrameLayout" whose child [1] is a
    // plain View acting as the "shutter". TextureView (hardware-composited GPU layer) only
    // renders ABOVE the shutter once a decoded frame is available. Between OnMediaOpened
    // and the first GPU-composited frame the shutter is exposed; its default color is
    // WHITE (#FFFFFF). Setting it to black eliminates the flash.
    //
    // Native path: MauiMediaElement → RelativeLayout[0] → PlayerView[0]
    //              → AspectRatioFrameLayout[0] → View[1] (shutter)
    private static void FixExoPlayerShutterColor(Android.Views.ViewGroup mauiMediaEl)
    {
        try
        {
            if (mauiMediaEl.GetChildAt(0) is not Android.Views.ViewGroup relLayout) return;
            if (relLayout.GetChildAt(0) is not Android.Views.ViewGroup playerView) return;
            if (playerView.GetChildAt(0) is not Android.Views.ViewGroup aspectRatio) return;

            for (int i = 0; i < aspectRatio.ChildCount; i++)
            {
                var child = aspectRatio.GetChildAt(i);
                // The shutter is a PLAIN View (not a ViewGroup, not an ImageView, etc.).
                if (child != null && child.GetType() == typeof(Android.Views.View))
                {
                    child.SetBackgroundColor(Android.Graphics.Color.Black);
                    return;
                }
            }
        }
        catch
        {
            // Best-effort: ExoPlayer internal structure may change across versions.
        }
    }
#endif
}
