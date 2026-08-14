using G9MAUIControls.Icons;
using System.Windows.Input;

namespace G9MAUIControls.Controls;

/// <summary>
///     Declarative description of one swipe action that <see cref="G9SwipeView" /> renders
///     into a <see cref="Microsoft.Maui.Controls.SwipeItem" /> at runtime.
///     <para>
///         The model exposes the minimal set of properties needed —
///         <see cref="Text" />, <see cref="Icon" />, <see cref="Background" />,
///         <see cref="IsDestructive" /> — and lets <see cref="G9SwipeView" /> own the
///         visual layout (icon-above-text, alignment, padding) and the corner-radius
///         math (rounded card edges via an outer clipping <see cref="Microsoft.Maui.Controls.Border" />).
///     </para>
///     <para>
///         Extends <see cref="BindableObject" /> so that localization markup extensions
///         (e.g. <c>{maui:Translate Key}</c>) — which return a <see cref="BindingBase" /> —
///         can bind to its properties in AOT-compiled XAML. <see cref="BindableObject" />
///         also satisfies <see cref="System.ComponentModel.INotifyPropertyChanged" />, so
///         <see cref="G9SwipeView" /> can continue subscribing to
///         <see cref="System.ComponentModel.INotifyPropertyChanged.PropertyChanged" /> for
///         in-place <c>SwipeItem</c> mutations on property changes.
///     </para>
/// </summary>
public sealed class G9SwipeAction : BindableObject
{
    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(G9SwipeAction));

    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(G9IconSource?), typeof(G9SwipeAction));

    public static readonly BindableProperty BackgroundProperty =
        BindableProperty.Create(nameof(Background), typeof(Color), typeof(G9SwipeAction));

    public static readonly BindableProperty ForegroundProperty =
        BindableProperty.Create(nameof(Foreground), typeof(Color), typeof(G9SwipeAction));

    public static readonly BindableProperty IsDestructiveProperty =
        BindableProperty.Create(nameof(IsDestructive), typeof(bool), typeof(G9SwipeAction), false);

    public static readonly BindableProperty IsVisibleProperty =
        BindableProperty.Create(nameof(IsVisible), typeof(bool), typeof(G9SwipeAction), true);

    public static readonly BindableProperty IsEnabledProperty =
        BindableProperty.Create(nameof(IsEnabled), typeof(bool), typeof(G9SwipeAction), true);

    public static readonly BindableProperty IconSizeProperty =
        BindableProperty.Create(nameof(IconSize), typeof(double), typeof(G9SwipeAction), 22.0);

    public static readonly BindableProperty WidthRequestProperty =
        BindableProperty.Create(nameof(WidthRequest), typeof(double), typeof(G9SwipeAction),
            G9SwipeView.DefaultActionWidth);

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(G9SwipeAction));

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(G9SwipeAction));

    /// <summary>
    ///     Action label rendered below the icon. Supports localization markup extensions such as
    ///     <c>{maui:Translate Key}</c> because this property is backed by a
    ///     <see cref="BindableProperty" />.
    ///     Mutating this on a culture flip causes <see cref="G9SwipeView" /> to update the
    ///     rendered <c>SwipeItem.Text</c> in place.
    /// </summary>
    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    ///     The action's glyph, from any source — a built-in <see cref="G9Glyph" />, a member of
    ///     your own registered icon font, or an explicit font/glyph pair. See
    ///     <see cref="G9IconSource" />.
    /// </summary>
    public G9IconSource? Icon
    {
        get => (G9IconSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    /// <summary>
    ///     Background color. When null, <see cref="G9SwipeView" /> resolves a sensible
    ///     default from the theme palette (Primary for normal actions, Error when
    ///     <see cref="IsDestructive" /> is true).
    /// </summary>
    public Color? Background
    {
        get => (Color?)GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    ///     Foreground color used for the icon glyph and label. When null the wrapper
    ///     resolves a sensible default from the theme palette (OnPrimary normally,
    ///     OnError when <see cref="IsDestructive" /> is true). The G9IconView View used
    ///     to render the glyph honors this color directly — no FontImageSource path
    ///     in this control.
    /// </summary>
    public Color? Foreground
    {
        get => (Color?)GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    public bool IsDestructive
    {
        get => (bool)GetValue(IsDestructiveProperty);
        set => SetValue(IsDestructiveProperty, value);
    }

    public bool IsVisible
    {
        get => (bool)GetValue(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    public bool IsEnabled
    {
        get => (bool)GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    /// <summary>
    ///     Fixed width for the action pane. Defaults to <see cref="G9SwipeView.DefaultActionWidth" />.
    ///     The native SwipeItem ignores child measure, so a hard width is the only way to
    ///     control horizontal sizing.
    /// </summary>
    public double WidthRequest
    {
        get => (double)GetValue(WidthRequestProperty);
        set => SetValue(WidthRequestProperty, value);
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

    /// <summary>
    ///     Fired when the user taps the action.
    /// </summary>
    public event EventHandler? Invoked;

    internal void RaiseInvoked() => Invoked?.Invoke(this, EventArgs.Empty);
}
