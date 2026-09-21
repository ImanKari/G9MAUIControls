using System.Globalization;
using G9MAUIControls.Controls;

namespace G9MAUIControls.Tests;

public sealed class G9CalendarTests
{
    private const string IsoDate = "yyyy-MM-dd"; // literal hyphens: independent of any culture's DateSeparator

    // ── IsPersianLanguage ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("fa", true)]
    [InlineData("fa-IR", true)]
    [InlineData("fa-AF", true)] // any region
    [InlineData("ar", false)] // right-to-left, but NOT Persian - the defect this replaced
    [InlineData("ar-SA", false)]
    [InlineData("he", false)]
    [InlineData("en", false)]
    [InlineData("en-US", false)]
    public void IsPersianLanguageFollowsTheLanguageNotTheReadingDirection(string cultureName, bool expected)
    {
        Assert.Equal(expected, G9Calendar.IsPersianLanguage(CultureInfo.GetCultureInfo(cultureName)));
    }

    [Fact]
    public void IsPersianLanguageIsFalseForNullAndForInvariant()
    {
        Assert.False(G9Calendar.IsPersianLanguage(null));
        Assert.False(G9Calendar.IsPersianLanguage(CultureInfo.InvariantCulture));
    }

    // ── IsSupportedByPersianCalendar ────────────────────────────────────────────────────────────

    [Fact]
    public void DefaultDateTimeIsNotSupportedAndAskingDoesNotThrow()
    {
        var exception = Record.Exception(() => G9Calendar.IsSupportedByPersianCalendar(DateTime.MinValue));

        Assert.Null(exception);
        Assert.False(G9Calendar.IsSupportedByPersianCalendar(DateTime.MinValue));
        Assert.False(G9Calendar.IsSupportedByPersianCalendar(default));
    }

    [Fact]
    public void SupportedRangeIsInclusiveAtBothEnds()
    {
        var min = G9Calendar.PersianMinSupportedDateTime;
        var max = G9Calendar.PersianMaxSupportedDateTime;

        Assert.True(G9Calendar.IsSupportedByPersianCalendar(min));
        Assert.True(G9Calendar.IsSupportedByPersianCalendar(max));
        Assert.False(G9Calendar.IsSupportedByPersianCalendar(min.AddTicks(-1)));
        Assert.True(G9Calendar.IsSupportedByPersianCalendar(new DateTime(2026, 9, 21)));
    }

    // ── ClampToPersianRange ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public void ClampPullsDefaultDateTimeIntoTheRangeAndKeepsItsKind(DateTimeKind kind)
    {
        var clamped = G9Calendar.ClampToPersianRange(DateTime.SpecifyKind(DateTime.MinValue, kind));

        Assert.Equal(kind, clamped.Kind);
        Assert.True(G9Calendar.IsSupportedByPersianCalendar(clamped));
        Assert.Equal(G9Calendar.PersianMinSupportedDateTime, clamped);
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public void ClampLeavesAnInRangeValueExactlyAsItWas(DateTimeKind kind)
    {
        var value = new DateTime(2026, 9, 21, 13, 45, 12, kind);

        var clamped = G9Calendar.ClampToPersianRange(value);

        Assert.Equal(value, clamped);
        Assert.Equal(value.Ticks, clamped.Ticks);
        Assert.Equal(kind, clamped.Kind);
    }

    [Fact]
    public void ClampKeepsTheLastRepresentableInstantInsideTheRange()
    {
        var clamped = G9Calendar.ClampToPersianRange(DateTime.MaxValue);

        Assert.True(G9Calendar.IsSupportedByPersianCalendar(clamped));
    }

    [Theory]
    [InlineData(DateTimeKind.Unspecified)]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Local)]
    public void PersianCalendarAcceptsTheClampedValue(DateTimeKind kind)
    {
        var persian = new PersianCalendar();
        var unclamped = DateTime.SpecifyKind(DateTime.MinValue, kind);

        // The premise: this is the call that took the app down from inside a visual pass.
        Assert.Throws<ArgumentOutOfRangeException>(() => persian.GetYear(unclamped));

        var clamped = G9Calendar.ClampToPersianRange(unclamped);
        Assert.Equal(1, persian.GetYear(clamped));
        Assert.Equal(1, persian.GetMonth(clamped));
        Assert.Equal(1, persian.GetDayOfMonth(clamped));
    }

    // ── GetGregorianFormatCulture ───────────────────────────────────────────────────────────────

    [Fact]
    public void GregorianFormatCultureOfPersianFormatsDefaultDateTimeWithoutThrowing()
    {
        var culture = G9Calendar.GetGregorianFormatCulture(CultureInfo.GetCultureInfo("fa-IR"));

        var formatted = DateTime.MinValue.ToString(IsoDate, culture);

        Assert.IsAssignableFrom<GregorianCalendar>(culture.DateTimeFormat.Calendar);
        Assert.Equal("0001-01-01", formatted);
    }

    [Fact]
    public void GregorianFormatCultureOfPersianPrintsAGregorianYear()
    {
        var culture = G9Calendar.GetGregorianFormatCulture(CultureInfo.GetCultureInfo("fa-IR"));

        // 2026, not 1405.
        Assert.Equal("2026-09-21", new DateTime(2026, 9, 21).ToString(IsoDate, culture));
    }

    [Fact]
    public void GregorianFormatCultureOfArabicSaudiPrintsAGregorianYear()
    {
        var culture = G9Calendar.GetGregorianFormatCulture(CultureInfo.GetCultureInfo("ar-SA"));

        Assert.IsAssignableFrom<GregorianCalendar>(culture.DateTimeFormat.Calendar);
        Assert.Equal("2026-09-21", new DateTime(2026, 9, 21).ToString(IsoDate, culture));
        Assert.Equal("0001-01-01", DateTime.MinValue.ToString(IsoDate, culture));
    }

    [Fact]
    public void GregorianFormatCultureReturnsAnAlreadyGregorianCultureItself()
    {
        var english = CultureInfo.GetCultureInfo("en-US");

        Assert.Same(english, G9Calendar.GetGregorianFormatCulture(english));
    }

    [Fact]
    public void GregorianFormatCultureOfNullIsInvariant()
    {
        Assert.Same(CultureInfo.InvariantCulture, G9Calendar.GetGregorianFormatCulture(null));
    }

    [Fact]
    public void GregorianFormatCultureCachesItsClone()
    {
        var persian = CultureInfo.GetCultureInfo("fa-IR");

        Assert.Same(G9Calendar.GetGregorianFormatCulture(persian), G9Calendar.GetGregorianFormatCulture(persian));
    }
}
