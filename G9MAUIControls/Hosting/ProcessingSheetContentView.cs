using G9MAUIControls.BottomSheet;
using G9MAUIControls.Controls;
using G9MAUIControls.Popup;
using G9MAUIControls.Theming;
using G9MAUIControls.Localization;

namespace G9MAUIControls.Hosting;

/// <summary>
///     A generic "open spinner instantly, then build + process, then swap in the real content"
///     bottom-sheet body. Used by <c>G9BottomSheetHelper.ShowProcessingBottomSheet</c>.
///     <para>
///         The sheet opens immediately showing only a centered spinner (zero work on the tap).
///         After the open animation completes, <see cref="G9BottomSheetHelper" /> drives the deferred
///         load (see <see cref="IDeferredSheetLoad" /> via <see cref="LoadableSheetContentView" />),
///         which runs the caller's <c>buildAsync</c> callback off the critical open frame. On
///         success the built view replaces the spinner; on failure the <c>onError</c> callback runs
///         (default: show an error popup and close the sheet).
///     </para>
///     <para>
///         <b>Threading contract:</b> <c>buildAsync</c> may do async data work, but the returned
///         <see cref="View" /> MUST be constructed on the UI thread (MAUI view construction is not
///         thread-safe). The idiomatic shape is:
///         <c>var data = await svc.LoadAsync(ct).ConfigureAwait(false); return await
///         MainThread.InvokeOnMainThreadAsync(() =&gt; new MyView(data));</c>
///     </para>
///     <para>
///         Fit-to-content sizing: the sheet opens at <c>loadingHeight</c> (the spinner) and grows
///         to the built content's height once it is swapped in. If the built view itself implements
///         <see cref="IG9BottomSheetContentHeightProvider" /> (e.g. a tabbed/list body), its height
///         changes are forwarded so the sheet keeps resizing as the user interacts with it.
///     </para>
/// </summary>
public sealed class ProcessingSheetContentView : LoadableSheetContentView, IG9BottomSheetContentHeightProvider
{
    private const double DefaultLoadingHeight = 160;

    private readonly Func<IG9BottomSheetHandle, CancellationToken, Task<View?>> _buildAsync;
    private readonly Func<Exception, IG9BottomSheetHandle, Task>? _onError;
    private readonly double _loadingHeight;
    private readonly Grid _root;
    private readonly View _loadingView;

    private View? _built;
    private IG9BottomSheetContentHeightProvider? _builtHeightProvider;

    /// <param name="buildAsync">
    ///     Async builder run after the sheet is visible. Returns the real content view (constructed
    ///     on the UI thread — see the type-level threading contract). Returning <c>null</c> closes
    ///     the sheet quietly (e.g. the target disappeared).
    /// </param>
    /// <param name="onError">
    ///     Invoked when <paramref name="buildAsync" /> throws (other than cancellation). When
    ///     <c>null</c>, the default behavior shows an error popup and closes the sheet.
    /// </param>
    /// <param name="loadingHeight">Spinner placeholder height for fit-to-content sheets.</param>
    public ProcessingSheetContentView(
        Func<IG9BottomSheetHandle, CancellationToken, Task<View?>> buildAsync,
        Func<Exception, IG9BottomSheetHandle, Task>? onError = null,
        double loadingHeight = DefaultLoadingHeight)
    {
        _buildAsync = buildAsync ?? throw new ArgumentNullException(nameof(buildAsync));
        _onError = onError;
        _loadingHeight = loadingHeight > 0 ? loadingHeight : DefaultLoadingHeight;

        _loadingView = new ContentView
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            HeightRequest = _loadingHeight,
            BackgroundColor = G9Palette.Current.Background,
            Content = new G9ActivityIndicator
            {
                IsRunning = true,
                Color = G9Palette.Current.Primary,
                HeightRequest = 42,
                WidthRequest = 42,
                HorizontalOptions = LayoutOptions.Center,
                VerticalOptions = LayoutOptions.Center
            }
        };
        // A property-changed handler rather than a binding. `new Binding("IsLoading")` is a string path,
        // which MAUI marks [RequiresUnreferencedCode] — it resolves the property by reflection, so a full
        // trim can remove the getter and the binding silently stops updating (IL2026, found by the
        // gallery's `AndroidLinkMode=Full` publish; see ADR-0011). This is the whole binding: one bool, one
        // target, on self. There is nothing a binding buys here that costs less than trim-unsafety.
        _loadingView.IsVisible = IsLoading;
        PropertyChanged += OnOwnPropertyChanged;

        _root = new Grid
        {
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill
        };
        _root.Add(_loadingView);

        Content = _root;
    }

    /// <inheritdoc />
    public event EventHandler? G9BottomSheetContentHeightChanged;

    private void OnOwnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == IsLoadingProperty.PropertyName)
        {
            _loadingView.IsVisible = IsLoading;
        }
    }

    /// <inheritdoc />
    public double GetDesiredG9BottomSheetContentHeight(double availableWidth, double maxHeight)
    {
        if (_built is null || IsLoading)
        {
            return Math.Min(_loadingHeight, maxHeight);
        }

        if (_builtHeightProvider is not null)
        {
            return _builtHeightProvider.GetDesiredG9BottomSheetContentHeight(availableWidth, maxHeight);
        }

        var measured = ((Microsoft.Maui.IView)_built).Measure(availableWidth, double.PositiveInfinity).Height;
        return double.IsNaN(measured) || measured <= 0
            ? Math.Min(_loadingHeight, maxHeight)
            : measured;
    }

    /// <inheritdoc />
    protected override async Task RunDeferredLoadAsync(CancellationToken cancellationToken)
    {

        View? built;
        try
        {
            built = await _buildAsync(G9BottomSheetHandle, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            await HandleBuildErrorAsync(ex).ConfigureAwait(false);
            return;
        }


        if (IsClosed || cancellationToken.IsCancellationRequested)
        {
            return;
        }

        if (built is null)
        {
            // Nothing to show (target gone / no data) — close quietly rather than leave a spinner.
            await CloseSelfAsync().ConfigureAwait(false);
            return;
        }

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (IsClosed || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            AttachBuilt(built);
            IsLoading = false;
            G9BottomSheetContentHeightChanged?.Invoke(this, EventArgs.Empty);
        }).ConfigureAwait(false);
    }

    /// <inheritdoc />
    protected override void OnClosed()
    {
        if (_builtHeightProvider is not null)
        {
            _builtHeightProvider.G9BottomSheetContentHeightChanged -= OnBuiltHeightChanged;
            _builtHeightProvider = null;
        }

        // Forward the close to a built body that follows the same loadable pattern, so its own
        // in-flight load / subscriptions are torn down. Non-loadable bodies (e.g. plain content
        // views with their own MarkClosed) are handled by the caller's ClosedCommand.
        if (_built is LoadableSheetContentView loadableBuilt)
        {
            loadableBuilt.MarkClosed();
        }

        base.OnClosed();
    }

    private void AttachBuilt(View built)
    {
        _built = built;

        // Forward THIS sheet's scoped handle to the built body. The built view is our CHILD, so the
        // helper only injected the handle into US (the ProcessingSheetContentView), never into it —
        // it keeps its default handle whose owner is null, so a self-close from inside it (e.g. a
        // header X calling G9BottomSheetHandle.Close()) falls back to CloseBottomSheet() and tears down
        // the PRIMARY sheet (the whole flow) instead of just this stacked one. Handing it our
        // sheet-scoped handle makes its Close() close exactly this sheet.
        if (built is IG9BottomSheetAwareView awareBuilt)
        {
            awareBuilt.G9BottomSheetHandle = G9BottomSheetHandle;
        }

        if (_loadingView.Parent is not null)
        {
            _root.Remove(_loadingView);
        }

        _root.Add(built);

        if (built is IG9BottomSheetContentHeightProvider heightProvider)
        {
            _builtHeightProvider = heightProvider;
            heightProvider.G9BottomSheetContentHeightChanged += OnBuiltHeightChanged;
        }
    }

    private void OnBuiltHeightChanged(object? sender, EventArgs e)
    {
        G9BottomSheetContentHeightChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task HandleBuildErrorAsync(Exception ex)
    {
        var handle = G9BottomSheetHandle;

        if (_onError is not null)
        {
            try
            {
                await _onError(ex, handle).ConfigureAwait(false);
            }
            catch
            {
                // Never let a secondary failure in the error handler escape the deferred-load path.
            }

            return;
        }

        // Default: show an error popup, then close the sheet.
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await G9PopupHelper.ShowG9PopupAsync(G9Strings.Get(G9StringKey.UnexpectedError), G9Strings.Get(G9StringKey.Error));
            }
            catch
            {
                // ignore popup failure
            }

            try
            {
                await handle.CloseAsync();
            }
            catch
            {
                // ignore close failure
            }
        }).ConfigureAwait(false);
    }

    private Task CloseSelfAsync()
    {
        var handle = G9BottomSheetHandle;
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                await handle.CloseAsync();
            }
            catch
            {
                // ignore close failure
            }
        });
    }
}
