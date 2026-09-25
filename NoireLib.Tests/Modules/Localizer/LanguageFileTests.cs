using FluentAssertions;
using NoireLib.Localizer;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the <c>.lang</c> format: <c>key = text</c> lines, comments, blank and malformed lines skipped, a byte order
/// mark ignored, escapes for line breaks and backslashes, and a write that reads back to the same table.
/// </summary>
public sealed class LanguageFileTests
{
    [Fact]
    public void Parse_ReadsKeysAndTexts_AndSkipsCommentsBlanksAndMalformedLines()
    {
        var table = LanguageFile.Parse("\uFEFF# header\r\n\r\nwindow.title = Titre\r\n  indented.key   =   spaced out  \nno equals sign\n= no key\nempty.value =\nurl = a=b\n");

        table.Should().HaveCount(3);
        table["window.title"].Should().Be("Titre");
        table["indented.key"].Should().Be("spaced out");
        table["url"].Should().Be("a=b", "only the first equals sign separates the key");
    }

    [Fact]
    public void Parse_UnescapesLineBreaksAndBackslashes()
    {
        var table = LanguageFile.Parse(@"multi = one\ntwo" + "\n" + @"path = C:\\plugins\\n" + "\n" + @"odd = keep \t as is");

        table["multi"].Should().Be("one\ntwo");
        table["path"].Should().Be(@"C:\plugins\n", "an escaped backslash is not the start of a line break");
        table["odd"].Should().Be(@"keep \t as is");
    }

    [Fact]
    public void Parse_KeysIgnoreCase_AndTheLastLineWins()
    {
        var table = LanguageFile.Parse("Key = first\nkey = second");

        table["KEY"].Should().Be("second");
    }

    [Fact]
    public void Write_ReadsBackToTheSameTable_WithTheSourceAboveEachLine()
    {
        var table = new Dictionary<string, string>
        {
            ["b.key"] = "two\nlines",
            ["a.key"] = @"back\slash",
        };

        var text = LanguageFile.Write(table, key => key == "a.key" ? "Source A" : null);

        text.Should().Be("# Source: Source A\na.key = back\\\\slash\n# Unused: the plugin no longer has this text.\nb.key = two\\nlines\n");
        LanguageFile.Parse(text).Should().BeEquivalentTo(table);
    }
}
