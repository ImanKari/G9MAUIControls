using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace G9MAUIControls.Icons;

/// <summary>
///     The registry of icon fonts this app has, and the by-<b>name</b> lookup that makes an
///     icon decided at runtime resolvable.
///     <para>
///         Registration exists for two reasons, and only two. First, so XAML can write
///         <c>Icon="MyIcons.Valve"</c> or even <c>Icon="Valve"</c> instead of a
///         <c>{x:Static}</c> ceremony. Second, so a <b>name that arrives at runtime</b> — from
///         an API response, a config file, a database row — can become a glyph. Code that
///         already has the enum member in hand never needs to register anything: the implicit
///         conversion on <see cref="G9IconSource" /> handles it.
///     </para>
///     <para>
///         <b>Resolve raw names, never <c>Enum.Parse</c>.</b> Icon fonts routinely contain
///         names that collide once they are PascalCased — <c>hour-glass</c> and <c>hourglass</c>
///         are different glyphs in more than one popular set, and a case-insensitive
///         <c>Enum.Parse</c> conflates them into whichever member it happens to hit. This
///         registry keys on the <b>raw</b> name for exactly that reason, and only falls back to
///         the member name when the raw name has no entry. An icon that renders a plausible but
///         wrong glyph is the single hardest icon bug to notice.
///     </para>
///     <example>
///         <code>
///         // MauiProgram
///         builder.ConfigureFonts(fonts => fonts.AddFont("my-icons.ttf", nameof(MyIcons)));
///
///         // Register the enum, and teach it the font's own raw names where they differ
///         // from the member names.
///         G9IconFonts.Register&lt;MyIcons&gt;(isDefault: true);
///         G9IconFonts.RegisterName("hour-glass", MyIcons.HourGlassEae7);
///
///         // Later, from data:
///         chip.Icon = G9IconFonts.Resolve(dto.IconName) ?? G9Glyph.Info;
///         </code>
///     </example>
/// </summary>
public static class G9IconFonts
{
    private static readonly Dictionary<string, G9IconSource> ByName = new(StringComparer.OrdinalIgnoreCase);
    // Per-font member maps, SNAPSHOT at registration — deliberately not the Type.
    //
    // Storing the Type made the resolve path reflect over it (`Type.GetField`), and a Type retrieved from a
    // dictionary carries no [DynamicallyAccessedMembers] annotation, so the trimmer could not prove the
    // enum's fields were needed: IL2067 + IL2070 under `AndroidLinkMode=Full`. Suppressing them would have
    // been a lie — under a full trim the fields really can go, and by-name icon resolution really would
    // start returning null in release only, which is close to the worst failure mode this library has.
    //
    // Register<TEnum>() already enumerates the members inside a generic context where TEnum's fields ARE
    // statically known and rooted. Capturing the result there means Resolve does dictionary lookups and no
    // reflection at all — trim-safe by construction rather than by annotation, and faster besides.
    private static readonly Dictionary<string, Dictionary<string, G9IconSource>> ByFontAlias =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock Gate = new();

    private static string? _defaultFontAlias;

    /// <summary>
    ///     The font alias used when a name is resolved without a <c>Font.Member</c> qualifier.
    ///     Set by passing <c>isDefault: true</c> to <see cref="Register{TEnum}" />.
    /// </summary>
    public static string? DefaultFontAlias
    {
        get { lock (Gate) { return _defaultFontAlias; } }
    }

    /// <summary>
    ///     Registers an icon-font enum so its members can be resolved by name.
    /// </summary>
    /// <typeparam name="TEnum">
    ///     The icon enum. Its <b>type name</b> is the font alias, so it must match the alias
    ///     passed to <c>fonts.AddFont(file, alias)</c> — <c>nameof(TEnum)</c> is the safe way
    ///     to write that call.
    /// </typeparam>
    /// <param name="isDefault">
    ///     When true, unqualified names (<c>"Valve"</c> rather than <c>"MyIcons.Valve"</c>)
    ///     resolve against this font. The last registration with <c>true</c> wins.
    /// </param>
    public static void Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicFields)] TEnum>(
        bool isDefault = false)
        where TEnum : struct, Enum
    {
        var type = typeof(TEnum);

        lock (Gate)
        {
            var members = new Dictionary<string, G9IconSource>(StringComparer.OrdinalIgnoreCase);
            ByFontAlias[type.Name] = members;
            if (isDefault)
            {
                _defaultFontAlias = type.Name;
            }

            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is not Enum member)
                {
                    continue;
                }

                var icon = G9IconSource.FromEnum(member);

                // This font's own member map. Keyed per font, so a member name shared by two fonts stays
                // resolvable through each of them — which is what the default-font path below relies on.
                members[field.Name] = icon;

                // Qualified key always wins its own slot; unqualified key is first-come so an
                // earlier font is not silently shadowed by a later one.
                ByName[$"{type.Name}.{field.Name}"] = icon;
                ByName.TryAdd(field.Name, icon);
            }
        }
    }

    /// <summary>
    ///     Maps one additional raw name onto an already-known glyph — the font's own name, as
    ///     the designer exported it, when it differs from the C# member name.
    ///     <para>
    ///         This is what keeps <c>hour-glass</c> and <c>hourglass</c> distinct. Call it for
    ///         every name a server or config file might send that is not already a member name.
    ///     </para>
    /// </summary>
    public static void RegisterName(string rawName, G9IconSource icon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawName);
        lock (Gate)
        {
            ByName[rawName] = icon;
        }
    }

    /// <summary>
    ///     Resolves a name to an icon, or <c>null</c> when nothing matches.
    ///     <para>Accepted forms, tried in this order:</para>
    ///     <list type="number">
    ///         <item>an exact registered raw name (<c>"hour-glass"</c>, <c>"MyIcons.Valve"</c>);</item>
    ///         <item>a built-in <see cref="G9Glyph" /> member name (<c>"Search"</c>);</item>
    ///         <item>
    ///             a <c>Font.Member</c> pair whose font is registered, resolved through the enum
    ///             even if that member was added after registration;
    ///         </item>
    ///         <item>an unqualified member of <see cref="DefaultFontAlias" />.</item>
    ///     </list>
    /// </summary>
    public static G9IconSource? Resolve(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        name = name.Trim();

        lock (Gate)
        {
            if (ByName.TryGetValue(name, out var direct))
            {
                return direct;
            }

            if (Enum.TryParse<G9Glyph>(name, ignoreCase: true, out var builtIn) && builtIn != G9Glyph.None)
            {
                return G9IconSource.FromGlyph(builtIn);
            }

            var dot = name.LastIndexOf('.');
            if (dot > 0 && dot < name.Length - 1)
            {
                var alias = name[..dot];
                var member = name[(dot + 1)..];
                if (ByFontAlias.TryGetValue(alias, out var aliasMembers) &&
                    aliasMembers.TryGetValue(member, out var qualified))
                {
                    return qualified;
                }
            }

            if (_defaultFontAlias is not null &&
                ByFontAlias.TryGetValue(_defaultFontAlias, out var defaultMembers) &&
                defaultMembers.TryGetValue(name, out var fromDefault))
            {
                return fromDefault;
            }
        }

        return null;
    }

    /// <summary>
    ///     <see cref="Resolve(string)" /> with a fallback, for the common "render something sensible
    ///     rather than nothing" case.
    /// </summary>
    public static G9IconSource Resolve(string? name, G9IconSource fallback) => Resolve(name) ?? fallback;

    /// <summary>Forgets every registration. Intended for tests.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            ByName.Clear();
            ByFontAlias.Clear();
            _defaultFontAlias = null;
        }
    }
}

/// <summary>
///     Lets XAML write an icon as plain text — <c>Icon="Search"</c>,
///     <c>Icon="MyIcons.Valve"</c>, <c>Icon="hour-glass"</c> — resolved through
///     <see cref="G9IconFonts.Resolve(string)" />.
///     <para>
///         An unresolvable value throws rather than rendering nothing. A blank icon slot in a
///         built app gives a developer no clue which of the dozen possible causes applies; a
///         XAML parse error names the exact attribute.
///     </para>
/// </summary>
public sealed class G9IconSourceTypeConverter : TypeConverter
{
    /// <inheritdoc />
    public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) =>
        sourceType == typeof(string);

    /// <inheritdoc />
    public override object? ConvertFrom(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is not string text)
        {
            return base.ConvertFrom(context, culture, value);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return G9IconSource.Empty;
        }

        return G9IconFonts.Resolve(text)
               ?? throw new FormatException(
                   $"'{text}' is not a known icon. Use a G9Glyph member (e.g. \"Search\"), " +
                   $"a registered font member (\"MyIcons.Valve\"), or a name registered with " +
                   $"G9IconFonts.RegisterName. Fonts are registered with G9IconFonts.Register<TEnum>().");
    }

    /// <inheritdoc />
    public override bool CanConvertTo(ITypeDescriptorContext? context, Type? destinationType) =>
        destinationType == typeof(string);

    /// <inheritdoc />
    public override object? ConvertTo(ITypeDescriptorContext? context, System.Globalization.CultureInfo? culture, object? value, Type destinationType) =>
        destinationType == typeof(string) && value is G9IconSource icon
            ? icon.ToString()
            : base.ConvertTo(context, culture, value, destinationType);
}
