using G9MAUIControls.Icons;
using Maui.BindableProperty.Generator.Core;
namespace G9MAUIControls.Controls;

[ContentProperty(nameof(TabContent))]
public partial class G9TabItem : BindableObject
{
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _text;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _emoji;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private G9IconSource? _icon;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _imagePath;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private ImageSource? _imageSource;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private int _badgeCount;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private string? _badgeText;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private bool _badgeDot;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private Color? _badgeColor;
    [AutoBindable(OnChanged = nameof(OnVisualChanged))] private View? _tabContent;

    public event EventHandler? VisualChanged;

    private void OnVisualChanged()
    {
        VisualChanged?.Invoke(this, EventArgs.Empty);
    }
}
