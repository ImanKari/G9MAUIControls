using G9MAUIControls.Storage;

namespace G9MAUIControls.Theming;

public static class G9Theme
{
    /// <summary>
    ///     Where the user's theme choice is persisted, through <see cref="G9Preferences" /> so the
    ///     app can redirect it to its own settings store. Reading it at startup is what stops the
    ///     app flashing the system theme before applying the chosen one.
    /// </summary>
    private const string ThemePreferenceKey = "theme";

    private static ResourceDictionary? _activeTheme;

    /// <summary>
    ///     Applies the persisted (or system) theme and starts following system theme changes.
    ///     <para>
    ///         <b>Call this from your <c>App</c> constructor, after <c>InitializeComponent()</c></b> — not from
    ///         <c>MauiProgram.CreateMauiApp</c>. Both halves of that matter:
    ///     </para>
    ///     <list type="bullet">
    ///         <item>
    ///             <b>After an <see cref="Application" /> exists.</b> This method pushes ~110 palette tokens
    ///             into <c>Application.Current.Resources</c> and subscribes to
    ///             <see cref="Application.RequestedThemeChanged" />. During <c>CreateMauiApp</c> there is no
    ///             Application yet — nowhere to push and nothing to subscribe to.
    ///         </item>
    ///         <item>
    ///             <b>After <c>InitializeComponent()</c>.</b> That is what merges
    ///             <c>G9PageTemplate</c> and the theme dictionary; applying a palette before the dictionaries
    ///             are merged replaces nothing.
    ///         </item>
    ///     </list>
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     No <see cref="Application" /> exists yet. Thrown deliberately, and early: the failure this
    ///     replaces was a <see cref="NullReferenceException" /> from deep inside
    ///     <see cref="ApplyCurrent" />, whose stack said nothing about the call being in the wrong place.
    /// </exception>
    public static void Init()
    {
        var application = RequireApplication(nameof(Init));

        // Seed the three resource keys the suite's XAML binds with DynamicResource. Unlike the C# metric
        // readers, DynamicResource has no fallback — an absent key silently leaves the property unset, so
        // a consumer who never heard of these keys would get zero corner radii and zero margins with
        // nothing in the build output to explain it. Existing values are preserved.
        G9LayoutMetrics.InstallDefaults(application);

        ApplyCurrent();

        application.RequestedThemeChanged += (_, e) =>
        {
            // Only follow the system while the user has expressed no preference of their own.
            if (application.UserAppTheme == AppTheme.Unspecified && e.RequestedTheme != AppTheme.Unspecified)
            {
                ApplyCurrent();
            }
        };
    }

    /// <summary>
    ///     The one place that turns "no Application yet" into a message naming the fix.
    ///     <para>
    ///         Every entry point here dereferenced <c>Application.Current</c> through a null-forgiving
    ///         <c>!</c>, which is a claim, not a check. On Android the claim is false for the whole of
    ///         <c>MauiApplication.OnCreate</c> — so the suite's most natural-looking wiring mistake produced
    ///         a <see cref="NullReferenceException" /> whose stack pointed at theming internals rather than
    ///         at the call site that was wrong. See LES-0016.
    ///     </para>
    /// </summary>
    private static Application RequireApplication(string member) =>
        Application.Current
        ?? throw new InvalidOperationException(
            $"G9Theme.{member} was called before an Application existed. Call G9Theme.Init() from your App "
            + "constructor, after InitializeComponent() — not from MauiProgram.CreateMauiApp, where "
            + "Application.Current is still null. See AiGuides/06-AndroidDebugLoop.md §4.1.");

    public static void ChangeTheme(AppTheme theme)
    {
        G9Preferences.SetInt(ThemePreferenceKey, (int)theme);
        ApplyCurrent();
    }

    /// <summary>
    ///     Re-applies whatever theme is currently selected. Safe to call at any time after
    ///     <see cref="Init" />.
    /// </summary>
    /// <exception cref="InvalidOperationException">No <see cref="Application" /> exists yet.</exception>
    public static void ApplyCurrent()
    {
        var application = RequireApplication(nameof(ApplyCurrent));
        var theme = (AppTheme)G9Preferences.GetInt(ThemePreferenceKey);

        application.UserAppTheme = theme;

        ResourceDictionary dict =
            theme != AppTheme.Unspecified
            ? theme == AppTheme.Dark
                ? new G9ThemeDark()
                : new G9ThemeLight()
            : application.RequestedTheme == AppTheme.Dark
                ? new G9ThemeDark()
                : new G9ThemeLight();

        ReplaceMergedDictionary(dict);
        ApplyPaletteFromDictionary(dict);
    }

    /// <summary>
    ///     Applies colors from a freshly-built theme <see cref="ResourceDictionary" />
    ///     onto the singleton <see cref="G9Palette.Current" />.
    ///     <para>
    ///         Wraps the ~100 colour setters in
    ///         <see cref="G9Palette.BeginBatchUpdate" /> /
    ///         <see cref="G9Palette.EndBatchUpdate" /> so that consumers see a
    ///         single <c>PropertyChanged(string.Empty)</c> "all properties changed"
    ///         event at the end instead of N individual events. The dense input
    ///         showcase blocked the UI thread for over 240 seconds without this
    ///         batching because every per-property event fanned out to every
    ///         binding listening on the palette.
    ///     </para>
    /// </summary>
    private static void ApplyPaletteFromDictionary(ResourceDictionary dict)
    {
        var p = G9Palette.Current;

        p.BeginBatchUpdate();
        try
        {
            ApplyPaletteFromDictionaryCore(p, dict);
        }
        finally
        {
            p.EndBatchUpdate();
        }
    }

    private static void ApplyPaletteFromDictionaryCore(G9Palette p, ResourceDictionary dict)
    {
        // ================= PRIMARY =================
        p.Primary = GetColor(dict, nameof(G9ColorToken.Primary));
        p.OnPrimary = GetColor(dict, nameof(G9ColorToken.OnPrimary));
        p.PrimaryContainer = GetColor(dict, nameof(G9ColorToken.PrimaryContainer));
        p.OnPrimaryContainer = GetColor(dict, nameof(G9ColorToken.OnPrimaryContainer));
        p.PrimaryBorder = GetColor(dict, nameof(G9ColorToken.PrimaryBorder));
        p.PrimaryHover = GetColor(dict, nameof(G9ColorToken.PrimaryHover));
        p.PrimaryPressed = GetColor(dict, nameof(G9ColorToken.PrimaryPressed));

        // ================= SECONDARY =================
        p.Secondary = GetColor(dict, nameof(G9ColorToken.Secondary));
        p.OnSecondary = GetColor(dict, nameof(G9ColorToken.OnSecondary));
        p.SecondaryContainer = GetColor(dict, nameof(G9ColorToken.SecondaryContainer));
        p.OnSecondaryContainer = GetColor(dict, nameof(G9ColorToken.OnSecondaryContainer));
        p.SecondaryBorder = GetColor(dict, nameof(G9ColorToken.SecondaryBorder));

        // ================= TERTIARY =================
        p.Tertiary = GetColor(dict, nameof(G9ColorToken.Tertiary));
        p.OnTertiary = GetColor(dict, nameof(G9ColorToken.OnTertiary));
        p.TertiaryContainer = GetColor(dict, nameof(G9ColorToken.TertiaryContainer));
        p.OnTertiaryContainer = GetColor(dict, nameof(G9ColorToken.OnTertiaryContainer));
        p.TertiaryBorder = GetColor(dict, nameof(G9ColorToken.TertiaryBorder));

        // ================= QUATERNARY =================
        p.Quaternary = GetColor(dict, nameof(G9ColorToken.Quaternary));
        p.OnQuaternary = GetColor(dict, nameof(G9ColorToken.OnQuaternary));
        p.QuaternaryContainer = GetColor(dict, nameof(G9ColorToken.QuaternaryContainer));
        p.OnQuaternaryContainer = GetColor(dict, nameof(G9ColorToken.OnQuaternaryContainer));
        p.QuaternaryBorder = GetColor(dict, nameof(G9ColorToken.QuaternaryBorder));

        // ================= ERROR =================
        p.Error = GetColor(dict, nameof(G9ColorToken.Error));
        p.OnError = GetColor(dict, nameof(G9ColorToken.OnError));
        p.ErrorContainer = GetColor(dict, nameof(G9ColorToken.ErrorContainer));
        p.OnErrorContainer = GetColor(dict, nameof(G9ColorToken.OnErrorContainer));
        p.ErrorBorder = GetColor(dict, nameof(G9ColorToken.ErrorBorder));

        // ================= WARNING =================
        p.Warning = GetColor(dict, nameof(G9ColorToken.Warning));
        p.OnWarning = GetColor(dict, nameof(G9ColorToken.OnWarning));
        p.WarningContainer = GetColor(dict, nameof(G9ColorToken.WarningContainer));
        p.OnWarningContainer = GetColor(dict, nameof(G9ColorToken.OnWarningContainer));
        p.WarningBorder = GetColor(dict, nameof(G9ColorToken.WarningBorder));

        // ================= SUCCESS =================
        p.Success = GetColor(dict, nameof(G9ColorToken.Success));
        p.OnSuccess = GetColor(dict, nameof(G9ColorToken.OnSuccess));
        p.SuccessContainer = GetColor(dict, nameof(G9ColorToken.SuccessContainer));
        p.OnSuccessContainer = GetColor(dict, nameof(G9ColorToken.OnSuccessContainer));
        p.SuccessBorder = GetColor(dict, nameof(G9ColorToken.SuccessBorder));

        // ================= INFO =================
        p.Info = GetColor(dict, nameof(G9ColorToken.Info));
        p.OnInfo = GetColor(dict, nameof(G9ColorToken.OnInfo));
        p.InfoContainer = GetColor(dict, nameof(G9ColorToken.InfoContainer));
        p.OnInfoContainer = GetColor(dict, nameof(G9ColorToken.OnInfoContainer));
        p.InfoBorder = GetColor(dict, nameof(G9ColorToken.InfoBorder));

        // ================= OUTLINE =================
        p.Outline = GetColor(dict, nameof(G9ColorToken.Outline));
        p.OutlineVariant = GetColor(dict, nameof(G9ColorToken.OutlineVariant));
        p.OutlineBorder = GetColor(dict, nameof(G9ColorToken.OutlineBorder));

        // ================= BACKGROUND =================
        p.Background = GetColor(dict, nameof(G9ColorToken.Background));
        p.OnBackground = GetColor(dict, nameof(G9ColorToken.OnBackground));
        p.BackgroundSecondary = GetColor(dict, nameof(G9ColorToken.BackgroundSecondary));

        // ================= SURFACE =================
        p.Surface = GetColor(dict, nameof(G9ColorToken.Surface));
        p.OnSurface = GetColor(dict, nameof(G9ColorToken.OnSurface));
        p.SurfaceVariant = GetColor(dict, nameof(G9ColorToken.SurfaceVariant));
        p.OnSurfaceVariant = GetColor(dict, nameof(G9ColorToken.OnSurfaceVariant));
        p.SurfaceBorder = GetColor(dict, nameof(G9ColorToken.SurfaceBorder));

        // ================= INVERSE =================
        p.InverseSurface = GetColor(dict, nameof(G9ColorToken.InverseSurface));
        p.InverseOnSurface = GetColor(dict, nameof(G9ColorToken.InverseOnSurface));
        p.InversePrimary = GetColor(dict, nameof(G9ColorToken.InversePrimary));

        // ================= SURFACE CONTAINERS =================
        p.SurfaceContainerHighest = GetColor(dict, nameof(G9ColorToken.SurfaceContainerHighest));
        p.SurfaceContainerHigh = GetColor(dict, nameof(G9ColorToken.SurfaceContainerHigh));
        p.SurfaceContainer = GetColor(dict, nameof(G9ColorToken.SurfaceContainer));
        p.SurfaceContainerLow = GetColor(dict, nameof(G9ColorToken.SurfaceContainerLow));
        p.SurfaceContainerLowest = GetColor(dict, nameof(G9ColorToken.SurfaceContainerLowest));
        p.SurfaceBright = GetColor(dict, nameof(G9ColorToken.SurfaceBright));
        p.SurfaceDim = GetColor(dict, nameof(G9ColorToken.SurfaceDim));

        // ================= SCRIM & OVERLAY =================
        p.Scrim = GetColor(dict, nameof(G9ColorToken.Scrim));
        p.ScrimLight = GetColor(dict, nameof(G9ColorToken.ScrimLight));
        p.Overlay = GetColor(dict, nameof(G9ColorToken.Overlay));
        p.OverlayLight = GetColor(dict, nameof(G9ColorToken.OverlayLight));

        // ================= TEXT COLORS =================
        p.TextPrimary = GetColor(dict, nameof(G9ColorToken.TextPrimary));
        p.TextSecondary = GetColor(dict, nameof(G9ColorToken.TextSecondary));
        p.TextTertiary = GetColor(dict, nameof(G9ColorToken.TextTertiary));
        p.TextDisabled = GetColor(dict, nameof(G9ColorToken.TextDisabled));

        // ================= STANDARD COLORS =================
        p.White = GetColor(dict, nameof(G9ColorToken.White));
        p.Black = GetColor(dict, nameof(G9ColorToken.Black));
        p.Light = GetColor(dict, nameof(G9ColorToken.Light));
        p.Dark = GetColor(dict, nameof(G9ColorToken.Dark));

        // ================= DIVIDER =================
        p.Divider = GetColor(dict, nameof(G9ColorToken.Divider));
        p.DividerStrong = GetColor(dict, nameof(G9ColorToken.DividerStrong));

        // ================= FOCUS & STATES =================
        p.Focus = GetColor(dict, nameof(G9ColorToken.Focus));
        p.FocusVisible = GetColor(dict, nameof(G9ColorToken.FocusVisible));
        p.Disabled = GetColor(dict, nameof(G9ColorToken.Disabled));
        p.DisabledContainer = GetColor(dict, nameof(G9ColorToken.DisabledContainer));

        // ================= CARD =================
        p.CardBackground = GetColor(dict, nameof(G9ColorToken.CardBackground));
        p.CardBorder = GetColor(dict, nameof(G9ColorToken.CardBorder));
        p.CardElevated = GetColor(dict, nameof(G9ColorToken.CardElevated));

        // ================= DEFAULT (non-primary button) =================
        p.Default = GetColor(dict, nameof(G9ColorToken.Default));
        p.OnDefault = GetColor(dict, nameof(G9ColorToken.OnDefault));
        p.DefaultContainer = GetColor(dict, nameof(G9ColorToken.DefaultContainer));
        p.OnDefaultContainer = GetColor(dict, nameof(G9ColorToken.OnDefaultContainer));
        p.DefaultBorder = GetColor(dict, nameof(G9ColorToken.DefaultBorder));
        p.DefaultHover = GetColor(dict, nameof(G9ColorToken.DefaultHover));
        p.DefaultPressed = GetColor(dict, nameof(G9ColorToken.DefaultPressed));

        // ================= INPUT =================
        p.InputBackground = GetColor(dict, nameof(G9ColorToken.InputBackground));
        p.InputBorder = GetColor(dict, nameof(G9ColorToken.InputBorder));
        p.InputBorderFocused = GetColor(dict, nameof(G9ColorToken.InputBorderFocused));
        p.InputPlaceholder = GetColor(dict, nameof(G9ColorToken.InputPlaceholder));
    }

    private static Color GetColor(ResourceDictionary dict, string key)
    {
#if DEBUG
        if (!dict.TryGetValue(key, out var value))
        {
            throw new KeyNotFoundException(
                $"Theme color '{key}' not found in dictionary. Did you forget to add it to the XAML theme file?");
        }
#else
        if (!dict.TryGetValue(key, out var value))
            return Colors.Magenta; // Fallback
#endif

        return value is Color c ? c : Colors.Magenta;
    }

    private static void ReplaceMergedDictionary(ResourceDictionary dict)
    {
        // Through the same guard as the public entry points. Its only caller validates already, so this is
        // belt-and-braces — but it is the last `Application.Current!` in the file, and leaving one behind is
        // how the hole gets reintroduced by a future caller who reasonably assumes it was checked.
        var merged = RequireApplication(nameof(ReplaceMergedDictionary)).Resources.MergedDictionaries;

        // Remove EVERY theme dictionary present, not just the one we remember adding.
        //
        // Consumers are told to merge G9ThemeLight (or Dark) in App.xaml — that is how the baseline gets
        // established before any page exists. Tracking only `_activeTheme` meant the very first
        // ApplyCurrent() had nothing to remove and simply ADDED a second theme dictionary on top of the
        // consumer's. Both then stayed merged for the life of the app, and the two colour paths disagreed:
        // XAML `{theme:G9Color …}` resolved through MergedDictionaries (whichever won) while G9Palette was
        // populated from the dictionary we had just built. The visible result was a dark page with white
        // cards and near-invisible secondary text — a palette that is half light and half dark, with nothing
        // wrong in either dictionary. Found by launching on a device; see LES-0018.
        // MergedDictionaries is ICollection<ResourceDictionary> — no indexer — so collect first, then remove.
        var stale = merged.Where(static d => d is G9ThemeLight or G9ThemeDark).ToList();
        foreach (var old in stale)
        {
            merged.Remove(old);
        }

        merged.Add(dict);
        _activeTheme = dict;
    }
}
