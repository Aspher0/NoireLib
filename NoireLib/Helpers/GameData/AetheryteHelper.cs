using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// One aetheryte or aethernet shard, flattened from the sheet. The Aetheryte sheet carries no coordinates of its own,
/// so <see cref="Position"/> arrives empty from <see cref="AetheryteHelper.ReadAll"/> and is filled by
/// <see cref="AetheryteHelper.ApplyLevelPositions"/> from the crystals placed in the level files.
/// </summary>
/// <param name="Id">The Aetheryte row id.</param>
/// <param name="IsCityAetheryte">True for a map-teleport target, false for an aethernet shard.</param>
/// <param name="AethernetGroup">The aethernet group, or zero when the node is not part of one.</param>
/// <param name="TerritoryId">The territory the crystal stands in.</param>
/// <param name="Position">The crystal's world position, or the origin when nothing has placed it yet.</param>
/// <param name="Name">The display name in the client's own language.</param>
/// <param name="RequiredQuest">The quest that attunes it, or zero when none is needed.</param>
/// <param name="AetherstreamX">The aetherstream X coordinate: a fare-region coordinate, not a world position.</param>
/// <param name="AetherstreamY">The aetherstream Y coordinate: a fare-region coordinate, not a world position.</param>
/// <param name="ArrivalOnly">
/// True for a point that can only be arrived at, never departed from: a hidden aetheryte such as an airship landing,
/// which has no crystal to be positioned from at all.
/// </param>
public readonly record struct AetheryteEntry(
    uint Id,
    bool IsCityAetheryte,
    byte AethernetGroup,
    uint TerritoryId,
    Vector3 Position,
    string Name,
    uint RequiredQuest,
    int AetherstreamX,
    int AetherstreamY,
    bool ArrivalOnly = false);

/// <summary>
/// One residential aethernet shard, which unlike a city shard has no Aetheryte row of its own and exists only as a
/// crystal placed in the district's level file.
/// </summary>
/// <param name="TerritoryId">The residential district the crystal stands in.</param>
/// <param name="Position">The crystal's world position.</param>
/// <param name="PlaceNameId">
/// The PlaceName row of the ward the crystal serves, kept as a row id rather than as the text it resolves to, so it
/// reads in whatever language the client runs in and never has to be matched as a string.
/// </param>
/// <param name="Order">The crystal's index within its district's level file, which is a stable per-district key.</param>
public readonly record struct ResidentialShard(uint TerritoryId, Vector3 Position, uint PlaceNameId, int Order);

/// <summary>
/// Reads the aetheryte network out of the game's data: the identity of every crystal from the sheet, its world
/// position from the level files, the residential shards that exist only as placements, and what the logged-in
/// character has attuned to.
/// </summary>
public static class AetheryteHelper
{
    /// <summary>
    /// The shared-group asset family a residential aethernet crystal belongs to. Most districts place the Eorzean
    /// crystal (<c>sgbg_w_aet_001_06a.sgb</c>), but Shirogane places the Far Eastern model
    /// (<c>sgbg_w_aet_005_01j.sgb</c>), so a crystal is matched by this shared prefix rather than one exact path.
    /// </summary>
    public const string ResidentialCrystalAssetPrefix = "bgcommon/world/aet/shared/for_bg/sgbg_w_aet_";

    /// <summary>
    /// Reads every aetheryte and aethernet shard the sheet describes, without positions.<br/>
    /// The generic estate-hall rows (a Free Company and a private one per district) are left out: they are neither a
    /// teleport aetheryte nor a shard, carry no resolvable position, and would otherwise appear as a nameless pair at
    /// the origin in every district. A character's own estate is read from the teleport list instead. The sheet's
    /// placeholder row is left out on the same principle, by the one thing that distinguishes it: a real aetheryte
    /// always resolves to a name, and it resolves to none through either of its name columns.
    /// </summary>
    /// <returns>The aetheryte and shard identity rows.</returns>
    public static IReadOnlyList<AetheryteEntry> ReadAll()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var rows = new List<AetheryteEntry>();
            var sheet = ExcelSheetHelper.GetSheet<Aetheryte>();
            if (sheet == null)
                return (IReadOnlyList<AetheryteEntry>)rows;

            foreach (var row in sheet)
            {
                if (row.RowId == 0 || row.Territory.RowId == 0 || IsEstateHall(row))
                    continue;

                var name = ResolveName(row);
                if (name.Length == 0)
                    continue;

                var territoryId = row.Territory.RowId;
                var position = Vector3.Zero;
                foreach (var levelRef in row.Level)
                {
                    if (levelRef.ValueNullable is { } level)
                    {
                        position = new Vector3(level.X, level.Y, level.Z);
                        if (level.Territory.RowId != 0)
                            territoryId = level.Territory.RowId;
                        break;
                    }
                }

                rows.Add(new AetheryteEntry(
                    Id: row.RowId,
                    IsCityAetheryte: row.IsAetheryte,
                    AethernetGroup: row.AethernetGroup,
                    TerritoryId: territoryId,
                    Position: position,
                    Name: name,
                    RequiredQuest: row.RequiredQuest.RowId,
                    AetherstreamX: row.AetherstreamX,
                    AetherstreamY: row.AetherstreamY,
                    ArrivalOnly: row.Invisible));
            }

            return rows;
        }, []) ?? [];
    }

    /// <summary>
    /// Fills the entries with the world positions of the crystals placed in the level files. Every placed crystal
    /// carries its own Aetheryte row id, so a row is positioned by matching that id directly, with no map marker, map
    /// projection, or name matching; a row whose crystal was not in the objects given keeps the position it arrived
    /// with.
    /// </summary>
    /// <param name="entries">The identity rows, typically from <see cref="ReadAll"/>.</param>
    /// <param name="levelObjects">Level objects to read the crystals out of; anything else in the list is ignored.</param>
    /// <returns>The entries with positions filled where a crystal carried the row id.</returns>
    public static IReadOnlyList<AetheryteEntry> ApplyLevelPositions(
        IReadOnlyList<AetheryteEntry> entries,
        IReadOnlyList<LevelObject> levelObjects)
    {
        var positionById = new Dictionary<uint, Vector3>();
        foreach (var levelObject in levelObjects)
        {
            if (levelObject.Kind == LevelObjectKind.Aetheryte && levelObject.BaseId != 0)
                positionById.TryAdd(levelObject.BaseId, levelObject.Position);
        }

        var result = new List<AetheryteEntry>(entries.Count);
        foreach (var entry in entries)
            result.Add(positionById.TryGetValue(entry.Id, out var position) ? entry with { Position = position } : entry);

        return result;
    }

    /// <summary>Resolves an aetheryte's display name: its aethernet name when it has one, else its place name.</summary>
    /// <param name="aetheryteId">The Aetheryte row id.</param>
    /// <returns>The name, or empty when neither column resolves.</returns>
    public static string Name(uint aetheryteId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (aetheryteId != 0 && ExcelSheetHelper.TryGetRow<Aetheryte>(aetheryteId, out var row) && row is { } aetheryte)
                return ResolveName(aetheryte);

            return string.Empty;
        }, string.Empty) ?? string.Empty;
    }

    /// <summary>
    /// Whether an Aetheryte row is an estate hall, being a Free Company or private-estate teleport target rather than
    /// a real teleport aetheryte or an aethernet shard. An estate hall is flagged not-an-aetheryte and belongs to no
    /// aethernet group. This is how an owned estate is recognised in the teleport list, whose entry for one carries no
    /// ward, plot, or housing flag at all. Language-independent, since it reads flags and never a name.
    /// </summary>
    /// <param name="row">The Aetheryte sheet row.</param>
    /// <returns>True when the row is an estate hall.</returns>
    public static bool IsEstateHall(Aetheryte row) => IsEstateHall(row.IsAetheryte, row.AethernetGroup);

    /// <inheritdoc cref="IsEstateHall(Aetheryte)"/>
    /// <param name="aetheryteId">The Aetheryte row id.</param>
    /// <returns>True when the row is an estate hall; false when the id resolves to nothing.</returns>
    public static bool IsEstateHall(uint aetheryteId) => ReadEstateHall(aetheryteId).IsEstateHall;

    /// <summary>
    /// The estate-hall rule over the two flags alone, for a caller holding them without the row and for testing the
    /// rule with no game behind it. Prefer <see cref="IsEstateHall(Aetheryte)"/>.
    /// </summary>
    /// <param name="isAetheryte">The row's IsAetheryte flag.</param>
    /// <param name="aethernetGroup">The row's aethernet group.</param>
    /// <returns>True when the flags describe an estate hall.</returns>
    public static bool IsEstateHall(bool isAetheryte, byte aethernetGroup) => !isAetheryte && aethernetGroup == 0;

    /// <summary>
    /// Reads whether an aetheryte is an estate hall, its PlaceName row id, and its place name in one sheet lookup.
    /// The row id is what tells a Free Company estate from a private one in every client language; the name labels it.
    /// </summary>
    /// <param name="aetheryteId">The Aetheryte row id.</param>
    /// <returns>Whether it is an estate hall, its PlaceName row id, and that name's text.</returns>
    public static (bool IsEstateHall, uint PlaceNameId, string PlaceName) ReadEstateHall(uint aetheryteId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (aetheryteId != 0 && ExcelSheetHelper.TryGetRow<Aetheryte>(aetheryteId, out var row)
                && row is { } aetheryte && aetheryte.RowId != 0)
            {
                var name = aetheryte.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;
                return (IsEstateHall(aetheryte), aetheryte.PlaceName.RowId, name);
            }

            return (false, 0u, string.Empty);
        }, (false, 0u, string.Empty));
    }

    /// <summary>
    /// The aetherytes the logged-in character has attuned to, read from the game's own teleport list. Call it on the
    /// framework thread, and only once the character's state is loaded (<see cref="CharacterHelper.IsStateReady"/>).
    /// </summary>
    /// <returns>The attuned aetheryte ids.</returns>
    public static IReadOnlySet<uint> ReadUnlocked()
    {
        var unlocked = new HashSet<uint>();
        if (!CharacterHelper.IsStateReady)
            return unlocked;

        SafeExecutor.ExecuteSafely(() =>
        {
            foreach (var entry in NoireService.AetheryteList)
                unlocked.Add(entry.AetheryteId);
        });

        return unlocked;
    }

    /// <summary>
    /// The gil fare to each attuned aetheryte, read from the game's own teleport list, so it already reflects a
    /// discount the character may have. An aetheryte listed more than once keeps its cheapest entry. Framework thread, and
    /// only once the character's state is loaded.
    /// </summary>
    /// <returns>The fare per aetheryte id.</returns>
    public static IReadOnlyDictionary<uint, int> ReadTeleportFares()
    {
        var fares = new Dictionary<uint, int>();
        if (!CharacterHelper.IsStateReady)
            return fares;

        SafeExecutor.ExecuteSafely(() =>
        {
            foreach (var entry in NoireService.AetheryteList)
            {
                var cost = checked((int)entry.GilCost);
                fares[entry.AetheryteId] = fares.TryGetValue(entry.AetheryteId, out var existing)
                    ? Math.Min(existing, cost)
                    : cost;
            }
        });

        return fares;
    }

    /// <summary>Whether a shared-group asset path is a residential aethernet crystal.</summary>
    /// <param name="assetPath">The shared-group asset path (.sgb).</param>
    /// <returns>True when the asset is an aethernet crystal.</returns>
    public static bool IsResidentialCrystal(string assetPath)
        => assetPath.StartsWith(ResidentialCrystalAssetPrefix, StringComparison.OrdinalIgnoreCase)
           && assetPath.EndsWith(".sgb", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a residential district's aethernet shards, labelled with the ward each one serves.<br/>
    /// A district's level file places no Aetheryte-type objects at all, so its crystals are shared-group placements
    /// and are recognised by their asset path. Each is then labelled by the nearest map marker, which a district
    /// spanning a main map and a subdivision needs projected through each map's own offset;
    /// <see cref="MapCoordinateHelper.ProjectMarkers"/> takes care of that.
    /// </summary>
    /// <param name="districtTerritoryId">The residential district's TerritoryType row id.</param>
    /// <param name="objects">
    /// The district's placed level objects, or null to read them here. Pass them when they are already in hand, since
    /// reading a level file is the expensive part.
    /// </param>
    /// <returns>The district's shards, in the order its level file places them.</returns>
    public static IReadOnlyList<ResidentialShard> ReadResidentialShards(
        uint districtTerritoryId,
        IReadOnlyList<LevelObject>? objects = null)
    {
        var placed = objects ?? LevelFileHelper.ReadObjects(districtTerritoryId, LevelFileHelper.Files.PlanMap);
        var markers = MapCoordinateHelper.ProjectMarkers(districtTerritoryId, MapMarkerDataType.AethernetShard);

        var shards = new List<ResidentialShard>();
        var order = 0;
        foreach (var levelObject in placed)
        {
            if (levelObject.Kind != LevelObjectKind.SharedGroup || !IsResidentialCrystal(levelObject.AssetPath))
                continue;

            var placeNameId = MapCoordinateHelper.TryFindNearestMarker(markers, levelObject.Position, out var nearest)
                ? nearest.Marker.DataKey
                : 0u;

            shards.Add(new ResidentialShard(districtTerritoryId, levelObject.Position, placeNameId, order++));
        }

        return shards;
    }

    // An aetheryte's display name: its aethernet name when it has one, else its place name. Empty when neither
    // resolves. An empty name marks the sheet's placeholder row.
    private static string ResolveName(Aetheryte row)
    {
        var name = row.AethernetName.ValueNullable?.Name.ExtractText();
        if (string.IsNullOrWhiteSpace(name))
            name = row.PlaceName.ValueNullable?.Name.ExtractText();

        return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
    }
}
