namespace G9MAUIControls.Popup;

/// <summary>
///     How the popup card sizes itself relative to its measured content.
/// </summary>
public enum G9PopupViewAutoSizeMode
{
    /// <summary>The card hugs its content on both axes (default).</summary>
    Both,

    /// <summary>The card uses the configured width but auto-sizes height to content.</summary>
    Height,

    /// <summary>The card uses the configured height but auto-sizes width to content.</summary>
    Width,

    /// <summary>The card uses both configured width and height verbatim.</summary>
    None
}

/// <summary>
///     Controls how the modal overlay (the dim layer behind the card) is rendered. Replaces the
///     vendored Syncfusion <c>G9PopupOverlayMode</c> with the only two cases the app actually used.
/// </summary>
public enum G9PopupViewOverlayMode
{
    /// <summary>Solid color overlay using <c>Settings.OverlayColor</c> at <c>Settings.OverlayOpacity</c>.</summary>
    Transparent,

    /// <summary>Best-effort blur overlay (falls back to a solid scrim on platforms without compositor blur).</summary>
    Blur,

    /// <summary>
    ///     No overlay scrim. The popup is non-modal — taps on empty areas around the card pass
    ///     straight through to the page content underneath, so the page stays interactive while
    ///     the popup is open. Used by floating tool palettes (e.g. the in-app developer overlay).
    /// </summary>
    None
}

/// <summary>
///     Easing curve used to animate the popup card on open and close.
/// </summary>
public enum G9PopupViewAnimationEasing
{
    Linear,
    SinIn,
    SinOut,
    SinInOut,
    CubicOut,
    BounceOut
}

/// <summary>
///     Where the popup positions itself relative to a caller-supplied anchor view. Used by the
///     legacy <c>SfG9Popup.ShowRelativeToView</c> overload and preserved here so callers don't
///     have to migrate. Most callers don't set this — the helper centers the popup by default.
/// </summary>
public enum G9PopupViewRelativePosition
{
    AlignTop,
    AlignBottom,
    AlignLeft,
    AlignRight,
    AlignTopLeft,
    AlignTopRight,
    AlignBottomLeft,
    AlignBottomRight,
    AlignToLeftOf,
    AlignToRightOf,
    AlignToTopOf,
    AlignToBottomOf
}

/// <summary>
///     Strength of the blur applied by <see cref="G9PopupViewOverlayMode.Blur" />. The control
///     translates these to platform blur intensities; on platforms that don't expose a blur
///     primitive (older Android API levels, software-rendered MAUI Catalyst windows) the
///     overlay falls back to a slightly darker solid scrim.
/// </summary>
public enum G9PopupViewBlurIntensity
{
    None,
    Light,
    ExtraLight,
    Dark,
    ExtraDark
}
