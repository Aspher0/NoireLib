using FluentAssertions;
using NoireLib.Draw3D.Assets;
using NoireLib.Helpers;
using System;
using System.Text;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks CRC-32 against the two things that already depend on it and can never change: a share code produced by an
/// earlier build must still decode, and an FFXIV shader name must still hash to the id the game files carry.
/// </summary>
public class Crc32HelperTests
{
    [Fact]
    public void Compute_StandardConfiguration_MatchesTheKnownCheckVector()
    {
        // The published CRC-32 check value: the checksum of "123456789" under the IEEE polynomial.
        Crc32Helper.Compute(Encoding.ASCII.GetBytes("123456789")).Should().Be(0xCBF43926u);
    }

    [Fact]
    public void Compute_TwoSpans_MatchesTheConcatenation()
    {
        var whole = Encoding.ASCII.GetBytes("123456789");

        Crc32Helper.Compute(whole.AsSpan(0, 4), whole.AsSpan(4))
            .Should().Be(Crc32Helper.Compute(whole), "a split payload must checksum as though it had been one buffer");
    }

    [Fact]
    public void Compute_EmptyInput_IsZero()
        => Crc32Helper.Compute([]).Should().Be(0u);

    [Fact]
    public void Compute_OpenForm_ReproducesTheShaderNameConfiguration()
    {
        // FFXIV shader ids use the reflected polynomial with a zero initial value and no final inversion.
        Crc32Helper.Compute(Encoding.ASCII.GetBytes("g_SamplerDiffuse"), seed: 0u, finalXor: 0u)
            .Should().Be(0x115306BEu);
    }

    [Fact]
    public void GameShaderNames_IdOf_StillProducesTheIdsInTheGameFiles()
    {
        GameShaderNames.IdOf("g_SamplerDiffuse").Should().Be(0x115306BEu);
        GameShaderNames.IdOf("g_SamplerNormal").Should().Be(0x0C5EC1F1u);
        GameShaderNames.IdOf("g_DiffuseColor").Should().Be(0x2C2A34DDu);
    }

    [Fact]
    public void GameShaderNames_NameOf_RoundTripsAKnownId()
        => GameShaderNames.NameOf(0x115306BEu).Should().Be("g_SamplerDiffuse");
}
