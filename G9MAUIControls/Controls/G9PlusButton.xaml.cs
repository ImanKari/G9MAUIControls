using Maui.BindableProperty.Generator.Core;
using System.Collections.ObjectModel;

namespace G9MAUIControls.Controls;

public partial class G9PlusButton : ContentView
{
    public G9PlusButton()
    {
        // Optimized default values for smooth mobile animation
        MainSizeCollapsed = 69;
        MainSizeExpanded = 49;
        ItemVerticalOffset = 55;
        TextBorderTargetWidth = 139;

        // Timing configuration
        ItemsStartDelayMs = 50;
        ItemStaggerMs = 40;
        Phase1AnimationDurationMs = 200;
        Phase2DelayDurationMs = 30;
        Phase2AnimationDurationMs = 180;
        Phase3DelayDurationMs = 20;
        Phase3AnimationDurationMs = 150;
        MainAnimationDurationMs = 180;
        MainIconRotationExpanded = 45;

        InitializeComponent();
        BindingContext = this;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        ResetToCollapsedState();
    }

    private void ResetToCollapsedState()
    {
        ItemsContainer.Opacity = 0;
        ItemsContainer.InputTransparent = true;
        RootLayout.HeightRequest = MainSizeCollapsed;
        RootLayout.WidthRequest = MainSizeCollapsed;
        MainIcon.Rotation = 0;
        _currentMainSize = MainSizeCollapsed;
        _currentMainRotation = 0;
        _isExpanded = false;
        UpdateHitAreaSize(false);

        foreach (var t in ItemsContainer.Children)
        {
            if (t is VisualElement ve)
            {
                ve.TranslationY = 0;
            }

            if (t is G9G9PlusButtonItemView itemView)
            {
                itemView.TextBorderPart.WidthRequest = 0;
                itemView.TextLabelPart.Opacity = 0;
            }
        }
    }

    private void ResetToExpandedState()
    {
        ItemsContainer.Opacity = 1;
        ItemsContainer.InputTransparent = false;
        RootLayout.HeightRequest = MainSizeExpanded;
        RootLayout.WidthRequest = MainSizeExpanded;
        MainIcon.Rotation = MainIconRotationExpanded;
        _currentMainSize = MainSizeExpanded;
        _currentMainRotation = MainIconRotationExpanded;
        _isExpanded = true;
        UpdateHitAreaSize(true);

        // Set all item views to expanded state
        for (var i = 0; i < ItemsContainer.Children.Count; i++)
        {
            if (ItemsContainer.Children[i] is VisualElement ve)
            {
                ve.TranslationY = -ItemVerticalOffset * (i + 1);
            }

            if (ItemsContainer.Children[i] is G9G9PlusButtonItemView itemView)
            {
                itemView.TextBorderPart.WidthRequest = TextBorderTargetWidth;
                itemView.TextLabelPart.Opacity = 1;
            }
        }
    }

    private void OnMainTapped(object? sender, TappedEventArgs e)
    {
        _ = ToggleAsync();
    }

    /// <summary>Toggles expanded/collapsed state; properly handles mid-animation cancellation.</summary>
    public async Task ToggleAsync()
    {
        // Cancel any running animation and wait for cleanup
        if (_isAnimating)
        {
            if (_animationCts != null)
            {
                await _animationCts.CancelAsync();
            }

            // Give a brief moment for animations to abort
            await Task.Delay(16);
        }

        // Abort all running animations on UI thread
        await Dispatcher.DispatchAsync(AbortAllAnimations);

        // Toggle target state
        var targetExpanded = !_isExpanded;
        _isExpanded = targetExpanded;

        _animationCts = new CancellationTokenSource();
        var token = _animationCts.Token;

        try
        {
            _isAnimating = true;

            if (targetExpanded)
            {
                await RunExpandSequenceAsync(token);
            }
            else
            {
                await RunCollapseSequenceAsync(token);
            }
        }
        catch (OperationCanceledException)
        {
            // Animation was cancelled, state will be set by next toggle
        }
        finally
        {
            _isAnimating = false;
        }
    }

    #region Main Button Animation

    private Task RunMainAnimationAsync(double targetSize, double targetRotation, CancellationToken token)
    {
        var tcs = new TaskCompletionSource<bool>();

        Dispatcher.Dispatch(() =>
        {
            if (token.IsCancellationRequested)
            {
                tcs.TrySetCanceled(token);
                return;
            }

            var startSize = _currentMainSize;
            var startRotation = _currentMainRotation;

            // Skip if already at target
            if (Math.Abs(startSize - targetSize) < 0.1 && Math.Abs(startRotation - targetRotation) < 0.1)
            {
                tcs.TrySetResult(true);
                return;
            }

            var anim = new Animation();

            anim.Add(0, 1, new Animation(v =>
            {
                _currentMainSize = v;
                RootLayout.HeightRequest = v;
                RootLayout.WidthRequest = v;
            }, startSize, targetSize, Easing.CubicOut));

            anim.Add(0, 1, new Animation(v =>
            {
                _currentMainRotation = v;
                MainIcon.Rotation = v;
            }, startRotation, targetRotation, Easing.CubicOut));

            anim.Commit(this, "MainButtonAnim", 16, MainAnimationDurationMs,
                finished: (_, cancelled) =>
                {
                    if (cancelled || token.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                    }
                    else
                    {
                        tcs.TrySetResult(true);
                    }
                });
        });

        return tcs.Task;
    }

    #endregion

    #region Animation settings (configurable)

    [AutoBindable(OnChanged = nameof(OnItemsChanged))]
    private ObservableCollection<G9PlusButtonItem>? _items;

    [AutoBindable] private double _mainSizeCollapsed;
    [AutoBindable] private double _mainSizeExpanded;
    [AutoBindable] private double _itemVerticalOffset;
    [AutoBindable] private double _textBorderTargetWidth;

    /// <summary>Delay (ms) between main button animation completing and first item starting. Default 50.</summary>
    [AutoBindable] private uint _itemsStartDelayMs;

    /// <summary>Delay (ms) before starting each subsequent item's animation (stagger). Default 40.</summary>
    [AutoBindable] private uint _itemStaggerMs;

    /// <summary>Duration (ms) for phase 1: icon slide to target position (TranslationY). Default 200.</summary>
    [AutoBindable] private uint _phase1AnimationDurationMs;

    /// <summary>Delay (ms) after phase 1 completes, before phase 2 (label width) starts. Default 30.</summary>
    [AutoBindable] private uint _phase2DelayDurationMs;

    /// <summary>Duration (ms) for phase 2: label area width 0→target. Default 180.</summary>
    [AutoBindable] private uint _phase2AnimationDurationMs;

    /// <summary>Delay (ms) after phase 2 completes, before phase 3 (label fade) starts. Default 20.</summary>
    [AutoBindable] private uint _phase3DelayDurationMs;

    /// <summary>Duration (ms) for phase 3: label fade-in/out. Default 150.</summary>
    [AutoBindable] private uint _phase3AnimationDurationMs;

    /// <summary>Duration (ms) for main button expand/collapse (size + rotation). Default 180.</summary>
    [AutoBindable] private uint _mainAnimationDurationMs;

    [AutoBindable] private double _mainIconRotationExpanded;

    [AutoBindable(OnChanged = nameof(OnMenuFlowDirectionChanged))]
    private FlowDirection _menuFlowDirection;

    private void OnMenuFlowDirectionChanged()
    {
        var direction = MenuFlowDirection;
        HitAreaLayout.FlowDirection = direction;
        RootLayout.FlowDirection = direction;
        foreach (var t in ItemsContainer.Children)
        {
            if (t is G9G9PlusButtonItemView itemView)
            {
                itemView.MenuFlowDirection = direction;
            }
        }
    }

    private void OnItemsChanged()
    {
        if (Items != null)
        {
            foreach (var item in Items)
            {
                item.CloseMenuAsync = CloseMenuFromItemSelectionAsync;
            }
        }

        BindableLayout.SetItemsSource(ItemsContainer, Items);
        OnMenuFlowDirectionChanged();
        UpdateHitAreaSize(_isExpanded);
    }

    private Task CloseMenuFromItemSelectionAsync()
    {
        if (!_isExpanded)
        {
            return Task.CompletedTask;
        }

        return ToggleAsync();
    }

    #endregion

    #region State and cancellation

    private bool _isExpanded;
    private bool _isAnimating;
    private CancellationTokenSource? _animationCts;

    // Track current animation state for smooth interruption
    private double _currentMainSize;
    private double _currentMainRotation;

    #endregion

    #region Expand Sequence

    private async Task RunExpandSequenceAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        // Prepare items container (visible but items at origin)
        await Dispatcher.DispatchAsync(() =>
        {
            ItemsContainer.Opacity = 1;
            ItemsContainer.InputTransparent = false;
            UpdateHitAreaSize(true);
        });

        await RunMainAnimationAsync(MainSizeExpanded, MainIconRotationExpanded, token);
        token.ThrowIfCancellationRequested();

        if (ItemsStartDelayMs > 0)
        {
            await Task.Delay((int)ItemsStartDelayMs, token);
        }

        token.ThrowIfCancellationRequested();

        var count = ItemsContainer.Children.Count;
        var itemTasks = new List<Task>();

        for (var i = 0; i < count; i++)
        {
            var index = i;
            var staggerDelay = (int)(index * ItemStaggerMs);

            itemTasks.Add(RunItemExpandSequenceAsync(index, staggerDelay, token));
        }

        await Task.WhenAll(itemTasks);
    }

    private async Task RunItemExpandSequenceAsync(int index, int initialDelay, CancellationToken token)
    {
        if (initialDelay > 0)
        {
            await Task.Delay(initialDelay, token);
        }

        token.ThrowIfCancellationRequested();

        await RunItemPhase1ExpandAsync(index, token);
        token.ThrowIfCancellationRequested();

        if (Phase2DelayDurationMs > 0)
        {
            await Task.Delay((int)Phase2DelayDurationMs, token);
        }

        token.ThrowIfCancellationRequested();

        await RunItemPhase2ExpandAsync(index, token);
        token.ThrowIfCancellationRequested();

        if (Phase3DelayDurationMs > 0)
        {
            await Task.Delay((int)Phase3DelayDurationMs, token);
        }

        token.ThrowIfCancellationRequested();

        await RunItemPhase3ExpandAsync(index, token);
    }

    #endregion

    #region Collapse Sequence

    private async Task RunCollapseSequenceAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();

        var count = ItemsContainer.Children.Count;

        var itemTasks = new List<Task>();

        for (var i = 0; i < count; i++)
        {
            var index = i;
            var staggerDelay = (int)((count - 1 - index) * ItemStaggerMs);

            itemTasks.Add(RunItemCollapseSequenceAsync(index, staggerDelay, token));
        }

        await Task.WhenAll(itemTasks);
        token.ThrowIfCancellationRequested();

        if (ItemsStartDelayMs > 0)
        {
            await Task.Delay((int)ItemsStartDelayMs, token);
        }

        token.ThrowIfCancellationRequested();

        await Dispatcher.DispatchAsync(() =>
        {
            ItemsContainer.Opacity = 0;
            ItemsContainer.InputTransparent = true;
            UpdateHitAreaSize(false);
        });

        token.ThrowIfCancellationRequested();

        await RunMainAnimationAsync(MainSizeCollapsed, 0, token);
        
    }

    private async Task RunItemCollapseSequenceAsync(int index, int initialDelay, CancellationToken token)
    {
        if (initialDelay > 0)
        {
            await Task.Delay(initialDelay, token);
        }

        token.ThrowIfCancellationRequested();

        await RunItemPhase3CollapseAsync(index, token);
        token.ThrowIfCancellationRequested();

        if (Phase3DelayDurationMs > 0)
        {
            await Task.Delay((int)Phase3DelayDurationMs, token);
        }

        token.ThrowIfCancellationRequested();

        await RunItemPhase2CollapseAsync(index, token);
        token.ThrowIfCancellationRequested();

        if (Phase2DelayDurationMs > 0)
        {
            await Task.Delay((int)Phase2DelayDurationMs, token);
        }

        token.ThrowIfCancellationRequested();

        await RunItemPhase1CollapseAsync(index, token);
    }

    #endregion

    #region Item Phase Animations

    private Task RunItemPhase1ExpandAsync(int index, CancellationToken token)
    {
        var tcs = new TaskCompletionSource<bool>();

        Dispatcher.Dispatch(() =>
        {
            if (token.IsCancellationRequested)
            {
                tcs.TrySetCanceled(token);
                return;
            }

            var child = ItemsContainer.Children[index];
            if (child is not VisualElement ve)
            {
                tcs.TrySetResult(true);
                return;
            }

            var startY = ve.TranslationY;
            var targetY = -ItemVerticalOffset * (index + 1);

            if (Math.Abs(startY - targetY) < 0.1)
            {
                tcs.TrySetResult(true);
                return;
            }

            var anim = new Animation(v => ve.TranslationY = v, startY, targetY, Easing.CubicOut);
            anim.Commit(this, $"ItemP1Exp_{index}", 16, Phase1AnimationDurationMs,
                finished: (_, cancelled) =>
                {
                    if (cancelled || token.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                    }
                    else
                    {
                        tcs.TrySetResult(true);
                    }
                });
        });

        return tcs.Task;
    }

    private Task RunItemPhase1CollapseAsync(int index, CancellationToken token)
    {
        var tcs = new TaskCompletionSource<bool>();

        Dispatcher.Dispatch(() =>
        {
            if (token.IsCancellationRequested)
            {
                tcs.TrySetCanceled(token);
                return;
            }

            var child = ItemsContainer.Children[index];
            if (child is not VisualElement ve)
            {
                tcs.TrySetResult(true);
                return;
            }

            var startY = ve.TranslationY;
            const double targetY = 0;

            if (Math.Abs(startY - targetY) < 0.1)
            {
                tcs.TrySetResult(true);
                return;
            }

            var anim = new Animation(v => ve.TranslationY = v, startY, targetY, Easing.CubicIn);
            anim.Commit(this, $"ItemP1Col_{index}", 16, Phase1AnimationDurationMs,
                finished: (_, cancelled) =>
                {
                    if (cancelled || token.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                    }
                    else
                    {
                        tcs.TrySetResult(true);
                    }
                });
        });

        return tcs.Task;
    }

    private Task RunItemPhase2ExpandAsync(int index, CancellationToken token)
    {
        var tcs = new TaskCompletionSource<bool>();

        Dispatcher.Dispatch(() =>
        {
            if (token.IsCancellationRequested)
            {
                tcs.TrySetCanceled(token);
                return;
            }

            if (ItemsContainer.Children[index] is not G9G9PlusButtonItemView itemView)
            {
                tcs.TrySetResult(true);
                return;
            }

            var startWidth = itemView.TextBorderPart.WidthRequest;
            var targetWidth = TextBorderTargetWidth;

            if (Math.Abs(startWidth - targetWidth) < 0.1)
            {
                tcs.TrySetResult(true);
                return;
            }

            var anim = new Animation(v => itemView.TextBorderPart.WidthRequest = v, startWidth, targetWidth,
                Easing.CubicOut);
            anim.Commit(this, $"ItemP2Exp_{index}", 16, Phase2AnimationDurationMs,
                finished: (_, cancelled) =>
                {
                    if (cancelled || token.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                    }
                    else
                    {
                        tcs.TrySetResult(true);
                    }
                });
        });

        return tcs.Task;
    }

    private Task RunItemPhase2CollapseAsync(int index, CancellationToken token)
    {
        var tcs = new TaskCompletionSource<bool>();

        Dispatcher.Dispatch(() =>
        {
            if (token.IsCancellationRequested)
            {
                tcs.TrySetCanceled(token);
                return;
            }

            if (ItemsContainer.Children[index] is not G9G9PlusButtonItemView itemView)
            {
                tcs.TrySetResult(true);
                return;
            }

            var startWidth = itemView.TextBorderPart.WidthRequest;
            const double targetWidth = 0;

            if (Math.Abs(startWidth - targetWidth) < 0.1)
            {
                tcs.TrySetResult(true);
                return;
            }

            var anim = new Animation(v => itemView.TextBorderPart.WidthRequest = v, startWidth, targetWidth,
                Easing.CubicIn);
            anim.Commit(this, $"ItemP2Col_{index}", 16, Phase2AnimationDurationMs,
                finished: (_, cancelled) =>
                {
                    if (cancelled || token.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                    }
                    else
                    {
                        tcs.TrySetResult(true);
                    }
                });
        });

        return tcs.Task;
    }

    private Task RunItemPhase3ExpandAsync(int index, CancellationToken token)
    {
        var tcs = new TaskCompletionSource<bool>();

        Dispatcher.Dispatch(() =>
        {
            if (token.IsCancellationRequested)
            {
                tcs.TrySetCanceled(token);
                return;
            }

            if (ItemsContainer.Children[index] is not G9G9PlusButtonItemView itemView)
            {
                tcs.TrySetResult(true);
                return;
            }

            var startOpacity = itemView.TextLabelPart.Opacity;
            const double targetOpacity = 1;

            if (Math.Abs(startOpacity - targetOpacity) < 0.01)
            {
                tcs.TrySetResult(true);
                return;
            }

            var anim = new Animation(v => itemView.TextLabelPart.Opacity = v, startOpacity, targetOpacity,
                Easing.CubicOut);
            anim.Commit(this, $"ItemP3Exp_{index}", 16, Phase3AnimationDurationMs,
                finished: (_, cancelled) =>
                {
                    if (cancelled || token.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                    }
                    else
                    {
                        tcs.TrySetResult(true);
                    }
                });
        });

        return tcs.Task;
    }

    private Task RunItemPhase3CollapseAsync(int index, CancellationToken token)
    {
        var tcs = new TaskCompletionSource<bool>();

        Dispatcher.Dispatch(() =>
        {
            if (token.IsCancellationRequested)
            {
                tcs.TrySetCanceled(token);
                return;
            }

            if (ItemsContainer.Children[index] is not G9G9PlusButtonItemView itemView)
            {
                tcs.TrySetResult(true);
                return;
            }

            var startOpacity = itemView.TextLabelPart.Opacity;
            const double targetOpacity = 0;

            if (Math.Abs(startOpacity - targetOpacity) < 0.01)
            {
                tcs.TrySetResult(true);
                return;
            }

            var anim = new Animation(v => itemView.TextLabelPart.Opacity = v, startOpacity, targetOpacity,
                Easing.CubicIn);
            anim.Commit(this, $"ItemP3Col_{index}", 16, Phase3AnimationDurationMs,
                finished: (_, cancelled) =>
                {
                    if (cancelled || token.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled();
                    }
                    else
                    {
                        tcs.TrySetResult(true);
                    }
                });
        });

        return tcs.Task;
    }

    #endregion

    #region Animation Utilities

    private void AbortAllAnimations()
    {
        this.AbortAnimation("MainButtonAnim");

        var n = ItemsContainer.Children.Count;
        for (var i = 0; i < n; i++)
        {
            this.AbortAnimation($"ItemP1Exp_{i}");
            this.AbortAnimation($"ItemP1Col_{i}");
            this.AbortAnimation($"ItemP2Exp_{i}");
            this.AbortAnimation($"ItemP2Col_{i}");
            this.AbortAnimation($"ItemP3Exp_{i}");
            this.AbortAnimation($"ItemP3Col_{i}");
        }
    }

    /// <summary>
    ///     Ensures translated menu items remain inside this control's hit-test area.
    /// </summary>
    private void UpdateHitAreaSize(bool expanded)
    {
        if (!expanded)
        {
            HitAreaLayout.HeightRequest = MainSizeCollapsed;
            HitAreaLayout.WidthRequest = MainSizeCollapsed;
            ItemsContainer.HeightRequest = MainSizeCollapsed;
            ItemsContainer.WidthRequest = MainSizeCollapsed;
            return;
        }

        var itemCount = ItemsContainer.Children.Count;
        if (itemCount <= 0)
        {
            HitAreaLayout.HeightRequest = MainSizeExpanded;
            HitAreaLayout.WidthRequest = MainSizeExpanded;
            ItemsContainer.HeightRequest = MainSizeExpanded;
            ItemsContainer.WidthRequest = MainSizeExpanded;
            return;
        }

        var expandedHeight = Math.Max(MainSizeExpanded, 49) + (ItemVerticalOffset * itemCount) + 8;
        var expandedWidth = Math.Max(MainSizeExpanded, TextBorderTargetWidth + MainSizeExpanded + 30);

        HitAreaLayout.HeightRequest = expandedHeight;
        HitAreaLayout.WidthRequest = expandedWidth;
        ItemsContainer.HeightRequest = expandedHeight;
        ItemsContainer.WidthRequest = expandedWidth;
    }

    #endregion
}
