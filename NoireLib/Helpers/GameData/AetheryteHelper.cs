using FFXIVClientStructs.FFXIV.Client.Game.UI;
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
/// <param name="ArrivalOnly">True for a hidden aetheryte such as an airship landing, which can be arrived at but never departed from and has no crystal.</param>
/// <param name="Ward">The one-based residential ward the point stands in, or zero when it stands in no particular ward; only an owned estate's teleport target carries it.</param>
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
    bool ArrivalOnly = false,
    int Ward = 0);

/// <summary>
/// One residential aethernet shard, which unlike a city shard has no Aetheryte row of its own and exists only as a
/// crystal placed in the district's level file.
/// </summary>
/// <param name="TerritoryId">The residential district the crystal stands in.</param>
/// <param name="Position">The crystal's world position.</param>
/// <param name="PlaceNameId">The PlaceName row id of the ward the crystal serves.</param>
/// <param name="Order">The crystal's index within its district's level file, a stable per-district key.</param>
public readonly record struct ResidentialShard(uint TerritoryId, Vector3 Position, uint PlaceNameId, int Order);

/// <summary>
/// Reads the aetheryte network from the game's data: crystal identity from the sheet, world positions from the level
/// files, the residential shards that exist only as placements, and what the logged-in character has attuned to.
/// </summary>
public static class AetheryteHelper
{
    /// <summary>
    /// The shared-group asset family a residential aethernet crystal belongs to. Shirogane places the Far Eastern
    /// model where other districts place the Eorzean one, so crystals match on this prefix rather than an exact path.
    /// </summary>
    public const string ResidentialCrystalAssetPrefix = "bgcommon/world/aet/shared/for_bg/sgbg_w_aet_";

    /// <summary>
    /// Reads every aetheryte and aethernet shard the sheet describes, without positions. The generic estate-hall rows
    /// are skipped, since a character's own estate comes from the teleport list instead, as is the sheet's placeholder
    /// row, which resolves to no name through either name column.
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
    /// Fills the entries with the world positions of the crystals placed in the level files, matching on the Aetheryte
    /// row id each placed crystal carries. A row whose crystal is absent keeps the position it arrived with.
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
    /// a teleport aetheryte or an aethernet shard. An estate hall is flagged not-an-aetheryte and belongs to no
    /// aethernet group, which is also how an owned estate is recognised in the teleport list, whose entry for one
    /// carries no ward, plot, or housing flag.
    /// </summary>
    /// <param name="row">The Aetheryte sheet row.</param>
    /// <returns>True when the row is an estate hall.</returns>
    public static bool IsEstateHall(Aetheryte row) => IsEstateHall(row.IsAetheryte, row.AethernetGroup);

    /// <inheritdoc cref="IsEstateHall(Aetheryte)"/>
    /// <param name="aetheryteId">The Aetheryte row id.</param>
    /// <returns>True when the row is an estate hall; false when the id resolves to nothing.</returns>
    public static bool IsEstateHall(uint aetheryteId) => ReadEstateHall(aetheryteId).IsEstateHall;

    /// <summary>Applies the estate-hall rule to the two flags alone, for a caller holding them without the row.</summary>
    /// <param name="isAetheryte">The row's IsAetheryte flag.</param>
    /// <param name="aethernetGroup">The row's aethernet group.</param>
    /// <returns>True when the flags describe an estate hall.</returns>
    public static bool IsEstateHall(bool isAetheryte, byte aethernetGroup) => !isAetheryte && aethernetGroup == 0;

    /// <summary>
    /// Reads whether an aetheryte is an estate hall, its PlaceName row id, and its place name in one sheet lookup. The
    /// row id separates a Free Company estate from a private one in every client language.
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
    /// Reads the aetherytes the logged-in character has attuned to from the game's teleport list. Framework thread
    /// only, and only once <see cref="CharacterHelper.IsStateReady"/>.
    /// </summary>
    /// <returns>The attuned aetheryte ids.</returns>
    public static IReadOnlySet<uint> ReadUnlocked() => ReadUnlockedState().Unlocked;

    /// <summary>
    /// Reads the attuned aetherytes together with whether the teleport list could be read at all. An empty set with
    /// <c>Known</c> false means the list was not there to read, never that the character is attuned to nothing.
    /// </summary>
    /// <returns>The attuned aetheryte ids, and whether the read produced a real answer.</returns>
    public static (IReadOnlySet<uint> Unlocked, bool Known) ReadUnlockedState()
    {
        var unlocked = new HashSet<uint>();

        // With no character there is nothing to attune, so the empty set is a real answer; mid-login the same empty
        // set is only a read that could not happen.
        if (!CharacterHelper.IsStateReady)
            return (unlocked, CharacterHelper.IsLoggedOut);

        var read = SafeExecutor.ExecuteSafely(() =>
        {
            RefreshTeleportList();
            foreach (var entry in NoireService.AetheryteList)
                unlocked.Add(entry.AetheryteId);

            return true;
        }, false);

        // The client fills the list asynchronously, so an empty result usually means it has not filled yet rather than
        // a character attuned to nothing. The list is game memory that outlives a character switch, so a non-empty one
        // still reads as the previous character's attunements until the new character's refresh runs.
        return (unlocked, IsCurrentAnswer(read, unlocked.Count, teleportListOwner, CharacterHelper.LocalContentId));
    }

    /// <summary>
    /// Decides whether a teleport-list read answers for the character standing there now: it succeeded, found at least
    /// one attunement, and the list was last refreshed by that same character.
    /// </summary>
    /// <param name="read">Whether the list was read at all.</param>
    /// <param name="attunedCount">How many attunements the read found.</param>
    /// <param name="listOwner">The character the list was last refreshed for, zero when it never was.</param>
    /// <param name="character">The character logged in now, zero when none is.</param>
    /// <returns>True when the read is the current character's answer.</returns>
    internal static bool IsCurrentAnswer(bool read, int attunedCount, ulong listOwner, ulong character)
        => read && attunedCount > 0 && listOwner != 0 && listOwner == character;

    // The character the teleport list was last refreshed for. The list outlives a character switch, so a read is only
    // an answer when the refresh that filled it was asked by the character standing there now.
    private static ulong teleportListOwner;

    /// <summary>
    /// Asks the game to refill its teleport list, the source of the attuned set, the fares, and the character's own
    /// estates. Framework thread only, and gated on the player object being in the world rather than on the character
    /// state alone: the game builds each fare from where the character stands, so calling it while that object is
    /// absent access-violates inside game code, past any try/catch.
    /// </summary>
    /// <returns>True when the game was asked, false when it was not safe to ask.</returns>
    public static unsafe bool RefreshTeleportList()
    {
        if (!CharacterHelper.IsPlayerLoaded)
            return false;

        var telepo = Telepo.Instance();
        if (telepo == null)
            return false;

        telepo->UpdateAetheryteList();
        teleportListOwner = CharacterHelper.LocalContentId;
        return true;
    }

    /// <summary>
    /// Reads the gil fare to each attuned aetheryte from the game's teleport list, so any discount the character has
    /// is already applied. An aetheryte listed more than once keeps its cheapest entry. Framework thread only, and
    /// only once <see cref="CharacterHelper.IsStateReady"/>.
    /// </summary>
    /// <returns>The fare per aetheryte id.</returns>
    public static IReadOnlyDictionary<uint, int> ReadTeleportFares()
    {
        var fares = new Dictionary<uint, int>();
        if (!CharacterHelper.IsStateReady)
            return fares;

        SafeExecutor.ExecuteSafely(() =>
        {
            RefreshTeleportList();
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
    /// Reads a residential district's aethernet shards, labelled with the ward each one serves. A district's level
    /// file places no Aetheryte-type objects, so its crystals are shared-group placements recognised by asset path and
    /// labelled from the nearest map marker.
    /// </summary>
    /// <param name="districtTerritoryId">The residential district's TerritoryType row id.</param>
    /// <param name="objects">The district's placed level objects, or null to read the level file here.</param>
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

    // An aetheryte's aethernet name when it has one, else its place name. An empty result marks the placeholder row.
    private static string ResolveName(Aetheryte row)
    {
        var name = row.AethernetName.ValueNullable?.Name.ExtractText();
        if (string.IsNullOrWhiteSpace(name))
            name = row.PlaceName.ValueNullable?.Name.ExtractText();

        return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
    }
}
