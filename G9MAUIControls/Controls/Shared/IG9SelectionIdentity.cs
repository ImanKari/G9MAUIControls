namespace G9MAUIControls.Controls;

/// <summary>
///     Lets a selectable item say which underlying thing it represents, so selection survives the
///     item object being replaced.
///     <para>
///         <b>Why identity and not reference equality.</b> Selection lists are rebuilt constantly —
///         a filter is typed, a page reloads, a virtualized row is recycled — and each rebuild
///         produces <i>new</i> item instances for the <i>same</i> underlying entities. Comparing by
///         reference silently drops the user's selection on every one of those rebuilds. Comparing
///         by <see cref="SelectionIdentity" /> keeps it.
///     </para>
///     <para>
///         Every selection surface in the suite honours this: <c>G9SelectionSheet</c> (shared by
///         <c>G9Picker</c> and <c>G9ComboBox</c>) and the bottom-sheet list picker. Return a stable
///         key — a database id, a code, a GUID string. Returning <c>null</c> falls back to
///         reference equality, which is correct only for items you never rebuild.
///     </para>
/// </summary>
public interface IG9SelectionIdentity
{
    /// <summary>
    ///     A stable key for the entity this item represents, or <c>null</c> to fall back to
    ///     reference equality. Must not change over the item's lifetime.
    /// </summary>
    object? SelectionIdentity { get; }
}
