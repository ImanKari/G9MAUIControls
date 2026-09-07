using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using System.Globalization;
using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

/// <summary>
///     Outlined text input. Inherits the shared outline + notched-label + icon-padding
///     architecture from <see cref="G9OutlinedFieldBase" /> so all input-like controls
///     stay visually consistent. Adds: password toggle, clear button, max length, validators,
///     keyboard type, semantic input typing (number / email / Persian letters / etc.) and
///     explicit input flow direction.
///     <para>
///         <b>Use <see cref="InputType" /> to drive the keyboard, live keystroke filter,
///         and on-blur validation in one go.</b> See <see cref="G9InputType" /> for the
///         full enum vocabulary. The legacy <see cref="KeyboardType" /> property still
///         works (it picks the on-screen keyboard only) but new code should set
///         <see cref="InputType" /> instead — it's the strict superset.
///     </para>
///     // TODO (palette step): outline / focus-ring colors are inherited from the base.
/// </summary>
public partial class G9TextEntry : G9OutlinedFieldBase
{
    private readonly Entry _entry;
    private bool _syncingText;
    private bool _passwordVisible;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnTextChanged))]
    private string? _text;

    [AutoBindable(OnChanged = nameof(OnPasswordToggleChanged))] private bool _passwordToggle;
    [AutoBindable(OnChanged = nameof(OnIsPasswordChanged))] private bool _isPassword;
    [AutoBindable(OnChanged = nameof(OnClearButtonChanged))] private bool _clearButton;
    [AutoBindable(OnChanged = nameof(OnKeyboardTypeChanged))] private G9KeyboardType _keyboardType;

    /// <summary>
    ///     Semantic input typing — drives the on-screen keyboard, the live keystroke
    ///     filter (rejected characters never reach <see cref="Text" />), and the on-blur
    ///     validation. See <see cref="G9InputType" /> for the full vocabulary. Use
    ///     <see cref="G9InputType.Custom" /> together with <see cref="AllowedCharsPattern" />
    ///     and <see cref="ValidationPattern" /> for project-specific rules.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnInputTypeChanged))] private G9InputType _inputType;

    /// <summary>
    ///     Regex pattern for the live keystroke filter when <see cref="InputType" /> is
    ///     <see cref="G9InputType.Custom" />. Specify a single-character class like
    ///     <c>"[A-Za-z0-9_]"</c>; the filter applies it per-character so anything
    ///     non-matching is dropped before it reaches <see cref="Text" />.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnInputTypeChanged))] private string? _allowedCharsPattern;

    /// <summary>
    ///     Regex run on focus loss when <see cref="InputType" /> is
    ///     <see cref="G9InputType.Custom" /> (or alongside the built-in email / URL
    ///     validators). When the value doesn't match, <see cref="ValidationErrorText" />
    ///     (or the built-in default) is surfaced via
    ///     <see cref="G9OutlinedFieldBase.ErrorText" />.
    /// </summary>
    [AutoBindable] private string? _validationPattern;

    /// <summary>
    ///     Custom error message shown when the field's value fails the
    ///     <see cref="InputType" /> validation. When null, a localized default is shown
    ///     (e.g. "Invalid email" / "Invalid URL" / "Invalid value").
    /// </summary>
    [AutoBindable] private string? _validationErrorText;

    [AutoBindable(OnChanged = nameof(OnInputDirectionChanged))] private G9TextInputDirection _inputTextDirection;
    [AutoBindable(OnChanged = nameof(OnFontChanged))] private string? _customFont;
    [AutoBindable] private IG9TextValidator? _validator;
    [AutoBindable] private bool _validateOnTextChanged;

    /// <summary>
    ///     Offers a microphone in the trailing slot that dictates straight into
    ///     <see cref="Text" />. <b>Off by default</b> on a general text entry — a microphone is a
    ///     promise the app has to keep (a permission prompt, a privacy declaration), so a field
    ///     opts in. <c>G9SearchEntry</c> turns it on for itself.
    ///     <para>
    ///         The affordance also needs a registered <see cref="G9Speech.Provider" />: with none,
    ///         the microphone stays hidden rather than offering a control that can only fail. See
    ///         <see cref="IG9SpeechToText" />.
    ///     </para>
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVoiceEnabledChanged))] private bool _voiceEnabled;

    /// <summary>
    ///     Locale to recognize in. Null follows the app's active language, which is what a field
    ///     normally wants; set it when the spoken language differs from the UI language (an
    ///     English-only crop database inside a Persian app).
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVoiceCultureChanged))] private CultureInfo? _voiceCulture;

    /// <summary>
    ///     Stable, lazily-built microphone icon reused across every
    ///     <see cref="ResolveTrailingIcon" /> call. Building a fresh <see cref="G9IconView" /> each
    ///     time costs the platform a frame to load the glyph from the embedded font, which the user
    ///     sees as a tofu-rectangle flash on tap. One instance, mutated in place — the same trick
    ///     <c>G9ChipGroup</c> and <c>G9TabView</c> use (<c>G9Controls.md</c> principle 12).
    /// </summary>
    private G9IconView? _voiceIcon;

    private G9VoiceDictation? _voice;

    public G9TextEntry()
    {
        _entry = new Entry
        {
            StyleId = "no-underline",
            BackgroundColor = Colors.Transparent,
            Placeholder = string.Empty,
            FontSize = 15,
            VerticalOptions = LayoutOptions.Center,
            ClearButtonVisibility = ClearButtonVisibility.Never,
            HeightRequest = G9Metrics.ControlHeight - 4
        };
        _entry.TextChanged += OnInnerTextChanged;
        _entry.Focused += OnInnerFocusChanged;
        _entry.Unfocused += OnInnerFocusChanged;

        InputType = G9InputType.Default;
        KeyboardType = G9KeyboardType.Default;
        InputTextDirection = G9TextInputDirection.MatchParent;
    }

    public Entry InnerEntry => _entry;

    /// <summary>
    ///     The dictation session this field drives, created on first use so a field that never
    ///     shows a microphone allocates nothing.
    /// </summary>
    private G9VoiceDictation Voice
    {
        get
        {
            if (_voice is not null)
            {
                return _voice;
            }

            _voice = new G9VoiceDictation(
                () => Text,
                value => Text = value,
                RequestVisualUpdate)
            {
                Culture = VoiceCulture
            };

            _voice.ListeningStarted += (_, _) => VoiceListeningStarted?.Invoke(this, EventArgs.Empty);
            _voice.ListeningEnded += (_, _) => VoiceListeningEnded?.Invoke(this, EventArgs.Empty);
            _voice.Failed += (_, message) => VoiceFailed?.Invoke(this, message);

            return _voice;
        }
    }

    /// <summary>Raised when a dictation session starts — hosts can show a "listening…" hint.</summary>
    public event EventHandler? VoiceListeningStarted;

    /// <summary>Raised when a dictation session ends (result, cancellation, or error).</summary>
    public event EventHandler? VoiceListeningEnded;

    /// <summary>
    ///     Raised when dictation could not run or did not finish (no recognizer, refused
    ///     permission, no acoustic model for the locale). Carries a localized message; the host
    ///     decides where to show it — a toast, usually.
    /// </summary>
    public event EventHandler<string>? VoiceFailed;

    /// <summary>True while this field is dictating.</summary>
    public bool IsListening => _voice?.IsListening == true;

    /// <summary>
    ///     Starts dictation, or stops the running session. The trailing microphone calls this;
    ///     exposed publicly so a hardware key or an external button can drive it too.
    /// </summary>
    public Task ToggleVoiceAsync() => Voice.ToggleAsync();

    /// <summary>Begins a dictation session. Appends to whatever the field already holds.</summary>
    public Task StartVoiceAsync() => Voice.StartAsync();

    /// <summary>Stops an in-flight dictation session. Safe to call when none is running.</summary>
    public Task StopVoiceAsync() => Voice.StopAsync();

    protected override View BuildInnerContent() => _entry;

    /// <summary>The platform-focusable inner element for the wrapper-level tap-to-focus.</summary>
    protected override VisualElement? FocusTarget => _entry;

    protected override bool IsContentFocused => _entry?.IsFocused == true;
    protected override bool HasContentValue => !string.IsNullOrEmpty(Text);
    /// <summary>
    ///     Both trailing affordances are VALUE-GATED: an empty field shows neither, and — because
    ///     this same predicate is what reserves the trailing room — an empty field does not reserve
    ///     space for one either. That matters most in RTL, where the placeholder sits on the trailing
    ///     side: a reserved-but-empty slot would inset the placeholder and then let it snap back on
    ///     the first keystroke.
    ///     <para>
    ///         The eye is pointless on an empty password (there is nothing to reveal) and it is
    ///         visual noise on the very first screen of the app.
    ///     </para>
    /// </summary>
    protected override bool HasExtraTrailingAffordance()
        => ((PasswordToggle || ClearButton) && HasContentValue) || ShouldShowVoiceMic();

    protected override int GetTextLength() => Text?.Length ?? 0;

    /// <summary>
    ///     Whether the trailing slot should currently show the microphone.
    ///     <para>
    ///         <b>The empty-field rule, plus one exception that matters.</b> The clear button owns
    ///         the slot as soon as there is a value — "if I see ×, I can clear; if I see a mic, I
    ///         can dictate" — so the microphone is an empty-field affordance. The exception is
    ///         <see cref="IsListening" />: a live session writes its transcript into the field,
    ///         which would make the microphone vanish under the user's finger the moment the first
    ///         word landed, leaving nothing to stop it with. So while listening the microphone
    ///         stays, and it is what the user taps to stop.
    ///     </para>
    ///     <para>
    ///         A password field with a value keeps its eye — revealing a password is the more
    ///         urgent affordance, and dictating one is not a thing.
    ///     </para>
    /// </summary>
    protected virtual bool ShouldShowVoiceMic()
    {
        return VoiceEnabled
               && G9VoiceDictation.IsAvailable
               && !(PasswordToggle && HasContentValue)
               && (IsListening || !HasContentValue);
    }

    protected override void OnVisibilityLost()
    {
        // Drop platform focus when the host hides (e.g. tab content toggled off) so WinUI
        // can't keep this Entry as the focused element. A focused but invisible TextBox is
        // the trigger for the "page scrolls to a hidden text field" jump on tab switches.
        if (_entry?.IsFocused == true)
        {
            try { _entry.Unfocus(); } catch { /* ignore */ }
        }
    }

    protected override void OnTrailingTap()
    {
        // Built-in callbacks are evaluated first so consumers don't have to wire eye-toggle
        // or clear-button handlers manually. TrailingCommand only fires when neither built-in
        // handler claims the tap.
        if (PasswordToggle && HasContentValue)
        {
            _passwordVisible = !_passwordVisible;
            ApplyEntryProperties();
            RequestVisualUpdate();
            return;
        }

        // Focus is handed to the inner Entry on a mic tap so the user can simply keep typing if
        // they change their mind — no second tap on the field. Mirrors the system search bars,
        // where one gesture both activates the field and starts listening.
        if (ShouldShowVoiceMic())
        {
            try { _entry.Focus(); } catch { /* ignore */ }
            _ = ToggleVoiceAsync();
            return;
        }

        if (ClearButton && !string.IsNullOrEmpty(Text))
        {
            Text = string.Empty;
            return;
        }

        base.OnTrailingTap();
    }

    protected override View? ResolveTrailingIcon(Color stateColor)
    {
        if (PasswordToggle && HasContentValue)
        {
            return G9IconFactory.Create(
                null,
                _passwordVisible ? G9Glyphs.EyeClosed : G9Glyphs.EyeOpen,
                null, null,
                stateColor, G9Metrics.InputIconSize);
        }

        if (ShouldShowVoiceMic())
        {
            // Built once, then recycled across every signature-driven rebuild so the platform
            // handler stays attached. Listening-state visuals are mutated in OnRefresh, not here —
            // see ResolveTrailingIconSignature.
            _voiceIcon ??= new G9IconView
            {
                Icon = IsListening ? G9Glyphs.MicOff : G9Glyphs.Mic,
                Color = IsListening ? G9Palette.Current.Error : G9Palette.Current.Primary,
                Size = G9Metrics.InputIconSize,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };

            return _voiceIcon;
        }

        if (ClearButton && !string.IsNullOrEmpty(Text))
        {
            return G9IconFactory.Create(null, G9Glyphs.Clear, null, null, stateColor, G9Metrics.InputIconSize);
        }

        return null;
    }

    protected override string? ResolveTrailingIconSignature(Color stateColor)
    {
        if (PasswordToggle && HasContentValue)
        {
            return $"pwd|{_passwordVisible}";
        }

        // CONSTANT across Mic ↔ MicOff on purpose. A changed signature makes the base detach the
        // previous view and attach the freshly resolved one, and the Android handler then has to
        // ship the new glyph through the typeface mapper before the next frame paints — the tofu
        // rectangle the user sees on tap. The listening visual is mutated in place in OnRefresh.
        if (ShouldShowVoiceMic())
        {
            return "voice";
        }

        if (ClearButton && !string.IsNullOrEmpty(Text))
        {
            return "clear";
        }

        return null;
    }

    private void OnTextChanged()
    {
        if (_entry is null) { RequestVisualUpdate(); return; }
        if (_syncingText) return;

        // Sanitize the bound value so programmatic Text writes (binding updates,
        // ViewModel sets, voice transcripts) honour the same input-type contract as
        // typing. If the consumer pushes "abc" into a Number field we strip it; the
        // bound property snaps back to the cleaned value below.
        var sanitized = G9InputTypePolicy.SanitizeText(InputType, Text ?? string.Empty, AllowedCharsPattern);
        if (!string.Equals(sanitized, Text, StringComparison.Ordinal))
        {
            // Defer to dispatcher so the in-flight property setter completes before we
            // re-enter it. Direct assignment here would re-fire OnTextChanged from the
            // bindable generator and re-enter the same path.
            Dispatcher.Dispatch(() =>
            {
                if (string.Equals(Text, sanitized, StringComparison.Ordinal)) return;
                Text = sanitized;
            });
            return;
        }

        _syncingText = true;
        try
        {
            _entry.Text = Text ?? string.Empty;
        }
        finally
        {
            _syncingText = false;
        }

        // A cleared password re-masks itself. The eye disappears with the value, so a latched
        // _passwordVisible would silently reveal whatever is typed next with no control to hide it.
        if (PasswordToggle && _passwordVisible && !HasContentValue)
        {
            _passwordVisible = false;
            ApplyEntryProperties();
        }

        if (ValidateOnTextChanged) Validate();
        RequestVisualUpdate();
    }

    private void OnInnerTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncingText) return;

        var raw = e.NewTextValue ?? string.Empty;
        var sanitized = G9InputTypePolicy.SanitizeText(InputType, raw, AllowedCharsPattern);

        // If the platform Entry produced characters we don't accept, snap its visible
        // text back to the sanitized version. This is what makes "Number only accepts
        // digits" work on physical keyboards and on paste — the OS-level numeric
        // keyboard alone is not sufficient (Android lets external IMEs feed any
        // characters to numeric fields).
        if (!string.Equals(sanitized, raw, StringComparison.Ordinal))
        {
            _syncingText = true;
            try
            {
                _entry.Text = sanitized;
                // Move the caret to the end of the sanitized text so subsequent typing
                // appends rather than landing inside a stripped region.
                _entry.CursorPosition = sanitized.Length;
                _entry.SelectionLength = 0;
            }
            finally
            {
                _syncingText = false;
            }
        }

        _syncingText = true;
        try
        {
            Text = sanitized;
        }
        finally
        {
            _syncingText = false;
        }

        if (ValidateOnTextChanged) Validate();
        RequestVisualUpdate();
    }

    private void OnInnerFocusChanged(object? sender, FocusEventArgs e)
    {
        // Blur-validation + deferred visual refresh are handled by the shared base flow
        // (G9OutlinedFieldBase.HandleInnerFocusChanged). The blur-time Validate() runs
        // only when ShouldAutoValidate() is true, so a field with no validation rule but
        // an externally-set HasError / ErrorText keeps its error across focus changes
        // (the "focus/unfocus wipes the error" bug). Email / URL / Custom fields still
        // validate on blur so the user sees the message as soon as they tab away.
        HandleInnerFocusChanged(e.IsFocused, () => _entry?.IsFocused == true);
    }

    /// <inheritdoc />
    protected override bool ShouldAutoValidate()
    {
        if (Validator is not null) return true;
        if (InputType is G9InputType.Email or G9InputType.Url) return true;
        if (InputType == G9InputType.Custom && !string.IsNullOrEmpty(ValidationPattern)) return true;
        return false;
    }

    /// <inheritdoc />
    protected override bool RunValidation() => Validate();

    private void OnPasswordToggleChanged()
    {
        // The entered password value always flows left-to-right (see ResolveInputFlowDirection),
        // so the show/hide-password eye toggle reads most naturally on the physical-right edge in
        // BOTH directions — same place it sits in LTR. Without this, RTL would push the eye icon
        // to the physical-left (reading-start) edge, away from where the value ends. Pin it right,
        // mirroring G9BarcodeTextEntry. We only force-on (never clobber a subclass that pinned
        // it for its own reasons, e.g. barcode entry).
        if (PasswordToggle)
        {
            ForceTrailingIconRight = true;
        }

        ApplyEntryProperties();
        RequestVisualUpdate();
    }
    private void OnIsPasswordChanged() { ApplyEntryProperties(); RequestVisualUpdate(); }
    private void OnClearButtonChanged() => RequestVisualUpdate();
    private void OnVoiceEnabledChanged() => RequestVisualUpdate();

    private void OnVoiceCultureChanged()
    {
        // Only touch the session if one was ever created — reading the property would build it.
        if (_voice is not null)
        {
            _voice.Culture = VoiceCulture;
        }
    }
    private void OnKeyboardTypeChanged() => ApplyEntryProperties();
    private void OnInputDirectionChanged() => ApplyEntryProperties();
    private void OnFontChanged() => ApplyEntryProperties();

    private void OnInputTypeChanged()
    {
        // The semantic InputType supersedes the legacy KeyboardType. Mirror the resolved
        // keyboard back into KeyboardType so existing bindings on KeyboardType remain
        // consistent when the consumer sets InputType.
        var resolvedKeyboard = G9InputTypePolicy.ResolveKeyboard(InputType);
        if (KeyboardType != resolvedKeyboard) KeyboardType = resolvedKeyboard;
        ApplyEntryProperties();

        // Re-sanitize the current text whenever the input type changes — flipping
        // Default → Number on a field that already has "abc 123" should clear the
        // letters immediately.
        if (!string.IsNullOrEmpty(Text))
        {
            var sanitized = G9InputTypePolicy.SanitizeText(InputType, Text!, AllowedCharsPattern);
            if (!string.Equals(sanitized, Text, StringComparison.Ordinal))
            {
                Text = sanitized;
            }
        }
    }

    public bool Validate()
    {
        var message = Validator?.Validate(Text);
        message ??= G9InputTypePolicy.Validate(InputType, Text, ValidationPattern, ValidationErrorText);

        if (!string.IsNullOrWhiteSpace(message))
        {
            ErrorText = message;
            HasError = true;
            return false;
        }

        HasError = false;
        return true;
    }

    private void ApplyEntryProperties()
    {
        if (_entry is null) return;

        var palette = G9Palette.Current;

        // Defensive equality checks before each platform property write. On WinUI, mutating
        // platform Entry properties (especially IsPassword which swaps TextBox <-> PasswordBox
        // at the platform layer) while the field is still inside a GotFocus / LostFocus
        // event dispatch can crash AOT with System.ExecutionEngineException. By skipping
        // no-op writes we eliminate the bulk of the cross-fire.
        var targetIsReadOnly = IsReadOnly;
        if (_entry.IsReadOnly != targetIsReadOnly) _entry.IsReadOnly = targetIsReadOnly;

        var targetIsPassword = IsPassword && !_passwordVisible;
        if (_entry.IsPassword != targetIsPassword) _entry.IsPassword = targetIsPassword;

        var targetMaxLength = MaxLength <= 0 ? int.MaxValue : MaxLength;
        if (_entry.MaxLength != targetMaxLength) _entry.MaxLength = targetMaxLength;

        // Resolve the platform Keyboard via the input-type policy so Number / Decimal /
        // Phone / etc. all map to the right on-screen keyboard. KeyboardType is kept in
        // sync (see OnInputTypeChanged) but the policy is the single source of truth.
        var resolvedKeyboardType = G9InputTypePolicy.ResolveKeyboard(InputType);
        if (KeyboardType == G9KeyboardType.Password) resolvedKeyboardType = G9KeyboardType.Default;
        var targetKeyboard = G9Visuals.ResolveKeyboard(resolvedKeyboardType);
        if (!ReferenceEquals(_entry.Keyboard, targetKeyboard)) _entry.Keyboard = targetKeyboard;

        var targetFont = !string.IsNullOrWhiteSpace(CustomFont)
            ? CustomFont
            : G9Visuals.ResolveCulturalFont();
        if (!string.Equals(_entry.FontFamily, targetFont, StringComparison.Ordinal)) _entry.FontFamily = targetFont;

        var targetTextColor = IsEnabled ? palette.TextPrimary : palette.TextDisabled;
        if (_entry.TextColor != targetTextColor) _entry.TextColor = targetTextColor;

        var targetFlow = ResolveInputFlowDirection();
        if (_entry.FlowDirection != targetFlow) _entry.FlowDirection = targetFlow;
    }

    protected override void OnRefresh()
    {
        RefreshVoiceIcon();

        if (_entry is null) return;

        if (!_syncingText)
        {
            _syncingText = true;
            try
            {
                var target = Text ?? string.Empty;
                // Only write to the platform Entry if the text actually differs. Writing the
                // same value back during a TextChanged / Focused / Unfocused dispatch can
                // cause WinUI to re-enter the platform TextBox while it is still draining
                // the original event, which is one of the known triggers for the AOT
                // ExecutionEngineException seen on .NET 10 + WinUI.
                if (_entry.Text != target)
                {
                    _entry.Text = target;
                }
            }
            finally
            {
                _syncingText = false;
            }
        }

        ApplyEntryProperties();
    }

    /// <summary>
    ///     Runs AFTER the base has refreshed the trailing icon colour, because
    ///     <c>UpdateIconColor</c> writes the field's per-state colour over the microphone whenever
    ///     the signature is unchanged — which, by design, it always is across a listen toggle. So
    ///     the brand accent is re-asserted here (Primary idle, Error while listening) and the glyph
    ///     is swapped in place, keeping the platform handler attached.
    /// </summary>
    private void RefreshVoiceIcon()
    {
        if (_voiceIcon is null || !ShouldShowVoiceMic())
        {
            return;
        }

        var palette = G9Palette.Current;
        var listening = IsListening;
        var targetIcon = listening ? G9Glyphs.MicOff : G9Glyphs.Mic;
        var targetColor = listening ? palette.Error : palette.Primary;

        if (!Equals(_voiceIcon.Icon, targetIcon)) _voiceIcon.Icon = targetIcon;
        if (_voiceIcon.Color != targetColor) _voiceIcon.Color = targetColor;
    }

    private FlowDirection ResolveInputFlowDirection()
    {
        // Explicit consumer override always wins.
        if (InputTextDirection == G9TextInputDirection.LeftToRight)
        {
            return FlowDirection.LeftToRight;
        }
        if (InputTextDirection == G9TextInputDirection.RightToLeft)
        {
            return FlowDirection.RightToLeft;
        }

        // MatchParent path: numbers, phone numbers, emails, URLs and passwords are
        // universally entered left-to-right. Forcing them through the parent's RTL
        // flow would mirror "+98 21 1234" into "1234 21 89+" and stash the leading
        // '+' on the wrong side of the box. The floating label, outline notch,
        // helper text and icon placement still follow the parent's culture — only
        // the entered value and its caret are pinned LTR. Consumers who genuinely
        // want the entered value to flow with the page culture can opt out by
        // setting <see cref="InputTextDirection" /> to
        // <see cref="G9TextInputDirection.RightToLeft" />.
        if (G9InputTypePolicy.PrefersLeftToRight(InputType) || IsPassword)
        {
            return FlowDirection.LeftToRight;
        }

        return G9Visuals.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
    }
}
