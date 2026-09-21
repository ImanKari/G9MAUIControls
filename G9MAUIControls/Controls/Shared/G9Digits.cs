namespace G9MAUIControls.Controls;

/// <summary>
///     Digit normalization shared by every control that filters, compares or parses numeric text.
///     <para>
///         A Persian or Arabic keyboard (and any paste from a Persian source) produces
///         <c>U+06F0–U+06F9</c> or <c>U+0660–U+0669</c>, which <see cref="char.IsDigit(char)" />
///         accepts but <c>c is &gt;= '0' and &lt;= '9'</c> rejects. Before this helper the two checks
///         were mixed across the suite: numeric filters silently dropped such input, while the PIN
///         entry kept it verbatim, so an OTP typed as "۱۲۳۴" never compared equal to "1234".
///         Everything that handles digits maps them to ASCII through here first.
///     </para>
///     <para>
///         Deliberately free of any MAUI dependency so it can be file-linked into a plain
///         <c>net10.0</c> test project.
///     </para>
/// </summary>
public static class G9Digits
{
    private const char PersianZero = '۰';
    private const char PersianNine = '۹';
    private const char ArabicIndicZero = '٠';
    private const char ArabicIndicNine = '٩';

    /// <summary>
    ///     Maps a Persian (<c>U+06F0–U+06F9</c>) or Arabic-Indic (<c>U+0660–U+0669</c>) digit to its
    ///     ASCII counterpart. Every other character is returned unchanged.
    /// </summary>
    public static char NormalizeToAscii(char c)
    {
        if (c is >= PersianZero and <= PersianNine)
            return (char)('0' + (c - PersianZero));
        if (c is >= ArabicIndicZero and <= ArabicIndicNine)
            return (char)('0' + (c - ArabicIndicZero));
        return c;
    }

    /// <summary>
    ///     Returns <paramref name="text" /> with every Persian / Arabic-Indic digit replaced by its
    ///     ASCII counterpart. <see langword="null" /> yields an empty string. The original instance
    ///     is returned when there is nothing to replace, so the common all-ASCII keystroke path
    ///     allocates nothing.
    /// </summary>
    public static string NormalizeToAscii(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var firstHit = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (NormalizeToAscii(text[i]) == text[i])
                continue;
            firstHit = i;
            break;
        }

        if (firstHit < 0)
            return text;

        return string.Create(text.Length, (text, firstHit), static (span, state) =>
        {
            var (source, start) = state;
            source.AsSpan(0, start).CopyTo(span);
            for (var i = start; i < source.Length; i++)
                span[i] = NormalizeToAscii(source[i]);
        });
    }
}
