using G9MAUIControls.Icons;

namespace G9MAUIControls.Controls;

/// <summary>
///     Identity of what an icon slot is showing: every input that decides WHICH view
///     <see cref="G9IconFactory.Create" /> builds, plus the size it is built at.
///     <para>
///         The earlier per-control string signatures recorded a bitmap only as "0" / "1" and left
///         the size out, so swapping one <see cref="Microsoft.Maui.Controls.ImageSource" /> for
///         another, or changing the icon size, compared equal and never refreshed. Here the
///         <see cref="ImageSource" /> takes part by reference and the size by value — and being a
///         struct, comparing it allocates nothing, unlike building a string per visual pass.
///     </para>
/// </summary>
internal readonly record struct G9IconSlotSignature(
    string? Emoji,
    G9IconSource? Icon,
    string? ImagePath,
    ImageSource? ImageSource,
    double Size);

/// <summary>
///     Keeps an icon host's content stable across visual passes (<c>G9Controls.md</c> §12 / §12a):
///     the view is rebuilt only when the slot's <see cref="G9IconSlotSignature" /> changes;
///     otherwise the existing view is re-tinted in place. Rebuilding on every pass re-decoded
///     bitmaps and flashed font glyphs on each theme flip, press and loading toggle.
/// </summary>
internal static class G9IconSlot
{
    /// <summary>
    ///     Reconciles <paramref name="host" /> with the given icon inputs. <paramref name="last" />
    ///     is the caller's cached signature for this slot and is updated when the view is rebuilt.
    ///     Returns <c>true</c> when the slot has an icon to show.
    /// </summary>
    public static bool Apply(
        ContentView host,
        ref G9IconSlotSignature last,
        string? emoji,
        G9IconSource? icon,
        string? imagePath,
        ImageSource? imageSource,
        Color color,
        double size)
    {
        var hasIcon = G9IconFactory.HasIcon(emoji, icon, imagePath, imageSource);
        var signature = hasIcon
            ? new G9IconSlotSignature(emoji, icon, imagePath, imageSource, size)
            : default;

        if (signature != last || (hasIcon && host.Content is null))
        {
            last = signature;
            host.Content = hasIcon
                ? G9IconFactory.Create(emoji, icon, imagePath, imageSource, color, size)
                : null;
            return hasIcon;
        }

        // Same icon — only the tint can differ. Emoji and bitmaps carry their own colours.
        if (host.Content is G9IconView view && view.Color != color)
        {
            view.Color = color;
        }

        return hasIcon;
    }
}
