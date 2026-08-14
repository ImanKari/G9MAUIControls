using System.Globalization;

namespace G9MAUIControls.Theming;

/// <summary>
///     Converts a Color to the same color with the specified alpha value (0-1).
/// </summary>
public sealed class G9AlphaConverter : IValueConverter
{
    public static G9AlphaConverter Instance { get; } = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not Color c)
        {
            return value;
        }

        if (parameter is double a)
        {
            return c.WithAlpha((float)Math.Clamp(a, 0, 1));
        }

        return c;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}