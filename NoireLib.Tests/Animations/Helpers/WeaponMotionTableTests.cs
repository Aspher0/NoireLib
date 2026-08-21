using FluentAssertions;
using NoireLib.Animations.Helpers;
using System;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The weapon-to-motion table the game ships at chara/xls/weapontype/motion.wtd. The entries used here are
/// the real ones read out of the shipped file, so a change to the reader that disagrees with the game fails
/// rather than agrees with itself.
/// </summary>
public class WeaponMotionTableTests
{
    private static uint Packed(string code)
        => (uint)((code[0] << 16) | (code[1] << 8) | code[2]);

    private static byte[] File(ushort version, params (uint SetId, string Code)[] entries)
    {
        var bytes = new List<byte>();

        bytes.AddRange(BitConverter.GetBytes(version));
        bytes.AddRange(BitConverter.GetBytes((ushort)entries.Length));

        foreach (var (setId, code) in entries)
        {
            bytes.AddRange(BitConverter.GetBytes(setId));
            bytes.AddRange(BitConverter.GetBytes(Packed(code)));
        }

        return [.. bytes];
    }

    // A slice of the shipped table, keeping the entries the fallback ladder depends on.
    private static byte[] ShippedSlice() => File(1,
        (101, "sld"), (201, "swd"), (301, "clw"), (401, "2ax"), (501, "2sp"), (601, "2bw"),
        (675, "emp"), (690, "2bw"), (693, "emp"), (1601, "clw"), (1801, "dgr"), (2601, "chk"),
        (2901, "brs"), (2951, "plt"), (3001, "bld"), (7001, "min"), (8041, "bld"));

    [Fact]
    public void Parse_ReadsTheHeaderAndEveryEntry()
        => WeaponMotionTable.Parse(ShippedSlice())!.Count.Should().Be(17);

    [Fact]
    public void CodeFor_EmptyHand_IsTheEmptyCode()
        => WeaponMotionTable.Parse(ShippedSlice())!.CodeFor(0).Should().Be(WeaponMotionTable.EmptyCode);

    [Theory]
    // An exact entry answers with its own code.
    [InlineData(101, "sld")]
    [InlineData(201, "swd")]
    [InlineData(301, "clw")]
    [InlineData(1601, "clw")]
    [InlineData(2951, "plt")]
    // A set id between two entries takes the lower one, which is how a weapon range shares one motion.
    [InlineData(351, "clw")]
    [InlineData(1851, "dgr")]
    [InlineData(2651, "chk")]
    [InlineData(3051, "bld")]
    // The table closes a range with an explicit empty entry rather than a gap.
    [InlineData(680, "emp")]
    [InlineData(695, "emp")]
    // A set id below the first entry clamps to it rather than reading off the front.
    [InlineData(1, "sld")]
    [InlineData(100, "sld")]
    // Past the last entry the lookup keeps the last code, so a modern fist off-hand composes a folder the game
    // does not ship and the caller's fallback ladder has to catch it.
    [InlineData(8804, "bld")]
    public void CodeFor_TakesTheHighestEntryTheSetIdReaches(int weaponModelSetId, string expected)
        => WeaponMotionTable.Parse(ShippedSlice())!.CodeFor((ushort)weaponModelSetId).Should().Be(expected);

    [Fact]
    public void Parse_UnknownVersion_Declines()
        => WeaponMotionTable.Parse(File(2, (101, "sld"))).Should().BeNull();

    [Fact]
    public void Parse_TruncatedFile_Declines()
        => WeaponMotionTable.Parse(ShippedSlice().AsSpan(0, 20)).Should().BeNull();

    [Fact]
    public void Parse_EmptyFile_Declines()
        => WeaponMotionTable.Parse([]).Should().BeNull();

    [Fact]
    public void Parse_NoEntries_Declines()
        => WeaponMotionTable.Parse(File(1)).Should().BeNull();

    [Fact]
    public void Parse_EntriesOutOfOrder_Declines()
        => WeaponMotionTable.Parse(File(1, (201, "swd"), (101, "sld"))).Should().BeNull();
}
