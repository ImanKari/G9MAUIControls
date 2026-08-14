using System.Windows.Input;

namespace G9MAUIControls.Controls;

/// <summary>
///     Gives any view a tap command plus hover and press feedback, animated on the compositor.
///     <para>
///         <b>Why the suite carries its own instead of taking a behaviours package.</b> This
///         replaces one narrow use of a third-party <c>TouchBehavior</c> in <c>G9TabBar</c>. That
///         package is a fine library, but the feedback it was providing here is an opacity tween
///         and a command invoke — roughly the code below — and a control suite should not put a
///         whole extra dependency into every consumer's app to get it.
///     </para>
///     <para>
///         <b>Opacity, never scale, and never a shadow.</b> The animation writes
///         <see cref="VisualElement.Opacity" />, which maps to a native compositor alpha on every
///         target (Android <c>RenderNode</c>, iOS <c>CALayer</c>, WinUI <c>CompositionTransform</c>),
///         so it costs no layout pass. Scale is deliberately left at rest: a tab cell that grows on
///         hover shifts its neighbours' optical centres. A shadow is out of the question — see
///         <c>Controls/G9Controls.md</c> §0.
///     </para>
///     <para>
///         <b>Children are made input-transparent.</b> A label or icon inside the target would
///         otherwise consume the Android touch stream and the parent's recognizer would never fire —
///         the "it only responds sometimes, near the edges" class of bug described in
///         <c>G9Controls.md</c> §10b.
///     </para>
/// </summary>
public sealed class G9PressFeedbackBehavior : Behavior<View>
{
    private View? _target;
    private TapGestureRecognizer? _tap;
    private PointerGestureRecognizer? _pointer;

    /// <summary>Opacity at rest.</summary>
    public double RestOpacity { get; init; } = 1.0;

    /// <summary>Opacity while a pointer hovers (desktop only; touch never reports hover).</summary>
    public double HoveredOpacity { get; init; } = 0.86;

    /// <summary>Opacity while pressed.</summary>
    public double PressedOpacity { get; init; } = 0.78;

    /// <summary>Duration of each opacity transition, in milliseconds.</summary>
    public uint AnimationDurationMs { get; init; } = 110;

    /// <summary>Invoked on tap.</summary>
    public ICommand? Command { get; init; }

    /// <summary>Passed to <see cref="Command" />.</summary>
    public object? CommandParameter { get; init; }

    /// <summary>
    ///     Builds a behavior with the suite's standard feedback curve, wired to
    ///     <paramref name="command" />.
    /// </summary>
    public static G9PressFeedbackBehavior For(ICommand command) => new() { Command = command };

    /// <inheritdoc />
    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _target = bindable;

        MakeChildrenInputTransparent(bindable);

        _tap = new TapGestureRecognizer();
        _tap.Tapped += OnTapped;
        bindable.GestureRecognizers.Add(_tap);

        _pointer = new PointerGestureRecognizer();
        _pointer.PointerEntered += OnPointerEntered;
        _pointer.PointerExited += OnPointerExited;
        _pointer.PointerPressed += OnPointerPressed;
        _pointer.PointerReleased += OnPointerReleased;
        bindable.GestureRecognizers.Add(_pointer);
    }

    /// <inheritdoc />
    protected override void OnDetachingFrom(View bindable)
    {
        if (_tap is not null)
        {
            _tap.Tapped -= OnTapped;
            bindable.GestureRecognizers.Remove(_tap);
            _tap = null;
        }

        if (_pointer is not null)
        {
            _pointer.PointerEntered -= OnPointerEntered;
            _pointer.PointerExited -= OnPointerExited;
            _pointer.PointerPressed -= OnPointerPressed;
            _pointer.PointerReleased -= OnPointerReleased;
            bindable.GestureRecognizers.Remove(_pointer);
            _pointer = null;
        }

        _target = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        // Touch platforms report no hover, so a tap is the only chance to show press feedback at
        // all: flash to pressed and settle back.
        _ = FlashAsync();

        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
    }

    private void OnPointerEntered(object? sender, PointerEventArgs e) => AnimateTo(HoveredOpacity);

    private void OnPointerExited(object? sender, PointerEventArgs e) => AnimateTo(RestOpacity);

    private void OnPointerPressed(object? sender, PointerEventArgs e) => AnimateTo(PressedOpacity);

    private void OnPointerReleased(object? sender, PointerEventArgs e) => AnimateTo(HoveredOpacity);

    private async Task FlashAsync()
    {
        var target = _target;
        if (target is null)
        {
            return;
        }

        AnimateTo(PressedOpacity);
        await Task.Delay((int)AnimationDurationMs).ConfigureAwait(true);

        // Re-read: the view may have been detached while the flash was in flight.
        if (_target is not null)
        {
            AnimateTo(RestOpacity);
        }
    }

    private void AnimateTo(double opacity)
    {
        var target = _target;
        if (target is null)
        {
            return;
        }

        // One named animator, so a rapid hover→press→release sequence replaces the in-flight tween
        // instead of stacking three that fight over the same property.
        target.Animate(
            "G9PressFeedback",
            new Animation(v => target.Opacity = v, target.Opacity, opacity),
            length: AnimationDurationMs,
            easing: Easing.SinOut);
    }

    private static void MakeChildrenInputTransparent(View view)
    {
        if (view is not Layout layout)
        {
            return;
        }

        foreach (var child in layout.Children)
        {
            if (child is View childView)
            {
                childView.InputTransparent = true;
            }
        }
    }
}
