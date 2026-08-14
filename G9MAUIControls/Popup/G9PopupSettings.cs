using G9MAUIControls.Popup;
// G9PopupSettings is the public configuration record passed to G9PopupHelper.ShowG9PopupAsync(...)
// and friends. It maps to G9PopupViewOpenOptions / G9PopupView at the helper layer. Namespace
// follows the folder path (Components.G9Popup).
using G9MAUIControls.Icons;

namespace G9MAUIControls.Popup;

/// <summary>
///     Per-popup configuration overrides. Every field is nullable so callers can set only
///     the bits they care about — the helper merges with <see cref="CreateDefault" /> via
///     <see cref="WithDefaults" /> at presentation time.
/// </summary>
public sealed record G9PopupSettings
{
    /// <summary>Tap on the overlay outside the card requests a close.</summary>
    public bool? CloseOnBackgroundClick { get; init; }

    /// <summary>Hardware/system back closes the popup.</summary>
    public bool? CloseOnBackButton { get; init; }

    /// <summary>Open / close animation duration in ms.</summary>
    public uint? AnimationDuration { get; init; }

    /// <summary>Open / close animation kind.</summary>
    public G9PopupAnimationType? Animation { get; init; }

    /// <summary>Easing curve applied to both open and close motion.</summary>
    public G9PopupViewAnimationEasing? AnimationEasing { get; init; }

    /// <summary>How the card sizes itself relative to its content.</summary>
    public G9PopupViewAutoSizeMode? AutoSizeMode { get; init; }

    /// <summary>Overlay rendering mode (solid scrim or best-effort blur).</summary>
    public G9PopupViewOverlayMode? OverlayMode { get; init; }

    /// <summary>Auto-close after this many ms. <c>0</c> = stays open until dismissed.</summary>
    public int? AutoCloseDuration { get; init; }

    /// <summary>Render the header row (icon badge + title + divider).</summary>
    public bool? ShowHeader { get; init; }

    /// <summary>Render the footer row (action buttons).</summary>
    public bool? ShowFooter { get; init; }

    /// <summary>
    ///     How footer buttons are arranged: <see cref="G9PopupFooterButtonLayout.Row" /> (default,
    ///     equal columns) or <see cref="G9PopupFooterButtonLayout.Stacked" /> (one full-width button
    ///     per row — use for 3+ buttons / long labels).
    /// </summary>
    public G9PopupFooterButtonLayout? FooterButtonLayout { get; init; }

    /// <summary>Render an explicit close button in the header.</summary>
    public bool? ShowCloseButton { get; init; }

    /// <summary>Card width in dp. <c>null</c> = let <see cref="AutoSizeMode" /> decide.</summary>
    public double? Width { get; init; }

    /// <summary>Card height in dp. <c>null</c> = let <see cref="AutoSizeMode" /> decide.</summary>
    public double? Height { get; init; }

    /// <summary>Inner padding around the content area.</summary>
    public Thickness? Padding { get; init; }

    /// <summary>Card corner radius in dp.</summary>
    public double? CornerRadius { get; init; }

    /// <summary>Overlay opacity (0..1) when in <see cref="G9PopupViewOverlayMode.Transparent" /> mode.</summary>
    public float? OverlayOpacity { get; init; }

    /// <summary>Solid overlay color (used directly in <see cref="G9PopupViewOverlayMode.Transparent" /> mode).</summary>
    public Color? OverlayColor { get; init; }

    /// <summary>Card background color override.</summary>
    public Color? CardBackgroundColor { get; init; }

    /// <summary>Card border color override (the outer hairline stroke).</summary>
    public Color? BorderColor { get; init; }

    /// <summary>Title text color override.</summary>
    public Color? TitleColor { get; init; }

    /// <summary>Body message text color override.</summary>
    public Color? MessageColor { get; init; }

    /// <summary>Replaces the type-default header icon while keeping the accent color.</summary>
    public G9IconSource? IconOverride { get; init; }

    /// <summary>Strength of the blur when <see cref="OverlayMode" /> is <see cref="G9PopupViewOverlayMode.Blur" />.</summary>
    public G9PopupViewBlurIntensity? BlurIntensity { get; init; }

    public static G9PopupSettings CreateDefault()
    {
        return new G9PopupSettings
        {
            CloseOnBackgroundClick = false,
            CloseOnBackButton = true,
            AnimationDuration = 240,
            Animation = G9PopupAnimationType.SlideUp,
            AnimationEasing = G9PopupViewAnimationEasing.SinOut,
            OverlayMode = G9PopupViewOverlayMode.Transparent,
            AutoCloseDuration = 0,
            ShowHeader = true,
            ShowFooter = true,
            ShowCloseButton = false,
            FooterButtonLayout = G9PopupFooterButtonLayout.Row,
            AutoSizeMode = G9PopupViewAutoSizeMode.Height,
            Padding = new Thickness(20, 16, 20, 12),
            CornerRadius = 16,
            OverlayOpacity = 0.45f
        };
    }

    public G9PopupSettings WithDefaults(G9PopupSettings defaults)
    {
        return new G9PopupSettings
        {
            CloseOnBackgroundClick = CloseOnBackgroundClick ?? defaults.CloseOnBackgroundClick,
            CloseOnBackButton = CloseOnBackButton ?? defaults.CloseOnBackButton,
            AnimationDuration = AnimationDuration ?? defaults.AnimationDuration,
            Animation = Animation ?? defaults.Animation,
            AnimationEasing = AnimationEasing ?? defaults.AnimationEasing,
            AutoSizeMode = AutoSizeMode ?? defaults.AutoSizeMode,
            OverlayMode = OverlayMode ?? defaults.OverlayMode,
            AutoCloseDuration = AutoCloseDuration ?? defaults.AutoCloseDuration,
            ShowHeader = ShowHeader ?? defaults.ShowHeader,
            ShowFooter = ShowFooter ?? defaults.ShowFooter,
            ShowCloseButton = ShowCloseButton ?? defaults.ShowCloseButton,
            FooterButtonLayout = FooterButtonLayout ?? defaults.FooterButtonLayout,
            Width = Width ?? defaults.Width,
            Height = Height ?? defaults.Height,
            Padding = Padding ?? defaults.Padding,
            CornerRadius = CornerRadius ?? defaults.CornerRadius,
            OverlayOpacity = OverlayOpacity ?? defaults.OverlayOpacity,
            OverlayColor = OverlayColor ?? defaults.OverlayColor,
            CardBackgroundColor = CardBackgroundColor ?? defaults.CardBackgroundColor,
            BorderColor = BorderColor ?? defaults.BorderColor,
            TitleColor = TitleColor ?? defaults.TitleColor,
            MessageColor = MessageColor ?? defaults.MessageColor,
            IconOverride = IconOverride ?? defaults.IconOverride,
            BlurIntensity = BlurIntensity ?? defaults.BlurIntensity
        };
    }
}
