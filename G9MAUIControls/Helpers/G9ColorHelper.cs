using System.Globalization;

namespace G9MAUIControls.Helpers;

/// <summary>
///     Helper methods for color operations and theme color resolution.
/// </summary>
public static class G9ColorHelper
{
    /// <summary>
    ///     Applies an opacity value to a color. If the opacity parameter is null, returns the color unchanged.
    /// </summary>
    /// <param name="color">The source color.</param>
    /// <param name="opacityParameter">Opacity as string (invariant culture), float, or double. Values are clamped to 0–1.</param>
    /// <returns>The color with the applied alpha, or the original color if parameter is null.</returns>
    public static Color WithOpacity(Color color, object? opacityParameter)
    {
        if (opacityParameter == null)
        {
            return color;
        }

        var opacity = 1.0f;
        switch (opacityParameter)
        {
            case string opacityString:
                if (!float.TryParse(opacityString, NumberStyles.Float, CultureInfo.InvariantCulture, out opacity))
                {
                    opacity = 1.0f;
                }

                break;
            case float floatValue:
                opacity = floatValue;
                break;
            case double doubleValue:
                opacity = (float)doubleValue;
                break;
        }

        opacity = Math.Clamp(opacity, 0f, 1f);
        return color.WithAlpha(opacity);
    }

    /// <summary>
    ///     Darkens a color by a given percentage (0.0 to 1.0).
    /// </summary>
    public static Color Darken(Color color, double percentage)
    {
        percentage = Math.Clamp(percentage, 0, 1);

        return new Color(
            (float)(color.Red * (1 - percentage)),
            (float)(color.Green * (1 - percentage)),
            (float)(color.Blue * (1 - percentage)),
            color.Alpha
        );
    }

    /// <summary>
    ///     Lightens a color by a given percentage (0.0 to 1.0).
    /// </summary>
    public static Color Lighten(Color color, double percentage)
    {
        percentage = Math.Clamp(percentage, 0, 1);

        return new Color(
            (float)(color.Red + ((1 - color.Red) * percentage)),
            (float)(color.Green + ((1 - color.Green) * percentage)),
            (float)(color.Blue + ((1 - color.Blue) * percentage)),
            color.Alpha
        );
    }

    /// <summary>
    ///     Mixes two colors together with a given ratio (0.0 = all color1, 1.0 = all color2).
    /// </summary>
    public static Color Mix(Color color1, Color color2, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);

        return new Color(
            (float)((color1.Red * (1 - ratio)) + (color2.Red * ratio)),
            (float)((color1.Green * (1 - ratio)) + (color2.Green * ratio)),
            (float)((color1.Blue * (1 - ratio)) + (color2.Blue * ratio)),
            (float)((color1.Alpha * (1 - ratio)) + (color2.Alpha * ratio))
        );
    }

    /// <summary>
    ///     Creates a border color that's approximately 15% darker than the base color.
    /// </summary>
    public static Color CreateBorderColor(Color baseColor)
    {
        return Darken(baseColor, 0.15);
    }

    /// <summary>
    ///     Creates a hover color that's approximately 8% darker than the base color.
    /// </summary>
    public static Color CreateHoverColor(Color baseColor)
    {
        return Darken(baseColor, 0.08);
    }

    /// <summary>
    ///     Creates a pressed color that's approximately 20% darker than the base color.
    /// </summary>
    public static Color CreatePressedColor(Color baseColor)
    {
        return Darken(baseColor, 0.20);
    }

    /// <summary>
    ///     Converts Color to hex string.
    /// </summary>
    public static string ToHex(Color color)
    {
        return $"#{(int)(color.Red * 255):X2}{(int)(color.Green * 255):X2}{(int)(color.Blue * 255):X2}";
    }

    /// <summary>
    ///     Gets the luminance of a color (0.0 = black, 1.0 = white).
    ///     Useful for determining if text should be white or black on this background.
    /// </summary>
    public static double GetLuminance(Color color)
    {
        // Using relative luminance formula
        var r = color.Red <= 0.03928 ? color.Red / 12.92 : Math.Pow((color.Red + 0.055) / 1.055, 2.4);
        var g = color.Green <= 0.03928 ? color.Green / 12.92 : Math.Pow((color.Green + 0.055) / 1.055, 2.4);
        var b = color.Blue <= 0.03928 ? color.Blue / 12.92 : Math.Pow((color.Blue + 0.055) / 1.055, 2.4);

        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    /// <summary>
    ///     Determines if text should be white or black based on background color for optimal contrast.
    /// </summary>
    public static Color GetContrastingTextColor(Color backgroundColor)
    {
        return GetLuminance(backgroundColor) > 0.5 ? Colors.Black : Colors.White;
    }

    /// <summary>
    ///     Calculates the contrast ratio between two colors (1.0 to 21.0).
    ///     WCAG AA requires at least 4.5:1 for normal text, 3:1 for large text.
    /// </summary>
    public static double GetContrastRatio(Color color1, Color color2)
    {
        var l1 = GetLuminance(color1);
        var l2 = GetLuminance(color2);

        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);

        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    ///     Checks if the contrast ratio meets WCAG AA standards for normal text (4.5:1).
    /// </summary>
    public static bool MeetsWcagAA(Color foreground, Color background)
    {
        return GetContrastRatio(foreground, background) >= 4.5;
    }

    /// <summary>
    ///     Checks if the contrast ratio meets WCAG AAA standards for normal text (7:1).
    /// </summary>
    public static bool MeetsWcagAAA(Color foreground, Color background)
    {
        return GetContrastRatio(foreground, background) >= 7.0;
    }
}