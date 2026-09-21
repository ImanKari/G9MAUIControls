using System.Collections.Concurrent;
using System.Globalization;

namespace G9MAUIControls.Controls;

/// <summary>
///     The calendar decisions shared by <c>G9DateTimePicker</c>, its sheet and
///     <c>G9CultureDateTimeLabel</c>: WHEN the Persian (Jalali) calendar applies, and WHICH dates it
///     can represent at all.
///     <para>
///         Two defects made this a shared helper. The pickers chose the calendar from "is the culture
///         right-to-left", so an Arabic or Hebrew app was handed Jalali dates — the calendar follows
///         the LANGUAGE, not the reading direction. And <see cref="PersianCalendar" /> throws
///         <see cref="ArgumentOutOfRangeException" /> for anything before 0622-03-22, so a bound
///         <c>default(DateTime)</c> took the app down from inside a visual pass that only catches
///         <see cref="ObjectDisposedException" />. Every Jalali conversion is range-checked here first.
///     </para>
///     <para>
///         Deliberately free of any MAUI dependency so it can be file-linked into a plain
///         <c>net10.0</c> test project.
///     </para>
/// </summary>
public static class G9Calendar
{
    private const string PersianLanguageCode = "fa";

    private static readonly PersianCalendar Persian = new();

    /// <summary>Gregorian-calendar clones handed out by <see cref="GetGregorianFormatCulture" />, by culture name.</summary>
    private static readonly ConcurrentDictionary<string, CultureInfo> GregorianClones =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>First instant the Persian calendar can represent (0622-03-22, Jalali 0001/01/01).</summary>
    public static DateTime PersianMinSupportedDateTime => Persian.MinSupportedDateTime;

    /// <summary>Last instant the Persian calendar can represent.</summary>
    public static DateTime PersianMaxSupportedDateTime => Persian.MaxSupportedDateTime;

    /// <summary>
    ///     <see langword="true" /> when <paramref name="culture" />'s language is Persian (<c>fa</c>,
    ///     any region). This — not "is the culture right-to-left" — decides whether dates are shown
    ///     in the Jalali calendar. <see langword="null" /> yields <see langword="false" />.
    /// </summary>
    public static bool IsPersianLanguage(CultureInfo? culture)
    {
        return culture is not null &&
               culture.TwoLetterISOLanguageName.Equals(PersianLanguageCode, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    ///     <see langword="true" /> when <paramref name="value" /> can be passed to
    ///     <see cref="PersianCalendar" /> members without throwing.
    /// </summary>
    public static bool IsSupportedByPersianCalendar(DateTime value)
    {
        return value >= Persian.MinSupportedDateTime && value <= Persian.MaxSupportedDateTime;
    }

    /// <summary>
    ///     Returns <paramref name="value" /> pulled into the range the Persian calendar supports.
    ///     A value already inside the range is returned unchanged (including its
    ///     <see cref="DateTime.Kind" />); a clamped result carries the original kind.
    /// </summary>
    public static DateTime ClampToPersianRange(DateTime value)
    {
        if (value < Persian.MinSupportedDateTime)
            return DateTime.SpecifyKind(Persian.MinSupportedDateTime, value.Kind);
        if (value > Persian.MaxSupportedDateTime)
            return DateTime.SpecifyKind(Persian.MaxSupportedDateTime, value.Kind);
        return value;
    }

    /// <summary>
    ///     A culture that is safe to pass to <see cref="DateTime.ToString(string, IFormatProvider)" />
    ///     for a GREGORIAN rendering of any <see cref="DateTime" />.
    ///     <para>
    ///         <c>value.ToString("yyyy/MM/dd", culture)</c> formats in the CULTURE's calendar, not
    ///         the Gregorian one. Under <c>fa-IR</c> that is the Persian calendar, which throws for a
    ///         date before 0622-03-22 — so "fall back to Gregorian" for an out-of-range value threw
    ///         the very exception it was dodging. Under <c>ar-SA</c> or <c>th-TH</c> it silently
    ///         prints a Hijri / Buddhist year next to a Gregorian picker.
    ///     </para>
    ///     <para>
    ///         Returns <paramref name="culture" /> itself when its calendar already is Gregorian;
    ///         otherwise a cached clone switched to the Gregorian calendar (localized month names
    ///         are kept); and <see cref="CultureInfo.InvariantCulture" /> when the culture offers
    ///         no Gregorian calendar, or is <see langword="null" />.
    ///     </para>
    /// </summary>
    public static CultureInfo GetGregorianFormatCulture(CultureInfo? culture)
    {
        if (culture is null)
            return CultureInfo.InvariantCulture;
        if (culture.DateTimeFormat.Calendar is GregorianCalendar)
            return culture;

        return GregorianClones.GetOrAdd(culture.Name, static (_, source) => CreateGregorianClone(source), culture);
    }

    private static CultureInfo CreateGregorianClone(CultureInfo source)
    {
        try
        {
            foreach (var calendar in source.OptionalCalendars)
            {
                if (calendar is not GregorianCalendar gregorian)
                    continue;

                var clone = (CultureInfo)source.Clone();
                clone.DateTimeFormat.Calendar = gregorian;
                return CultureInfo.ReadOnly(clone);
            }
        }
        catch (ArgumentException)
        {
            // The platform's globalization data refused the calendar for this culture.
        }
        catch (InvalidOperationException)
        {
            // Read-only DateTimeFormatInfo on an unusual culture instance.
        }

        return CultureInfo.InvariantCulture;
    }
}
