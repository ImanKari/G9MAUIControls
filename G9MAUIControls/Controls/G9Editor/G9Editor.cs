using G9MAUIControls.Icons;
using G9MAUIControls.Localization;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using System.Globalization;

namespace G9MAUIControls.Controls;

/// <summary>
///     Outlined multi-line editor. Inherits the shared outline + notched-label architecture
///     from <see cref="G9OutlinedFieldBase" />. Differs from <see cref="G9TextEntry" /> in
///     that the notch / floating label sits above the multi-line content and the box height
///     scales with <see cref="MinimumEditorHeight" /> / <see cref="AutoSize" />.
///     <para>
///         <b>Dictation.</b> Set <see cref="VoiceEnabled" /> and the editor grows a microphone in
///         its trailing slot, pinned to the BOTTOM of the box rather than centred: a text area is
///         tall, and an affordance floating in the middle of a paragraph reads as part of the text.
///         Unlike <see cref="G9TextEntry" />, the microphone stays visible whether or not the field
///         has a value — an editor has no clear button competing for the slot, and a long
///         description is exactly the thing a user wants to keep dictating into. Every transcript
///         APPENDS. The engine is shared: <see cref="G9VoiceDictation" />.
///     </para>
///     // TODO (palette step): outline / focus-ring colors are inherited from the base.
/// </summary>
public partial class G9Editor : G9OutlinedFieldBase
{
    private readonly Editor _editor;
    private bool _syncingText;

    /// <summary>
    ///     Stable, lazily-built microphone icon — one instance, mutated in place on a listen
    ///     toggle. Rebuilding it would make the platform re-rasterise the glyph, which the user
    ///     sees as a one-frame tofu rectangle. Same rule as <see cref="G9TextEntry" />.
    /// </summary>
    private G9IconView? _voiceIcon;

    private G9VoiceDictation? _voice;

    [AutoBindable(DefaultBindingMode = nameof(BindingMode.TwoWay), OnChanged = nameof(OnTextChanged))]
    private string? _text;

    [AutoBindable(OnChanged = nameof(OnEditorPropertyChanged))] private double _minimumEditorHeight;
    [AutoBindable(OnChanged = nameof(OnEditorPropertyChanged))] private EditorAutoSizeOption _autoSize;
    [AutoBindable(OnChanged = nameof(OnEditorPropertyChanged))] private bool _isSpellCheckEnabled;
    [AutoBindable(OnChanged = nameof(OnEditorPropertyChanged))] private bool _isTextPredictionEnabled;
    [AutoBindable(OnChanged = nameof(OnEditorPropertyChanged))] private G9KeyboardType _keyboardType;

    /// <summary>
    ///     Semantic input typing — drives the on-screen keyboard and the live keystroke
    ///     filter (rejected characters never reach <see cref="Text" />). See
    ///     <see cref="G9InputType" /> for the full vocabulary. Use
    ///     <see cref="G9InputType.Custom" /> together with <see cref="AllowedCharsPattern" />
    ///     for project-specific rules.
    ///     <para>
    ///         Validation-on-blur (Email / Url / Custom) is supported the same way as
    ///         <see cref="G9TextEntry" /> — see <see cref="ValidationPattern" /> and
    ///         <see cref="ValidationErrorText" />.
    ///     </para>
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnInputTypeChanged))] private G9InputType _inputType;

    /// <summary>
    ///     Regex pattern for the live keystroke filter when <see cref="InputType" /> is
    ///     <see cref="G9InputType.Custom" />. Same semantics as
    ///     <see cref="G9TextEntry.AllowedCharsPattern" />.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnInputTypeChanged))] private string? _allowedCharsPattern;

    /// <summary>
    ///     Regex run on focus loss. Same semantics as
    ///     <see cref="G9TextEntry.ValidationPattern" />.
    /// </summary>
    [AutoBindable] private string? _validationPattern;

    /// <summary>
    ///     Custom error message shown when validation fails. Same semantics as
    ///     <see cref="G9TextEntry.ValidationErrorText" />.
    /// </summary>
    [AutoBindable] private string? _validationErrorText;

    [AutoBindable(OnChanged = nameof(OnEditorPropertyChanged))] private G9TextInputDirection _inputTextDirection;
    [AutoBindable(OnChanged = nameof(OnEditorPropertyChanged))] private string? _customFont;

    /// <summary>
    ///     Offers a microphone in the trailing slot that dictates into <see cref="Text" />,
    ///     appending to whatever is already there. Off by default — a microphone costs a
    ///     permission, so the field opts in.
    ///     <para>
    ///         Needs a registered <see cref="G9Speech.Provider" />; with none the microphone stays
    ///         hidden rather than offering a control that can only fail.
    ///     </para>
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVoiceEnabledChanged))] private bool _voiceEnabled;

    /// <summary>
    ///     Locale to recognize in. Null follows the app's active language.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVoiceCultureChanged))] private CultureInfo? _voiceCulture;

    public G9Editor()
    {
        _editor = new Editor
        {
            StyleId = "no-underline",
            BackgroundColor = Colors.Transparent,
            Placeholder = string.Empty,
            FontSize = 15,
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill
        };
        _editor.TextChanged += OnInnerTextChanged;
        _editor.Focused += OnInnerFocusChanged;
        _editor.Unfocused += OnInnerFocusChanged;

        // Editors should not be a fixed-height single line; the box auto-grows.
        Box.HeightRequest = -1;
        Box.MinimumHeightRequest = 96;

        MinimumEditorHeight = 96;
        AutoSize = EditorAutoSizeOption.TextChanges;
        IsSpellCheckEnabled = true;
        IsTextPredictionEnabled = true;
        InputType = G9InputType.Default;
        KeyboardType = G9KeyboardType.Default;
        InputTextDirection = G9TextInputDirection.MatchParent;

        // The trailing slot defaults to vertically centred, which is right for a one-line entry and
        // wrong for a text area: a microphone floating beside the middle of a paragraph reads as
        // part of the text. Pin it to the bottom, level with the last line, where a "finish this
        // thought out loud" affordance belongs. Costs nothing when no trailing icon is shown — the
        // host stays collapsed.
        TrailingHost.VerticalOptions = LayoutOptions.End;
        TrailingHost.Margin = new Thickness(0, 0, 0, 8);
    }

    public Editor InnerEditor => _editor;

    /// <summary>
    ///     The dictation session this editor drives, created on first use so an editor that never
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
    ///     Raised when dictation could not run or did not finish. Carries a localized message; the
    ///     host decides where to show it.
    /// </summary>
    public event EventHandler<string>? VoiceFailed;

    /// <summary>True while this editor is dictating.</summary>
    public bool IsListening => _voice?.IsListening == true;

    /// <summary>Starts dictation, or stops the running session. The trailing microphone calls this.</summary>
    public Task ToggleVoiceAsync() => Voice.ToggleAsync();

    /// <summary>Begins a dictation session. Appends to whatever the editor already holds.</summary>
    public Task StartVoiceAsync() => Voice.StartAsync();

    /// <summary>Stops an in-flight dictation session. Safe to call when none is running.</summary>
    public Task StopVoiceAsync() => Voice.StopAsync();

    /// <summary>Editors keep a comfortable top/bottom inner padding so the floating label
    /// notch never overlaps the first line of text.</summary>
    protected override Thickness InnerContentPadding => new(0, 12, 0, 8);

    protected override View BuildInnerContent() => _editor;

    /// <summary>The platform-focusable inner element for the wrapper-level tap-to-focus.</summary>
    protected override VisualElement? FocusTarget => _editor;

    protected override bool IsContentFocused => _editor?.IsFocused == true;
    protected override bool HasContentValue => !string.IsNullOrEmpty(Text);
    protected override int GetTextLength() => Text?.Length ?? 0;

    /// <summary>
    ///     Whether the trailing microphone should be shown. Unlike <see cref="G9TextEntry" /> this
    ///     is NOT value-gated: an editor has no clear button to hand the slot over to, and the
    ///     whole point of dictating a description is being able to keep going after the first
    ///     sentence.
    /// </summary>
    private bool ShouldShowVoiceMic() => VoiceEnabled && G9VoiceDictation.IsAvailable;

    /// <inheritdoc />
    protected override bool HasExtraTrailingAffordance() => ShouldShowVoiceMic();

    /// <inheritdoc />
    protected override void OnTrailingTap()
    {
        if (ShouldShowVoiceMic())
        {
            // Focus first so the user can simply carry on typing if they change their mind.
            try { _editor.Focus(); } catch { /* ignore */ }
            _ = ToggleVoiceAsync();
            return;
        }

        base.OnTrailingTap();
    }

    /// <inheritdoc />
    protected override View? ResolveTrailingIcon(Color stateColor)
    {
        if (!ShouldShowVoiceMic())
        {
            return base.ResolveTrailingIcon(stateColor);
        }

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

    /// <inheritdoc />
    /// <remarks>
    ///     Constant across Mic ↔ MicOff, so the base never detaches and re-attaches the icon on a
    ///     listen toggle. The visual change is mutated in place in <see cref="OnRefresh" />.
    /// </remarks>
    protected override string? ResolveTrailingIconSignature(Color stateColor)
    {
        return ShouldShowVoiceMic() ? "voice" : base.ResolveTrailingIconSignature(stateColor);
    }

    protected override void OnVisibilityLost()
    {
        if (_editor?.IsFocused == true)
        {
            try { _editor.Unfocus(); } catch { /* ignore */ }
        }
    }

    private void OnTextChanged()
    {
        if (_editor is null) { RequestVisualUpdate(); return; }
        if (_syncingText) return;
        _syncingText = true;
        try
        {
            _editor.Text = Text ?? string.Empty;
        }
        finally
        {
            _syncingText = false;
        }
        RequestVisualUpdate();
    }

    private void OnInnerTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_syncingText) return;

        var raw = e.NewTextValue ?? string.Empty;
        var sanitized = G9InputTypePolicy.SanitizeText(InputType, raw, AllowedCharsPattern);

        if (!string.Equals(sanitized, raw, StringComparison.Ordinal))
        {
            _syncingText = true;
            try
            {
                _editor.Text = sanitized;
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
        RequestVisualUpdate();
    }

    private void OnInnerFocusChanged(object? sender, FocusEventArgs e)
    {
        // Blur-validation + deferred visual refresh are handled by the shared base flow
        // (G9OutlinedFieldBase.HandleInnerFocusChanged), guarded by ShouldAutoValidate
        // so an externally-set error survives focus/blur.
        HandleInnerFocusChanged(e.IsFocused, () => _editor?.IsFocused == true);
    }

    /// <inheritdoc />
    protected override bool ShouldAutoValidate()
    {
        // Editors validate on blur only for self-validating input types or a Custom type
        // with a pattern. No rule → never auto-validate (preserves a consumer-set error).
        if (InputType is G9InputType.Email or G9InputType.Url) return true;
        if (InputType == G9InputType.Custom && !string.IsNullOrEmpty(ValidationPattern)) return true;
        return false;
    }

    /// <inheritdoc />
    protected override bool RunValidation() => Validate();

    /// <summary>
    ///     Run the input-type validation against the current <see cref="Text" /> and
    ///     surface the error via <see cref="G9OutlinedFieldBase.ErrorText" /> /
    ///     <see cref="G9OutlinedFieldBase.HasError" />. Returns true when the value is
    ///     valid (or empty).
    /// </summary>
    public bool Validate()
    {
        var message = G9InputTypePolicy.Validate(InputType, Text, ValidationPattern, ValidationErrorText);

        if (!string.IsNullOrWhiteSpace(message))
        {
            ErrorText = message;
            HasError = true;
            return false;
        }

        HasError = false;
        return true;
    }

    private void OnEditorPropertyChanged() { ApplyEditorProperties(); RequestVisualUpdate(); }
    private void OnVoiceEnabledChanged() => RequestVisualUpdate();

    private void OnVoiceCultureChanged()
    {
        // Only touch the session if one was ever created — reading the property would build it.
        if (_voice is not null)
        {
            _voice.Culture = VoiceCulture;
        }
    }

    private void OnInputTypeChanged()
    {
        var resolvedKeyboard = G9InputTypePolicy.ResolveKeyboard(InputType);
        if (KeyboardType != resolvedKeyboard) KeyboardType = resolvedKeyboard;
        ApplyEditorProperties();
        RequestVisualUpdate();

        if (!string.IsNullOrEmpty(Text))
        {
            var sanitized = G9InputTypePolicy.SanitizeText(InputType, Text!, AllowedCharsPattern);
            if (!string.Equals(sanitized, Text, StringComparison.Ordinal))
            {
                Text = sanitized;
            }
        }
    }

    private void ApplyEditorProperties()
    {
        if (_editor is null) return;

        var palette = G9Palette.Current;

        // Defensive equality checks — see G9TextEntry.ApplyEntryProperties for the full
        // rationale. WinUI focus events crash AOT (ExecutionEngineException) when
        // platform RichEditBox properties are re-written during the dispatch.
        var targetIsReadOnly = IsReadOnly;
        if (_editor.IsReadOnly != targetIsReadOnly) _editor.IsReadOnly = targetIsReadOnly;

        var targetMaxLength = MaxLength <= 0 ? int.MaxValue : MaxLength;
        if (_editor.MaxLength != targetMaxLength) _editor.MaxLength = targetMaxLength;

        if (_editor.AutoSize != AutoSize) _editor.AutoSize = AutoSize;

        if (Math.Abs(_editor.MinimumHeightRequest - MinimumEditorHeight) > 0.5)
        {
            _editor.MinimumHeightRequest = MinimumEditorHeight;
        }

        var targetEditorHeight = AutoSize == EditorAutoSizeOption.Disabled ? MinimumEditorHeight : -1;
        if (Math.Abs(_editor.HeightRequest - targetEditorHeight) > 0.5)
        {
            _editor.HeightRequest = targetEditorHeight;
        }

        if (_editor.IsSpellCheckEnabled != IsSpellCheckEnabled) _editor.IsSpellCheckEnabled = IsSpellCheckEnabled;
        if (_editor.IsTextPredictionEnabled != IsTextPredictionEnabled) _editor.IsTextPredictionEnabled = IsTextPredictionEnabled;

        var targetKeyboard = G9Visuals.ResolveKeyboard(G9InputTypePolicy.ResolveKeyboard(InputType));
        if (!ReferenceEquals(_editor.Keyboard, targetKeyboard)) _editor.Keyboard = targetKeyboard;

        var targetFont = !string.IsNullOrWhiteSpace(CustomFont)
            ? CustomFont
            : G9Visuals.ResolveCulturalFont();
        if (!string.Equals(_editor.FontFamily, targetFont, StringComparison.Ordinal)) _editor.FontFamily = targetFont;

        var targetTextColor = IsEnabled ? palette.TextPrimary : palette.TextDisabled;
        if (_editor.TextColor != targetTextColor) _editor.TextColor = targetTextColor;

        var targetFlow = InputTextDirection switch
        {
            G9TextInputDirection.LeftToRight => FlowDirection.LeftToRight,
            G9TextInputDirection.RightToLeft => FlowDirection.RightToLeft,
            // MatchParent: numeric / email / URL / phone editors stay LTR even in an RTL
            // page — see <see cref="G9TextEntry.ResolveInputFlowDirection" /> for the
            // same rationale (the entered value is universally written LTR; mirroring
            // it for RTL UI corrupts the visible string).
            _ when G9InputTypePolicy.PrefersLeftToRight(InputType) => FlowDirection.LeftToRight,
            _ => G9Visuals.IsRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight
        };
        if (_editor.FlowDirection != targetFlow) _editor.FlowDirection = targetFlow;

        if (Math.Abs(Box.MinimumHeightRequest - MinimumEditorHeight) > 0.5)
        {
            Box.MinimumHeightRequest = MinimumEditorHeight;
        }
    }

    protected override void OnRefresh()
    {
        RefreshVoiceIcon();

        if (_editor is null) return;

        if (!_syncingText)
        {
            _syncingText = true;
            try
            {
                var target = Text ?? string.Empty;
                if (_editor.Text != target)
                {
                    _editor.Text = target;
                }
            }
            finally
            {
                _syncingText = false;
            }
        }

        ApplyEditorProperties();
    }

    /// <summary>
    ///     Runs after the base has refreshed the trailing icon colour — which, because the
    ///     signature is deliberately constant, overwrites the microphone's accent with the field's
    ///     per-state colour. Re-assert it here and swap the glyph in place.
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
}
