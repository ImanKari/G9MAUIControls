using G9MAUIControls.Icons;
using G9MAUIControls.Theming;
using Maui.BindableProperty.Generator.Core;
using System.Windows.Input;

namespace G9MAUIControls.Controls;

/// <summary>
///     Shared base for every outlined-field control (text entry, editor, picker, combo box,
///     date/time picker). Owns:
///       - The outline rendering (a custom <see cref="GraphicsView" /> that draws a rounded
///         stroke with an optional notch where the floating label sits — no platform border,
///         no parent-matching label background).
///       - The floating label (animated TranslationY + Scale; 100% transparent in both rest
///         and floated states because the notch in the outline replaces the label backdrop).
///       - The leading and trailing icon slots, and the convention that any inner content
///         lives between them respecting the icon padding.
///       - The helper / error / counter footer row.
///     Subclasses provide their own inner content (Entry, Editor, value Label) by overriding
///     <see cref="BuildInnerContent" /> and update visuals via <see cref="OnRefresh" />.
///     // TODO (palette step): every state color in this base will move to G9Palette.
/// </summary>
public abstract partial class G9OutlinedFieldBase : G9ControlBase
{
    private readonly VerticalStackLayout _root;
    private readonly Grid _box;
    private readonly GraphicsView _outlineView;
    private readonly G9OutlinedFieldDrawable _outline = new();
    private readonly Grid _innerRow;
    private readonly Grid _leadingHost;
    private readonly ContentView _innerContentHost;
    private readonly Grid _trailingHost;
    private readonly Label _floatingLabel;
    private readonly Label _helperLabel;
    private readonly Label _counterLabel;
    /// <summary>
    ///     Row under the box holding the helper / error text and the character counter. It is
    ///     COLLAPSED (<c>IsVisible = false</c>) whenever both are empty — an empty-but-visible
    ///     footer still costs the root stack's 4dp spacing, which makes the control measure 4dp
    ///     taller than its box. In a row that centers a field next to fixed-height buttons (the
    ///     search + sort/filter header) that phantom 4dp pushed the field's box 2dp ABOVE the
    ///     buttons' centre line. Toggled in <see cref="OnApplyVisuals" />.
    /// </summary>
    private readonly Grid _footer;
    private readonly TapGestureRecognizer _trailingTap;
    private readonly TapGestureRecognizer _leadingTap;
    /// <summary>
    ///     Per-icon-host ripple drawable + its hosting <see cref="GraphicsView" />. The
    ///     drawable is mutated per animation frame (radius progress + tap origin) and the
    ///     view is invalidated each frame to repaint. The view sits at host-children index
    ///     0 (deepest) so the icon glyph paints over it; opacity is 0 at rest and ramps
    ///     up + decays through the animation. Stable instances — never recreated.
    /// </summary>
    private readonly G9RippleDrawable _leadingRippleDrawable = new();
    private readonly G9RippleDrawable _trailingRippleDrawable = new();
    private readonly GraphicsView _leadingRipple;
    private readonly GraphicsView _trailingRipple;
    private bool _wasFloated;
    private bool _isFirstApply = true;
    private double _measuredLabelWidth;
    private double _lastRestX = double.NaN;
    private double _lastRestY = double.NaN;
    /// <summary>
    ///     Cached floated TranslationX from the previous visual pass. Tracked separately so
    ///     a culture flip (LTR ↔ RTL) — which only changes the SIGN of the floated offset
    ///     and the label's <c>VisualElement.HorizontalOptions</c> anchor, but
    ///     leaves the rest position and the floated state unchanged — still triggers a
    ///     fresh transform write. Without this, fields without a leading icon kept the
    ///     stale TranslationX from the previous direction and the floated label drifted
    ///     to the wrong side of the box until the user typed (which forced a state
    ///     transition that re-ran the apply).
    /// </summary>
    private double _lastFloatedX = double.NaN;
    private bool _lastIsRtl;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _label;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _placeholder;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _helperText;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _errorText;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _hasError;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _alwaysFloat;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _isReadOnly;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private int _maxLength;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _showCharacterCounter;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _statusColor;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _useStatusColor;

    /// <summary>
    ///     When <c>true</c> the field paints the soft inner focus halo — the second, glowing
    ///     stroke drawn on the inside of the outline while the field is focused (the "inner
    ///     second border / shadow glow").
    ///     <para>
    ///         Default <c>false</c> for EVERY outlined field (text entry, editor, picker, combo
    ///         box, date/time picker, barcode entry) because they all derive from this base. A
    ///         focused field is still clearly indicated by the thicker primary-coloured emphasis
    ///         stroke on the outline itself — this flag only controls the extra inner glow on top
    ///         of it. Opt a specific field back in with <c>ShowFocusHalo="True"</c>. The halo is
    ///         never painted in the error / status-colour states regardless of this flag.
    ///     </para>
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _showFocusHalo;

    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _leadingEmoji;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9IconSource? _leadingIcon;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _leadingImagePath;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private ImageSource? _leadingImageSource;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _trailingEmoji;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9IconSource? _trailingIcon;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _trailingImagePath;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private ImageSource? _trailingImageSource;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _isTrailingBusy;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _forceTrailingIconRight;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _forceLeadingIconLeft;
    /// <summary>
    ///     Optional override for the box height in logical pixels. Default <c>0</c> means
    ///     "use <see cref="G9Metrics.ControlHeight" />". Useful for compact / dense
    ///     layouts that want a smaller field, and for proving the layout still works at
    ///     non-default heights.
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnFieldHeightChanged))] private double _fieldHeight;

    /// <summary>
    ///     When <c>true</c> the field reserves vertical space at its TOP (as padding on the field
    ///     root) for the floating label's overhang, so the floated label renders WITHIN the field's
    ///     own bounds instead of spilling ~<see cref="G9Metrics.FloatingLabelClearance" />dp
    ///     above the box and being covered / clipped by whatever sits directly on top of the field.
    ///     <para>
    ///         Use this when the field's TOP butts directly against another element — a bottom-sheet
    ///         header, a card edge, the very top of a scroll with no padding. It is OFF by default: a
    ///         field placed in a form / with a top gap floats its label into the empty space above,
    ///         so reserving clearance there would only add dead space (and, in a height-matched lane
    ///         next to buttons, would push the field taller and break the shared centre line — turn
    ///         it OFF explicitly there). <c>G9SearchEntry</c> turns this ON by default because a
    ///         search box almost always sits at the top of a list / directly under a header.
    ///     </para>
    /// </summary>
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _reserveFloatingLabelClearance;

    [AutoBindable] private ICommand? _leadingCommand;
    [AutoBindable] private ICommand? _trailingCommand;
    [AutoBindable] private object? _commandParameter;

    // Icon rebuild caches — we only swap the leading/trailing host content when the
    // parameters that drive the icon visuals actually change. Repeatedly creating
    // G9IconView / Image / ActivityIndicator views during focus events on WinUI is one of
    // the known triggers for visual-tree re-entry, so we deduplicate aggressively.
    private string? _leadingSignature;
    private string? _trailingSignature;

    /// <summary>
    ///     Long-lived <see cref="G9IconView" /> reused across every
    ///     <see cref="G9IconSource" />-driven transition on the trailing slot
    ///     (<c>QrCode</c> ↔ <c>CheckCircle</c> ↔ <c>Info</c>, password show ↔ hide,
    ///     etc.). Lazily added to <see cref="_trailingHost" /> on first use, then kept
    ///     alive and toggled <see cref="VisualElement.IsVisible" /> when the signature
    ///     flips between Material-icon and spinner states. Mutating
    ///     <see cref="G9IconView.Icon" /> /
    ///     <see cref="G9IconView.Color" /> on the cached instance keeps
    ///     the platform handler attached, so the embedded font's already-rasterised
    ///     glyph stays on screen — no 1-frame tofu rectangle while the typeface mapper
    ///     loads the new code-point. Documented in <c>G9Controls.md</c> principle 12.
    /// </summary>
    private G9IconView? _trailingDefaultMauiIcon;

    /// <summary>
    ///     Long-lived spinner shown when <see cref="IsTrailingBusy" /> is true. Same
    ///     reason as <see cref="_trailingDefaultMauiIcon" /> — keeping the
    ///     <see cref="ActivityIndicator" /> in the visual tree and toggling visibility
    ///     instead of detach / re-attach avoids a handler tear-down + rebuild on every
    ///     busy-state flip. Without this, the moment the busy state flips off, the
    ///     ActivityIndicator is removed and the trailing material icon is freshly
    ///     attached — the new G9IconView needs one frame to load the glyph from the
    ///     embedded font, which the user sees as a tofu rectangle.
    /// </summary>
    private ActivityIndicator? _trailingBusyIndicator;

    /// <summary>
    ///     Mirror of <see cref="_trailingDefaultMauiIcon" /> for the leading slot.
    /// </summary>
    private G9IconView? _leadingDefaultMauiIcon;

    protected G9OutlinedFieldBase()
    {
        _outlineView = new GraphicsView
        {
            Drawable = _outline,
            BackgroundColor = Colors.Transparent,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };

        _leadingHost = new Grid
        {
            // Single-cell Grid acts as the icon host. Grid.Padding is reliably honoured
            // by MAUI's measurement on every platform (ContentView.Padding has known
            // edge cases when the child carries an explicit WidthRequest). The host
            // auto-sizes to (2 × icon margin + glyph) via the parent Grid's Auto column.
            VerticalOptions = LayoutOptions.Center,
            IsClippedToBounds = false,
            IsVisible = false
        };
        _trailingHost = new Grid
        {
            VerticalOptions = LayoutOptions.Center,
            IsClippedToBounds = false,
            IsVisible = false
        };

        // Ripple overlay layer per icon host. The drawable paints a circle expanding
        // from the tap point and fading out — clipped to the host's pill shape so the
        // ripple looks like it lives inside the icon's hit area, not bleeding into
        // the field's text. The GraphicsView is index 0 in the host's children so it
        // sits BENEATH the icon glyph; the icon paints on top while the ripple animates.
        _leadingRipple = BuildRippleLayer(_leadingRippleDrawable);
        _leadingHost.Children.Add(_leadingRipple);
        _trailingRipple = BuildRippleLayer(_trailingRippleDrawable);
        _trailingHost.Children.Add(_trailingRipple);

        _leadingTap = new TapGestureRecognizer();
        _leadingTap.Tapped += OnLeadingTapped;
        _leadingHost.GestureRecognizers.Add(_leadingTap);

        _trailingTap = new TapGestureRecognizer();
        _trailingTap.Tapped += OnTrailingTapped;
        _trailingHost.GestureRecognizers.Add(_trailingTap);

        _innerContentHost = new ContentView
        {
            VerticalOptions = LayoutOptions.Fill,
            HorizontalOptions = LayoutOptions.Fill,
            BackgroundColor = Colors.Transparent
        };

        _innerRow = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star),
                new ColumnDefinition(GridLength.Auto)
            },
            ColumnSpacing = 0
        };
        _innerRow.Add(_leadingHost, 0);
        _innerRow.Add(_innerContentHost, 1);
        _innerRow.Add(_trailingHost, 2);

        _floatingLabel = new Label
        {
            FontSize = G9Metrics.FloatingLabelFontSizeRest,
            // Zero internal padding — the label's bounding box (which the notch is sized to)
            // must equal the visible text width, otherwise the text appears off-centre
            // inside the notch. Breathing room between the text and the notch edges is
            // added via G9Metrics.OutlineNotchTextGap when the notch is computed.
            Padding = new Thickness(0),
            Margin = new Thickness(0),
            BackgroundColor = Colors.Transparent,
            LineBreakMode = LineBreakMode.TailTruncation,
            MaxLines = 1,
            VerticalTextAlignment = TextAlignment.Center,
            HorizontalTextAlignment = TextAlignment.Start,
            VerticalOptions = LayoutOptions.Start,
            HorizontalOptions = LayoutOptions.Start,
            InputTransparent = true,
            HeightRequest = G9Metrics.FloatingLabelHeight,
            AnchorX = 0,
            AnchorY = 0.5
        };
        _floatingLabel.SizeChanged += (_, _) =>
        {
            if (_floatingLabel.Width > 0 && Math.Abs(_floatingLabel.Width - _measuredLabelWidth) > 0.5)
            {
                _measuredLabelWidth = _floatingLabel.Width;
                // Notch width depends on the measured label width, but we must NOT trigger a
                // full visual pass here — that would replay the floating-label animation
                // every time the label remeasures (Bold ↔ Regular flip changes the width).
                // Instead, re-paint just the outline to update the notch geometry.
                UpdateOutlineNotchOnly();
            }
        };

        _box = new Grid
        {
            HeightRequest = G9Metrics.ControlHeight,
            FlowDirection = FlowDirection.LeftToRight
        };
        _box.Children.Add(_outlineView);
        _box.Children.Add(_innerRow);
        _box.Children.Add(_floatingLabel);
        _box.SizeChanged += (_, _) =>
        {
            // The rest-state Y depends on the box height. When the consumer sets a custom
            // FieldHeight, the box re-measures and we need to reposition the rest label.
            // Mark the cached rest values as stale so the next visual pass re-runs the
            // anchor math without re-running the float animation.
            _lastRestY = double.NaN;
            RequestVisualUpdate();
        };

        // Wrapper-level tap → focus the inner element. Solves the issue where tapping on
        // the floating label or the outline edges did not focus the inner Entry / Editor:
        // even with InputTransparent=true on the label, the platform hit-test stops at
        // the topmost child in a Grid. Adding a tap recognizer on the box itself
        // guarantees a focus call no matter where inside the box the user taps. Subclasses
        // expose their focusable inner element through <see cref="FocusTarget"/>.
        // The icon hosts have their own gestures attached, so this wrapper recognizer
        // does not interfere with the leading / trailing icon taps.
        var boxTap = new TapGestureRecognizer();
        boxTap.Tapped += OnBoxTapped;
        _box.GestureRecognizers.Add(boxTap);

        _helperLabel = new Label
        {
            FontSize = G9Metrics.HelperFontSize,
            LineBreakMode = LineBreakMode.WordWrap,
            MaxLines = 2,
            IsVisible = false
        };
        _counterLabel = new Label
        {
            FontSize = G9Metrics.CounterFontSize,
            HorizontalTextAlignment = TextAlignment.End,
            IsVisible = false
        };

        _footer = new Grid
        {
            ColumnDefinitions = { new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto) },
            IsVisible = false
        };
        _footer.Add(_helperLabel, 0);
        _footer.Add(_counterLabel, 1);

        _root = new VerticalStackLayout
        {
            Spacing = 4,
            Children = { _box, _footer }
        };

        Content = _root;
        MaxLength = -1;
    }

    /// <summary>The host grid that lays out the leading icon, inner content, and trailing icon.</summary>
    protected Grid Box => _box;

    /// <summary>The cell that subclasses fill with their inner content (Entry / Editor / value Label).</summary>
    protected ContentView InnerContentHost => _innerContentHost;

    protected Grid LeadingHost => _leadingHost;
    protected Grid TrailingHost => _trailingHost;
    protected Label HelperLabel => _helperLabel;
    protected Label CounterLabel => _counterLabel;
    protected Label FloatingLabel => _floatingLabel;

    /// <summary>
    ///     The platform-focusable inner element. Subclasses with a focusable inner control
    ///     (e.g. G9TextEntry's <c>Entry</c>, G9Editor's <c>Editor</c>) override this to
    ///     return that control. The base class registers a wrapper-level tap recognizer
    ///     that focuses the returned element so taps on the floating-label, the outline,
    ///     or anywhere in the box reliably bring the keyboard up — not only taps that land
    ///     inside the inner control's exact hit-test slot.
    ///     <para>
    ///         Subclasses that open a sheet on tap (G9Picker, G9ComboBox, G9DateTimePicker)
    ///     leave this null and attach their own gesture; the base does not interfere.
    ///     </para>
    /// </summary>
    protected virtual VisualElement? FocusTarget => null;

    /// <summary>
    ///     Inner padding applied to <see cref="InnerContentHost" /> (left, right). Subclasses
    ///     can override to widen the text padding (e.g. multiline editors that want extra
    ///     vertical breathing room).
    /// </summary>
    protected virtual Thickness InnerContentPadding => new(0);

    /// <summary>
    ///     When true the floating label is forced into its floated position regardless of the
    ///     inner content's focus / value state. Used by controls that are "always floated"
    ///     when they have a value (pickers, combos, date pickers).
    /// </summary>
    protected virtual bool IsValueFloated => false;

    /// <summary>True when the inner content has focus. Used to decide between rest / floated states.</summary>
    protected virtual bool IsContentFocused => false;

    /// <summary>True when the inner content holds a non-empty value (used by G9TextEntry / G9Editor).</summary>
    protected virtual bool HasContentValue => false;

    /// <summary>Build / push the inner content into <see cref="InnerContentHost" />.</summary>
    protected abstract View BuildInnerContent();

    /// <summary>Subclass hook called after the base finishes pushing colors and label state.</summary>
    protected virtual void OnRefresh() { }

    /// <summary>Resolve the trailing tap gesture. Default implementation runs <see cref="TrailingCommand" />.</summary>
    protected virtual void OnTrailingTap()
    {
        ExecuteCommand(TrailingCommand, CommandParameter);
    }

    private void OnLeadingTapped(object? sender, TappedEventArgs e)
    {
        // Only run press feedback when the tap is actionable. Without this, a passive
        // decorative leading icon (e.g. the wheat 🌾 on Farm Name with no command wired)
        // would still ripple on every focus tap, suggesting interactivity that doesn't
        // exist.
        if (!HasLeadingActionable()) return;

        var origin = ResolveTapOrigin(_leadingHost, e);
        PlayIconPress(_leadingHost, _leadingRipple, _leadingRippleDrawable, origin);
        ExecuteCommand(LeadingCommand, CommandParameter);
    }

    private void OnTrailingTapped(object? sender, TappedEventArgs e)
    {
        if (!HasTrailingActionable()) return;

        var origin = ResolveTapOrigin(_trailingHost, e);
        PlayIconPress(_trailingHost, _trailingRipple, _trailingRippleDrawable, origin);
        OnTrailingTap();
    }

    /// <summary>
    ///     True when the leading icon has a wired command. Decorative icons (no command)
    ///     skip the press animation so the user sees no false-positive interactivity hint.
    /// </summary>
    protected virtual bool HasLeadingActionable() => LeadingCommand is not null;

    /// <summary>
    ///     True when the trailing icon will do something on tap — either an external
    ///     <see cref="TrailingCommand" /> or a built-in subclass affordance (password
    ///     toggle, clear button, scanner state, etc.) reported via
    ///     <see cref="HasExtraTrailingAffordance" />.
    /// </summary>
    protected virtual bool HasTrailingActionable() => TrailingCommand is not null || HasExtraTrailingAffordance();

    /// <summary>
    ///     Maps the tap point reported by the platform into a normalized 0..1 origin for
    ///     the ripple. Falls back to the host's geometric centre when the platform doesn't
    ///     surface a tap position (some Android emulator scenarios, programmatic Tapped
    ///     invocations) so the ripple still plays — just radial from the centre.
    /// </summary>
    private static PointF ResolveTapOrigin(Grid host, TappedEventArgs e)
    {
        var p = e.GetPosition(host);
        if (p.HasValue && host.Width > 0 && host.Height > 0)
        {
            return new PointF((float)(p.Value.X / host.Width), (float)(p.Value.Y / host.Height));
        }
        return new PointF(0.5f, 0.5f);
    }

    /// <summary>
    ///     Wrapper-level tap handler. Resolves the subclass's focus target and gives it
    ///     focus, so taps anywhere in the box (including over the floating label, the
    ///     outline edges, the empty padding, etc.) reliably activate the inner Entry /
    ///     Editor and bring up the keyboard.
    ///     <para>
    ///         Subclasses that open a sheet on tap (G9Picker, G9ComboBox,
    ///     G9DateTimePicker) leave <see cref="FocusTarget" /> null; this handler exits
    ///     immediately and the subclass's own gesture recognizer (attached to the wrapper
    ///     ContentView) opens the sheet.
    ///     </para>
    /// </summary>
    private void OnBoxTapped(object? sender, TappedEventArgs e)
    {
        var target = FocusTarget;
        if (target is null) return;
        if (!IsEnabled || IsReadOnly) return;
        if (target.IsFocused) return;

        try { target.Focus(); }
        catch { /* platform may not be ready (e.g. during first layout) */ }
    }

    protected static void ExecuteCommand(ICommand? command, object? param)
    {
        if (command?.CanExecute(param) == true) command.Execute(param);
    }

    // ── Shared inner-content focus / blur-validation flow ────────────────────────
    // Every outlined field with a focusable inner control (G9TextEntry's Entry,
    // G9Editor's Editor, …) shares the same focus-change rules, so they live here
    // once instead of being copy-pasted into each subclass:
    //
    //   1. On blur, run validation ONLY when the field actually has a validation rule
    //      (ShouldAutoValidate). Without that guard, a blur on a field whose error was
    //      set externally via HasError / ErrorText would call RunValidation(), find
    //      nothing wrong, and clear the consumer's error — which is exactly the
    //      "focus/unfocus wipes the error style + message" bug. The guard was added to
    //      G9TextEntry first; lifting it to the base fixes G9Editor (and any future
    //      input) for free.
    //   2. The blur validation is deferred to the next dispatcher tick so it runs after
    //      the platform finishes its own focus-changed dispatch (re-entering a WinUI
    //      TextBox / RichEditBox synchronously during its Unfocused event reliably
    //      crashes AOT with ExecutionEngineException).
    //   3. The visual refresh is likewise deferred so we never push platform property
    //      writes back into a control that is still draining its focus event.

    /// <summary>
    ///     True when this field has a validation rule that <see cref="RunValidation" />
    ///     can actually evaluate (an explicit validator, a self-validating input type,
    ///     etc.). Default <c>false</c> — a field with no rule never auto-validates on
    ///     blur, so an externally-driven <see cref="HasError" /> / <see cref="ErrorText" />
    ///     is preserved across focus changes. Subclasses override to opt in.
    /// </summary>
    protected virtual bool ShouldAutoValidate() => false;

    /// <summary>
    ///     Run the field's validation and surface the result via <see cref="HasError" /> /
    ///     <see cref="ErrorText" />. Default no-op returning <c>true</c> (valid). Only
    ///     invoked when <see cref="ShouldAutoValidate" /> is true. Subclasses with
    ///     validation override this (typically delegating to their public Validate()).
    /// </summary>
    protected virtual bool RunValidation() => true;

    /// <summary>
    ///     Shared handler subclasses call from their inner control's Focused / Unfocused
    ///     events. Centralises the blur-validation guard and the deferred visual refresh
    ///     so every input behaves identically. <paramref name="isFocused" /> is the new
    ///     focus state; <paramref name="isStillFocused" /> is re-checked after the
    ///     dispatch in case focus returned during the tick.
    /// </summary>
    protected void HandleInnerFocusChanged(bool isFocused, Func<bool> isStillFocused)
    {
        if (!isFocused && ShouldAutoValidate() && HasContentValue)
        {
            Dispatcher.Dispatch(() =>
            {
                if (isStillFocused()) return; // came back into focus during the dispatch
                RunValidation();
            });
        }

        Dispatcher.Dispatch(RequestVisualUpdate);
    }

    /// <summary>Convenience setter — pushes a child into <see cref="InnerContentHost" /> exactly once.</summary>
    protected void EnsureInnerContent()
    {
        if (_innerContentHost.Content is not null) return;

        var content = BuildInnerContent();
        if (content is null) return; // Subclass field not yet initialized.
        _innerContentHost.Content = content;
    }

    private void OnVisualChanged() => RequestVisualUpdate();

    /// <inheritdoc />
    /// <remarks>
    ///     Outlined fields have an expensive <see cref="OnApplyVisuals" /> — it
    ///     repositions the floating label, computes notch geometry, runs animations
    ///     when the floated state flips, and reconciles the leading / trailing icon
    ///     hosts. None of that is needed for a pure palette flip; only the outline
    ///     stroke colour, the floating-label tint, the helper / counter labels, and
    ///     the icon tints change. We push only those onto the cached children. With
    ///     30+ outlined entries on the showcase page this saves 500 ms+ on every
    ///     theme switch.
    /// </remarks>
    protected override void OnPaletteChanged()
    {
        if (Handler is null) return;
        if (_outlineView.Handler is null || _floatingLabel.Handler is null) return;

        var palette = G9Palette.Current;
        var stateColor = ResolveStateColor(palette);
        var restingContentColor = ResolveRestingContentColor(palette);
        var floated = AlwaysFloat || IsValueFloated || IsContentFocused || HasContentValue;
        var targetLabelColor = floated ? stateColor : restingContentColor;
        // Mirror the disabled-state dim from Refresh so a palette swap mid-disabled-state
        // (e.g. light↔dark theme toggle while a form is in its loading state) keeps the
        // floating label dimmed instead of snapping back to full opacity.
        if (!IsEnabled)
        {
            const double DisabledDimAlpha = 0.45;
            targetLabelColor = targetLabelColor
                .WithAlpha((float)(targetLabelColor.Alpha * DisabledDimAlpha));
        }

        // Floating label colour follows the state colour when floated, the muted
        // tertiary text colour at rest. FontAttributes / position aren't touched —
        // theme doesn't change them.
        if (_floatingLabel.TextColor != targetLabelColor)
        {
            _floatingLabel.TextColor = targetLabelColor;
        }

        // Outline chrome. We don't recompute the notch — that depends on label width,
        // which palette changes don't affect.
        ApplyOutlineChrome(stateColor);
        _outlineView.Invalidate();

        // Icon hosts: refresh tint without rebuilding the icon view.
        UpdateIconColor(_leadingHost, restingContentColor);
        UpdateIconColor(_trailingHost, stateColor);

        // Helper / counter / character-counter colours.
        if (_helperLabel.TextColor != (HasError ? palette.Error : palette.TextTertiary))
        {
            _helperLabel.TextColor = HasError ? palette.Error : palette.TextTertiary;
        }
        if (_counterLabel.TextColor != palette.TextTertiary)
        {
            _counterLabel.TextColor = palette.TextTertiary;
        }

        // Subclass hook so G9TextEntry / G9Editor can refresh their inner Entry's
        // TextColor without doing the full OnApplyVisuals.
        OnRefresh();
    }

    /// <inheritdoc />
    /// <remarks>
    ///     Culture flip (LTR ↔ RTL) needs three things refreshed in place:
    ///     <list type="bullet">
    ///         <item>the cultural typeface on the floating label, helper, counter
    ///             (so Persian glyphs render in the Persian face and Latin glyphs in
    ///             the Latin face);</item>
    ///         <item>the floating-label anchor side (start ↔ end) — handled by
    ///             <see cref="OnApplyVisuals" /> via the cached <c>_lastIsRtl</c>
    ///             trigger;</item>
    ///         <item>the helper-label margin side.</item>
    ///     </list>
    ///     We touch the labels' fonts here directly and defer the geometry work to
    ///     the coalesced <see cref="G9ControlBase.RequestVisualUpdate" /> from the
    ///     base — that keeps the per-keystroke cost for fields that are NOT being
    ///     culture-flipped to a single property write.
    /// </remarks>
    protected override void OnCultureChangedHook()
    {
        if (Handler is null) return;

        var font = G9Visuals.ResolveCulturalFont();
        if (!string.Equals(_floatingLabel.FontFamily, font, StringComparison.Ordinal))
        {
            _floatingLabel.FontFamily = font;
        }
        if (!string.Equals(_helperLabel.FontFamily, font, StringComparison.Ordinal))
        {
            _helperLabel.FontFamily = font;
        }
        if (!string.Equals(_counterLabel.FontFamily, font, StringComparison.Ordinal))
        {
            _counterLabel.FontFamily = font;
        }

        // Subclasses (G9TextEntry / G9Editor) push the cultural font onto their
        // inner Entry / Editor through their own ApplyEntryProperties /
        // ApplyEditorProperties paths in the next coalesced apply.
        RequestVisualUpdate();
    }

    protected override void OnApplyVisuals()
    {
        EnsureInnerContent();

        // If the subclass hasn't constructed its inner content yet (we're still inside the
        // base constructor running setter callbacks), skip the visual pass — the subclass
        // constructor will trigger another RequestVisualUpdate when its own fields are ready.
        if (_innerContentHost.Content is null)
        {
            return;
        }

        // Belt-and-braces guard against ObjectDisposedException during page teardown.
        // ApplyVisualsCore in the base already short-circuits when the handler is null,
        // but a queued dispatch may have started before the platform handler was nulled
        // out. If any of the visual children has lost its handler, bail out so we don't
        // write FontAttributes / TextColor / etc. into a dead mapper.
        if (Handler is null
            || _floatingLabel.Handler is null
            || _outlineView.Handler is null)
        {
            return;
        }

        // Optional top clearance (see ReserveFloatingLabelClearance): reserve the floated label's
        // overhang as padding on the field root so a field whose top butts against another element
        // (sheet header, card edge) contains its floated label instead of spilling it above and
        // being covered. Only the TOP is affected; left/right/bottom stay 0.
        var desiredRootPadding = new Thickness(
            0, ReserveFloatingLabelClearance ? G9Metrics.FloatingLabelClearance : 0, 0, 0);
        if (_root.Padding != desiredRootPadding)
        {
            _root.Padding = desiredRootPadding;
        }

        var palette = G9Palette.Current;
        var floated = AlwaysFloat || IsValueFloated || IsContentFocused || HasContentValue;
        var stateColor = ResolveStateColor(palette);
        var restingContentColor = ResolveRestingContentColor(palette);
        var slot = new G9FieldSlotLayout(
            HasLeadingIcon: HasLeadingIcon(),
            HasTrailingIcon: HasVisibleTrailingIcon(),
            IsRtl: G9Visuals.IsRtl,
            ForceTrailingIconRight: ForceTrailingIconRight,
            ForceLeadingIconLeft: ForceLeadingIconLeft);

        // Disabled-state dim. We DO NOT set Opacity on `this`: on Android, View.setAlpha(<1)
        // (which is what MAUI's Opacity translates to) forces the view to render through an
        // offscreen hardware alpha layer that is clipped to the view's own bounds. The
        // floating label sits with TranslationY = FloatingLabelFloatedY (-11dp) when floated,
        // so half of its height physically extends ABOVE the parent's bounds — exactly the
        // region Android's alpha layer clips. The visible symptom is the "half-rendered
        // label / outlined placeholder" the moment IsEnabled flips to false (e.g. the login
        // form going to its readonly/loading state on submit). Two-part fix:
        //   1. Keep the parent fully opaque (`Opacity = 1`) so the floating label is never
        //      sent through an offscreen layer that would clip it.
        //   2. Dim every child of `_box` EXCEPT the floating label individually
        //      (`_outlineView`, `_innerRow`) plus the helper / counter labels below the
        //      box. The floating label is dimmed through its TextColor alpha further down
        //      (see the `targetLabelColor` write), so visually the field still reads as
        //      disabled while the label keeps its full bounds.
        // Do NOT collapse this back to a single `Opacity = ...` write on `this` — the
        // clipping bug WILL come back, and this exact rule is shared by every G9
        // outlined field (G9TextEntry, G9Editor, G9Picker, G9ComboBox,
        // G9DateTimePicker) because they all derive from this base.
        const double DisabledDimAlpha = 0.45;
        var dimAlpha = IsEnabled ? 1.0 : DisabledDimAlpha;
        if (Math.Abs(Opacity - 1.0) > 0.001)
            Opacity = 1.0;
        if (Math.Abs(_outlineView.Opacity - dimAlpha) > 0.001)
            _outlineView.Opacity = dimAlpha;
        if (Math.Abs(_innerRow.Opacity - dimAlpha) > 0.001)
            _innerRow.Opacity = dimAlpha;
        if (Math.Abs(_helperLabel.Opacity - dimAlpha) > 0.001)
            _helperLabel.Opacity = dimAlpha;
        if (Math.Abs(_counterLabel.Opacity - dimAlpha) > 0.001)
            _counterLabel.Opacity = dimAlpha;

        // Inner content padding so glyphs never collide with the icon slots — pulled from
        // the shared G9FieldSlotLayout helper so every outlined field uses the same gap.
        var resolvedInnerPad = slot.ResolveInnerPadding(InnerContentPadding);
        if (_innerContentHost.Padding != resolvedInnerPad)
            _innerContentHost.Padding = resolvedInnerPad;

        // Floating label content + colors. The label is always transparent; the outline draws
        // a notch around it so the parent background shows through automatically.
        _floatingLabel.Text = Label ?? Placeholder ?? string.Empty;
        _floatingLabel.IsVisible = !string.IsNullOrWhiteSpace(_floatingLabel.Text);

        // Cultural typeface — Persian face for RTL, Latin for LTR. Without an
        // explicit FontFamily MAUI's platform fallback drops to the system
        // sans-serif on Persian glyphs, which renders the floating label in a face
        // that mismatches the rest of the UI (which goes through the app's
        // <c>CulturalFont</c> resource). Setting it here keeps every label
        // consistent regardless of platform.
        var culturalFont = G9Visuals.ResolveCulturalFont();
        if (!string.Equals(_floatingLabel.FontFamily, culturalFont, StringComparison.Ordinal))
        {
            _floatingLabel.FontFamily = culturalFont;
        }
        // FontAttributes and TextColor are now managed by the animation helper to avoid
        // the Bold→None width jump during the unfocus slide. We compute the targets here
        // and pass them to AnimateFloatingLabel.
        var targetFontAttrs = floated ? FontAttributes.Bold : FontAttributes.None;
        var targetLabelColor = floated ? stateColor : restingContentColor;
        // Disabled-state dim for the floating label: the parent's Opacity is intentionally
        // pinned at 1.0 (see the per-child dim block above) so the label's alpha must be
        // dimmed at the COLOR level, not by setting the parent's Opacity. This keeps the
        // visual disabled cue while preserving the floating label's full rendering bounds.
        if (!IsEnabled)
        {
            targetLabelColor = targetLabelColor
                .WithAlpha((float)(targetLabelColor.Alpha * DisabledDimAlpha));
        }

        // Anchor the label at the corner padding for both LTR and RTL. Margin pins the
        // label edge; TranslationX (animated) slides it over the leading icon column when
        // a leading icon is present at rest, and back to 0 when floated.
        // No padding compensation needed — the label's internal Padding is 0, so its
        // bounding box equals the visible text width and Margin alone places that edge.
        var cornerInset = G9Metrics.InputHorizontalPadding;

        if (slot.IsRtl)
        {
            if (_floatingLabel.HorizontalOptions != LayoutOptions.End)
                _floatingLabel.HorizontalOptions = LayoutOptions.End;
            if (_floatingLabel.HorizontalTextAlignment != TextAlignment.End)
                _floatingLabel.HorizontalTextAlignment = TextAlignment.End;
            if (_floatingLabel.AnchorX != 1)
                _floatingLabel.AnchorX = 1;
        }
        else
        {
            if (_floatingLabel.HorizontalOptions != LayoutOptions.Start)
                _floatingLabel.HorizontalOptions = LayoutOptions.Start;
            if (_floatingLabel.HorizontalTextAlignment != TextAlignment.Start)
                _floatingLabel.HorizontalTextAlignment = TextAlignment.Start;
            if (_floatingLabel.AnchorX != 0)
                _floatingLabel.AnchorX = 0;
        }
        var labelMargin = new Thickness(cornerInset, 0, cornerInset, 0);
        if (_floatingLabel.Margin != labelMargin)
            _floatingLabel.Margin = labelMargin;

        var fieldHeight = ResolveFieldHeight();
        var restY = (fieldHeight - G9Metrics.FloatingLabelHeight) / 2.0;
        var restX = slot.ResolveLabelRestTranslationX();
        var floatedX = slot.ResolveLabelFloatedTranslationX();

        // Only kick a new animation when the floated state actually transitions, or when
        // any of the target positions / direction moved. Re-running the animation on every
        // visual pass would replay it endlessly because every pass triggers a label
        // remeasure (Bold ↔ Regular flip changes width), which fires SizeChanged and in
        // turn another visual pass.
        // We track floatedX AND the RTL flag in the gate explicitly so a culture toggle
        // (LTR ↔ RTL) — which flips the sign of floatedX and the label's anchor side
        // without changing the floated state — still triggers a transform write. Without
        // this, fields without a leading icon kept their stale TranslationX after RTL
        // flip and the floated label drifted off-position.
        var restMoved = double.IsNaN(_lastRestX) || double.IsNaN(_lastRestY)
                        || Math.Abs(_lastRestX - restX) > 0.5
                        || Math.Abs(_lastRestY - restY) > 0.5;
        var floatedMoved = double.IsNaN(_lastFloatedX) || Math.Abs(_lastFloatedX - floatedX) > 0.5;
        var directionFlipped = _lastIsRtl != slot.IsRtl;
        var stateChanged = _wasFloated != floated || restMoved || floatedMoved || directionFlipped;
        if (stateChanged || _isFirstApply)
        {
            // Animate only when the floated state itself toggled. Pure rest-position moves
            // (icon added/removed, direction flipped, sample-data refreshed) snap
            // immediately to avoid a visible slide while the user is just looking at the
            // form.
            var animate = !_isFirstApply && _wasFloated != floated;
            G9OutlinedFieldVisual.AnimateFloatingLabel(this, _floatingLabel, floated, restY, restX, floatedX, animate, targetFontAttrs, targetLabelColor);
            _wasFloated = floated;
            _lastRestX = restX;
            _lastRestY = restY;
            _lastFloatedX = floatedX;
            _lastIsRtl = slot.IsRtl;
        }
        else
        {
            // No animation needed, but ensure font/color stay correct (e.g. theme changed
            // while the field was already in a steady state).
            if (_floatingLabel.FontAttributes != targetFontAttrs)
                _floatingLabel.FontAttributes = targetFontAttrs;
            if (_floatingLabel.TextColor != targetLabelColor)
                _floatingLabel.TextColor = targetLabelColor;
        }

        _isFirstApply = false;

        // Floated label sits at corner + extra X; notch must match that visual position
        // (otherwise the cut-out misses the label by a few px and the design looks off).
        UpdateOutline(stateColor, floated, G9Metrics.InputHorizontalPadding + Math.Abs(floatedX));

        // Helper / counter footer.
        _helperLabel.Text = HasError ? (ErrorText ?? string.Empty) : (HelperText ?? string.Empty);
        _helperLabel.TextColor = HasError ? palette.Error : palette.TextTertiary;
        if (!string.Equals(_helperLabel.FontFamily, culturalFont, StringComparison.Ordinal))
        {
            _helperLabel.FontFamily = culturalFont;
        }
        var helperMargin = G9Visuals.IsRtl ? new Thickness(0, 0, 14, 0) : new Thickness(14, 0, 0, 0);
        if (_helperLabel.Margin != helperMargin)
            _helperLabel.Margin = helperMargin;
        _helperLabel.IsVisible = !string.IsNullOrWhiteSpace(_helperLabel.Text);

        _counterLabel.IsVisible = ShowCharacterCounter && MaxLength > 0;
        _counterLabel.Text = MaxLength > 0 ? $"{(GetTextLength()):0} / {MaxLength}" : string.Empty;
        _counterLabel.TextColor = palette.TextTertiary;
        if (!string.Equals(_counterLabel.FontFamily, culturalFont, StringComparison.Ordinal))
        {
            _counterLabel.FontFamily = culturalFont;
        }

        // Collapse the footer row when it carries nothing: an empty-but-visible footer still
        // contributes the root stack's 4dp spacing, so the field would measure 4dp taller than its
        // box and sit off-centre against fixed-height neighbours (see the _footer field remark).
        var footerVisible = _helperLabel.IsVisible || _counterLabel.IsVisible;
        if (_footer.IsVisible != footerVisible)
        {
            _footer.IsVisible = footerVisible;
        }

        RebuildIcons(stateColor, slot);
        OnRefresh();
    }

    /// <summary>
    ///     Resolve the box height used to compute the rest-state label Y. Priority order:
    ///     <list type="number">
    ///         <item><description><see cref="FieldHeight" /> when explicitly set (> 0).</description></item>
    ///         <item><description>Measured <see cref="VisualElement.Height" /> on the box once layout completes.</description></item>
    ///         <item><description><see cref="VisualElement.HeightRequest" /> on the box (set by some subclasses).</description></item>
    ///         <item><description><see cref="G9Metrics.ControlHeight" /> as the final fallback.</description></item>
    ///     </list>
    /// </summary>
    private double ResolveFieldHeight()
    {
        if (FieldHeight > 0)
        {
            return FieldHeight;
        }

        var measured = _box.Height;
        if (measured > 8)
        {
            return measured;
        }

        var request = _box.HeightRequest;
        if (request > 0)
        {
            return request;
        }

        return G9Metrics.ControlHeight;
    }

    private void OnFieldHeightChanged()
    {
        // Push the requested height onto the box. G9Editor uses HeightRequest = -1 with
        // AutoSize, so we only override when the consumer set an explicit value.
        if (FieldHeight > 0)
        {
            _box.HeightRequest = FieldHeight;
            _box.MinimumHeightRequest = FieldHeight;
        }

        // Invalidate the cached rest values so the next visual pass repositions the label.
        _lastRestY = double.NaN;
        RequestVisualUpdate();
    }

    /// <summary>Length of the inner value used by the character counter. Override in subclasses with a Text property.</summary>
    protected virtual int GetTextLength() => 0;

    /// <summary>
    ///     Re-paint just the outline notch, without touching the stroke color, ring state,
    ///     or any other visual property. Used after a label remeasure so the notch keeps
    ///     pace with the label width without firing a full visuals pass (which would replay
    ///     the floating-label animation).
    /// </summary>
    private void UpdateOutlineNotchOnly()
    {
        if (!_outline.ShowNotch) return;

        var labelWidth = _measuredLabelWidth > 0
            ? _measuredLabelWidth
            : (_floatingLabel.Text?.Length ?? 0) * 7.0;
        labelWidth *= G9Metrics.FloatingLabelFloatedScale;

        // Gap between the visible text and each notch edge — tunable via metrics.
        var notchPadding = G9Metrics.OutlineNotchTextGap;
        // Match the actual floated label position (corner padding + the small extra
        // breathing-room offset from FloatingLabelFloatedExtraX) so the notch cut-out
        // stays aligned with the text inside it.
        var anchorX = G9Metrics.InputHorizontalPadding + G9Metrics.FloatingLabelFloatedExtraX;

        if (G9Visuals.IsRtl)
        {
            var width = (float)Math.Max(0, _box.Width);
            if (width <= 0)
            {
                return;
            }

            _outline.NotchRight = (float)(width - anchorX + notchPadding);
            _outline.NotchLeft = (float)(width - anchorX - labelWidth - notchPadding);
        }
        else
        {
            _outline.NotchLeft = (float)(anchorX - notchPadding);
            _outline.NotchRight = (float)(anchorX + labelWidth + notchPadding);
        }

        _outlineView.Invalidate();
    }

    private void UpdateOutline(Color stateColor, bool floated, double labelAnchorX)
    {
        ApplyOutlineChrome(stateColor);

        if (floated && !string.IsNullOrWhiteSpace(_floatingLabel.Text))
        {
            // Width measured by the label after layout. Fall back to a heuristic on the very
            // first paint so the gap is roughly correct before measurement settles.
            var labelWidth = _measuredLabelWidth > 0
                ? _measuredLabelWidth
                : _floatingLabel.Text!.Length * 7.0;

            // Apply the floated scale because the label visibly shrinks in the floated state.
            labelWidth *= G9Metrics.FloatingLabelFloatedScale;

            // Gap between the visible text and each notch edge — tunable via metrics.
            var notchPadding = G9Metrics.OutlineNotchTextGap;

            if (G9Visuals.IsRtl)
            {
                // In RTL the label hugs the right edge of the input. The notch must be measured
                // from the right (right padding ↔ right edge of label).
                var width = (float)Math.Max(0, _box.Width);
                if (width > 0)
                {
                    _outline.NotchRight = (float)(width - labelAnchorX + notchPadding);
                    _outline.NotchLeft = (float)(width - labelAnchorX - labelWidth - notchPadding);
                    _outline.ShowNotch = true;
                }
                else
                {
                    _outline.ShowNotch = false;
                }
            }
            else
            {
                _outline.NotchLeft = (float)(labelAnchorX - notchPadding);
                _outline.NotchRight = (float)(labelAnchorX + labelWidth + notchPadding);
                _outline.ShowNotch = true;
            }
        }
        else
        {
            _outline.ShowNotch = false;
        }

        _outlineView.Invalidate();
    }

    private void ApplyOutlineChrome(Color stateColor)
    {
        _outline.StrokeColor = stateColor;
        _outline.StrokeThickness = G9Metrics.OutlinedFieldStrokeThickness;
        _outline.CornerRadius = (float)G9Metrics.RadiusMd;

        // Focus / error / status emphasis is applied as a thicker stroke on the SAME
        // outline rather than a separate outer ring. A normal focus state can ALSO draw a
        // soft inner halo on the same notched path, giving the two-stroke read without
        // extending beyond the GraphicsView bounds — but that inner glow is opt-in per
        // field via ShowFocusHalo and OFF by default (see the property remarks).
        var showEmphasis = IsContentFocused || HasError || UseStatusColor;
        _outline.EmphasisStrokeThickness = showEmphasis
            ? G9Metrics.OutlinedFieldEmphasisStrokeThickness
            : 0f;

        var showFocusHalo = ShowFocusHalo && IsContentFocused && !HasError && !UseStatusColor;
        _outline.HaloStrokeColor = showFocusHalo
            ? stateColor.WithAlpha(G9Metrics.FocusRingOpacity)
            : null;
        _outline.HaloStrokeThickness = showFocusHalo
            ? G9Metrics.OutlinedFieldFocusHaloThickness
            : 0f;
    }

    private Color ResolveStateColor(G9Palette palette)
    {
        if (HasError) return palette.Error;
        if (UseStatusColor) return StatusColor ?? palette.Primary;
        return IsContentFocused || HasFilledValue() ? palette.Primary : ResolveRestingOutlineColor(palette);
    }

    /// <summary>
    ///     The field's chrome colour AT REST — not focused, no value, no error. Drives the outline and
    ///     the trailing icon. The leading icon and un-floated label are resolved separately by
    ///     <see cref="ResolveRestingContentColor" />.
    ///     <para>
    ///         Overridable so a field can make its resting chrome match its own placeholder instead of
    ///         the generic <see cref="G9Palette.Outline" /> hairline — see
    ///         <c>G9SearchEntry</c>. Only the RESTING colour is a subclass's to choose: focused,
    ///         filled, error and status states stay on the shared palette so every input in the app
    ///         still signals those the same way.
    ///     </para>
    /// </summary>
    protected virtual Color ResolveRestingOutlineColor(G9Palette palette) => palette.Outline;

    /// <summary>
    ///     The un-floated placeholder/label and leading-icon colour AT REST. Defaults to
    ///     <see cref="G9Palette.TextTertiary" /> for normal outlined fields. Search overrides this
    ///     to the dedicated input-placeholder token so its starter state is one muted input tone.
    /// </summary>
    protected virtual Color ResolveRestingContentColor(G9Palette palette) => palette.TextTertiary;

    private bool HasFilledValue() => HasContentValue || IsValueFloated;

    private bool HasLeadingIcon()
    {
        return G9IconFactory.HasIcon(LeadingEmoji, LeadingIcon, LeadingImagePath, LeadingImageSource);
    }

    /// <summary>Implementation hook so G9TextEntry can include "ClearButton" as a visible trailing affordance.</summary>
    protected virtual bool HasExtraTrailingAffordance() => false;

    private bool HasVisibleTrailingIcon()
    {
        return IsTrailingBusy
               || HasExtraTrailingAffordance()
               || G9IconFactory.HasIcon(TrailingEmoji, TrailingIcon, TrailingImagePath, TrailingImageSource);
    }

    private void RebuildIcons(Color stateColor, G9FieldSlotLayout slot)
    {
        var palette = G9Palette.Current;

        // ── Resolve physical columns ────────────────────────────────────────────
        // The box's FlowDirection is locked to LTR so columns 0/2 are always physical
        // left/right. We re-assign Grid.Column on the icon hosts every visual pass so
        // a culture flip (LTR ↔ RTL) physically swaps the leading and trailing icons.
        // ForceTrailingIconRight pins the trailing host to physical-right regardless of
        // direction (used by password / barcode entries).
        var leadingColumn = slot.ResolvePhysicalLeadingColumn();
        var trailingColumn = slot.ResolvePhysicalTrailingColumn();

        if (Grid.GetColumn(_leadingHost) != leadingColumn)
        {
            Grid.SetColumn(_leadingHost, leadingColumn);
        }
        if (Grid.GetColumn(_trailingHost) != trailingColumn)
        {
            Grid.SetColumn(_trailingHost, trailingColumn);
        }

        // ── Size each icon host explicitly ──────────────────────────────────────
        // The host's WidthRequest is set to (OuterMargin + glyph + InnerMargin).
        // The icon glyph inside the host uses Margin to position itself with the
        // correct outer/inner gaps. Combined with the parent Grid's Auto column,
        // this guarantees the column reports that exact width on every platform.
        var leadingHostWidth = G9Metrics.LeadingIconSlotWidth;
        var trailingHostWidth = G9Metrics.TrailingIconSlotWidth;

        if (Math.Abs(_leadingHost.WidthRequest - leadingHostWidth) > 0.01)
        {
            _leadingHost.WidthRequest = leadingHostWidth;
        }
        if (Math.Abs(_trailingHost.WidthRequest - trailingHostWidth) > 0.01)
        {
            _trailingHost.WidthRequest = trailingHostWidth;
        }

        // Apply asymmetric margins on icon hosts. The margin is in PHYSICAL coordinates
        // because the parent Grid is locked to LTR. We resolve which physical side is
        // "outer" (wall) vs "inner" (text) based on the icon's physical column:
        //   - Leading icon at column 0 (physical-left): outer=left, inner=right
        //   - Leading icon at column 2 (physical-right): outer=right, inner=left
        //   - Trailing icon at column 2 (physical-right): outer=right, inner=left
        //   - Trailing icon at column 0 (physical-left): outer=left, inner=right
        Thickness leadingPad;
        if (leadingColumn == 0)
        {
            leadingPad = new Thickness(
                G9Metrics.LeadingIconOuterMargin, 0,
                G9Metrics.LeadingIconInnerMargin, 0);
        }
        else
        {
            leadingPad = new Thickness(
                G9Metrics.LeadingIconInnerMargin, 0,
                G9Metrics.LeadingIconOuterMargin, 0);
        }
        if (_leadingHost.Padding != leadingPad)
            _leadingHost.Padding = leadingPad;

        Thickness trailingPad;
        if (trailingColumn == 2)
        {
            trailingPad = new Thickness(
                G9Metrics.TrailingIconInnerMargin, 0,
                G9Metrics.TrailingIconOuterMargin, 0);
        }
        else
        {
            trailingPad = new Thickness(
                G9Metrics.TrailingIconOuterMargin, 0,
                G9Metrics.TrailingIconInnerMargin, 0);
        }
        if (_trailingHost.Padding != trailingPad)
            _trailingHost.Padding = trailingPad;

        // ── Ripple layer must span the FULL host, ignoring the icon padding ──────
        // The ripple GraphicsView lives inside the padded icon host. On Android the
        // canvas overflows the host padding so the ripple already fills the whole slot.
        // On WinUI (and iOS / Mac Catalyst) a padded layout arranges the child within
        // the content rect, so the GraphicsView's dirtyRect — and therefore the ripple's
        // max radius, computed from the rect diagonal — shrinks to the inner glyph area;
        // the ripple visibly only paints the centre of the slot and is clipped to the
        // box. Counter the padding with an equal negative margin on the ripple layer so
        // its arranged rect equals the full host bounds on every platform. Android keeps
        // margin 0 (its behaviour is already correct and must not change).
        ExpandRippleToHost(_leadingRipple, leadingPad);
        ExpandRippleToHost(_trailingRipple, trailingPad);

        // ── Leading icon ──
        var hasLeading = HasLeadingIcon();
        if (_leadingHost.IsVisible != hasLeading)
            _leadingHost.IsVisible = hasLeading;
        if (_leadingHost.HorizontalOptions != LayoutOptions.Fill)
            _leadingHost.HorizontalOptions = LayoutOptions.Fill;
        var leadingSig = hasLeading
            ? $"L|{LeadingEmoji}|{LeadingIcon}|{LeadingImagePath}|{(LeadingImageSource is null ? "0" : "1")}"
            : "L|none";
        if (_leadingSignature != leadingSig)
        {
            _leadingSignature = leadingSig;
            ApplyLeadingIcon(hasLeading, ResolveRestingContentColor(palette));
        }
        else if (hasLeading)
        {
            // Only update the icon color without rebuilding the view tree.
            UpdateIconColor(_leadingHost, ResolveRestingContentColor(palette));
        }

        // ── Trailing icon ──
        var hasTrailing = HasVisibleTrailingIcon();
        // Call-to-action trailing glyphs (e.g. the barcode scan icon) can opt to stay tinted
        // even at rest via ResolveTrailingIconColor; default is the resolved state colour.
        var trailingIconColor = ResolveTrailingIconColor(stateColor);
        if (_trailingHost.IsVisible != hasTrailing)
            _trailingHost.IsVisible = hasTrailing;
        if (_trailingHost.HorizontalOptions != LayoutOptions.Fill)
            _trailingHost.HorizontalOptions = LayoutOptions.Fill;

        // Build a signature describing the trailing visual. Subclasses that drive their own
        // affordance (password toggle, clear button, scanner state) participate via
        // ResolveTrailingIconSignature so cached identity remains correct.
        // Color is excluded from the signature so focus/unfocus only updates the color
        // in-place without tearing down and rebuilding the view tree.
        string trailingSig;
        if (IsTrailingBusy)
        {
            trailingSig = "T|busy";
        }
        else
        {
            var subclassSig = ResolveTrailingIconSignature(stateColor);
            if (subclassSig is not null)
            {
                trailingSig = $"T|sub|{subclassSig}";
            }
            else if (G9IconFactory.HasIcon(TrailingEmoji, TrailingIcon, TrailingImagePath, TrailingImageSource))
            {
                trailingSig = $"T|def|{TrailingEmoji}|{TrailingIcon}|{TrailingImagePath}|{(TrailingImageSource is null ? "0" : "1")}";
            }
            else
            {
                trailingSig = "T|none";
            }
        }

        if (_trailingSignature == trailingSig)
        {
            // Signature unchanged — just update color without rebuilding. The cached
            // material icon path also handles its own color via ShowDefaultTrailingIcon
            // below, but routing here covers subclass / image / emoji paths uniformly.
            if (hasTrailing) UpdateIconColor(_trailingHost, IsTrailingBusy ? stateColor : trailingIconColor);
            return;
        }

        _trailingSignature = trailingSig;

        if (IsTrailingBusy)
        {
            // Reuse the cached spinner so the swap busy↔idle does NOT detach the
            // material-icon view (whose platform handler caches the embedded font's
            // rasterized glyph). See _trailingDefaultMauiIcon and _trailingBusyIndicator
            // for the full rationale.
            ShowTrailingBusyIndicator(stateColor);
            return;
        }

        var subclassTrailing = ResolveTrailingIcon(stateColor);
        if (subclassTrailing is not null)
        {
            // Subclass returned its own custom view — hide the cached default icon
            // and spinner, route through the legacy SetIconHostContent path.
            HideCachedTrailingChildren();
            SetIconHostContent(_trailingHost, subclassTrailing);
            return;
        }

        if (TrailingIcon.HasValue
            && string.IsNullOrWhiteSpace(TrailingEmoji)
            && string.IsNullOrWhiteSpace(TrailingImagePath)
            && TrailingImageSource is null)
        {
            // Pure-Material-icon trailing slot — the common case (clear button, password
            // toggle replaced by a custom icon, scanner glyphs, generic info icons).
            // Show the cached G9IconView and mutate Icon / IconColor on it. Crucially,
            // when the previous frame had IsTrailingBusy=true, the cached G9IconView was
            // already in the visual tree (just hidden) — flipping IsVisible back on
            // does not trigger handler creation, so the user sees the QR glyph in the
            // very first frame after the busy state ends, with no tofu rectangle.
            ShowDefaultTrailingIcon(TrailingIcon.Value, trailingIconColor);
            return;
        }

        if (G9IconFactory.HasIcon(TrailingEmoji, TrailingIcon, TrailingImagePath, TrailingImageSource))
        {
            // Emoji / image / mixed trailing slot — these paths involve fonts or raster
            // sources that don't share the embedded-Material-font tofu issue, so the
            // pre-existing detach + reattach behaviour is fine.
            HideCachedTrailingChildren();
            SetIconHostContent(_trailingHost, G9IconFactory.Create(
                TrailingEmoji, TrailingIcon, TrailingImagePath, TrailingImageSource,
                trailingIconColor, G9Metrics.InputIconSize));
            return;
        }

        HideCachedTrailingChildren();
        SetIconHostContent(_trailingHost, null);
    }

    /// <summary>
    ///     Apply the leading icon. Material-icon-only path uses the cached
    ///     <see cref="_leadingDefaultMauiIcon" /> to avoid tofu flashes on signature flips
    ///     (e.g. theme palette swap that propagates a re-resolve). Emoji / image paths
    ///     fall back to <see cref="SetIconHostContent" />, same as before.
    /// </summary>
    private void ApplyLeadingIcon(bool hasLeading, Color color)
    {
        if (!hasLeading)
        {
            HideCachedLeadingChild();
            SetIconHostContent(_leadingHost, null);
            return;
        }

        if (LeadingIcon.HasValue
            && string.IsNullOrWhiteSpace(LeadingEmoji)
            && string.IsNullOrWhiteSpace(LeadingImagePath)
            && LeadingImageSource is null)
        {
            ShowDefaultLeadingIcon(LeadingIcon.Value, color);
            return;
        }

        HideCachedLeadingChild();
        SetIconHostContent(_leadingHost, G9IconFactory.Create(
            LeadingEmoji, LeadingIcon, LeadingImagePath, LeadingImageSource,
            color, G9Metrics.InputIconSize));
    }

    private void ShowDefaultTrailingIcon(G9IconSource icon, Color color)
    {
        if (_trailingDefaultMauiIcon is null)
        {
            _trailingDefaultMauiIcon = new G9IconView {
                Icon = icon,
                Color = color,
                Size = G9Metrics.InputIconSize,
                WidthRequest = G9Metrics.InputIconSize,
                HeightRequest = G9Metrics.InputIconSize,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            };
            _trailingHost.Children.Add(_trailingDefaultMauiIcon);
        }
        else
        {
            if (!Equals(_trailingDefaultMauiIcon.Icon, icon)) _trailingDefaultMauiIcon.Icon = icon;
            if (_trailingDefaultMauiIcon.Color != color) _trailingDefaultMauiIcon.Color = color;
        }

        // Hide any subclass / emoji / image content currently attached, then make the
        // cached material icon visible. Spinner stays in the tree but hidden.
        for (var i = _trailingHost.Children.Count - 1; i >= 0; i--)
        {
            var child = _trailingHost.Children[i];
            if (child is GraphicsView) continue;
            if (ReferenceEquals(child, _trailingDefaultMauiIcon))
            {
                if (_trailingDefaultMauiIcon.IsVisible != true) _trailingDefaultMauiIcon.IsVisible = true;
                continue;
            }
            if (ReferenceEquals(child, _trailingBusyIndicator))
            {
                if (_trailingBusyIndicator!.IsRunning) _trailingBusyIndicator.IsRunning = false;
                if (_trailingBusyIndicator.IsVisible) _trailingBusyIndicator.IsVisible = false;
                continue;
            }
            // Foreign child (subclass-supplied or image / emoji) — remove it.
            _trailingHost.Children.RemoveAt(i);
        }
    }

    private void ShowDefaultLeadingIcon(G9IconSource icon, Color color)
    {
        if (_leadingDefaultMauiIcon is null)
        {
            _leadingDefaultMauiIcon = new G9IconView {
                Icon = icon,
                Color = color,
                Size = G9Metrics.InputIconSize,
                WidthRequest = G9Metrics.InputIconSize,
                HeightRequest = G9Metrics.InputIconSize,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center,
                InputTransparent = true
            };
            _leadingHost.Children.Add(_leadingDefaultMauiIcon);
        }
        else
        {
            if (!Equals(_leadingDefaultMauiIcon.Icon, icon)) _leadingDefaultMauiIcon.Icon = icon;
            if (_leadingDefaultMauiIcon.Color != color) _leadingDefaultMauiIcon.Color = color;
        }

        for (var i = _leadingHost.Children.Count - 1; i >= 0; i--)
        {
            var child = _leadingHost.Children[i];
            if (child is GraphicsView) continue;
            if (ReferenceEquals(child, _leadingDefaultMauiIcon))
            {
                if (_leadingDefaultMauiIcon.IsVisible != true) _leadingDefaultMauiIcon.IsVisible = true;
                continue;
            }
            _leadingHost.Children.RemoveAt(i);
        }
    }

    private void ShowTrailingBusyIndicator(Color color)
    {
        if (_trailingBusyIndicator is null)
        {
            _trailingBusyIndicator = new ActivityIndicator
            {
                IsRunning = true,
                Color = color,
                WidthRequest = 18,
                HeightRequest = 18,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            };
            _trailingHost.Children.Add(_trailingBusyIndicator);
        }
        else
        {
            if (_trailingBusyIndicator.Color != color) _trailingBusyIndicator.Color = color;
            if (!_trailingBusyIndicator.IsRunning) _trailingBusyIndicator.IsRunning = true;
        }

        for (var i = _trailingHost.Children.Count - 1; i >= 0; i--)
        {
            var child = _trailingHost.Children[i];
            if (child is GraphicsView) continue;
            if (ReferenceEquals(child, _trailingBusyIndicator))
            {
                if (_trailingBusyIndicator.IsVisible != true) _trailingBusyIndicator.IsVisible = true;
                continue;
            }
            if (ReferenceEquals(child, _trailingDefaultMauiIcon))
            {
                // Hide the cached material icon, keep it attached so the next un-busy
                // transition can show it without re-creating the platform handler.
                if (_trailingDefaultMauiIcon!.IsVisible) _trailingDefaultMauiIcon.IsVisible = false;
                continue;
            }
            _trailingHost.Children.RemoveAt(i);
        }
    }

    /// <summary>
    ///     Hide the cached default material icon and spinner without removing them from
    ///     the visual tree, then strip any other non-ripple children. Called when the
    ///     trailing slot transitions to a path that wants to attach its own view
    ///     (subclass-supplied, emoji, image, or "no icon").
    /// </summary>
    private void HideCachedTrailingChildren()
    {
        if (_trailingDefaultMauiIcon is { IsVisible: true })
        {
            _trailingDefaultMauiIcon.IsVisible = false;
        }
        if (_trailingBusyIndicator is not null)
        {
            if (_trailingBusyIndicator.IsRunning) _trailingBusyIndicator.IsRunning = false;
            if (_trailingBusyIndicator.IsVisible) _trailingBusyIndicator.IsVisible = false;
        }
    }

    private void HideCachedLeadingChild()
    {
        if (_leadingDefaultMauiIcon is { IsVisible: true })
        {
            _leadingDefaultMauiIcon.IsVisible = false;
        }
    }

    /// <summary>
    ///     Replace the icon-host's icon child while preserving the ripple overlay layer
    ///     that sits at index 0. <see cref="Grid" /> is used as the host (instead of
    ///     <see cref="ContentView" />) because <c>Grid.Padding</c> is reliably honoured
    ///     during measurement on every platform (Android / iOS / WinUI), while
    ///     <c>ContentView.Padding</c> has known edge cases when the child carries an
    ///     explicit <c>View.WidthRequest</c> — the host measured at the child's
    ///     width and the padding visually collapsed to zero.
    ///     <para>
    ///         The ripple <see cref="GraphicsView" /> is added in the constructor and
    ///         always remains as the first child of the host. We only clear / replace
    ///         non-<see cref="GraphicsView" /> children here so the ripple layer stays
    ///         intact across icon swaps, focus / blur, theme changes, and busy-state
    ///         transitions.
    ///     </para>
    /// </summary>
    private static void SetIconHostContent(Grid host, View? content)
    {
        // Remove every child except the ripple overlay (GraphicsView).
        for (var i = host.Children.Count - 1; i >= 0; i--)
        {
            if (host.Children[i] is GraphicsView) continue;
            host.Children.RemoveAt(i);
        }
        if (content is not null)
        {
            host.Children.Add(content);
        }
    }

    /// <summary>
    ///     Update just the color of an existing icon inside a host Grid without rebuilding
    ///     the view tree. Handles G9IconView (IconColor), Label (TextColor), and
    ///     ActivityIndicator (Color) cases. Avoids the focus/unfocus icon flash.
    ///     <para>
    ///         The host's first child is the ripple <see cref="GraphicsView" /> overlay,
    ///         so we skip past it to find the actual icon view. The trailing host can
    ///         simultaneously hold the cached <see cref="G9IconView" /> and
    ///         <see cref="ActivityIndicator" /> with one of them hidden — we pick the
    ///         visible one, since the hidden one's color is irrelevant until it
    ///         re-appears (at which point the next signature flip writes a fresh
    ///         color anyway).
    ///     </para>
    /// </summary>
    private static void UpdateIconColor(Grid host, Color color)
    {
        foreach (var child in host.Children)
        {
            switch (child)
            {
                case GraphicsView: continue; // skip the ripple layer
                case VisualElement ve when !ve.IsVisible: continue;
                case G9IconView icon:
                    if (icon.Color != color) icon.Color = color;
                    return;
                case Microsoft.Maui.Controls.Label:
                    // Emoji labels keep their natural color — skip.
                    return;
                case ActivityIndicator ai:
                    if (ai.Color != color) ai.Color = color;
                    return;
            }
        }
    }

    /// <summary>
    ///     Builds a stable ripple layer for an icon host. The layer is a
    ///     <see cref="GraphicsView" /> (not a <see cref="Border" />) because we don't
    ///     want a stroke around the ripple — only the painted circle. We rely on the
    ///     drawable's own <c>FillCircle</c> + alpha decay for the soft edge, so no
    ///     platform-clipping is needed: any portion of the ripple that overshoots the
    ///     host bounds is simply not rendered (the GraphicsView is sized to the host).
    /// </summary>
    private static GraphicsView BuildRippleLayer(G9RippleDrawable drawable)
    {
        return new GraphicsView
        {
            Drawable = drawable,
            BackgroundColor = Colors.Transparent,
            InputTransparent = true,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            Opacity = 0
        };
    }

    /// <summary>
    ///     Counter the icon-host padding with an equal negative margin on the ripple
    ///     <see cref="GraphicsView" /> so its arranged rect equals the FULL host bounds,
    ///     not the padding-inset content rect. This makes the ripple's max radius
    ///     (computed from the canvas diagonal in <see cref="G9RippleDrawable" />) cover
    ///     the whole icon slot.
    ///     <para>
    ///     <b>Android is exempt.</b> On Android the GraphicsView canvas already overflows
    ///     the host padding, so the ripple fills the slot correctly with margin 0; adding
    ///     a negative margin there would over-expand it. The negative-margin counter is
    ///     only needed where a padded layout clips the child to the content rect — WinUI,
    ///     iOS, and Mac Catalyst. See <c>G9Controls.md</c> §15 (Windows pitfall about the
    ///     ripple only painting the box interior).
    ///     </para>
    /// </summary>
    private static void ExpandRippleToHost(GraphicsView ripple, Thickness hostPadding)
    {
#if ANDROID
        // No-op — Android's GraphicsView already overflows host padding.
        _ = ripple;
        _ = hostPadding;
#else
        var target = new Thickness(-hostPadding.Left, -hostPadding.Top, -hostPadding.Right, -hostPadding.Bottom);
        if (ripple.Margin != target)
        {
            ripple.Margin = target;
        }
#endif
    }

    /// <summary>
    ///     Combined press animation: a soft scale dip on the host (visible as the icon
    ///     compressing slightly into the chip) plus a Material-style ink ripple expanding
    ///     from the tap origin. The two animations run in parallel — by the time the
    ///     scale-back-up completes, the ripple has already faded out, so a second tap
    ///     starts on a clean canvas.
    /// </summary>
    private static void PlayIconPress(Grid host, GraphicsView ripple, G9RippleDrawable drawable, PointF origin)
    {
        // Tint the ripple to the host's resolved icon color at a low alpha. This keeps
        // the ripple readable on every theme — light icon on dark surface produces a
        // subtle pale ripple; dark icon on light surface produces a faint dark ripple.
        // We sniff the icon's current color from the host's child view tree.
        drawable.Color = ResolveRippleTint(host);
        drawable.Center = origin;
        drawable.Progress = 0;
        ripple.Opacity = 1;
        ripple.Invalidate();

        // Ripple radius animation — CubicOut so the wave decelerates as it expands,
        // matching the M3 "ink drop" recipe.
        var rippleAnim = new Animation(v =>
        {
            drawable.Progress = (float)v;
            ripple.Invalidate();
        }, 0, 1);
        rippleAnim.Commit(host, "AppFieldIconRipple", 16, G9Metrics.RippleDurationMs, Easing.CubicOut, (_, _) =>
        {
            ripple.Opacity = 0;
        });

        // Scale dip animation in parallel. Lighter than the previous 0.78 dip — feels
        // like a tactile press without the icon visibly "punching in" too far.
        host.AbortAnimation("AppFieldIconScale");
        var scaleAnim = new Animation(v => host.Scale = v, host.Scale, G9Metrics.IconPressScaleTo, Easing.CubicIn);
        scaleAnim.Commit(host, "AppFieldIconScale", 16, G9Metrics.PressDurationMs, finished: (_, _) =>
        {
            var release = new Animation(v => host.Scale = v, host.Scale, 1.0, Easing.SpringOut);
            release.Commit(host, "AppFieldIconScale", 16, G9Metrics.ReleaseDurationMs);
        });
    }

    /// <summary>
    ///     Picks a sensible ripple ink color from the host's child icon. G9IconView exposes
    ///     <see cref="G9IconView.Color" />, Label exposes
    ///     <see cref="Label.TextColor" />, and ActivityIndicator exposes
    ///     <see cref="ActivityIndicator.Color" />. If we can't read a color we fall back
    ///     to the theme's primary at low alpha — the ripple still plays, just in the brand
    ///     color instead of the icon's tint.
    /// </summary>
    private static Color ResolveRippleTint(Grid host)
    {
        Color? sampled = null;
        foreach (var child in host.Children)
        {
            switch (child)
            {
                case GraphicsView: continue; // skip the ripple layer itself
                case G9IconView mauiIcon when mauiIcon.Color is not null:
                    sampled = mauiIcon.Color;
                    break;
                case Microsoft.Maui.Controls.Label lbl when lbl.TextColor is not null:
                    sampled = lbl.TextColor;
                    break;
                case ActivityIndicator ai when ai.Color is not null:
                    sampled = ai.Color;
                    break;
            }
            if (sampled is not null) break;
        }
        var basis = sampled ?? G9Palette.Current.Primary;
        return basis.WithAlpha(G9Colors.IconRippleAlpha);
    }

    /// <summary>
    ///     Override to draw a built-in trailing affordance (clear, password toggle, etc.).
    ///     Return null to fall back to the default <see cref="TrailingIcon" /> rendering.
    /// </summary>
    protected virtual View? ResolveTrailingIcon(Color stateColor) => null;

    /// <summary>
    ///     Override to provide a stable signature that identifies the subclass-rendered trailing
    ///     affordance. Used to skip unnecessary view-tree swaps. Return null when the subclass
    ///     does not own the trailing slot.
    /// </summary>
    protected virtual string? ResolveTrailingIconSignature(Color stateColor) => null;

    /// <summary>
    ///     The colour used to paint the trailing icon glyph. Defaults to the field's resolved
    ///     state colour (neutral outline when resting, Primary when focused/filled, status/error
    ///     when applicable). Subclasses whose trailing icon is a call-to-action — e.g. the barcode
    ///     scan glyph — can override this to keep the icon tinted even while the field's outline is
    ///     in its neutral resting state, so the action does not read as disabled. The busy spinner
    ///     and status/error states are unaffected (they paint with the state colour directly).
    /// </summary>
    protected virtual Color ResolveTrailingIconColor(Color stateColor) => stateColor;
}
