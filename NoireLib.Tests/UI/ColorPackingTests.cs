using Dalamud.Bindings.ImGui;
using FluentAssertions;
using NoireLib.Helpers;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Pins <see cref="ColorHelper.Vector4ToUint"/> to ImGui's own packing, bit for bit.
/// </summary>
/// <remarks>
/// The packing moved from a native call into managed arithmetic for speed. That is only sound while the two agree
/// exactly: the vertex colours it produces are handed straight to ImGui, so a single rounding step out of line is a
/// colour that is wrong by one and a gradient that bands. Asserted against the native converter rather than against a
/// table written by hand, so it keeps holding if ImGui's rounding ever changes.<br/>
/// Needs a context, since the converter it compares against is a native call.
/// </remarks>
[Collection(NoireUiTestCollection.Name)]
public sealed class ColorPackingTests : IClassFixture<UiHarness>
{
    private readonly UiHarness harness;

    public ColorPackingTests(UiHarness harness) => this.harness = harness;

    [Fact]
    public void Vector4ToUint_AcrossTheUnitCube_MatchesImGui()
    {
        harness.Draw(static () =>
        {
            for (var r = 0; r <= 32; r++)
            {
                for (var g = 0; g <= 32; g++)
                {
                    for (var b = 0; b <= 32; b++)
                    {
                        for (var a = 0; a <= 32; a += 8)
                        {
                            var colour = new Vector4(r / 32f, g / 32f, b / 32f, a / 32f);

                            ColorHelper.Vector4ToUint(colour).Should().Be(ImGui.ColorConvertFloat4ToU32(colour));
                        }
                    }
                }
            }
        });
    }

    [Theory]
    [InlineData(0f, 0f, 0f, 0f)]
    [InlineData(1f, 1f, 1f, 1f)]
    [InlineData(-1f, 2f, 0.5f, 100f)]
    [InlineData(0.0019f, 0.0021f, 0.998f, 0.999f)]
    [InlineData(float.Epsilon, 0.5f, 1f - float.Epsilon, 0.25f)]
    public void Vector4ToUint_AtTheEdges_MatchesImGui(float r, float g, float b, float a)
    {
        harness.Draw(() =>
        {
            var colour = new Vector4(r, g, b, a);

            // Out of range values included on purpose: ImGui saturates rather than wrapping, and a packer that wrapped
            // would turn an over-bright colour into a dark one.
            ColorHelper.Vector4ToUint(colour).Should().Be(ImGui.ColorConvertFloat4ToU32(colour));
        });
    }

    [Fact]
    public void UintAlpha_ReturnsWhatWasPacked()
    {
        for (var step = 0; step <= 255; step++)
        {
            var packed = ColorHelper.Vector4ToUint(new Vector4(0.5f, 0.5f, 0.5f, step / 255f));

            ColorHelper.UintAlpha(packed).Should().BeApproximately(step / 255f, 0.002f);
        }
    }
}
