using System.Globalization;

namespace G9MAUIControls.Localization;

/// <summary>
///     One dictation session, driven against the registered <see cref="G9Speech.Provider" /> and
///     detached from any particular control.
///     <para>
///         <b>Why this is a class and not a base control.</b> Three controls in the suite offer a
///         microphone — <c>G9SearchEntry</c>, <c>G9TextEntry</c> and <c>G9Editor</c> — and they do
///         not share a base class that could own the behaviour (<c>G9SearchEntry : G9TextEntry</c>,
///         but <c>G9Editor</c> is a sibling under <c>G9OutlinedFieldBase</c>, whose job is the
///         outline / notch / icon-slot machinery, not speech). The session was originally written
///         inline in <c>G9SearchEntry</c>; adding a second and a third copy is how the permission
///         re-check, the cancellation contract and the append semantics drift apart. It lives here
///         instead, and each control owns only its own microphone <em>affordance</em>.
///     </para>
///     <para>
///         <b>The recognizer stays a plug-in.</b> Nothing here references a speech package. With no
///         <see cref="G9Speech.Provider" /> registered <see cref="StartAsync" /> reports
///         <see cref="Failed" /> and does nothing else, and <see cref="IsAvailable" /> is false so a
///         host can hide the microphone rather than offer a control that cannot work. See
///         <see cref="IG9SpeechToText" />.
///     </para>
///     <example>
///         <code>
///         _voice = new G9VoiceDictation(
///             readText: () =&gt; Text,
///             writeText: value =&gt; Text = value,
///             onStateChanged: RequestVisualUpdate);
///
///         _voice.Failed += (_, message) =&gt; VoiceFailed?.Invoke(this, message);
///         </code>
///     </example>
/// </summary>
public sealed class G9VoiceDictation
{
    private readonly Func<string?> _readText;
    private readonly Action _onStateChanged;
    private readonly Action<string> _writeText;

    private string? _baseText;
    private CancellationTokenSource? _cancellation;

    /// <summary>Creates a session bound to one field's text.</summary>
    /// <param name="readText">Reads the field's current value. Called once, when a session starts.</param>
    /// <param name="writeText">
    ///     Writes a transcript back. Called for every partial result and once more for the final one,
    ///     always on the thread the session was started from.
    /// </param>
    /// <param name="onStateChanged">
    ///     Invoked whenever <see cref="IsListening" /> flips, so the host can re-resolve its
    ///     microphone visual. Typically <c>RequestVisualUpdate</c>.
    /// </param>
    public G9VoiceDictation(Func<string?> readText, Action<string> writeText, Action onStateChanged)
    {
        _readText = readText ?? throw new ArgumentNullException(nameof(readText));
        _writeText = writeText ?? throw new ArgumentNullException(nameof(writeText));
        _onStateChanged = onStateChanged ?? throw new ArgumentNullException(nameof(onStateChanged));
    }

    /// <summary>True while a session is running. Hosts paint their microphone from this.</summary>
    public bool IsListening { get; private set; }

    /// <summary>
    ///     Locale to recognize in. Null means <see cref="G9Culture.CurrentCulture" />, which is what
    ///     nearly every field wants — set it only when the spoken language should differ from the UI
    ///     language.
    /// </summary>
    public CultureInfo? Culture { get; set; }

    /// <summary>True when a provider is registered AND reports this device can recognize speech.</summary>
    public static bool IsAvailable => G9Speech.IsAvailable;

    /// <summary>Raised when a session starts — a host may show a "listening…" hint.</summary>
    public event EventHandler? ListeningStarted;

    /// <summary>Raised when a session ends, for any reason (result, cancel, failure).</summary>
    public event EventHandler? ListeningEnded;

    /// <summary>
    ///     Raised when a session could not run or did not finish: no provider, a refused permission,
    ///     no acoustic model for the locale, a native error. Carries an already-localized message;
    ///     WHERE to show it is the host's decision, so nothing is displayed from here.
    /// </summary>
    public event EventHandler<string>? Failed;

    /// <summary>Starts a session, or stops the running one. This is what a microphone tap calls.</summary>
    public Task ToggleAsync() => IsListening ? StopAsync() : StartAsync();

    /// <summary>
    ///     Begins a session. Safe to call when one is already running (it returns immediately).
    ///     <para>
    ///         Partial transcripts are appended to whatever the field held BEFORE the session
    ///         started, never used to replace it — tapping the microphone mid-sentence means "keep
    ///         going", not "start over". That is also why the base text is captured here rather than
    ///         re-read per partial: the field is being written to as we go.
    ///     </para>
    /// </summary>
    public async Task StartAsync()
    {
        if (IsListening)
        {
            return;
        }

        var provider = G9Speech.Provider;
        if (provider is null || !provider.IsSupported)
        {
            Failed?.Invoke(this, G9Strings.Get(G9StringKey.SpeechRecognitionUnavailable));
            return;
        }

        _cancellation = new CancellationTokenSource();

        try
        {
            // Always re-checked: the permission may have been revoked between sessions.
            if (!await provider.RequestPermissionAsync(_cancellation.Token).ConfigureAwait(true))
            {
                Failed?.Invoke(this, G9Strings.Get(G9StringKey.MicrophonePermissionDenied));
                Reset();
                return;
            }
        }
        catch (OperationCanceledException)
        {
            Reset();
            return;
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, G9Strings.Format(G9StringKey.PermissionErrorFormat, ex.Message));
            Reset();
            return;
        }

        IsListening = true;
        _baseText = _readText() ?? string.Empty;
        ListeningStarted?.Invoke(this, EventArgs.Empty);
        _onStateChanged();

        var partial = new Progress<string>(Apply);

        try
        {
            var result = await provider
                .ListenAsync(Culture ?? G9Culture.CurrentCulture, partial, _cancellation.Token)
                .ConfigureAwait(true);

            switch (result.Status)
            {
                case G9SpeechStatus.Recognized:
                    if (!string.IsNullOrEmpty(result.Text))
                    {
                        Apply(result.Text!);
                    }

                    break;

                case G9SpeechStatus.PermissionDenied:
                    Failed?.Invoke(this, G9Strings.Get(G9StringKey.MicrophonePermissionDenied));
                    break;

                case G9SpeechStatus.Failed:
                    // A recognizer with no acoustic model for the active locale lands here — an
                    // expected outcome on some platform/language pairs, not a defect.
                    Failed?.Invoke(this, result.ErrorMessage ?? G9Strings.Get(G9StringKey.VoiceRecognitionFailed));
                    break;

                case G9SpeechStatus.Cancelled:
                default:
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // The user tapped the microphone again, or the field unloaded. Not a failure.
        }
        catch (Exception ex)
        {
            Failed?.Invoke(this, ex.Message);
        }
        finally
        {
            Reset();
        }
    }

    /// <summary>Stops the running session. Safe to call when none is running.</summary>
    public async Task StopAsync()
    {
        if (!IsListening)
        {
            return;
        }

        try
        {
            if (_cancellation is { } cancellation)
            {
                await cancellation.CancelAsync().ConfigureAwait(true);
            }
        }
        catch (Exception)
        {
            // Best-effort stop; Reset() cleans up regardless. A recognizer that throws on
            // cancellation must not leave the field stuck in its listening visual — which is the
            // state where the user can no longer reach the button that would have fixed it.
        }

        Reset();
    }

    private void Apply(string transcript)
    {
        _writeText(string.IsNullOrEmpty(_baseText)
            ? transcript
            : $"{_baseText} {transcript}".TrimEnd());
    }

    private void Reset()
    {
        var wasListening = IsListening;

        IsListening = false;
        _baseText = null;
        _cancellation?.Dispose();
        _cancellation = null;

        if (wasListening)
        {
            ListeningEnded?.Invoke(this, EventArgs.Empty);
        }

        _onStateChanged();
    }
}
