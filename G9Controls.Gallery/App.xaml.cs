using G9MAUIControls.Theming;

namespace G9Controls.Gallery;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // Applies the persisted (or system) theme and pushes every palette token. Must run after
        // InitializeComponent so the theme dictionaries are already merged.
        G9Theme.Init();
    }

    protected override Window CreateWindow(IActivationState? activationState) =>
        new(new GalleryShell());
}
