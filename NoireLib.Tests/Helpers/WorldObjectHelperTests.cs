using FluentAssertions;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the world object lookup: the index survives its disk encoding, answers one row across every territory it
/// stands in and only the territories asked for, groups shared groups by path, and a query always names a criterion.
/// </summary>
public sealed class WorldObjectHelperTests
{
    private const uint LowerDecks = 129;
    private const uint OldGridania = 133;
    private const uint MarketBoard = 2000402;

    private static WorldObjectIndex BuildIndex() => new(
        [
            new IndexedPlacement(PlacementKinds.EventObject, MarketBoard, LowerDecks, 4172329, new Vector3(1f, 2f, 3f), 0.5f, 3, -1, 0, 0, []),
            new IndexedPlacement(PlacementKinds.EventObject, MarketBoard, OldGridania, 3653447, new Vector3(4f, 5f, 6f), 1.5f, 3, -1, 0, 0, [OldGridania]),
            new IndexedPlacement(PlacementKinds.EventNpc, MarketBoard, LowerDecks, 1, Vector3.Zero, 0f, 1, -1, 0, 0, []),
            new IndexedPlacement(PlacementKinds.SharedGroup, 0, 341, 7, Vector3.One, 0f, 0, 0, 12, 1, []),
            new IndexedPlacement(PlacementKinds.SharedGroup, 0, 341, 8, Vector3.One, 0f, 4, 1, 0, 0, []),
        ],
        ["bgcommon/world/aet/shared/for_bg/sgbg_w_aet_01.sgb", "bgcommon/world/sys/shared/for_bg/sgbg_door.sgb"]);

    [Fact]
    public void Index_SurvivesItsDiskEncoding()
    {
        var index = BuildIndex();

        var read = WorldObjectIndex.FromFile(index.ToFile());

        read.Placements.Should().HaveCount(5);
        read.AssetPaths.Should().Equal(index.AssetPaths);
        read.Placements[1].Should().BeEquivalentTo(index.Placements[1]);
        read.Placements[3].FestivalId.Should().Be(12);
    }

    [Fact]
    public void Index_ReturnsEveryPlacementOfARowAcrossTerritories()
        => BuildIndex().PlacementsOf(PlacementKinds.EventObject, MarketBoard, null).Should().HaveCount(2, "because one base id stands in several cities");

    [Fact]
    public void Index_KeepsOnlyTheTerritoriesAskedFor()
        => BuildIndex().PlacementsOf(PlacementKinds.EventObject, MarketBoard, new HashSet<uint> { OldGridania })
            .Should().ContainSingle().Which.InstanceId.Should().Be(3653447u);

    [Fact]
    public void Index_SeparatesKindsSharingAnId()
        => BuildIndex().PlacementsOf(PlacementKinds.EventNpc, MarketBoard, null).Should().ContainSingle();

    [Fact]
    public void Index_MatchesSharedGroupsByPath()
        => BuildIndex().SharedGroupsMatching("SGBG_W_AET_", null).Should().ContainSingle().Which.InstanceId.Should().Be(7u);

    [Fact]
    public void NameMatches_ComparesWithoutCase()
    {
        WorldObjectHelper.NameMatches("Market Board", "market board", NameMatch.Exact).Should().BeTrue();
        WorldObjectHelper.NameMatches("market board", "board", NameMatch.Exact).Should().BeFalse();
        WorldObjectHelper.NameMatches("market board", "BOARD", NameMatch.Contains).Should().BeTrue();
        WorldObjectHelper.NameMatches(null, "board", NameMatch.Contains).Should().BeFalse();
    }

    [Fact]
    public void FoldVariants_ReadsEachPlaceOnceInAscendingOrder()
    {
        var aliases = new Dictionary<uint, uint> { [1000] = LowerDecks };

        WorldObjectHelper.FoldVariants([1000u, 128u, LowerDecks, 0u], aliases).Should().Equal(128u, LowerDecks);
    }

    [Fact]
    public void OrderByDistance_PutsTheNearestPlacementFirst()
    {
        var far = Placement(new Vector3(50f, 0f, 0f), 1);
        var near = Placement(new Vector3(2f, 0f, 0f), 2);

        WorldObjectHelper.OrderByDistance([far, near], Vector3.Zero).Select(static placement => placement.InstanceId).Should().Equal(2u, 1u);
    }

    [Fact]
    public void Find_RefusesAQueryWithoutCriterion()
    {
        var find = () => WorldObjectHelper.Find(new WorldObjectQuery());

        find.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Shortcuts_PickTheKindsTheirCriterionCanAnswer()
    {
        WorldObjectQuery.ByScript("CmnDefMarketBoard").Kinds.Should().Be(PlacementKinds.EventObject | PlacementKinds.EventNpc);
        WorldObjectQuery.ByAssetPath("sgbg_w_aet_").Kinds.Should().Be(PlacementKinds.SharedGroup);
        WorldObjectQuery.ByHandlerContent(EventHandlerContent.SpecialShop).HandlerContent.Should().Be(EventHandlerContent.SpecialShop);
        WorldObjectQuery.ByBaseId(PlacementKinds.EventObject, MarketBoard).BaseIds.Should().Contain(MarketBoard);
        new WorldObjectQuery().Scope.Should().BeSameAs(PlacementScope.World);
    }

    private static WorldObjectPlacement Placement(Vector3 position, uint instanceId)
        => new(PlacementKinds.EventObject, MarketBoard, LowerDecks, string.Empty, 0, 0, Vector2.Zero, position, 0f, instanceId,
            LevelFileHelper.Files.PlanLive, string.Empty, 0, 0, []);
}
