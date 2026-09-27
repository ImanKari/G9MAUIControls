namespace G9MAUIControls.Controls;

// This file is deliberately free of any MAUI dependency: it is file-linked into
// tests/G9MAUIControls.Tests and compiled there as plain net10.0. Geometry is therefore plain
// floats in UNIT box coordinates (0..1 across the square); G9CheckBoxDrawable scales it to dp.

/// <summary>
///     The state rules and the animation timeline of <see cref="G9CheckBox" />, kept out of the
///     control so they can be tested without a device.
///     <para>
///         <b>One progress value drives the whole checked transition.</b> <c>0</c> is an empty
///         outlined square, <c>1</c> a filled square with its mark fully drawn. The fill occupies the
///         first <see cref="FillPhaseEnd" /> of the timeline and the mark the part after
///         <see cref="MarkPhaseStart" />; the two overlap so the tick starts drawing while the fill is
///         still settling, which is what makes it read as one gesture rather than two steps.
///         Un-checking runs the SAME value backwards, so a tap that reverses a half-finished
///         animation simply turns around from wherever it is — no second timeline to reconcile.
///     </para>
/// </summary>
internal static class G9CheckBoxMath
{
    /// <summary>Timeline point at which the box fill has fully arrived.</summary>
    public const float FillPhaseEnd = 0.4f;

    /// <summary>Timeline point at which the mark starts drawing itself in.</summary>
    public const float MarkPhaseStart = 0.25f;

    /// <summary>
    ///     Where the checked timeline rests for a state: a checked OR indeterminate box is a filled
    ///     box with its mark drawn; only the mark's SHAPE differs (<see cref="TargetMorph" />).
    /// </summary>
    public static float TargetProgress(bool isChecked, bool isIndeterminate) =>
        isChecked || isIndeterminate ? 1f : 0f;

    /// <summary>Mark shape: <c>0</c> = check, <c>1</c> = indeterminate bar.</summary>
    public static float TargetMorph(bool isIndeterminate) => isIndeterminate ? 1f : 0f;

    /// <summary>
    ///     What a user tap turns the box into. An indeterminate box becomes CHECKED (the platform
    ///     convention — Android, iOS and WinUI all resolve "mixed" towards "all"); otherwise the
    ///     checked state flips. The result is never indeterminate: only code can put a box there.
    /// </summary>
    public static (bool IsChecked, bool IsIndeterminate) NextOnTap(bool isChecked, bool isIndeterminate) =>
        isIndeterminate ? (true, false) : (!isChecked, false);

    /// <summary>How much of the box fill is showing at a timeline point, 0..1.</summary>
    public static float FillAmount(float progress) => Clamp01(progress / FillPhaseEnd);

    /// <summary>How much of the mark's length is drawn at a timeline point, 0..1.</summary>
    public static float MarkAmount(float progress) =>
        Clamp01((progress - MarkPhaseStart) / (1f - MarkPhaseStart));

    /// <summary>
    ///     Clamps to 0..1. <c>NaN</c> (a 0/0 from an animation that was handed a degenerate range)
    ///     maps to 0 — drawing nothing is the safe failure for a paint pass.
    /// </summary>
    public static float Clamp01(float value)
    {
        if (!(value > 0f)) return 0f;
        return value >= 1f ? 1f : value;
    }
}

/// <summary>
///     The mark as a two-segment polyline A → B → C in unit box coordinates. The check and the
///     indeterminate bar share the SAME three points — the bar is just the check with B pulled up
///     level with A and C — so interpolating the points morphs one into the other, and the same
///     length-trim draws either one in.
/// </summary>
internal readonly record struct G9CheckMarkGeometry(float Ax, float Ay, float Bx, float By, float Cx, float Cy)
{
    /// <summary>
    ///     The tick: a short arm down to the elbow, a long arm up to the top-end, ~1 : 2. Never
    ///     mirrored in RTL — a check is a glyph, not a layout (G9Controls.md §9).
    /// </summary>
    public static readonly G9CheckMarkGeometry Check = new(0.24f, 0.52f, 0.42f, 0.70f, 0.77f, 0.33f);

    /// <summary>The indeterminate bar, centred, with the same end insets as the tick.</summary>
    public static readonly G9CheckMarkGeometry Dash = new(0.26f, 0.50f, 0.50f, 0.50f, 0.74f, 0.50f);

    /// <summary>The mark at a morph point between <see cref="Check" /> (0) and <see cref="Dash" /> (1).</summary>
    public static G9CheckMarkGeometry Resolve(float morph)
    {
        var t = G9CheckBoxMath.Clamp01(morph);
        if (t <= 0f) return Check;
        if (t >= 1f) return Dash;

        return new G9CheckMarkGeometry(
            Lerp(Check.Ax, Dash.Ax, t), Lerp(Check.Ay, Dash.Ay, t),
            Lerp(Check.Bx, Dash.Bx, t), Lerp(Check.By, Dash.By, t),
            Lerp(Check.Cx, Dash.Cx, t), Lerp(Check.Cy, Dash.Cy, t));
    }

    /// <summary>Length of A → B.</summary>
    public float FirstLength => Distance(Ax, Ay, Bx, By);

    /// <summary>Length of B → C.</summary>
    public float SecondLength => Distance(Bx, By, Cx, Cy);

    /// <summary>
    ///     The part of the polyline drawn when <paramref name="amount" /> of its total LENGTH is
    ///     showing — a stroke that draws itself in from A. Trimming by length (not by segment) keeps
    ///     the pen at a constant speed across the elbow.
    /// </summary>
    public G9CheckMarkTrim Trim(float amount)
    {
        var t = G9CheckBoxMath.Clamp01(amount);
        var first = FirstLength;
        var total = first + SecondLength;
        if (t <= 0f || total <= 0f) return G9CheckMarkTrim.Empty;

        var drawn = t * total;
        if (drawn <= first)
        {
            var f = drawn / first;
            return new G9CheckMarkTrim(false, Ax + ((Bx - Ax) * f), Ay + ((By - Ay) * f), false, Bx, By);
        }

        // drawn > first here, so the second arm has a positive length.
        var s = (drawn - first) / (total - first);
        return new G9CheckMarkTrim(false, Bx, By, true, Bx + ((Cx - Bx) * s), By + ((Cy - By) * s));
    }

    private static float Lerp(float from, float to, float t) => from + ((to - from) * t);

    private static float Distance(float x1, float y1, float x2, float y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        return MathF.Sqrt((dx * dx) + (dy * dy));
    }
}

/// <summary>
///     A trimmed mark, ready to stroke: the first arm runs A → (<see cref="FirstEndX" />,
///     <see cref="FirstEndY" />); when <see cref="HasSecond" /> the second runs B →
///     (<see cref="SecondEndX" />, <see cref="SecondEndY" />).
/// </summary>
internal readonly record struct G9CheckMarkTrim(
    bool IsEmpty,
    float FirstEndX,
    float FirstEndY,
    bool HasSecond,
    float SecondEndX,
    float SecondEndY)
{
    /// <summary>Nothing drawn.</summary>
    public static readonly G9CheckMarkTrim Empty = new(true, 0f, 0f, false, 0f, 0f);
}
