using System.Windows.Input;

using G9MAUIControls.Icons;

namespace G9MAUIControls.EdgePanel;

/// <summary>
///     Represents a single item in the <see cref="G9EdgePanel" /> list menu.
///     Supports Material icon, image source, emoji, direct text, localized text,
///     nested sub-lists, and a touch/click callback.
/// </summary>
public sealed class G9EdgeMenuItem
{
    public G9EdgeMenuItem()
    {
    }

    public G9EdgeMenuItem(string text)
    {
        Text = text;
    }

    public G9EdgeMenuItem(string text, G9IconSource icon)
    {
        Text = text;
        Icon = icon;
    }

    /// <summary>Material icon enum (optional). Rendered with G9IconView.</summary>
    public G9IconSource? Icon { get; set; }

    /// <summary>Image source path for FFImageLoading CachedImage (optional).</summary>
    public string? ImageSource { get; set; }

    /// <summary>Emoji string rendered as a label (optional, used when Icon and ImageSource are null).</summary>
    public string? Emoji { get; set; }

    /// <summary>Direct display text. Takes priority over <see cref="LocalizedTextKey" />.</summary>
    public string? Text { get; set; }

    /// <summary>Resource key for localized text from G9StringResources. Used when <see cref="Text" /> is null.</summary>
    public string? LocalizedTextKey { get; set; }

    /// <summary>
    ///     Nested sub-list. When set, tapping this item replaces the current list
    ///     with <see cref="NextList" />, and a back-navigation item is auto-prepended.
    /// </summary>
    public IList<G9EdgeMenuItem>? NextList { get; set; }

    /// <summary>
    ///     Optional header when navigating into <see cref="NextList" /> (main vs nested titles).
    /// </summary>
    public G9EdgeMenuHeader? SubMenuHeader { get; set; }

    /// <summary>Row background. When null, uses transparent.</summary>
    public Color? BackgroundColor { get; set; }

    /// <summary>Label text color. When null, uses theme default.</summary>
    public Color? TextColor { get; set; }

    /// <summary>Leading icon tint. When null, uses theme default.</summary>
    public Color? IconColor { get; set; }

    /// <summary>Callback invoked when the item is tapped.</summary>
    public Action<G9EdgeMenuItem>? Clicked { get; set; }

    /// <summary>Command executed when the item is tapped (runs after <see cref="Clicked" />).</summary>
    public ICommand? Command { get; set; }

    /// <summary>Parameter passed to <see cref="Command" />.</summary>
    public object? CommandParameter { get; set; }

    /// <summary>Optional automation ID for testing.</summary>
    public string? AutomationId { get; set; }

    /// <summary>When true, a thin divider is drawn below this item.</summary>
    public bool ShowDividerBelow { get; set; }

    /// <summary>Whether this item is enabled. Disabled items are visually dimmed and not tappable.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    ///     When true, the panel collapses (slides back to the tab) after the item's
    ///     <see cref="Clicked"/> / <see cref="Command"/> handlers run. Use this for
    ///     leaf actions whose effect must be visible on the host underneath the panel
    ///     (e.g. "zoom map to this block" when the panel covers the map). Has no effect
    ///     for items that open a <see cref="NextList"/> sub-menu — the panel stays open
    ///     to show the new level.
    /// </summary>
    public bool CloseAfterClick { get; set; }
}
