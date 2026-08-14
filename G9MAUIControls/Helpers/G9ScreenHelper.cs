namespace G9MAUIControls.Helpers;

/// <summary>
///     Provides screen-related measurements in device-independent pixels (DIPs).
/// </summary>
public static class G9ScreenHelper
{
    /// <summary>Screen width in DIPs.</summary>
    public static double WidthInDip
    {
        get
        {
            var info = DeviceDisplay.MainDisplayInfo;
            return info.Width / info.Density;
        }
    }

    /// <summary>Screen height in DIPs.</summary>
    public static double HeightInDip
    {
        get
        {
            var info = DeviceDisplay.MainDisplayInfo;
            return info.Height / info.Density;
        }
    }

    /// <summary>
    ///     Returns true when the screen is wide enough to accommodate the given width in DIPs.
    /// </summary>
    public static bool CanFit(double widthInDip)
    {
        return WidthInDip >= widthInDip;
    }
}