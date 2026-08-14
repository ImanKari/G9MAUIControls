using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

/// <summary>
///     Collapsible section control: a tappable header (leading icon + title + a trailing
///     expand/collapse chevron) over a content region that animates open / closed when the
///     header is tapped. Mirrors the Material 3 / iOS "disclosure group" pattern and the
///     design screenshots used inside the app's bottom sheets.
///     <para>
///         <b>Layout / clipping.</b> The control is NOT wrapped in a content-clipping
///         <see cref="Border" />. A MAUI <see cref="Border" /> clips its content to its
///         rounded <c>StrokeShape</c> even when it is transparent, which would cut off the
///         header icon / title near the rounded corners. Instead the chrome (the optional
///         framed surface) is painted by a CONTENT-LESS background <see cref="Border" /> that
///         sits BEHIND the header+content stack inside a non-clipping <see cref="Grid" /> —
///         the same "background border + sibling overlay" pattern the design system uses for
///         cards that must not clip their content (see <c>08-UI-UX-Design-System.md</c> §4).
///     </para>
///     <para>
///         <b>Expand / collapse animation + height measurement.</b> Opening / closing animates
///         the content host's <c>HeightRequest</c> between 0 and the content's measured natural
///         height (with the host clipped during the animation so it reads as a slide-reveal),
///         while the chevron rotates in lockstep. Crucially, the moment the open animation
///         finishes the height is reset to auto (<c>-1</c>) and clipping is turned off, so the
///         final resting layout is the exact natural height — a hosting fit-to-content bottom
///         sheet settles on the correct size and the content (e.g. a picker's floating label)
///         is never statically clipped. This keeps the animation smooth without leaving an
///         ambiguous fixed height behind it.
///     </para>
///     <para>
///         <b>Universal icon system.</b> The leading icon accepts an emoji
///         (<see cref="IconEmoji" />), a Material glyph (<see cref="Icon" />), or a
///         bitmap (<see cref="IconPath" /> / <see cref="IconSource" />), routed through
///         <see cref="G9IconFactory.Create" />.
///     </para>
///     <para>
///         <b>RTL.</b> The header relies on the ambient <see cref="VisualElement.FlowDirection" />
///         (same approach as <see cref="G9NavCard" />): the icon + title sit on the
///         reading-start edge and the chevron on the reading-end edge, mirroring automatically
///         under a culture flip. The chevron is a vertical up/down glyph, so it needs no
///         horizontal mirroring.
///     </para>
///     // TODO (palette step): header / chevron color recipes will move to G9Palette.
/// </summary>
[Microsoft.Maui.Controls.ContentProperty(nameof(ExpanderContent))]
public partial class G9Expander : G9ControlBase
{
    private const string ContentAnimationName = "G9Expander.Content";

    private readonly Grid _outerGrid;
    private readonly Border _backgroundBorder;
    private readonly VerticalStackLayout _stack;
    private readonly Grid _header;
    private readonly ContentView _iconHost;
    private readonly Label _titleLabel;
    private readonly HorizontalStackLayout _titleHost;
    private readonly ContentView _chevronHost;
    private readonly G9IconView _chevronIcon;
    private readonly ContentView _contentHost;

    /// <summary>Set on the first apply so the initial collapsed/expanded state is seeded
    /// without animation; subsequent toggles run through <see cref="OnIsExpandedChanged" />.</summary>
    private bool _initialized;

    /// <summary>Guards against an <see cref="OnApplyVisuals" /> pass disturbing an in-flight
    /// expand / collapse animation (rotation + height).</summary>
    private bool _animating;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _title;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _titleColor;

    // ── Leading icon (universal icon system) ──
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _iconEmoji;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9IconSource? _icon;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _iconPath;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private ImageSource? _iconSource;
    /// <summary>Tint for the leading icon. Defaults to the primary text color.</summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _iconColor;

    /// <summary>Tint for the trailing expand/collapse chevron. Defaults to the secondary text color.</summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _chevronColor;

    /// <summary>
    ///     Collapsible content shown below the header while expanded. This is the control's
    ///     XAML <c>ContentProperty</c>, so nested XAML content lands here automatically.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnExpanderContentChanged))] private View? _expanderContent;

    /// <summary>
    ///     Whether the expander is open. Two-way bindable so a view model can drive or
    ///     observe the open state. Toggling animates the chevron and the content reveal.
    /// </summary>
    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnIsExpandedChanged))]
    private bool _isExpanded;

    /// <summary>
    ///     When true the whole expander is painted as a rounded surface card (background +
    ///     hairline outline). When false (the default) it renders chrome-free — just the
    ///     header row over the content — so it reads as an inline section, matching the
    ///     in-sheet section headers in the app design.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _showFrame;

    /// <summary>Minimum height of the header row. Defaults to <see cref="G9Metrics.ExpanderHeaderHeight" />.</summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _headerHeight;

    /// <summary>Padding applied inside the revealed content region. Defaults to none.</summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Thickness _contentPadding;

    public G9Expander()
    {
        _iconHost = new ContentView
        {
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        };

        _titleLabel = new Label
        {
            FontSize = G9Metrics.ExpanderTitleFontSize,
            FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };

        // Icon + title travel together on the reading-start edge. The host follows the
        // ambient flow direction so the icon lands on the leading side of the title in RTL.
        _titleHost = new HorizontalStackLayout
        {
            Spacing = G9Metrics.ExpanderHeaderSpacing,
            VerticalOptions = LayoutOptions.Center,
            Children = { _iconHost, _titleLabel }
        };

        // The chevron is built ONCE (never recreated in OnApplyVisuals) so its animated
        // rotation survives theme / culture / property refreshes — only its IconColor is
        // mutated. Recreating it would reset Rotation to 0 and drop a frame (G9Controls.md §12a).
        _chevronIcon = new G9IconView {
            Icon = G9Glyphs.Chevron,
            Size = G9Metrics.ExpanderChevronSize,
            WidthRequest = G9Metrics.ExpanderChevronSize,
            HeightRequest = G9Metrics.ExpanderChevronSize,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };
        _chevronHost = new ContentView
        {
            VerticalOptions = LayoutOptions.Center,
            Content = _chevronIcon
            // AnchorX/Y default to 0.5 — rotation pivots around the glyph centre.
        };

        _header = new Grid
        {
            MinimumHeightRequest = G9Metrics.ExpanderHeaderHeight,
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = G9Metrics.ExpanderHeaderSpacing
        };
        _header.Add(_titleHost, 0);
        _header.Add(_chevronHost, 2);

        _contentHost = new ContentView
        {
            IsVisible = false
        };

        // Header + content stacked. NOT inside a clipping Border — see class remarks.
        _stack = new VerticalStackLayout
        {
            Spacing = 0,
            Children = { _header, _contentHost }
        };

        // Content-less background surface for the framed look. It sits BEHIND the stack and
        // never holds content, so its rounded StrokeShape clips nothing.
        _backgroundBorder = new Border
        {
            StrokeThickness = 0,
            StrokeShape = G9Colors.Round(G9Metrics.RadiusLg),
            BackgroundColor = Colors.Transparent,
            IsVisible = false,
            InputTransparent = true
        };

        _outerGrid = new Grid();
        _outerGrid.Add(_backgroundBorder);
        _outerGrid.Add(_stack);

        Content = _outerGrid;

        // Tap target is the HEADER ONLY — tapping the revealed content must not collapse it.
        var tap = new TapGestureRecognizer();
        tap.Tapped += OnHeaderTapped;
        _header.GestureRecognizers.Add(tap);

        ShowFrame = false;
        HeaderHeight = G9Metrics.ExpanderHeaderHeight;
        ContentPadding = new Thickness(0);

        SemanticProperties.SetDescription(_header, Title);
    }

    /// <summary>Raised after <see cref="IsExpanded" /> changes, carrying the new state.</summary>
    public event EventHandler<bool>? ExpandedChanged;

    /// <summary>Toggle the open / closed state (same effect as tapping the header).</summary>
    public void Toggle() => IsExpanded = !IsExpanded;

    private void OnVisualChanged() => RequestVisualUpdate();

    private void OnExpanderContentChanged()
    {
        _contentHost.Content = ExpanderContent;
        RequestVisualUpdate();
    }

    private void OnIsExpandedChanged()
    {
        ApplyExpansionState(animate: true);
        SemanticProperties.SetHint(_header, IsExpanded ? "Expanded" : "Collapsed");
        ExpandedChanged?.Invoke(this, IsExpanded);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Theme-only switches push the new palette colors onto the cached children without
    ///     rebuilding the leading icon (the only allocation-heavy part of the full pass).
    /// </remarks>
    protected override void OnPaletteChanged()
    {
        if (Handler is null) return;
        ApplyChrome();
        ApplyTextAndChevronColors();
        if (_iconHost.Content is G9IconView icon)
        {
            var iconColor = IconColor ?? G9Palette.Current.TextPrimary;
            if (icon.Color != iconColor) icon.Color = iconColor;
        }
    }

    protected override void OnApplyVisuals()
    {
        ApplyChrome();

        _header.MinimumHeightRequest = HeaderHeight;
        ApplyContentPadding();

        _titleLabel.Text = Title ?? string.Empty;
        _titleHost.FlowDirection = G9Visuals.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        SemanticProperties.SetDescription(_header, Title);

        ApplyTextAndChevronColors();

        // Leading icon (rebuilt only when icon props change — driven by OnVisualChanged).
        var hasIcon = G9IconFactory.HasIcon(IconEmoji, Icon, IconPath, IconSource);
        _iconHost.IsVisible = hasIcon;
        _iconHost.Content = hasIcon
            ? G9IconFactory.Create(
                IconEmoji, Icon, IconPath, IconSource,
                IconColor ?? G9Palette.Current.TextPrimary,
                G9Metrics.ExpanderIconSize)
            : null;

        Opacity = IsEnabled ? 1 : 0.45;

        // Seed the collapsed / expanded state once without animation; later toggles animate
        // through OnIsExpandedChanged. Never disturb an in-flight animation.
        if (!_initialized)
        {
            _initialized = true;
            ApplyExpansionState(animate: false);
        }
        else if (!_animating)
        {
            // Keep the resting state in sync on a plain refresh (e.g. content assigned later)
            // without re-running the animation.
            _contentHost.IsVisible = IsExpanded;
            _contentHost.HeightRequest = -1;
            _contentHost.IsClippedToBounds = false;
            _chevronHost.Rotation = IsExpanded ? 180d : 0d;
        }
    }

    private void ApplyChrome()
    {
        var palette = G9Palette.Current;
        if (ShowFrame)
        {
            _backgroundBorder.IsVisible = true;
            _backgroundBorder.BackgroundColor = palette.Surface;
            _backgroundBorder.Stroke = new SolidColorBrush(palette.OutlineVariant);
            _backgroundBorder.StrokeThickness = 1;
            _stack.Padding = new Thickness(G9Metrics.ExpanderHeaderPaddingX, G9Metrics.NavCardPaddingY);
        }
        else
        {
            _backgroundBorder.IsVisible = false;
            _stack.Padding = new Thickness(0);
        }
    }

    private void ApplyContentPadding()
    {
        // Fold the header→content gap into the content host's top padding so it animates with
        // the reveal (an invisible / zero-height host then shows no leftover gap).
        _contentHost.Padding = new Thickness(
            ContentPadding.Left,
            ContentPadding.Top + G9Metrics.ExpanderContentTopGap,
            ContentPadding.Right,
            ContentPadding.Bottom);
    }

    private void ApplyTextAndChevronColors()
    {
        var palette = G9Palette.Current;
        _titleLabel.TextColor = TitleColor ?? palette.TextPrimary;
        var chevronColor = ChevronColor ?? palette.TextSecondary;
        if (_chevronIcon.Color != chevronColor) _chevronIcon.Color = chevronColor;
    }

    /// <summary>
    ///     Reveal / hide the content and rotate the chevron. When <paramref name="animate" />
    ///     is false (initial seed) the state is applied instantly; otherwise the content height
    ///     animates between 0 and its natural height while the chevron rotates in lockstep.
    /// </summary>
    private void ApplyExpansionState(bool animate)
    {
        var targetRotation = IsExpanded ? 180d : 0d;

        // Stop any in-flight content animation before re-seeding / re-animating.
        this.AbortAnimation(ContentAnimationName);

        if (!animate || Handler is null || _contentHost.Handler is null)
        {
            _animating = false;
            _contentHost.IsClippedToBounds = false;
            _contentHost.HeightRequest = -1;
            _contentHost.IsVisible = IsExpanded;
            _chevronHost.Rotation = targetRotation;
            return;
        }

        _animating = true;

        // Chevron rotation runs on the compositor in parallel (no layout impact).
        _ = AnimateChevronAsync(targetRotation);

        if (IsExpanded)
        {
            AnimateOpen();
        }
        else
        {
            AnimateClose();
        }
    }

    private void AnimateOpen()
    {
        _contentHost.IsClippedToBounds = true;
        _contentHost.IsVisible = true;
        _contentHost.HeightRequest = -1; // ensure natural measure

        var target = MeasureContentHeight();
        if (target <= 0)
        {
            // Couldn't measure (not laid out yet) — show instantly at natural height.
            _contentHost.HeightRequest = -1;
            _contentHost.IsClippedToBounds = false;
            _animating = false;
            return;
        }

        _contentHost.HeightRequest = 0;
        var animation = new Animation(v => _contentHost.HeightRequest = v, 0, target, Easing.CubicOut);
        animation.Commit(
            this,
            ContentAnimationName,
            length: G9Metrics.ExpanderContentAnimationMs,
            finished: (_, cancelled) =>
            {
                if (!cancelled)
                {
                    // Reset to auto so the resting layout is the exact natural height and the
                    // content (e.g. a picker floating label) is no longer statically clipped.
                    _contentHost.HeightRequest = -1;
                    _contentHost.IsClippedToBounds = false;
                }
                _animating = false;
            });
    }

    private void AnimateClose()
    {
        var start = _contentHost.Height > 0 ? _contentHost.Height : MeasureContentHeight();
        if (start <= 0)
        {
            // Nothing measurable — collapse instantly.
            _contentHost.HeightRequest = -1;
            _contentHost.IsClippedToBounds = false;
            _contentHost.IsVisible = false;
            _animating = false;
            return;
        }

        _contentHost.IsClippedToBounds = true;
        _contentHost.HeightRequest = start;
        var animation = new Animation(v => _contentHost.HeightRequest = v, start, 0, Easing.CubicIn);
        animation.Commit(
            this,
            ContentAnimationName,
            length: G9Metrics.ExpanderContentAnimationMs,
            finished: (_, cancelled) =>
            {
                if (!cancelled)
                {
                    _contentHost.IsVisible = false;
                    _contentHost.HeightRequest = -1;
                    _contentHost.IsClippedToBounds = false;
                }
                _animating = false;
            });
    }

    /// <summary>
    ///     Measure the natural height of the content host at the current row width. Uses the
    ///     header width as the constraint (the header is always laid out, the content host may
    ///     not be when collapsed) — both share the same inner width inside the stack.
    /// </summary>
    private double MeasureContentHeight()
    {
        var width = _contentHost.Width;
        if (width <= 0) width = _header.Width;
        if (width <= 0) width = Width;
        if (width <= 0) return 0;

        var measured = ((IView)_contentHost).Measure(width, double.PositiveInfinity);
        return measured.Height;
    }

    private async Task AnimateChevronAsync(double targetRotation)
    {
        try
        {
            await _chevronHost.RotateToAsync(
                targetRotation,
                G9Metrics.ExpanderChevronRotationMs,
                Easing.CubicInOut).ConfigureAwait(true);
        }
        catch
        {
            // Handler torn down mid-animation — ignore.
        }
    }

    private async void OnHeaderTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled) return;

        try
        {
            await _header.ScaleToAsync(0.99, 60, Easing.CubicIn).ConfigureAwait(true);
            await _header.ScaleToAsync(1, 90, Easing.CubicOut).ConfigureAwait(true);
        }
        catch
        {
        }

        IsExpanded = !IsExpanded;
    }
}
