using Microsoft.Maui.Controls.Shapes;

namespace G9MAUIControls.Controls;

/// <summary>
///     Shared notification-style corner badge used by every New-folder control that
///     overlays a small count / dot indicator on an icon (currently
///     <see cref="G9IconButton" /> and <see cref="G9NavCard" />). It owns the badge
///     <see cref="Border" /> + <see cref="Label" /> and the geometry that makes the
///     badge behave correctly on all platforms and cultures:
///     <list type="bullet">
///         <item>
///             <b>Corner-centred for any width.</b> The badge's CENTRE lands exactly on
///             the host's top-trailing corner (half over the host, half outside),
///             regardless of how wide the count text is — a wide "1299" / "+99" straddles
///             the corner symmetrically instead of growing inward across the icon.
///         </item>
///         <item>
///             <b>Never clipped (incl. Android).</b> The host is pinned to the stack's
///             bottom-leading corner and the badge to the top-trailing corner inside a
///             stack sized to <c>host + half-badge</c>, so the whole badge stays within
///             the stack bounds. Negative-margin overflow gets clipped on Android — this
///             approach uses none.
///         </item>
///         <item>
///             <b>RTL-aware.</b> The stack inherits the ambient <see cref="FlowDirection" />,
///             so the badge auto-mirrors to the host's top-leading corner in RTL. The count
///             text itself can also flip RTL (so "99+" reads "+99") via the
///             <c>mirrorTextInRtl</c> flag.
///         </item>
///     </list>
///     <para>
///         <b>Single source of truth.</b> Any change to badge appearance or geometry made
///         here applies to every control that uses it — keep it that way rather than
///         re-implementing the badge per control. (The <see cref="G9TabView" /> badge is
///         intentionally NOT built on this — it uses a different, tab-specific device.)
///     </para>
/// </summary>
internal sealed class G9CornerBadge
{
    /// <summary>Diameter of the empty "dot" badge, in dp.</summary>
    public const double DotSize = 12;

    /// <summary>Height of the count / text pill badge, in dp (also its corner diameter).</summary>
    public const double CountHeight = 18;

    /// <summary>Font size of the count text, in dp.</summary>
    public const double CountFontSize = 10;

    /// <summary>Ring (stroke) thickness around the badge so it reads off the host, in dp.</summary>
    public const double RingThickness = 1.5;

    private readonly Grid _root;
    private readonly Border _badge;
    private readonly Label _label;

    /// <summary>
    ///     Wrap <paramref name="host" /> (the icon chip / button frame) with a corner badge
    ///     overlay. The host is re-anchored to the stack's bottom-leading corner; the badge
    ///     docks to the top-trailing corner. Add <see cref="View" /> to the visual tree in
    ///     place of the bare host.
    /// </summary>
    public G9CornerBadge(View host)
    {
        ArgumentNullException.ThrowIfNull(host);

        host.HorizontalOptions = LayoutOptions.Start;
        host.VerticalOptions = LayoutOptions.End;

        _label = new Label
        {
            FontSize = CountFontSize,
            FontAttributes = FontAttributes.Bold,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Margin = 0,
            LineHeight = 1,
            TextColor = Colors.White,
            InputTransparent = true
        };

        _badge = new Border
        {
            Padding = 0,
            StrokeThickness = RingThickness,
            Stroke = Colors.Transparent,
            IsVisible = false,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.End,
            VerticalOptions = LayoutOptions.Start,
            ZIndex = 100,
            Content = _label
        };

        _root = new Grid
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            Children = { host, _badge }
        };
    }

    /// <summary>The view to place in the visual tree (host + badge overlay).</summary>
    public View View => _root;

    /// <summary>
    ///     Update the badge. The stack is sized to <c>host + half-badge</c> so the badge's
    ///     centre lands on the host's top-trailing corner.
    /// </summary>
    /// <param name="countText">Count / text for a pill badge. Non-empty wins over the dot.</param>
    /// <param name="showDot">Show an empty dot badge when no count text is set.</param>
    /// <param name="background">Badge fill color.</param>
    /// <param name="foreground">Count text color.</param>
    /// <param name="ringColor">Stroke color around the badge (usually the host's backdrop).</param>
    /// <param name="hostWidth">Host width in dp — drives the stack overhang.</param>
    /// <param name="hostHeight">Host height in dp — drives the stack overhang.</param>
    /// <param name="mirrorTextInRtl">When true the count text flows RTL in RTL mode ("99+" → "+99").</param>
    public void Update(
        string? countText,
        bool showDot,
        Color background,
        Color foreground,
        Color ringColor,
        double hostWidth,
        double hostHeight,
        bool mirrorTextInRtl)
    {
        var hasCount = !string.IsNullOrWhiteSpace(countText);
        var show = hasCount || showDot;

        _badge.IsVisible = show;
        if (!show)
        {
            _label.IsVisible = false;
            // Collapse the overhang back to the bare host when no badge is shown.
            _root.WidthRequest = hostWidth;
            _root.HeightRequest = hostHeight;
            return;
        }

        _badge.BackgroundColor = background;
        _badge.Stroke = new SolidColorBrush(ringColor);

        double badgeW, badgeH;
        if (hasCount)
        {
            _label.Text = countText;
            _label.TextColor = foreground;
            _label.IsVisible = true;
            // Mirror the count text in RTL so "99+" reads as "+99" (opt-out via flag).
            _label.FlowDirection = (mirrorTextInRtl && G9Visuals.IsRtl)
                ? FlowDirection.RightToLeft
                : FlowDirection.LeftToRight;
            badgeH = CountHeight;
            badgeW = Math.Max(badgeH, 8 + countText!.Length * 7);
            _badge.StrokeShape = new RoundRectangle { CornerRadius = (float)(badgeH / 2) };
        }
        else
        {
            _label.IsVisible = false;
            badgeW = badgeH = DotSize;
            _badge.StrokeShape = new RoundRectangle { CornerRadius = (float)(DotSize / 2) };
        }

        _badge.WidthRequest = badgeW;
        _badge.HeightRequest = badgeH;

        // Grow the stack by half the badge on the trailing + top edge so the badge's centre
        // sits exactly on the host's top-trailing corner while the whole badge stays inside
        // the stack (no negative margin → no Android clip). The host is pinned bottom-leading,
        // so all the extra room is on the badge side.
        _root.WidthRequest = hostWidth + badgeW / 2;
        _root.HeightRequest = hostHeight + badgeH / 2;
    }
}
