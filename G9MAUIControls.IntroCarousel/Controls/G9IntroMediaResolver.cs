namespace G9MAUIControls.Controls;

/// <summary>
///     Copies bundled MauiAsset videos into <see cref="FileSystem.CacheDirectory" /> once
///     so <see cref="CommunityToolkit.Maui.Views.MediaElement" /> can play from a file path.
/// </summary>
internal static class G9IntroMediaResolver
{
    private static readonly Dictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<string?> ResolveVideoFileAsync(
        string assetPath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            return null;
        }

        if (Cache.TryGetValue(assetPath, out var cached) && File.Exists(cached))
        {
            return cached;
        }

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (Cache.TryGetValue(assetPath, out cached) && File.Exists(cached))
            {
                return cached;
            }

            var cacheDir = Path.Combine(FileSystem.CacheDirectory, "intro-media");
            Directory.CreateDirectory(cacheDir);

            var fileName = Path.GetFileName(assetPath);
            var dest = Path.Combine(cacheDir, fileName);

            if (!File.Exists(dest))
            {
                await using var package = await FileSystem
                    .OpenAppPackageFileAsync(assetPath)
                    .ConfigureAwait(false);
                await using var output = File.Create(dest);
                await package.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
            }

            Cache[assetPath] = dest;
            return dest;
        }
        finally
        {
            Gate.Release();
        }
    }

    public static Task PreloadAllAsync(
        IEnumerable<string?> assetPaths,
        CancellationToken cancellationToken = default)
    {
        var paths = assetPaths
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (paths.Count == 0)
        {
            return Task.CompletedTask;
        }

        return Task.WhenAll(paths.Select(path => ResolveVideoFileAsync(path!, cancellationToken)));
    }
}
