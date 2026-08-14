using System.ComponentModel;
using System.Globalization;
using System.Reflection;

namespace G9MAUIControls.Icons;

/// <summary>
///     One icon, from any source, in one value type — the single currency every control's
///     icon slot accepts.
///     <para>
///         <b>Why a struct and not an enum.</b> A control library cannot know which icon font
///         its consumer uses. Typing the slots against a specific enum (as an earlier internal
///         version of these controls did, with one slot per font) welds the library to that
///         font, forces its package on every consumer, and needs a new slot on every control
///         each time somebody adds a second font. A value that carries
///         <i>(font family, glyph)</i> — or a built-in vector <see cref="G9Glyph" /> — makes
///         the slot font-agnostic, so a consumer's own font is a first-class citizen rather
///         than a special case.
///     </para>
///     <para>
///         <b>Four ways to produce one.</b>
///     </para>
///     <list type="number">
///         <item>
///             <b>Any enum, implicitly.</b> The font family is the enum's <i>type name</i> and
///             the glyph comes from its <see cref="DescriptionAttribute" /> — the same
///             convention icon-font enum generators already emit, so an existing icon enum works
///             with no adapter and no change. When there is no
///             <see cref="DescriptionAttribute" />, the numeric value is treated as the
///             code point, which covers hand-written <c>Foo = 0xE801</c> enums.
///         </item>
///         <item>
///             <b>A built-in glyph</b> — <c>G9Glyph.Search</c> — drawn as vector geometry with
///             no font at all.
///         </item>
///         <item><b>Explicitly</b> — <see cref="FromFont(string,string)" /> / <see cref="FromCodePoint" />.</item>
///         <item>
///             <b>By name</b> — <see cref="G9IconFonts.Resolve(string)" />, for names that arrive at
///             runtime (a server-supplied icon name, a config file).
///         </item>
///     </list>
///     <example>
///         <code>
///         // 1 — your own font enum, nothing to adapt
///         fonts.AddFont("my-icons.ttf", nameof(MyIcons));
///         button.LeadingIcon = MyIcons.Valve;
///
///         // 2 — a built-in, zero configuration
///         entry.TrailingIcon = G9Glyph.Search;
///
///         // 3 — explicit
///         card.Icon = G9IconSource.FromCodePoint("MyIcons", 0xE801);
///
///         // 4 — a name decided at runtime
///         chip.Icon = G9IconFonts.Resolve(dto.IconName);
///         </code>
///     </example>
///     <para>
///         In XAML the <see cref="G9IconSourceTypeConverter" /> accepts
///         <c>Icon="Search"</c> (a built-in or a member of the default font),
///         <c>Icon="MyIcons.Valve"</c> (a registered font), and
///         <c>Icon="hour-glass"</c> (a registered raw name).
///     </para>
/// </summary>
[TypeConverter(typeof(G9IconSourceTypeConverter))]
public readonly struct G9IconSource : IEquatable<G9IconSource>
{
    private G9IconSource(string? fontFamily, string? glyph, G9Glyph builtIn)
    {
        FontFamily = fontFamily;
        Glyph = glyph;
        BuiltIn = builtIn;
    }

    /// <summary>
    ///     The registered MAUI font alias to render <see cref="Glyph" /> with, or <c>null</c>
    ///     when this is a built-in vector glyph.
    /// </summary>
    public string? FontFamily { get; }

    /// <summary>
    ///     The glyph text (one character, or a surrogate pair above U+FFFF), or <c>null</c>
    ///     when this is a built-in vector glyph.
    /// </summary>
    public string? Glyph { get; }

    /// <summary>
    ///     The built-in vector glyph, or <see cref="G9Glyph.None" /> when this icon comes from
    ///     a font.
    /// </summary>
    public G9Glyph BuiltIn { get; }

    /// <summary>True when this value carries no icon at all.</summary>
    public bool IsEmpty => BuiltIn == G9Glyph.None && string.IsNullOrEmpty(Glyph);

    /// <summary>True when this icon is drawn as vector geometry rather than from a font.</summary>
    public bool IsBuiltIn => BuiltIn != G9Glyph.None;

    /// <summary>An icon value carrying nothing. Equivalent to <c>default</c>.</summary>
    public static G9IconSource Empty => default;

    /// <summary>A font glyph given as its literal text.</summary>
    /// <param name="fontFamily">The MAUI font alias registered via <c>fonts.AddFont(file, alias)</c>.</param>
    /// <param name="glyph">The glyph text — one char, or a surrogate pair for astral code points.</param>
    public static G9IconSource FromFont(string fontFamily, string glyph)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        ArgumentException.ThrowIfNullOrEmpty(glyph);
        return new G9IconSource(fontFamily, glyph, G9Glyph.None);
    }

    /// <summary>A font glyph given as a Unicode code point (e.g. <c>0xE801</c>).</summary>
    /// <param name="fontFamily">The MAUI font alias registered via <c>fonts.AddFont(file, alias)</c>.</param>
    /// <param name="codePoint">The code point. Values above U+FFFF become a surrogate pair.</param>
    public static G9IconSource FromCodePoint(string fontFamily, int codePoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fontFamily);
        return new G9IconSource(fontFamily, char.ConvertFromUtf32(codePoint), G9Glyph.None);
    }

    /// <summary>One of the library's built-in vector glyphs.</summary>
    public static G9IconSource FromGlyph(G9Glyph glyph) => new(null, null, glyph);

    /// <summary>
    ///     Reads an icon out of any enum member: family = the enum <b>type name</b>,
    ///     glyph = its <see cref="DescriptionAttribute" /> (or, absent one, its numeric value
    ///     read as a code point).
    ///     <para>
    ///         The type name is the family because that is the convention every icon-font enum
    ///         generator in this ecosystem already follows — it is why registering the font as
    ///         <c>fonts.AddFont("my.ttf", nameof(MyIcons))</c> is all the wiring a consumer
    ///         needs.
    ///     </para>
    /// </summary>
    /// <exception cref="ArgumentException">
    ///     The member's <see cref="DescriptionAttribute" /> is present but empty, so there is no
    ///     glyph to draw. A silently-empty icon slot is far harder to diagnose than a throw at
    ///     the assignment.
    /// </exception>
    public static G9IconSource FromEnum(Enum value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var type = value.GetType();
        var name = value.ToString();

        // Cache is keyed on the exact member so the reflection below happens once per glyph
        // per process, never per visual pass.
        if (EnumGlyphCache.TryGetValue((type, name), out var cached))
        {
            return cached;
        }

        string? glyph = type.GetField(name, BindingFlags.Public | BindingFlags.Static)
            ?.GetCustomAttribute<DescriptionAttribute>()
            ?.Description;

        if (glyph is not null && glyph.Length == 0)
        {
            throw new ArgumentException(
                $"Icon enum member '{type.Name}.{name}' has an empty [Description]; there is no glyph to render.",
                nameof(value));
        }

        // No [Description] → treat the member's numeric value as the code point. This is the
        // shape of a hand-written `Valve = 0xE801` enum.
        glyph ??= char.ConvertFromUtf32(Convert.ToInt32(value, CultureInfo.InvariantCulture));

        var resolved = new G9IconSource(type.Name, glyph, G9Glyph.None);
        EnumGlyphCache[(type, name)] = resolved;
        return resolved;
    }

    private static readonly Dictionary<(Type, string), G9IconSource> EnumGlyphCache = [];

    /// <summary>Any icon-font enum member becomes an icon. See <see cref="FromEnum" />.</summary>
    /// <remarks>
    ///     <b>Takes a NULLABLE enum, and <c>null</c> means <see cref="Empty" /> rather than throwing.</b>
    ///     Every icon slot in the suite is a nullable <c>G9IconSource?</c>, and the natural way to feed
    ///     one is a nullable lookup — <c>Icon = icons.ResolveOrNull(name)</c>, where <c>null</c> means
    ///     "this thing has no icon". Boxing a null <c>MyIcons?</c> to <see cref="Enum" /> yields null, so
    ///     a non-nullable parameter turned that ordinary case into an <see cref="ArgumentNullException" />
    ///     at paint time, and a nullable-warning at every call site telling the consumer to guard
    ///     something the slot already models. <see cref="Empty" /> is what the slot means by "nothing",
    ///     and <c>G9IconFactory.HasIcon</c> already treats it as absent.
    ///     <para>
    ///         <see cref="FromEnum" /> itself still throws on null — it is a direct call with a
    ///         non-nullable parameter, where null is a caller error rather than an expressed absence.
    ///     </para>
    /// </remarks>
    public static implicit operator G9IconSource(Enum? value) => value is null ? Empty : FromEnum(value);

    /// <summary>A built-in glyph becomes an icon.</summary>
    public static implicit operator G9IconSource(G9Glyph glyph) => FromGlyph(glyph);

    /// <summary>
    ///     An icon NAME becomes an icon, resolved through <see cref="G9IconFonts.Resolve(string)" />:
    ///     a built-in glyph (<c>"Search"</c>), a registered font member (<c>"MyIcons.Valve"</c>), or a
    ///     name registered with <see cref="G9IconFonts.RegisterName" />.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <b>This operator is what makes <c>Icon="Search"</c> compile in XAML, and it is not
    ///         redundant with <see cref="G9IconSourceTypeConverter" />.</b> Every icon slot in the suite
    ///         is declared <c>G9IconSource?</c>, and MAUI's XAML compiler looks for a
    ///         <see cref="System.ComponentModel.TypeConverterAttribute" /> on the PROPERTY's type — which
    ///         is <see cref="Nullable{T}" />, not <see cref="G9IconSource" />. It does not unwrap the
    ///         nullable, so the converter was never found and every string-valued icon attribute failed
    ///         to compile with <c>XC0009: No property, BindableProperty, or event found for "Icon", or
    ///         mismatching type between value and property</c> — an error that names the property and
    ///         says nothing about the conversion. An implicit conversion IS resolved through the nullable
    ///         lift, so this closes the hole for every slot at once.
    ///     </para>
    ///     <para>
    ///         The converter stays: it is what produces the good error message for an unknown name, and
    ///         it is the path used where a converter is asked for explicitly.
    ///     </para>
    ///     <para>
    ///         <b>Throws on an unknown name</b>, matching the converter — a blank icon slot in a built
    ///         app gives no clue which of a dozen causes applies, while a failure names the exact value.
    ///         Resolution happens when this runs, so fonts registered at startup are visible to XAML
    ///         evaluated later.
    ///     </para>
    /// </remarks>
    /// <exception cref="FormatException">The name matches no built-in glyph, registered font member or registered raw name.</exception>
    public static implicit operator G9IconSource(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Empty;
        }

        return G9IconFonts.Resolve(name)
               ?? throw new FormatException(
                   $"'{name}' is not a known icon. Use a G9Glyph member (e.g. \"Search\"), " +
                   $"a registered font member (\"MyIcons.Valve\"), or a name registered with " +
                   $"G9IconFonts.RegisterName. Fonts are registered with G9IconFonts.Register<TEnum>().");
    }

    /// <inheritdoc />
    public bool Equals(G9IconSource other) =>
        BuiltIn == other.BuiltIn
        && string.Equals(FontFamily, other.FontFamily, StringComparison.Ordinal)
        && string.Equals(Glyph, other.Glyph, StringComparison.Ordinal);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is G9IconSource other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(FontFamily, Glyph, BuiltIn);

    /// <summary>Equality.</summary>
    public static bool operator ==(G9IconSource left, G9IconSource right) => left.Equals(right);

    /// <summary>Inequality.</summary>
    public static bool operator !=(G9IconSource left, G9IconSource right) => !left.Equals(right);

    /// <summary>
    ///     A stable identity string. The control bases compare these to decide whether an icon
    ///     host actually needs rebuilding, so it must change if and only if the icon changes —
    ///     see <c>Controls/G9Controls.md</c> §12a for why an unnecessary rebuild costs a frame
    ///     of tofu.
    /// </summary>
    public override string ToString() =>
        IsBuiltIn ? $"builtin:{BuiltIn}"
        : IsEmpty ? string.Empty
        : $"{FontFamily}:{Glyph}";
}
