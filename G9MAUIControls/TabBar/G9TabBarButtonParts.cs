using G9MAUIControls.Icons;

namespace G9MAUIControls.TabBar;

/// <summary>Visual elements that make up a single bar item.</summary>
internal sealed record MenuButtonParts(
    Grid Root,
    G9IconView Icon,
    Label Label,
    G9TabBarItem Item);

/// <summary>
///     Visual elements that make up a single sub-menu item. The
///     <see cref="OuterSurface" /> is the translucent glass shell that mirrors the bar;
///     the smaller <see cref="InnerSurface" /> carries the green primary gradient and
///     hosts the icon. The label sits below the inner circle, still inside the glass shell.
/// </summary>
internal sealed record SubMenuButtonParts(
    Grid Root,
    Border OuterSurface,
    Border InnerSurface,
    G9IconView Icon,
    Label Label,
    G9TabBarItem Item);

/// <summary>
///     Visual elements that make up the overflow column item. <see cref="Surface" /> is
///     the translucent glass cell that flips its background between glass (idle) and
///     primary-gradient (selected) so the selected overflow entry reads as "the bar's
///     active selection lives here".
/// </summary>
internal sealed record OverflowItemParts(
    Grid Root,
    Border Surface,
    G9IconView Icon,
    Label Label,
    G9TabBarItem Item);
