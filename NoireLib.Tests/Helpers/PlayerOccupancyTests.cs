using Dalamud.Game.ClientState.Conditions;
using FluentAssertions;
using NoireLib.Helpers;
using System.Collections.Generic;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks what keeps the local player from acting: each cause on its own, every occupying flag raised, and the flags
/// a caller chooses to ignore.
/// </summary>
public sealed class PlayerOccupancyTests
{
    private static PlayerOccupancy Evaluate(
        IReadOnlySet<ConditionFlag> raised,
        ConditionFlag[]? ignored = null,
        bool isDead = false,
        bool isCasting = false,
        bool isTargetable = true,
        bool isAirborne = false)
        => CharacterHelper.EvaluateOccupancy(isDead, isCasting, isTargetable, isAirborne, raised.Contains, ignored);

    [Fact]
    public void EvaluateOccupancy_NothingRaised_IsFree()
    {
        var occupancy = Evaluate(new HashSet<ConditionFlag>());

        occupancy.IsOccupied.Should().BeFalse();
        occupancy.Describe().Should().BeEmpty();
    }

    [Fact]
    public void EvaluateOccupancy_EachCauseOnItsOwn_Occupies()
    {
        var none = new HashSet<ConditionFlag>();

        Evaluate(none, isDead: true).IsOccupied.Should().BeTrue();
        Evaluate(none, isCasting: true).IsOccupied.Should().BeTrue();
        Evaluate(none, isTargetable: false).IsOccupied.Should().BeTrue();
        Evaluate(none, isAirborne: true).IsOccupied.Should().BeTrue("because a player in the air cannot interact");
        PlayerOccupancy.NoPlayer.IsOccupied.Should().BeTrue();
    }

    [Theory]
    [InlineData(ConditionFlag.Jumping)]
    [InlineData(ConditionFlag.OccupiedInEvent)]
    [InlineData(ConditionFlag.OccupiedSummoningBell)]
    [InlineData(ConditionFlag.Fishing)]
    [InlineData(ConditionFlag.MountOrOrnamentTransition)]
    [InlineData(ConditionFlag.UsingHousingFunctions)]
    public void EvaluateOccupancy_OccupyingFlag_IsReported(ConditionFlag flag)
    {
        var occupancy = Evaluate(new HashSet<ConditionFlag> { flag });

        occupancy.IsOccupied.Should().BeTrue();
        occupancy.ActiveConditions.Should().Equal(flag);
    }

    [Fact]
    public void EvaluateOccupancy_UnlistedFlag_DoesNotOccupy()
        => Evaluate(new HashSet<ConditionFlag> { ConditionFlag.Mounted }).IsOccupied.Should().BeFalse();

    [Fact]
    public void EvaluateOccupancy_IgnoredFlag_IsLeftOut()
    {
        var raised = new HashSet<ConditionFlag> { ConditionFlag.OccupiedInEvent, ConditionFlag.InCombat };

        var occupancy = Evaluate(raised, [ConditionFlag.OccupiedInEvent]);

        occupancy.ActiveConditions.Should().Equal(ConditionFlag.InCombat);
        Evaluate(new HashSet<ConditionFlag> { ConditionFlag.OccupiedInEvent }, [ConditionFlag.OccupiedInEvent]).IsOccupied.Should().BeFalse();
    }

    [Fact]
    public void Describe_ListsEveryCause()
        => Evaluate(new HashSet<ConditionFlag> { ConditionFlag.InCombat }, isCasting: true, isAirborne: true)
            .Describe().Should().Be("casting, airborne, InCombat");
}
