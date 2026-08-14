using Maui.BindableProperty.Generator.Core;

namespace G9MAUIControls.Controls;

public partial class G9TitleWithLine : ContentView
{
    public G9TitleWithLine()
    {
        InitializeComponent();

        SetValue(PaddingProperty, new Thickness(18, 3, 18, 0));
    }

    #region Bindable Properties

    [AutoBindable(DefaultValue = "ابزار")] 
    private string _text = null!;

    [AutoBindable] 
    private Thickness _padding;

    #endregion
}

