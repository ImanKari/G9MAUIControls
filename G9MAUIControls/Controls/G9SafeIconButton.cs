using G9MAUIControls.Controls;
using G9MAUIControls.Helpers;
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
public class G9SafeIconButton : G9IconButton, IG9SafeExecutionHost
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

    // Throttle / busy / CanExecute / Command→SafeCommand routing. Shared with G9SafeButton.
    private readonly G9SafeExecution _safe;

    public G9SafeIconButton()
    {
        _safe = new G9SafeExecution(this);

        // G9IconButton raises Clicked on tap — that's our entry point for safe execution. The
        // base class's own direct Command run is switched off below (ExecutesCommandOnTap), so a
        // plain Command="{Binding …}" goes through the safe runner exactly once.
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

        // Legacy routing: a plain Command / CommandParameter is run through the safe layer.
        // It is READ when needed, never copied into SafeCommand and never cleared, so the
        // consumer's bindings on both stay alive (see G9SafeExecution). The null-conditional
        // covers property writes made by the base constructor, before _safe exists.
        if (propertyName is nameof(Command) or nameof(CommandParameter))
        {
            _safe?.OnCommandSourceChanged();
        }
    }

    private static void OnSafeCommandPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is G9SafeIconButton button)
        {
            button._safe.OnCommandSourceChanged();
        }
    }

    private static void OnSafeCommandParameterPropertyChanged(BindableObject bindable, object? oldValue,
        object? newValue)
    {
        if (bindable is G9SafeIconButton button)
        {
            button._safe.OnCommandSourceChanged();
        }
    }

    /// <inheritdoc />
    private protected override bool IsInteractionBlocked => _safe.IsInteractionBlocked;

    /// <inheritdoc />
    /// <remarks>Always false: the command is run by the safe layer from <c>Clicked</c>, never directly.</remarks>
    private protected override bool ExecutesCommandOnTap => false;

    /// <inheritdoc />
    protected override void OnAttachedToLiveTree()
    {
        base.OnAttachedToLiveTree();
        _safe.Attach();
    }

    /// <inheritdoc />
    protected override void OnDetachedFromLiveTree()
    {
        _safe.Detach();
        base.OnDetachedFromLiveTree();
    }

    private async void OnG9SafeButtonClicked(object? sender, EventArgs e)
    {
        await _safe.HandleClickAsync();
    }

    /// <summary>
    ///     Toggles the base loading spinner and the disable-while-loading guard. Wired into
    ///     <see cref="G9SafeCommandOptions.SetBusy" /> so it runs on operation start/finish.
    ///     <para>
    ///         The guard is an internal flag — <see cref="VisualElement.IsEnabled" /> is never
    ///         written, so a consumer's <c>IsEnabled="{Binding …}"</c> keeps working.
    ///     </para>
    /// </summary>
    public void SetLoadingState(bool isLoading) => _safe.SetLoadingState(isLoading);

    string IG9SafeExecutionHost.SafeExecutionName => nameof(G9SafeIconButton);

    IReadOnlyList<Func<object?, Task>> IG9SafeExecutionHost.GetSafeClickedHandlers() =>
        SafeClickedAsync is null
            ? []
            : [.. SafeClickedAsync.GetInvocationList().Cast<Func<object?, Task>>()];

    void IG9SafeExecutionHost.OnInteractionBlockChanged() => RequestVisualUpdate();
}
