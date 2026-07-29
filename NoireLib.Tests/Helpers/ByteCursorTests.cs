using FluentAssertions;
using NoireLib.Helpers;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the sequential reader: each read advances by its own width, and a read past the end throws rather than
/// returning a plausible wrong value from a truncated file.
/// </summary>
public class ByteCursorTests
{
    [Fact]
    public void Reads_AdvanceByTheirOwnWidth()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07];
        var cursor = new ByteCursor(data);

        cursor.U8().Should().Be(0x01);
        cursor.Position.Should().Be(1);

        cursor.U16().Should().Be(0x0302, "little endian, so the low byte comes first");
        cursor.Position.Should().Be(3);

        cursor.U32().Should().Be(0x07060504u);
        cursor.Position.Should().Be(7);
    }

    [Fact]
    public void Skip_MovesWithoutReading_AndAcceptsANegativeCount()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04];
        var cursor = new ByteCursor(data);

        cursor.Skip(2);
        cursor.U8().Should().Be(0x03);

        cursor.Skip(-3);
        cursor.U8().Should().Be(0x01);
    }

    [Fact]
    public void Position_IsSettableToSeek()
    {
        byte[] data = [0x01, 0x02, 0x03, 0x04];
        var cursor = new ByteCursor(data) { Position = 3 };

        cursor.U8().Should().Be(0x04);
    }

    [Fact]
    public void Read_PastTheEnd_Throws()
    {
        byte[] data = [0x01, 0x02];
        var cursor = new ByteCursor(data) { Position = 1 };

        var read = () => cursor.U32();

        read.Should().Throw<ArgumentException>("a truncated file must fail at the read, not hand back a wrong value");
    }
}
