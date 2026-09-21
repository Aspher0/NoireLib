using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using Lumina.Excel.Sheets;

namespace NoireLib.Helpers;

/// <summary>
/// Converts between the game's three ways of naming a spot on a map, and reads the sheet rows the conversion needs.
/// <br/>
/// A territory has a <b>world</b> position (what a game object carries), a <b>marker</b> pixel in the 0-2048 space a
/// map image is authored in (what the MapMarker sheet stores), and a <b>map coordinate</b> (the "X: 12.3, Y: 9.8" a
/// flag or a chat link is written with). All three are the same point through one map's size factor and offset, and
/// none of them carries a height: a real altitude only ever comes from a placed object's own transform.
/// </summary>
public static class MapCoordinateHelper
{
    // The map image space is 2048 pixels across, world origin at its centre. A map coordinate runs over 41 units of it, starting at 1.
    private const float MarkerCentre = 1024f;
    private const float MarkerSpan = 2048f;
    private const float CoordinateSpan = 41f;
    private const float CoordinateOrigin = 1f;

    /// <summary>
    /// Projects a map marker into world space through the map it is drawn on. Only X and Z are meaningful: a marker
    /// carries no height. Y comes back zero. It is never guessed.
    /// </summary>
    /// <param name="marker">The marker to project.</param>
    /// <param name="map">The map the marker belongs to.</param>
    /// <returns>The marker's world position, with a zero height.</returns>
    public static Vector3 MarkerToWorld(MapMarkerEntry marker, MapProjection map)
    {
        var (x, z) = MarkerToWorld(marker.MapX, marker.MapY, map.SizeFactor, map.OffsetX, map.OffsetY);
        return new Vector3(x, 0f, z);
    }

    /// <summary>Converts a map-marker pixel to a world X and Z through a map's size factor and offset.</summary>
    /// <param name="markerX">The marker's map X pixel.</param>
    /// <param name="markerY">The marker's map Y pixel.</param>
    /// <param name="sizeFactor">The map row's SizeFactor.</param>
    /// <param name="offsetX">The map row's OffsetX.</param>
    /// <param name="offsetY">The map row's OffsetY.</param>
    /// <returns>The world X and Z the marker sits at.</returns>
    public static (float X, float Z) MarkerToWorld(float markerX, float markerY, float sizeFactor, float offsetX, float offsetY)
        => (MarkerToWorld(markerX, sizeFactor, offsetX), MarkerToWorld(markerY, sizeFactor, offsetY));

    /// <summary>Converts one map-marker pixel to its world axis value.</summary>
    /// <param name="marker">The marker pixel on that axis.</param>
    /// <param name="sizeFactor">The map row's SizeFactor.</param>
    /// <param name="offset">The map row's offset for that axis.</param>
    /// <returns>The world value.</returns>
    public static float MarkerToWorld(float marker, float sizeFactor, float offset)
        => ((marker - MarkerCentre) / Scale(sizeFactor)) - offset;

    /// <summary>Converts a world position to the map coordinate the game shows beside the minimap and in map links. World Z is the coordinate's Y.</summary>
    /// <param name="world">The world position.</param>
    /// <param name="map">The map to express it on.</param>
    /// <returns>The map coordinate pair.</returns>
    public static (float X, float Y) WorldToMapCoordinate(Vector3 world, MapProjection map)
        => (WorldToMapCoordinate(world.X, map.SizeFactor, map.OffsetX),
            WorldToMapCoordinate(world.Z, map.SizeFactor, map.OffsetY));

    /// <summary>Converts a map coordinate pair back to the world position it names, at a zero height.</summary>
    /// <param name="x">The map coordinate's X.</param>
    /// <param name="y">The map coordinate's Y, which is the world Z axis.</param>
    /// <param name="map">The map the coordinate is expressed on.</param>
    /// <returns>The world position, with a zero height.</returns>
    public static Vector3 MapCoordinateToWorld(float x, float y, MapProjection map)
        => new(MapCoordinateToWorld(x, map.SizeFactor, map.OffsetX), 0f,
               MapCoordinateToWorld(y, map.SizeFactor, map.OffsetY));

    /// <summary>Converts a world position back to the map-marker pixel the sheet would store it as.</summary>
    /// <param name="world">The world position. Its height is not part of a marker.</param>
    /// <param name="map">The map to express it on.</param>
    /// <returns>The marker X and Y pixels.</returns>
    public static (float X, float Y) WorldToMarker(Vector3 world, MapProjection map)
        => WorldToMarker(world.X, world.Z, map.SizeFactor, map.OffsetX, map.OffsetY);

    /// <inheritdoc cref="WorldToMarker(Vector3, MapProjection)"/>
    /// <param name="worldX">The world X.</param>
    /// <param name="worldZ">The world Z.</param>
    /// <param name="sizeFactor">The map row's SizeFactor.</param>
    /// <param name="offsetX">The map row's OffsetX.</param>
    /// <param name="offsetY">The map row's OffsetY.</param>
    /// <returns>The marker X and Y pixels.</returns>
    public static (float X, float Y) WorldToMarker(float worldX, float worldZ, float sizeFactor, float offsetX, float offsetY)
        => (WorldToMarker(worldX, sizeFactor, offsetX), WorldToMarker(worldZ, sizeFactor, offsetY));

    /// <summary>Converts one world axis value to its map-marker pixel.</summary>
    /// <param name="world">The world value on that axis.</param>
    /// <param name="sizeFactor">The map row's SizeFactor.</param>
    /// <param name="offset">The map row's offset for that axis.</param>
    /// <returns>The marker pixel.</returns>
    public static float WorldToMarker(float world, float sizeFactor, float offset)
        => ((world + offset) * Scale(sizeFactor)) + MarkerCentre;

    /// <summary>
    /// Converts a world axis value to the map coordinate the game writes it as, being the number shown beside the
    /// minimap and carried in a map link. The X world axis maps to the coordinate's X and the Z world axis to its Y.
    /// </summary>
    /// <param name="world">The world value on that axis.</param>
    /// <param name="sizeFactor">The map row's SizeFactor.</param>
    /// <param name="offset">The map row's offset for that axis.</param>
    /// <returns>The map coordinate.</returns>
    public static float WorldToMapCoordinate(float world, float sizeFactor, float offset)
    {
        var scale = Scale(sizeFactor);
        return (CoordinateSpan / scale * (WorldToMarker(world, sizeFactor, offset) / MarkerSpan)) + CoordinateOrigin;
    }

    /// <summary>Converts a map coordinate back to its world axis value.</summary>
    /// <param name="coordinate">The map coordinate on that axis.</param>
    /// <param name="sizeFactor">The map row's SizeFactor.</param>
    /// <param name="offset">The map row's offset for that axis.</param>
    /// <returns>The world value.</returns>
    public static float MapCoordinateToWorld(float coordinate, float sizeFactor, float offset)
    {
        var scale = Scale(sizeFactor);
        var marker = (coordinate - CoordinateOrigin) * scale / CoordinateSpan * MarkerSpan;
        return MarkerToWorld(marker, sizeFactor, offset);
    }

    /// <summary>Reads the maps a territory is drawn across, with the projection each of them uses.</summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The territory's maps, or an empty list. A territory that spans a main map and a subdivision has two.</returns>
    public static IReadOnlyList<MapProjection> ReadMaps(uint territoryId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var list = new List<MapProjection>();
            var sheet = ExcelSheetHelper.GetSheet<Map>();
            if (sheet == null)
                return (IReadOnlyList<MapProjection>)list;

            foreach (var map in sheet)
            {
                if (map.RowId != 0 && map.TerritoryType.RowId == territoryId)
                    list.Add(new MapProjection(map.RowId, (uint)map.MapMarkerRange, map.SizeFactor, map.OffsetX, map.OffsetY));
            }

            return list;
        }, []) ?? [];
    }

    /// <summary>Reads the markers a map draws.</summary>
    /// <param name="map">The map to read.</param>
    /// <returns>The map's markers, or an empty list.</returns>
    public static IReadOnlyList<MapMarkerEntry> ReadMarkers(MapProjection map) => ReadMarkers(map.MapMarkerRange);

    /// <inheritdoc cref="ReadMarkers(MapProjection)"/>
    /// <param name="mapMarkerRange">The map's <see cref="MapProjection.MapMarkerRange"/>.</param>
    /// <returns>The markers in that range, or an empty list.</returns>
    public static IReadOnlyList<MapMarkerEntry> ReadMarkers(uint mapMarkerRange)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var list = new List<MapMarkerEntry>();
            if (!ExcelSheetHelper.TryGetSubrows<MapMarker>(mapMarkerRange, out var markers))
                return (IReadOnlyList<MapMarkerEntry>)list;

            foreach (var marker in markers)
                list.Add(new MapMarkerEntry(marker.DataType, marker.DataKey.RowId, marker.X, marker.Y, marker.Icon));

            return list;
        }, []) ?? [];
    }

    /// <summary>
    /// Reads every marker a territory draws and projects each into world space through its own map's projection. A
    /// territory that spans several maps has a different offset per map. Projecting them together through one is
    /// wrong. This resolves that without the caller needing to know the territory spans anything.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <param name="dataType">Keeps only the markers of one <see cref="MapMarkerDataType"/>, or null for every marker.</param>
    /// <returns>The projected markers.</returns>
    public static IReadOnlyList<ProjectedMapMarker> ProjectMarkers(uint territoryId, int? dataType = null)
    {
        var projected = new List<ProjectedMapMarker>();
        foreach (var map in ReadMaps(territoryId))
        {
            foreach (var marker in ReadMarkers(map))
            {
                if (dataType.HasValue && marker.DataType != dataType.Value)
                    continue;

                projected.Add(new ProjectedMapMarker(marker, MarkerToWorld(marker, map), map.MapId));
            }
        }

        return projected;
    }

    /// <summary>
    /// Finds the projected marker nearest a world position, compared on the ground plane alone since a marker carries
    /// no height. This is how a placed object is matched to the label the map draws over it.
    /// </summary>
    /// <param name="markers">The projected markers to search.</param>
    /// <param name="position">The world position to match.</param>
    /// <param name="nearest">The nearest marker when one was found.</param>
    /// <returns>True when there was a marker to match.</returns>
    public static bool TryFindNearestMarker(
        IReadOnlyList<ProjectedMapMarker> markers,
        Vector3 position,
        out ProjectedMapMarker nearest)
    {
        nearest = default;
        var bestDistance = float.MaxValue;
        var found = false;

        foreach (var marker in markers)
        {
            var dx = marker.World.X - position.X;
            var dz = marker.World.Z - position.Z;
            var distance = (dx * dx) + (dz * dz);
            if (distance >= bestDistance)
                continue;

            bestDistance = distance;
            nearest = marker;
            found = true;
        }

        return found;
    }

    /// <summary>
    /// Expresses a world position as the map coordinate pair the game shows beside the minimap, on whichever of the
    /// territory's maps the position belongs to.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <param name="world">The world position.</param>
    /// <param name="coordinate">The coordinate, its X from the world X axis and its Y from the world Z axis.</param>
    /// <param name="mapId">The Map row the coordinate is expressed on, zero when there is none.</param>
    /// <returns>True when the territory is drawn on a map.</returns>
    public static bool TryWorldToMapCoordinate(uint territoryId, Vector3 world, out Vector2 coordinate, out uint mapId)
    {
        coordinate = default;
        mapId = 0;

        var (ownMapId, maps) = MapsOf(territoryId);
        if (PickMap(maps, ownMapId, world) is not { } map)
            return false;

        var (x, y) = WorldToMapCoordinate(world, map);
        coordinate = new Vector2(x, y);
        mapId = map.MapId;
        return true;
    }

    /// <summary>
    /// Writes a world position the way a report names a place, with the map coordinate beside it. A position on no
    /// map is written on its own.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <param name="world">The world position.</param>
    /// <returns>The position and its map coordinate.</returns>
    public static string DescribePlace(uint territoryId, Vector3 world)
        => TryWorldToMapCoordinate(territoryId, world, out var coordinate, out _)
            ? string.Create(CultureInfo.InvariantCulture,
                $"{world.X:F3}, {world.Y:F3}, {world.Z:F3} (map {coordinate.X:F1}, {coordinate.Y:F1})")
            : string.Create(CultureInfo.InvariantCulture, $"{world.X:F3}, {world.Y:F3}, {world.Z:F3}");

    /// <summary>
    /// The map a world position belongs to. A housing district draws its main division and its subdivision on two
    /// maps with different offsets, and a position belongs to the one it lands inside, the territory's own map first.
    /// </summary>
    /// <param name="maps">The territory's maps, from <see cref="ReadMaps"/>.</param>
    /// <param name="ownMapId">The TerritoryType row's own Map id, zero when it names none.</param>
    /// <param name="world">The world position.</param>
    /// <returns>The map to express the position on, or null when there are no maps.</returns>
    public static MapProjection? PickMap(IReadOnlyList<MapProjection> maps, uint ownMapId, Vector3 world)
    {
        if (maps.Count == 0)
            return null;

        MapProjection? own = null;
        MapProjection? inside = null;

        foreach (var candidate in maps)
        {
            var isInside = IsInsideMap(candidate, world);

            if (candidate.MapId == ownMapId)
            {
                own = candidate;

                if (isInside)
                    return candidate;
            }
            else if (isInside && inside == null)
            {
                inside = candidate;
            }
        }

        return inside ?? own ?? maps[0];
    }

    /// <summary>Whether a world position lands inside the area a map draws.</summary>
    /// <param name="map">The map to test against.</param>
    /// <param name="world">The world position.</param>
    /// <returns>True when the position's coordinate falls within the map's own span.</returns>
    public static bool IsInsideMap(MapProjection map, Vector3 world)
    {
        var (x, y) = WorldToMapCoordinate(world, map);
        var last = WorldToMapCoordinate((MarkerCentre / Scale(map.SizeFactor)) - map.OffsetX, map.SizeFactor, map.OffsetX);

        return x >= CoordinateOrigin && y >= CoordinateOrigin && x <= last && y <= last;
    }

    /// <summary>The maps a territory is drawn across and its own map id, read once per territory.</summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The territory's own Map id and every map it is drawn across.</returns>
    public static (uint OwnMapId, IReadOnlyList<MapProjection> Maps) MapsOf(uint territoryId)
    {
        lock (CacheLock)
        {
            if (Cached.TryGetValue(territoryId, out var known))
                return known;
        }

        var ownMapId = SafeExecutor.ExecuteSafely(
            () => ExcelSheetHelper.TryGetRow<TerritoryType>(territoryId, out var row) && row is { } territory
                ? territory.Map.RowId
                : 0u,
            0u);

        var read = (ownMapId, ReadMaps(territoryId));

        lock (CacheLock)
            Cached[territoryId] = read;

        return read;
    }

    private static readonly object CacheLock = new();

    private static readonly Dictionary<uint, (uint OwnMapId, IReadOnlyList<MapProjection> Maps)> Cached = [];

    private static float Scale(float sizeFactor) => sizeFactor / 100f;
}
