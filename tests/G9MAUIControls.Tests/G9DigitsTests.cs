using G9MAUIControls.Controls;

namespace G9MAUIControls.Tests;

// Code points are written as \u escapes on purpose: the assertions must not depend on how an editor,
// a diff tool or a CI checkout happens to encode this file.
public sealed class G9DigitsTests
{
    private const char PersianZero = '۰';
    private const char ArabicIndicZero = '٠';

    [Fact]
    public void CharOverloadMapsEveryPersianDigit()
    {
        for (var i = 0; i <= 9; i++)
            Assert.Equal((char)('0' + i), G9Digits.NormalizeToAscii((char)(PersianZero + i)));
    }

    [Fact]
    public void CharOverloadMapsEveryArabicIndicDigit()
    {
        for (var i = 0; i <= 9; i++)
            Assert.Equal((char)('0' + i), G9Digits.NormalizeToAscii((char)(ArabicIndicZero + i)));
    }

    [Theory]
    [InlineData('0')]
    [InlineData('9')]
    [InlineData('a')]
    [InlineData(' ')]
    [InlineData('-')]
    [InlineData('ٟ')] // one below Arabic-Indic zero
    [InlineData('٪')] // ARABIC PERCENT SIGN, one above Arabic-Indic nine
    [InlineData('ۯ')] // one below Persian zero
    [InlineData('ۺ')] // one above Persian nine
    [InlineData('१')] // DEVANAGARI DIGIT ONE: char.IsDigit is true, but it is not ours to map
    public void CharOverloadLeavesEverythingElseUntouched(char c)
    {
        Assert.Equal(c, G9Digits.NormalizeToAscii(c));
    }

    [Fact]
    public void StringOfPersianDigitsBecomesAscii()
    {
        Assert.Equal("0123456789",
            G9Digits.NormalizeToAscii("۰۱۲۳۴۵۶۷۸۹"));
    }

    [Fact]
    public void StringOfArabicIndicDigitsBecomesAscii()
    {
        Assert.Equal("0123456789",
            G9Digits.NormalizeToAscii("٠١٢٣٤٥٦٧٨٩"));
    }

    [Fact]
    public void OtpTypedOnAPersianKeyboardComparesEqualToAscii()
    {
        // The defect the helper exists for: "۱۲۳۴" never compared equal to "1234".
        Assert.Equal("1234", G9Digits.NormalizeToAscii("۱۲۳۴"));
    }

    [Theory]
    [InlineData("a۱b٢c3", "a1b2c3")] // Persian + Arabic-Indic + ASCII + letters
    [InlineData("۵abc", "5abc")] // first character is the only hit
    [InlineData("abc٥", "abc5")] // last character is the only hit
    [InlineData("+۹۸ (٠٩) 12-34", "+98 (09) 12-34")]
    [InlineData("سلام ۴۲", "سلام 42")] // Persian letters stay
    public void MixedStringsOnlyHaveTheirDigitsReplaced(string input, string expected)
    {
        Assert.Equal(expected, G9Digits.NormalizeToAscii(input));
    }

    [Fact]
    public void NullYieldsEmptyString()
    {
        Assert.Equal(string.Empty, G9Digits.NormalizeToAscii(null));
    }

    [Fact]
    public void EmptyYieldsEmptyString()
    {
        Assert.Equal(string.Empty, G9Digits.NormalizeToAscii(string.Empty));
    }

    [Theory]
    [InlineData("1234567890")]
    [InlineData("abc -+.,/")]
    [InlineData("سلام")] // Persian letters, no digits
    [InlineData("٪ۺ")] // the code points adjacent to both ranges
    public void TextWithNothingToReplaceIsReturnedAsTheSameInstance(string input)
    {
        // Documented contract: the all-ASCII keystroke path allocates nothing.
        Assert.Same(input, G9Digits.NormalizeToAscii(input));
    }

    [Fact]
    public void LengthIsPreserved()
    {
        const string input = "x۱۲٣y";
        Assert.Equal(input.Length, G9Digits.NormalizeToAscii(input).Length);
    }
}
