namespace G9MAUIControls.Storage;

/// <summary>
///     The small amount of state the controls persist, and the seam for deciding where it goes.
///     <para>
///         There are exactly two consumers, and it is worth knowing what they are before
///         replacing this:
///     </para>
///     <list type="bullet">
///         <item>
///             <b>The chosen theme</b> (<c>G9Theme</c>) — so the app reopens in the theme the user
///             picked rather than flashing the system one first.
///         </item>
///         <item>
///             <b>Learned bottom-sheet heights</b> — a fit-to-content sheet measures its body on
///             first open; remembering the result is what lets the <i>second</i> open animate
///             straight to the right height instead of settling into it.
///         </item>
///     </list>
///     <para>
///         The default is MAUI <see cref="Microsoft.Maui.Storage.Preferences" />, which is right
///         for almost everyone. Replace it when the app has its own settings store it wants these
///         keys inside, or when a test needs them in memory.
///     </para>
///     <example>
///         <code>
///         G9Preferences.Store = new MyAppSettingsStore();   // implements IG9PreferenceStore
///         </code>
///     </example>
/// </summary>
public static class G9Preferences
{
    /// <summary>
    ///     Prefix on every key the controls write, so they are identifiable in a shared store and
    ///     can never collide with an app key.
    /// </summary>
    public const string KeyPrefix = "g9controls.";

    /// <summary>
    ///     Where control state is persisted. Defaults to MAUI
    ///     <see cref="Microsoft.Maui.Storage.Preferences" />.
    /// </summary>
    public static IG9PreferenceStore Store { get; set; } = new G9MauiPreferenceStore();

    /// <summary>Reads an <see cref="int" />, or <paramref name="defaultValue" /> when absent.</summary>
    public static int GetInt(string key, int defaultValue = 0) => Store.GetInt(KeyPrefix + key, defaultValue);

    /// <summary>Writes an <see cref="int" />.</summary>
    public static void SetInt(string key, int value) => Store.SetInt(KeyPrefix + key, value);

    /// <summary>Reads a <see cref="string" />, or <paramref name="defaultValue" /> when absent.</summary>
    public static string? GetString(string key, string? defaultValue = null) => Store.GetString(KeyPrefix + key, defaultValue);

    /// <summary>Writes a <see cref="string" />.</summary>
    public static void SetString(string key, string? value) => Store.SetString(KeyPrefix + key, value);

    /// <summary>Removes a key.</summary>
    public static void Remove(string key) => Store.Remove(KeyPrefix + key);
}

/// <summary>Where <see cref="G9Preferences" /> reads and writes. Keys arrive already prefixed.</summary>
public interface IG9PreferenceStore
{
    /// <summary>Reads an <see cref="int" />, or <paramref name="defaultValue" /> when absent.</summary>
    int GetInt(string key, int defaultValue);

    /// <summary>Writes an <see cref="int" />.</summary>
    void SetInt(string key, int value);

    /// <summary>Reads a <see cref="string" />, or <paramref name="defaultValue" /> when absent.</summary>
    string? GetString(string key, string? defaultValue);

    /// <summary>Writes a <see cref="string" />. A <c>null</c> value removes the key.</summary>
    void SetString(string key, string? value);

    /// <summary>Removes a key. A key that is not present is not an error.</summary>
    void Remove(string key);
}

/// <summary>
///     The default store: MAUI <see cref="Microsoft.Maui.Storage.Preferences" />.
///     <para>
///         Every call is guarded. Preferences touches platform storage, and a control's visual
///         pass must not be able to fault because a device's shared-prefs file is momentarily
///         unreadable — a forgotten sheet height is not worth a crash.
///     </para>
/// </summary>
public sealed class G9MauiPreferenceStore : IG9PreferenceStore
{
    /// <inheritdoc />
    public int GetInt(string key, int defaultValue)
    {
        try { return Preferences.Default.Get(key, defaultValue); }
        catch (Exception) { return defaultValue; }
    }

    /// <inheritdoc />
    public void SetInt(string key, int value)
    {
        try { Preferences.Default.Set(key, value); }
        catch (Exception) { /* non-essential state; see class remarks */ }
    }

    /// <inheritdoc />
    public string? GetString(string key, string? defaultValue)
    {
        try { return Preferences.Default.Get(key, defaultValue); }
        catch (Exception) { return defaultValue; }
    }

    /// <inheritdoc />
    public void SetString(string key, string? value)
    {
        try
        {
            if (value is null) { Preferences.Default.Remove(key); }
            else { Preferences.Default.Set(key, value); }
        }
        catch (Exception) { /* non-essential state; see class remarks */ }
    }

    /// <inheritdoc />
    public void Remove(string key)
    {
        try { Preferences.Default.Remove(key); }
        catch (Exception) { /* non-essential state; see class remarks */ }
    }
}

/// <summary>An in-memory store. Intended for tests and for opting persistence out entirely.</summary>
public sealed class G9InMemoryPreferenceStore : IG9PreferenceStore
{
    private readonly Dictionary<string, object?> _values = [];

    /// <inheritdoc />
    public int GetInt(string key, int defaultValue) =>
        _values.TryGetValue(key, out var v) && v is int i ? i : defaultValue;

    /// <inheritdoc />
    public void SetInt(string key, int value) => _values[key] = value;

    /// <inheritdoc />
    public string? GetString(string key, string? defaultValue) =>
        _values.TryGetValue(key, out var v) && v is string s ? s : defaultValue;

    /// <inheritdoc />
    public void SetString(string key, string? value)
    {
        if (value is null) { _values.Remove(key); }
        else { _values[key] = value; }
    }

    /// <inheritdoc />
    public void Remove(string key) => _values.Remove(key);
}
