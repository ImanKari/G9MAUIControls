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

    public string Text { get; set; } = string.Empty;
    public G9IconSource Icon { get; set; } = G9Glyph.Check;
    public Action<G9TabBarClickContext>? Clicked { get; set; }
    public ICommand? Command { get; set; }
    public object? CommandParameter { get; set; }
    public string? AutomationId { get; set; }

    /// <summary>
    ///     Optional submenu polar angle in degrees. Negative angles render above the FAB.
    /// </summary>
    public double AngleDegrees { get; set; } = double.NaN;
}