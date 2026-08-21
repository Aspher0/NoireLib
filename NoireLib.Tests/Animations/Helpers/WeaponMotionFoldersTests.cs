using FluentAssertions;
using NoireLib.Animations.Helpers;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// The per-weapon folder names and the order a character's own folder degrades through when it holds no copy
/// of the animation being asked for. The groups used here are the real ResidentMotionType rows.
/// </summary>
public class WeaponMotionFoldersTests
{
    // The grouped rows of ResidentMotionType, which pair a weapon with its off-hand variants and gather the
    // folders one role can carry.
    private static IReadOnlyList<IReadOnlyList<string>> Groups() =>
    [
        ["bt_swd_sld", "bt_swd_emp"],
        ["bt_clw_clw"],
        ["bt_stf_emp", "bt_stf_sld", "bt_2st_emp"],
        ["bt_jst_sld", "bt_jst_emp", "bt_2js_emp"],
        ["bt_2ax_emp", "bt_swd_sld", "bt_swd_emp", "bt_2sw_emp", "bt_2gb_emp"],
        ["bt_clw_clw", "bt_2sp_emp", "bt_dgr_dgr", "bt_2kt_emp", "bt_2km_emp", "bt_bld_bld"],
    ];

    [Fact]
    public void Compose_JoinsTwoCodesIntoAFolderName()
        => WeaponMotionFolders.Compose("swd", "sld").Should().Be("bt_swd_sld");

    [Theory]
    [InlineData("bt_swd_sld", "swd")]
    [InlineData("bt_clw_clw", "clw")]
    [InlineData("bt_2sp_emp", "2sp")]
    public void MainCodeOf_ReadsTheMainHandCode(string folder, string expected)
        => WeaponMotionFolders.MainCodeOf(folder).Should().Be(expected);

    [Theory]
    [InlineData("bt_common")]
    [InlineData("bt_swd")]
    [InlineData("")]
    [InlineData(null)]
    public void MainCodeOf_AFolderThatIsNotOurShape_IsNull(string? folder)
        => WeaponMotionFolders.MainCodeOf(folder).Should().BeNull();

    [Fact]
    public void LadderFrom_StartsWithTheCharactersOwnFolder()
        => WeaponMotionFolders.LadderFrom("bt_2sw_emp", Groups())[0].Should().Be("bt_2sw_emp");

    [Fact]
    public void LadderFrom_NoReadableFolder_IsEmptySoTheCallerFallsBack()
        => WeaponMotionFolders.LadderFrom(null, Groups()).Should().BeEmpty();

    [Fact]
    public void LadderFrom_AFolderTheGameDoesNotShip_ReachesTheOneSharingItsMainHand()
        => WeaponMotionFolders.LadderFrom("bt_clw_bld", Groups()).Should().Contain("bt_clw_clw",
            "a modern fist weapon composes bt_clw_bld, which the game ships no folder for");

    [Fact]
    public void LadderFrom_PrefersTheSameMainHandBeforeTheGamesGroups()
    {
        var ladder = new List<string>(WeaponMotionFolders.LadderFrom("bt_swd_sld", Groups()));

        ladder.Should().HaveElementAt(0, "bt_swd_sld");
        ladder.Should().HaveElementAt(1, "bt_swd_emp");
        ladder.Should().Contain("bt_2ax_emp");
        ladder.IndexOf("bt_swd_emp").Should().BeLessThan(ladder.IndexOf("bt_2ax_emp"));
    }

    [Fact]
    public void LadderFrom_ReachesTheOffHandVariantsTheGameGroupsWithIt()
        => WeaponMotionFolders.LadderFrom("bt_stf_sld", Groups())
            .Should().Contain("bt_stf_emp").And.Contain("bt_2st_emp");

    [Fact]
    public void LadderFrom_NamesEachFolderOnce()
    {
        var ladder = WeaponMotionFolders.LadderFrom("bt_swd_sld", Groups());

        ladder.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void LadderFrom_NoGroups_IsJustTheOwnFolder()
        => WeaponMotionFolders.LadderFrom("bt_swd_sld", []).Should().Equal("bt_swd_sld");
}
