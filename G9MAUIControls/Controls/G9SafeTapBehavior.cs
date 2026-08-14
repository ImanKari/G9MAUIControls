using G9MAUIControls.Controls;
using G9MAUIControls.Helpers;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;

namespace G9MAUIControls.Controls;

/// <summary>
///     Attaches the app-standard "safe execution" layer to a tap on any <see cref="View" /> — the
///     gesture analog of <c>G9SafeButton</c>. Use it for user-initiated taps that are NOT a
///     <c>G9SafeButton</c>/<c>G9SafeIconButton</c> (settings rows, cards, label/icon taps) where a
///     rapid double-tap could fire the action twice (open a sheet/popup twice, navigate twice,
///     mutate data twice).
///     <para>
///         <see cref="TapGestureRecognizer" /> is <c>sealed</c> in MAUI, so the safe layer is
///         delivered as a behavior rather than a recognizer subclass. The behavior installs its
///         own <see cref="TapGestureRecognizer" /> on the target and routes the tap through
///         <see cref="G9SafeCommand" /> with throttle <b>on by default</b> (369 ms, keyed per
///         behavior instance), matching <c>G9SafeButton</c>.
///     </para>
///     <example>
///         <code>
///         &lt;Border&gt;
///             &lt;Border.Behaviors&gt;
///                 &lt;buttons:G9SafeTapBehavior Command="{Binding OpenLanguagePickerCommand}" /&gt;
///             &lt;/Border.Behaviors&gt;
///         &lt;/Border&gt;
///         </code>
///     </example>
/// </summary>
public sealed class G9SafeTapBehavior : Behavior<View>
{
    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(G9SafeTapBehavior));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(G9SafeTapBehavior));

    public static readonly BindableProperty NumberOfTapsRequiredProperty =
        BindableProperty.Create(nameof(NumberOfTapsRequired), typeof(int), typeof(G9SafeTapBehavior), 1);

    public static readonly BindableProperty EnableThrottleProperty =
        BindableProperty.Create(nameof(EnableThrottle), typeof(bool), typeof(G9SafeTapBehavior), true);

    public static readonly BindableProperty ThrottleKeyProperty =
        BindableProperty.Create(nameof(ThrottleKey), typeof(string), typeof(G9SafeTapBehavior));

    public static readonly BindableProperty ThrottleIntervalProperty =
        BindableProperty.Create(
            nameof(ThrottleInterval),
            typeof(TimeSpan),
            typeof(G9SafeTapBehavior),
            TimeSpan.FromMilliseconds(369));

    public static readonly BindableProperty SourceProperty =
        BindableProperty.Create(nameof(Source), typeof(string), typeof(G9SafeTapBehavior));

    public static readonly BindableProperty ShowErrorG9PopupProperty =
        BindableProperty.Create(nameof(ShowErrorG9Popup), typeof(bool), typeof(G9SafeTapBehavior), true);

    private readonly TapGestureRecognizer _recognizer = new();
    private View? _target;

    /// <summary>Throttled command executed on tap.</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    public int NumberOfTapsRequired
    {
        get => (int)GetValue(NumberOfTapsRequiredProperty);
        set => SetValue(NumberOfTapsRequiredProperty, value);
    }

    /// <summary>When true (default), a rapid repeat tap within <see cref="ThrottleInterval" /> is dropped.</summary>
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

    public string? Source
    {
        get => (string?)GetValue(SourceProperty);
        set => SetValue(SourceProperty, value);
    }

    public bool ShowErrorG9Popup
    {
        get => (bool)GetValue(ShowErrorG9PopupProperty);
        set => SetValue(ShowErrorG9PopupProperty, value);
    }

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);

        _target = bindable;
        // Inherit the host's BindingContext so {Binding} on Command/CommandParameter resolves
        // against the same view model as the rest of the view.
        BindingContext = bindable.BindingContext;
        bindable.BindingContextChanged += OnTargetBindingContextChanged;

        _recognizer.NumberOfTapsRequired = NumberOfTapsRequired;
        _recognizer.Tapped += OnTapped;
        bindable.GestureRecognizers.Add(_recognizer);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        bindable.GestureRecognizers.Remove(_recognizer);
        _recognizer.Tapped -= OnTapped;
        bindable.BindingContextChanged -= OnTargetBindingContextChanged;
        _target = null;
        BindingContext = null;

        base.OnDetachingFrom(bindable);
    }

    private void OnTargetBindingContextChanged(object? sender, EventArgs e)
    {
        if (_target is not null)
        {
            BindingContext = _target.BindingContext;
        }
    }

    private void OnTapped(object? sender, TappedEventArgs e)
    {
        if (Command is null)
        {
            return;
        }

        G9SafeCommand.RunSafe(
            () => ExecuteAsync(e),
            new G9SafeCommandOptions
            {
                Source = Source ?? nameof(G9SafeTapBehavior),
                ShowErrorG9Popup = ShowErrorG9Popup,
                EnableThrottle = EnableThrottle,
                ThrottleInterval = ThrottleInterval,
                ThrottleKey = string.IsNullOrWhiteSpace(ThrottleKey)
                    ? $"{nameof(G9SafeTapBehavior)}:{GetHashCode()}"
                    : ThrottleKey
            });
    }

    private async Task ExecuteAsync(TappedEventArgs e)
    {
        var parameter = CommandParameter ?? e.Parameter;
        if (Command is null || !Command.CanExecute(parameter))
        {
            return;
        }

        if (Command is IAsyncRelayCommand asyncRelayCommand)
        {
            await asyncRelayCommand.ExecuteAsync(parameter);
            return;
        }

        Command.Execute(parameter);
    }
}
