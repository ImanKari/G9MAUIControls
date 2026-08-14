using System.Globalization;

namespace G9MAUIControls.Localization;

/// <summary>
///     What the controls need to know about the app's active culture — the reading direction,
///     the culture to format numbers and dates with, and which typeface to put text in.
///     <para>
///         <b>Why the library owns a culture facade instead of reading
///         <see cref="CultureInfo.CurrentUICulture" /> directly.</b> Two reasons, both learned
///         the hard way. First, a language switch has to <i>repaint</i> controls that are already
///         on screen — a static property gives nothing to subscribe to, so every control base
///         needs a change signal, which is <see cref="CultureChanged" />. Second, RTL is not
///         reliably derivable at the point a control needs it: an app may run an RTL layout under
///         an LTR thread culture, or pin direction independently of language, so
///         <see cref="IsRtl" /> is settable rather than inferred.
///     </para>
///     <para>
///         <b>Fonts.</b> The controls set <see cref="Label.FontFamily" /> explicitly on their own
///         text rather than letting the platform pick. Without that, Android's family fallback
///         drops non-Latin strings onto the system sans-serif, so a field's inner text renders in
///         a visibly different face from the floating label beside it. Give
///         <see cref="RtlFontFamily" /> / <see cref="LtrFontFamily" /> the aliases you registered
///         with <c>fonts.AddFont(...)</c>; leave them null to let the platform decide, which is
///         correct for a single-script LTR app.
///     </para>
///     <example>
///         <code>
///         // Point the facade at whatever the app already uses as its source of truth.
///         G9Culture.Configure(
///             currentCulture: () => LocalizationManager.Current.CurrentCulture,
///             isRtl:          () => LocalizationManager.Current.CurrentCulture.TextInfo.IsRightToLeft);
///
///         G9Culture.RtlFontFamily = "Yekan";
///         G9Culture.LtrFontFamily = "OpenSansRegular";
///
///         // …and tell the controls when it changed.
///         LocalizationManager.Current.PropertyChanged += (_, _) => G9Culture.NotifyChanged();
///         </code>
///     </example>
/// </summary>
public static class G9Culture
{
    private static Func<CultureInfo>? _cultureAccessor;
    private static Func<bool>? _isRtlAccessor;

    /// <summary>
    ///     Raised after the culture or reading direction changes. Every control base subscribes
    ///     while it is loaded and repaints itself, so a language switch needs no per-control
    ///     wiring from the consumer.
    ///     <para>
    ///         Always raised on the main thread by <see cref="NotifyChanged" />; handlers may
    ///         touch the visual tree directly.
    ///     </para>
    /// </summary>
    public static event EventHandler<G9CultureEventArgs>? CultureChanged;

    /// <summary>
    ///     The culture the controls format with — dates in the date picker, numbers in the range
    ///     slider, and <see cref="string.Format(IFormatProvider,string,object?)" /> calls in the
    ///     internal strings.
    ///     <para>Defaults to <see cref="CultureInfo.CurrentUICulture" /> when unconfigured.</para>
    /// </summary>
    public static CultureInfo CurrentCulture => _cultureAccessor?.Invoke() ?? CultureInfo.CurrentUICulture;

    /// <summary>
    ///     True when the UI reads right-to-left. Drives icon-slot column swapping, floating-label
    ///     anchoring, progress-fill anchor, toast stack side, and every drawable that does its own
    ///     mirroring.
    ///     <para>
    ///         Defaults to <see cref="TextInfo.IsRightToLeft" /> of <see cref="CurrentCulture" />
    ///         when unconfigured.
    ///     </para>
    /// </summary>
    public static bool IsRtl => _isRtlAccessor?.Invoke() ?? CurrentCulture.TextInfo.IsRightToLeft;

    /// <summary>
    ///     Font family for right-to-left text, or <c>null</c> to let the platform choose.
    /// </summary>
    public static string? RtlFontFamily { get; set; }

    /// <summary>
    ///     Font family for left-to-right text, or <c>null</c> to let the platform choose.
    /// </summary>
    public static string? LtrFontFamily { get; set; }

    /// <summary>
    ///     The typeface the controls' own text should use right now — <see cref="RtlFontFamily" />
    ///     while <see cref="IsRtl" />, otherwise <see cref="LtrFontFamily" />. A non-empty
    ///     <paramref name="explicitFontFamily" /> always wins; that is the per-control escape
    ///     hatch for a field that needs a specific face.
    /// </summary>
    public static string? ResolveFontFamily(string? explicitFontFamily = null) =>
        !string.IsNullOrWhiteSpace(explicitFontFamily)
            ? explicitFontFamily
            : IsRtl ? RtlFontFamily : LtrFontFamily;

    /// <summary>
    ///     Points the facade at the app's own culture state. Pass <c>null</c> for either accessor
    ///     to keep that value's default behaviour.
    /// </summary>
    /// <param name="currentCulture">Returns the culture to format with.</param>
    /// <param name="isRtl">Returns whether the UI currently reads right-to-left.</param>
    public static void Configure(Func<CultureInfo>? currentCulture = null, Func<bool>? isRtl = null)
    {
        _cultureAccessor = currentCulture;
        _isRtlAccessor = isRtl;
    }

    /// <summary>
    ///     Tells every loaded control to repaint because the culture or direction changed. Call
    ///     this from wherever the app switches language.
    ///     <para>
    ///         Marshals to the main thread itself, so it is safe to call from a settings service
    ///         running on a background thread.
    ///     </para>
    /// </summary>
    public static void NotifyChanged()
    {
        // Snapshot the state BEFORE marshalling. On a background call the args must describe the
        // change that happened, not whatever the accessors happen to return by the time the main
        // thread drains its queue — two rapid switches would otherwise both report the second one.
        var args = new G9CultureEventArgs(CurrentCulture, IsRtl);

        if (MainThread.IsMainThread)
        {
            CultureChanged?.Invoke(null, args);
            return;
        }

        MainThread.BeginInvokeOnMainThread(() => CultureChanged?.Invoke(null, args));
    }

    /// <summary>Clears the configuration and every subscriber. Intended for tests.</summary>
    public static void Reset()
    {
        _cultureAccessor = null;
        _isRtlAccessor = null;
        RtlFontFamily = null;
        LtrFontFamily = null;
        CultureChanged = null;
    }
    /// <summary>
    ///     Reads an app-supplied font-family resource, falling back when the key is absent.
    ///     <para>
    ///         <b>Must be TryGetValue, not the indexer.</b> <c>Resources["Key"]</c> throws
    ///         <see cref="KeyNotFoundException" /> when the key is missing, so a
    ///         <c>Resources["Key"] as string ?? fallback</c> expression never reaches its fallback — the throw
    ///         happens first. Four call sites in this suite were written that way, and every one of them
    ///         crashed the first consumer that did not happen to define the key: the tab bar took the whole
    ///         app down in its constructor. The keys are OPTIONAL app conveniences, so absence is the normal
    ///         case for a fresh consumer. See LES-0020.
    ///     </para>
    /// </summary>
    /// <param name="key">The resource key, e.g. <c>CulturalFont</c> or <c>EnglishFont</c>.</param>
    /// <param name="fallback">Used when the key is absent, present-but-not-a-string, or empty.</param>
    public static string ResolveAppFont(string key, string? fallback)
    {
        if (!string.IsNullOrWhiteSpace(key)
            && Application.Current?.Resources.TryGetValue(key, out var value) == true
            && value is string family
            && !string.IsNullOrWhiteSpace(family))
        {
            return family;
        }

        return fallback ?? string.Empty;
    }
}

/// <summary>
///     Describes a culture change, as it was at the moment <see cref="G9Culture.NotifyChanged" />
///     was called.
/// </summary>
/// <param name="culture">The culture now in effect.</param>
/// <param name="isRtl">Whether the UI now reads right-to-left.</param>
public sealed class G9CultureEventArgs(CultureInfo culture, bool isRtl) : EventArgs
{
    /// <summary>The culture now in effect.</summary>
    public CultureInfo Culture { get; } = culture;

    /// <summary>Whether the UI now reads right-to-left.</summary>
    public bool IsRtl { get; } = isRtl;

}
