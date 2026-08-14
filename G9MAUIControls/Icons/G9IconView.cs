using G9MAUIControls.Theming;

namespace G9MAUIControls.Icons;

/// <summary>
///     The view that renders a <see cref="G9IconSource" />: a <see cref="Label" /> for a font
///     glyph, a <see cref="GraphicsView" /> for a built-in vector glyph.
///     <para>
///         <b>Why one view type instead of two.</b> Every control's icon host caches a
///         long-lived child and toggles / mutates it rather than detaching and re-attaching one
///         (<c>Controls/G9Controls.md</c> §12a). That contract only holds if the host's child
///         type is stable — a host that swapped a <c>Label</c> for a <c>GraphicsView</c> when
///         the consumer changed icon kind would pay a fresh platform handler, and on Android a
///         freshly-created glyph view needs a frame to rasterize, which paints as a tofu box.
///         Both renderers therefore live inside this one view, and switching kind toggles
///         <see cref="VisualElement.IsVisible" /> on children that both stay attached.
///     </para>
/// </summary>
public sealed class G9IconView : Grid
{
    private readonly Label _glyphLabel;
    private readonly GraphicsView _vectorView;
    private readonly G9GlyphDrawable _drawable = new();

    /// <summary>Backs <see cref="Icon" />.</summary>
    public static readonly BindableProperty IconProperty = BindableProperty.Create(
        nameof(Icon), typeof(G9IconSource), typeof(G9IconView), default(G9IconSource),
        propertyChanged: (b, _, n) => ((G9IconView)b).OnIconChanged((G9IconSource)n));

    /// <summary>Backs <see cref="Color" />.</summary>
    public static readonly BindableProperty ColorProperty = BindableProperty.Create(
        nameof(Color), typeof(Color), typeof(G9IconView), Colors.Black,
        propertyChanged: (b, _, n) => ((G9IconView)b).OnColorChanged(n as Color));

    /// <summary>Backs <see cref="Size" />.</summary>
    public static readonly BindableProperty SizeProperty = BindableProperty.Create(
        nameof(Size), typeof(double), typeof(G9IconView), 20d,
        propertyChanged: (b, _, n) => ((G9IconView)b).OnSizeChanged((double)n));

    /// <summary>Creates an empty icon view. Assign <see cref="Icon" /> to give it something to draw.</summary>
    public G9IconView()
    {
        // Never hit-testable. The finger must always reach the gesture owner on the control
        // root, from anywhere inside the control — see G9Controls.md §10b.
        InputTransparent = true;
        CascadeInputTransparent = true;
        HorizontalOptions = LayoutOptions.Center;
        VerticalOptions = LayoutOptions.Center;

        // ── Both children are pinned LeftToRight, and that is a correctness fix, not a preference. ──
        //
        // An icon is a PICTURE, not text: it must render exactly as authored regardless of the
        // ambient reading direction. Left to inherit, an RTL parent makes the platform mirror the
        // GraphicsView's canvas, so every vector glyph is drawn flipped — a tick leans the wrong way,
        // a magnifier's handle swaps corners, and, worst of all, a DIRECTIONAL glyph is reversed
        // AFTER the caller already chose the correct one for RTL. That double flip is silent and
        // reads as "the arrow points the wrong way" rather than as a mirroring bug.
        //
        // Direction stays the CALLER's decision — `IsRtl ? ChevronBack : ChevronForward` — which is
        // the only place that knows whether a given glyph is directional at all. See LES-0034; the
        // same double-mirror bit the source product's switch tick before the extraction.
        _glyphLabel = new Label
        {
            FlowDirection = FlowDirection.LeftToRight,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            LineBreakMode = LineBreakMode.NoWrap,
            InputTransparent = true,
            IsVisible = false
        };

        _vectorView = new GraphicsView
        {
            FlowDirection = FlowDirection.LeftToRight,
            Drawable = _drawable,
            BackgroundColor = Colors.Transparent,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
            InputTransparent = true,
            IsVisible = false
        };

        Children.Add(_glyphLabel);
        Children.Add(_vectorView);
    }

    /// <summary>The icon to draw.</summary>
    public G9IconSource Icon
    {
        get => (G9IconSource)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>The glyph colour.</summary>
    public Color Color
    {
        get => (Color)GetValue(ColorProperty);
        set => SetValue(ColorProperty, value);
    }

    /// <summary>The glyph's box size in device-independent units.</summary>
    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    private G9IconSource _icon;
    private Color _color = Colors.Black;
    private double _size = 20;

    private void OnIconChanged(G9IconSource icon)
    {
        if (_icon == icon)
        {
            return;
        }

        _icon = icon;
        Apply();
    }

    private void OnColorChanged(Color? color)
    {
        var resolved = color ?? Colors.Black;
        if (_color.Equals(resolved))
        {
            return;
        }

        _color = resolved;
        ApplyColor();
    }

    private void OnSizeChanged(double size)
    {
        if (Math.Abs(_size - size) < 0.01)
        {
            return;
        }

        _size = size;
        Apply();
    }

    /// <summary>
    ///     Stroke weight multiplier for built-in vector glyphs. Ignored for font glyphs, whose
    ///     weight is a property of the font. Raise it for large glyphs on a coloured fill (a
    ///     FAB's plus), lower it for dense rows.
    /// </summary>
    public float VectorWeightScale
    {
        get => _drawable.WeightScale;
        set
        {
            if (Math.Abs(_drawable.WeightScale - value) < 0.001f)
            {
                return;
            }

            _drawable.WeightScale = value;
            _vectorView.Invalidate();
        }
    }

    private void Apply()
    {
        WidthRequest = _size;
        HeightRequest = _size;

        if (_icon.IsBuiltIn)
        {
            _drawable.Glyph = _icon.BuiltIn;
            _drawable.Color = _color;
            _vectorView.WidthRequest = _size;
            _vectorView.HeightRequest = _size;
            _vectorView.IsVisible = true;
            _glyphLabel.IsVisible = false;
            _vectorView.Invalidate();
            return;
        }

        if (_icon.IsEmpty)
        {
            _vectorView.IsVisible = false;
            _glyphLabel.IsVisible = false;
            return;
        }

        _glyphLabel.Text = _icon.Glyph;
        _glyphLabel.FontFamily = _icon.FontFamily;
        _glyphLabel.FontSize = _size;
        _glyphLabel.TextColor = _color;
        _glyphLabel.WidthRequest = _size;
        _glyphLabel.IsVisible = true;
        _vectorView.IsVisible = false;
    }

    private void ApplyColor()
    {
        // Only write to the visible renderer. Touching the dormant one would dirty a view the
        // user cannot see, for no benefit — the same reason UpdateIconColor skips hidden hosts.
        if (_vectorView.IsVisible)
        {
            _drawable.Color = _color;
            _vectorView.Invalidate();
        }
        else if (_glyphLabel.IsVisible)
        {
            _glyphLabel.TextColor = _color;
        }
    }
}

/// <summary>
///     Builds the view for an icon slot, and decides <b>which</b> of the four possible icon
///     inputs a slot is actually showing.
/// </summary>
public static class G9IconFactory
{
    /// <summary>
    ///     Precedence for a slot that has more than one input set: <b>emoji → icon → image</b>.
    ///     Set only one per slot; the order exists so a stray leftover value cannot silently win
    ///     over the one the author meant.
    /// </summary>
    public static bool HasIcon(string? emoji, G9IconSource? icon, string? imagePath, ImageSource? imageSource) =>
        !string.IsNullOrWhiteSpace(emoji)
        || (icon.HasValue && !icon.Value.IsEmpty)
        || !string.IsNullOrWhiteSpace(imagePath)
        || imageSource is not null;

    /// <summary>
    ///     A stable identity for the slot's current content. Control bases compare this between
    ///     visual passes and rebuild the icon host only when it changes — see
    ///     <c>Controls/G9Controls.md</c> §12a.
    /// </summary>
    public static string Signature(string? emoji, G9IconSource? icon, string? imagePath, ImageSource? imageSource)
    {
        if (!string.IsNullOrWhiteSpace(emoji))
        {
            return "e:" + emoji;
        }

        if (icon.HasValue && !icon.Value.IsEmpty)
        {
            return "i:" + icon.Value;
        }

        if (!string.IsNullOrWhiteSpace(imagePath))
        {
            return "p:" + imagePath;
        }

        return imageSource is not null ? "s:" + imageSource.GetHashCode().ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
    }

    /// <summary>Resolves a path-or-source pair into a MAUI <see cref="ImageSource" />.</summary>
    public static ImageSource? ResolveImageSource(string? path, ImageSource? source)
    {
        if (source is not null)
        {
            return source;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return Uri.TryCreate(path, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            ? ImageSource.FromUri(uri)
            : ImageSource.FromFile(path);
    }

    /// <summary>
    ///     Builds the view for one icon slot. Returns a zero-size invisible placeholder when the
    ///     slot is empty, so callers can attach the result unconditionally without a null branch
    ///     and without the host collapsing differently between states.
    /// </summary>
    public static View Create(
        string? emoji,
        G9IconSource? icon,
        string? imagePath,
        ImageSource? imageSource,
        Color color,
        double size,
        double imageCornerRadius = 4)
    {
        if (!string.IsNullOrWhiteSpace(emoji))
        {
            // No HeightRequest on purpose: an emoji at a given FontSize renders taller than its
            // em-square (~27dp at FontSize 20), and a fixed height clips its descender.
            return new Label
            {
                Text = emoji,
                FontSize = size,
                LineBreakMode = LineBreakMode.NoWrap,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                WidthRequest = size,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            };
        }

        if (icon.HasValue && !icon.Value.IsEmpty)
        {
            return new G9IconView { Icon = icon.Value, Color = color, Size = size };
        }

        var resolved = ResolveImageSource(imagePath, imageSource);
        if (resolved is not null)
        {
            return new Border
            {
                WidthRequest = size,
                HeightRequest = size,
                StrokeThickness = 0,
                StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle
                {
                    CornerRadius = new CornerRadius(imageCornerRadius)
                },
                BackgroundColor = Colors.Transparent,
                InputTransparent = true,
                Content = G9ImageFactory.Create(resolved, size)
            };
        }

        return new BoxView
        {
            WidthRequest = 0,
            HeightRequest = 0,
            Opacity = 0,
            InputTransparent = true
        };
    }
}

/// <summary>
///     The one place bitmap icons are turned into views, and the seam for plugging in a caching
///     image control.
///     <para>
///         <b>Read this before shipping bitmap icons in a list.</b> The default here is a plain
///         MAUI <see cref="Image" />, which decodes its source <i>on the UI thread on every
///         rebuild</i>, with no cache and an animated platform fade-in. That is fine for a
///         handful of icons and measurably not fine at scale: the same PNG reused across many
///         chips / tabs / cards is re-decoded once per cell, a state change that rebuilds an icon
///         host re-decodes and visibly flashes, and identical sources keep allocating fresh
///         bitmaps because nothing holds them. All three were observed in the app this suite was
///         extracted from.
///     </para>
///     <para>
///         The library will not take a dependency on an image-caching package to fix that for
///         you — which one to use is your call, and it is a heavy native dependency to force on
///         consumers who only ever use font glyphs. Instead, plug yours in once at startup:
///     </para>
///     <example>
///         <code>
///         // e.g. with FFImageLoading.Maui
///         G9ImageFactory.Factory = (source, size) => new CachedImage
///         {
///             Source = source,
///             Aspect = Aspect.AspectFill,
///             WidthRequest = size,
///             HeightRequest = size,
///             CacheType = CacheType.All,      // memory + disk both warm
///             DownsampleToViewSize = true,    // never decode a 2048px source to draw at 22dp
///             BitmapOptimizations = true,
///             FadeAnimationEnabled = false,   // no alpha tween on a button press
///             LoadingDelay = 169,             // a transient state never pays for a decode
///             InputTransparent = true
///         };
///         </code>
///     </example>
/// </summary>
public static class G9ImageFactory
{
    /// <summary>
    ///     Builds the view for a bitmap icon at the given box size. Replace at startup to route
    ///     bitmap icons through a caching image control.
    /// </summary>
    public static Func<ImageSource, double, View> Factory { get; set; } = DefaultFactory;

    /// <summary>Builds a bitmap icon view through the configured <see cref="Factory" />.</summary>
    public static View Create(ImageSource source, double size) => Factory(source, size);

    private static View DefaultFactory(ImageSource source, double size) => new Image
    {
        Source = source,
        Aspect = Aspect.AspectFill,
        WidthRequest = size,
        HeightRequest = size,
        BackgroundColor = Colors.Transparent,
        InputTransparent = true
    };

    /// <summary>Restores the plain-<see cref="Image" /> default.</summary>
    public static void Reset() => Factory = DefaultFactory;
}
