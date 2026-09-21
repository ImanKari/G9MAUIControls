using G9MAUIControls.Controls;
using G9MAUIControls.Helpers;
using System.Windows.Input;

namespace G9MAUIControls.Controls;

/// <summary>
///     Text button with the app-standard "safe execution" layer (throttle / double-tap
///     guard, busy spinner, automatic error popup, and <c>Command → SafeCommand</c>
///     auto-routing) layered over <see cref="G9Button" />.
///     <para>
///         Shape / padding / typeface now normalize to the G9 design-system defaults —
///         the legacy <c>CustomizedButton</c> visual machinery (manual border, VSM,
///         ContentTemplate, icon-image building) has been removed. Use <see cref="ButtonType" />
///         (mapped to <see cref="G9Button.Variant" />) for named looks, or the
///         <see cref="G9Button.BaseBackgroundColor" /> / <see cref="G9Button.TextColor" />
///         escape hatches for arbitrary colors. Icon-only buttons should use
///         <see cref="G9SafeIconButton" /> instead.
///     </para>
/// </summary>
public class G9SafeButton : G9Button, IG9SafeExecutionHost
{
    public static readonly BindableProperty ButtonTypeProperty =
        BindableProperty.Create(
            nameof(ButtonType),
            typeof(G9SafeButtonType),
            typeof(G9SafeButton),
            G9SafeButtonType.Default,
            propertyChanged: OnButtonTypeChanged);

    public static readonly BindableProperty SafeCommandProperty =
        BindableProperty.Create(
            nameof(SafeCommand),
            typeof(ICommand),
            typeof(G9SafeButton),
            propertyChanged: OnSafeCommandPropertyChanged);

    public static readonly BindableProperty SafeCommandParameterProperty =
        BindableProperty.Create(
            nameof(SafeCommandParameter),
            typeof(object),
            typeof(G9SafeButton),
            propertyChanged: OnSafeCommandParameterPropertyChanged);

    public static readonly BindableProperty EnableSafeExecutionProperty =
        BindableProperty.Create(
            nameof(EnableSafeExecution),
            typeof(bool),
            typeof(G9SafeButton),
            true);

    public static readonly BindableProperty DisableWhileLoadingProperty =
        BindableProperty.Create(
            nameof(DisableWhileLoading),
            typeof(bool),
            typeof(G9SafeButton),
            true);

    public static readonly BindableProperty ShowSpinnerWhileLoadingProperty =
        BindableProperty.Create(
            nameof(ShowSpinnerWhileLoading),
            typeof(bool),
            typeof(G9SafeButton),
            true);

    public static readonly BindableProperty SpinnerColorProperty =
        BindableProperty.Create(
            nameof(SpinnerColor),
            typeof(Color),
            typeof(G9SafeButton),
            Colors.White);

    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(
            nameof(Source),
            typeof(string),
            typeof(G9SafeButton));

    public static readonly BindableProperty ErrorMessageProperty =
        BindableProperty.Create(
            nameof(ErrorMessage),
            typeof(string),
            typeof(G9SafeButton));

    public static readonly BindableProperty ErrorTitleProperty =
        BindableProperty.Create(
            nameof(ErrorTitle),
            typeof(string),
            typeof(G9SafeButton));

    public static readonly BindableProperty ShowErrorG9PopupProperty =
        BindableProperty.Create(
            nameof(ShowErrorG9Popup),
            typeof(bool),
            typeof(G9SafeButton),
            true);

    public static readonly BindableProperty EnableThrottleProperty =
        BindableProperty.Create(
            nameof(EnableThrottle),
            typeof(bool),
            typeof(G9SafeButton),
            true);

    public static readonly BindableProperty ThrottleKeyProperty =
        BindableProperty.Create(
            nameof(ThrottleKey),
            typeof(string),
            typeof(G9SafeButton));

    public static readonly BindableProperty ThrottleIntervalProperty =
        BindableProperty.Create(
            nameof(ThrottleInterval),
            typeof(TimeSpan),
            typeof(G9SafeButton),
            TimeSpan.FromMilliseconds(369));

    public static readonly BindableProperty BusyDelayProperty =
        BindableProperty.Create(
            nameof(BusyDelay),
            typeof(TimeSpan),
            typeof(G9SafeButton),
            TimeSpan.Zero);

    // Throttle / busy / CanExecute / Command→SafeCommand routing. Shared with G9SafeIconButton.
    private readonly G9SafeExecution _safe;
    private bool _isMirroringBackground;

    public G9SafeButton()
    {
        _safe = new G9SafeExecution(this);

        // G9Button raises Clicked on tap — that's our entry point for safe execution. The base
        // class's own direct Command run is switched off below (ExecutesCommandOnTap), so a
        // plain Command="{Binding …}" goes through the safe runner exactly once.
        Clicked += OnG9SafeButtonClicked;
    }

    public Func<object?, Task>? SafeClickedCallbackAsync { get; set; }

    /// <summary>Legacy named look. Mapped onto <see cref="G9Button.Variant" />.</summary>
    public G9SafeButtonType ButtonType
    {
        get => (G9SafeButtonType)GetValue(ButtonTypeProperty);
        set => SetValue(ButtonTypeProperty, value);
    }

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
    ///     Kept for call-site compatibility. The <see cref="G9Button" /> built-in spinner
    ///     is driven by <see cref="G9Button.IsLoading" />; when this is false the loading
    ///     spinner is suppressed (the disable-while-loading guard still applies).
    /// </summary>
    public bool ShowSpinnerWhileLoading
    {
        get => (bool)GetValue(ShowSpinnerWhileLoadingProperty);
        set => SetValue(ShowSpinnerWhileLoadingProperty, value);
    }

    /// <summary>
    ///     Kept for call-site compatibility. Accepted no-op: the <see cref="G9Button" />
    ///     spinner inherits the resolved text color and cannot be independently recolored.
    /// </summary>
    public Color SpinnerColor
    {
        get => (Color)GetValue(SpinnerColorProperty);
        set => SetValue(SpinnerColorProperty, value);
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

        switch (propertyName)
        {
            // Legacy routing: a plain Command / CommandParameter is run through the safe layer.
            // It is READ when needed, never copied into SafeCommand and never cleared, so the
            // consumer's bindings on both stay alive (see G9SafeExecution). The null-conditional
            // covers property writes made by the base constructor, before _safe exists.
            case nameof(Command):
            case nameof(CommandParameter):
                _safe?.OnCommandSourceChanged();
                break;
            case nameof(BackgroundColor):
            case nameof(Background):
                MirrorExplicitBackgroundToBase(propertyName);
                break;
        }
    }

    private static void OnButtonTypeChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is G9SafeButton button && newValue is G9SafeButtonType type)
        {
            button.Variant = MapButtonType(type);
        }
    }

    private static G9ButtonVariant MapButtonType(G9SafeButtonType type)
    {
        return type switch
        {
            G9SafeButtonType.Primary => G9ButtonVariant.Primary,
            G9SafeButtonType.Default => G9ButtonVariant.Default,
            G9SafeButtonType.Secondary => G9ButtonVariant.Secondary,
            G9SafeButtonType.Info => G9ButtonVariant.Info,
            G9SafeButtonType.Success => G9ButtonVariant.Success,
            G9SafeButtonType.Warning => G9ButtonVariant.Warning,
            G9SafeButtonType.Error => G9ButtonVariant.Error,
            G9SafeButtonType.Surface => G9ButtonVariant.Surface,
            G9SafeButtonType.Outline => G9ButtonVariant.Outline,
            _ => G9ButtonVariant.Default
        };
    }

    /// <summary>
    ///     Mirror an explicit <see cref="VisualElement.BackgroundColor" /> / Background brush set
    ///     by a call site onto <see cref="G9Button.BaseBackgroundColor" /> so the rounded
    ///     frame paints it, then clear the raw ContentView background so it doesn't draw a
    ///     square behind the rounded button.
    /// </summary>
    private void MirrorExplicitBackgroundToBase(string? propertyName)
    {
        if (_isMirroringBackground)
        {
            return;
        }

        Color? resolved = propertyName switch
        {
            nameof(BackgroundColor) when IsSet(BackgroundColorProperty) => BackgroundColor,
            nameof(Background) when Background is SolidColorBrush brush => brush.Color,
            _ => null
        };

        if (resolved is null)
        {
            return;
        }

        _isMirroringBackground = true;
        try
        {
            BaseBackgroundColor = resolved;
            ClearValue(BackgroundColorProperty);
            ClearValue(BackgroundProperty);
        }
        finally
        {
            _isMirroringBackground = false;
        }
    }

    private static void OnSafeCommandPropertyChanged(BindableObject bindable, object? oldValue, object? newValue)
    {
        if (bindable is G9SafeButton button)
        {
            button._safe.OnCommandSourceChanged();
        }
    }

    private static void OnSafeCommandParameterPropertyChanged(BindableObject bindable, object? oldValue,
        object? newValue)
    {
        if (bindable is G9SafeButton button)
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

    string IG9SafeExecutionHost.SafeExecutionName => nameof(G9SafeButton);

    IReadOnlyList<Func<object?, Task>> IG9SafeExecutionHost.GetSafeClickedHandlers() =>
        SafeClickedAsync is null
            ? []
            : [.. SafeClickedAsync.GetInvocationList().Cast<Func<object?, Task>>()];

    void IG9SafeExecutionHost.OnInteractionBlockChanged() => RequestVisualUpdate();
}
