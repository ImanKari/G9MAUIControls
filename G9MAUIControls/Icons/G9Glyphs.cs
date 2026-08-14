namespace G9MAUIControls.Icons;

/// <summary>
///     The icons the controls draw <b>for themselves</b> — the dropdown chevron, the clear
///     cross, the password eye, the popup type accents — and the one place to replace them.
///     <para>
///         Every value defaults to a built-in vector <see cref="G9Glyph" />, so the suite looks
///         complete with no configuration. Assign your own font glyph to any of them and every
///         control picks it up on its next visual pass, which is what lets a consumer with a
///         house icon set make the whole suite match:
///     </para>
///     <example>
///         <code>
///         G9Glyphs.Chevron   = MyIcons.ExpandMore;
///         G9Glyphs.Clear     = MyIcons.CancelFilled;
///         G9Glyphs.EyeOpen   = MyIcons.Visibility;
///         G9Glyphs.EyeClosed = MyIcons.VisibilityOff;
///         </code>
///     </example>
///     <para>
///         Set these <b>once, at startup</b>, before the first page is created — normally from
///         the <c>UseG9MauiControls</c> configuration callback. They are read during a control's
///         visual pass, not cached at construction, so a later change is not an error; it simply
///         will not repaint controls that are already on screen.
///     </para>
/// </summary>
public static class G9Glyphs
{
    /// <summary>Dropdown affordance on picker, combo box and date/time fields.</summary>
    public static G9IconSource Chevron { get; set; } = G9Glyph.ChevronDown;

    /// <summary>Collapse affordance — the expanded state of an expander / combo box.</summary>
    public static G9IconSource ChevronCollapse { get; set; } = G9Glyph.ChevronUp;

    /// <summary>Drill-down affordance on nav cards and cascade panel rows.</summary>
    public static G9IconSource ChevronForward { get; set; } = G9Glyph.ChevronRight;

    /// <summary>Back affordance on cascade panels and full-screen sheet headers.</summary>
    public static G9IconSource ChevronBack { get; set; } = G9Glyph.ChevronLeft;

    /// <summary>
    ///     "Go back" on a sheet or page header — a shafted arrow, not a chevron.
    /// </summary>
    /// <remarks>Pair with <see cref="ArrowForward" />: pick the forward one under RTL.</remarks>
    public static G9IconSource ArrowBack { get; set; } = G9Glyph.ArrowBack;

    /// <summary>The RTL counterpart of <see cref="ArrowBack" />.</summary>
    public static G9IconSource ArrowForward { get; set; } = G9Glyph.ArrowForward;

    /// <summary>The clear button inside a text entry, and sheet / popup close buttons.</summary>
    public static G9IconSource Clear { get; set; } = G9Glyph.Close;

    /// <summary>Search entry, and the selection sheet's filter box.</summary>
    public static G9IconSource Search { get; set; } = G9Glyph.Search;

    /// <summary>Password reveal toggle, showing state.</summary>
    public static G9IconSource EyeOpen { get; set; } = G9Glyph.Eye;

    /// <summary>Password reveal toggle, hidden state.</summary>
    public static G9IconSource EyeClosed { get; set; } = G9Glyph.EyeOff;

    /// <summary>Selection tick in pickers, combo boxes and chip groups.</summary>
    public static G9IconSource Check { get; set; } = G9Glyph.Check;

    /// <summary>The date half of a date/time picker.</summary>
    public static G9IconSource Calendar { get; set; } = G9Glyph.Calendar;

    /// <summary>The time half of a date/time picker, and duration pickers.</summary>
    public static G9IconSource Clock { get; set; } = G9Glyph.Clock;

    /// <summary>Voice input, idle.</summary>
    public static G9IconSource Mic { get; set; } = G9Glyph.Mic;

    /// <summary>Voice input, unavailable or permission denied.</summary>
    public static G9IconSource MicOff { get; set; } = G9Glyph.MicOff;

    /// <summary>The tab bar's centre action button.</summary>
    public static G9IconSource Plus { get; set; } = G9Glyph.Plus;

    /// <summary>
    ///     Accent glyph for the Information popup / toast type.
    ///     <para>
    ///         The four accents below are what make a popup <i>read</i> as its type at a glance,
    ///         so keep them semantically distinct if you replace them. Pointing two types at the
    ///         same glyph is how every alert in an app ends up looking like the same alert.
    ///     </para>
    /// </summary>
    public static G9IconSource Info { get; set; } = G9Glyph.Info;

    /// <summary>Accent glyph for the Success popup / toast type.</summary>
    public static G9IconSource Success { get; set; } = G9Glyph.CheckCircle;

    /// <summary>Accent glyph for the Warning popup / toast type.</summary>
    public static G9IconSource Warning { get; set; } = G9Glyph.Warning;

    /// <summary>Accent glyph for the Error popup / toast type.</summary>
    public static G9IconSource Error { get; set; } = G9Glyph.ErrorCircle;

    /// <summary>Retry action on failure surfaces.</summary>
    public static G9IconSource Refresh { get; set; } = G9Glyph.Refresh;

    /// <summary>Destructive swipe action.</summary>
    public static G9IconSource Delete { get; set; } = G9Glyph.Delete;

    /// <summary>Restores every glyph to its built-in vector default.</summary>
    public static void Reset()
    {
        Chevron = G9Glyph.ChevronDown;
        ChevronCollapse = G9Glyph.ChevronUp;
        ChevronForward = G9Glyph.ChevronRight;
        ChevronBack = G9Glyph.ChevronLeft;
        ArrowBack = G9Glyph.ArrowBack;
        ArrowForward = G9Glyph.ArrowForward;
        Clear = G9Glyph.Close;
        Search = G9Glyph.Search;
        EyeOpen = G9Glyph.Eye;
        EyeClosed = G9Glyph.EyeOff;
        Check = G9Glyph.Check;
        Calendar = G9Glyph.Calendar;
        Clock = G9Glyph.Clock;
        Mic = G9Glyph.Mic;
        MicOff = G9Glyph.MicOff;
        Plus = G9Glyph.Plus;
        Info = G9Glyph.Info;
        Success = G9Glyph.CheckCircle;
        Warning = G9Glyph.Warning;
        Error = G9Glyph.ErrorCircle;
        Refresh = G9Glyph.Refresh;
        Delete = G9Glyph.Delete;
    }
}
