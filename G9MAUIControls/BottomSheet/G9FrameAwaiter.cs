using System.Diagnostics;

namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Waits for display FRAMES rather than for milliseconds.
/// </summary>
/// <remarks>
///     <para>
///         The sheet pipeline used to sequence itself with fixed delays — 369 ms before building
///         deferred content, 220 ms for glyphs to settle, 16 / 160 / 380 ms settle passes. Every
///         one of them was standing in for "the platform has laid this out and drawn it", sized
///         for the slowest device and paid in full on every other. A frame is the unit that
///         question is actually asked in, so this asks it directly.
///     </para>
///     <para>
///         Android resolves a frame through the <c>Choreographer</c> — the same clock the view
///         system draws on — so "one frame later" means exactly one traversal opportunity later.
///         The other platforms approximate it with a 16 ms delay; none of them has the first-frames
///         problems this exists for, so the approximation only has to be short, not exact.
///     </para>
///     <para><b>Threading:</b> main thread only, like the engine that calls it.</para>
/// </remarks>
internal static class G9FrameAwaiter
{
    private const int FallbackFrameMs = 16;

    /// <summary>Completes at the start of the next display frame.</summary>
    public static Task NextFrameAsync()
    {
        // The shared clock owns the ONE platform frame callback of the process; asking it for a
        // frame allocates no Java peer (see G9FrameClock for why that matters here).
        return G9FrameClock.NextFrameAsync() ?? Task.Delay(FallbackFrameMs);
    }

    /// <summary>
    ///     Polls <paramref name="condition" /> once per frame until it holds or
    ///     <paramref name="deadline" /> passes. Returns whether the condition held.
    /// </summary>
    /// <remarks>
    ///     The deadline is what makes every wait in the pipeline safe: a condition that never
    ///     becomes true delays an open, it cannot hang one.
    /// </remarks>
    public static async Task<bool> WaitUntilAsync(Func<bool> condition, Deadline deadline)
    {
        while (!condition())
        {
            if (deadline.IsExpired)
            {
                return false;
            }

            await NextFrameAsync().ConfigureAwait(true);
        }

        return true;
    }

    /// <summary>Waits <paramref name="frames" /> frames, stopping early at the deadline.</summary>
    public static async Task WaitFramesAsync(int frames, Deadline deadline)
    {
        for (var i = 0; i < frames && !deadline.IsExpired; i++)
        {
            await NextFrameAsync().ConfigureAwait(true);
        }
    }

    /// <summary>A monotonic point in time. Wall-clock changes cannot move it.</summary>
    public readonly struct Deadline
    {
        private readonly long _expiresAt;

        private Deadline(long expiresAt)
        {
            _expiresAt = expiresAt;
        }

        public static Deadline After(int milliseconds)
        {
            var ticks = (long)(Math.Max(0, milliseconds) / 1000d * Stopwatch.Frequency);
            return new Deadline(Stopwatch.GetTimestamp() + ticks);
        }

        public bool IsExpired => Stopwatch.GetTimestamp() >= _expiresAt;

        /// <summary>Milliseconds left, never negative.</summary>
        public int RemainingMs
        {
            get
            {
                var remaining = _expiresAt - Stopwatch.GetTimestamp();
                return remaining <= 0 ? 0 : (int)(remaining * 1000 / Stopwatch.Frequency);
            }
        }
    }
}
