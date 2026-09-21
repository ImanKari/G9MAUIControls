namespace G9MAUIControls.Toast;

internal sealed class G9InlineToastHandle(Layout parent, View layer, G9ToastPosition position = default)
{
    public Layout Parent { get; } = parent;
    public View Layer { get; } = layer;
    public G9ToastPosition Position { get; } = position;
    public VisualElement? FillLayer { get; init; }
    public CancellationTokenSource? AutoDismissCts { get; set; }
    public bool IsDismissing { get; set; }
    public double StackOffset { get; set; }

    // What the toast says, kept so G9ToastHelper can recognise "the same toast again" and refresh
    // this one instead of stacking a copy. Null for the loading / progress toasts, which are never
    // de-duplicated.
    public string? Message { get; init; }
    public G9ToastType? Type { get; init; }

    /// <summary>
    ///     True when the toast carries an action button. Such a toast is never collapsed into: two
    ///     "Item deleted — Undo" toasts read the same but undo different items.
    /// </summary>
    public bool HasAction { get; init; }
}
