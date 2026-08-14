using G9MAUIControls.Helpers;
using G9MAUIControls.Icons;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using Microsoft.Maui.Controls.Shapes;
using System.Windows.Input;

namespace G9MAUIControls.Controls;

/// <summary>
///     Compact icon-only button with optional badge and async loading state.
///     Designed for toolbar actions (filter, sort, refresh, settings) where a full
///     <see cref="G9Button" /> would be visually heavy. Supports two visual styles:
///     <list type="bullet">
///         <item><description><b>Styled</b> (default) — colored background + shadow, like a mini G9Button.</description></item>
///         <item><description><b>Ghost</b> — transparent background with a subtle border on hover/press only.</description></item>
///     </list>
///     <para>
///         <b>Badge</b>: a small circular indicator (number or dot) overlaid on the
///         top-trailing corner. Typical use: filter button shows a dot when filters are
///         active, notification bell shows unread count.
///     </para>
///     <para>
///         <b>Loading</b>: when <see cref="IsLoading" /> is true, the icon is replaced
///         by a spinner of the same size. The button stays the same width/height to
///         prevent layout jumps. The caller sets <c>IsLoading = true</c> before the
///         async operation and <c>IsLoading = false</c> when it completes.
///     </para>
/// </summary>
public partial class G9IconButton : G9ControlBase
{
    private readonly Border _frame;
    private readonly Grid _rootGrid;
    private readonly G9CornerBadge _badgeOverlay;
    private readonly ContentView _iconHost;
    private readonly ActivityIndicator _spinner;
    private string? _lastIconSig;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _emoji;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9IconSource? _icon;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _imagePath;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private ImageSource? _imageSource;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _iconSize;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _buttonSize;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _isGhost;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9ButtonVariant _variant;

    /// <summary>
    ///     When true the frame is rendered as a perfect circle (corner radius =
    ///     <see cref="ButtonSize" /> / 2) instead of the default rounded square. Use for FAB-style
    ///     map tools and other floating circular icon buttons.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _isCircular;

    /// <summary>
    ///     Minimum TAPPABLE size of the control, independent of the visible frame.
    ///     <para>
    ///         The tap gesture lives on the CONTROL, so the control's bounds — not the icon, not the
    ///         frame's paint — are the hit target. A <see cref="ButtonSize" /> of 42 therefore gives a
    ///         42dp target, under the 44dp accessibility floor (design guide §10), and a tap that lands
    ///         a few dp outside simply falls through to whatever is behind: the user reads that as "the
    ///         button sometimes doesn't work".
    ///     </para>
    ///     <para>
    ///         Setting this grows the control's measured bounds WITHOUT growing the drawn button — the
    ///         frame stays centred inside the larger target. <c>0</c> (the default) keeps the old
    ///         behaviour (hit target == <see cref="ButtonSize" />), so the deliberately tiny inline
    ///         buttons (24–32dp chips over photo thumbnails) are unaffected.
    ///     </para>
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _minimumTouchTarget;

    /// <summary>
    ///     Optional explicit background color that OVERRIDES the <see cref="Variant" />'s resolved
    ///     background. When set, the frame paints this exact color and derives a contrasting
    ///     icon color + a darker border from it. This is the color escape hatch mirroring
    ///     <see cref="G9Button.BaseBackgroundColor" />; the icon tint can still be overridden
    ///     via <see cref="TextColor" />. New code should prefer <see cref="Variant" />.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _baseBackgroundColor;

    /// <summary>
    ///     Optional explicit icon color. When set it wins over the variant- or
    ///     <see cref="BaseBackgroundColor" />-derived icon color.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _textColor;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _isLoading;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _badgeText;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _showBadgeDot;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _badgeColor;

    /// <summary>
    ///     When true (the default) the count badge's text is laid out right-to-left in RTL
    ///     mode, so a value like <c>"99+"</c> renders as <c>"+99"</c>. Set false to keep the
    ///     badge text in its literal left-to-right order regardless of culture.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _mirrorBadgeTextInRtl = true;

    [AutoBindable] private ICommand? _command;
    [AutoBindable] private object? _commandParameter;

    public G9IconButton()
    {
        _iconHost = new ContentView
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        _spinner = new ActivityIndicator
        {
            IsRunning = false,
            IsVisible = false,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true
        };

        // Frame holds icon only. Spinner is added/removed dynamically on loading.
        _rootGrid = new Grid
        {
            InputTransparent = true,
            Children = { _iconHost }
        };

        _frame = new Border
        {
            StrokeThickness = 0,
            Content = _rootGrid,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center
        };

        // Corner notification badge over the frame. Shared geometry (corner-centred for
        // any width, clip-safe on Android, RTL-aware) lives in G9CornerBadge — the same
        // helper G9NavCard uses, so the badge looks and behaves identically across
        // controls. See G9Controls.md §15a.
        _badgeOverlay = new G9CornerBadge(_frame);

        Content = _badgeOverlay.View;

        var tap = new TapGestureRecognizer();
        tap.Tapped += OnTapped;
        GestureRecognizers.Add(tap);

        // Defaults
        IconSize = 20;
        ButtonSize = 40;
        Variant = G9ButtonVariant.Surface;
        IsGhost = false;
    }

    public event EventHandler? Clicked;

    private void OnVisualChanged() => RequestVisualUpdate();

    /// <summary>
    ///     Resolve the effective frame colors, honoring the <see cref="BaseBackgroundColor" /> /
    ///     <see cref="TextColor" /> escape hatches over the named <see cref="Variant" />.
    ///     Ghost style is handled by the caller (transparent frame + secondary icon tint).
    /// </summary>
    private (Color Background, Color Icon, Color Stroke, bool UsesGradient) ResolveColors()
    {
        var variant = G9Visuals.ResolveButtonVariant(Variant);

        if (BaseBackgroundColor is null)
        {
            return (variant.Background, TextColor ?? variant.Text, variant.Stroke, variant.UsesGradient);
        }

        var bg = BaseBackgroundColor;
        if (bg.Alpha <= 0f)
        {
            // Transparent override — transparent surface, no border; icon keeps the explicit
            // TextColor (or the variant text as a sensible fallback).
            return (Colors.Transparent, TextColor ?? variant.Text, Colors.Transparent, false);
        }

        // Solid custom color: derive contrasting icon + a darker border, flat (no gradient).
        return (
            bg,
            TextColor ?? G9ColorHelper.GetContrastingTextColor(bg),
            G9ColorHelper.CreateBorderColor(bg),
            false);
    }

    /// <summary>Corner radius for the frame: a circle when <see cref="IsCircular" />, else RadiusMd.</summary>
    private double FrameRadius => IsCircular
        ? (ButtonSize > 0 ? ButtonSize : 40) / 2
        : G9Metrics.RadiusMd;

    /// <inheritdoc />
    /// <remarks>
    ///     Theme switches only need to update the frame background / stroke colors and
    ///     the icon tint. Avoids the expensive frame size / shape / shadow / spinner
    ///     reconciliation that <see cref="OnApplyVisuals" /> always performs.
    /// </remarks>
    protected override void OnPaletteChanged()
    {
        if (Handler is null) return;
        var palette = G9Palette.Current;
        Color iconColor;
        if (IsGhost)
        {
            _frame.Background = new SolidColorBrush(Colors.Transparent);
            iconColor = TextColor ?? palette.TextSecondary;
        }
        else
        {
            var colors = ResolveColors();
            var enabled = IsEnabled && !IsLoading;
            _frame.Background = G9Colors.BuildSolidOrGradient(colors.Background, colors.UsesGradient && enabled);
            _frame.Stroke = new SolidColorBrush(colors.Stroke);
            iconColor = colors.Icon;
        }
        if (_iconHost.Content is G9IconView mi && mi.Color != iconColor)
        {
            mi.Color = iconColor;
        }
    }

    protected override void OnApplyVisuals()
    {
        var palette = G9Palette.Current;
        var enabled = IsEnabled && !IsLoading;
        var size = ButtonSize > 0 ? ButtonSize : 40;
        var iconSz = IconSize > 0 ? IconSize : 20;

        // Frame sizing
        if (_frame.WidthRequest != size) _frame.WidthRequest = size;
        if (_frame.HeightRequest != size) _frame.HeightRequest = size;
        _frame.StrokeShape = G9Colors.Round(FrameRadius);

        // Hit target. The gesture is on the control, so the control's MEASURED bounds are what the
        // finger must land in — the drawn frame is irrelevant to hit-testing. MinimumTouchTarget lets a
        // caller keep a small button but a finger-sized target: the content (frame + badge) is centred,
        // so the extra area is invisible slop around the circle.
        var touchTarget = MinimumTouchTarget > 0 ? Math.Max(MinimumTouchTarget, size) : size;
        if (MinimumWidthRequest != touchTarget) MinimumWidthRequest = touchTarget;
        if (MinimumHeightRequest != touchTarget) MinimumHeightRequest = touchTarget;

        // Visual style
        if (IsGhost)
        {
            _frame.Background = new SolidColorBrush(Colors.Transparent);
            _frame.StrokeThickness = 0;
        }
        else
        {
            var colors = ResolveColors();
            var hasCustomBg = BaseBackgroundColor is not null;
            _frame.Background = G9Colors.BuildSolidOrGradient(colors.Background, colors.UsesGradient && enabled);
            _frame.StrokeThickness = hasCustomBg
                ? (colors.Stroke.Alpha > 0f ? 1 : 0)
                : (Variant is G9ButtonVariant.Outline or G9ButtonVariant.Text ? 1.5 : 0);
            _frame.Stroke = new SolidColorBrush(colors.Stroke);
            // No drop shadow, ever — the app is shadow-free by policy. See G9Controls.md.
        }

        // Apply loading/disabled opacity only to the frame, NOT the badge.
        // The badge must remain fully visible even during loading.
        var frameOpacity = !IsEnabled ? 0.38 : IsLoading ? 0.7 : 1.0;
        if (_frame.Opacity != frameOpacity)
            _frame.Opacity = frameOpacity;
        // Keep the control itself fully opaque so the badge isn't affected.
        if (Opacity != 1.0)
            Opacity = 1.0;

        // Resolve icon color
        Color iconColor;
        if (IsGhost)
        {
            iconColor = TextColor ?? palette.TextSecondary;
        }
        else
        {
            var colors = ResolveColors();
            iconColor = colors.Icon;
        }

        // Icon: build once, then only toggle visibility + update color.
        // Never destroy/recreate the icon view on loading state changes.
        var hasIcon = G9IconFactory.HasIcon(Emoji, Icon, ImagePath, ImageSource);
        var iconSig = hasIcon ? $"{Emoji}|{Icon}|{ImagePath}|{(ImageSource is null ? "0" : "1")}" : "none";
        if (_lastIconSig != iconSig)
        {
            _lastIconSig = iconSig;
            _iconHost.Content = hasIcon
                ? G9IconFactory.Create(Emoji, Icon, ImagePath, ImageSource, iconColor, iconSz)
                : null;
        }
        else if (hasIcon && _iconHost.Content is not null)
        {
            // Just update color in-place without rebuilding
            switch (_iconHost.Content)
            {
                case G9IconView mi:
                    if (mi.Color != iconColor) mi.Color = iconColor;
                    break;
            }
        }

        // Toggle visibility: icon hidden during loading, shown otherwise
        var iconVisible = !IsLoading && hasIcon;
        if (_iconHost.IsVisible != iconVisible)
            _iconHost.IsVisible = iconVisible;

        // Spinner: add to rootGrid on loading start, remove on loading end (performance)
        if (IsLoading)
        {
            if (!_rootGrid.Children.Contains(_spinner))
                _rootGrid.Children.Add(_spinner);
            _spinner.IsRunning = true;
            _spinner.IsVisible = true;
            _spinner.Color = iconColor;
            _spinner.WidthRequest = iconSz;
            _spinner.HeightRequest = iconSz;
        }
        else
        {
            if (_rootGrid.Children.Contains(_spinner))
            {
                _spinner.IsRunning = false;
                _spinner.IsVisible = false;
                _rootGrid.Children.Remove(_spinner);
            }
        }

        // Badge — delegated to the shared corner-badge helper (corner-centred for any
        // width, clip-safe on Android, RTL-aware). The frame size drives the overhang.
        _badgeOverlay.Update(
            countText: BadgeText,
            showDot: ShowBadgeDot,
            background: BadgeColor ?? palette.Error,
            foreground: Colors.White,
            ringColor: IsGhost ? palette.Background : palette.Surface,
            hostWidth: size,
            hostHeight: size,
            mirrorTextInRtl: MirrorBadgeTextInRtl);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled || IsLoading) return;

        // Invoke handlers SYNCHRONOUSLY first so any state change inside the handler
        // (most commonly IsLoading = true) lands on this same frame as the touch-up.
        // Awaiting the press animation first added ~230 ms between finger-up and the
        // spinner appearing — same bug G9Button had. The press animation is a
        // fire-and-forget tactile cue; it never blocks user-defined click handlers.
        try
        {
            Clicked?.Invoke(this, EventArgs.Empty);

            if (Command is { } cmd && cmd.CanExecute(CommandParameter))
            {
                cmd.Execute(CommandParameter);
            }
        }
        catch
        {
            // UI buttons must never crash from a click handler.
        }

        _ = PlayPressAnimationAsync();
    }

    private async Task PlayPressAnimationAsync()
    {
        try
        {
            await this.ScaleToAsync(0.82, G9Metrics.PressDurationMs, Easing.CubicIn).ConfigureAwait(true);
            await this.ScaleToAsync(1.0, G9Metrics.ReleaseDurationMs, Easing.CubicOut).ConfigureAwait(true);
        }
        catch
        {
            Scale = 1.0;
        }
    }
}
