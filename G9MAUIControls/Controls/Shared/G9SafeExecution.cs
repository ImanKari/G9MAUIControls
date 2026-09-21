using CommunityToolkit.Mvvm.Input;
using G9MAUIControls.Helpers;
using System.Windows.Input;

namespace G9MAUIControls.Controls;

/// <summary>
///     What <see cref="G9SafeExecution" /> needs from the button it serves. Every member except
///     the last three is satisfied by the public properties <see cref="G9SafeButton" /> and
///     <see cref="G9SafeIconButton" /> already expose, so implementing it adds nothing to either
///     class's public surface.
/// </summary>
internal interface IG9SafeExecutionHost
{
    ICommand? Command { get; }
    object? CommandParameter { get; }
    ICommand? SafeCommand { get; }
    object? SafeCommandParameter { get; }
    bool EnableSafeExecution { get; }
    bool DisableWhileLoading { get; }
    bool ShowSpinnerWhileLoading { get; }
    bool IsLoading { get; set; }
    string? Source { get; }
    string? ErrorMessage { get; }
    string? ErrorTitle { get; }
    bool ShowErrorG9Popup { get; }
    bool EnableThrottle { get; }
    string? ThrottleKey { get; }
    TimeSpan ThrottleInterval { get; }
    TimeSpan BusyDelay { get; }
    Func<object?, Task>? SafeClickedCallbackAsync { get; }

    /// <summary>Type name used for the default diagnostics source and throttle key.</summary>
    string SafeExecutionName { get; }

    /// <summary>Snapshot of the <c>SafeClickedAsync</c> subscribers (an event cannot be read from outside its class).</summary>
    IReadOnlyList<Func<object?, Task>> GetSafeClickedHandlers();

    /// <summary>The blocked state changed — repaint so the button dims / un-dims.</summary>
    void OnInteractionBlockChanged();
}

/// <summary>
///     The "safe execution" layer shared by <see cref="G9SafeButton" /> and
///     <see cref="G9SafeIconButton" />. The two were line-for-line twins, so every defect existed
///     twice and every fix had to be applied twice; this is the single copy.
///     <para>
///         <b>It never writes a property the consumer can bind.</b> The previous implementation
///         did two things that fought the consumer's XAML:
///     </para>
///     <list type="bullet">
///         <item>
///             It copied <c>Command</c> / <c>CommandParameter</c> into the <c>Safe*</c> twins ONCE
///             and then nulled the originals. A local value outranks — and removes — a one-way
///             binding, so in a recycled <c>CollectionView</c> cell with
///             <c>CommandParameter="{Binding .}"</c> the binding was gone after the first row and a
///             tap on row B ran with row A's item. Now the effective command and parameter are
///             <em>read</em> at the moment they are needed (<see cref="EffectiveCommand" />,
///             <see cref="EffectiveParameter" />) and nothing is copied or cleared.
///         </item>
///         <item>
///             It wrote <c>IsEnabled</c> for both the busy guard and the command's
///             <c>CanExecute</c>, which replaced a consumer's <c>IsEnabled="{Binding CanSave}"</c>;
///             after the first click the button no longer followed the view model. Both are now
///             private flags surfaced through <see cref="IsInteractionBlocked" />, which the base
///             button consults for its tap guard and its disabled look.
///         </item>
///     </list>
/// </summary>
internal sealed class G9SafeExecution(IG9SafeExecutionHost host)
{
    private ICommand? _subscribedCommand;
    private bool _attached;
    private bool _isExecuting;
    private bool _busyBlocked;
    private bool _canExecute = true;

    /// <summary>
    ///     True while the button must ignore taps and look disabled for a reason of its OWN — it is
    ///     busy, or its command reports <c>CanExecute == false</c>. Independent of, and combined
    ///     with, the consumer-owned <c>IsEnabled</c>.
    /// </summary>
    public bool IsInteractionBlocked => _busyBlocked || !_canExecute;

    /// <summary>An explicit <c>SafeCommand</c> wins; otherwise the plain <c>Command</c> is run safely.</summary>
    public ICommand? EffectiveCommand => host.SafeCommand ?? host.Command;

    /// <summary>Same precedence as <see cref="EffectiveCommand" />, resolved independently so the two can be mixed.</summary>
    public object? EffectiveParameter => host.SafeCommandParameter ?? host.CommandParameter;

    /// <summary>Call when any of <c>Command</c>, <c>SafeCommand</c> or their parameters changed.</summary>
    public void OnCommandSourceChanged()
    {
        SyncSubscription();
        RefreshCanExecute();
    }

    /// <summary>
    ///     The button became live. <c>CanExecuteChanged</c> is only observed from here until
    ///     <see cref="Detach" />: a view-model command usually outlives the page, and a permanent
    ///     subscription from it kept the button — and through it the whole page — reachable after
    ///     the page was popped.
    /// </summary>
    public void Attach()
    {
        _attached = true;
        SyncSubscription();
        // The command may have changed its mind while nobody was listening.
        RefreshCanExecute();
    }

    public void Detach()
    {
        _attached = false;
        SyncSubscription();
    }

    /// <summary>Entry point for the button's <c>Clicked</c> event.</summary>
    public async Task HandleClickAsync()
    {
        if (!host.EnableSafeExecution || _isExecuting)
        {
            return;
        }

        if (EffectiveCommand is null
            && host.SafeClickedCallbackAsync is null
            && host.GetSafeClickedHandlers().Count == 0)
        {
            return;
        }

        _isExecuting = true;

        try
        {
            await G9SafeCommand.RunAsync(
                _ => ExecuteSafeActionAsync(),
                BuildOptions());
        }
        catch (Exception ex)
        {
            // RunAsync does not throw by contract, but this is awaited from an async-void event
            // handler — anything that did escape would take the process down.
            G9Press.ReportFailure(host, ex);
        }
        finally
        {
            _isExecuting = false;
            RefreshCanExecute();
        }
    }

    /// <summary>
    ///     Toggles the base loading spinner and the disable-while-loading guard. Wired into
    ///     <see cref="G9SafeCommandOptions.SetBusy" /> so it runs on operation start/finish.
    /// </summary>
    public void SetLoadingState(bool isLoading)
    {
        if (host.ShowSpinnerWhileLoading)
        {
            host.IsLoading = isLoading;
        }

        SetBusyBlocked(isLoading && host.DisableWhileLoading);
    }

    private void SetBusyBlocked(bool value)
    {
        if (_busyBlocked == value) return;
        _busyBlocked = value;
        host.OnInteractionBlockChanged();
    }

    private void SyncSubscription()
    {
        var target = _attached ? EffectiveCommand : null;
        if (ReferenceEquals(target, _subscribedCommand)) return;

        if (_subscribedCommand is not null)
        {
            _subscribedCommand.CanExecuteChanged -= OnCanExecuteChanged;
        }

        _subscribedCommand = target;

        if (_subscribedCommand is not null)
        {
            _subscribedCommand.CanExecuteChanged += OnCanExecuteChanged;
        }
    }

    private void OnCanExecuteChanged(object? sender, EventArgs e)
    {
        // Commands raise this from whatever thread changed their state (a background save
        // completing, a sync tick). The refresh ends in a repaint, which must be on the UI thread.
        if (MainThread.IsMainThread)
        {
            RefreshCanExecute();
            return;
        }

        MainThread.BeginInvokeOnMainThread(RefreshCanExecute);
    }

    private void RefreshCanExecute()
    {
        bool canExecute;
        try
        {
            // No command → nothing to veto the press (click callbacks may still be attached).
            canExecute = EffectiveCommand?.CanExecute(EffectiveParameter) ?? true;
        }
        catch (Exception ex)
        {
            // A CanExecute that throws (a parameter of the wrong type mid-rebind) must not break
            // the binding pass that triggered this. Leave the button pressable; the safe runner
            // re-checks and reports when it is actually pressed.
            G9Press.ReportFailure(host, ex);
            canExecute = true;
        }

        if (_canExecute == canExecute) return;
        _canExecute = canExecute;
        host.OnInteractionBlockChanged();
    }

    private G9SafeCommandOptions BuildOptions()
    {
        return new G9SafeCommandOptions
        {
            Source = host.Source ?? host.SafeExecutionName,
            ErrorMessage = host.ErrorMessage,
            ErrorTitle = host.ErrorTitle,
            ShowErrorG9Popup = host.ShowErrorG9Popup,
            EnableThrottle = host.EnableThrottle,
            ThrottleKey = string.IsNullOrWhiteSpace(host.ThrottleKey)
                ? $"{host.SafeExecutionName}:{host.GetHashCode()}"
                : host.ThrottleKey,
            ThrottleInterval = host.ThrottleInterval,
            BusyDelay = host.BusyDelay,
            SetBusy = SetLoadingState
        };
    }

    private async Task ExecuteSafeActionAsync()
    {
        // Resolved HERE, at press time, never cached: this is what makes a recycled cell run
        // with the row it currently shows.
        var command = EffectiveCommand;
        var parameter = EffectiveParameter;

        if (host.SafeClickedCallbackAsync is { } callback)
        {
            await callback(parameter);
        }

        foreach (var handler in host.GetSafeClickedHandlers())
        {
            await handler(parameter);
        }

        if (command is null || !command.CanExecute(parameter))
        {
            return;
        }

        if (command is IAsyncRelayCommand asyncRelayCommand)
        {
            await asyncRelayCommand.ExecuteAsync(parameter);
            return;
        }

        command.Execute(parameter);
    }
}
