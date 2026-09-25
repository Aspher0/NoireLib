using FluentAssertions;
using Dalamud.Bindings.ImGui;
using NoireLib.Internal.Helpers;
using NoireLib.UI;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>Editing across wrapped lines removes a real character, and a search is marked once per line it spans.</summary>
[Collection(NoireUiTestCollection.Name)]
public sealed class NoireTextAreaTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public NoireTextAreaTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void DeletingASoftBreak_WithBackspace_RemovesTheLastCharacterOfTheLineAbove()
    {
        var (text, cursor) = DeleteTheBreak(forward: false);

        text.Should().Be("helloworld");
        cursor.Should().Be(5);
    }

    [Fact]
    public void DeletingASoftBreak_WithDelete_RemovesTheFirstCharacterOfTheLineBelow()
    {
        var (text, cursor) = DeleteTheBreak(forward: true);

        text.Should().Be("hello orld");
        cursor.Should().Be(6);
    }

    [Fact]
    public void DeleteAcross_AtTheEdges_ChangesNothing_AndTakesASurrogatePairWhole()
    {
        SoftWrap.DeleteAcross("abc", 0, forward: false).Should().Be(("abc", 0));
        SoftWrap.DeleteAcross("abc", 3, forward: true).Should().Be(("abc", 3));
        SoftWrap.DeleteAcross("a\U0001F600b", 3, forward: false).Should().Be(("ab", 1));
        SoftWrap.DeleteAcross("a\U0001F600b", 1, forward: true).Should().Be(("ab", 1));
    }

    [Fact]
    public void TextMatches_FindsEveryOccurrence_OrOnlyWholeWords()
    {
        TextMatches.Next("Remplacer, remplacer", "remplacer", false, 0).Should().Be(0);
        TextMatches.Next("Remplacer, remplacer", "remplacer", false, 9).Should().Be(11);
        TextMatches.Next("Remplacement Remplacer", "remplacer", true, 0).Should().Be(13);
        TextMatches.Next("Remplacement", "remplacer", true, 0).Should().Be(-1);
        TextMatches.Next("abc", "b", false, 4).Should().Be(-1);
        TextMatches.Next("abc", string.Empty, false, 0).Should().Be(-1);
    }

    [Fact]
    public void Mark_GivesEachOccurrenceOneMarkPerLine()
    {
        var whole = new List<Vector4>();
        var cut = new List<Vector4>();
        float lineHeight = 0f, ab = 0f, abc = 0f, d = 0f;

        harness.Draw(() =>
        {
            NoireTextArea.Mark("abc abc", "abc \nabc", [4], "abc", false, whole);
            NoireTextArea.Mark("abcdef", "abc\ndef", [3], "cd", false, cut);
            lineHeight = ImGui.GetFontSize();
            ab = ImGui.CalcTextSize("ab").X;
            abc = ImGui.CalcTextSize("abc").X;
            d = ImGui.CalcTextSize("d").X;
        }, profile: false);

        whole.Should().Equal(new Vector4(0f, 0f, abc, lineHeight), new Vector4(0f, lineHeight, abc, lineHeight * 2f));
        cut.Should().Equal(new Vector4(ab, 0f, abc, lineHeight), new Vector4(0f, lineHeight, d, lineHeight * 2f));
    }

    // "hello world" wrapped after the space, then the break alone removed, as the field sees Backspace at the start of
    // "world" or Delete at the end of "hello ".
    private static (string Text, int Cursor) DeleteTheBreak(bool forward)
    {
        var (display, softBreaks) = SoftWrap.Wrap("hello world", 6f, static _ => 1f);
        display.Should().Be("hello \nworld");

        var after = display.Remove(6, 1);
        var carried = SoftWrap.Carry(display, softBreaks, after);
        var text = SoftWrap.Unwrap(after, carried);
        text.Should().Be("hello world", "because the break is not part of the text");

        return SoftWrap.DeleteAcross(text, SoftWrap.TextIndex(6, carried), forward);
    }
}
