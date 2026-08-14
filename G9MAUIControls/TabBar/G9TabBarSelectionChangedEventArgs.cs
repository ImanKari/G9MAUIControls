namespace G9MAUIControls.TabBar;

public sealed class G9TabBarSelectionChangedEventArgs(
    int index,
    G9TabBarItem item,
    bool isSubMenuItem)
    : EventArgs
{
    public int Index { get; } = index;
    public G9TabBarItem Item { get; } = item;
    public bool IsSubMenuItem { get; } = isSubMenuItem;
}