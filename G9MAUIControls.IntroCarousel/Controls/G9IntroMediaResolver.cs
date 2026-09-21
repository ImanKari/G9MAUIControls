using System.Collections.Concurrent;

namespace G9MAUIControls.Controls;

/// <summary>
///     Copies bundled MauiAsset videos into <see cref="FileSystem.CacheDirectory" /> once
///     so <see cref="CommunityToolkit.Maui.Views.MediaElement" /> can play from a file path.
///     <para>
///         Three rules keep the copy trustworthy, each the fix for a way the old one went wrong:
///         the copy is written to a <c>.tmp</c> sibling and renamed into place, so a copy cancelled
///         half-way (a slide change, the page closing) can never be mistaken for the finished file
///         on the next run; copies live in a per-app-version folder, so an update that ships a new
///         <c>intro.mp4</c> under the same name does not keep playing the old one; and the path map
///         is a <see cref="ConcurrentDictionary{TKey,TValue}" />, because the lock-free fast path
///         reads it while <see cref="PreloadAllAsync" /> writes it from another thread.
///     </para>
/// </summary>
internal static class G9IntroMediaResolver
{
    private const string CacheFolderName = "intro-media";
    private const string TempSuffix = ".tmp";

    private static readonly ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Set once the copies left behind by other app versions have been swept (under <see cref="Gate" />).</summary>
    private static bool _staleCopiesSwept;

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

            var cacheRoot = Path.Combine(FileSystem.CacheDirectory, CacheFolderName);
            var versionFolder = ResolveVersionFolderName();
            var cacheDir = Path.Combine(cacheRoot, versionFolder);
            Directory.CreateDirectory(cacheDir);

            if (!_staleCopiesSwept)
            {
                _staleCopiesSwept = true;
                SweepStaleCopies(cacheRoot, versionFolder);
            }

            var fileName = Path.GetFileName(assetPath);
            var dest = Path.Combine(cacheDir, fileName);

            if (!File.Exists(dest))
            {
                var temp = dest + TempSuffix;
                try
                {
                    await using (var package = await FileSystem
                                     .OpenAppPackageFileAsync(assetPath)
                                     .ConfigureAwait(false))
                    await using (var output = File.Create(temp))
                    {
                        await package.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    }

                    // Only a COMPLETE copy ever gets the real name.
                    File.Move(temp, dest, overwrite: true);
                }
                catch
                {
                    TryDelete(temp);
                    throw;
                }
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

    /// <summary>
    ///     Folder name derived from the app's version + build, so every app update starts from an
    ///     empty folder. The asset stream's length would be a finer key, but a packaged Android
    ///     asset stream does not reliably report one.
    /// </summary>
    private static string ResolveVersionFolderName()
    {
        string raw;
        try
        {
            raw = AppInfo.Current.VersionString + "-" + AppInfo.Current.BuildString;
        }
        catch
        {
            // No app info on this host (a test harness): one shared folder, as before.
            raw = "0";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = raw.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return "v" + new string(chars);
    }

    /// <summary>
    ///     Best-effort removal of everything in the cache root that is not the current version's
    ///     folder: other versions' copies, and the loose files written before copies were versioned
    ///     (any of which may be a truncated copy that was trusted forever).
    /// </summary>
    private static void SweepStaleCopies(string cacheRoot, string currentVersionFolder)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(cacheRoot))
            {
                TryDelete(file);
            }

            foreach (var directory in Directory.EnumerateDirectories(cacheRoot))
            {
                if (string.Equals(Path.GetFileName(directory), currentVersionFolder, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch
                {
                    // In use or already gone — it is only disk space; try again next launch.
                }
            }

            // A .tmp left in the current folder by a process that died mid-copy.
            foreach (var leftover in Directory.EnumerateFiles(Path.Combine(cacheRoot, currentVersionFolder), "*" + TempSuffix))
            {
                TryDelete(leftover);
            }
        }
        catch
        {
            // The sweep is housekeeping; it must never fail a resolve.
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort.
        }
    }
}
