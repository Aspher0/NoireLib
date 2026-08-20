using FluentAssertions;
using NoireLib.Animations.PapFormat;
using System;
using System.IO;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Ported from PapEdit.Tests: locks the "read just the names" contract PapFormat exposes for callers (like a
/// retargeter) that only need to know what a .pap declares, not the full animation/TMB tree.
/// </summary>
public class PapAnimationNamesTests
{
    /// <summary> magic, version, count, model id, model type, variant, and three offsets. </summary>
    private const int HeaderLength = 26;

    /// <summary> The info offset field sits after magic, version, count, model id, type and variant. </summary>
    private const int InfoOffsetPosition = 14;

    /// <summary> A .pap header followed by one 40-byte entry per animation. </summary>
    private static byte[] BuildPap(params string[] names)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(0x20706170);          // magic "pap "
        writer.Write(1);                   // version
        writer.Write((short)names.Length);
        writer.Write((short)101);          // model id
        writer.Write((byte)1);             // model type
        writer.Write((byte)1);             // variant
        writer.Write(HeaderLength);        // info offset, immediately after this header
        writer.Write(0);                   // havok offset
        writer.Write(0);                   // footer offset

        foreach (var name in names)
        {
            var raw = new byte[32];
            Encoding.UTF8.GetBytes(name).CopyTo(raw, 0);
            writer.Write(raw);
            writer.Write((short)0);        // type
            writer.Write((short)0);        // havok index
            writer.Write(0);               // face
        }

        writer.Flush();
        return stream.ToArray();
    }

    [Fact]
    public void Read_ReturnsTheDeclaredNames()
        => PapAnimationNames.Read(BuildPap("beesknees", "thavdance")).Should().Equal("beesknees", "thavdance");

    [Fact]
    public void Read_TrimsThePaddingOffAName()
        => PapAnimationNames.Read(BuildPap("water")).Should().ContainSingle().Which.Should().Be("water");

    [Fact]
    public void Read_ReturnsNothingForAFileThatIsNotAPap()
        => PapAnimationNames.Read(Encoding.UTF8.GetBytes("this is not a pap file at all!!!")).Should().BeEmpty();

    [Fact]
    public void Read_ReturnsNothingForTruncatedData()
        => PapAnimationNames.Read([0x70, 0x61, 0x70, 0x20]).Should().BeEmpty();

    [Fact]
    public void Read_RefusesAnInfoOffsetPastTheEndOfTheFile()
    {
        var data = BuildPap("water");
        // Point the info offset far beyond the data rather than letting it read out of bounds.
        BitConverter.GetBytes(9999).CopyTo(data, InfoOffsetPosition);

        PapAnimationNames.Read(data).Should().BeEmpty();
    }

    [Theory]
    [InlineData("water", "water", true)]
    [InlineData("water", "thavdance", false)]
    [InlineData("Water", "water", false)]
    public void Matches_ComparesNamesExactly(string actual, string required, bool expected)
        => PapAnimationNames.Matches(actual, required).Should().Be(expected);

    [Fact]
    public void Matches_TreatsAnUnknownNameAsAMatch()
    {
        // An unreadable file is reported as unreadable elsewhere, not as a wrong name here.
        PapAnimationNames.Matches(null, "water").Should().BeTrue();
        PapAnimationNames.Matches("water", null).Should().BeTrue();
    }
}
