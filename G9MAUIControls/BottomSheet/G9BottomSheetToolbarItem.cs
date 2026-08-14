using G9MAUIControls.Icons;

namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Extended toolbar item for full-screen bottom sheet headers with optional busy indicator
///     and active badge support.
/// </summary>
public sealed class G9BottomSheetToolbarItem : ToolbarItem
{
    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(
            nameof(Icon),
            typeof(G9IconSource?),
            typeof(G9BottomSheetToolbarItem),
            default(G9IconSource?));

    public static readonly BindableProperty ShowBusyIndicatorProperty =
        BindableProperty.Create(
            nameof(ShowBusyIndicator),
            typeof(bool),
            typeof(G9BottomSheetToolbarItem),
            false);

    public static readonly BindableProperty IsBusyProperty =
        BindableProperty.Create(
            nameof(IsBusy),
            typeof(bool),
            typeof(G9BottomSheetToolbarItem),
            false,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly BindableProperty ShowActiveBadgeProperty =
        BindableProperty.Create(
            nameof(ShowActiveBadge),
            typeof(bool),
            typeof(G9BottomSheetToolbarItem),
            false);

    public static readonly BindableProperty IsActiveProperty =
        BindableProperty.Create(
            nameof(IsActive),
            typeof(bool),
            typeof(G9BottomSheetToolbarItem),
            false,
            defaultBindingMode: BindingMode.TwoWay);

    public static readonly BindableProperty DisableWhileBusyProperty =
        BindableProperty.Create(
            nameof(DisableWhileBusy),
            typeof(bool),
            typeof(G9BottomSheetToolbarItem),
            true);

    public static readonly BindableProperty AnimatePressProperty =
        BindableProperty.Create(
            nameof(AnimatePress),
            typeof(bool),
            typeof(G9BottomSheetToolbarItem),
            true);

    /// <summary>
    ///     Optional custom icon size (dp) for the rendered header action. When <c>0</c> (default)
    ///     the shared <c>G9LayoutMetrics.ToolbarIconSize</c> is used, so existing
    ///     toolbar items are unaffected. Set a positive value for a larger/smaller glyph (e.g. a
    ///     prominent status toggle) without replacing the whole trailing slot with a custom view.
    /// </summary>
    public static readonly BindableProperty IconSizeProperty =
        BindableProperty.Create(
            nameof(IconSize),
            typeof(double),
            typeof(G9BottomSheetToolbarItem),
            0d);

    /// <summary>
    ///     Optional async callback executed by the full-screen bottom sheet header renderer.
    ///     When set, this is preferred over command execution.
    /// </summary>
    public Func<Task>? AsyncAction { get; set; }

    public G9IconSource? Icon
    {
        get => (G9IconSource?)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public bool ShowBusyIndicator
    {
        get => (bool)GetValue(ShowBusyIndicatorProperty);
        set => SetValue(ShowBusyIndicatorProperty, value);
    }

    public bool IsBusy
    {
        get => (bool)GetValue(IsBusyProperty);
        set => SetValue(IsBusyProperty, value);
    }

    public bool ShowActiveBadge
    {
        get => (bool)GetValue(ShowActiveBadgeProperty);
        set => SetValue(ShowActiveBadgeProperty, value);
    }

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public bool DisableWhileBusy
    {
        get => (bool)GetValue(DisableWhileBusyProperty);
        set => SetValue(DisableWhileBusyProperty, value);
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
}
