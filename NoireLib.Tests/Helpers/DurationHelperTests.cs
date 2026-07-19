using FluentAssertions;
using NoireLib.Helpers;
using System;
using Xunit;

namespace NoireLib.Tests.Helpers;

/// <summary>
/// The duration parser is the part of the inputs surface that has to be right without anyone looking at it: a field
/// that reads "5 minuts" as five minutes saves a number nobody typed, and nothing on screen says so.
/// </summary>
public class DurationHelperTests
{
    #region Reading the shorthand

    [Theory]
    [InlineData("90s", 90_000)]
    [InlineData("1m30s", 90_000)]
    [InlineData("2m 30s", 150_000)]
    [InlineData("1h", 3_600_000)]
    [InlineData("1h30m", 5_400_000)]
    [InlineData("1d", 86_400_000)]
    [InlineData("500ms", 500)]
    [InlineData("2s500ms", 2_500)]
    [InlineData("1d2h3m4s5ms", 93_784_005)]
    public void TryParse_ReadsTheUnitForm(string text, double expectedMilliseconds)
    {
        DurationHelper.TryParse(text, out var value).Should().BeTrue();

        value.TotalMilliseconds.Should().Be(expectedMilliseconds);
    }

    [Theory]
    [InlineData("1h30", 5_400_000)]
    [InlineData("1m30", 90_000)]
    [InlineData("1d12", 129_600_000)]
    [InlineData("2s500", 2_500)]
    public void TryParse_TakesABareTailAsTheNextUnitDown(string text, double expectedMilliseconds)
    {
        // The rule that makes "1h30" mean what everyone types it to mean.
        DurationHelper.TryParse(text, out var value).Should().BeTrue();

        value.TotalMilliseconds.Should().Be(expectedMilliseconds);
    }

    [Fact]
    public void TryParse_TakesALeadingBareNumberAsSeconds()
    {
        DurationHelper.TryParse("90", out var value).Should().BeTrue();

        value.Should().Be(TimeSpan.FromSeconds(90), "because a number in a duration field is a count of seconds");
    }

    [Theory]
    [InlineData(DurationUnit.Milliseconds, 90)]
    [InlineData(DurationUnit.Seconds, 90_000)]
    [InlineData(DurationUnit.Minutes, 5_400_000)]
    public void TryParse_MeasuresABareNumberInTheUnitAsked(DurationUnit unit, double expectedMilliseconds)
    {
        DurationHelper.TryParse("90", unit, out var value).Should().BeTrue();

        value.TotalMilliseconds.Should().Be(expectedMilliseconds);
    }

    [Theory]
    [InlineData("1.5h", 5_400_000)]
    [InlineData("1,5h", 5_400_000)]
    [InlineData("0.5s", 500)]
    public void TryParse_ReadsFractions_WithEitherDecimalSeparator(string text, double expectedMilliseconds)
    {
        // Both separators, so a field behaves the same for someone whose keyboard puts a comma there.
        DurationHelper.TryParse(text, out var value).Should().BeTrue();

        value.TotalMilliseconds.Should().Be(expectedMilliseconds);
    }

    [Theory]
    [InlineData("1M30S", 90_000)]
    [InlineData("1 MIN 30 SEC", 90_000)]
    [InlineData("1minute30seconds", 90_000)]
    [InlineData("  1m30s  ", 90_000)]
    public void TryParse_IgnoresCaseSpacingAndSpelling(string text, double expectedMilliseconds)
    {
        DurationHelper.TryParse(text, out var value).Should().BeTrue();

        value.TotalMilliseconds.Should().Be(expectedMilliseconds);
    }

    [Theory]
    [InlineData("-5s", -5_000)]
    [InlineData("-1m30s", -90_000)]
    [InlineData("+5s", 5_000)]
    public void TryParse_ReadsASign(string text, double expectedMilliseconds)
    {
        DurationHelper.TryParse(text, out var value).Should().BeTrue();

        value.TotalMilliseconds.Should().Be(expectedMilliseconds);
    }

    #endregion

    #region Reading the clock form

    [Theory]
    [InlineData("1:30", 90_000)]
    [InlineData("0:05", 5_000)]
    [InlineData("1:30:00", 5_400_000)]
    [InlineData("2:00:30", 7_230_000)]
    [InlineData("90:00", 5_400_000)]
    public void TryParse_ReadsTheClockForm(string text, double expectedMilliseconds)
    {
        DurationHelper.TryParse(text, out var value).Should().BeTrue();

        value.TotalMilliseconds.Should().Be(expectedMilliseconds);
    }

    [Theory]
    [InlineData("1:90")]
    [InlineData("1:30:90")]
    [InlineData("1:2:3:4")]
    [InlineData("1:")]
    [InlineData(":30")]
    [InlineData("::")]
    public void TryParse_RefusesAClockThatIsNotOne(string text)
    {
        // Only the leading part may run past its own unit, which is what makes "90:00" ninety minutes and "1:90"
        // nothing at all.
        DurationHelper.TryParse(text, out var value).Should().BeFalse();

        value.Should().Be(TimeSpan.Zero);
    }

    #endregion

    #region Refusing the rest

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("soon")]
    [InlineData("5 minuts")]
    [InlineData("5 monts")]
    [InlineData("m")]
    [InlineData("-")]
    [InlineData("5s junk")]
    [InlineData("5s!")]
    [InlineData("1.2.3s")]
    public void TryParse_RefusesAnythingItDoesNotFullyUnderstand(string? text)
    {
        DurationHelper.TryParse(text, out var value).Should().BeFalse(
            "because a field that reads part of the text and ignores the rest saves a number nobody typed");

        value.Should().Be(TimeSpan.Zero);
    }

    [Theory]
    [InlineData("30s1m")]
    [InlineData("1s5h")]
    [InlineData("1m1m")]
    [InlineData("5ms30")]
    public void TryParse_RefusesUnitsThatDoNotGetSmaller(string text)
    {
        // Required rather than tidied up, because it is what makes a bare tail readable at all: without the rule,
        // "5ms30" would have to mean something.
        DurationHelper.TryParse(text, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("999999999999999999999d")]
    [InlineData("99999999999999999999999999")]
    public void TryParse_RefusesADurationTooLargeToHold(string text)
    {
        var act = () => DurationHelper.TryParse(text, out _);

        act.Should().NotThrow("because an overflow in a text field is a crash the user caused by leaning on a key");
        DurationHelper.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParse_RefusesTextLongerThanItWillLookAt()
    {
        var text = new string('1', DurationHelper.MaxLength + 1);

        DurationHelper.TryParse(text, out _).Should().BeFalse();
    }

    #endregion

    #region Writing

    [Theory]
    [InlineData(0, "0s")]
    [InlineData(500, "500ms")]
    [InlineData(1_000, "1s")]
    [InlineData(90_000, "1m30s")]
    [InlineData(3_600_000, "1h")]
    [InlineData(5_400_000, "1h30m")]
    [InlineData(86_400_000, "1d")]
    [InlineData(93_784_005, "1d2h3m4s5ms")]
    [InlineData(-90_000, "-1m30s")]
    public void Format_WritesTheShorthand(double milliseconds, string expected)
    {
        DurationHelper.Format(TimeSpan.FromMilliseconds(milliseconds)).Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(999)]
    [InlineData(90_000)]
    [InlineData(5_400_000)]
    [InlineData(93_784_005)]
    [InlineData(-90_000)]
    public void Format_RoundTripsThroughTryParse(double milliseconds)
    {
        // The contract that lets a field show what was typed rather than what it was stored as: whatever Format
        // writes, TryParse has to read back to the same duration.
        var original = TimeSpan.FromMilliseconds(milliseconds);

        DurationHelper.TryParse(DurationHelper.Format(original), out var round).Should().BeTrue();

        round.Should().Be(original);
    }

    #endregion

    #region Parse

    [Fact]
    public void Parse_ReturnsTheFallback_ForTextThatIsNotADuration()
    {
        var fallback = TimeSpan.FromMinutes(5);

        DurationHelper.Parse("nonsense", fallback).Should().Be(fallback);
    }

    [Fact]
    public void Parse_ReturnsTheValue_ForTextThatIs()
    {
        DurationHelper.Parse("1m30s", TimeSpan.FromMinutes(5)).Should().Be(TimeSpan.FromSeconds(90));
    }

    #endregion
}
