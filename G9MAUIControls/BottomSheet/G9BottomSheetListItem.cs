using G9MAUIControls.Controls;
using G9MAUIControls.Icons;

namespace G9MAUIControls.BottomSheet;

/// <summary>
///     Represents one selectable row in the reusable list picker bottom sheet.
/// </summary>
public sealed record G9BottomSheetListItem : IG9SelectionIdentity
{
    private ImageSource? _resolvedIconSource;
    public required string Text { get; init; }

    /// <summary>
    ///     Optional stable identifier for deterministic selection mapping.
    /// </summary>
    public string? Key { get; init; }

    /// <summary>
    ///     Optional font icon (G9IconSource).
    /// </summary>
    public G9IconSource? Icon { get; init; }

    /// <summary>
    ///     Optional image path/url (png/jpg/etc.).
    /// </summary>
    public string? IconPath { get; init; }

    /// <summary>
    ///     Optional image source override. When set, this has priority over <see cref="IconPath" />.
    /// </summary>
    public ImageSource? IconSource { get; init; }

    public bool HasIcon => Icon.HasValue;

    public bool HasResolvedIconSource => ResolvedIconSource is not null;

    public ImageSource? ResolvedIconSource => _resolvedIconSource ??= ResolveIconSource();

    public object? SelectionIdentity => string.IsNullOrWhiteSpace(Key) ? Text : Key;

    private ImageSource? ResolveIconSource()
    {
        if (IconSource is not null) return IconSource;

        if (string.IsNullOrWhiteSpace(IconPath)) return null;

        if (Uri.TryCreate(IconPath, UriKind.Absolute, out var absoluteUri) &&
            (absoluteUri.Scheme == Uri.UriSchemeHttp || absoluteUri.Scheme == Uri.UriSchemeHttps))
            return ImageSource.FromUri(absoluteUri);

        return ImageSource.FromFile(IconPath);
    }
}
