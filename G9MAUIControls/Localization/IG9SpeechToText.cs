using System.Globalization;

namespace G9MAUIControls.Localization;

/// <summary>
///     Voice input for <c>G9SearchEntry</c>'s microphone button, as a plug-in.
///     <para>
///         <b>Why this is not built in.</b> Speech recognition means a native permission on every
///         platform (<c>RECORD_AUDIO</c>; <c>NSMicrophoneUsageDescription</c> plus
///         <c>NSSpeechRecognitionUsageDescription</c>), a platform recognizer package, and an app
///         store privacy declaration. A UI control library must not impose any of that on a
///         consumer who never shows a microphone. So the mic button is <b>hidden unless a
///         provider is registered</b>, and registering one is an explicit, informed choice.
///     </para>
///     <para>
///         Any recognizer works — <c>CommunityToolkit.Maui.Media.SpeechToText</c> is the obvious
///         one, but so is a platform API or a cloud service. Implement this interface, hand it to
///         <see cref="G9Speech.Provider" />, and the mic appears.
///     </para>
///     <example>
///         <code>
///         // MauiProgram (with CommunityToolkit.Maui.Media)
///         G9Speech.Provider = new ToolkitSpeechProvider();
///
///         sealed class ToolkitSpeechProvider : IG9SpeechToText
///         {
///             public bool IsSupported =&gt; true;
///
///             public async Task&lt;bool&gt; RequestPermissionAsync(CancellationToken ct) =&gt;
///                 await SpeechToText.Default.RequestPermissions(ct);
///
///             public async Task&lt;G9SpeechResult&gt; ListenAsync(
///                 CultureInfo culture, IProgress&lt;string&gt;? partial, CancellationToken ct)
///             {
///                 try
///                 {
///                     var text = await SpeechToText.Default.ListenAsync(culture, partial, ct);
///                     return G9SpeechResult.Recognized(text);
///                 }
///                 catch (OperationCanceledException) { return G9SpeechResult.Cancelled(); }
///                 catch (Exception ex)                { return G9SpeechResult.Failed(ex.Message); }
///             }
///         }
///         </code>
///     </example>
/// </summary>
public interface IG9SpeechToText
{
    /// <summary>
    ///     Whether this device can recognize speech at all. Return <c>false</c> and the mic button
    ///     stays hidden — better than offering a control that cannot work.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    ///     Requests whatever permissions the recognizer needs. Called on the mic tap, before
    ///     <see cref="ListenAsync" />. Return <c>false</c> if the user refused.
    /// </summary>
    Task<bool> RequestPermissionAsync(CancellationToken cancellationToken);

    /// <summary>
    ///     Listens until the recognizer finishes, the user stops it, or
    ///     <paramref name="cancellationToken" /> fires.
    /// </summary>
    /// <param name="culture">
    ///     The locale to recognize in — normally <see cref="G9Culture.CurrentCulture" />. Not
    ///     every platform ships an acoustic model for every locale; report that as
    ///     <see cref="G9SpeechResult.Failed" /> rather than throwing.
    /// </param>
    /// <param name="partialResult">
    ///     Receives interim transcripts, which the entry shows live so the user can see it is
    ///     listening. May be <c>null</c>.
    /// </param>
    /// <param name="cancellationToken">Cancelled when the user taps the mic again, or the field unloads.</param>
    Task<G9SpeechResult> ListenAsync(
        CultureInfo culture,
        IProgress<string>? partialResult,
        CancellationToken cancellationToken);
}

/// <summary>The outcome of one <see cref="IG9SpeechToText.ListenAsync" /> session.</summary>
/// <param name="Status">How the session ended.</param>
/// <param name="Text">The final transcript when <paramref name="Status" /> is <see cref="G9SpeechStatus.Recognized" />.</param>
/// <param name="ErrorMessage">
///     A human-readable reason when <paramref name="Status" /> is <see cref="G9SpeechStatus.Failed" />.
///     Surfaced to the consumer through the entry's <c>VoiceFailed</c> event, not shown by the
///     control itself — where and how to tell the user is an app decision.
/// </param>
public readonly record struct G9SpeechResult(G9SpeechStatus Status, string? Text, string? ErrorMessage)
{
    /// <summary>A completed session with a transcript.</summary>
    public static G9SpeechResult Recognized(string? text) => new(G9SpeechStatus.Recognized, text, null);

    /// <summary>The user or the control stopped the session.</summary>
    public static G9SpeechResult Cancelled() => new(G9SpeechStatus.Cancelled, null, null);

    /// <summary>The recognizer failed.</summary>
    public static G9SpeechResult Failed(string? errorMessage) => new(G9SpeechStatus.Failed, null, errorMessage);

    /// <summary>The user refused a required permission.</summary>
    public static G9SpeechResult PermissionDenied() => new(G9SpeechStatus.PermissionDenied, null, null);
}

/// <summary>How a speech session ended.</summary>
public enum G9SpeechStatus
{
    /// <summary>Speech was transcribed.</summary>
    Recognized,

    /// <summary>The session was stopped before producing a result.</summary>
    Cancelled,

    /// <summary>The recognizer failed — no model for the locale, no network, a native error.</summary>
    Failed,

    /// <summary>A required OS permission was refused.</summary>
    PermissionDenied
}

/// <summary>
///     Holds the app's speech-to-text provider. Leave <see cref="Provider" /> null (the default)
///     and every microphone affordance in the suite stays hidden.
/// </summary>
public static class G9Speech
{
    /// <summary>
    ///     The recognizer to use, or <c>null</c> for none. Set once at startup, before the first
    ///     page loads.
    /// </summary>
    public static IG9SpeechToText? Provider { get; set; }

    /// <summary>True when a provider is registered and reports the device can recognize speech.</summary>
    public static bool IsAvailable => Provider?.IsSupported == true;
}
