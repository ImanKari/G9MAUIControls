using Microsoft.UI.Xaml;

namespace G9Controls.Gallery.WinUI;

/// <summary>The WinUI entry point. Provides the app's <c>Main</c> via <c>DISABLE_XAML_GENERATED_MAIN</c> off.</summary>
public partial class App : MauiWinUIApplication
{
    public App() => InitializeComponent();

    protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
}
