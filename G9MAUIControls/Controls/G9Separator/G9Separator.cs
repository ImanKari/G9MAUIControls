using G9MAUIControls.Icons;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
namespace G9MAUIControls.Controls;

/// <summary>
///     Section separator with optional title and icon. The line(s) always sit
///     immediately adjacent to the title with just <see cref="Grid.ColumnSpacing" />
///     of gap between them, regardless of alignment or RTL — no half-width empty
///     gap that the previous 3-column-with-spans layout produced.
///     <para>
///         <b>Layout strategy:</b> column definitions are rebuilt per alignment
///         instead of fixed at <c>Star, Auto, Star</c>. Each alignment picks the
///         minimum set of columns it needs:
///         <list type="bullet">
///             <item>Title-less (no text): a single Star column holding one full-width line.</item>
///             <item><b>Start</b> alignment: <c>Auto, Star</c> — title hugs the
///             start edge at its natural width; line takes the leftover.</item>
///             <item><b>End</b> alignment: <c>Star, Auto</c> — line takes the
///             leading space; title hugs the end edge.</item>
///             <item><b>Center</b> alignment: <c>Star, Auto, Star</c> — equal
///             lines flanking the centered title.</item>
///         </list>
///     </para>
///     <para>
///         <b>RTL handling:</b> the root grid is locked to <see cref="FlowDirection.LeftToRight"/>
///         so column 0 always means physical-left and the column count maps
///         deterministically to the painted result. RTL alignment is achieved by
///         flipping which physical column the title lives in (Start in RTL =
///         physical-right). The title <see cref="Label"/> itself inherits the page's
///         FlowDirection so its glyphs lay out in correct reading order.
///     </para>
///     // TODO (palette step): line/title color recipes will move to G9Palette.
/// </summary>
public partial class G9Separator : G9ControlBase
{
    private readonly Grid _root;
    private readonly BoxView _leadingLine;
    private readonly BoxView _trailingLine;
    private readonly HorizontalStackLayout _titleHost;
    private readonly Label _titleLabel;
    private readonly ContentView _iconHost;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _title;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9SeparatorTitleAlignment _titleAlignment;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _titleColor;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _lineColor;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private double _thickness;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _iconEmoji;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9IconSource? _icon;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _iconPath;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private ImageSource? _iconSource;

    public G9Separator()
    {
        _leadingLine = new BoxView { HeightRequest = 1, VerticalOptions = LayoutOptions.Center };
        _trailingLine = new BoxView { HeightRequest = 1, VerticalOptions = LayoutOptions.Center };

        _iconHost = new ContentView
        {
            VerticalOptions = LayoutOptions.Center,
            IsVisible = false
        };

        _titleLabel = new Label
        {
            FontSize = G9Metrics.SeparatorTitleFontSize,
            FontAttributes = FontAttributes.Bold,
            CharacterSpacing = 1.2,
            TextTransform = TextTransform.Uppercase,
            VerticalTextAlignment = TextAlignment.Center,
            LineBreakMode = LineBreakMode.NoWrap
        };

        _titleHost = new HorizontalStackLayout
        {
            Spacing = 4,
            VerticalOptions = LayoutOptions.Center,
            Children = { _iconHost, _titleLabel }
        };

        _root = new Grid
        {
            ColumnSpacing = 10,
            // Lock to LTR so column 0/1/N always correspond to physical-left/center/right
            // in our painted output. Each rebuild assigns Grid.Column based on the
            // physical column we want the element in. RTL alignment flips which
            // physical column the title goes into; the title text glyphs themselves
            // still respect the page's FlowDirection on the Label.
            FlowDirection = FlowDirection.LeftToRight
        };

        Content = _root;

        TitleAlignment = G9SeparatorTitleAlignment.Auto;
        Thickness = 1;
        Padding = new Thickness(0, 14, 0, 8);
    }

    private void OnVisualChanged() => RequestVisualUpdate();

    /// <inheritdoc />
    /// <remarks>
    ///     <see cref="OnApplyVisuals" /> on this control rebuilds the column
    ///     definitions and re-creates the icon view on every pass — heavy work that
    ///     pure color-only theme switches don't need. Override the lightweight
    ///     palette hook to push the new line / title colors directly onto the cached
    ///     children, skipping the rebuild. The full apply still runs when
    ///     <see cref="Title" /> / <see cref="TitleAlignment" /> / icon properties
    ///     change because those drive the rebuild — see <c>OnVisualChanged</c>.
    /// </remarks>
    protected override void OnPaletteChanged()
    {
        if (Handler is null) return;
        var palette = G9Palette.Current;
        var lineColor = LineColor ?? palette.Divider;
        var titleColor = TitleColor ?? palette.TextTertiary;
        if (_leadingLine.Color != lineColor) _leadingLine.Color = lineColor;
        if (_trailingLine.Color != lineColor) _trailingLine.Color = lineColor;
        if (_titleLabel.TextColor != titleColor) _titleLabel.TextColor = titleColor;
    }

    protected override void OnApplyVisuals()
    {
        var palette = G9Palette.Current;
        var hasTitle = !string.IsNullOrWhiteSpace(Title);
        var lineColor = LineColor ?? palette.Divider;
        var titleColor = TitleColor ?? palette.TextTertiary;

        _leadingLine.HeightRequest = Thickness;
        _trailingLine.HeightRequest = Thickness;
        _leadingLine.Color = lineColor;
        _trailingLine.Color = lineColor;

        _titleHost.IsVisible = hasTitle;
        _titleLabel.Text = Title;
        _titleLabel.TextColor = titleColor;

        var hasIcon = G9IconFactory.HasIcon(IconEmoji, Icon, IconPath, IconSource);
        _iconHost.IsVisible = hasIcon && hasTitle;
        _iconHost.Content = hasIcon
            ? G9IconFactory.Create(IconEmoji, Icon, IconPath, IconSource, titleColor, G9Metrics.SeparatorIconSize)
            : null;

        // The title host owns icon + label as a HorizontalStackLayout. Its child order
        // is fixed (icon, then label) so to make the icon land on the LEADING side of
        // the title in RTL (physical-right of the label, since reading starts there),
        // we flip the host's FlowDirection to RTL when the page is RTL.
        //
        // The ROOT grid stays locked to LTR for column-index determinism (so column 0
        // always means physical-left). These two FlowDirection choices are independent.
        _titleHost.FlowDirection = G9Visuals.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

        // Tear down + rebuild column definitions and child placement. The grid columns
        // are tiny structures so this is cheap, and it lets each alignment use exactly
        // the minimum columns it needs (which is the part that actually fixes the gap
        // — see class summary).
        _root.Children.Clear();
        _root.ColumnDefinitions.Clear();

        if (!hasTitle)
        {
            // No title — paint a single full-width line.
            _root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
            _leadingLine.IsVisible = true;
            _trailingLine.IsVisible = false;
            Grid.SetColumn(_leadingLine, 0);
            _root.Children.Add(_leadingLine);
            return;
        }

        var alignment = ResolveAlignment();
        var isRtl = G9Visuals.IsRtl;

        // For Auto / RTL-flipped reasoning we resolve the EFFECTIVE physical alignment.
        // - Start in LTR = physical-Start (left)
        // - Start in RTL = physical-End (right) — title hugs the reading-start edge.
        // - End in LTR  = physical-End (right)
        // - End in RTL  = physical-Start (left)
        // - Center      = physical-Center always
        var physical = alignment switch
        {
            G9SeparatorTitleAlignment.Center => PhysicalAlignment.Center,
            G9SeparatorTitleAlignment.Start => isRtl ? PhysicalAlignment.End : PhysicalAlignment.Start,
            G9SeparatorTitleAlignment.End => isRtl ? PhysicalAlignment.Start : PhysicalAlignment.End,
            _ => isRtl ? PhysicalAlignment.End : PhysicalAlignment.Start
        };

        switch (physical)
        {
            case PhysicalAlignment.Start:
                // [Title][         Line         ]
                // Title in physical-left Auto column, line fills the rest. The
                // ColumnSpacing (10) is the only gap between the two — exactly what
                // the user wants.
                _root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                _root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                _leadingLine.IsVisible = false;
                _trailingLine.IsVisible = true;
                _titleHost.HorizontalOptions = LayoutOptions.Start;

                Grid.SetColumn(_titleHost, 0);
                Grid.SetColumn(_trailingLine, 1);
                _root.Children.Add(_titleHost);
                _root.Children.Add(_trailingLine);
                break;

            case PhysicalAlignment.End:
                // [         Line         ][Title]
                _root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                _root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                _leadingLine.IsVisible = true;
                _trailingLine.IsVisible = false;
                _titleHost.HorizontalOptions = LayoutOptions.End;

                Grid.SetColumn(_leadingLine, 0);
                Grid.SetColumn(_titleHost, 1);
                _root.Children.Add(_leadingLine);
                _root.Children.Add(_titleHost);
                break;

            case PhysicalAlignment.Center:
                // [ Line ][Title][ Line ]
                _root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                _root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                _root.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
                _leadingLine.IsVisible = true;
                _trailingLine.IsVisible = true;
                _titleHost.HorizontalOptions = LayoutOptions.Center;

                Grid.SetColumn(_leadingLine, 0);
                Grid.SetColumn(_titleHost, 1);
                Grid.SetColumn(_trailingLine, 2);
                _root.Children.Add(_leadingLine);
                _root.Children.Add(_titleHost);
                _root.Children.Add(_trailingLine);
                break;
        }
    }

    private enum PhysicalAlignment
    {
        Start,
        Center,
        End
    }

    private G9SeparatorTitleAlignment ResolveAlignment()
    {
        if (TitleAlignment != G9SeparatorTitleAlignment.Auto) return TitleAlignment;
        // Auto: title hugs the reading-start edge.
        return G9SeparatorTitleAlignment.Start;
    }
}
