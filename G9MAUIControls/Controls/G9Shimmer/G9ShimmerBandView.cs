namespace G9MAUIControls.Controls;

/// <summary>
///     The moving highlight band of a <see cref="G9Shimmer" /> skeleton. This is a thin
///     handler-backed view whose sweep animation is owned by the PLATFORM compositor — Android
///     runs it as an <c>AnimatedVectorDrawable</c> on the RenderThread (API 25+), iOS/Mac Catalyst
///     as a <c>CABasicAnimation</c> in the Core Animation render server — so the band keeps moving
///     even while the UI thread is blocked building the heavy content the shimmer masks. That is
///     the same mechanism that keeps the stock <c>ActivityIndicator</c> spinning under load, and
///     the reason UI-thread shimmer implementations (MAUI <c>Animation</c>, SkiaSharp invalidation
///     timers) froze exactly when they were needed.
///     <para>
///         Windows has no handler registered — <see cref="G9Shimmer" /> simply omits the band
///         there and shows the static skeleton shapes.
///     </para>
/// </summary>
public sealed class G9ShimmerBandView : View
{
    public G9ShimmerBandView()
    {
        InputTransparent = true;
    }
}
