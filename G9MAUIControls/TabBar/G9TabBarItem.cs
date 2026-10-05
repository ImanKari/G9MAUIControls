using System.Windows.Input;

using G9MAUIControls.Icons;

namespace G9MAUIControls.TabBar;

public sealed class G9TabBarItem
{
    public G9TabBarItem()
    {
    }

    public G9TabBarItem(string text, G9IconSource icon)
    {
        Text = text;
        Icon = icon;
    }

    public G9TabBarItem(string text, G9IconSource icon, G9IconSource selectedIcon)
        : this(text, icon)
    {
        SelectedIcon = selectedIcon;
    }

    public string Text { get; set; } = string.Empty;
    public G9IconSource Icon { get; set; } = G9Glyph.Check;

    /// <summary>
    ///     Optional icon shown while this item is the selected tab — typically the FILLED twin of an
    ///     outline <see cref="Icon" />. Used by <see cref="G9TabBarStyle.Sculpted" />, whose selected state
    ///     is "filled icon + primary colour" instead of a pill; Classic ignores it (its pill already marks
    ///     the selection, and swapping the glyph under a sliding pill would read as a second animation).
    ///     <c>null</c> keeps <see cref="Icon" /> in both states.
    /// </summary>
    public G9IconSource? SelectedIcon { get; set; }
    public Action<G9TabBarClickContext>? Clicked { get; set; }
    public ICommand? Command { get; set; }
    public object? CommandParameter { get; set; }
    public string? AutomationId { get; set; }

    /// <summary>
    ///     Optional submenu polar angle in degrees. Negative angles render above the FAB.
    /// </summary>
    public double AngleDegrees { get; set; } = double.NaN;
}