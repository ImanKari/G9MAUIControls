using G9MAUIControls.Theming;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;

namespace G9MAUIControls.Controls;

internal sealed class G9DrumItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Text { get; init; }
    public required int Value { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
///     Drum-style picker column built on a native <see cref="ScrollView" /> for the smooth
///     platform-correct fling behaviour, with a polling-based settle detector to decide
///     when to snap. This is the cross-platform-stable architecture: every platform
///     (Android, iOS, macOS Catalyst, Windows) ships a built-in scroller with proper
///     touch / fling semantics — we use it directly instead of trying to re-implement
///     pan, fling and inertia on top of <see cref="PanGestureRecognizer" /> (which has
///     known timing issues on Android, see dotnet/maui#1495).
///     <para>
///         Settle detection: a single dispatcher timer ticks every 16ms while a snap is
///         pending. Each tick compares the current <c>ScrollY</c> against the previous
///         tick. When the position is stable for 2 consecutive ticks (≈32ms), we snap.
///     </para>
///     <para>
///         Snap animation: a custom per-frame interpolated scroll (169ms, CubicOut) that
///         is fully cancellable via a <see cref="CancellationTokenSource" />. Each frame
///         writes <c>ScrollToAsync(animated: false)</c> so the platform never runs its
///         own opaque deceleration animation — we own every frame and can stop instantly
///         on user touch.
///     </para>
///     <para>
///         Selection style updates live during the drag. As the user scrolls, the row
///         currently centered in the band is highlighted (bold + dark) so the user sees
///         exactly which value will be selected if they release at that moment. Only the
///         previously-selected and newly-selected items are touched per Scrolled event,
///         so the cost is O(1) regardless of column length.
///     </para>
/// </summary>
internal sealed class G9DrumColumn : Grid
{
    /// <summary>
    ///     Pixel tolerance for divergence detection during programmatic scrolls. Each
    ///     frame we set the position via <c>ScrollToAsync(animated: false)</c>, so the
    ///     ScrollY we read back from the platform should match <see cref="_lastProgrammaticScrollY"/>
    ///     to within a couple of pixels. Anything bigger is a user touch.
    /// </summary>
    private const double DivergenceThreshold = 20.0;

    /// <summary>Snap animation duration in ms. Short enough to feel instant, long enough to read as a glide.</summary>
    private const int SnapDurationMs = 169;

    /// <summary>
    ///     Slower animation duration used for the Today button and other programmatic
    ///     transitions where the user wants to SEE the columns rolling. The post-drag
    ///     snap is intentionally fast (169ms) so the user feels the gesture is committed
    ///     instantly; transitions like Today should read as a smooth roll, not a snap.
    /// </summary>
    public const int RollDurationMs = 480;

    /// <summary>Frame interval for the custom interpolated snap. 16ms ≈ 60fps.</summary>
    private const int SnapFrameIntervalMs = 16;

    private readonly Label _label;
    private readonly ScrollView _scroll;
    private readonly VerticalStackLayout _stack;
    private readonly GraphicsView _bandOverlay;
    private readonly G9DrumColumnDrawable _drawable = new();
    private readonly ObservableCollection<G9DrumItem> _items = [];
    private readonly double _columnHeight;
    private readonly double _topPad;
    private bool _programmaticScroll;
    private double _lastObservedY;
    private int _stableTickCount;
    private IDispatcherTimer? _settleTimer;

    /// <summary>
    ///     Last <see cref="ScrollView.ScrollY"/> we wrote programmatically. Used to detect
    ///     when the user touched the surface DURING our animated programmatic scroll —
    ///     when the position diverges from the value we wrote, the platform's animation
    ///     has been interrupted by a user touch and we should release the
    ///     <see cref="_programmaticScroll"/> guard immediately so OnScrolled processes
    ///     the user gesture instead of swallowing it.
    /// </summary>
    private double _lastProgrammaticScrollY;

    /// <summary>
    ///     Cancellation source for the in-flight snap animation. A new gesture cancels
    ///     the running animation immediately so the user is never locked out.
    /// </summary>
    private CancellationTokenSource? _snapCts;

    /// <summary>
    ///     Tick value when the most recent programmatic scroll guard was released.
    ///     Sub-pixel residual <see cref="ScrollView.Scrolled" /> events can fire for a
    ///     few frames after the guard releases — counting those as a "new user gesture"
    ///     and immediately settling produces a spurious snap fire. We ignore Scrolled
    ///     events with sub-pixel deltas within this short window.
    /// </summary>
    private long _guardReleasedTick;

    /// <summary>
    ///     Minimum time (ms) a fresh gesture must have been observed before
    ///     <see cref="OnSettleTick" /> is allowed to declare it settled. Without this,
    ///     short columns (e.g. 12-month) settle in 48ms while the user finger is still
    ///     dragging slowly, producing premature snaps that feel like the column "jumps"
    ///     before the user is done.
    /// </summary>
    private const int MinSettleAgeMs = 120;

    /// <summary>
    ///     Window (ms) after a programmatic guard release during which sub-pixel
    ///     Scrolled events (|dy| &lt; 1px) are ignored. Catches the residual reports
    ///     that fire as the platform's scroll position settles after our last frame.
    /// </summary>
    private const int PostGuardSettleWindowMs = 80;

    /// <summary>
    ///     Index of the item currently bold-styled by the live drag highlight. Tracked so
    ///     each Scrolled event only touches two items (clear old, set new) instead of
    ///     scanning the whole list. -1 means no item is highlighted.
    /// </summary>
    private int _liveHighlightIndex = -1;

    /// <summary>
    ///     ScrollY value from the previous OnScrolled call. Used to compute |dy| for
    ///     post-guard residual filtering.
    /// </summary>
    private double _lastScrolledY;

    /// <summary>
    ///     Tick when the current gesture's first non-residual Scrolled event arrived.
    ///     The settle detector won't declare a gesture settled until at least
    ///     <see cref="MinSettleAgeMs" /> have passed since this point — prevents the
    ///     case where a slow drag pauses for a frame and the snap fires while the
    ///     finger is still on the screen.
    /// </summary>
    private long _lastGestureStartTick;

    /// <summary>
    ///     Whether the user's finger is currently in contact with the scroll surface.
    ///     Set by the platform-specific touch hook attached on <c>HandlerChanged</c>:
    ///     <list type="bullet">
    ///         <item>Android — <c>MotionEventActions.Down</c> / <c>PointerDown</c> sets
    ///             true; <c>Up</c> / <c>Cancel</c> / <c>PointerUp</c> sets false.</item>
    ///         <item>iOS / Mac Catalyst — <c>UIPanGestureRecognizer</c>'s
    ///             <c>Began</c> / <c>Ended</c> / <c>Cancelled</c> states drive it.</item>
    ///         <item>Windows — Pointer pressed / released routed events on the
    ///             platform <c>ScrollViewer</c>.</item>
    ///     </list>
    ///     <para>
    ///         <b>Why it matters.</b> The settle detector polls <see cref="ScrollView.ScrollY" />
    ///         every 16 ms and snaps to the nearest row when the position is stable
    ///         for ≥ 2 ticks. If the user puts a finger ON the surface and HOLDS it
    ///         still — without dragging — the position is by definition stable, so
    ///         the settle detector would fire the snap animation while the finger is
    ///         still in contact. The user's subsequent drag would then be fighting
    ///         against the in-flight snap. Gating the settle on
    ///         <c>!_isFingerDown</c> lets the user freeze the column under their
    ///         finger and resume scrolling from exactly where it was when they
    ///         touched.
    ///     </para>
    /// </summary>
    private bool _isFingerDown;

    public G9DrumColumn(string label)
    {
        RowDefinitions =
        [
            new RowDefinition(GridLength.Auto),
            new RowDefinition(GridLength.Auto)
        ];

        _label = new Label
        {
            Text = label,
            FontSize = G9Metrics.DrumLabelFontSize,
            FontAttributes = FontAttributes.Bold,
            CharacterSpacing = 1,
            TextTransform = TextTransform.Uppercase,
            TextColor = G9Palette.Current.TextTertiary,
            HorizontalTextAlignment = TextAlignment.Center,
            Padding = new Thickness(0, 6, 0, 2)
        };
        Grid.SetRow(_label, 0);
        Children.Add(_label);

        _stack = new VerticalStackLayout
        {
            Spacing = 0,
            HorizontalOptions = LayoutOptions.Fill
        };

        _scroll = new ScrollView
        {
            Orientation = ScrollOrientation.Vertical,
            HeightRequest = G9Metrics.DrumColumnHeight,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            VerticalScrollBarVisibility = ScrollBarVisibility.Never,
            Content = _stack
        };
        _scroll.Scrolled += OnScrolled;
        _scroll.HandlerChanged += OnScrollHandlerChanged;

        _bandOverlay = new GraphicsView
        {
            Drawable = _drawable,
            InputTransparent = true
        };

        var host = new Grid
        {
            HeightRequest = G9Metrics.DrumColumnHeight,
            Children = { _scroll, _bandOverlay }
        };
        Grid.SetRow(host, 1);
        Children.Add(host);

        _columnHeight = G9Metrics.DrumColumnHeight;
        _topPad = (_columnHeight - G9Metrics.DrumRowHeight) / 2.0;
    }

    public event EventHandler<int>? SelectedValueChanged;

    public int SelectedValue { get; private set; }

    public int ItemCount => _items.Count;

    /// <summary>
    ///     Smoothly animates the column to the specified value with a configurable
    ///     duration. Used by the "Today" button to transition all columns in parallel
    ///     without rebuilding their items, and the longer default duration lets the user
    ///     SEE the columns rolling instead of snapping.
    /// </summary>
    public async Task AnimateToValue(int value, int durationMs = RollDurationMs)
    {
        if (SelectedValue == value)
        {
            SetSelected(value);
            return;
        }
        SetSelected(value);
        await ScrollToValueAsync(value, animate: true, durationMs).ConfigureAwait(true);
    }

    public void SetItems(IEnumerable<G9DrumItem> items, int selectedValue)
    {
        // Cancel any in-flight snap so a fresh column build doesn't fight a stale animation.
        CancelSnap();

        _items.Clear();
        _stack.Children.Clear();
        _liveHighlightIndex = -1;

        // Top spacer so the first row can sit in the center band when selected.
        _stack.Children.Add(new BoxView { HeightRequest = _topPad, Color = Colors.Transparent });

        var idx = 0;
        foreach (var item in items)
        {
            item.IsSelected = item.Value == selectedValue;
            _items.Add(item);
            if (item.IsSelected) _liveHighlightIndex = idx;
            _stack.Children.Add(CreateRow(item));
            idx++;
        }

        // Bottom spacer so the last row can also reach the center.
        _stack.Children.Add(new BoxView { HeightRequest = _topPad, Color = Colors.Transparent });

        SelectedValue = selectedValue;
        StopSettleTimer();
        _ = ScrollToValueAsync(selectedValue, animate: false);
    }

    /// <summary>
    ///     Incrementally trims or extends the column to the specified item count without
    ///     destroying existing rows. Use this for cheap updates when only the count
    ///     changes (e.g. day column going from 31 to 30 on month switch). The new items
    ///     are produced by <paramref name="newItemFactory" />, called only for indices
    ///     beyond the current count when extending. Items already in the column are kept
    ///     intact, so their <see cref="View" /> instances are reused — no measure /
    ///     arrange pass for the survivors. This is ~50× faster than <see cref="SetItems" />
    ///     on Android and runs in &lt;5ms instead of ~700ms for a typical 30↔31 transition.
    /// </summary>
    public void TrimOrExtendItems(int targetCount, int selectedValue, Func<int, G9DrumItem> newItemFactory)
    {
        if (_items.Count == targetCount)
        {
            // No structural change — just sync selection.
            SetSelected(selectedValue);
            return;
        }

        var previousScrollY = _scroll.ScrollY;
        var rowHeight = G9Metrics.DrumRowHeight;
        var maxValidScrollY = Math.Max(0, (targetCount - 1) * rowHeight);

        // The visual tree is: [topSpacer] + [row 0] + [row 1] + ... + [row N-1] + [bottomSpacer]
        // We need to keep both spacers and only adjust rows in between.
        if (_items.Count > targetCount)
        {
            // Trim — remove rows from the end. Bottom spacer is always last in _stack.
            var toRemove = _items.Count - targetCount;
            for (var i = 0; i < toRemove; i++)
            {
                // Remove the last row — second-to-last child (bottom spacer is the last).
                var lastRowIdx = _stack.Children.Count - 2;
                if (lastRowIdx >= 1)
                {
                    _stack.Children.RemoveAt(lastRowIdx);
                }
                _items.RemoveAt(_items.Count - 1);
            }
        }
        else
        {
            // Extend — insert rows just before the bottom spacer.
            var toAdd = targetCount - _items.Count;
            for (var i = 0; i < toAdd; i++)
            {
                var newIndex = _items.Count;
                var newItem = newItemFactory(newIndex);
                _items.Add(newItem);
                // Bottom spacer is the last child; insert before it.
                var insertAt = _stack.Children.Count - 1;
                _stack.Children.Insert(insertAt, CreateRow(newItem));
            }
        }

        SetSelected(selectedValue);

        // If we trimmed past the current scroll position, snap back to the selected
        // row so the band shows a real value instead of whitespace where the removed
        // row used to be. Use a fast 169ms glide so the user sees the correction.
        if (previousScrollY > maxValidScrollY)
        {
            _ = ScrollToValueAsync(selectedValue, animate: true);
        }
    }

    private View CreateRow(G9DrumItem item)
    {
        var palette = G9Palette.Current;
        var label = new Label
        {
            HeightRequest = G9Metrics.DrumRowHeight,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center,
            InputTransparent = true,
            BackgroundColor = Colors.Transparent
        };
        // Assigned, not bound. G9DrumItem.Text is `init`-only, so the binding could never fire a second
        // time — it bought nothing and cost trim-unsafety: a string-path Binding resolves by reflection and
        // is [RequiresUnreferencedCode] (IL2026 under AndroidLinkMode=Full; ADR-0011).
        label.Text = item.Text;
        Apply(label, item.IsSelected, palette);
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(G9DrumItem.IsSelected))
            {
                Apply(label, item.IsSelected, G9Palette.Current);
            }
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SelectFromTap(item);

        var hit = new ContentView
        {
            HeightRequest = G9Metrics.DrumRowHeight,
            BackgroundColor = Colors.Transparent,
            Content = label,
            BindingContext = item
        };
        hit.GestureRecognizers.Add(tap);
        return hit;
    }

    private static void Apply(Label label, bool selected, G9Palette palette)
    {
        label.FontSize = selected ? G9Metrics.DrumSelectedFontSize : G9Metrics.DrumUnselectedFontSize;
        label.FontAttributes = selected ? FontAttributes.Bold : FontAttributes.None;
        label.TextColor = selected ? palette.TextPrimary : palette.TextTertiary;
    }

    private void SelectFromTap(G9DrumItem item)
    {
        if (SelectedValue == item.Value)
        {
            _ = ScrollToValueAsync(item.Value, animate: true);
            return;
        }

        SetSelected(item.Value);
        _ = ScrollToValueAsync(item.Value, animate: true);
        SelectedValueChanged?.Invoke(this, item.Value);
    }

    private void SetSelected(int value)
    {
        SelectedValue = value;
        var newIdx = -1;
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var should = item.Value == value;
            item.IsSelected = should;
            if (should) newIdx = i;
        }
        _liveHighlightIndex = newIdx;
    }

    public void ScrollToSelected() => _ = ScrollToValueAsync(SelectedValue, animate: false);

    /// <summary>
    ///     Programmatic scroll to a row. <paramref name="animate" /> true uses the custom
    ///     169ms CubicOut interpolation that's fully cancellable. False sets the position
    ///     instantly (used for initial layout and re-positioning when the surface is
    ///     reused).
    /// </summary>
    private async Task ScrollToValueAsync(int value, bool animate, int durationMs = SnapDurationMs)
    {
        var index = -1;
        for (var i = 0; i < _items.Count; i++)
        {
            if (_items[i].Value == value)
            {
                index = i;
                break;
            }
        }
        if (index < 0) return;

        var y = index * G9Metrics.DrumRowHeight;

        // Wait for the ScrollView to be measured. On first open, the layout pass
        // hasn't completed yet and ScrollToAsync silently fails.
        if (_scroll.Width <= 0 || _scroll.Height <= 0)
        {
            var tcs = new TaskCompletionSource();
            void Handler(object? s, EventArgs e)
            {
                if (_scroll.Width > 0 && _scroll.Height > 0)
                {
                    _scroll.SizeChanged -= Handler;
                    tcs.TrySetResult();
                }
            }
            _scroll.SizeChanged += Handler;
            _ = Task.Delay(500).ContinueWith(_ => tcs.TrySetResult());
            await tcs.Task.ConfigureAwait(true);
            await Task.Delay(50).ConfigureAwait(true);
        }

        if (animate)
        {
            await AnimatedScrollAsync(y, durationMs).ConfigureAwait(true);
        }
        else
        {
            try
            {
                _programmaticScroll = true;
                _lastProgrammaticScrollY = y;
                await _scroll.ScrollToAsync(0, y, false).ConfigureAwait(true);
            }
            finally
            {
                _programmaticScroll = false;
                _guardReleasedTick = Environment.TickCount;
            }
        }
    }

    /// <summary>
    ///     Custom interpolated, cancellable scroll. Each frame writes the eased position
    ///     via <c>ScrollToAsync(animated: false)</c> which the platform applies instantly.
    ///     We own the timing, so a user touch can interrupt mid-flight by cancelling the
    ///     <see cref="_snapCts"/> token — the next frame returns immediately and the
    ///     <see cref="_programmaticScroll"/> guard releases, letting OnScrolled process
    ///     the user gesture without delay.
    /// </summary>
    private async Task AnimatedScrollAsync(double targetY, int durationMs = SnapDurationMs)
    {
        // Cancel any prior in-flight snap so we don't fight ourselves.
        CancelSnap();
        var cts = new CancellationTokenSource();
        _snapCts = cts;
        var token = cts.Token;

        var startY = _scroll.ScrollY;
        var distance = targetY - startY;
        if (Math.Abs(distance) < 0.5)
        {
            _snapCts = null;
            return;
        }

        var sw = Stopwatch.StartNew();
        try
        {
            _programmaticScroll = true;
            while (sw.ElapsedMilliseconds < durationMs)
            {
                if (token.IsCancellationRequested) return;

                var t = sw.ElapsedMilliseconds / (double)durationMs;
                if (t > 1) t = 1;
                var eased = Easing.CubicOut.Ease(t);
                var y = startY + distance * eased;

                _lastProgrammaticScrollY = y;
                try
                {
                    await _scroll.ScrollToAsync(0, y, false).ConfigureAwait(true);
                }
                catch (OperationCanceledException) { return; }

                if (token.IsCancellationRequested) return;
                try { await Task.Delay(SnapFrameIntervalMs, token).ConfigureAwait(true); }
                catch (OperationCanceledException) { return; }
            }

            // Final exact position.
            if (token.IsCancellationRequested) return;
            _lastProgrammaticScrollY = targetY;
            try { await _scroll.ScrollToAsync(0, targetY, false).ConfigureAwait(true); }
            catch (OperationCanceledException) { /* swallow */ }
        }
        finally
        {
            _programmaticScroll = false;
            _guardReleasedTick = Environment.TickCount;
            if (ReferenceEquals(_snapCts, cts))
            {
                _snapCts = null;
            }
            cts.Dispose();
        }
    }

    private void CancelSnap()
    {
        var cts = _snapCts;
        if (cts is null) return;
        try { cts.Cancel(); } catch { }
    }

    private void OnScrolled(object? sender, ScrolledEventArgs e)
    {
        var settleRunning = _settleTimer?.IsRunning ?? false;
        var dy = e.ScrollY - _lastScrolledY;
        _lastScrolledY = e.ScrollY;

        // Detect user interruption of an in-flight animated programmatic scroll.
        // Each animation frame we wrote the exact target Y via ScrollToAsync(animated: false),
        // so the platform should report that same Y back when it fires Scrolled. Any
        // deviation past DivergenceThreshold means a finger has touched the surface and
        // is dragging — we cancel the snap and release the guard so the gesture is
        // honored from this frame on instead of being silently swallowed.
        if (_programmaticScroll && Math.Abs(e.ScrollY - _lastProgrammaticScrollY) > DivergenceThreshold)
        {
            CancelSnap();
            _programmaticScroll = false;
        }

        // Skip our own programmatic scrolls to avoid recursive snap detection.
        if (_programmaticScroll)
        {
            _lastProgrammaticScrollY = e.ScrollY;
            return;
        }

        // Ignore sub-pixel residuals fired right after a programmatic snap finished.
        // Without this, a 0.3px platform-side echo of our last animation frame counts
        // as a "new gesture" and the settle timer immediately fires a spurious snap.
        // The threshold (|dy| < 1.0 within 80ms post-guard) is well below any real
        // user drag (which produces multi-px deltas) but catches the noise.
        //
        // EXCEPT when the finger is actually in contact with the surface — those
        // sub-pixel events are real slow-drag input from the user, not platform
        // echoes. Without the !_isFingerDown gate, slow drags that follow a fresh
        // snap are silently dropped: the highlight stays stuck on the previous
        // selection (no UpdateLiveSelection call), the settle timer never starts,
        // and the user sees the column "freeze" under their finger until they drag
        // fast enough to clear the residual window.
        if (!settleRunning
            && !_isFingerDown
            && Math.Abs(dy) < 1.0
            && Environment.TickCount - _guardReleasedTick < PostGuardSettleWindowMs)
        {
            return;
        }

        // Fresh user gesture: settle timer was idle and a new movement just arrived.
        if (!settleRunning)
        {
            _lastGestureStartTick = Environment.TickCount;
        }

        // Live selection: highlight the row currently centered in the band so the
        // user sees the snap target during the drag.
        UpdateLiveSelection(e.ScrollY);

        // Start (or re-arm) the settle detector. The timer ticks every 16ms and
        // declares the scroll "settled" when ScrollY hasn't moved for 2 consecutive
        // ticks. This is the only reliable cross-platform way to know when the
        // native fling has finished — there's no public "scroll-end" callback in
        // MAUI's ScrollView.
        _lastObservedY = e.ScrollY;
        _stableTickCount = 0;
        StartSettleTimer();
    }

    /// <summary>
    ///     O(1) live highlight update — only flips the previously-highlighted item off
    ///     and the newly-centered item on. Avoids the full-list scan that ran on every
    ///     Scrolled event in earlier versions (~30 events × 100 items per fling).
    /// </summary>
    private void UpdateLiveSelection(double scrollY)
    {
        if (_items.Count == 0) return;

        var rowHeight = G9Metrics.DrumRowHeight;
        var idx = (int)Math.Round(scrollY / rowHeight);
        idx = Math.Clamp(idx, 0, _items.Count - 1);
        if (idx == _liveHighlightIndex) return;

        if (_liveHighlightIndex >= 0 && _liveHighlightIndex < _items.Count)
        {
            _items[_liveHighlightIndex].IsSelected = false;
        }
        var newItem = _items[idx];
        newItem.IsSelected = true;
        _liveHighlightIndex = idx;
        SelectedValue = newItem.Value;
    }

    private void StartSettleTimer()
    {
        if (_settleTimer is null)
        {
            _settleTimer = Dispatcher.CreateTimer();
            _settleTimer.Interval = TimeSpan.FromMilliseconds(16);
            _settleTimer.IsRepeating = true;
            _settleTimer.Tick += OnSettleTick;
        }
        if (!_settleTimer.IsRunning)
        {
            _settleTimer.Start();
        }
    }

    private void StopSettleTimer()
    {
        if (_settleTimer?.IsRunning == true)
        {
            _settleTimer.Stop();
        }
        _stableTickCount = 0;
    }

    /// <summary>
    ///     Polls ScrollY at 16ms intervals to detect when the native fling has stopped.
    ///     The user lifts their finger → fling decelerates → eventually ScrollY stops
    ///     changing → 2 consecutive identical ticks → snap fires.
    /// </summary>
    private void OnSettleTick(object? sender, EventArgs e)
    {
        // Hard gate: while the finger is still in contact with the surface the
        // user is in control. The position will read as "stable" any time they
        // hold still — settling there would fire the snap animation under their
        // finger and fight the moment they start dragging again. Reset the
        // stable-tick counter so the post-release settle window starts fresh
        // once they lift off.
        if (_isFingerDown)
        {
            _stableTickCount = 0;
            _lastObservedY = _scroll.ScrollY;
            return;
        }

        var currentY = _scroll.ScrollY;
        var diff = Math.Abs(currentY - _lastObservedY);
        if (diff < 0.5)
        {
            _stableTickCount++;
            if (_stableTickCount >= 2)
            {
                // Don't settle a gesture that just barely started — the user could
                // still be in the middle of a slow drag. Require at least
                // MinSettleAgeMs since the gesture began before we declare it done.
                var gestureAge = Environment.TickCount - _lastGestureStartTick;
                if (gestureAge < MinSettleAgeMs)
                {
                    // Reset stable count so we wait another full stability window.
                    _stableTickCount = 0;
                    return;
                }
                StopSettleTimer();
                _ = SnapAfterSettleAsync(currentY);
            }
        }
        else
        {
            _stableTickCount = 0;
            _lastObservedY = currentY;
        }
    }

    private async Task SnapAfterSettleAsync(double scrollY)
    {
        var rowHeight = G9Metrics.DrumRowHeight;
        var idx = (int)Math.Round(scrollY / rowHeight);
        if (idx < 0 || idx >= _items.Count) return;

        var value = _items[idx].Value;
        var snapY = idx * rowHeight;

        // Selection is already correct from the live updates during scroll, but commit
        // the change event now so consumers who only care about the final value get one
        // SelectedValueChanged per gesture.
        if (SelectedValue != value)
        {
            SetSelected(value);
        }
        SelectedValueChanged?.Invoke(this, value);

        // If the scroll position is already exactly on a row boundary, no animation
        // needed — the user picked perfectly. Otherwise glide to the exact row using
        // the cancellable interpolated animation.
        if (Math.Abs(scrollY - snapY) > 0.5)
        {
            await AnimatedScrollAsync(snapY).ConfigureAwait(true);
        }
    }

    // ── Finger-down tracking via platform hooks ────────────────────────────────
    //
    // Why platform-specific. MAUI's PointerGestureRecognizer only fires for
    // mouse / stylus on mobile — finger touches are routed straight to the
    // ScrollView's internal scroll machinery and never reach the gesture
    // recognizer. To know when a finger is in CONTACT with the surface (as
    // opposed to "currently changing ScrollY") we hook the platform view's
    // native touch / pointer event after the handler attaches.
    //
    // Reentrancy & lifecycle: HandlerChanged fires once when the handler
    // attaches and again with PlatformView == null when it detaches. We always
    // detach previous listeners before attaching new ones to avoid duplicate
    // subscriptions across handler swaps (which can happen when the page is
    // reused via fast nav).

    private void OnScrollHandlerChanged(object? sender, EventArgs e)
    {
#if ANDROID
        AttachAndroidTouchListener();
#elif IOS || MACCATALYST
        AttachIosPanRecognizer();
#elif WINDOWS
        AttachWindowsPointerHandlers();
#endif
    }

    /// <summary>
    ///     Set finger-down state and react to the transitions:
    ///     <list type="bullet">
    ///         <item>Down → cancel any in-flight snap immediately so the user's
    ///             touch lands at the position they SAW under their finger, not
    ///             at wherever the snap had drifted to. Stop the settle timer
    ///             so it doesn't tick during the hold.</item>
    ///         <item>Up → record the gesture-start tick so the post-release
    ///             settle timer respects <see cref="MinSettleAgeMs" />, and
    ///             arm the timer so the platform's native fling can decelerate
    ///             before we snap.</item>
    ///     </list>
    /// </summary>
    private void SetFingerDown(bool isDown)
    {
        if (_isFingerDown == isDown) return;
        _isFingerDown = isDown;

        if (isDown)
        {
            // Touch lands. Kill the in-flight snap so the user's drag isn't
            // fighting an animation that's still writing positions.
            CancelSnap();
            _programmaticScroll = false;
            // Pause the settle detector — it would otherwise fire under the
            // finger if the user held still for ≥ 32 ms.
            StopSettleTimer();
        }
        else
        {
            // Touch lifts. Reset the gesture clock so MinSettleAgeMs measures
            // from "user just released" rather than "user first touched". Arm
            // the settle detector so the native fling can run to a stop and
            // we can snap to the final row.
            _lastGestureStartTick = Environment.TickCount;
            _stableTickCount = 0;
            _lastObservedY = _scroll.ScrollY;
            StartSettleTimer();
        }
    }

#if ANDROID
    /// <summary>
    ///     Cached delegate so detach finds the same instance that attach
    ///     subscribed. Lambdas captured per-call would create a fresh delegate
    ///     each time and the detach would no-op.
    /// </summary>
    private global::Android.Views.View.IOnTouchListener? _androidTouchListener;

    private global::Android.Views.View? _androidScrollPlatformView;

    private void AttachAndroidTouchListener()
    {
        // Detach previous listener (handler swap during page reuse).
        if (_androidScrollPlatformView is not null)
        {
            _androidScrollPlatformView.SetOnTouchListener(null);
            _androidScrollPlatformView = null;
        }

        if (_scroll.Handler?.PlatformView is not global::Android.Views.View view) return;
        _androidScrollPlatformView = view;
        _androidTouchListener ??= new AndroidScrollTouchListener(this);
        view.SetOnTouchListener(_androidTouchListener);
    }

    /// <summary>
    ///     Listener that observes ACTION_DOWN / ACTION_UP / ACTION_CANCEL on the
    ///     platform <c>NestedScrollView</c> WITHOUT consuming the event — every
    ///     callback returns false so the platform's own scroller still receives
    ///     the touch and handles dragging / flinging exactly as before. We only
    ///     read the events to track contact state.
    /// </summary>
    private sealed class AndroidScrollTouchListener : Java.Lang.Object, global::Android.Views.View.IOnTouchListener
    {
        private readonly WeakReference<G9DrumColumn> _ownerRef;

        public AndroidScrollTouchListener(G9DrumColumn owner)
        {
            _ownerRef = new WeakReference<G9DrumColumn>(owner);
        }

        public bool OnTouch(global::Android.Views.View? v, global::Android.Views.MotionEvent? e)
        {
            if (e is null) return false;
            if (!_ownerRef.TryGetTarget(out var owner)) return false;

            switch (e.ActionMasked)
            {
                case global::Android.Views.MotionEventActions.Down:
                case global::Android.Views.MotionEventActions.PointerDown:
                    owner.SetFingerDown(true);
                    break;
                case global::Android.Views.MotionEventActions.Move:
                    // Android dispatch quirk: when a touch lands on a child view that
                    // claims the gesture (e.g. a row cell with its own
                    // TapGestureRecognizer), ACTION_DOWN is delivered to the child and
                    // the parent ScrollView's OnTouchListener is NOT called for DOWN.
                    // The listener only starts firing once the parent's
                    // onInterceptTouchEvent takes over after the user drags past the
                    // touch slop. For slow drags that's the first event we see — so we
                    // treat MOVE as a "finger is in contact" signal too. Idempotent:
                    // SetFingerDown short-circuits when the state is already correct.
                    owner.SetFingerDown(true);
                    break;
                case global::Android.Views.MotionEventActions.Up:
                case global::Android.Views.MotionEventActions.Cancel:
                case global::Android.Views.MotionEventActions.PointerUp:
                    owner.SetFingerDown(false);
                    break;
            }
            // Always return false so the platform scroll handler still consumes
            // the event normally. We're a passive observer.
            return false;
        }
    }
#endif

#if IOS || MACCATALYST
    private global::UIKit.UIPanGestureRecognizer? _iosObserverPan;

    private void AttachIosPanRecognizer()
    {
        if (_scroll.Handler?.PlatformView is not global::UIKit.UIView view) return;

        if (_iosObserverPan is not null)
        {
            _iosObserverPan.View?.RemoveGestureRecognizer(_iosObserverPan);
            _iosObserverPan.Dispose();
            _iosObserverPan = null;
        }

        // Attach a passive UIPanGestureRecognizer that reports state but never
        // claims the gesture (CancelsTouchesInView=false). The native scroll
        // pan still drives scrolling; ours just observes the touch lifecycle.
        var pan = new global::UIKit.UIPanGestureRecognizer(OnIosPan)
        {
            CancelsTouchesInView = false,
            DelaysTouchesBegan = false,
            DelaysTouchesEnded = false
        };
        view.AddGestureRecognizer(pan);
        _iosObserverPan = pan;
    }

    private void OnIosPan(global::UIKit.UIPanGestureRecognizer pan)
    {
        switch (pan.State)
        {
            case global::UIKit.UIGestureRecognizerState.Began:
            case global::UIKit.UIGestureRecognizerState.Changed:
                // Same Android-style guard: when the touch starts on a child view that
                // claims it, we may not see Began on our observer. Changed is the
                // first state that reliably fires once the scroll pan engages.
                SetFingerDown(true);
                break;
            case global::UIKit.UIGestureRecognizerState.Ended:
            case global::UIKit.UIGestureRecognizerState.Cancelled:
            case global::UIKit.UIGestureRecognizerState.Failed:
                SetFingerDown(false);
                break;
        }
    }
#endif

#if WINDOWS
    private Microsoft.UI.Xaml.UIElement? _windowsScrollPlatformView;

    private void AttachWindowsPointerHandlers()
    {
        if (_windowsScrollPlatformView is not null)
        {
            _windowsScrollPlatformView.PointerPressed -= OnWindowsPointerPressed;
            _windowsScrollPlatformView.PointerReleased -= OnWindowsPointerReleased;
            _windowsScrollPlatformView.PointerCanceled -= OnWindowsPointerReleased;
            _windowsScrollPlatformView.PointerCaptureLost -= OnWindowsPointerReleased;
            _windowsScrollPlatformView = null;
        }

        if (_scroll.Handler?.PlatformView is not Microsoft.UI.Xaml.UIElement view) return;
        _windowsScrollPlatformView = view;
        view.PointerPressed += OnWindowsPointerPressed;
        view.PointerReleased += OnWindowsPointerReleased;
        view.PointerCanceled += OnWindowsPointerReleased;
        view.PointerCaptureLost += OnWindowsPointerReleased;
    }

    private void OnWindowsPointerPressed(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        SetFingerDown(true);
    }

    private void OnWindowsPointerReleased(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        SetFingerDown(false);
    }
#endif
}
