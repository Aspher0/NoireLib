using FluentAssertions;
using NoireLib.Helpers;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the stain color unpacking. The channel order was verified against dyes whose colors are
/// unambiguous, and those values are reused here so a change to the unpacking has to disagree with the
/// game's own table to pass.
/// </summary>
public class StainHelperTests
{
    [Theory]
    // Packed value, then the red, green and blue the game's dye table holds for it.
    [InlineData(0x00E4DFD0u, 228, 223, 208)]   // Snow White: near-neutral and light
    [InlineData(0x001E1E1Eu, 30, 30, 30)]      // Jet Black: near-neutral and dark
    [InlineData(0x00781A1Au, 120, 26, 26)]     // Dalamud Red: red dominant
    [InlineData(0x004F5766u, 79, 87, 102)]     // Ceruleum Blue: blue dominant
    [InlineData(0x008B9C63u, 139, 156, 99)]    // Meadow Green: green dominant
    public void ToColor_UnpacksTheChannelsInOrder(uint packed, int r, int g, int b)
    {
        var color = StainHelper.ToColor(packed);

        color.X.Should().BeApproximately(r / 255f, 1e-6f);
        color.Y.Should().BeApproximately(g / 255f, 1e-6f);
        color.Z.Should().BeApproximately(b / 255f, 1e-6f);
    }

    [Fact]
    public void ToColor_IgnoresTheUnusedHighByte()
    {
        // No row in the game's table sets it, so a value that did must not shift the channels.
        StainHelper.ToColor(0x00781A1Au).Should().Be(StainHelper.ToColor(0xFF781A1Au));
    }

    [Fact]
    public void ToColor_ProducesNormalizedComponents()
    {
        var white = StainHelper.ToColor(0x00FFFFFFu);
        white.X.Should().Be(1f);
        white.Y.Should().Be(1f);
        white.Z.Should().Be(1f);

        StainHelper.ToColor(0u).Should().Be(System.Numerics.Vector3.Zero);
    }
}
