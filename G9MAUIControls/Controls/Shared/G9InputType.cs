using G9MAUIControls.Localization;
using System.Globalization;
using System.Text.RegularExpressions;

namespace G9MAUIControls.Controls;

/// <summary>
///     Semantic input typing for <see cref="G9TextEntry" /> / <see cref="G9Editor" /> and
///     every entry that inherits them (<see cref="G9SearchEntry" />,
///     <c>G9BarcodeTextEntry</c>, etc.). One enum drives <b>three</b> things at once
///     so consumers don't have to wire them separately:
///     <list type="number">
///         <item>
///             <b>On-screen keyboard.</b> Picks <see cref="G9KeyboardType" /> via
///             <see cref="G9InputTypePolicy.ResolveKeyboard" />.
///         </item>
///         <item>
///             <b>Live keystroke filter.</b> Each character the user types — physical
///             keyboard or paste — is filtered through
///             <see cref="G9InputTypePolicy.SanitizeText" />. Rejected characters never
///             reach the bound <c>Text</c> (so <c>InputType.Number</c> truly accepts
///             only digits even on a hardware keyboard or paste from an arbitrary
///             string).
///         </item>
///         <item>
///             <b>On-blur validation.</b> Types like <see cref="Email" /> /
///             <see cref="Url" /> validate the final value against a stricter pattern
///             once the user leaves the field, surfacing
///             <see cref="G9OutlinedFieldBase.ErrorText" />.
///         </item>
///     </list>
///     Use <see cref="Custom" /> together with
///     <see cref="G9TextEntry.AllowedCharsPattern" /> /
///     <see cref="G9TextEntry.ValidationPattern" /> to define the allowed-char regex and
///     the blur-time validation regex inline. <see cref="G9TextEntry.ValidationErrorText" />
///     overrides the localized default message.
/// </summary>
public enum G9InputType
{
    /// <summary>No filtering, no validation, default keyboard. Same as not setting the property.</summary>
    Default,

    /// <summary>Digits 0-9 only. Numeric on-screen keyboard.</summary>
    Number,

    /// <summary>
    ///     Digits 0-9 plus one decimal separator (matches the active culture's
    ///     <see cref="NumberFormatInfo.NumberDecimalSeparator" />, falling back to '.'
    ///     when the culture uses something exotic). Numeric on-screen keyboard.
    /// </summary>
    Decimal,

    /// <summary>
    ///     Digits 0-9 with an optional leading '-' for negative values. Numeric
    ///     on-screen keyboard.
    /// </summary>
    SignedNumber,

    /// <summary>
    ///     Digits 0-9 with optional leading '-' and one decimal separator. Numeric
    ///     on-screen keyboard.
    /// </summary>
    SignedDecimal,

    /// <summary>
    ///     Phone-number characters: digits, '+', '-', space, '(' and ')'. Telephone
    ///     on-screen keyboard. No format validation — phone numbers vary too much by
    ///     country to validate with a regex.
    /// </summary>
    Phone,

    /// <summary>
    ///     Email-shaped input — typing accepts everything except whitespace; on blur the
    ///     final value is checked against a permissive RFC-shape regex
    ///     (<c>local@domain.tld</c>). Email on-screen keyboard.
    /// </summary>
    Email,

    /// <summary>
    ///     URL-shaped input — typing rejects whitespace; on blur the value is checked
    ///     with <see cref="Uri.TryCreate(string, UriKind, out Uri?)" /> against
    ///     <see cref="UriKind.Absolute" />. URL on-screen keyboard.
    /// </summary>
    Url,

    /// <summary>Latin letters a-z and A-Z only. Default keyboard.</summary>
    Letters,

    /// <summary>Latin letters and digits 0-9. No spaces, no special characters.</summary>
    LettersAndNumbers,

    /// <summary>Latin letters, digits, and the space character. No special characters.</summary>
    LettersNumbersSpace,

    /// <summary>
    ///     Persian and Arabic letters (Unicode <c>U+0600</c>–<c>U+06FF</c>) plus the
    ///     space character. Suitable for Persian-language name and address fields.
    /// </summary>
    PersianLetters,

    /// <summary>
    ///     Persian / Arabic letters plus digits (both Persian-Indic
    ///     <c>U+06F0</c>–<c>U+06F9</c> and ASCII 0-9) plus space.
    /// </summary>
    PersianLettersAndNumbers,

    /// <summary>
    ///     Latin + Persian / Arabic letters + digits (ASCII and Persian-Indic) + space.
    ///     Use for fields that may receive mixed-language input (e.g. agricultural
    ///     product codes).
    /// </summary>
    MultilingualLettersAndNumbers,

    /// <summary>
    ///     Letters (Latin / Persian / Arabic), digits, and space. Rejects every
    ///     "special" character (punctuation, currency, math symbols) so the field
    ///     remains paste-safe for value-entry where extra characters would corrupt the
    ///     server-side parse.
    /// </summary>
    NoSpecialChars,

    /// <summary>
    ///     Use the consumer-supplied <see cref="G9TextEntry.AllowedCharsPattern" /> as
    ///     the live filter and <see cref="G9TextEntry.ValidationPattern" /> as the
    ///     blur-time validation. Either may be null — null filter means accept
    ///     anything; null validation pattern means no blur check.
    /// </summary>
    Custom
}

/// <summary>
///     Stateless helper that resolves an <see cref="G9InputType" /> into its three
///     behaviours: keyboard, live filter, blur validation. Cached compiled regexes are
///     reused across every input on the page — recomputing them per keystroke would be
///     measurable overhead on a slow Android phone.
/// </summary>
internal static class G9InputTypePolicy
{
    // Compiled regexes — created once at class load. Building Regex on every keystroke
    // adds ~50 µs which is negligible per call but visibly compounds on paste of a
    // 2 KB string (the filter runs over each character).
    private static readonly Regex EmailRegex =
        new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Whether values typed into a field of this type should default to
    ///     left-to-right layout — i.e. the entered text and the caret start on the
    ///     left edge of the input box, even when the surrounding UI is right-to-left
    ///     (Persian, Arabic, Hebrew). Numbers, phone numbers, email addresses and
    ///     URLs are universally written left-to-right; forcing them through the
    ///     parent's RTL direction would mirror "+98 21 1234" into "1234 21 89+" and
    ///     hide the leading '+' on the right edge of the box.
    ///     <para>
    ///         The floating label, outline notch, helper text and icon placement
    ///         still follow the parent's culture — only the entered value and its
    ///         caret are forced LTR. Consumers can override per-field via
    ///         <see cref="G9TextEntry.InputTextDirection" /> /
    ///         <see cref="G9Editor.InputTextDirection" />.
    ///     </para>
    /// </summary>
    public static bool PrefersLeftToRight(G9InputType inputType)
    {
        return inputType
            is G9InputType.Number
            or G9InputType.Decimal
            or G9InputType.SignedNumber
            or G9InputType.SignedDecimal
            or G9InputType.Phone
            or G9InputType.Email
            or G9InputType.Url;
    }

    public static G9KeyboardType ResolveKeyboard(G9InputType inputType)
    {
        return inputType switch
        {
            G9InputType.Number or G9InputType.Decimal
                or G9InputType.SignedNumber or G9InputType.SignedDecimal
                => G9KeyboardType.Number,
            G9InputType.Phone => G9KeyboardType.Phone,
            G9InputType.Email => G9KeyboardType.Email,
            G9InputType.Url => G9KeyboardType.Url,
            _ => G9KeyboardType.Default
        };
    }

    /// <summary>
    ///     Returns a sanitized version of <paramref name="raw" /> with every disallowed
    ///     character stripped. If the result equals <paramref name="raw" />, returns
    ///     <paramref name="raw" /> unchanged — callers can compare by reference to skip
    ///     a redundant write back to the platform Entry.
    /// </summary>
    /// <param name="inputType">Selected input type.</param>
    /// <param name="raw">The candidate text (typed or pasted) to filter.</param>
    /// <param name="allowedCharsPattern">
    ///     Custom regex pattern. Used when <paramref name="inputType" /> is
    ///     <see cref="G9InputType.Custom" />. The pattern is matched against the
    ///     entire candidate string; if it doesn't match, characters are stripped one
    ///     by one. For per-character filtering specify a single character class like
    ///     <c>"[A-Za-z0-9]"</c> — that's the common case.
    /// </param>
    public static string SanitizeText(G9InputType inputType, string raw, string? allowedCharsPattern)
    {
        if (string.IsNullOrEmpty(raw)) return raw;

        return inputType switch
        {
            G9InputType.Default => raw,
            G9InputType.Number => FilterChars(raw, IsAsciiDigit),
            G9InputType.Decimal => FilterDecimal(raw, allowSign: false),
            G9InputType.SignedNumber => FilterSignedInteger(raw),
            G9InputType.SignedDecimal => FilterDecimal(raw, allowSign: true),
            G9InputType.Phone => FilterChars(raw, IsPhoneChar),
            G9InputType.Email => FilterChars(raw, c => !char.IsWhiteSpace(c)),
            G9InputType.Url => FilterChars(raw, c => !char.IsWhiteSpace(c)),
            G9InputType.Letters => FilterChars(raw, IsLatinLetter),
            G9InputType.LettersAndNumbers => FilterChars(raw, c => IsLatinLetter(c) || IsAsciiDigit(c)),
            G9InputType.LettersNumbersSpace => FilterChars(raw, c => IsLatinLetter(c) || IsAsciiDigit(c) || c == ' '),
            G9InputType.PersianLetters => FilterChars(raw, c => IsPersianLetter(c) || c == ' '),
            G9InputType.PersianLettersAndNumbers => FilterChars(raw, c => IsPersianLetter(c) || IsPersianOrAsciiDigit(c) || c == ' '),
            G9InputType.MultilingualLettersAndNumbers => FilterChars(raw, c => IsLatinLetter(c) || IsPersianLetter(c) || IsPersianOrAsciiDigit(c) || c == ' '),
            G9InputType.NoSpecialChars => FilterChars(raw, c => IsLatinLetter(c) || IsPersianLetter(c) || IsPersianOrAsciiDigit(c) || c == ' '),
            G9InputType.Custom => FilterByCustomPattern(raw, allowedCharsPattern),
            _ => raw
        };
    }

    /// <summary>
    ///     Validate the final value (called on focus loss). Returns null when the value
    ///     is acceptable, or the localized error message that should be shown.
    ///     <paramref name="customMessage" /> wins when the consumer provides one.
    /// </summary>
    public static string? Validate(
        G9InputType inputType,
        string? value,
        string? validationPattern,
        string? customMessage)
    {
        if (string.IsNullOrEmpty(value)) return null;

        switch (inputType)
        {
            case G9InputType.Email:
                if (!EmailRegex.IsMatch(value))
                {
                    return customMessage ?? G9Strings.Get(G9StringKey.InvalidEmail);
                }
                return null;

            case G9InputType.Url:
                if (!Uri.TryCreate(value, UriKind.Absolute, out _))
                {
                    return customMessage ?? G9Strings.Get(G9StringKey.InvalidUrl);
                }
                return null;

            case G9InputType.Custom when !string.IsNullOrEmpty(validationPattern):
                try
                {
                    if (!Regex.IsMatch(value, validationPattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    {
                        return customMessage ?? G9Strings.Get(G9StringKey.InvalidValue);
                    }
                }
                catch (RegexMatchTimeoutException)
                {
                    // Pathological pattern — fail closed so the user sees the error rather
                    // than silently accepting whatever they typed.
                    return customMessage ?? G9Strings.Get(G9StringKey.InvalidValue);
                }
                catch (ArgumentException)
                {
                    // Invalid regex from the consumer. Don't crash — surface the validation
                    // message so it's at least visible during dev that the pattern is bad.
                    return customMessage ?? G9Strings.Get(G9StringKey.InvalidValue);
                }
                return null;

            default:
                return null;
        }
    }

    // ── Character predicates ────────────────────────────────────────────────────

    private static bool IsAsciiDigit(char c) => c is >= '0' and <= '9';

    private static bool IsLatinLetter(char c) =>
        c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z');

    /// <summary>
    ///     Persian / Arabic letter range (Unicode block 0x0600–0x06FF). Includes the
    ///     full Arabic alphabet plus the Persian-specific letters گ چ پ ژ. Excludes
    ///     digits — those are checked separately so callers can opt in via
    ///     <see cref="IsPersianOrAsciiDigit" />.
    /// </summary>
    private static bool IsPersianLetter(char c)
    {
        if (c is >= '\u0600' and <= '\u06FF')
        {
            // Reject Persian / Arabic digits (\u06F0..\u06F9) and the Arabic-Indic digits
            // (\u0660..\u0669) from the "letters" predicate; the digit predicates accept
            // them.
            if (c is >= '\u0660' and <= '\u0669') return false;
            if (c is >= '\u06F0' and <= '\u06F9') return false;
            return true;
        }
        return false;
    }

    private static bool IsPersianOrAsciiDigit(char c)
    {
        if (IsAsciiDigit(c)) return true;
        if (c is >= '\u0660' and <= '\u0669') return true; // Arabic-Indic
        if (c is >= '\u06F0' and <= '\u06F9') return true; // Persian (Eastern Arabic-Indic)
        return false;
    }

    private static bool IsPhoneChar(char c)
    {
        if (IsAsciiDigit(c)) return true;
        return c is '+' or '-' or ' ' or '(' or ')';
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static string FilterChars(string s, Func<char, bool> predicate)
    {
        var keep = true;
        for (var i = 0; i < s.Length; i++)
        {
            if (!predicate(s[i])) { keep = false; break; }
        }
        if (keep) return s;

        // Allocate a buffer up to the original length and write only allowed characters.
        return string.Create(CountAllowed(s, predicate), (s, predicate), static (span, state) =>
        {
            var (src, pred) = state;
            var w = 0;
            for (var i = 0; i < src.Length; i++)
            {
                if (pred(src[i])) span[w++] = src[i];
            }
        });
    }

    private static int CountAllowed(string s, Func<char, bool> predicate)
    {
        var count = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (predicate(s[i])) count++;
        }
        return count;
    }

    private static string FilterSignedInteger(string s)
    {
        // Allow exactly one leading '-'.
        var sb = new System.Text.StringBuilder(s.Length);
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (i == 0 && c == '-') { sb.Append(c); continue; }
            if (IsAsciiDigit(c)) sb.Append(c);
        }
        return sb.ToString();
    }

    private static string FilterDecimal(string s, bool allowSign)
    {
        // Resolve the active culture's decimal separator. Falls back to '.' when it's a
        // multi-character thing (rare). Persian users typically have '.' or '٫' depending
        // on settings — we accept both.
        var sep = CultureInfo.CurrentCulture.NumberFormat.NumberDecimalSeparator;
        var sepChar = sep.Length == 1 ? sep[0] : '.';

        var sb = new System.Text.StringBuilder(s.Length);
        var sawSeparator = false;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (allowSign && i == 0 && c == '-') { sb.Append(c); continue; }
            if (IsAsciiDigit(c)) { sb.Append(c); continue; }
            if (!sawSeparator && (c == sepChar || c == '.' || c == '٫'))
            {
                // Normalise to a single canonical separator so the parsed string round-trips
                // cleanly through double.TryParse with the active culture.
                sb.Append(sepChar);
                sawSeparator = true;
            }
        }
        return sb.ToString();
    }

    private static string FilterByCustomPattern(string s, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return s;
        try
        {
            var rx = new Regex(pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            // Per-character filter against the same pattern. For complex multi-character
            // patterns the consumer should split into a single-character class anyway —
            // anything else can't be applied incrementally as the user types.
            var sb = new System.Text.StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (rx.IsMatch(c.ToString())) sb.Append(c);
            }
            return sb.ToString();
        }
        catch
        {
            // Invalid pattern from the consumer — pass through unchanged so the field
            // stays usable. The blur-time validation will surface an error if the
            // ValidationPattern is also invalid.
            return s;
        }
    }
}
