namespace G9MAUIControls.Controls;

/// <summary>
///     Shared rendering helpers for the outlined input visuals (G9TextEntry, G9Editor,
///     G9Picker, G9ComboBox, G9DateTimePicker). The actual outline + notch is painted by
///     <see cref="G9OutlinedFieldDrawable" /> which is owned by <see cref="G9OutlinedFieldBase" />;
///     this helper only handles the floating-label TranslationX/Y/Scale animation.
/// </summary>
internal static class G9OutlinedFieldVisual
{
    private const string FloatLabelAnimationName = "AppFloatLabel";

    /// <summary>
    ///     Animates a floating label between rest (centered inside the box and shifted over
    ///     the leading icon when present) and floated (at the corner padding, scaled down,
    ///     translated above the border). Uses a single MAUI <see cref="Animation" /> so the
    ///     three transforms run on the compositor and stay in sync.
    ///     <para>
    ///         <b>Font-attribute deferral</b>: when transitioning to rest (unfocusing),
    ///         the label font must stay Bold until the animation finishes. Changing
    ///         Bold→None before the animation causes an immediate width change (because
    ///         Bold text is wider) which makes the label visibly jump/blink at the start
    ///         of the slide. We defer the font change to the animation's completion
    ///         callback so the text width stays constant throughout the slide, then snaps
    ///         to Normal once the label is already in its final rest position.
    ///     </para>
    /// </summary>
    /// <param name="owner">VisualElement that owns the animation timer.</param>
    /// <param name="label">The label to animate.</param>
    /// <param name="floated">Target state — <c>true</c> for floated, <c>false</c> for rest.</param>
    /// <param name="restY">Y translation when floated is false (centered inside the box).</param>
    /// <param name="restX">X translation when floated is false (shifted over a leading icon).</param>
    /// <param name="floatedX">
    ///     X translation when floated is true. Usually a small leading-direction offset so
    ///     the label is not flush against the corner curvature.
    /// </param>
    /// <param name="animate">When false, jump immediately to the target state.</param>
    /// <param name="targetFontAttributes">
    ///     The font attributes that should apply in the target state. Applied immediately
    ///     when floating (Bold) and deferred to after animation completes when un-floating
    ///     (None) so the Bold→None width change doesn't cause a visible jump mid-animation.
    /// </param>
    /// <param name="targetColor">
    ///     The text color for the target state. Applied immediately when floating and
    ///     deferred when un-floating so the color transition doesn't flash.
    /// </param>
    public static void AnimateFloatingLabel(
        VisualElement owner,
        Label label,
        bool floated,
        double restY,
        double restX,
        double floatedX,
        bool animate,
        FontAttributes targetFontAttributes = FontAttributes.None,
        Color? targetColor = null)
    {
        var targetY = floated ? G9Metrics.FloatingLabelFloatedY : restY;
        var targetX = floated ? floatedX : restX;
        var targetScale = floated ? G9Metrics.FloatingLabelFloatedScale : G9Metrics.FloatingLabelRestScale;

        if (!animate)
        {
            owner.AbortAnimation(FloatLabelAnimationName);
            label.TranslationX = targetX;
            label.TranslationY = targetY;
            label.Scale = targetScale;
            label.FontAttributes = targetFontAttributes;
            if (targetColor is not null) label.TextColor = targetColor;
            return;
        }

        // When floating: apply Bold + color immediately so the notch calculation
        // uses the correct (wider) text width from the start.
        // When un-floating: defer font/color to completion so the Bold→None width
        // shrink doesn't happen during the slide (causes a visible jump).
        if (floated)
        {
            label.FontAttributes = targetFontAttributes;
            if (targetColor is not null) label.TextColor = targetColor;
        }

        var startX = label.TranslationX;
        var startY = label.TranslationY;
        var startScale = label.Scale;

        owner.AbortAnimation(FloatLabelAnimationName);
        var anim = new Animation
        {
            { 0, 1, new Animation(v => label.TranslationX = startX + ((targetX - startX) * v)) },
            { 0, 1, new Animation(v => label.TranslationY = startY + ((targetY - startY) * v)) },
            { 0, 1, new Animation(v => label.Scale = startScale + ((targetScale - startScale) * v)) }
        };
        anim.Commit(owner, FloatLabelAnimationName, 16, G9Metrics.FloatingLabelDurationMs, Easing.CubicOut,
            finished: (_, _) =>
            {
                // Deferred font/color change for un-floating (rest state).
                if (!floated)
                {
                    label.FontAttributes = targetFontAttributes;
                    if (targetColor is not null) label.TextColor = targetColor;
                }
            });
    }
}
