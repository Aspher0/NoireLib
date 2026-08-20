using FluentAssertions;
using NoireLib.Animations.PapFormat;
using NoireLib.Animations.PapFormat.Parsing;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// A padded string writes its value, a terminating null, then padding out to a fixed slot. Every count in that
/// arithmetic has to be in bytes: a value that fits in characters can still overrun the slot in UTF-8, and one
/// byte past the end shifts every offset after it, which is how a .pap becomes unreadable rather than merely wrong.
/// </summary>
public class ParsedPaddedStringTests
{
    private static byte[] Write(ParsedPaddedString field)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        field.Write(writer);
        writer.Flush();

        return stream.ToArray();
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("exactly_thirty_one_characters__")]
    public void Write_FillsTheSlotExactly(string value)
    {
        var field = new ParsedPaddedString("Name", value, 32, 0x00);

        Write(field).Should().HaveCount(32, "the field must occupy its whole slot and no more");
    }

    [Fact]
    public void Write_RefusesAValueThatFillsTheSlotWithNoRoomForATerminator()
    {
        // 32 characters in a 32-byte slot leaves nowhere for the null, and the old padding loop silently
        // emitted 33 bytes rather than refusing.
        var field = new ParsedPaddedString("Name", new string('a', 32), 32, 0x00);

        var write = () => Write(field);

        write.Should().Throw<InvalidDataException>().WithMessage("*Name*");
    }

    [Fact]
    public void Write_CountsBytesRatherThanCharacters()
    {
        // Sixteen two-byte characters fit a 32-character budget but not a 32-byte slot.
        var field = new ParsedPaddedString("Name", new string('e', 16), 32, 0x00);
        field.Value = new string('é', 16);

        var write = () => Write(field);

        write.Should().Throw<InvalidDataException>();
    }

    [Fact]
    public void MaxByteLength_LeavesRoomForTheTerminator()
        => new ParsedPaddedString("Name", string.Empty, 32, 0x00).MaxByteLength.Should().Be(31);

    [Fact]
    public void Read_LeavesThePositionAtTheEndOfTheSlot()
    {
        var raw = new byte[32];
        Encoding.UTF8.GetBytes("cbbm_replace_this").CopyTo(raw, 0);

        using var stream = new MemoryStream(raw, false);
        using var reader = new BinaryReader(stream);

        var field = new ParsedPaddedString("Name", 32, 0x00);
        field.Read(reader);

        field.Value.Should().Be("cbbm_replace_this");
        stream.Position.Should().Be(32);
    }

    [Fact]
    public void Read_DoesNotSeekBackwardsWhenTheValueFillsTheSlot()
    {
        // A malformed file can carry a value with no room left for its terminator; reading it must not
        // rewind the stream into the previous field.
        var raw = new byte[32];
        Encoding.UTF8.GetBytes(new string('a', 32)).CopyTo(raw, 0);

        using var stream = new MemoryStream(raw, false);
        using var reader = new BinaryReader(stream);

        var field = new ParsedPaddedString("Name", 32, 0x00);
        field.Read(reader);

        stream.Position.Should().Be(32);
    }
}

/// <summary>
/// The animation name is the one padded field a caller composes by hand, so the cap it has to respect is
/// published rather than left for the writer to discover.
/// </summary>
public class PapAnimationNameLengthTests
{
    [Fact]
    public void MaxNameLength_IsTheSlotMinusItsTerminator()
        => PapAnimation.MaxNameLength.Should().Be(31);

    [Fact]
    public void SetName_AcceptsANameAtTheCap()
    {
        var animation = new PapAnimation(null!);

        animation.SetName(new string('a', PapAnimation.MaxNameLength));

        animation.GetName().Should().HaveLength(PapAnimation.MaxNameLength);
    }

    [Fact]
    public void SetName_RefusesAName_PastTheCap()
    {
        var animation = new PapAnimation(null!);

        var rename = () => animation.SetName(new string('a', PapAnimation.MaxNameLength + 1));

        rename.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetName_RefusesAName_ThatOnlyOverrunsInBytes()
    {
        var animation = new PapAnimation(null!);

        var rename = () => animation.SetName(new string('é', PapAnimation.MaxNameLength));

        rename.Should().Throw<ArgumentException>();
    }
}
