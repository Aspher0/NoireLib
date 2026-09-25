using FluentAssertions;
using NoireLib.Helpers;
using NoireLib.UI;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks where lines break: Chinese breaks between its characters but never before a closing mark, other scripts break
/// at spaces only, and the text area wraps Chinese by the same rule.
/// </summary>
public sealed class LineBreakHelperTests
{
    private const char FullWidthComma = (char)0xFF0C;

    // Ten ideographs, a full-width comma, then two more ideographs.
    private static readonly string Chinese = Ideographs(10) + FullWidthComma + Ideographs(2);

    [Fact]
    public void Break_CutsChinese_BetweenCharacters_KeepingTheCommaOffTheNextLine()
    {
        var lines = new List<string>();

        LineBreakHelper.Break(Chinese, 10f, static part => part.Length, lines);

        lines.Should().Equal(Chinese[..9], Chinese[9..]);
    }

    [Fact]
    public void Break_CutsLatin_AtSpacesOnly()
    {
        var lines = new List<string>();

        LineBreakHelper.Break("one two three averyveryverylongword", 9f, static part => part.Length, lines);

        lines.Should().Equal("one two", "three", "averyveryverylongword");
        LineBreakHelper.ContainsCjk("one two").Should().BeFalse();
        LineBreakHelper.ContainsCjk(Chinese).Should().BeTrue();
    }

    [Fact]
    public void TextArea_WrapsChinese_ByTheSameRule()
    {
        var (display, _) = SoftWrap.Wrap(Chinese, 10f, static _ => 1f);

        display.Should().Be(Chinese[..9] + "\n" + Chinese[9..]);
    }

    private static string Ideographs(int count)
    {
        var characters = new char[count];

        for (var index = 0; index < count; index++)
            characters[index] = (char)(0x4E00 + index);

        return new string(characters);
    }
}
