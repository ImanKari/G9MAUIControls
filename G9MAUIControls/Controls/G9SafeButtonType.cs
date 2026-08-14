namespace G9MAUIControls.Controls;

public enum G9SafeButtonType
{
    Primary = 0,
    Default = 1,
    Secondary = 2,
    Info = 3,
    Success = 4,
    Warning = 5,
    Error = 6,
    Surface = 7,

    /// <summary>
    ///     Bordered / outline look: transparent fill, themed text, hairline outline. The
    ///     design-system standard for secondary / cancel / decline actions that sit next to a
    ///     solid <see cref="Primary" /> (or destructive <see cref="Error" />) main action.
    /// </summary>
    Outline = 8
}