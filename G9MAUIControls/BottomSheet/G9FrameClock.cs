#if ANDROID
using Android.Views;
#endif

namespace G9MAUIControls.BottomSheet;

/// <summary>
///     The display's frame clock, shared by everything in the sheet engine that has to act ONCE PER
///     FRAME — the pre-open frame waits and the motion driver.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why one shared clock.</b> On Android a frame callback is a Java object, and every
///         managed object that has a Java peer is a <i>GC bridge</i> object: when it dies, the .NET
///         collector has to stop and synchronise with the Java collector to let go of it. A device
///         trace of 59 sheet opens showed one such bridged collection every 1.1 s, each stalling
///         the UI thread for tens of milliseconds, and 36 of the 59 opens had at least one land
///         inside them. Allocating a callback object per awaited frame (which the first version of
///         <see cref="G9FrameAwaiter" /> did) manufactures exactly that garbage, in exactly the
///         window where a stall is most visible. This class owns ONE callback for the life of the
///         process and re-posts it; a frame costs no allocation at all.
///     </para>
///     <para>
///         <b>Why the frame TIME matters.</b> A subscriber receives the frame's vsync timestamp,
///         not the time its callback happened to run. Motion evaluated against the vsync time lands
///         where the display will actually show it; motion evaluated against "now" inherits however
///         late the callback was scheduled, which reads as micro-stutter even at a full frame rate.
///         It is the clock the platform's own animators use, for that reason.
///     </para>
///     <para><b>Threading:</b> main thread only.</para>
/// </remarks>
internal static class G9FrameClock
{
    /// <summary>The nominal frame interval assumed until two real frames have been observed.</summary>
    public const long DefaultFrameIntervalNanos = 16_666_667;

    private const long MinimumFrameIntervalNanos = 4_000_000;
    private const long MaximumFrameIntervalNanos = 34_000_000;

    /// <summary>
    ///     The most recently observed interval between two consecutive frames, clamped to a sane
    ///     display range (240 Hz … 30 Hz) so one hitch cannot pass for the refresh rate.
    /// </summary>
    public static long FrameIntervalNanos { get; private set; } = DefaultFrameIntervalNanos;

#if ANDROID
    private static readonly List<Action<long>> Subscribers = new();
    private static readonly List<Action<long>> Dispatching = new();
    private static readonly List<TaskCompletionSource> OneShots = new();
    private static readonly List<TaskCompletionSource> OneShotsDispatching = new();

    private static FrameSource? _source;
    private static bool _isPosted;
    private static long _lastFrameNanos;

    /// <summary>True when this platform has a real frame clock to subscribe to.</summary>
    public static bool IsSupported => true;

    /// <summary>
    ///     Calls <paramref name="onFrame" /> with the vsync timestamp (ns) of every frame until it
    ///     is unsubscribed. Returns <c>false</c> when no clock is available on this thread.
    /// </summary>
    public static bool Subscribe(Action<long> onFrame)
    {
        if (!Subscribers.Contains(onFrame))
        {
            Subscribers.Add(onFrame);
        }

        return EnsurePosted();
    }

    public static void Unsubscribe(Action<long> onFrame)
    {
        Subscribers.Remove(onFrame);
    }

    /// <summary>Completes at the start of the next frame, or <c>null</c> when no clock is available.</summary>
    public static Task? NextFrameAsync()
    {
        // Continuations run INLINE, inside the frame callback: we are on the main thread at the
        // start of a frame, which is exactly where the next pipeline step wants to run. Queuing
        // them instead costs a trip through the main looper — up to a frame — per awaited frame.
        var completion = new TaskCompletionSource();
        OneShots.Add(completion);

        if (EnsurePosted())
        {
            return completion.Task;
        }

        OneShots.Remove(completion);
        return null;
    }

    private static bool EnsurePosted()
    {
        if (_isPosted)
        {
            return true;
        }

        var choreographer = Choreographer.Instance;
        if (choreographer is null)
        {
            return false;
        }

        _source ??= new FrameSource();
        choreographer.PostFrameCallback(_source);
        _isPosted = true;
        return true;
    }

    private static void OnFrame(long frameTimeNanos)
    {
        _isPosted = false;

        if (_lastFrameNanos != 0)
        {
            var interval = frameTimeNanos - _lastFrameNanos;
            if (interval is >= MinimumFrameIntervalNanos and <= MaximumFrameIntervalNanos)
            {
                FrameIntervalNanos = interval;
            }
        }

        _lastFrameNanos = frameTimeNanos;

        try
        {
            // Snapshots: a subscriber may unsubscribe itself (a motion that just finished) or a
            // continuation may ask for another frame while we iterate.
            if (Subscribers.Count > 0)
            {
                Dispatching.AddRange(Subscribers);
                foreach (var subscriber in Dispatching)
                {
                    // Unsubscribed by an earlier subscriber in this same frame.
                    if (Subscribers.Contains(subscriber))
                    {
                        subscriber(frameTimeNanos);
                    }
                }
            }

            if (OneShots.Count > 0)
            {
                OneShotsDispatching.AddRange(OneShots);
                OneShots.Clear();
                foreach (var completion in OneShotsDispatching)
                {
                    completion.TrySetResult();
                }
            }
        }
        finally
        {
            // Whatever a subscriber did — including throw — the clock itself stays consistent:
            // a wedged `_isPosted` would freeze every later sheet motion in the process.
            Dispatching.Clear();
            OneShotsDispatching.Clear();

            if (Subscribers.Count > 0 || OneShots.Count > 0)
            {
                EnsurePosted();
            }
            else
            {
                // Idle: the next interval measured would span the gap, not a frame.
                _lastFrameNanos = 0;
            }
        }
    }

    private sealed class FrameSource : Java.Lang.Object, Choreographer.IFrameCallback
    {
        public void DoFrame(long frameTimeNanos)
        {
            OnFrame(frameTimeNanos);
        }
    }
#else
    /// <summary>True when this platform has a real frame clock to subscribe to.</summary>
    public static bool IsSupported => false;

    public static bool Subscribe(Action<long> onFrame) => false;

    public static void Unsubscribe(Action<long> onFrame)
    {
    }

    public static Task? NextFrameAsync() => null;
#endif
}
