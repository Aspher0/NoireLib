using FluentAssertions;
using NoireLib.Helpers;
using System.Collections.Generic;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks the pure rules in the game-data helpers: the ones that resolve a path, project a coordinate, factor a name,
/// pick a canonical territory, or reduce a set of placed objects. Every one of them is a function over its inputs, so
/// none of these needs a game. The sheet and level-file reads around them are exercised in game.
/// </summary>
public sealed class GameDataHelperTests
{
    [Theory]
    [InlineData("ffxiv/fst_f1/fld/f1f1/level/f1f1", "bg/ffxiv/fst_f1/fld/f1f1/level/")]
    [InlineData("ex4/05_zon_z5/dun/z5d1/level/z5d1", "bg/ex4/05_zon_z5/dun/z5d1/level/")]
    public void ResolveLevelDirectory_TurnsABgStringIntoItsLevelDirectory(string bg, string expected)
        => LevelFileHelper.ResolveLevelDirectory(bg).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("ffxiv/fst_f1/fld/f1f1")]
    public void ResolveLevelDirectory_ReturnsNullWhenThereIsNoLevelSegment(string bg)
        => LevelFileHelper.ResolveLevelDirectory(bg).Should().BeNull();

    /// <summary>
    /// The asset root is the directory a territory's <c>level/</c> and <c>collision/</c> folders sit in, which is
    /// what anything built from its files is keyed on, since several territories can share one.
    /// </summary>
    [Theory]
    [InlineData("ffxiv/fst_f1/fld/f1f1/level/f1f1", "bg/ffxiv/fst_f1/fld/f1f1/")]
    [InlineData("ex4/05_zon_z5/dun/z5d1/level/z5d1", "bg/ex4/05_zon_z5/dun/z5d1/")]
    public void ResolveLevelRoot_TurnsABgStringIntoItsAssetRoot(string bg, string expected)
        => LevelFileHelper.ResolveLevelRoot(bg).Should().Be(expected);

    [Theory]
    [InlineData("")]
    [InlineData("ffxiv/fst_f1/fld/f1f1")]
    public void ResolveLevelRoot_ReturnsNullWhenThereIsNoLevelSegment(string bg)
        => LevelFileHelper.ResolveLevelRoot(bg).Should().BeNull();

    /// <summary>The two are the same path, one segment apart, and must never drift from each other.</summary>
    [Theory]
    [InlineData("ffxiv/fst_f1/fld/f1f1/level/f1f1")]
    [InlineData("ffxiv/hou_ha1/hou/dyn_a1/level/dyn_a1")]
    public void ResolveLevelRoot_IsTheLevelDirectoryWithoutItsLastSegment(string bg)
        => LevelFileHelper.ResolveLevelDirectory(bg).Should().Be(LevelFileHelper.ResolveLevelRoot(bg) + "level/");

    [Theory]
    [InlineData("ffxiv/wil_w1/twn/w1t2/level/w1t2", "ffxiv/wil_w1")]
    [InlineData("ffxiv/hou_ha1/hou/dyn_a1/level/dyn_a1", "ffxiv/hou_ha1")]
    [InlineData("ffxiv", "")]
    [InlineData("", "")]
    public void ResolveRegionRoot_TakesTheFirstTwoSegments(string bg, string expected)
        => LevelFileHelper.ResolveRegionRoot(bg).Should().Be(expected);

    [Fact]
    public void MarkerToWorld_AndBack_RoundTrips()
    {
        const float sizeFactor = 200f;
        const float offsetX = -224f;
        const float offsetY = -96f;

        var (x, z) = MapCoordinateHelper.MarkerToWorld(1536f, 512f, sizeFactor, offsetX, offsetY);
        var (markerX, markerY) = MapCoordinateHelper.WorldToMarker(x, z, sizeFactor, offsetX, offsetY);

        markerX.Should().BeApproximately(1536f, 0.001f);
        markerY.Should().BeApproximately(512f, 0.001f);
    }

    [Fact]
    public void MarkerToWorld_PutsTheMapCentreAtTheNegatedOffset()
    {
        // The marker space is 2048 across with the world origin at its centre, so the centre pixel is the offset back.
        var (x, z) = MapCoordinateHelper.MarkerToWorld(1024f, 1024f, 100f, 40f, -60f);

        x.Should().BeApproximately(-40f, 0.001f);
        z.Should().BeApproximately(60f, 0.001f);
    }

    [Fact]
    public void MapCoordinate_AndBack_RoundTrips()
    {
        const float sizeFactor = 95f;
        const float offset = -336f;

        var coordinate = MapCoordinateHelper.WorldToMapCoordinate(120.5f, sizeFactor, offset);
        MapCoordinateHelper.MapCoordinateToWorld(coordinate, sizeFactor, offset)
            .Should().BeApproximately(120.5f, 0.001f);
    }

    [Fact]
    public void TryFindNearestMarker_ComparesOnTheGroundPlaneOnly()
    {
        var markers = new List<ProjectedMapMarker>
        {
            new(new MapMarkerEntry(MapMarkerDataType.AethernetShard, 100, 0, 0, 0), new Vector3(0, 0, 0), 1),
            new(new MapMarkerEntry(MapMarkerDataType.AethernetShard, 200, 0, 0, 0), new Vector3(50, 0, 0), 1),
        };

        // A height far from either marker must not decide the match, since a marker carries no height at all.
        MapCoordinateHelper.TryFindNearestMarker(markers, new Vector3(40, 900, 0), out var nearest).Should().BeTrue();
        nearest.Marker.DataKey.Should().Be(200);
    }

    [Fact]
    public void TryFindNearestMarker_ReportsFailureWithNothingToMatch()
        => MapCoordinateHelper.TryFindNearestMarker([], Vector3.Zero, out _).Should().BeFalse();

    [Fact]
    public void BuildAliases_FoldsTerritoriesSharingAPathAndAPlaceName()
    {
        var aliases = TerritoryHelper.BuildAliases(
        [
            new TerritoryEntry(148, "ffxiv/fst_f1/fld/f1f2/level/f1f2", 53),
            new TerritoryEntry(397, "ffxiv/fst_f1/fld/f1f2/level/f1f2", 53),
            new TerritoryEntry(511, "ffxiv/fst_f1/fld/f1f2/level/f1f2", 53),
        ]);

        aliases.Should().HaveCount(2);
        aliases[397].Should().Be(148);
        aliases[511].Should().Be(148);
        aliases.Should().NotContainKey(148);
    }

    [Fact]
    public void BuildAliases_KeepsTerritoriesApartWhenOnlyThePathMatches()
    {
        // An apartment and the private chambers are built from one level file and are genuinely different places.
        var aliases = TerritoryHelper.BuildAliases(
        [
            new TerritoryEntry(608, "ffxiv/hou_ha1/hou/dyn_a1/level/dyn_a1", 2109),
            new TerritoryEntry(609, "ffxiv/hou_ha1/hou/dyn_a1/level/dyn_a1", 2110),
        ]);

        aliases.Should().BeEmpty();
    }

    [Fact]
    public void BuildAliases_LetsAPreferredTerritoryWinItsGroup()
    {
        var aliases = TerritoryHelper.BuildAliases(
            [
                new TerritoryEntry(136, "ffxiv/hou_ha1/hou/s1h1/level/s1h1", 502),
                new TerritoryEntry(339, "ffxiv/hou_ha1/hou/s1h1/level/s1h1", 502),
            ],
            new HashSet<uint> { 339 });

        aliases.Should().ContainSingle();
        aliases[136].Should().Be(339);
    }

    [Fact]
    public void ResolveAlias_ReturnsATerritoryUnchangedWhenItIsItsOwnCanonical()
    {
        var aliases = new Dictionary<uint, uint> { [397] = 148 };

        TerritoryHelper.ResolveAlias(aliases, 397).Should().Be(148u);
        TerritoryHelper.ResolveAlias(aliases, 148).Should().Be(148u);
        TerritoryHelper.ResolveAlias(null, 148).Should().Be(148u);
    }

    [Fact]
    public void SharedName_FactorsTheKindOutOfADistrictsInteriorNames()
    {
        HousingHelper.SharedName(["Private House - Mist", "Private House - The Lavender Beds", "Private House - The Goblet"])
            .Should().Be("Private House");
    }

    [Theory]
    [InlineData("Private Cottage")]
    public void SharedName_NeedsTwoNamesToShareAnything(string only)
        => HousingHelper.SharedName([only]).Should().BeEmpty();

    [Fact]
    public void SharedName_ReturnsNothingWhenTheDistrictComesFirst()
        => HousingHelper.SharedName(["Mist - Private House", "The Goblet - Private House"]).Should().BeEmpty();

    [Theory]
    [InlineData("Private House", "Dark Minimalist", "Private House (Dark Minimalist)")]
    [InlineData("", "Dark Minimalist", "Dark Minimalist")]
    [InlineData("Private House", "", "Private House")]
    [InlineData("", "", "")]
    public void ComposeName_UsesWhicheverPartsAreKnown(string kind, string design, string expected)
        => HousingHelper.ComposeName(kind, design).Should().Be(expected);

    [Theory]
    [InlineData((byte)0, HousingInteriorKind.Cottage)]
    [InlineData((byte)1, HousingInteriorKind.House)]
    [InlineData((byte)2, HousingInteriorKind.Mansion)]
    public void FromPlotSize_MapsEachSizeToItsInterior(byte size, HousingInteriorKind expected)
        => HousingInteriorKinds.FromPlotSize(size).Should().Be(expected);

    [Fact]
    public void FromPlotSize_ReturnsNullForASizeTheSheetDoesNotDescribe()
        => HousingInteriorKinds.FromPlotSize(9).Should().BeNull();

    [Theory]
    [InlineData(true, false, 0u, EstateKind.Apartment)]
    [InlineData(false, true, 0u, EstateKind.SharedEstate)]
    [InlineData(false, false, HousingHelper.FreeCompanyEstatePlaceName, EstateKind.FreeCompanyEstate)]
    [InlineData(false, false, HousingHelper.PrivateEstatePlaceName, EstateKind.PrivateEstate)]
    [InlineData(false, false, 0u, EstateKind.PrivateEstate)]
    public void ClassifyEstate_ReadsTheFlagsThenThePlaceNameRow(
        bool isApartment, bool isSharedHouse, uint placeNameRow, EstateKind expected)
        => HousingHelper.ClassifyEstate(isApartment, isSharedHouse, placeNameRow).Should().Be(expected);

    [Theory]
    [InlineData(0ul, (ushort)0, false)]
    [InlineData(ulong.MaxValue, ushort.MaxValue, false)]
    [InlineData(1234ul, ushort.MaxValue, false)]
    [InlineData(1234ul, (ushort)339, true)]
    public void IsOwnedHouse_RejectsTheNotOwnedSentinel(ulong id, ushort territory, bool expected)
        => HousingHelper.IsOwnedHouse(id, territory).Should().Be(expected);

    [Fact]
    public void FindInteriorDoors_TakesTheFarthestObjectOutAndTheNearestIn()
    {
        var objects = new List<LevelObject>
        {
            new(LevelObjectKind.EventObject, InstanceId: 1, new Vector3(0, 0, -12), BaseId: 2001),
            new(LevelObjectKind.EventObject, InstanceId: 2, new Vector3(0, 0, 14), BaseId: 2002),
            new(LevelObjectKind.EventNpc, InstanceId: 3, new Vector3(0, 0, 99), BaseId: 3001),
        };

        var doors = HousingHelper.FindInteriorDoors(1234, objects);

        doors.TerritoryId.Should().Be(1234u);
        doors.Outward.Should().Be(new HousingDoor(new Vector3(0, 0, 14), 2002));
        doors.Inward.Should().Be(new HousingDoor(new Vector3(0, 0, -12), 2001));
    }

    [Fact]
    public void FindInteriorDoors_UsesOneDoorForBothDirections()
    {
        var objects = new List<LevelObject>
        {
            new(LevelObjectKind.EventObject, InstanceId: 1, new Vector3(0, 0, 8), BaseId: 2001),
        };

        var doors = HousingHelper.FindInteriorDoors(1234, objects);

        doors.Outward.Found.Should().BeTrue();
        doors.Inward.Found.Should().BeFalse();
    }

    [Fact]
    public void FindInteriorDoors_RestrictsToTheDoorsThatBelongToTheTerritoryBeingRead()
    {
        // An apartment and the private chambers share a level file, so both doors sit in both territories.
        var objects = new List<LevelObject>
        {
            new(LevelObjectKind.EventObject, InstanceId: 1, new Vector3(0, 0, 14), BaseId: 2001),
            new(LevelObjectKind.EventObject, InstanceId: 2, new Vector3(0, 0, 14), BaseId: 2002),
        };

        HousingHelper.FindInteriorDoors(1234, objects, new HashSet<uint> { 2001 })
            .Outward.InteractObjectId.Should().Be(2001u);
    }

    [Fact]
    public void FindInteriorDoors_IgnoresARestrictionThatWouldLeaveNothing()
    {
        var objects = new List<LevelObject>
        {
            new(LevelObjectKind.EventObject, InstanceId: 1, new Vector3(0, 0, 14), BaseId: 2001),
        };

        HousingHelper.FindInteriorDoors(1234, objects, new HashSet<uint> { 9999 })
            .Outward.InteractObjectId.Should().Be(2001u);
    }

    [Fact]
    public void FindInteriorDoors_ReportsNothingWhenTheFileHeldNoEventObjects()
    {
        var doors = HousingHelper.FindInteriorDoors(1234, []);

        doors.Outward.Found.Should().BeFalse();
        doors.Inward.Found.Should().BeFalse();
    }

    [Theory]
    [InlineData("bgcommon/world/aet/shared/for_bg/sgbg_w_aet_001_06a.sgb", true)]
    [InlineData("bgcommon/world/aet/shared/for_bg/sgbg_w_aet_005_01j.sgb", true)]
    [InlineData("bgcommon/world/aet/shared/for_bg/sgbg_w_aet_001_06a.sgb1", false)]
    [InlineData("bgcommon/world/lgt/shared/for_bg/sgbg_w_lgt_001.sgb", false)]
    public void IsResidentialCrystal_MatchesTheCrystalAssetFamily(string path, bool expected)
        => AetheryteHelper.IsResidentialCrystal(path).Should().Be(expected);

    /// <summary>
    /// The teleport list is game memory that outlives a character switch, so a read only answers for the character
    /// standing there now when the refresh that filled it was theirs. Right after switching, the previous
    /// character's non-empty list must read as no answer at all, never as the new character's attunements.
    /// </summary>
    [Theory]
    [InlineData(true, 5, 100UL, 100UL, true)]
    [InlineData(true, 5, 100UL, 200UL, false)]
    [InlineData(true, 5, 0UL, 200UL, false)]
    [InlineData(true, 0, 100UL, 100UL, false)]
    [InlineData(false, 5, 100UL, 100UL, false)]
    public void IsCurrentAnswer_OnlyForTheCharacterWhoRefreshedTheList(
        bool read, int attunedCount, ulong listOwner, ulong character, bool expected)
        => AetheryteHelper.IsCurrentAnswer(read, attunedCount, listOwner, character).Should().Be(expected);

    [Fact]
    public void ApplyLevelPositions_PlacesEachRowFromTheCrystalCarryingItsId()
    {
        var entries = new List<AetheryteEntry>
        {
            new(2, true, 0, 132, Vector3.Zero, "Limsa Lominsa", 0, 0, 0),
            new(999, false, 0, 132, Vector3.Zero, "Unplaced", 0, 0, 0),
        };
        var objects = new List<LevelObject>
        {
            new(LevelObjectKind.Aetheryte, InstanceId: 1, new Vector3(1, 2, 3), BaseId: 2),
            new(LevelObjectKind.SharedGroup, InstanceId: 2, new Vector3(9, 9, 9), BaseId: 999),
        };

        var placed = AetheryteHelper.ApplyLevelPositions(entries, objects);

        placed[0].Position.Should().Be(new Vector3(1, 2, 3));
        placed[1].Position.Should().Be(Vector3.Zero);
    }

    [Fact]
    public void FindPlacements_KeepsTheLowestNumberedTerritory()
    {
        var byTerritory = new Dictionary<uint, IReadOnlyList<LevelObject>>
        {
            [820] = [new LevelObject(LevelObjectKind.EventNpc, InstanceId: 1, new Vector3(8, 0, 0), BaseId: 500)],
            [132] = [new LevelObject(LevelObjectKind.EventNpc, InstanceId: 2, new Vector3(1, 0, 0), BaseId: 500)],
        };

        var placements = EventNpcHelper.FindPlacements(byTerritory, new HashSet<uint> { 500 });

        placements[500].TerritoryId.Should().Be(132u);
        placements[500].Position.Should().Be(new Vector3(1, 0, 0));
    }

    [Fact]
    public void FindPositions_ReadsOnlyTheEventNpcs()
    {
        var objects = new List<LevelObject>
        {
            new(LevelObjectKind.EventObject, InstanceId: 1, new Vector3(5, 0, 0), BaseId: 500),
            new(LevelObjectKind.EventNpc, InstanceId: 2, new Vector3(7, 0, 0), BaseId: 500),
        };

        EventNpcHelper.FindPositions(objects, new HashSet<uint> { 500 })[500].Should().Be(new Vector3(7, 0, 0));
    }

    [Fact]
    public void BuildPopRangeIndex_KeysEveryArrivalByItsTerritoryAndInstance()
    {
        var byTerritory = new Dictionary<uint, IReadOnlyList<LevelObject>>
        {
            [963] = [
                new LevelObject(LevelObjectKind.PopRange, InstanceId: 8904508, new Vector3(5, 6, 7)),
                new LevelObject(LevelObjectKind.ExitRange, InstanceId: 42, new Vector3(0, 0, 0)),
            ],
        };

        var index = LevelFileHelper.BuildPopRangeIndex(byTerritory);

        index.Should().ContainSingle();
        index[(963u, 8904508u)].Should().Be(new Vector3(5, 6, 7));
    }

    [Fact]
    public void OfKind_KeepsOnlyTheKindAsked()
    {
        var objects = new List<LevelObject>
        {
            new(LevelObjectKind.Aetheryte, InstanceId: 1, Vector3.Zero, BaseId: 2),
            new(LevelObjectKind.PopRange, InstanceId: 2, Vector3.Zero),
        };

        LevelFileHelper.OfKind(objects, LevelObjectKind.Aetheryte).Should().ContainSingle();
    }

    [Fact]
    public void LevelObjectFilter_DropsUnmappedKindsByDefault()
    {
        LevelObjectFilter.Default.Keeps(new LevelObject(LevelObjectKind.Other, 1, Vector3.Zero)).Should().BeFalse();
        LevelObjectFilter.Everything.Keeps(new LevelObject(LevelObjectKind.Other, 1, Vector3.Zero)).Should().BeTrue();
    }

    [Fact]
    public void LevelObjectFilter_KeepsOnlyTheWantedInteractables()
    {
        var filter = new LevelObjectFilter
        {
            EventNpcBaseIds = new HashSet<uint> { 500 },
            EventObjectBaseIds = new HashSet<uint> { 2001 },
        };

        filter.Keeps(new LevelObject(LevelObjectKind.EventNpc, 1, Vector3.Zero, BaseId: 500)).Should().BeTrue();
        filter.Keeps(new LevelObject(LevelObjectKind.EventNpc, 2, Vector3.Zero, BaseId: 501)).Should().BeFalse();
        filter.Keeps(new LevelObject(LevelObjectKind.EventObject, 3, Vector3.Zero, BaseId: 2001)).Should().BeTrue();
        filter.Keeps(new LevelObject(LevelObjectKind.EventObject, 4, Vector3.Zero, BaseId: 2002)).Should().BeFalse();

        // A kind the filter says nothing about is untouched by it.
        filter.Keeps(new LevelObject(LevelObjectKind.Aetheryte, 5, Vector3.Zero, BaseId: 2)).Should().BeTrue();
    }

    [Fact]
    public void LevelObjectFilter_RestrictsToTheKindsAsked()
    {
        var filter = new LevelObjectFilter { Kinds = new HashSet<LevelObjectKind> { LevelObjectKind.Aetheryte } };

        filter.Keeps(new LevelObject(LevelObjectKind.Aetheryte, 1, Vector3.Zero)).Should().BeTrue();
        filter.Keeps(new LevelObject(LevelObjectKind.PopRange, 2, Vector3.Zero)).Should().BeFalse();
    }

    [Fact]
    public void CollectStandIds_TakesTheDeparturesAndEveryDestination()
    {
        var stands = new List<ChocoboTaxiStandInfo>
        {
            new(1, "Bentbranch Meadows", [new ChocoboTaxiRide(1, 4, 240, 180, "Hawthorne Hut")]),
        };

        ChocoboTaxiHelper.CollectStandIds(stands).Should().BeEquivalentTo([1u, 4u]);
    }

    [Fact]
    public void ScanPorters_TakesTheLowestNumberedNpcServingAStand()
    {
        var scan = new EventNpcHandlerScan(
            new Dictionary<uint, IReadOnlyList<uint>>(),
            new Dictionary<uint, IReadOnlyList<uint>> { [4] = [1004321, 1000512] });

        ChocoboTaxiHelper.ScanPorters(new HashSet<uint> { 4 }, scan)[4].Should().Be(1000512u);
    }
}
