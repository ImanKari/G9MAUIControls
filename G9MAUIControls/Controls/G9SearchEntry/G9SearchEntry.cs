using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using System.Globalization;
using System.Windows.Input;

using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

/// <summary>
///     Search-flavored input — a thin specialization of <see cref="G9TextEntry" /> that
///     ships with the Material 3 search-bar defaults already configured:
///     <list type="bullet">
///         <item><see cref="G9OutlinedFieldBase.LeadingIcon" /> defaults to <see cref="G9Glyphs.Search" />.</item>
///         <item><see cref="G9TextEntry.ClearButton" /> defaults to <c>true</c>.</item>
///         <item><see cref="G9OutlinedFieldBase.Placeholder" /> defaults to a localized
///               "Search…" string.</item>
///     </list>
///     <para>
///         The control adds two search-specific behaviors that don't belong on the
///         general-purpose text entry:
///     </para>
///     <list type="number">
///         <item>
///             <b>Debounced query.</b> A debounce timer (<see cref="DebounceMs" />,
///             default 250 ms) starts on every <see cref="G9TextEntry.Text" /> change
///             and only fires <see cref="DebouncedTextChanged" /> + the
///             <see cref="SearchCommand" /> when the user has been still for the
///             debounce window. Avoids one-query-per-keystroke spam against the
///             underlying list / API. Set <see cref="DebounceMs" /> to <c>0</c> to
///             fire instantly with no debounce.
///         </item>
///         <item>
///             <b>Built-in voice dictation.</b> When <see cref="VoiceEnabled" /> is true
///             the trailing slot shows a mic icon while the field is empty. Tapping it
///             requests <see cref="Microsoft.Maui.ApplicationModel.Permissions.Microphone" />
///             then drives a <c>SpeechToText</c>
///             session in the active culture, streaming partial results into
///             <see cref="G9TextEntry.Text" /> as the user speaks. Android speaks
///             Persian directly via Google's recognizer (<c>fa-IR</c>). iOS does NOT
///             ship a Persian acoustic model in <c>SFSpeechRecognizer</c>, so on iOS
///             the recognizer call simply errors out for Persian — Persian users on
///             iOS should rely on the keyboard's dictation mic instead.
///         </item>
///     </list>
/// </summary>
public partial class G9SearchEntry : G9TextEntry
{
    private IDispatcherTimer? _debounceTimer;
    private CancellationTokenSource? _voiceCts;
    private bool _isListening;
    private string? _voiceBaseText;

    /// <summary>
    ///     Stable, lazily-built mic <see cref="G9IconView" /> instance
    ///     reused across every <see cref="ResolveTrailingIcon" /> call. Building a
    ///     fresh <c>G9IconView</c> every time the trailing slot re-resolves needs the
    ///     platform a frame to load the glyph from the embedded font, which the user
    ///     sees as a brief tofu-rectangle flash when tapping the icon. Keeping a
    ///     single instance and mutating <see cref="G9IconView.Icon" /> /
    ///     <see cref="G9IconView.Color" /> on listen-state changes
    ///     means the platform never has to re-rasterise the glyph, so the icon
    ///     transitions in place with no visible gap. Same trick already in use by
    ///     <c>G9ChipGroup</c> and <c>G9TabView</c> for the same reason.
    /// </summary>
    private G9IconView? _voiceIcon;

    /// <summary>
    ///     Debounce window for <see cref="DebouncedTextChanged" /> and
    ///     <see cref="SearchCommand" />. The default of 250 ms is the typical instant-search
    ///     sweet spot — short enough to feel responsive while typing, long enough to
    ///     coalesce a fast typist's keystrokes into a single query. Set to <c>0</c> to
    ///     disable debouncing and fire on every keystroke (e.g. when the underlying list
    ///     filter is purely client-side and cheap).
    /// </summary>
    [AutoBindable] private int _debounceMs;

    /// <summary>
    ///     When false, the trailing voice-mic affordance is suppressed regardless of
    ///     platform support. Use to disable voice input on screens where it doesn't make
    ///     sense (e.g. typed barcode lookup) or when you want to keep the field visually
    ///     uncluttered.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVoiceEnabledChanged))] private bool _voiceEnabled;

    /// <summary>
    ///     Optional override for the voice-recognition culture. When null we use
    ///     <see cref="System.Globalization.CultureInfo.CurrentUICulture" />, which matches
    ///     the app's active language (so a Persian-language session naturally recognizes
    ///     Persian on Android). Set explicitly when the recognized speech should differ
    ///     from the UI language — e.g. an English-only crop database in a Persian app.
    /// </summary>
    [AutoBindable] private CultureInfo? _voiceCulture;

    /// <summary>
    ///     Optional command bound to the search action. Fired with the current
    ///     <see cref="G9TextEntry.Text" /> as the parameter when the debounce window
    ///     elapses, OR immediately when the user taps the keyboard's "Search" return key.
    /// </summary>
    [AutoBindable] private ICommand? _searchCommand;

    public G9SearchEntry()
    {
        // M3 search-bar defaults. Set BEFORE the first apply pass so the constructor
        // chain ends with the user's overrides taking precedence — any consumer-set
        // value in XAML / code arrives via the AutoBindable setter AFTER the ctor runs
        // and naturally wins.
        LeadingIcon = G9Glyphs.Search;
        ClearButton = true;
        VoiceEnabled = true;
        DebounceMs = 250;
        Placeholder = G9Strings.Get(G9StringKey.Search);
        // A search box almost always sits at the top of a list / directly under a header, where the
        // floated label would otherwise overhang the field top and be covered by that element. Reserve
        // the clearance by default. Height-matched lanes (search beside sort/filter buttons) turn this
        // OFF to keep the shared centre line — see SearchSortFilterHeader / SamplingBatch / SamplesList.
        ReserveFloatingLabelClearance = true;

        // Observe Text via the bindable PropertyChanged pipeline. G9TextEntry's Text is
        // generated by [AutoBindable] which surfaces INotifyPropertyChanged; subscribing
        // here avoids the need for a public TextChanged event on the base class. This
        // also captures programmatic Text mutations (binding updates, Submit() calls
        // from code) so the debounce + SearchCommand fire consistently regardless of
        // how the value arrived.
        PropertyChanged += OnSearchPropertyChanged;
    }

    /// <summary>
    ///     Fired after <see cref="DebounceMs" /> have elapsed since the last keystroke
    ///     (or immediately when <see cref="DebounceMs" /> is 0 / on Search-key submit).
    ///     Cheaper alternative to a manual <see cref="G9TextEntry.Text" /> binding
    ///     subscription when the consumer wants instant-search semantics.
    /// </summary>
    public event EventHandler<string?>? DebouncedTextChanged;

    /// <summary>
    ///     Raised at the start of a voice recognition session — host views can show a
    ///     "listening…" hint, dim other UI, etc.
    /// </summary>
    public event EventHandler? VoiceListeningStarted;

    /// <summary>
    ///     Raised when the voice session ends (final result, cancellation, or error).
    /// </summary>
    public event EventHandler? VoiceListeningEnded;

    /// <summary>
    ///     Raised when the voice session fails (permission denied, recognizer
    ///     unavailable, locale not supported). Carries the failure reason as a
    ///     localizable string so consumers can surface it to the user via toast.
    /// </summary>
    public event EventHandler<string>? VoiceFailed;

    public bool IsListening => _isListening;

    /// <summary>
    ///     Forces an immediate fire of <see cref="DebouncedTextChanged" /> /
    ///     <see cref="SearchCommand" /> with the current text — bypassing the debounce
    ///     window. Use when the user explicitly commits the query (Enter key, search
    ///     button, etc.).
    /// </summary>
    public void Submit()
    {
        StopDebounce();
        Fire(Text);
    }

    /// <summary>
    ///     Starts a voice session if one isn't already running, otherwise stops it. The
    ///     mic glyph in the trailing slot calls this on tap. Exposed publicly so
    ///     consumers can drive voice from a hardware key or external "search by voice"
    ///     button.
    /// </summary>
    public Task ToggleVoiceAsync()
    {
        return _isListening ? StopVoiceAsync() : StartVoiceAsync();
    }

    /// <summary>
    ///     Begins a voice session through the registered <see cref="G9Speech.Provider" />.
    ///     <para>
    ///         <b>The recognizer is a plug-in, not a dependency.</b> Speech recognition costs a
    ///         native permission on every platform, a recognizer package, and an app-store privacy
    ///         declaration — none of which a control library may impose on a consumer who never
    ///         shows a microphone. So with no provider registered the mic affordance never appears
    ///         and this method reports <see cref="VoiceFailed" /> rather than doing anything. See
    ///         <see cref="IG9SpeechToText" /> for a ready-made adapter.
    ///     </para>
    /// </summary>
    public async Task StartVoiceAsync()
    {
        if (_isListening)
        {
            return;
        }

        var provider = G9Speech.Provider;
        if (provider is null || !provider.IsSupported)
        {
            VoiceFailed?.Invoke(this, G9Strings.Get(G9StringKey.SpeechRecognitionUnavailable));
            return;
        }

        _voiceCts = new CancellationTokenSource();

        try
        {
            // Always re-checked: the user may have revoked the permission between sessions.
            if (!await provider.RequestPermissionAsync(_voiceCts.Token).ConfigureAwait(true))
            {
                VoiceFailed?.Invoke(this, G9Strings.Get(G9StringKey.MicrophonePermissionDenied));
                await ResetVoiceState().ConfigureAwait(true);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            await ResetVoiceState().ConfigureAwait(true);
            return;
        }
        catch (Exception ex)
        {
            VoiceFailed?.Invoke(this, G9Strings.Format(G9StringKey.PermissionErrorFormat, ex.Message));
            await ResetVoiceState().ConfigureAwait(true);
            return;
        }

        var culture = VoiceCulture ?? G9Culture.CurrentCulture;

        _isListening = true;
        _voiceBaseText = Text ?? string.Empty;
        VoiceListeningStarted?.Invoke(this, EventArgs.Empty);
        // Force a visual refresh so the trailing icon toggles to the "listening" state.
        RequestVisualUpdate();

        // Partial transcripts are appended to whatever the user had typed BEFORE the session
        // started. Replacing the text instead would clobber an in-progress query, which is the
        // opposite of what tapping the mic mid-search means.
        var partial = new Progress<string>(text => ApplyTranscript(text));

        try
        {
            var result = await provider
                .ListenAsync(culture, partial, _voiceCts.Token)
                .ConfigureAwait(true);

            switch (result.Status)
            {
                case G9SpeechStatus.Recognized:
                    if (!string.IsNullOrEmpty(result.Text))
                    {
                        ApplyTranscript(result.Text);
                    }

                    break;

                case G9SpeechStatus.PermissionDenied:
                    VoiceFailed?.Invoke(this, G9Strings.Get(G9StringKey.MicrophonePermissionDenied));
                    break;

                case G9SpeechStatus.Failed:
                    // A recognizer with no acoustic model for the active locale lands here — an
                    // expected outcome on some platform/language pairs, not a defect. The consumer
                    // decides how to surface it.
                    VoiceFailed?.Invoke(
                        this,
                        result.ErrorMessage ?? G9Strings.Get(G9StringKey.VoiceRecognitionFailed));
                    break;

                case G9SpeechStatus.Cancelled:
                default:
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            // The user tapped the mic again, or the field unloaded. Not a failure.
        }
        catch (Exception ex)
        {
            VoiceFailed?.Invoke(this, ex.Message);
        }
        finally
        {
            await ResetVoiceState().ConfigureAwait(true);
        }
    }

    /// <summary>Stops an in-flight voice session. Safe to call when none is running.</summary>
    public async Task StopVoiceAsync()
    {
        if (!_isListening)
        {
            return;
        }

        try
        {
            await _voiceCts!.CancelAsync().ConfigureAwait(true);
        }
        catch (Exception)
        {
            // Best-effort stop; ResetVoiceState cleans up regardless. A recognizer that throws on
            // cancellation must not leave the field stuck in its listening visual.
        }

        await ResetVoiceState().ConfigureAwait(true);
    }

    private void ApplyTranscript(string transcript)
    {
        Text = string.IsNullOrEmpty(_voiceBaseText)
            ? transcript
            : $"{_voiceBaseText} {transcript}".TrimEnd();
    }

    private Task ResetVoiceState()
    {
        _isListening = false;
        _voiceBaseText = null;
        _voiceCts?.Dispose();
        _voiceCts = null;
        VoiceListeningEnded?.Invoke(this, EventArgs.Empty);
        RequestVisualUpdate();
        return Task.CompletedTask;
    }

    private void OnSearchPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(Text)) return;

        if (DebounceMs <= 0)
        {
            Fire(Text);
            return;
        }

        StopDebounce();
        _debounceTimer ??= Dispatcher.CreateTimer();
        _debounceTimer.Interval = TimeSpan.FromMilliseconds(DebounceMs);
        _debounceTimer.IsRepeating = false;
        _debounceTimer.Tick -= OnDebounceTick;
        _debounceTimer.Tick += OnDebounceTick;
        _debounceTimer.Start();
    }

    private void OnDebounceTick(object? sender, EventArgs e)
    {
        StopDebounce();
        Fire(Text);
    }

    private void StopDebounce()
    {
        if (_debounceTimer is null) return;
        if (_debounceTimer.IsRunning) _debounceTimer.Stop();
        _debounceTimer.Tick -= OnDebounceTick;
    }

    private void Fire(string? value)
    {
        DebouncedTextChanged?.Invoke(this, value);
        if (SearchCommand is { } cmd && cmd.CanExecute(value))
        {
            cmd.Execute(value);
        }
    }

    /// <summary>
    ///     Override the inherited tap-resolution so we can claim the trailing slot for the
    ///     voice mic when <see cref="VoiceEnabled" /> is true. The clear-button takes
    ///     precedence when the field has text (matches user mental model — "if I see ×, I
    ///     can clear; if I see mic, I can dictate"); the mic only shows when the field is
    ///     empty.
    ///     <para>
    ///         Focus is handed to the inner <see cref="Entry" /> on every mic tap so the
    ///         user can immediately keep typing if they decide voice isn't what they
    ///         want — no extra tap on the field needed. Mirrors the system search bars
    ///         (Google, iOS Spotlight) where a mic tap activates the field AND starts
    ///         voice in one gesture.
    ///     </para>
    /// </summary>
    protected override void OnTrailingTap()
    {
        if (ShouldShowVoiceMic())
        {
            try { InnerEntry.Focus(); } catch { /* ignore */ }
            _ = ToggleVoiceAsync();
            return;
        }
        base.OnTrailingTap();
    }

    /// <summary>
    ///     Replace the trailing icon with a mic when the field is empty AND voice is
    ///     supported. When the field has text, defer to the base which renders the clear
    ///     button. When voice is disabled / unsupported, also defer — keeps the resting
    ///     state identical to a plain G9TextEntry with ClearButton.
    ///     <para>
    ///         <b>Stable instance.</b> The mic <see cref="G9IconView" /> is
    ///         cached in <see cref="_voiceIcon" /> and mutated in place when the
    ///         listening state flips (<c>Mic</c> ↔ <c>MicOff</c>, Primary ↔ Error).
    ///         Returning a fresh icon View on every call would make the platform
    ///         re-rasterise the glyph from the embedded font, which takes a frame —
    ///         the user sees that frame as a tofu rectangle flash on icon tap. Since
    ///         the same G9IconView instance is recycled, the platform widget stays put
    ///         and only the glyph + tint change.
    ///     </para>
    ///     <para>
    ///         The mic glyph color is intentionally pinned to <c>palette.Primary</c>
    ///         (or <c>palette.Error</c> while listening) instead of following the
    ///         field's per-state <c>stateColor</c>. Tracking <c>stateColor</c> would
    ///         force a rebuild on focus changes, defeating the cache.
    ///     </para>
    ///     <para>
    ///         <b>Glyph and color mutations on listening-state changes are NOT done
    ///         here.</b> They live in <see cref="OnRefresh" /> — which the base calls
    ///         <em>after</em> <c>RebuildIcons</c>, after the base may have written the
    ///         field's <c>stateColor</c> over our pinned color via <c>UpdateIconColor</c>.
    ///         Doing the mutation in <c>ResolveTrailingIcon</c> alone wouldn't help: the
    ///         signature is constant for the voice state (see
    ///         <see cref="ResolveTrailingIconSignature" />) so the base never re-invokes
    ///         this method on a Mic↔MicOff toggle — exactly the property we rely on to
    ///         keep the platform handler attached and avoid the tofu flash.
    ///     </para>
    /// </summary>
    protected override View? ResolveTrailingIcon(Color stateColor)
    {
        if (ShouldShowVoiceMic())
        {
            var palette = G9Palette.Current;
            // Lazily build the G9IconView View once. After the first build the same
            // instance is returned across every signature-driven rebuild (text
            // cleared → mic re-shown), keeping the platform handler attached. Any
            // listening-state visual changes happen inside OnRefresh.
            if (_voiceIcon is null)
            {
                _voiceIcon = new G9IconView {
                    Icon = _isListening ? G9Glyphs.MicOff : G9Glyphs.Mic,
                    Color = _isListening ? palette.Error : palette.Primary,
                    Size = G9Metrics.InputIconSize,
                    HorizontalOptions = LayoutOptions.Center,
                    VerticalOptions = LayoutOptions.Center
                };
            }
            return _voiceIcon;
        }
        return base.ResolveTrailingIcon(stateColor);
    }

    /// <summary>
    ///     Signature describing the trailing visual identity. The base caches by signature
    ///     to skip needless view rebuilds — when the signature is unchanged across an
    ///     <c>OnApplyVisuals</c> pass, the base only refreshes <c>IconColor</c> on the
    ///     existing view; when it changes, the base detaches the previous view from the
    ///     icon host and attaches the freshly resolved one (a 1-frame tofu rectangle as
    ///     the platform handler tears down and recreates).
    ///     <para>
    ///         The mic signature is held <b>constant</b> across both
    ///         <c>Mic</c> and <c>MicOff</c> states. We don't want the base to detach /
    ///         re-attach our cached <see cref="_voiceIcon" /> when the user taps the
    ///         mic — the listening-state visual change is mutated in place inside
    ///         <see cref="OnRefresh" /> instead. <b>Same trick used in</b>
    ///         <c>G9ChipGroup.CheckmarkIcon</c> and <c>G9TabView</c>'s tab indicator,
    ///         documented in <c>G9Controls.md</c> principle 12 (destruction-free
    ///         animations are mandatory).
    ///     </para>
    /// </summary>
    protected override string? ResolveTrailingIconSignature(Color stateColor)
    {
        if (ShouldShowVoiceMic()) return "voice";
        return base.ResolveTrailingIconSignature(stateColor);
    }

    /// <summary>
    ///     Final pass that runs <em>after</em> the base has refreshed icon colors
    ///     (<c>UpdateIconColor</c> writes the field's per-state color into the trailing
    ///     <see cref="G9IconView.Color" /> when the signature is
    ///     unchanged). For the mic we want to override that color back to the brand
    ///     accent — Primary while idle, Error while actively listening — and keep the
    ///     glyph in sync with <see cref="_isListening" /> by mutating
    ///     <see cref="G9IconView.Icon" /> in place.
    ///     <para>
    ///         Mutating in place is what eliminates the tofu rectangle flash on mic tap:
    ///         the platform handler stays attached, the embedded font is already loaded,
    ///         and only the glyph code-point + tint change. If we let the base rebuild
    ///         (signature change → detach + re-attach) the Android handler had to ship
    ///         the new glyph through the typeface mapper before the next frame painted —
    ///         that's the rectangle the user saw.
    ///     </para>
    /// </summary>
    protected override void OnRefresh()
    {
        base.OnRefresh();
        if (_voiceIcon is null) return;
        if (!ShouldShowVoiceMic()) return;

        var palette = G9Palette.Current;
        var targetIcon = _isListening ? G9Glyphs.MicOff : G9Glyphs.Mic;
        var targetColor = _isListening ? palette.Error : palette.Primary;

        if (!Equals(_voiceIcon.Icon, targetIcon)) _voiceIcon.Icon = targetIcon;
        if (_voiceIcon.Color != targetColor) _voiceIcon.Color = targetColor;
    }

    /// <summary>
    ///     Tell the base that we have an "extra" trailing affordance (the mic) so the
    ///     icon ripple / press feedback fires when the user taps the empty-state mic —
    ///     same actionable-gate logic the base applies to <c>ClearButton</c> /
    ///     <c>PasswordToggle</c>.
    /// </summary>
    protected override bool HasExtraTrailingAffordance()
    {
        return ShouldShowVoiceMic() || base.HasExtraTrailingAffordance();
    }

    /// <summary>
    ///     A resting search box is ONE muted tone: its outline matches the placeholder
    ///     and the leading search glyph — <see cref="G9Palette.InputPlaceholder" /> — rather than the
    ///     generic <see cref="G9Palette.Outline" /> hairline every other outlined field rests on.
    ///     <para>
    ///         An empty search box is an invitation, not a form field with an answer in it. The
    ///         box read as multiple greys stacked on each other — border, icon, placeholder — which
    ///         is what this collapses into one.
    ///     </para>
    ///     <para>
    ///         Only the RESTING colour changes. Focus, a typed value, error and status states all
    ///         still resolve through the base, so a search box signals those exactly like every other
    ///         input in the app.
    ///     </para>
    /// </summary>
    protected override Color ResolveRestingOutlineColor(G9Palette palette) => palette.InputPlaceholder;

    /// <inheritdoc />
    protected override Color ResolveRestingContentColor(G9Palette palette) => palette.InputPlaceholder;

    private bool ShouldShowVoiceMic()
    {
        return VoiceEnabled && string.IsNullOrEmpty(Text);
    }

    private void OnVoiceEnabledChanged() => RequestVisualUpdate();
}
