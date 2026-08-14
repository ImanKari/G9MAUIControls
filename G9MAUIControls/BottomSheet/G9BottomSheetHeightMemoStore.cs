using System.Globalization;
using G9MAUIControls.Storage;

namespace G9MAUIControls.BottomSheet;

/// <summary>
///     The bottom-sheet height memo: settled fit-to-content BODY heights keyed by body identity +
///     width + culture + font scale, persisted across app restarts so even the FIRST open of a
///     sheet per session starts at (near) its real height instead of the loading floor.
///     <para>
///         <b>Safety model:</b> a memo value is only ever consumed as the loading-placeholder /
///         opening height — the fit engine still measures the real content once it is loaded and
///         corrects with an ANIMATED resize. A stale or wrong entry therefore degrades to exactly
///         the no-memo behavior (open at a plausible height, animate to the real one); it can
///         never strand a sheet at a wrong size. That is what makes persistence safe.
///     </para>
///     <para>
///         <b>Staleness guards:</b> the persisted blob is stamped with the app version and
///         discarded wholesale on mismatch (layout changes between versions are the systematic
///         staleness source). Width-dp, culture, and the platform font scale are part of each
///         entry key, so device-metric changes miss the cache instead of hitting a wrong value.
///         Values are most-recent-wins; sheets whose height varies by SITUATION (permissions,
///         item counts) bake the variable into <c>G9BottomSheetOptions.HeightMemoKey</c> — the
///         store itself stays dumb on purpose.
///     </para>
///     <para>
///         <b>First install:</b> a key with no learned entry falls back to the compiled
///         <see cref="G9BottomSheetHeightSeeds" /> table (read-only, never persisted), so the very
///         first open of a known sheet also starts near-size.
///     </para>
///     <para>
///         <b>Performance:</b> lazily loaded on the first sheet open (one Preferences read + a
///         tiny parse), never on the startup path. Writes are debounced (<see cref="FlushDelayMs" />)
///         and serialize at most <see cref="MaxEntries" /> lines on the main thread — microseconds —
///         with the actual disk write handled by the platform Preferences implementation.
///         Heights are DEVICE-specific (see the storage key's <c>includeInBackup: false</c>): text
///         metrics differ across platforms and wrap points differ across widths/font scales, so
///         the memo is intentionally never transported to another device.
///     </para>
///     <para><b>Threading:</b> main-thread only, like the sheet engine that calls it.</para>
/// </summary>
internal static class G9BottomSheetHeightMemoStore
{
    private const string MemoPreferenceKey = "sheet-heights";
    private const int MaxEntries = 64;
    private const int FlushDelayMs = 3000;
    private const string HeaderPrefix = "v1\t";
    private const char LineSeparator = '\n';
    private const char FieldSeparator = '\t';

    private static readonly Dictionary<string, double> Entries = new(StringComparer.Ordinal);
    private static bool _loaded;
    private static bool _flushScheduled;

    /// <summary>
    ///     Returns the remembered BODY height for a memo key, when one exists. A key never seen on
    ///     this device falls back to the compiled <see cref="G9BottomSheetHeightSeeds" /> so the
    ///     first-ever open still starts near-size; the fallback is read-only, and a learned height
    ///     wins from the moment the first real measurement is recorded.
    /// </summary>
    public static bool TryGet(string key, out double bodyHeight)
    {
        EnsureLoaded();
        return Entries.TryGetValue(key, out bodyHeight) ||
            G9BottomSheetHeightSeeds.TryGet(key, out bodyHeight);
    }

    /// <summary>
    ///     Records a settled BODY height (most-recent-wins) and schedules a debounced persist.
    /// </summary>
    public static void Record(string key, double bodyHeight)
    {
        if (bodyHeight <= 0)
        {
            return;
        }

        EnsureLoaded();

        if (Entries.TryGetValue(key, out var existing) && Math.Abs(existing - bodyHeight) < 0.5)
        {
            return; // unchanged — no rewrite, no flush
        }

        if (Entries.Count >= MaxEntries && !Entries.ContainsKey(key))
        {
            Entries.Clear();
        }

        Entries[key] = bodyHeight;
        ScheduleFlush();
    }

    /// <summary>
    ///     The platform font-scale component for memo keys. Text heights scale with the OS
    ///     accessibility text size, which can change BETWEEN sessions without an app update — so
    ///     it must be part of the persisted key, not just assumed stable.
    /// </summary>
    public static string ResolveFontScaleKeyComponent()
    {
#if ANDROID
        var scale = Android.App.Application.Context.Resources?.Configuration?.FontScale ?? 1f;
        return scale.ToString("0.##", CultureInfo.InvariantCulture);
#elif IOS || MACCATALYST
        return UIKit.UIApplication.SharedApplication.PreferredContentSizeCategory.ToString();
#else
        return "1";
#endif
    }

    private static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;

        var blob = G9Preferences.GetString(MemoPreferenceKey);
        if (string.IsNullOrEmpty(blob))
        {
            return;
        }

        var lines = blob.Split(LineSeparator);
        if (lines.Length == 0 || lines[0] != BuildHeader())
        {
            // Different app version (or unknown format) — the layouts may have changed, so the
            // whole memo is discarded rather than risking systematically-stale guesses.
            G9Preferences.Remove(MemoPreferenceKey);
            return;
        }

        for (var i = 1; i < lines.Length && Entries.Count < MaxEntries; i++)
        {
            var separatorIndex = lines[i].LastIndexOf(FieldSeparator);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = lines[i][..separatorIndex];
            if (double.TryParse(lines[i][(separatorIndex + 1)..], NumberStyles.Float, CultureInfo.InvariantCulture, out var height) &&
                height > 0)
            {
                Entries[key] = height;
            }
        }
    }

    private static void ScheduleFlush()
    {
        if (_flushScheduled)
        {
            return;
        }

        _flushScheduled = true;
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Task.Delay(FlushDelayMs).ConfigureAwait(true);
            _flushScheduled = false;
            Flush();
        });
    }

    private static void Flush()
    {
        // ≤ 64 short lines — serialization is microseconds; the platform Preferences write is
        // handled by the OS (asynchronous apply() on Android).
        var builder = new System.Text.StringBuilder(Entries.Count * 48 + 32);
        builder.Append(BuildHeader());
        foreach (var entry in Entries)
        {
            builder.Append(LineSeparator)
                .Append(entry.Key)
                .Append(FieldSeparator)
                .Append(entry.Value.ToString("0.#", CultureInfo.InvariantCulture));
        }

        G9Preferences.SetString(MemoPreferenceKey, builder.ToString());
    }

    private static string BuildHeader()
    {
        var info = AppInfo.Current;
        return $"{HeaderPrefix}{info.VersionString}+{info.BuildString}";
    }
}
