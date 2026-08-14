using G9MAUIControls.Popup;
using G9MAUIControls.Toast;

namespace G9MAUIControls.Popup;

/// <summary>
///     Per-open options the helper passes to <see cref="G9PopupView" /> for a single Show / Close
///     cycle. Designed as a thin record so the helper layer can build it from
///     <see cref="G9PopupSettings" /> + visuals once and forward without re-allocating.
/// </summary>
public sealed record G9PopupViewOpenOptions
{
    /// <summary>Card width in dp. <c>null</c> = let <see cref="AutoSizeMode" /> decide.</summary>
    public double? Width { get; init; }

    /// <summary>Card height in dp. <c>null</c> = let <see cref="AutoSizeMode" /> decide.</summary>
    public double? Height { get; init; }

    /// <summary>Card corner radius in dp.</summary>
    public double CornerRadius { get; init; } = 16;

    /// <summary>Inner padding around the content area.</summary>
    public Thickness Padding { get; init; } = new(20, 16, 20, 12);

    /// <summary>How the card sizes itself.</summary>
    public G9PopupViewAutoSizeMode AutoSizeMode { get; init; } = G9PopupViewAutoSizeMode.Height;

    /// <summary>Card background color.</summary>
    public Color? CardBackground { get; init; }

    /// <summary>Card border color (for the outer hairline stroke).</summary>
    public Color? BorderColor { get; init; }

    /// <summary>Overlay rendering mode.</summary>
    public G9PopupViewOverlayMode OverlayMode { get; init; } = G9PopupViewOverlayMode.Transparent;

    /// <summary>Strength of the blur when <see cref="OverlayMode" /> is <see cref="G9PopupViewOverlayMode.Blur" />.</summary>
    public G9PopupViewBlurIntensity BlurIntensity { get; init; } = G9PopupViewBlurIntensity.None;

    /// <summary>Solid overlay color (used directly in <see cref="G9PopupViewOverlayMode.Transparent" /> mode).</summary>
    public Color? OverlayColor { get; init; }

    /// <summary>Overlay opacity (0..1) when in <see cref="G9PopupViewOverlayMode.Transparent" /> mode.</summary>
    public double OverlayOpacity { get; init; } = 0.45;

    /// <summary>Whether tapping outside the card requests a close.</summary>
    public bool CloseOnBackgroundTap { get; init; }

    /// <summary>Whether hardware/system back closes the popup.</summary>
    public bool CloseOnBackButton { get; init; } = true;

    /// <summary>Open / close animation kind.</summary>
    public G9PopupAnimationType Animation { get; init; } = G9PopupAnimationType.SlideUp;

    /// <summary>Easing applied to both open and close motion.</summary>
    public G9PopupViewAnimationEasing AnimationEasing { get; init; } = G9PopupViewAnimationEasing.SinOut;

    /// <summary>Open / close animation duration in ms.</summary>
    public uint AnimationDuration { get; init; } = 240;

    /// <summary>Auto-close after this many ms. <c>0</c> = stays open until dismissed.</summary>
    public int AutoCloseDuration { get; init; }

    /// <summary>Anchor view for relative positioning. When <c>null</c>, the card is centered.</summary>
    public View? RelativeView { get; init; }

    /// <summary>Where to place the card relative to <see cref="RelativeView" />.</summary>
    public G9PopupViewRelativePosition? RelativePosition { get; init; }

    /// <summary>Pixel offset from the relative position anchor.</summary>
    public int RelativeAbsoluteX { get; init; }

    /// <summary>Pixel offset from the relative position anchor.</summary>
    public int RelativeAbsoluteY { get; init; }

    /// <summary>Absolute x position when neither <see cref="RelativeView" /> nor centering is wanted.</summary>
    public int? AbsoluteX { get; init; }

    /// <summary>Absolute y position when neither <see cref="RelativeView" /> nor centering is wanted.</summary>
    public int? AbsoluteY { get; init; }

    /// <summary>
    ///     When <c>true</c>, the user can drag the card around the popup host area. By default the
    ///     drag is captured on the entire card; supply <see cref="DragHandle" /> to scope the
    ///     gesture to a specific child (typically a header bar) so taps inside the body still work
    ///     normally. Drag offset resets every time the popup is re-opened so a fresh open always
    ///     centers the card.
    /// </summary>
    public bool IsDraggable { get; init; }

    /// <summary>
    ///     Optional view that captures the drag pan gesture when <see cref="IsDraggable" /> is
    ///     <c>true</c>. When <c>null</c>, the entire card frame captures drags (which would prevent
    ///     interactions with body children). Pass a header bar so the drag region is clearly
    ///     distinct from interactive body content.
    /// </summary>
    public View? DragHandle { get; init; }
}
