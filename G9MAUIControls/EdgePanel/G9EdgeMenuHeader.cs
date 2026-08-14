using Microsoft.Maui.Controls;

namespace G9MAUIControls.EdgePanel;

/// <summary>
///     Optional title area shown above menu rows (below the panel chrome). Use exactly one of
///     <see cref="Text" />, <see cref="LocalizationKey" />, or <see cref="CustomView" />.
/// </summary>
public sealed class G9EdgeMenuHeader
{
    /// <summary>Plain text (not localized).</summary>
    public string? Text { get; init; }

    /// <summary><c>G9StringResources</c> resource key.</summary>
    public string? LocalizationKey { get; init; }

    /// <summary>
    ///     Custom header view (takes precedence when not null). The same instance may be reused;
    ///     <see cref="G9EdgePanel" /> detaches it from the previous parent when rebuilding menus.
    /// </summary>
    public View? CustomView { get; init; }

    /// <summary>Returns true when this header has no content.</summary>
    public bool IsEmpty => CustomView is null && string.IsNullOrEmpty(Text) && string.IsNullOrEmpty(LocalizationKey);

    public static G9EdgeMenuHeader FromText(string text) => new() { Text = text };

    public static G9EdgeMenuHeader FromDictionaryKey(string localizationKey) => new() { LocalizationKey = localizationKey };

    public static G9EdgeMenuHeader FromView(View view) => new() { CustomView = view };
}
