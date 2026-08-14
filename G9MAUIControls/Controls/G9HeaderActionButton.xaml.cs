using System.Windows.Input;

using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

public partial class G9HeaderActionButton : ContentView
{
    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(G9HeaderActionButton));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(G9HeaderActionButton));

    // The cast is load-bearing. BindableProperty.Create takes defaultValue as OBJECT, so no implicit
    // conversion is applied at the call site: a bare `G9Glyph.Menu` is boxed as a G9Glyph, MAUI compares
    // it against the declared return type in the constructor, and throws
    //   ArgumentException: Default value did not match return type
    // from this class's STATIC constructor. That surfaces as a TypeInitializationException the first
    // time anything instantiates the control — no compiler warning, and nothing at all until the exact
    // screen using it is opened. Casting first boxes a G9IconSource, which is what the property
    // declares. See LES-0032.
    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(
            nameof(Icon),
            typeof(G9IconSource),
            typeof(G9HeaderActionButton),
            (G9IconSource)G9Glyph.Menu,
            propertyChanged: OnVisualStateRelatedPropertyChanged);

    public static readonly BindableProperty IconImageSourceProperty =
        BindableProperty.Create(
            nameof(IconImageSource),
            typeof(ImageSource),
            typeof(G9HeaderActionButton),
            default(ImageSource),
            propertyChanged: OnVisualStateRelatedPropertyChanged);

    public static readonly BindableProperty ButtonTextProperty =
        BindableProperty.Create(
            nameof(ButtonText),
            typeof(string),
            typeof(G9HeaderActionButton),
            string.Empty,
            propertyChanged: OnVisualStateRelatedPropertyChanged);

    public static readonly BindableProperty IsBusyProperty =
        BindableProperty.Create(
            nameof(IsBusy),
            typeof(bool),
            typeof(G9HeaderActionButton),
            false,
            defaultBindingMode: BindingMode.OneWay,
            propertyChanged: OnVisualStateRelatedPropertyChanged);

    public static readonly BindableProperty IsActiveProperty =
        BindableProperty.Create(
            nameof(IsActive),
            typeof(bool),
            typeof(G9HeaderActionButton),
            false,
            defaultBindingMode: BindingMode.OneWay,
            propertyChanged: OnVisualStateRelatedPropertyChanged);

    public static readonly BindableProperty ShowActiveBadgeProperty =
        BindableProperty.Create(
            nameof(ShowActiveBadge),
            typeof(bool),
            typeof(G9HeaderActionButton),
            true,
            propertyChanged: OnVisualStateRelatedPropertyChanged);

    public static readonly BindableProperty AnimatePressProperty =
        BindableProperty.Create(nameof(AnimatePress), typeof(bool), typeof(G9HeaderActionButton), true);

    public static readonly BindableProperty IconSizeProperty =
        BindableProperty.Create(
            nameof(IconSize),
            typeof(double),
            typeof(G9HeaderActionButton),
            G9MAUIControls.Theming.G9LayoutMetrics.ToolbarIconSize);

    /// <summary>
    ///     Square size of the button. <c>0</c> (the default) means "the sheet-toolbar size" — the
    ///     border then keeps a <c>DynamicResource</c> on <c>ToolbarButtonSize</c>, so it still
    ///     follows a live edit of that token. A host that puts the button in a row with other
    ///     controls (<c>SearchSortFilterHeader</c>) sets this to that row's control height, so the
    ///     whole lane measures the same (see design guide §3c).
    /// </summary>
    public static readonly BindableProperty ButtonSizeProperty =
        BindableProperty.Create(
            nameof(ButtonSize),
            typeof(double),
            typeof(G9HeaderActionButton),
            0d,
            propertyChanged: OnButtonSizeChanged);

    public G9HeaderActionButton()
    {
        InitializeComponent();
        ApplyButtonSize();
        UpdateVisualState();
    }

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

    public G9IconSource Icon
    {
        get => (G9IconSource)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public ImageSource? IconImageSource
    {
        get => (ImageSource?)GetValue(IconImageSourceProperty);
        set => SetValue(IconImageSourceProperty, value);
    }

    public string ButtonText
    {
        get => (string)GetValue(ButtonTextProperty);
        set => SetValue(ButtonTextProperty, value);
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool ShowActiveBadge
    {
        get => (bool)GetValue(ShowActiveBadgeProperty);
        set => SetValue(ShowActiveBadgeProperty, value);
    }

    public bool AnimatePress
    {
        get => (bool)GetValue(AnimatePressProperty);
        set => SetValue(AnimatePressProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public double ButtonSize
    {
        get => (double)GetValue(ButtonSizeProperty);
        set => SetValue(ButtonSizeProperty, value);
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);

        if (propertyName == nameof(IsEnabled))
        {
            UpdateVisualState();
        }
    }

    private static void OnVisualStateRelatedPropertyChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((G9HeaderActionButton)bindable).UpdateVisualState();
    }

    private static void OnButtonSizeChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((G9HeaderActionButton)bindable).ApplyButtonSize();
    }

    /// <summary>
    ///     An explicit <see cref="ButtonSize" /> wins and REPLACES the dynamic resource (otherwise a
    ///     later edit of <c>ToolbarButtonSize</c> would snap the border back to the toolbar size);
    ///     with no explicit size the border tracks <c>ToolbarButtonSize</c> dynamically.
    /// </summary>
    private void ApplyButtonSize()
    {
        if (ButtonSize > 0)
        {
            ButtonBorder.RemoveDynamicResource(HeightRequestProperty);
            ButtonBorder.RemoveDynamicResource(WidthRequestProperty);
            ButtonBorder.HeightRequest = ButtonSize;
            ButtonBorder.WidthRequest = ButtonSize;
            return;
        }

        ButtonBorder.SetDynamicResource(HeightRequestProperty, "ToolbarButtonSize");
        ButtonBorder.SetDynamicResource(WidthRequestProperty, "ToolbarButtonSize");
    }

    private async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled || IsBusy)
        {
            return;
        }

        if (AnimatePress)
        {
            await AnimatePressAsync();
        }

        if (Command?.CanExecute(CommandParameter) == true)
        {
            Command.Execute(CommandParameter);
        }
    }

    private void UpdateVisualState()
    {
        if (!MainThread.IsMainThread)
        {
            MainThread.BeginInvokeOnMainThread(UpdateVisualState);
            return;
        }

        var isBusy = IsBusy;
        BusyIndicator.IsVisible = isBusy;
        BusyIndicator.IsRunning = isBusy;

        var hasImageIcon = IconImageSource is not null;
        var hasText = !string.IsNullOrWhiteSpace(ButtonText);

        GlyphIconView.IsVisible = !isBusy && !hasImageIcon && !hasText;
        ImageIconView.IsVisible = !isBusy && hasImageIcon;
        TextIconView.IsVisible = !isBusy && !hasImageIcon && hasText;
    }

    private async Task AnimatePressAsync()
    {
        try
        {
            await ButtonBorder.ScaleToAsync(0.92, 70, Easing.CubicOut);
            await ButtonBorder.ScaleToAsync(1.0, 90, Easing.CubicOut);
        }
        catch
        {
            ButtonBorder.Scale = 1.0;
        }
    }
}
