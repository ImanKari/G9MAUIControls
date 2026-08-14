using G9MAUIControls.Controls;
using G9MAUIControls.Icons;
namespace G9MAUIControls.Controls;

public sealed class G9SelectionItem : IG9SelectionIdentity
{
    public string Text { get; set; } = string.Empty;
    public string? Key { get; set; }
    public string? Emoji { get; set; }
    public G9IconSource? Icon { get; set; }
    public string? IconPath { get; set; }
    public ImageSource? IconSource { get; set; }

    /// <summary>
    ///     Optional semantic tint override for <see cref="Icon" /> (e.g. a health/growth
    ///     status option that wants its icon colored green/orange/red instead of the field's
    ///     default neutral <c>OnSurfaceVariant</c>). Ignored for Emoji/IconPath/IconSource icons.
    /// </summary>
    public Color? IconTintColor { get; set; }

    /// <summary>
    ///     Optional two-color swatch shown in place of an icon when no
    ///     Emoji/Icon/IconPath/IconSource is set (e.g. a variety's stored
    ///     FirstColor/SecondColor, which has no real icon image). Both must be set to render.
    /// </summary>
    public Color? SwatchFirstColor { get; set; }

    public Color? SwatchSecondColor { get; set; }

    public object? Value { get; set; }
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    ///     Optional per-item SELECTED background color. When set, this chip paints with this color
    ///     while selected instead of the group-wide <c>G9ChipGroup.SelectedBackground</c> — used,
    ///     e.g., to paint each task-state chip in its own state <c>foregroundColorCode</c>. The
    ///     UNSELECTED look is unchanged (the default chip style). <c>null</c> ⇒ use the group default.
    /// </summary>
    public Color? SelectedColor { get; set; }

    /// <summary>
    ///     Optional per-item SELECTED text/icon color. <c>null</c> ⇒ use the group-wide
    ///     <c>G9ChipGroup.SelectedTextColor</c> (or its default).
    /// </summary>
    public Color? SelectedTextColor { get; set; }

    public object? SelectionIdentity => string.IsNullOrWhiteSpace(Key) ? Text : Key;
}

public interface IG9TextValidator
{
    string? Validate(string? text);
}
