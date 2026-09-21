namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Runs ONE sheet motion — a value travelling from A to B along an easing curve — against the
///     display's frame clock.
/// </summary>
/// <remarks>
///     <para>
///         MAUI's <c>Animation</c> advances by the wall-clock time between ticks. That is fine for
///         a fade; it is wrong for a sheet, for two reasons a device trace made measurable (34 of
///         119 motions had a frame gap over 25 ms, almost always the first):
///     </para>
///     <list type="number">
///         <item>
///             <b>The first frame is the expensive one</b> — the hardware layer is allocated and the
///             body rasterised for the first time. A decelerating curve is at its FASTEST at the
///             start: 32 ms into a 300 ms quintic ease-out the sheet is already 43 % of the way.
///             Charge that late first frame to the curve and the sheet does not slide in, it
///             appears half-open. The platform's own animators start their clock on the first
///             frame they draw (<c>START_ON_FIRST_FRAME</c>); so does this.
///         </item>
///         <item>
///             <b>A hitch in the middle teleports the sheet</b> by however long the hitch lasted. A
///             hitch cannot be un-dropped, but it can be shown as a brief PAUSE instead of a jump:
///             a frame that arrives more than <see cref="HitchThresholdFrames" /> intervals late
///             advances the motion by a single interval. The motion finishes that much later and
///             stays continuous, which the eye forgives far more readily.
///         </item>
///     </list>
///     <para>
///         Positions are evaluated at the frame's VSYNC time, not at the time the callback ran, so
///         scheduling jitter does not become positional jitter. The motion also ends as soon as it
///         is within <see cref="ArrivalEpsilon" /> of its target: the long tail of an ease-out is
///         invisible, and everything waiting on the motion (the deferred load, the close cleanup)
///         should not wait on pixels nobody can see.
///     </para>
///     <para>
///         Where there is no frame clock to subscribe to (iOS, Mac Catalyst, Windows) the same
///         curve and duration run on MAUI's animation ticker, which on Apple platforms is already
///         display-link driven.
///     </para>
///     <para><b>Threading:</b> main thread only.</para>
/// </remarks>
internal sealed class G9SheetMotionDriver
{
    /// <summary>Within this many dp of the target, the motion is over.</summary>
    private const double ArrivalEpsilon = 0.5;

    /// <summary>A frame later than this many intervals is a hitch, and is absorbed.</summary>
    private const double HitchThresholdFrames = 2.5;

    private readonly Action<long> _onFrame;

    private Action<double>? _apply;
    private Action? _finished;
    private Easing _easing = Easing.Linear;
    private double _from;
    private double _to;
    private long _durationNanos;
    private long _elapsedNanos;
    private long _lastFrameNanos;
    private IAnimatable? _fallbackOwner;
    private string? _fallbackName;

    public G9SheetMotionDriver()
    {
        _onFrame = OnFrame;
    }

    /// <summary>
    ///     App-wide switch (default <c>true</c>): drive motion from the display frame clock where
    ///     one exists. <c>false</c> runs every motion on MAUI's animation ticker, as 1.1.0 did.
    /// </summary>
    public static bool UseFrameClock { get; set; } = true;

    public bool IsRunning { get; private set; }

    /// <summary>Total time (ms) absorbed as hitches during the current / last motion.</summary>
    public double AbsorbedHitchMs { get; private set; }

    /// <summary>
    ///     Starts a motion. <paramref name="apply" /> receives every intermediate value and,
    ///     exactly once, <paramref name="to" />; <paramref name="finished" /> runs after that.
    ///     Neither is called again once <see cref="Cancel" /> returns.
    /// </summary>
    public void Start(
        IAnimatable fallbackOwner,
        string fallbackName,
        double from,
        double to,
        int durationMs,
        Easing easing,
        Action<double> apply,
        Action finished)
    {
        Cancel();

        _apply = apply;
        _finished = finished;
        _easing = easing;
        _from = from;
        _to = to;
        _durationNanos = Math.Max(1, durationMs) * 1_000_000L;
        _elapsedNanos = 0;
        _lastFrameNanos = 0;
        AbsorbedHitchMs = 0;
        IsRunning = true;

        if (UseFrameClock && AreSystemAnimationsEnabled() && G9FrameClock.Subscribe(_onFrame))
        {
            return;
        }

        // No frame clock: MAUI's ticker runs the same curve. It also honours the system
        // "remove animations" setting by completing immediately, which is the behaviour we want.
        _fallbackOwner = fallbackOwner;
        _fallbackName = fallbackName;
        new Animation(value => _apply?.Invoke(value), from, to).Commit(
            fallbackOwner,
            fallbackName,
            length: (uint)Math.Max(1, durationMs),
            easing: easing,
            finished: (_, cancelled) =>
            {
                if (!cancelled)
                {
                    Complete(applyTarget: false);
                }
            });
    }

    /// <summary>Stops the motion where it is. No further callbacks.</summary>
    public void Cancel()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        _apply = null;
        _finished = null;
        G9FrameClock.Unsubscribe(_onFrame);

        var owner = _fallbackOwner;
        var name = _fallbackName;
        _fallbackOwner = null;
        _fallbackName = null;
        if (owner is not null && name is not null)
        {
            owner.AbortAnimation(name);
        }
    }

    private void OnFrame(long frameTimeNanos)
    {
        if (!IsRunning)
        {
            G9FrameClock.Unsubscribe(_onFrame);
            return;
        }

        var interval = G9FrameClock.FrameIntervalNanos;

        if (_lastFrameNanos == 0)
        {
            // START ON FIRST FRAME. Whatever it cost to get here — allocating the layer, the
            // first rasterisation — is not charged to the curve. The frame being prepared now
            // reaches the display one interval from now, so that is the time it is evaluated for.
            _elapsedNanos = interval;
        }
        else
        {
            var delta = frameTimeNanos - _lastFrameNanos;
            if (delta > interval * HitchThresholdFrames)
            {
                AbsorbedHitchMs += (delta - interval) / 1_000_000d;
                delta = interval;
            }

            _elapsedNanos += Math.Max(0, delta);
        }

        _lastFrameNanos = frameTimeNanos;

        var progress = Math.Min(1d, _elapsedNanos / (double)_durationNanos);
        var value = _from + ((_to - _from) * _easing.Ease(progress));

        if (progress >= 1d || Math.Abs(_to - value) < ArrivalEpsilon)
        {
            Complete(applyTarget: true);
            return;
        }

        _apply?.Invoke(value);
    }

    private void Complete(bool applyTarget)
    {
        if (!IsRunning)
        {
            return;
        }

        var apply = _apply;
        var finished = _finished;

        IsRunning = false;
        _apply = null;
        _finished = null;
        _fallbackOwner = null;
        _fallbackName = null;
        G9FrameClock.Unsubscribe(_onFrame);

        if (applyTarget)
        {
            apply?.Invoke(_to);
        }

        finished?.Invoke();
    }

    private static bool AreSystemAnimationsEnabled()
    {
#if ANDROID
        // "Remove animations" / battery saver: the platform's animators jump to their end value,
        // and a sheet that kept sliding would be the one thing on screen ignoring the setting.
        return Android.Animation.ValueAnimator.AreAnimatorsEnabled();
#else
        return true;
#endif
    }
}
