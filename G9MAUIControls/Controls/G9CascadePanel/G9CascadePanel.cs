using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using Microsoft.Maui.Controls.Shapes;

using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

/// <summary>
///     A nested / cascading sliding-panel stack — the in-place "drill-down navigation"
///     pattern (Material 3 / iOS push navigation, Foundation "drilldown", the cascading
///     menu / master-detail stack). Unlike the bottom sheet it is <b>not</b> a full-screen
///     overlay: it lives inside whatever parent you place it in (a grid cell, a card, a
///     column) and fills exactly that container. Pushing a nested view slides a fresh
///     panel in <i>on top of</i> the current one; popping slides it back off. Panels stack
///     to arbitrary depth.
///     <list type="bullet">
///         <item>
///             <b>Directional.</b> Nested panels can enter from any edge —
///             <see cref="G9CascadeDirection.LeftToRight" />, <c>RightToLeft</c>,
///             <c>TopToBottom</c>, <c>BottomToTop</c>. <see cref="G9CascadeDirection.Auto" />
///             (the default) drills in the natural reading direction: left→right in LTR,
///             right→left in RTL.
///         </item>
///         <item>
///             <b>Two transitions.</b> <see cref="G9CascadeTransition.Overlay" /> (default)
///             slides the new panel in on top of a stationary base (with an optional depth
///             parallax); <see cref="G9CascadeTransition.Push" /> slides the base out as
///             the new panel slides in (conveyor replace).
///         </item>
///         <item>
///             <b>Content-sized scroll.</b> Each panel wraps its content in a
///             <see cref="ScrollView" />, so a panel taller than the host scrolls while a
///             short one doesn't.
///         </item>
///         <item>
///             <b>Lazy loading (opt-in) for the root AND every nested panel.</b> Pass a
///             <c>Func&lt;View&gt;</c> instead of a built view; the panel slides in
///             immediately showing a spinner and the heavy view is built one tick later
///             then swapped in — mirroring the bottom sheet's deferred-content behaviour.
///         </item>
///         <item>
///             <b>Fixed-vs-animated root.</b> The first (root) view appears with no
///             animation by default (<see cref="AnimateRoot" /> = false); nested panels
///             always animate.
///         </item>
///     </list>
///     <para>
///         Built entirely from MAUI primitives (<see cref="Border" />, <see cref="Grid" />,
///         <see cref="ScrollView" />) + compositor transforms (<c>TranslationX/Y</c>,
///         <c>Opacity</c>) so the slide runs on every platform without per-platform XAML.
///         The translating layer is locked to <see cref="FlowDirection.LeftToRight" /> so
///         the slide math is always in physical pixels (same approach as
///         <see cref="G9TabView" /> / <see cref="G9RangeSlider" />); panel <i>content</i>
///         follows the active culture so text reads correctly in RTL.
///     </para>
///     // TODO (palette step): surface / outline / spinner colors will move to G9Palette tokens.
/// </summary>
public partial class G9CascadePanel : G9ControlBase
{
    private readonly Border _clip;
    private readonly Grid _stack;
    private readonly List<CascadeLevel> _levels = [];

    private bool _rootBuilt;
    private bool _animating;

    /// <summary>The root view shown at depth 0. Replaced live if it changes after load.</summary>
    [AutoBindable(OnChanged = nameof(OnRootContentChanged))] private View? _rootContent;

    /// <summary>Optional title shown in the root panel's built-in header (when <see cref="ShowRootHeader" /> is true).</summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _rootTitle;

    /// <summary>Default entry direction for nested panels. <see cref="G9CascadeDirection.Auto" /> resolves per culture.</summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9CascadeDirection _direction = G9CascadeDirection.Auto;

    /// <summary>
    ///     How a nested panel transitions relative to the panel beneath it.
    ///     <see cref="G9CascadeTransition.Overlay" /> (default) slides the new panel in on
    ///     top of a stationary base; <see cref="G9CascadeTransition.Push" /> slides the
    ///     base out as the new panel slides in (conveyor replace).
    /// </summary>
    [AutoBindable] private G9CascadeTransition _transition = G9CascadeTransition.Overlay;

    /// <summary>When true the root view animates in on first appearance; default false (root is fixed).</summary>
    [AutoBindable] private bool _animateRoot;

    /// <summary>Show the built-in back+title header on nested panels. Default true.</summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _showHeader = true;

    /// <summary>Show the built-in header on the root panel too (usually false — the root has nowhere to go back to).</summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _showRootHeader;

    /// <summary>
    ///     Slide duration in ms for push / pop. Defaults to
    ///     <see cref="G9Metrics.CascadePanelAnimationMs" /> (set in the ctor — a
    ///     const-reference initializer here would NOT become the BindableProperty default).
    /// </summary>
    [AutoBindable] private uint _animationDurationMs;

    /// <summary>
    ///     When true (default) and in <see cref="G9CascadeTransition.Overlay" /> mode the
    ///     panel beneath parallaxes + dims slightly while covered, for an iOS-style depth cue.
    /// </summary>
    [AutoBindable] private bool _enableParallax = true;

    /// <summary>
    ///     Corner radius of the panel surface (clipped on every platform). Defaults to
    ///     <see cref="G9Metrics.RadiusLg" /> (set in the ctor — see note on
    ///     <see cref="AnimationDurationMs" />).
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _cornerRadius;

    /// <summary>
    ///     Optional lazy factory for the root view. When set (and <see cref="RootContent" />
    ///     is null) the root panel shows a spinner and builds this one tick after load —
    ///     the deferred-content path used for heavy root views.
    /// </summary>
    public Func<View>? RootContentFactory { get; set; }

    public G9CascadePanel()
    {
        _stack = new Grid
        {
            // Lock the translating layer to LTR so TranslationX is always physical pixels
            // regardless of culture (same rationale as G9TabView / G9RangeSlider). RTL is
            // handled by resolving Auto → RightToLeft and by setting each panel's CONTENT
            // FlowDirection to the culture below.
            FlowDirection = FlowDirection.LeftToRight,
            IsClippedToBounds = true,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        _clip = new Border
        {
            Padding = 0,
            StrokeThickness = 0,
            Stroke = Colors.Transparent,
            BackgroundColor = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = (float)G9Metrics.RadiusLg },
            Content = _stack
        };

        Content = _clip;

        // Defaults for [AutoBindable] properties whose initializer is a const REFERENCE
        // (not a literal) — the generator can't evaluate those, so the BindableProperty
        // default lands at default(T) (0). Set them here so AnimationDurationMs is a real
        // duration (a 0 here makes every slide snap instantly) and CornerRadius rounds the
        // clip. See G9Controls.md §7.
        AnimationDurationMs = G9Metrics.CascadePanelAnimationMs;
        CornerRadius = G9Metrics.RadiusLg;
    }

    /// <summary>Raised after a nested panel finishes sliding in. Argument is the new depth.</summary>
    public event EventHandler<int>? PanelPushed;

    /// <summary>Raised after a nested panel finishes sliding out. Argument is the new depth.</summary>
    public event EventHandler<int>? PanelPopped;

    /// <summary>Current nesting depth. 0 = only the root panel is showing.</summary>
    public int Depth => Math.Max(0, _levels.Count - 1);

    private void OnVisualChanged() => RequestVisualUpdate();

    private void OnRootContentChanged()
    {
        // Replace the root level's content live without disturbing the nested stack.
        if (_rootBuilt && _levels.Count > 0)
        {
            SetLevelContent(_levels[0], RootContent, null);
        }
        RequestVisualUpdate();
    }

    /// <summary>
    ///     Push a built view as a new nested panel. Slides in from <paramref name="direction" />
    ///     (or <see cref="Direction" /> when null).
    /// </summary>
    public void Push(View content, string? title = null, G9CascadeDirection? direction = null)
        => _ = PushAsync(content, null, title, direction);

    /// <summary>
    ///     Push a lazily-built view as a new nested panel. The panel slides in immediately
    ///     showing a spinner; <paramref name="contentFactory" /> is invoked one tick after
    ///     the slide so heavy view construction never janks the animation.
    /// </summary>
    public void Push(Func<View> contentFactory, string? title = null, G9CascadeDirection? direction = null)
        => _ = PushAsync(null, contentFactory, title, direction);

    /// <summary>Awaitable push; completes when the slide-in animation finishes.</summary>
    public async Task PushAsync(View? content, Func<View>? contentFactory, string? title = null, G9CascadeDirection? direction = null)
    {
        EnsureRootBuilt();
        if (_animating) return;
        _animating = true;
        try
        {
            var resolved = ResolveDirection(direction ?? Direction);
            var under = _levels.Count > 0 ? _levels[^1] : null;

            var level = BuildLevel(resolved, title, showHeaderDefault: ShowHeader, isRoot: false);
            _levels.Add(level);
            _stack.Add(level.Container);

            ApplyLevelColors(level);
            // Built content shows now; a factory parks the spinner and is built AFTER the
            // slide settles so heavy view construction never janks the animation.
            SetLevelContent(level, content, contentFactory);

            // Wait until the freshly-added container is actually arranged (its platform
            // handler is connected and it has a non-zero size). Only then does the
            // off-screen start offset set in AnimateInAsync "stick" — setting it before the
            // handler exists is silently dropped, leaving the panel to snap into place with
            // no motion (G9Controls.md §15 W2 — never animate an un-arranged view).
            await WaitForArrangedAsync(level.Container).ConfigureAwait(true);

            var (w, h) = ResolveSize(level.Container);
            await AnimateInAsync(level, under, resolved, w, h).ConfigureAwait(true);

            BuildDeferredContent(level);
            PanelPushed?.Invoke(this, Depth);
        }
        finally
        {
            _animating = false;
        }
    }

    /// <summary>Pop the top nested panel. No-op at the root.</summary>
    public void Pop() => _ = PopAsync();

    /// <summary>Awaitable pop; completes when the slide-out animation finishes.</summary>
    public async Task PopAsync()
    {
        if (_animating) return;
        if (_levels.Count <= 1) return; // never pop the root
        _animating = true;
        try
        {
            var top = _levels[^1];
            var under = _levels[^2];

            var (w, h) = ResolveSize(top.Container);
            await AnimateOutAsync(top, under, top.Direction, w, h).ConfigureAwait(true);

            _stack.Remove(top.Container);
            top.ContentHost.Content = null;
            _levels.RemoveAt(_levels.Count - 1);

            PanelPopped?.Invoke(this, Depth);
        }
        finally
        {
            _animating = false;
        }
    }

    /// <summary>Pop every nested panel back down to the root, one slide at a time.</summary>
    public async Task PopToRootAsync()
    {
        while (_levels.Count > 1 && !_animating)
        {
            await PopAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Synchronous fire-and-forget <see cref="PopToRootAsync" />.</summary>
    public void PopToRoot() => _ = PopToRootAsync();

    protected override void OnApplyVisuals()
    {
        EnsureRootBuilt();

        _clip.StrokeShape = new RoundRectangle { CornerRadius = (float)CornerRadius };
        Opacity = IsEnabled ? 1 : 0.5;

        foreach (var level in _levels)
        {
            ApplyLevelColors(level);
            ApplyLevelHeader(level);
        }
    }

    /// <inheritdoc />
    protected override void OnPaletteChanged()
    {
        if (Handler is null) return;
        foreach (var level in _levels)
        {
            ApplyLevelColors(level);
        }
    }

    /// <inheritdoc />
    protected override void OnCultureChangedHook()
    {
        // Re-skin headers (chevron direction + content FlowDirection) for the new culture.
        foreach (var level in _levels)
        {
            level.ContentHost.FlowDirection = CultureFlow();
            level.HeaderHost.FlowDirection = CultureFlow();
        }
        RequestVisualUpdate();
    }

    private void EnsureRootBuilt()
    {
        if (_rootBuilt) return;
        _rootBuilt = true;

        var root = BuildLevel(ResolveDirection(Direction), RootTitle, showHeaderDefault: ShowRootHeader, isRoot: true);
        _levels.Add(root);
        _stack.Add(root.Container);
        ApplyLevelColors(root);
        SetLevelContent(root, RootContent, RootContentFactory);

        if (AnimateRoot)
        {
            root.Container.Opacity = 0;
            _ = root.Container.FadeToAsync(1, AnimationDurationMs, Easing.CubicOut);
        }

        // A lazy root builds once the panel is arranged so the spinner shows until the heavy
        // root view is ready (mirrors nested lazy loading).
        if (root.PendingFactory is not null)
        {
            _ = BuildRootDeferredAsync(root);
        }
    }

    private async Task BuildRootDeferredAsync(CascadeLevel root)
    {
        await WaitForArrangedAsync(root.Container).ConfigureAwait(true);
        BuildDeferredContent(root);
    }

    private CascadeLevel BuildLevel(G9CascadeDirection direction, string? title, bool showHeaderDefault, bool isRoot)
    {
        var spinner = new ActivityIndicator
        {
            IsVisible = false,
            IsRunning = false,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        var contentHost = new ContentView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            FlowDirection = CultureFlow()
        };

        var scroll = new ScrollView
        {
            Orientation = ScrollOrientation.Vertical,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Content = contentHost
        };

        // Header (row 0) — back chevron + title. Built once; visibility toggled per level.
        var backIcon = new ContentView { VerticalOptions = LayoutOptions.Center, HorizontalOptions = LayoutOptions.Center };
        var backButton = new Border
        {
            Padding = 0,
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            WidthRequest = G9Metrics.CascadePanelHeaderHeight - 8,
            HeightRequest = G9Metrics.CascadePanelHeaderHeight - 8,
            VerticalOptions = LayoutOptions.Center,
            Content = backIcon
        };
        var backTap = new TapGestureRecognizer();
        backTap.Tapped += (_, _) => Pop();
        backButton.GestureRecognizers.Add(backTap);

        var titleLabel = new Label
        {
            FontSize = G9Metrics.CascadePanelHeaderFontSize,
            FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

        var headerDivider = new BoxView { HeightRequest = 1, HorizontalOptions = LayoutOptions.Fill, VerticalOptions = LayoutOptions.End };

        var headerRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            },
            ColumnSpacing = 4,
            Padding = new Thickness(G9Metrics.CascadePanelHeaderPadding, 0),
            HeightRequest = G9Metrics.CascadePanelHeaderHeight,
            FlowDirection = CultureFlow()
        };
        headerRow.Add(backButton, 0);
        headerRow.Add(titleLabel, 1);
        headerRow.Add(headerDivider);
        Grid.SetColumnSpan(headerDivider, 2);

        var headerHost = new ContentView
        {
            Content = headerRow,
            FlowDirection = CultureFlow()
        };

        var inner = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition(GridLength.Auto),
                new RowDefinition(GridLength.Star)
            }
        };
        inner.Add(headerHost, 0, 0);
        inner.Add(scroll, 0, 1);
        inner.Add(spinner, 0, 1);

        // Opaque surface so panels fully occlude whatever is beneath during a slide.
        var card = new Border
        {
            Padding = 0,
            StrokeThickness = 0,
            Stroke = Colors.Transparent,
            StrokeShape = new RoundRectangle { CornerRadius = 0 },
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Content = inner
        };

        var container = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Children = { card }
        };

        var level = new CascadeLevel
        {
            Container = container,
            Card = card,
            HeaderHost = headerHost,
            BackIconHost = backIcon,
            TitleLabel = titleLabel,
            HeaderDivider = headerDivider,
            Scroll = scroll,
            ContentHost = contentHost,
            Spinner = spinner,
            Direction = direction,
            Title = title,
            ShowHeader = showHeaderDefault,
            IsRoot = isRoot
        };

        ApplyLevelHeader(level);
        return level;
    }

    private void ApplyLevelHeader(CascadeLevel level)
    {
        level.HeaderHost.IsVisible = level.ShowHeader && (!level.IsRoot || ShowRootHeader);
        // The root level's header (if shown) has no back affordance — there's nowhere to go.
        level.BackIconHost.IsVisible = !level.IsRoot;
        level.TitleLabel.Text = level.Title ?? string.Empty;
    }

    private void ApplyLevelColors(CascadeLevel level)
    {
        var palette = G9Palette.Current;

        level.Card.BackgroundColor = palette.Surface;
        level.TitleLabel.TextColor = palette.TextPrimary;
        level.HeaderDivider.Color = palette.OutlineVariant.WithAlpha(0.6f);
        level.Spinner.Color = palette.Primary;

        if (!level.IsRoot)
        {
            // Culture-aware back chevron (points to the leading edge — "back").
            var icon = G9Visuals.IsRtl ? G9Glyphs.ChevronForward : G9Glyphs.ChevronBack;
            level.BackIconHost.Content = G9IconFactory.Create(
                null, icon, null, null, palette.Primary, G9Metrics.CascadePanelBackIconSize);
        }
    }

    private void SetLevelContent(CascadeLevel level, View? content, Func<View>? factory)
    {
        if (content is not null)
        {
            level.PendingFactory = null;
            ShowContent(level, content);
            return;
        }

        if (factory is not null)
        {
            // Park the spinner now; the heavy build runs in BuildDeferredContent AFTER the
            // slide settles so view construction never janks the animation.
            level.PendingFactory = factory;
            level.ContentHost.Content = null;
            level.Scroll.IsVisible = false;
            level.Spinner.IsVisible = true;
            level.Spinner.IsRunning = true;
            return;
        }

        // No content and no factory — empty panel.
        level.PendingFactory = null;
        ShowContent(level, null);
    }

    /// <summary>Run a level's deferred factory (if any) on the next dispatcher tick, after the slide settled.</summary>
    private void BuildDeferredContent(CascadeLevel level)
    {
        var factory = level.PendingFactory;
        if (factory is null) return;
        level.PendingFactory = null;

        Dispatcher.Dispatch(() =>
        {
            if (Handler is null) return; // torn down mid-flight
            try
            {
                ShowContent(level, factory());
            }
            catch
            {
                // A failed factory leaves the spinner; caller can retry by re-pushing.
            }
        });
    }

    private static void ShowContent(CascadeLevel level, View? content)
    {
        level.Spinner.IsVisible = false;
        level.Spinner.IsRunning = false;
        level.Scroll.IsVisible = true;
        level.ContentHost.Content = content;
    }

    private Task AnimateInAsync(CascadeLevel incoming, CascadeLevel? under, G9CascadeDirection direction, double w, double h)
    {
        var (startX, startY) = StartOffset(direction, w, h);

        // Park the incoming panel off-screen. Set AFTER the container is arranged (see
        // PushAsync) so the platform layer exists and the offset sticks.
        incoming.Container.TranslationX = startX;
        incoming.Container.TranslationY = startY;

        // Where the base panel travels during the push:
        //   • Push    → base slides fully OUT in the same direction (conveyor).
        //   • Overlay → base stays put, with an optional small parallax + dim.
        double underToX = 0, underToY = 0, underToOpacity = 1;
        if (under is not null)
        {
            if (Transition == G9CascadeTransition.Push)
            {
                (underToX, underToY) = PushUnderOffset(direction, w, h);
            }
            else if (EnableParallax)
            {
                (underToX, underToY) = ParallaxOffset(direction, w, h);
                underToOpacity = G9Metrics.CascadePanelUnderlayDimOpacity;
            }
        }

        var underFromX = under?.Container.TranslationX ?? 0;
        var underFromY = under?.Container.TranslationY ?? 0;
        var underFromOpacity = under?.Container.Opacity ?? 1;

        var tcs = new TaskCompletionSource();
        var anim = new Animation();

        // Incoming panel: off-screen → rest (0,0).
        anim.Add(0, 1, new Animation(v => incoming.Container.TranslationX = Lerp(startX, 0, v)));
        anim.Add(0, 1, new Animation(v => incoming.Container.TranslationY = Lerp(startY, 0, v)));

        if (under is not null)
        {
            anim.Add(0, 1, new Animation(v => under.Container.TranslationX = Lerp(underFromX, underToX, v)));
            anim.Add(0, 1, new Animation(v => under.Container.TranslationY = Lerp(underFromY, underToY, v)));
            if (Math.Abs(underToOpacity - underFromOpacity) > 0.001)
            {
                anim.Add(0, 1, new Animation(v => under.Container.Opacity = Lerp(underFromOpacity, underToOpacity, v)));
            }
        }

        anim.Commit(
            this,
            $"cascade-in-{incoming.GetHashCode()}",
            length: AnimationDurationMs,
            easing: Easing.CubicOut,
            finished: (_, _) =>
            {
                incoming.Container.TranslationX = 0;
                incoming.Container.TranslationY = 0;
                if (under is not null)
                {
                    under.Container.TranslationX = underToX;
                    under.Container.TranslationY = underToY;
                    under.Container.Opacity = underToOpacity;
                    // In Push mode the base is fully off-screen — hide it so it can't capture
                    // taps or paint behind the rounded corners.
                    under.Container.IsVisible = Transition != G9CascadeTransition.Push;
                }
                tcs.TrySetResult();
            });

        return tcs.Task;
    }

    private Task AnimateOutAsync(CascadeLevel top, CascadeLevel under, G9CascadeDirection direction, double w, double h)
    {
        var (endX, endY) = StartOffset(direction, w, h);

        // The base may have been parked off-screen (Push) or parallaxed/dimmed (Overlay) —
        // bring it back to rest as the top panel exits.
        under.Container.IsVisible = true;
        var underFromX = under.Container.TranslationX;
        var underFromY = under.Container.TranslationY;
        var underFromOpacity = under.Container.Opacity;
        var topFromX = top.Container.TranslationX;
        var topFromY = top.Container.TranslationY;

        var tcs = new TaskCompletionSource();
        var anim = new Animation();

        // Top panel: rest → off-screen.
        anim.Add(0, 1, new Animation(v => top.Container.TranslationX = Lerp(topFromX, endX, v)));
        anim.Add(0, 1, new Animation(v => top.Container.TranslationY = Lerp(topFromY, endY, v)));

        // Base panel: back to rest (0,0) + full opacity.
        anim.Add(0, 1, new Animation(v => under.Container.TranslationX = Lerp(underFromX, 0, v)));
        anim.Add(0, 1, new Animation(v => under.Container.TranslationY = Lerp(underFromY, 0, v)));
        if (Math.Abs(1 - underFromOpacity) > 0.001)
        {
            anim.Add(0, 1, new Animation(v => under.Container.Opacity = Lerp(underFromOpacity, 1, v)));
        }

        anim.Commit(
            this,
            $"cascade-out-{top.GetHashCode()}",
            length: AnimationDurationMs,
            easing: Easing.CubicIn,
            finished: (_, _) =>
            {
                under.Container.TranslationX = 0;
                under.Container.TranslationY = 0;
                under.Container.Opacity = 1;
                tcs.TrySetResult();
            });

        return tcs.Task;
    }

    private static double Lerp(double from, double to, double t) => from + ((to - from) * t);

    /// <summary>Off-screen entry / exit offset for the incoming / outgoing panel.</summary>
    private static (double X, double Y) StartOffset(G9CascadeDirection direction, double w, double h) => direction switch
    {
        G9CascadeDirection.LeftToRight => (-w, 0),
        G9CascadeDirection.RightToLeft => (w, 0),
        G9CascadeDirection.TopToBottom => (0, -h),
        G9CascadeDirection.BottomToTop => (0, h),
        _ => (-w, 0)
    };

    /// <summary>
    ///     In Push transition, the base panel slides fully out of the box in the SAME
    ///     direction the incoming panel travels (exits through the opposite edge).
    /// </summary>
    private static (double X, double Y) PushUnderOffset(G9CascadeDirection direction, double w, double h) => direction switch
    {
        G9CascadeDirection.LeftToRight => (w, 0),
        G9CascadeDirection.RightToLeft => (-w, 0),
        G9CascadeDirection.TopToBottom => (0, h),
        G9CascadeDirection.BottomToTop => (0, -h),
        _ => (w, 0)
    };

    /// <summary>Parallax offset the underneath panel travels in Overlay mode (same direction as the incoming travel).</summary>
    private static (double X, double Y) ParallaxOffset(G9CascadeDirection direction, double w, double h)
    {
        var fx = w * G9Metrics.CascadePanelParallaxFraction;
        var fy = h * G9Metrics.CascadePanelParallaxFraction;
        return direction switch
        {
            G9CascadeDirection.LeftToRight => (fx, 0),
            G9CascadeDirection.RightToLeft => (-fx, 0),
            G9CascadeDirection.TopToBottom => (0, fy),
            G9CascadeDirection.BottomToTop => (0, -fy),
            _ => (fx, 0)
        };
    }

    /// <summary>Resolve the slide distance from a container, falling back to the panel / a default.</summary>
    private (double W, double H) ResolveSize(VisualElement container)
    {
        var w = container.Width > 0 ? container.Width : (Width > 0 ? Width : 300);
        var h = container.Height > 0 ? container.Height : (Height > 0 ? Height : 300);
        return (w, h);
    }

    /// <summary>
    ///     Wait until <paramref name="view" /> is arranged with a non-zero size so the
    ///     platform has a real compositor layer to animate. One-shot
    ///     <see cref="VisualElement.SizeChanged" /> wait (G9Controls.md §15 W2) with a short
    ///     fallback so a collapsed parent never hangs the push.
    /// </summary>
    private static Task WaitForArrangedAsync(VisualElement view)
    {
        if (view.Width > 0 && view.Height > 0) return Task.CompletedTask;

        var tcs = new TaskCompletionSource();

        void Handler(object? sender, EventArgs e)
        {
            if (view.Width <= 0 || view.Height <= 0) return;
            view.SizeChanged -= Handler;
            tcs.TrySetResult();
        }

        view.SizeChanged += Handler;

        view.Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(120), () =>
        {
            view.SizeChanged -= Handler;
            tcs.TrySetResult();
        });

        return tcs.Task;
    }

    private G9CascadeDirection ResolveDirection(G9CascadeDirection direction)
    {
        if (direction != G9CascadeDirection.Auto) return direction;
        return G9Visuals.IsRtl ? G9CascadeDirection.RightToLeft : G9CascadeDirection.LeftToRight;
    }

    private static FlowDirection CultureFlow() =>
        G9Visuals.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    private sealed class CascadeLevel
    {
        public required Grid Container { get; init; }
        public required Border Card { get; init; }
        public required ContentView HeaderHost { get; init; }
        public required ContentView BackIconHost { get; init; }
        public required Label TitleLabel { get; init; }
        public required BoxView HeaderDivider { get; init; }
        public required ScrollView Scroll { get; init; }
        public required ContentView ContentHost { get; init; }
        public required ActivityIndicator Spinner { get; init; }
        public G9CascadeDirection Direction { get; init; }
        public string? Title { get; init; }
        public bool ShowHeader { get; init; }
        public bool IsRoot { get; init; }
        /// <summary>Deferred content factory, built after the slide settles (lazy loading).</summary>
        public Func<View>? PendingFactory { get; set; }
    }
}
