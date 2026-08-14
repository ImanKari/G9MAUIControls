using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;

namespace G9MAUIControls.Controls;

/// <summary>
///     Outlined multi-line editor. Inherits the shared outline + notched-label architecture
///     from <see cref="G9OutlinedFieldBase" />. Differs from <see cref="G9TextEntry" /> in
///     that the notch / floating label sits above the multi-line content and the box height
///     scales with <see cref="MinimumEditorHeight" /> / <see cref="AutoSize" />.
///     // TODO (palette step): outline / focus-ring colors are inherited from the base.
/// </summary>
public partial class G9Editor : G9OutlinedFieldBase
{
    private readonly Editor _editor;
    private bool _syncingText;

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
    }

    public Editor InnerEditor => _editor;

    /// <summary>Editors keep a comfortable top/bottom inner padding so the floating label
    /// notch never overlaps the first line of text.</summary>
    protected override Thickness InnerContentPadding => new(0, 12, 0, 8);

    protected override View BuildInnerContent() => _editor;

    /// <summary>The platform-focusable inner element for the wrapper-level tap-to-focus.</summary>
    protected override VisualElement? FocusTarget => _editor;

    protected override bool IsContentFocused => _editor?.IsFocused == true;
    protected override bool HasContentValue => !string.IsNullOrEmpty(Text);
    protected override int GetTextLength() => Text?.Length ?? 0;

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
}
