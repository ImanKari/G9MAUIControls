namespace G9MAUIControls.TabBar;

public sealed class G9TabBarClickContext(
    int index,
    G9TabBarItem item,
    bool isSubMenuItem)
{
    public int Index { get; } = index;
    public G9TabBarItem Item { get; } = item;
    public bool IsSubMenuItem { get; } = isSubMenuItem;
}