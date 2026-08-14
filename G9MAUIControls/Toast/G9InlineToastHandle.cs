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
}
