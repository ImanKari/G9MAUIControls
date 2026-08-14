using Maui.BindableProperty.Generator.Core;
using Microsoft.Maui.Controls.Shapes;

namespace G9MAUIControls.Controls;

public partial class G9G9PlusButtonItemView : ContentView
{
    #region Bindable Properties

    [AutoBindable(OnChanged = nameof(OnMenuFlowDirectionChanged))]
    private FlowDirection _menuFlowDirection;

    #endregion

    #region Public Properties

    /// <summary>Text border element for width animation (0 → target).</summary>
    public View TextBorderPart => TextBorder;

    /// <summary>Text label element for opacity fade animation.</summary>
    public Label TextLabelPart => TextLabel;

    #endregion

    #region Initialization

    public G9G9PlusButtonItemView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnLoaded;
        ApplyFlowDirectionStyling();
    }

    #endregion

    #region Flow Direction Handling

    private void OnMenuFlowDirectionChanged()
    {
        ApplyFlowDirectionStyling();
    }

    private void ApplyFlowDirectionStyling()
    {
        var isRtl = MenuFlowDirection == FlowDirection.RightToLeft;
        TapGrid.FlowDirection = isRtl ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        TextBorder.StrokeShape = CreateCornerRadius(isRtl);
    }

    private static RoundRectangle CreateCornerRadius(bool isRtl)
    {
        return new RoundRectangle
        {
            CornerRadius = isRtl
                ? new CornerRadius(9, 0, 29, 0) // RTL: rounded on left side
                : new CornerRadius(0, 9, 0, 29) // LTR: rounded on right side
        };
    }

    #endregion
}