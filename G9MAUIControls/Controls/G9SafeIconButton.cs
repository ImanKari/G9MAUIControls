using G9MAUIControls.Controls;
using G9MAUIControls.Helpers;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;

namespace G9MAUIControls.Controls;

/// <summary>
///     Icon-only button with the app-standard "safe execution" layer (throttle / double-tap
///     guard, busy spinner, automatic error popup, and <c>Command → SafeCommand</c>
///     auto-routing) layered over <see cref="G9IconButton" />.
///     <para>
///         Inherits the full <see cref="G9IconButton" /> icon / badge / variant / size API
///         directly (<c>Icon</c>, <c>ImageSource</c>, <c>IconSize</c>, <c>ButtonSize</c>,
///         <c>Variant</c>, <c>IsGhost</c>, <c>BadgeText</c>…). The busy spinner is driven by the
///         base <see cref="G9IconButton.IsLoading" /> property.
///     </para>
/// </summary>
public class G9SafeIconButton : G9IconButton
{
    public static readonly BindableProperty SafeCommandProperty =
        BindableProperty.Create(
            nameof(SafeCommand),
            typeof(ICommand),
            typeof(G9SafeIconButton),
            propertyChanged: OnSafeCommandPropertyChanged);

    public static readonly BindableProperty SafeCommandParameterProperty =
        BindableProperty.Create(
            nameof(SafeCommandParameter),
            typeof(object),
            typeof(G9SafeIconButton),
            propertyChanged: OnSafeCommandParameterPropertyChanged);

    public static readonly BindableProperty EnableSafeExecutionProperty =
        BindableProperty.Create(
            nameof(EnableSafeExecution),
            typeof(bool),
            typeof(G9SafeIconButton),
            true);

    public static readonly BindableProperty DisableWhileLoadingProperty =
        BindableProperty.Create(
            nameof(DisableWhileLoading),
            typeof(bool),
            typeof(G9SafeIconButton),
            true);

    public static readonly BindableProperty ShowSpinnerWhileLoadingProperty =
        BindableProperty.Create(
            nameof(ShowSpinnerWhileLoading),
            typeof(bool),
            typeof(G9SafeIconButton),
            true);

    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(
            nameof(Source),
            typeof(string),
            typeof(G9SafeIconButton));

    public static readonly BindableProperty ErrorMessageProperty =
        BindableProperty.Create(
            nameof(ErrorMessage),
            typeof(string),
            typeof(G9SafeIconButton));

    public static readonly BindableProperty ErrorTitleProperty =
        BindableProperty.Create(
            nameof(ErrorTitle),
            typeof(string),
            typeof(G9SafeIconButton));

    public static readonly BindableProperty ShowErrorG9PopupProperty =
        BindableProperty.Create(
            nameof(ShowErrorG9Popup),
            typeof(bool),
            typeof(G9SafeIconButton),
            true);

    public static readonly BindableProperty EnableThrottleProperty =
        BindableProperty.Create(
            nameof(EnableThrottle),
            typeof(bool),
            typeof(G9SafeIconButton),
            true);

    public static readonly BindableProperty ThrottleKeyProperty =
        BindableProperty.Create(
            nameof(ThrottleKey),
            typeof(string),
            typeof(G9SafeIconButton));

    public static readonly BindableProperty ThrottleIntervalProperty =
        BindableProperty.Create(
            nameof(ThrottleInterval),
            typeof(TimeSpan),
            typeof(G9SafeIconButton),
            TimeSpan.FromMilliseconds(369));

    public static readonly BindableProperty BusyDelayProperty =
        BindableProperty.Create(
            nameof(BusyDelay),
            typeof(TimeSpan),
            typeof(G9SafeIconButton),
            TimeSpan.Zero);

    private bool _isExecuting;
    private bool _isRoutingLegacyCommand;
    private bool _wasEnabledBeforeExecution = true;

    public G9SafeIconButton()
    {
        // G9IconButton raises Clicked on tap (before running its own Command, which we
        // null out via legacy routing) — that's our entry point for safe execution.
        Clicked += OnG9SafeButtonClicked;
    }

    public Func<object?, Task>? SafeClickedCallbackAsync { get; set; }

    public ICommand? SafeCommand
    {
        get => (ICommand?)GetValue(SafeCommandProperty);
        set => SetValue(SafeCommandProperty, value);
    }

    public object? SafeCommandParameter
    {
        get => GetValue(SafeCommandParameterProperty);
        set => SetValue(SafeCommandParameterProperty, value);
    }

    public bool EnableSafeExecution
    {
        get => (bool)GetValue(EnableSafeExecutionProperty);
        set => SetValue(EnableSafeExecutionProperty, value);
    }

    public bool DisableWhileLoading
    {
        get => (bool)GetValue(DisableWhileLoadingProperty);
        set => SetValue(DisableWhileLoadingProperty, value);
    }

    /// <summary>
    ///     Kept for call-site compatibility. The <see cref="G9IconButton" /> built-in spinner
    ///     is driven by <see cref="G9IconButton.IsLoading" />; when this is false the loading
    ///     spinner is suppressed (the disable-while-loading guard still applies).
    /// </summary>
    public bool ShowSpinnerWhileLoading
    {
        get => (bool)GetValue(ShowSpinnerWhileLoadingProperty);
        set => SetValue(ShowSpinnerWhileLoadingProperty, value);
    }

    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public string? ErrorMessage
    {
        get => (string?)GetValue(ErrorMessageProperty);
        set => SetValue(ErrorMessageProperty, value);
    }

    public string? ErrorTitle
    {
        get => (string?)GetValue(ErrorTitleProperty);
        set => SetValue(ErrorTitleProperty, value);
    }

    public bool ShowErrorG9Popup
    {
        get => (bool)GetValue(ShowErrorG9PopupProperty);
        set => SetValue(ShowErrorG9PopupProperty, value);
    }

    public bool EnableThrottle
    {
        get => (bool)GetValue(EnableThrottleProperty);
        set => SetValue(EnableThrottleProperty, value);
    }

    public string? ThrottleKey
    {
        get => (string?)GetValue(ThrottleKeyProperty);
        set => SetValue(ThrottleKeyProperty, value);
    }

    public TimeSpan ThrottleInterval
    {
        get => (TimeSpan)GetValue(ThrottleIntervalProperty);
        set => SetValue(ThrottleIntervalProperty, value);
    }

    public TimeSpan BusyDelay
    {
        get => (TimeSpan)GetValue(BusyDelayProperty);
        set => SetValue(BusyDelayProperty, value);
    }

    public event Func<object?, Task>? SafeClickedAsync;

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName ?? string.Empty);

        if (_isRoutingLegacyCommand)
        {
            return;
        }

        if (propertyName == nameof(Command))
        {
            RouteLegacyCommandBinding();
        }
        else if (propertyName == nameof(CommandParameter))
        {
            RouteLegacyCommandParameter();
        }
    }

    private static void OnSafeCommandPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is not G9SafeIconButton button)
        {
            return;
        }

        if (oldValue is ICommand oldCommand)
        {
            oldCommand.CanExecuteChanged -= button.OnBoundCommandCanExecuteChanged;
        }

        if (newValue is ICommand newCommand)
        {
            newCommand.CanExecuteChanged += button.OnBoundCommandCanExecuteChanged;
        }

        button.UpdateIsEnabledFromCommand();
    }

    private static void OnSafeCommandParameterPropertyChanged(BindableObject bindable, object? oldValue,
        object? newValue)
    {
        if (bindable is G9SafeIconButton button)
        {
            button.UpdateIsEnabledFromCommand();
        }
    }

    private void OnBoundCommandCanExecuteChanged(object? sender, EventArgs e)
    {
        UpdateIsEnabledFromCommand();
    }

    private void UpdateIsEnabledFromCommand()
    {
        if (SafeCommand is null)
        {
            return;
        }

        if (_isExecuting && DisableWhileLoading)
        {
            return;
        }

        if (IsLoading && DisableWhileLoading)
        {
            return;
        }

        var canRun = SafeCommand.CanExecute(SafeCommandParameter);
        if (IsEnabled != canRun)
        {
            IsEnabled = canRun;
        }
    }

    private void RouteLegacyCommandBinding()
    {
        if (Command is null)
        {
            return;
        }

        _isRoutingLegacyCommand = true;
        try
        {
            if (SafeCommand is null)
            {
                SafeCommand = Command;
            }

            if (SafeCommandParameter is null && CommandParameter is not null)
            {
                SafeCommandParameter = CommandParameter;
            }

            Command = null!;
            CommandParameter = null!;
        }
        finally
        {
            _isRoutingLegacyCommand = false;
        }
    }

    private void RouteLegacyCommandParameter()
    {
        if (CommandParameter is null || SafeCommandParameter is not null)
        {
            return;
        }

        _isRoutingLegacyCommand = true;
        try
        {
            SafeCommandParameter = CommandParameter;
            CommandParameter = null!;
        }
        finally
        {
            _isRoutingLegacyCommand = false;
        }
    }

    private async void OnG9SafeButtonClicked(object? sender, EventArgs e)
    {
        if (!EnableSafeExecution || _isExecuting)
        {
            return;
        }

        if (SafeCommand is null && SafeClickedCallbackAsync is null && SafeClickedAsync is null)
        {
            return;
        }

        _isExecuting = true;

        try
        {
            await G9SafeCommand.RunAsync(
                _ => ExecuteSafeActionAsync(),
                BuildSafeCommandOptions());
        }
        finally
        {
            _isExecuting = false;
            UpdateIsEnabledFromCommand();
        }
    }

    private G9SafeCommandOptions BuildSafeCommandOptions()
    {
        return new G9SafeCommandOptions
        {
            Source = Source ?? nameof(G9SafeIconButton),
            ErrorMessage = ErrorMessage,
            ErrorTitle = ErrorTitle,
            ShowErrorG9Popup = ShowErrorG9Popup,
            EnableThrottle = EnableThrottle,
            ThrottleKey = string.IsNullOrWhiteSpace(ThrottleKey)
                ? $"{nameof(G9SafeIconButton)}:{GetHashCode()}"
                : ThrottleKey,
            ThrottleInterval = ThrottleInterval,
            BusyDelay = BusyDelay,
            SetBusy = SetLoadingState
        };
    }

    private async Task ExecuteSafeActionAsync()
    {
        var parameter = SafeCommandParameter;

        if (SafeClickedCallbackAsync is not null)
        {
            await SafeClickedCallbackAsync(parameter);
        }

        if (SafeClickedAsync is not null)
        {
            foreach (var handler in SafeClickedAsync.GetInvocationList().Cast<Func<object?, Task>>())
            {
                await handler(parameter);
            }
        }

        if (SafeCommand is null || !SafeCommand.CanExecute(parameter))
        {
            return;
        }

        if (SafeCommand is IAsyncRelayCommand asyncRelayCommand)
        {
            await asyncRelayCommand.ExecuteAsync(parameter);
            return;
        }

        SafeCommand.Execute(parameter);
    }

    /// <summary>
    ///     Toggles the base loading spinner and the disable-while-loading guard. Wired into
    ///     <see cref="G9SafeCommandOptions.SetBusy" /> so it runs on operation start/finish.
    /// </summary>
    public void SetLoadingState(bool isLoading)
    {
        if (ShowSpinnerWhileLoading)
        {
            IsLoading = isLoading;
        }

        if (!DisableWhileLoading)
        {
            return;
        }

        if (isLoading)
        {
            _wasEnabledBeforeExecution = IsEnabled;
            IsEnabled = false;
        }
        else
        {
            IsEnabled = _wasEnabledBeforeExecution;
        }
    }
}
