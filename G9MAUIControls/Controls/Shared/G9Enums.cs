namespace G9MAUIControls.Controls;

public enum G9ButtonVariant
{
    Primary,
    Tonal,
    Default,
    Secondary,
    Info,
    Success,
    Warning,

    /// <summary>Solid red fill, white glyph. The loud, committing destructive action (Delete, Truncate).</summary>
    Error,

    /// <summary>
    ///     The SOFT destructive look: an <c>ErrorContainer</c> fill with the <c>Error</c> glyph on top —
    ///     the same red family as <see cref="Error" /> but quiet enough to sit permanently on screen.
    ///     Use it for a destructive/dismissive control that is always visible and must NOT shout, e.g.
    ///     the map multi-selection card's close (X). A solid <see cref="Error" /> button there would read
    ///     as the primary action of the card.
    /// </summary>
    ErrorTonal,

    Surface,
    Outline,
    Text
}

public enum G9ControlSize
{
    Small,
    Medium,
    Large,
    Hero
}

public enum G9TextInputDirection
{
    MatchParent,
    LeftToRight,
    RightToLeft
}

public enum G9KeyboardType
{
    Default,
    Email,
    Phone,
    Number,
    Url,
    Password
}

// G9BarcodeScanMode and G9BarcodeTextEntryState were here, and now live in G9MAUIControls.Barcode
// alongside the only control that uses them. Nothing in the core referenced either one: the core was
// carrying public vocabulary for a capability it does not ship, which is precisely what the ecosystem
// split exists to avoid — a consumer who never scans a barcode should not see barcode types in
// IntelliSense, and the core's public surface should not have to stay stable for a satellite's sake.
// See LES-0014.

public enum G9DateTimePickerMode
{
    Date,
    Time,
    DateTime
}

public enum G9DateTimeDisplayFormat
{
    ShortDate,
    LongDate,
    TimeOnly,
    ShortDateTime,
    LongDateTime,
    Custom
}

public enum G9RangeSliderMode
{
    Single,
    Range
}

/// <summary>
///     Allowed character set for an <see cref="G9PinEntry" /> cell.
/// </summary>
public enum G9PinEntryType
{
    /// <summary>Digits 0-9 only. Activates the numeric on-screen keyboard.</summary>
    Number,
    /// <summary>Latin letters A-Z / a-z only. Default keyboard.</summary>
    Letters,
    /// <summary>Letters and digits. Default keyboard.</summary>
    Alphanumeric,
    /// <summary>Digits 0-9, masked on screen with the configured mask character.</summary>
    Password
}

public enum G9ChipGroupSelectionMode
{
    MultiSelection,
    SingleSelection
}

/// <summary>How a <c>G9ChipGroup</c> arranges its chips when they do not all fit the width.</summary>
public enum G9ChipGroupLayoutMode
{
    /// <summary>Chips flow onto as many lines as they need (wrapping <c>FlexLayout</c>). The group grows taller.</summary>
    Wrap,

    /// <summary>
    ///     Chips stay on ONE line and the strip scrolls horizontally when they overflow (scroll bar hidden).
    ///     The group's height never changes with the chip count — use it where a second line would push the
    ///     content below it down (filter strips above a list).
    /// </summary>
    SingleLineScroll
}

public enum G9ProgressType
{
    Primary,
    Success,
    Warning,
    Error
}

public enum G9ProgressLabelPlacement
{
    None,
    Above,
    End
}

public enum G9SeparatorTitleAlignment
{
    Auto,
    Start,
    Center,
    End
}

public enum G9TabMode
{
    /// <summary>
    ///     Tabs distribute equally across the full width. Best for 2–4 fixed tabs that
    ///     should always be fully visible. Material Design "primary tabs" pattern.
    /// </summary>
    Fixed,

    /// <summary>
    ///     Tabs auto-size and the bar scrolls horizontally when total width exceeds the
    ///     viewport. Best for 5+ tabs or tabs with variable-length labels. Edge fades
    ///     hint at off-screen tabs. Material Design "secondary tabs" pattern.
    /// </summary>
    Scrollable
}

/// <summary>
///     Visual treatment of <c>G9TabView</c>'s bar and selection indicator.
///     Independent from <see cref="G9TabMode"/>: any style can be paired with
///     either <c>Fixed</c> or <c>Scrollable</c> layout.
/// </summary>
public enum G9TabStyle
{
    /// <summary>
    ///     Flat / minimal style (the default). The bar is transparent, with a thin top
    ///     and bottom line spanning its full width and a 1 dp vertical <c>|</c>
    ///     separator between every pair of cells. The active cell is marked by a
    ///     Primary-coloured bottom underline that animates between cells. The content
    ///     area defaults to no frame, no rounded corners, and zero padding — the
    ///     consumer supplies its own padding/margins/background. Matches Material 3's
    ///     "secondary tabs" pattern. The app designer prefers this style for every
    ///     tabbed surface in the app, so it is the default.
    /// </summary>
    Underlined,

    /// <summary>
    ///     Rounded segmented-control style (the legacy look). The bar is a single
    ///     rounded "pill" container; the active cell sits behind a smaller floating
    ///     pill that animates between cells with a colored shadow halo. The content
    ///     area defaults to a framed, rounded, padded panel. Use when the design
    ///     specifically calls for a card-style segmented switcher. Pair with
    ///     <c>ShowFrame=true</c> + <c>FrameCornerRadius=16</c> +
    ///     <c>ContentPadding=20</c> for the original framed look.
    /// </summary>
    Pill
}

/// <summary>
///     Slide direction for nested panels pushed onto an <see cref="G9CascadePanel" />.
///     Describes the visual travel of the INCOMING panel: <c>LeftToRight</c> means the
///     new panel enters from the left edge and slides rightward into place; the panel
///     beneath it parallaxes the same way and the back affordance exits back to the left.
/// </summary>
public enum G9CascadeDirection
{
    /// <summary>
    ///     Resolve from the active culture: <c>LeftToRight</c> in LTR, <c>RightToLeft</c>
    ///     in RTL. This is the default — nested panels drill in the natural reading
    ///     direction (the iOS / Android push convention).
    /// </summary>
    Auto,

    /// <summary>New panel enters from the left edge and slides rightward to cover.</summary>
    LeftToRight,

    /// <summary>New panel enters from the right edge and slides leftward to cover.</summary>
    RightToLeft,

    /// <summary>New panel enters from the top edge and slides downward to cover.</summary>
    TopToBottom,

    /// <summary>New panel enters from the bottom edge and slides upward to cover.</summary>
    BottomToTop
}

/// <summary>
///     How a nested <see cref="G9CascadePanel" /> panel transitions in / out relative to
///     the panel beneath it.
/// </summary>
public enum G9CascadeTransition
{
    /// <summary>
    ///     The new panel slides in <i>on top of</i> a stationary base panel (the base stays
    ///     put, optionally with a subtle parallax + dim when <c>EnableParallax</c> is on).
    ///     The iOS / Material "modal push over a backdrop" feel. This is the default.
    /// </summary>
    Overlay,

    /// <summary>
    ///     The base panel slides fully <i>out</i> of the box in the same direction the new
    ///     panel travels, while the new panel slides <i>in</i> to take its place — a
    ///     conveyor-belt / carousel replace. Popping reverses it.
    /// </summary>
    Push
}

public enum G9TabBarPosition
{
    /// <summary>
    ///     Tab bar sits above the content (default). Standard pattern for navigating
    ///     between sections at the top of a screen.
    /// </summary>
    Top,

    /// <summary>
    ///     Tab bar sits below the content. Useful for forms / wizards where the bar acts
    ///     as a footer-style switcher, or when the tab control sits at the bottom of a
    ///     card and you want the content to read upward into it.
    /// </summary>
    Bottom
}
