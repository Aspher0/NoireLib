using FluentAssertions;
using NoireLib.Helpers;
using System.Linq;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks how a color finds its dye and how a dye is marked special, over a small hand-built table that mirrors the
/// game's layout: spectrum items shared by many dyes and general-purpose items applying one dye each.
/// </summary>
public class DyeHelperTests
{
    private const uint StandardSpectrum = 52254;
    private const uint WideSpectrum = 52255;
    private const uint GeneralPurposeJetBlack = 13115;

    private static readonly GameDye[] Table = DyeHelper.MarkSpecial(
    [
        new GameDye(1, "Snow White", StainHelper.ToColor(0xE4DFD0), StandardSpectrum, false, false, true),
        new GameDye(6, "Soot Black", StainHelper.ToColor(0x2B2923), StandardSpectrum, false, false, true),
        new GameDye(86, "Ruby Red", StainHelper.ToColor(0xE40011), WideSpectrum, false, false, true),
        new GameDye(87, "Cherry Pink", StainHelper.ToColor(0xF5379B), WideSpectrum, false, false, true),
        new GameDye(102, "Jet Black", StainHelper.ToColor(0x1E1E1E), GeneralPurposeJetBlack, false, false, true),
    ]).ToArray();

    [Fact]
    public void MarkSpecial_FlagsOnlyTheDyesWhoseItemAppliesThemAlone()
    {
        Table.Should().ContainSingle(static dye => dye.IsSpecial).Which.StainId.Should().Be(102u);
    }

    [Fact]
    public void Nearest_PicksTheDyeOfTheClosestColor()
    {
        DyeHelper.Nearest(Table, "2B292300", allowSpecialDyes: true)!.Value.StainId.Should().Be(6u);
    }

    [Fact]
    public void Nearest_TakesASpecialDyeWhenItIsClosestAndAllowed()
    {
        DyeHelper.Nearest(Table, StainHelper.ToColor(0x1E1E1E), allowSpecialDyes: true)!.Value.StainId.Should().Be(102u);
    }

    [Fact]
    public void Nearest_FallsBackToASpectrumDyeWhenSpecialDyesAreRefused()
    {
        var dye = DyeHelper.Nearest(Table, StainHelper.ToColor(0x1E1E1E), allowSpecialDyes: false)!.Value;

        dye.StainId.Should().Be(6u);
        dye.ItemId.Should().Be(StandardSpectrum);
    }

    [Fact]
    public void Nearest_OfTextThatIsNotAColor_IsNull()
    {
        DyeHelper.Nearest(Table, "not a color", allowSpecialDyes: true).Should().BeNull();
    }

    [Fact]
    public void Nearest_OverNoDye_IsNull()
    {
        DyeHelper.Nearest([], Vector3.One, allowSpecialDyes: true).Should().BeNull();
    }
}
