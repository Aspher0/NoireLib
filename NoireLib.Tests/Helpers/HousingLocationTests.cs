using FluentAssertions;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using Lumina.Excel.Sheets;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks how a plot's placard and doors are picked out of a district's placed objects. The pairing rules run over
/// objects the test supplies. They need no game. The invariants the rules rest on are pinned separately against
/// the real archives: a silent drift there would return a confident wrong position, never none.
/// </summary>
public sealed class HousingLocationTests
{
    private static LevelObject Pop(uint id, Vector3 position, string layer)
        => new(LevelObjectKind.PopRange, id, position, Layer: layer);

    private static LevelObject Placard(uint id, Vector3 position, uint baseId = HousingHelper.PlacardBaseId)
        => new(LevelObjectKind.EventObject, id, position, BaseId: baseId, Layer: "LVD_signboard_01");

    [Fact]
    public void InLayer_MatchesAFragmentWithoutCase()
    {
        IReadOnlyList<LevelObject> objects =
        [
            Pop(1, Vector3.Zero, "LVD_frontpop_01"),
            Pop(2, Vector3.Zero, "LVD_FRONTPOP_02"),
            Pop(3, Vector3.Zero, "LVD_roomexit_01"),
        ];

        LevelFileHelper.InLayer(objects, "frontpop").Select(o => o.InstanceId).Should().Equal(1u, 2u);
        LevelFileHelper.InLayer(objects, "roomexit").Select(o => o.InstanceId).Should().Equal(3u);
        LevelFileHelper.InLayer(objects, string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void TryGetNearest_PicksTheClosestAndReportsAnEmptyList()
    {
        IReadOnlyList<LevelObject> objects =
        [
            Pop(1, new Vector3(10, 0, 0), "a"),
            Pop(2, new Vector3(3, 0, 0), "a"),
            Pop(3, new Vector3(-4, 0, 0), "a"),
        ];

        LevelFileHelper.TryGetNearest(objects, Vector3.Zero, out var nearest).Should().BeTrue();
        nearest.InstanceId.Should().Be(2);

        LevelFileHelper.TryGetNearest([], Vector3.Zero, out _).Should().BeFalse();
    }

    /// <summary>Two plots with their placards and doors, plus one apartment building. The second placard comes from another EObj row.</summary>
    [Fact]
    public void BuildLocations_NumbersPlotsFromOneAndAttachesTheNearestDoors()
    {
        IReadOnlyList<HousingPlot> plots =
        [
            new(0, HousingInteriorKind.Cottage, PlacardInstanceId: 100),
            new(30, HousingInteriorKind.Mansion, PlacardInstanceId: 200),
        ];

        IReadOnlyList<LevelObject> objects =
        [
            Placard(100, new Vector3(0, 0, 10)),
            Placard(200, new Vector3(500, 0, 10), baseId: 9999),
            Pop(101, new Vector3(0, 0, 2), "LVD_frontpop_01"),
            Pop(102, new Vector3(0, 0, 3), "LVD_roomexit_01"),
            Pop(201, new Vector3(500, 0, 2), "LVD_frontpop_02"),
            Pop(202, new Vector3(500, 0, 3), "LVD_roomexit_02"),
            Pop(301, new Vector3(-900, 0, 2), "LVD_frontpop_01"),
        ];

        var anchors = new Dictionary<ushort, Vector3>
        {
            [0] = Vector3.Zero,
            [30] = new(500, 0, 0),
            [HousingHelper.MainApartmentMarker] = new(-900, 0, 0),
        };

        var locations = HousingHelper.BuildLocations(339, plots, objects, anchors);

        locations.Should().HaveCount(3);

        var first = locations[0];
        first.District.Should().Be(339);
        first.Plot.Should().Be(1);
        first.IsApartment.Should().BeFalse();
        first.Subdivision.Should().BeFalse();
        first.Kind.Should().Be(HousingInteriorKind.Cottage);
        first.Anchor.Should().Be(Vector3.Zero);
        first.Entrance.Should().Be(new Vector3(0, 0, 2));
        first.ExitLanding.Should().Be(new Vector3(0, 0, 3));
        first.Placard.Should().Be(new Vector3(0, 0, 10));

        var second = locations[1];
        second.Plot.Should().Be(31);
        second.Subdivision.Should().BeTrue("plot 31 is the first of the subdivision");
        second.Entrance.Should().Be(new Vector3(500, 0, 2));
        second.Placard.Should().BeNull("the row named an object placed from another EObj row");

        var apartment = locations[2];
        apartment.IsApartment.Should().BeTrue();
        apartment.Plot.Should().Be(0);
        apartment.Kind.Should().BeNull();
        apartment.Placard.Should().BeNull();
        apartment.ExitLanding.Should().BeNull("an apartment building has no estate door to step out of");
        apartment.Entrance.Should().Be(new Vector3(-900, 0, 2));
    }

    [Fact]
    public void BuildLocations_FallsBackToTheAnchorWhenNoDoorIsPlaced()
    {
        var locations = HousingHelper.BuildLocations(
            339,
            [new HousingPlot(0, HousingInteriorKind.House)],
            [],
            new Dictionary<ushort, Vector3> { [0] = new(1, 2, 3) });

        locations.Should().ContainSingle();
        locations[0].Entrance.Should().Be(new Vector3(1, 2, 3));
        locations[0].ExitLanding.Should().BeNull();
        locations[0].Placard.Should().BeNull();
    }

    /// <summary>
    /// The whole design rests on three counts holding in every district: sixty placards placed from one EObj row,
    /// sixty-two entrance volumes for the sixty plots and two apartment buildings, and sixty estate exits. Each
    /// pairing must also be one-to-one, since the rule is "nearest in the layer" and a tie would hand two plots the
    /// same door. Skipped when no game installation is present.
    /// </summary>
    [Fact]
    public void EveryDistrict_PlacesOnePlacardAndOneDoorPairPerPlot()
    {
        var game = GameDataFixture.TryOpen();
        if (game == null)
            return;

        var territories = game.GetExcelSheet<TerritoryType>()!;
        var landSets = ReadLandSets(game);
        var anchorsByTerritory = ReadAnchors(game);

        foreach (var district in Enum.GetValues<ResidentialDistrict>())
        {
            var territoryId = (uint)district;
            var objects = ReadPlanMap(game, territories.GetRow(territoryId).Bg.ExtractText());
            var plots = HousingHelper.MatchLandSet(landSets, objects);
            var anchors = anchorsByTerritory[territoryId];

            plots.Should().HaveCount(60, because: $"{district} has sixty plots");
            anchors.Should().HaveCount(62, because: $"{district} marks sixty plots and two apartment buildings");

            var locations = HousingHelper.BuildLocations(territoryId, plots, objects, anchors);

            locations.Should().HaveCount(62);
            locations.Count(l => l.Placard.HasValue).Should().Be(60, because: $"{district} places a placard on every plot");
            locations.Count(l => l.ExitLanding.HasValue).Should().Be(60, because: $"{district} gives every estate an exit");

            locations.Select(l => l.Entrance).Distinct().Should()
                .HaveCount(62, because: $"no two addresses in {district} share an entrance");
            locations.Where(l => l.ExitLanding.HasValue).Select(l => l.ExitLanding!.Value).Distinct().Should()
                .HaveCount(60, because: $"no two plots in {district} share an estate exit");
        }
    }

    private static IReadOnlyList<HousingLandSetInfo> ReadLandSets(Lumina.GameData game)
    {
        var rows = new List<HousingLandSetInfo>();
        foreach (var row in game.GetExcelSheet<HousingLandSet>()!)
        {
            var plots = new List<HousingPlot>();
            var instances = new List<uint>();
            var index = 0;
            foreach (var plot in row.LandSet)
            {
                var kind = HousingInteriorKinds.FromPlotSize(plot.PlotSize);
                if (kind.HasValue)
                    plots.Add(new HousingPlot(index, kind.Value, plot.PlacardId));

                if (plot.PlacardId != 0)
                    instances.Add(plot.PlacardId);

                index++;
            }

            if (plots.Count > 0)
                rows.Add(new HousingLandSetInfo(row.RowId, plots, instances));
        }

        return rows;
    }

    private static Dictionary<uint, Dictionary<ushort, Vector3>> ReadAnchors(Lumina.GameData game)
    {
        var byTerritory = new Dictionary<uint, Dictionary<ushort, Vector3>>();
        foreach (var collection in game.GetSubrowExcelSheet<HousingMapMarkerInfo>()!)
        {
            foreach (var marker in collection)
            {
                var territory = marker.Map.ValueNullable?.TerritoryType.RowId ?? 0;
                if (territory == 0)
                    continue;

                if (!byTerritory.TryGetValue(territory, out var anchors))
                    byTerritory[territory] = anchors = [];

                anchors[(ushort)marker.SubrowId] = new Vector3(marker.X, marker.Y, marker.Z);
            }
        }

        return byTerritory;
    }

    // The archives are read directly. A test has no Dalamud.
    private static IReadOnlyList<LevelObject> ReadPlanMap(Lumina.GameData game, string bg)
    {
        var directory = bg[..(bg.IndexOf("/level/", StringComparison.Ordinal) + 7)];
        var lgb = game.GetFile<LgbFile>($"bg/{directory}planmap.lgb")!;

        var objects = new List<LevelObject>();
        foreach (var layer in lgb.Layers)
        {
            foreach (var instance in layer.InstanceObjects)
            {
                var translation = instance.Transform.Translation;
                var position = new Vector3(translation.X, translation.Y, translation.Z);
                var name = layer.Name ?? string.Empty;

                if (instance.AssetType == LayerEntryType.PopRange)
                    objects.Add(new LevelObject(LevelObjectKind.PopRange, instance.InstanceId, position, Layer: name));
                else if (instance.Object is LayerCommon.EventInstanceObject eventObject)
                    objects.Add(new LevelObject(LevelObjectKind.EventObject, instance.InstanceId, position,
                        BaseId: eventObject.ParentData.BaseId, Layer: name));
                else
                    objects.Add(new LevelObject(LevelObjectKind.Other, instance.InstanceId, position, Layer: name));
            }
        }

        return objects;
    }
}
