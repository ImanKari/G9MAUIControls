namespace G9MAUIControls.Icons;

/// <summary>
///     The library's own built-in glyph set — the small number of affordances the controls
///     need in order to look right the moment they are dropped into a project, with no icon
///     font configured at all.
///     <para>
///         These are <b>vector paths</b>, not font glyphs (see <see cref="G9GlyphDrawable" />).
///         A library that shipped a font would either force a multi-megabyte icon package on
///         every consumer or bundle a subset TTF that can silently fail to resolve inside some
///         Android native renderers and paint a tofu box (the A2 hazard catalogued in
///         <c>Controls/G9Controls.md</c> §15). Paths cannot tofu, cost nothing to package, and
///         stay crisp at every size and density.
///     </para>
///     <para>
///         This set is deliberately <b>not</b> a general-purpose icon library. It covers control
///         chrome only. For anything else, register your own icon font — see
///         <see cref="G9IconFonts" /> — and pass a <see cref="G9IconSource" />. Every default
///         below is individually overridable through <see cref="G9Glyphs" />, so a consumer with
///         a house icon font can make the controls use it end to end.
///     </para>
/// </summary>
public enum G9Glyph
{
    /// <summary>No glyph. Renders nothing.</summary>
    None = 0,

    /// <summary>Downward chevron. The dropdown affordance on picker-like fields.</summary>
    ChevronDown,

    /// <summary>Upward chevron. The collapse affordance on expanders.</summary>
    ChevronUp,

    /// <summary>Leading-edge chevron (points physically left).</summary>
    ChevronLeft,

    /// <summary>Trailing-edge chevron (points physically right). Drill-down affordance.</summary>
    ChevronRight,

    /// <summary>A back arrow WITH a shaft (←), not a bare chevron.</summary>
    /// <remarks>
    ///     Distinct from <see cref="ChevronLeft" /> on purpose: a chevron is a drill-down/expand
    ///     affordance, a shafted arrow is "go back". Sheet and page headers want the arrow; list rows
    ///     want the chevron. Mirror by CHOOSING <see cref="ArrowForward" /> in RTL — never by letting
    ///     the canvas flip, which reverses every other glyph too.
    /// </remarks>
    ArrowBack,

    /// <summary>A forward arrow WITH a shaft (→). The RTL counterpart of <see cref="ArrowBack" />.</summary>
    ArrowForward,

    /// <summary>A cross. Close / clear / dismiss.</summary>
    Close,

    /// <summary>A magnifier. Search fields and the selection sheet's filter box.</summary>
    Search,

    /// <summary>An open eye. "Password is visible."</summary>
    Eye,

    /// <summary>A struck-through eye. "Password is hidden."</summary>
    EyeOff,

    /// <summary>A tick. Selection confirmation and the picker sheet's Done action.</summary>
    Check,

    /// <summary>A tick inside a circle. The Success popup / toast accent glyph.</summary>
    CheckCircle,

    /// <summary>An exclamation mark inside a triangle. The Warning accent glyph.</summary>
    Warning,

    /// <summary>A cross inside a circle. The Error accent glyph.</summary>
    ErrorCircle,

    /// <summary>An "i" inside a circle. The Information accent glyph.</summary>
    Info,

    /// <summary>A calendar page. The date half of the date/time picker.</summary>
    Calendar,

    /// <summary>A clock face. The time half of the date/time picker and duration pickers.</summary>
    Clock,

    /// <summary>A microphone. Voice input on the search entry.</summary>
    Mic,

    /// <summary>A struck-through microphone. Voice input unavailable or denied.</summary>
    MicOff,

    /// <summary>A plus sign. The tab bar's centre action button.</summary>
    Plus,

    /// <summary>A minus sign.</summary>
    Minus,

    /// <summary>Three stacked bars. A menu / drawer affordance.</summary>
    Menu,

    /// <summary>A circular arrow. Refresh / retry.</summary>
    Refresh,

    /// <summary>A trash can. Destructive swipe actions.</summary>
    Delete
}
