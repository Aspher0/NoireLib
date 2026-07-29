using FluentAssertions;
using NoireLib.Helpers;
using System;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the byte-buffer reads a binary game file is parsed with: the terminator convention, the packed-string walk,
/// and the little-endian vector layout.
/// </summary>
public class BufferHelperTests
{
    [Fact]
    public void ReadNullTerminatedString_StopsAtTheTerminator()
    {
        byte[] data = [(byte)'a', (byte)'b', 0, (byte)'c'];

        BufferHelper.ReadNullTerminatedString(data, 0).Should().Be("ab");
        BufferHelper.ReadNullTerminatedString(data, 3).Should().Be("c", "a run without a terminator ends at the buffer");
    }

    [Fact]
    public void ReadNullTerminatedString_WalksPackedStrings()
    {
        var data = Encoding.UTF8.GetBytes("one\0two\0three\0");
        var at = 0;

        BufferHelper.ReadNullTerminatedString(data, at, out at).Should().Be("one");
        BufferHelper.ReadNullTerminatedString(data, at, out at).Should().Be("two");
        BufferHelper.ReadNullTerminatedString(data, at, out at).Should().Be("three");
        at.Should().Be(data.Length);
    }

    [Fact]
    public void ReadNullTerminatedString_MultiByteCharacters_AdvanceByBytesNotCharacters()
    {
        var data = Encoding.UTF8.GetBytes("éé\0next\0");

        BufferHelper.ReadNullTerminatedString(data, 0, out var at).Should().Be("éé");
        at.Should().Be(5, "each of those characters is two bytes, so the next string starts past four plus the terminator");
        BufferHelper.ReadNullTerminatedString(data, at).Should().Be("next");
    }

    [Fact]
    public void ReadNullTerminatedString_SlicedBuffer_CannotRunPastTheSlice()
    {
        var data = Encoding.UTF8.GetBytes("abcdef");

        BufferHelper.ReadNullTerminatedString(data.AsSpan(0, 3), 0).Should().Be("abc");
    }

    [Fact]
    public void ReadNullTerminatedString_StartOutsideTheBuffer_IsEmpty()
    {
        byte[] data = [(byte)'a'];

        BufferHelper.ReadNullTerminatedString(data, 5).Should().BeEmpty();
        BufferHelper.ReadNullTerminatedString(data, -1).Should().BeEmpty();
    }

    [Fact]
    public void ReadNullTerminatedString_ExplicitEncoding_IsHonoured()
    {
        byte[] data = [0x41, 0x42, 0];

        BufferHelper.ReadNullTerminatedString(data, 0, Encoding.ASCII).Should().Be("AB");
    }

    [Fact]
    public void ReadVector3_ReadsThreeLittleEndianFloats()
    {
        var data = new byte[12];
        BitConverter.TryWriteBytes(data.AsSpan(0), 1f);
        BitConverter.TryWriteBytes(data.AsSpan(4), 2f);
        BitConverter.TryWriteBytes(data.AsSpan(8), 3f);

        BufferHelper.ReadVector3(data, 0).Should().Be(new System.Numerics.Vector3(1, 2, 3));
    }

    [Fact]
    public void ReadVector4_ReadsTheSixteenByteRowAtAnOffset()
    {
        var data = new byte[32];
        for (var i = 0; i < 4; i++)
            BitConverter.TryWriteBytes(data.AsSpan(16 + i * 4), i + 1f);

        BufferHelper.ReadVector4(data, 16).Should().Be(new System.Numerics.Vector4(1, 2, 3, 4));
    }
}
