namespace G9MAUIControls.Controls;

/// <summary>
///     One onboarding slide: optional bundled video (<see cref="VideoAssetPath" />),
///     optional static image fallback (<see cref="ImageSource" />), and localized copy keys.
/// </summary>
public sealed class G9IntroSlideItem
{
    /// <summary>
    ///     MauiAsset logical path (e.g. <c>Onboarding/1video.mp4</c>) opened via
    ///     <see cref="FileSystem.OpenAppPackageFileAsync(string)" />.
    /// </summary>
    public string? VideoAssetPath { get; init; }

    /// <summary>
    ///     Maui image resource file name (e.g. <c>slide1.png</c>) used when
    ///     <see cref="VideoAssetPath" /> is empty or video playback fails.
    /// </summary>
    public string? ImageSource { get; init; }

    public string TitleResourceKey { get; init; } = string.Empty;

    public string SubtitleResourceKey { get; init; } = string.Empty;

    public bool HasVideo => !string.IsNullOrWhiteSpace(VideoAssetPath);
}
