using G9MAUIControls.Localization;
using G9MAUIControls.Helpers;
using G9MAUIControls.Icons;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using Microsoft.Maui.Controls.Shapes;
using System.Windows.Input;

namespace G9MAUIControls.Controls;

/// <summary>
///     Material-style navigation list card with a leading icon chip, title, optional
///     subtitle, and a flexible trailing accessory. Mirrors the common list-row patterns
///     from Material 3 list items and the iOS settings list:
///     <list type="bullet">
///         <item>
///             <b>Chevron</b> (<see cref="ShowChevron" />) — the disclosure arrow that
///             signals "tap to navigate to a detail screen". Auto-mirrors to a left chevron
///             under RTL.
///         </item>
///         <item>
///             <b>Value + chevron</b> (<see cref="ValueText" />) — a trailing value label
///             (e.g. a count "12", a state "On", a selected option) rendered before the
///             chevron. This is the canonical "settings row with current value" pattern.
///         </item>
///         <item>
///             <b>Coming soon</b> (<see cref="IsComingSoon" />) — replaces the disclosure
///             arrow/value with a quiet "coming soon" badge for visible but inactive future
///             actions.
///         </item>
///         <item>
///             <b>Icon badge</b> (<see cref="IconBadgeText" /> / <see cref="ShowIconBadgeDot" />)
///             — a small notification indicator overlaid on the top-trailing corner of the
///             leading icon chip: an empty dot for "has updates", or a small circle with a
///             count. Same visual language as <see cref="G9IconButton" />'s badge.
///         </item>
///         <item>
///             <b>Custom trailing</b> (<see cref="TrailingView" />) — any view (a switch, a
///             chip, a spinner) takes over the trailing slot completely.
///         </item>
///     </list>
///     // TODO (palette step): badge / chevron colors will move to G9Palette.
/// </summary>
public partial class G9NavCard : G9ControlBase
{
    private readonly Border _frame;
    private readonly Grid _row;
    private readonly G9CornerBadge _iconBadgeOverlay;
    private readonly Border _iconBadge;
    private readonly ContentView _iconHost;
    private readonly Label _titleLabel;
    private readonly Label _subtitleLabel;
    private readonly VerticalStackLayout _textHost;
    private readonly Label _valueLabel;
    private readonly Label _comingSoonLabel;
    private readonly Border _comingSoonBadge;
    private readonly ContentView _chevronHost;
    private readonly ContentView _customTrailingHost;
    private readonly HorizontalStackLayout _trailingRow;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _title;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _subtitle;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _accentColor;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _iconEmoji;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9IconSource? _icon;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _iconPath;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private ImageSource? _iconSource;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _showChevron;

    /// <summary>
    ///     Trailing value label rendered before the chevron — the "current value" of the
    ///     row (a count like "12", a state like "On", a selected option). Null / empty
    ///     hides it.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _valueText;

    /// <summary>Optional override color for <see cref="ValueText" />. Defaults to the muted tertiary text color.</summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _valueColor;

    /// <summary>
    ///     Replaces the value / chevron accessory with a localized "coming soon" badge. Use this for
    ///     visible future actions that should not navigate or show an unavailable toast yet.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _isComingSoon;

    /// <summary>
    ///     Optional override text for the <see cref="IsComingSoon" /> badge. Defaults to
    ///     <c>G9Strings.Get(G9StringKey.ComingSoon)</c>.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _comingSoonText;

    /// <summary>
    ///     Count / text shown in a small circular badge on the top-trailing corner of the
    ///     leading icon chip (notification-style). Takes precedence over
    ///     <see cref="ShowIconBadgeDot" /> when non-empty.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _iconBadgeText;

    /// <summary>
    ///     When true (and <see cref="IconBadgeText" /> is empty) shows a small empty dot
    ///     badge on the icon chip — the "has updates / unread" indicator.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _showIconBadgeDot;

    /// <summary>Optional override color for the icon badge. Defaults to the Error palette color.</summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _iconBadgeColor;

    /// <summary>
    ///     When true (the default) the icon count badge's text is laid out right-to-left in
    ///     RTL mode, so a value like <c>"99+"</c> renders as <c>"+99"</c>. Set false to keep
    ///     the badge text in its literal left-to-right order regardless of culture.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _mirrorBadgeTextInRtl = true;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private View? _trailingView;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _isDestructive;

    /// <summary>
    ///     When true the WHOLE row background is painted as a soft tint of
    ///     <see cref="CardAccentColor" /> (falling back to <see cref="AccentColor" /> then
    ///     <c>Primary</c>) mixed with the surface — the pastel "colored list row" look. Default
    ///     <c>false</c> keeps the standard surface card background, so existing usages are unchanged.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _useAccentSurface;

    /// <summary>
    ///     Optional accent used for the row-background tint when <see cref="UseAccentSurface" /> is on.
    ///     Defaults to <see cref="AccentColor" /> (then the <c>Primary</c> palette color). Independent of
    ///     the icon-chip accent so the row tint and the icon chip can differ if needed.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _cardAccentColor;

    [AutoBindable] private ICommand? _command;
    [AutoBindable] private object? _commandParameter;

    public G9NavCard()
    {
        _iconHost = new ContentView
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };
        _iconBadge = new Border
        {
            WidthRequest = G9Metrics.NavCardIconBadgeSize,
            HeightRequest = G9Metrics.NavCardIconBadgeSize,
            StrokeThickness = 0,
            StrokeShape = G9Colors.Round(G9Metrics.RadiusMd),
            Content = _iconHost
        };

        // Notification badge whose CENTRE sits on the icon chip's top-trailing corner.
        // Shared geometry (corner-centred for any width, clip-safe on Android, RTL-aware)
        // lives in G9CornerBadge — the same helper G9IconButton uses, so the badge looks
        // and behaves identically across controls. See G9Controls.md §15a.
        _iconBadgeOverlay = new G9CornerBadge(_iconBadge)
        {
            // ctor wraps _iconBadge; nothing else to configure here.
        };

        _titleLabel = new Label
        {
            FontSize = G9Metrics.NavCardTitleFontSize,
            FontAttributes = FontAttributes.Bold,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1
        };
        _subtitleLabel = new Label
        {
            FontSize = G9Metrics.NavCardSubtitleFontSize,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            IsVisible = false
        };

        _textHost = new VerticalStackLayout
        {
            Spacing = 2,
            VerticalOptions = LayoutOptions.Center,
            Children = { _titleLabel, _subtitleLabel }
        };

        // Trailing slot: a horizontal row holding the value label + chevron, OR a single
        // custom trailing view that replaces both.
        _valueLabel = new Label
        {
            FontSize = G9Metrics.NavCardValueFontSize,
            FontAttributes = FontAttributes.Bold,
            VerticalTextAlignment = TextAlignment.Center,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            IsVisible = false
        };
        _comingSoonLabel = new Label
        {
            FontSize = G9Metrics.NavCardComingSoonFontSize,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap,
            MaxLines = 1
        };
        _comingSoonBadge = new Border
        {
            Padding = new Thickness(G9Metrics.NavCardComingSoonPaddingX, G9Metrics.NavCardComingSoonPaddingY),
            StrokeThickness = 0,
            StrokeShape = G9Colors.Round(G9Metrics.NavCardComingSoonRadius),
            VerticalOptions = LayoutOptions.Center,
            Content = _comingSoonLabel,
            IsVisible = false
        };
        _chevronHost = new ContentView { VerticalOptions = LayoutOptions.Center };
        _customTrailingHost = new ContentView { VerticalOptions = LayoutOptions.Center, IsVisible = false };

        _trailingRow = new HorizontalStackLayout
        {
            Spacing = 8,
            VerticalOptions = LayoutOptions.Center,
            Children = { _valueLabel, _comingSoonBadge, _chevronHost }
        };

        _row = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 12
        };
        _row.Add(_iconBadgeOverlay.View, 0);
        _row.Add(_textHost, 1);
        _row.Add(_trailingRow, 2);
        _row.Add(_customTrailingHost, 2);

        _frame = new Border
        {
            Padding = new Thickness(G9Metrics.NavCardPaddingX, G9Metrics.NavCardPaddingY),
            StrokeThickness = 1,
            StrokeShape = G9Colors.Round(G9Metrics.RadiusLg),
            Content = _row
        };

        Content = _frame;

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);

        var pointer = new PointerGestureRecognizer();
        pointer.PointerEntered += OnPointerEntered;
        pointer.PointerExited += OnPointerExited;
        GestureRecognizers.Add(pointer);

        ShowChevron = true;
    }

    public event EventHandler? Tapped;

    private void OnVisualChanged() => RequestVisualUpdate();

    /// <summary>
    ///     Resolves the resting row-background color: a soft accent tint when
    ///     <see cref="UseAccentSurface" /> is on, otherwise the standard surface color.
    /// </summary>
    private Color ResolveCardBackground(G9Palette palette)
    {
        if (!UseAccentSurface)
        {
            return palette.Surface;
        }

        var accent = CardAccentColor ?? AccentColor ?? palette.Primary;
        return G9ColorHelper.Mix(accent, palette.Surface, 0.88);
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Pure-color theme switches push palette colors onto the cached children
    ///     without rebuilding the icon view (which is the most expensive part of
    ///     <see cref="OnApplyVisuals" /> — it allocates a fresh
    ///     <c>G9IconView</c> on every call). Saves ~80–160 ms per card
    ///     on a theme switch when many AppNavCards live on the page.
    /// </remarks>
    protected override void OnPaletteChanged()
    {
        if (Handler is null) return;
        var palette = G9Palette.Current;
        var accent = AccentColor ?? palette.Primary;
        if (_frame.BackgroundColor != ResolveCardBackground(palette)) _frame.BackgroundColor = ResolveCardBackground(palette);
        _frame.Stroke = new SolidColorBrush(palette.OutlineVariant);
        _iconBadge.BackgroundColor = G9ColorHelper.Mix(accent, palette.Surface, 0.82);
        if (_iconHost.Content is G9IconView icon)
        {
            if (icon.Color != accent) icon.Color = accent;
        }
        _titleLabel.TextColor = IsDestructive ? palette.Error : palette.TextPrimary;
        _subtitleLabel.TextColor = palette.TextTertiary;
        _valueLabel.TextColor = ValueColor ?? palette.TextTertiary;
        ApplyComingSoonBadge(palette);
        ApplyIconBadge(palette);
    }

    protected override void OnApplyVisuals()
    {
        var palette = G9Palette.Current;
        var accent = AccentColor ?? palette.Primary;

        Opacity = IsEnabled ? 1 : 0.45;
        _frame.BackgroundColor = ResolveCardBackground(palette);
        _frame.Stroke = new SolidColorBrush(palette.OutlineVariant);

        _iconBadge.BackgroundColor = G9ColorHelper.Mix(accent, palette.Surface, 0.82);
        _iconHost.Content = G9IconFactory.Create(
            IconEmoji, Icon, IconPath, IconSource,
            accent, string.IsNullOrWhiteSpace(IconEmoji) ? 18 : 22);

        ApplyIconBadge(palette);

        _titleLabel.Text = Title ?? string.Empty;
        _titleLabel.TextColor = IsDestructive ? palette.Error : palette.TextPrimary;
        _subtitleLabel.Text = Subtitle ?? string.Empty;
        _subtitleLabel.TextColor = palette.TextTertiary;
        _subtitleLabel.IsVisible = !string.IsNullOrWhiteSpace(Subtitle);

        // Trailing slot. A custom TrailingView takes over the whole slot; otherwise we
        // render the value label (optional) + chevron (optional) row.
        if (TrailingView is not null)
        {
            _customTrailingHost.Content = TrailingView;
            _customTrailingHost.IsVisible = true;
            _trailingRow.IsVisible = false;
        }
        else
        {
            _customTrailingHost.Content = null;
            _customTrailingHost.IsVisible = false;
            _trailingRow.IsVisible = true;

            ApplyComingSoonBadge(palette);

            if (IsComingSoon)
            {
                _valueLabel.Text = string.Empty;
                _valueLabel.IsVisible = false;
                _chevronHost.Content = null;
                _chevronHost.IsVisible = false;
                _comingSoonBadge.IsVisible = true;
            }
            else
            {
                _comingSoonBadge.IsVisible = false;

                _valueLabel.Text = ValueText ?? string.Empty;
                _valueLabel.TextColor = ValueColor ?? (IsDestructive ? palette.Error : palette.TextTertiary);
                _valueLabel.IsVisible = !string.IsNullOrWhiteSpace(ValueText);

                if (ShowChevron)
                {
                    var icon = G9Visuals.IsRtl ? G9Glyphs.ChevronBack : G9Glyphs.ChevronForward;
                    _chevronHost.Content = G9IconFactory.Create(
                        null, icon, null, null, palette.TextTertiary, G9Metrics.NavCardChevronSize);
                    _chevronHost.IsVisible = true;
                }
                else
                {
                    _chevronHost.Content = null;
                    _chevronHost.IsVisible = false;
                }
            }
        }
    }

    private void ApplyComingSoonBadge(G9Palette palette)
    {
        _comingSoonLabel.Text = string.IsNullOrWhiteSpace(ComingSoonText)
            ? G9Strings.Get(G9StringKey.ComingSoon)
            : ComingSoonText;
        _comingSoonLabel.TextColor = palette.Error;
        _comingSoonBadge.BackgroundColor = palette.ErrorContainer;
    }

    /// <summary>
    ///     Update the icon chip's corner notification badge via the shared
    ///     <see cref="G9CornerBadge" /> helper (count pill or empty dot). The helper owns
    ///     the corner-centred / clip-safe / RTL geometry — see <c>G9Controls.md</c> §15a.
    /// </summary>
    private void ApplyIconBadge(G9Palette palette)
    {
        _iconBadgeOverlay.Update(
            countText: IconBadgeText,
            showDot: ShowIconBadgeDot,
            background: IconBadgeColor ?? palette.Error,
            foreground: palette.OnError,
            ringColor: palette.Surface,
            hostWidth: G9Metrics.NavCardIconBadgeSize,
            hostHeight: G9Metrics.NavCardIconBadgeSize,
            mirrorTextInRtl: MirrorBadgeTextInRtl);
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled) return;
        if (IsComingSoon) return;

        try
        {
            await this.ScaleToAsync(0.985, 70, Easing.CubicIn).ConfigureAwait(true);
            await this.ScaleToAsync(1, 120, Easing.CubicOut).ConfigureAwait(true);
        }
        catch
        {
        }

        Tapped?.Invoke(this, EventArgs.Empty);

        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
    }

    private async void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (!IsEnabled) return;
        if (IsComingSoon) return;
        var palette = G9Palette.Current;
        var accent = AccentColor ?? palette.Primary;
        _frame.BackgroundColor = G9ColorHelper.Mix(ResolveCardBackground(palette), accent, 0.04);
        try
        {
            await this.TranslateToAsync(0, -1, G9Metrics.HoverDurationMs, Easing.CubicOut).ConfigureAwait(true);
        }
        catch
        {
        }
    }

    private async void OnPointerExited(object? sender, PointerEventArgs e)
    {
        _frame.BackgroundColor = ResolveCardBackground(G9Palette.Current);
        try
        {
            await this.TranslateToAsync(0, 0, G9Metrics.HoverDurationMs, Easing.CubicOut).ConfigureAwait(true);
        }
        catch
        {
        }
    }
}
