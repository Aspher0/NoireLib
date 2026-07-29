using FluentAssertions;
using NoireLib.Helpers;
using System;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the scene default-stain read, the one furniture renders undyed. The bytes are synthesised, so this runs
/// without the game: what it pins is the header check, the pointer indirection, and the two guards that turn a
/// pointer which did not mean this into "states none" rather than an arbitrary colour.
/// </summary>
public class StainHelperSceneTests
{
    private const uint SceneMagic = 0x31424753;
    private const uint SceneLayerMagic = 0x314E4353;
    private const int PointerBase = 0x14;
    private const int StainPointerOffset = 0x40;

    /// <summary>Builds the smallest byte buffer the reader accepts, optionally pointing at a stain value.</summary>
    private static byte[] Scene(uint pointer = 0, ushort? stainAt = null)
    {
        var data = new byte[0x100];
        BitConverter.TryWriteBytes(data.AsSpan(0), SceneMagic);
        BitConverter.TryWriteBytes(data.AsSpan(0xC), SceneLayerMagic);
        BitConverter.TryWriteBytes(data.AsSpan(StainPointerOffset), pointer);

        if (stainAt is { } value)
            BitConverter.TryWriteBytes(data.AsSpan(PointerBase + (int)pointer), value);

        return data;
    }

    [Fact]
    public void TryReadSceneDefaultStain_StatedStain_IsReadThroughThePointer()
    {
        StainHelper.TryReadSceneDefaultStain(Scene(pointer: 0x60, stainAt: 42), out var stain).Should().BeTrue();
        stain.Should().Be(42);
    }

    [Fact]
    public void TryReadSceneDefaultStain_NullPointer_ReadsAsStatingNone()
    {
        StainHelper.TryReadSceneDefaultStain(Scene(pointer: 0), out var stain).Should().BeTrue();
        stain.Should().Be(0, "the scene is readable and simply names no stain");
    }

    [Fact]
    public void TryReadSceneDefaultStain_ValueBeyondTheStainTable_ReadsAsStatingNone()
    {
        StainHelper.TryReadSceneDefaultStain(Scene(pointer: 0x60, stainAt: 40000), out var stain).Should().BeTrue();
        stain.Should().Be(0, "the pointer did not mean a stain here, and an arbitrary colour is worse than none");
    }

    [Fact]
    public void TryReadSceneDefaultStain_PointerPastTheEnd_ReadsAsStatingNone()
    {
        StainHelper.TryReadSceneDefaultStain(Scene(pointer: 0xF000), out var stain).Should().BeTrue();
        stain.Should().Be(0);
    }

    [Fact]
    public void TryReadSceneDefaultStain_NotAScene_IsRefused()
    {
        var wrongMagic = Scene(pointer: 0x60, stainAt: 42);
        BitConverter.TryWriteBytes(wrongMagic.AsSpan(0), 0xDEADBEEFu);

        StainHelper.TryReadSceneDefaultStain(wrongMagic, out _).Should().BeFalse();
        StainHelper.TryReadSceneDefaultStain(new byte[8], out _).Should().BeFalse("a file too short to hold the header is not a scene");
    }
}
