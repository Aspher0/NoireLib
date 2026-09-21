using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Where a residential plot's placard and doors stand, for any plot of any ward of any district, read from the
/// sheets and the district's level file with no character in the zone.
/// </summary>
public static unsafe partial class HousingHelper
{
    /// <summary>
    /// The EObj row every plot placard is placed from. It is the same row in all five districts. It tells a
    /// placard from anything else the land-set row could name.
    /// </summary>
    public const uint PlacardBaseId = 2002736;

    /// <summary>The number of plots in a division, the first of which is the district proper and the second its subdivision.</summary>
    public const int PlotsPerDivision = 30;

    /// <summary>
    /// The fragment naming the layers that hold the spot in front of a building's door. Every district names these
    /// layers around the same stem and none of them agree on the rest of the name.
    /// </summary>
    public const string EntranceLayer = "frontpop";

    /// <summary>The fragment naming the layers that hold the spot an estate puts a character stepping out of it.</summary>
    public const string ExitLandingLayer = "roomexit";

    private static readonly ConcurrentDictionary<uint, IReadOnlyList<HousingPlotLocation>> LocationCache = new();

    /// <summary>
    /// Every plot and both apartment buildings of a district, in plot order with the apartments last. Built once per
    /// district and cached, since none of it changes while the game runs.
    /// </summary>
    /// <param name="districtTerritoryId">The residential district's TerritoryType row id.</param>
    /// <returns>The district's locations, or empty when it is not a residential district or its files were unreadable.</returns>
    public static IReadOnlyList<HousingPlotLocation> ReadPlotLocations(uint districtTerritoryId)
        => LocationCache.GetOrAdd(districtTerritoryId, BuildLocations);

    /// <inheritdoc cref="ReadPlotLocations(uint)"/>
    /// <param name="district">The residential district.</param>
    public static IReadOnlyList<HousingPlotLocation> ReadPlotLocations(ResidentialDistrict district)
        => ReadPlotLocations((uint)district);

    /// <summary>
    /// Resolves where one plot's placard and doors stand. Ward and plot are the numbers the game displays. Plot 1
    /// is the first plot and plot 31 the first of the subdivision. The ward completes the address and changes no
    /// position, since every ward of a district is the same territory laid out identically.
    /// </summary>
    /// <param name="districtTerritoryId">The residential district's TerritoryType row id.</param>
    /// <param name="ward">The one-based ward.</param>
    /// <param name="plot">The one-based plot, from 1 to 60.</param>
    /// <param name="location">The resolved location when the plot exists.</param>
    /// <returns>True when the plot was resolved.</returns>
    public static bool TryResolvePlot(uint districtTerritoryId, int ward, int plot, out HousingPlotLocation location)
    {
        location = HousingPlotLocation.None;

        foreach (var candidate in ReadPlotLocations(districtTerritoryId))
        {
            if (candidate.IsApartment || candidate.Plot != plot)
                continue;

            location = candidate with { Ward = ward };
            return true;
        }

        return false;
    }

    /// <inheritdoc cref="TryResolvePlot(uint, int, int, out HousingPlotLocation)"/>
    /// <param name="district">The residential district.</param>
    /// <param name="ward">The one-based ward.</param>
    /// <param name="plot">The one-based plot, from 1 to 60.</param>
    /// <param name="location">The resolved location when the plot exists.</param>
    /// <returns>True when the plot was resolved.</returns>
    public static bool TryResolvePlot(ResidentialDistrict district, int ward, int plot, out HousingPlotLocation location)
        => TryResolvePlot((uint)district, ward, plot, out location);

    /// <summary>Resolves where a district's apartment building stands. An apartment has no placard and no estate door.</summary>
    /// <param name="districtTerritoryId">The residential district's TerritoryType row id.</param>
    /// <param name="ward">The one-based ward.</param>
    /// <param name="subdivision">True for the subdivision's building, false for the main division's.</param>
    /// <param name="location">The resolved location when the building exists.</param>
    /// <returns>True when the building was resolved.</returns>
    public static bool TryResolveApartment(uint districtTerritoryId, int ward, bool subdivision, out HousingPlotLocation location)
    {
        location = HousingPlotLocation.None;

        foreach (var candidate in ReadPlotLocations(districtTerritoryId))
        {
            if (!candidate.IsApartment || candidate.Subdivision != subdivision)
                continue;

            location = candidate with { Ward = ward };
            return true;
        }

        return false;
    }

    /// <inheritdoc cref="TryResolveApartment(uint, int, bool, out HousingPlotLocation)"/>
    /// <param name="district">The residential district.</param>
    /// <param name="ward">The one-based ward.</param>
    /// <param name="subdivision">True for the subdivision's building, false for the main division's.</param>
    /// <param name="location">The resolved location when the building exists.</param>
    /// <returns>True when the building was resolved.</returns>
    public static bool TryResolveApartment(ResidentialDistrict district, int ward, bool subdivision, out HousingPlotLocation location)
        => TryResolveApartment((uint)district, ward, subdivision, out location);

    /// <summary>
    /// Resolves an address read off the character, such as the one <see cref="ReadOwnedAddress"/> returns. The
    /// address holds its ward and plot zero-based and they are converted here.
    /// </summary>
    /// <param name="address">The address to resolve.</param>
    /// <param name="location">The resolved location when the address named one.</param>
    /// <returns>True when the address was resolved.</returns>
    public static bool TryResolve(HousingAddress address, out HousingPlotLocation location)
    {
        location = HousingPlotLocation.None;
        if (!address.Owned || address.District == 0)
            return false;

        return address.IsApartment
            ? TryResolveApartment(address.District, address.Ward + 1, address.Division != 0, out location)
            : TryResolvePlot(address.District, address.Ward + 1, address.Plot + 1, out location);
    }

    /// <summary>
    /// Builds a district's locations from its placed objects and its land-set row. Separated from the file read so
    /// the pairing rules can be exercised against objects a test supplies.
    /// </summary>
    /// <param name="districtTerritoryId">The district the objects were read from.</param>
    /// <param name="plots">The district's plots, from <see cref="MatchLandSet"/>.</param>
    /// <param name="objects">The district's placed objects, from its <c>planmap.lgb</c>.</param>
    /// <param name="anchors">Each marker subrow's anchor position, plots under 0 to 59 and apartments under 60 and 61.</param>
    /// <returns>The plots in order, with both apartment buildings last.</returns>
    public static IReadOnlyList<HousingPlotLocation> BuildLocations(
        uint districtTerritoryId,
        IReadOnlyList<HousingPlot> plots,
        IReadOnlyList<LevelObject> objects,
        IReadOnlyDictionary<ushort, Vector3> anchors)
    {
        var placards = new Dictionary<uint, Vector3>();
        foreach (var levelObject in objects)
        {
            if (levelObject.Kind == LevelObjectKind.EventObject && levelObject.BaseId == PlacardBaseId)
                placards[levelObject.InstanceId] = levelObject.Position;
        }

        var entrances = LevelFileHelper.InLayer(objects, EntranceLayer);
        var landings = LevelFileHelper.InLayer(objects, ExitLandingLayer);

        var list = new List<HousingPlotLocation>(plots.Count + 2);
        foreach (var plot in plots)
        {
            if (!anchors.TryGetValue((ushort)plot.Index, out var anchor))
                continue;

            // The placard's base id is checked. A row pointing elsewhere yields no position.
            Vector3? placard = placards.TryGetValue(plot.PlacardInstanceId, out var placardPosition)
                ? placardPosition
                : null;

            list.Add(new HousingPlotLocation(
                districtTerritoryId,
                Ward: 0,
                Plot: plot.Index + 1,
                IsApartment: false,
                Subdivision: plot.Index >= PlotsPerDivision,
                Kind: plot.Kind,
                Anchor: anchor,
                Entrance: LevelFileHelper.TryGetNearest(entrances, anchor, out var entrance) ? entrance.Position : anchor,
                Placard: placard,
                ExitLanding: LevelFileHelper.TryGetNearest(landings, anchor, out var landing) ? landing.Position : null));
        }

        foreach (var subrow in (ReadOnlySpan<ushort>)[MainApartmentMarker, SubdivisionApartmentMarker])
        {
            if (!anchors.TryGetValue(subrow, out var anchor))
                continue;

            list.Add(new HousingPlotLocation(
                districtTerritoryId,
                Ward: 0,
                Plot: 0,
                IsApartment: true,
                Subdivision: subrow == SubdivisionApartmentMarker,
                Kind: null,
                Anchor: anchor,
                Entrance: LevelFileHelper.TryGetNearest(entrances, anchor, out var entrance) ? entrance.Position : anchor,
                Placard: null,
                ExitLanding: null));
        }

        return list;
    }

    private static IReadOnlyList<HousingPlotLocation> BuildLocations(uint districtTerritoryId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var objects = LevelFileHelper.ReadObjects(districtTerritoryId, LevelFileHelper.Files.PlanMap);
            if (objects.Count == 0)
                return (IReadOnlyList<HousingPlotLocation>)[];

            var plots = MatchLandSet(ReadLandSets(), objects);
            if (plots.Count == 0)
                return (IReadOnlyList<HousingPlotLocation>)[];

            EnsureMarkersBuilt();
            var anchors = new Dictionary<ushort, Vector3>();
            foreach (var ((territory, subrow), position) in markerPositions!)
            {
                if (territory == districtTerritoryId)
                    anchors[subrow] = position;
            }

            return BuildLocations(districtTerritoryId, plots, objects, anchors);
        }, []) ?? [];
    }
}
