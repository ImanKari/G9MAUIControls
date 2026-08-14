using System.Globalization;
using System.Resources;

namespace G9MAUIControls.Localization;

/// <summary>
///     Every string the controls display <b>on their own initiative</b> — the picker sheet's
///     "Done", the combo box's "No results", the popup's "OK" / "Cancel", a field's built-in
///     validation message — and the one seam for translating them.
///     <para>
///         <b>Every string has a working English default.</b> A control library that needed a
///         resource file wired up before it would render readable text would be broken out of the
///         box, and the failure mode — a button labelled with a resource key — is the kind of
///         thing that reaches production. So the defaults below are real English, and
///         localization is strictly additive.
///     </para>
///     <para>
///         <b>Three ways to translate, cheapest first.</b>
///     </para>
///     <list type="number">
///         <item>
///             <b>Nothing.</b> English, from the defaults here.
///         </item>
///         <item>
///             <b>Point at a <see cref="ResourceManager" /></b> via
///             <see cref="UseResources" />. Keys are the <see cref="G9StringKey" /> member names,
///             optionally under a prefix so they can share an existing resx with app strings.
///             Any key you do not define falls back to English, so a partial translation is a
///             valid translation.
///         </item>
///         <item>
///             <b>Supply a resolver</b> via <see cref="UseProvider" /> for anything else — a
///             runtime-swappable localization library, a database, a remote catalogue. Return
///             <c>null</c> for a key to fall back.
///         </item>
///     </list>
///     <example>
///         <code>
///         // 2 — share the app's existing resx; keys look like "G9Done", "G9NoResults", …
///         G9Strings.UseResources(AppDictionary.ResourceManager, keyPrefix: "G9");
///
///         // 3 — anything else
///         G9Strings.UseProvider((key, culture) => myCatalogue.Find($"controls.{key}", culture));
///         </code>
///     </example>
/// </summary>
public static class G9Strings
{
    private static Func<string, CultureInfo, string?>? _provider;

    // Applied ONLY to G9StringKey lookups, never to Resolve. See UseResources.
    private static string _keyPrefix = string.Empty;

    private static readonly Dictionary<G9StringKey, string> Defaults = new()
    {
        [G9StringKey.Ok] = "OK",
        [G9StringKey.Cancel] = "Cancel",
        [G9StringKey.Save] = "Save",
        [G9StringKey.Confirm] = "Confirm",
        [G9StringKey.Close] = "Close",
        [G9StringKey.Done] = "Done",
        [G9StringKey.Back] = "Back",
        [G9StringKey.Retry] = "Retry",
        [G9StringKey.Skip] = "Skip",
        [G9StringKey.MoreDetails] = "More details",
        [G9StringKey.Clear] = "Clear",
        [G9StringKey.Reset] = "Reset",
        [G9StringKey.Delete] = "Delete",

        [G9StringKey.Search] = "Search…",
        [G9StringKey.NoResults] = "No results",
        [G9StringKey.Selected] = "selected",
        [G9StringKey.ComingSoon] = "Coming soon",
        [G9StringKey.Loading] = "Loading…",

        [G9StringKey.SelectDate] = "Select date",
        [G9StringKey.SelectTime] = "Select time",
        [G9StringKey.SelectDateTime] = "Select date and time",
        [G9StringKey.SelectDuration] = "Select duration",
        [G9StringKey.Today] = "Today",
        [G9StringKey.Now] = "Now",
        [G9StringKey.Year] = "Year",
        [G9StringKey.Month] = "Month",
        [G9StringKey.Day] = "Day",
        [G9StringKey.Hour] = "Hour",
        [G9StringKey.Minute] = "Minute",
        [G9StringKey.TimeSpanYearsFormat] = "{0}y",
        [G9StringKey.TimeSpanMonthsFormat] = "{0}m",
        [G9StringKey.TimeSpanDaysFormat] = "{0}d",

        [G9StringKey.Information] = "Information",
        [G9StringKey.Success] = "Success",
        [G9StringKey.Warning] = "Warning",
        [G9StringKey.Error] = "Error",

        [G9StringKey.RequiredSuffix] = "is required",
        [G9StringKey.InvalidEmail] = "Invalid email",
        [G9StringKey.InvalidUrl] = "Invalid URL",
        [G9StringKey.InvalidValue] = "Invalid value",

        [G9StringKey.Scanning] = "Scanning…",
        [G9StringKey.MicrophonePermissionDenied] = "Microphone permission denied",
        [G9StringKey.SpeechRecognitionPermissionDenied] = "Speech recognition permission denied",
        [G9StringKey.SpeechRecognitionUnavailable] = "Voice input is not available on this device",
        [G9StringKey.VoiceRecognitionFailed] = "Voice recognition failed",
        [G9StringKey.PermissionErrorFormat] = "Permission error: {0}",

        [G9StringKey.TimeAgoJustNow] = "just now",
        [G9StringKey.TimeAgoMinutesFormat] = "{0} min ago",
        [G9StringKey.TimeAgoHoursFormat] = "{0} h ago",
        [G9StringKey.TimeAgoDaysFormat] = "{0} d ago",
        [G9StringKey.TimeAgoWeeksFormat] = "{0} w ago",
        [G9StringKey.TimeAgoMonthsFormat] = "{0} mo ago",
        [G9StringKey.TimeAgoYearsFormat] = "{0} y ago",

        [G9StringKey.Cancelled] = "Cancelled",
        [G9StringKey.CancelFinishingStep] = "Finishing the current step…",

        [G9StringKey.UnexpectedError] = "Something went wrong. Please try again."
    };

    /// <summary>
    ///     Returns the localized text for a key, falling back to the built-in English default
    ///     when no provider is configured or the provider has no entry.
    /// </summary>
    public static string Get(G9StringKey key)
    {
        var name = key.ToString();

        try
        {
            var localized = _provider?.Invoke(_keyPrefix + name, G9Culture.CurrentCulture);
            if (!string.IsNullOrEmpty(localized))
            {
                return localized;
            }
        }
        catch (Exception)
        {
            // A consumer's resolver must never be able to take a control down mid-layout: a
            // missing satellite assembly or a disposed catalogue would otherwise crash the very
            // paint pass that asked for a button label. Fall through to English.
        }

        return Defaults.TryGetValue(key, out var fallback) ? fallback : name;
    }

    /// <summary>
    ///     Returns the localized text for a format key with its arguments applied, using
    ///     <see cref="G9Culture.CurrentCulture" /> as the format provider.
    /// </summary>
    public static string Format(G9StringKey key, params object?[] args) =>
        string.Format(G9Culture.CurrentCulture, Get(key), args);

    /// <summary>
    ///     Translates through a <see cref="ResourceManager" /> — the common case, and the one
    ///     that lets these strings live in an existing resx alongside app strings.
    /// </summary>
    /// <param name="resources">The resource manager to read from.</param>
    /// <param name="keyPrefix">
    ///     Prepended to the <see cref="G9StringKey" /> member name, so
    ///     <c>keyPrefix: "G9"</c> looks up <c>G9Done</c>, <c>G9NoResults</c>, and so on. Use one
    ///     when sharing a resx to keep control strings from colliding with app strings.
    ///     <para>
    ///         <b>It applies to the suite's own keys ONLY, never to <see cref="Resolve" />.</b> Those
    ///         keys come from the consumer's catalogue and are already whole — prefixing them would
    ///         look up a name the consumer never defined and silently return nothing. That is exactly
    ///         what happened to the intro carousel's slide titles in the first real integration: the
    ///         slides rendered, blank, with no error anywhere.
    ///     </para>
    /// </param>
    public static void UseResources(ResourceManager resources, string keyPrefix = "")
    {
        ArgumentNullException.ThrowIfNull(resources);
        _provider = resources.GetString;
        _keyPrefix = keyPrefix ?? string.Empty;
    }

    /// <summary>
    ///     Translates through an arbitrary resolver. Return <c>null</c> or empty for a key to
    ///     fall back to the built-in English default.
    /// </summary>
    public static void UseProvider(Func<string, CultureInfo, string?> provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
        _keyPrefix = string.Empty;
    }

    /// <summary>
    ///     Resolves an <b>arbitrary</b> key through the configured provider — for the places where a
    ///     caller hands the controls a resource key instead of literal text, so that one definition
    ///     re-localizes on a culture flip. <c>G9EdgeMenuItem.TextKey</c> is the reference case.
    ///     <para>
    ///         Unlike <see cref="Get(G9StringKey)" /> this has no built-in default to fall back to:
    ///         the key belongs to the consumer's catalogue, not to the suite. Returns <c>null</c>
    ///         when there is no provider or no entry, so the caller can decide whether to show the
    ///         raw key or nothing.
    ///     </para>
    /// </summary>
    public static string? Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        try
        {
            var value = _provider?.Invoke(key, G9Culture.CurrentCulture);
            return string.IsNullOrEmpty(value) ? null : value;
        }
        catch (Exception)
        {
            // A consumer's resolver must never fault the layout pass that asked for a label.
            return null;
        }
    }

    /// <summary>Overrides one string in code, without a resource file.</summary>
    public static void Override(G9StringKey key, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Defaults[key] = text;
    }

    /// <summary>Drops any configured provider so every string reverts to English.</summary>
    public static void Reset()
    {
        _provider = null;
        _keyPrefix = string.Empty;
    }
}

/// <summary>
///     The complete set of strings the controls can display. The member name is the lookup key
///     (optionally behind the prefix passed to <see cref="G9Strings.UseResources" />).
///     <para>
///         Members ending in <c>Format</c> are <see cref="string.Format(IFormatProvider,string,object?)" />
///         templates — a translation must keep their placeholders, or the value they were meant to
///         carry is silently dropped.
///     </para>
/// </summary>
public enum G9StringKey
{
    /// <summary>Affirmative button on a popup.</summary>
    Ok,

    /// <summary>Dismissive button on a popup or sheet.</summary>
    Cancel,

    /// <summary>Commit button on an editor sheet.</summary>
    Save,

    /// <summary>Default title of a confirmation popup.</summary>
    Confirm,

    /// <summary>Close action on a sheet header.</summary>
    Close,

    /// <summary>Commit action on a picker sheet.</summary>
    Done,

    /// <summary>Back action on a cascade panel or full-screen sheet header.</summary>
    Back,

    /// <summary>Retry action on a failure surface.</summary>
    Retry,

    /// <summary>Skip action on an onboarding carousel.</summary>
    Skip,

    /// <summary>
    ///     Reveals the technical detail of a failure — the extra button an error popup grows when a
    ///     trace is attached.
    /// </summary>
    MoreDetails,

    /// <summary>Clear-selection action on a multi-select picker.</summary>
    Clear,

    /// <summary>Reset action on a range slider or filter sheet.</summary>
    Reset,

    /// <summary>Destructive swipe action label.</summary>
    Delete,

    /// <summary>Placeholder in a search field and a selection sheet's filter box.</summary>
    Search,

    /// <summary>Empty state of a filtered selection list.</summary>
    NoResults,

    /// <summary>Suffix on a multi-select summary, e.g. "3 selected".</summary>
    Selected,

    /// <summary>Placeholder for a control whose feature is not wired up yet.</summary>
    ComingSoon,

    /// <summary>Label under a loading spinner.</summary>
    Loading,

    /// <summary>Title of a date picker sheet.</summary>
    SelectDate,

    /// <summary>Title of a time picker sheet.</summary>
    SelectTime,

    /// <summary>Title of a combined date and time picker sheet.</summary>
    SelectDateTime,

    /// <summary>Title of a duration picker sheet.</summary>
    SelectDuration,

    /// <summary>Jump-to-today action in a date picker.</summary>
    Today,

    /// <summary>Jump-to-now action in a time picker.</summary>
    Now,

    /// <summary>Year column header in a date drum picker.</summary>
    Year,

    /// <summary>Month column header in a date drum picker.</summary>
    Month,

    /// <summary>Day column header in a date drum picker.</summary>
    Day,

    /// <summary>Hour column header in a time drum picker.</summary>
    Hour,

    /// <summary>Minute column header in a time drum picker.</summary>
    Minute,

    /// <summary>Years part of a formatted duration. <c>{0}</c> = count.</summary>
    TimeSpanYearsFormat,

    /// <summary>Months part of a formatted duration. <c>{0}</c> = count.</summary>
    TimeSpanMonthsFormat,

    /// <summary>Days part of a formatted duration. <c>{0}</c> = count.</summary>
    TimeSpanDaysFormat,

    /// <summary>Default title of an Information popup / toast.</summary>
    Information,

    /// <summary>Default title of a Success popup / toast.</summary>
    Success,

    /// <summary>Default title of a Warning popup / toast.</summary>
    Warning,

    /// <summary>Default title of an Error popup / toast.</summary>
    Error,

    /// <summary>Appended to a field label when a required field is left empty.</summary>
    RequiredSuffix,

    /// <summary>Validation message for a malformed email address.</summary>
    InvalidEmail,

    /// <summary>Validation message for a malformed URL.</summary>
    InvalidUrl,

    /// <summary>Generic validation failure message.</summary>
    InvalidValue,

    /// <summary>Status text while a scan-style input is active.</summary>
    Scanning,

    /// <summary>The user denied the microphone permission at the OS prompt.</summary>
    MicrophonePermissionDenied,

    /// <summary>
    ///     Apple platforms require a separate speech-recognition permission on top of the
    ///     microphone one; this covers that second refusal.
    /// </summary>
    SpeechRecognitionPermissionDenied,

    /// <summary>No speech-to-text provider is configured, or the platform has no recognizer.</summary>
    SpeechRecognitionUnavailable,

    /// <summary>
    ///     The recognizer reported failure without a more specific error — e.g. an Apple
    ///     recognizer with no acoustic model for the active locale.
    /// </summary>
    VoiceRecognitionFailed,

    /// <summary>Wrapper for an unexpected OS permission error. <c>{0}</c> = detail.</summary>
    PermissionErrorFormat,

    /// <summary>Relative time, under a minute.</summary>
    TimeAgoJustNow,

    /// <summary>Relative time in minutes. <c>{0}</c> = count.</summary>
    TimeAgoMinutesFormat,

    /// <summary>Relative time in hours. <c>{0}</c> = count.</summary>
    TimeAgoHoursFormat,

    /// <summary>Relative time in days. <c>{0}</c> = count.</summary>
    TimeAgoDaysFormat,

    /// <summary>Relative time in weeks. <c>{0}</c> = count.</summary>
    TimeAgoWeeksFormat,

    /// <summary>Relative time in months. <c>{0}</c> = count.</summary>
    TimeAgoMonthsFormat,

    /// <summary>Relative time in years. <c>{0}</c> = count.</summary>
    TimeAgoYearsFormat,

    /// <summary>Neutral terminal message after the user cancelled an operation.</summary>
    Cancelled,

    /// <summary>
    ///     Shown when a cancellation cannot interrupt the step already running, so the user
    ///     understands the delay instead of assuming the cancel was ignored.
    /// </summary>
    CancelFinishingStep,

    /// <summary>Body of the fallback error popup when no better message is available.</summary>
    UnexpectedError
}
