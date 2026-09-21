using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Reads the aetheryte network from the game's data: crystal identity from the sheet, world positions from the level
/// files, the residential shards that exist only as placements, and what the logged-in character has attuned to.
/// </summary>
public static class AetheryteHelper
{
    /// <summary>
    /// The shared-group asset family a residential aethernet crystal belongs to. Shirogane places the Far Eastern
    /// model where other districts place the Eorzean one. Crystals match on this prefix, never an exact path.
    /// </summary>
    public const string ResidentialCrystalAssetPrefix = "bgcommon/world/aet/shared/for_bg/sgbg_w_aet_";

    /// <summary>Reads every aetheryte and aethernet shard, without positions. Estate-hall rows and the placeholder row are skipped.</summary>
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

    /// <summary>Fills the entries with the positions of the crystals placed in the level files, matched on Aetheryte row id.</summary>
    /// <param name="entries">The identity rows, typically from <see cref="ReadAll"/>.</param>
    /// <param name="levelObjects">Level objects to read the crystals out of.</param>
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

    /// <summary>Whether an Aetheryte row is an estate hall: flagged not-an-aetheryte and in no aethernet group.</summary>
    /// <param name="row">The Aetheryte sheet row.</param>
    /// <returns>True when the row is an estate hall.</returns>
    public static bool IsEstateHall(Aetheryte row) => IsEstateHall(row.IsAetheryte, row.AethernetGroup);

    /// <inheritdoc cref="IsEstateHall(Aetheryte)"/>
    /// <param name="aetheryteId">The Aetheryte row id.</param>
    /// <returns>True when the row is an estate hall. False when the id resolves to nothing.</returns>
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

        // With no character the empty set is a real answer. Mid-login it is not.
        if (!CharacterHelper.IsStateReady)
            return (unlocked, CharacterHelper.IsLoggedOut);

        var read = SafeExecutor.ExecuteSafely(() =>
        {
            RefreshTeleportList();
            foreach (var entry in NoireService.AetheryteList)
                unlocked.Add(entry.AetheryteId);

            return true;
        }, false);

        // The client fills the list asynchronously. The list outlives a character switch until the new character's refresh runs.
        return (unlocked, IsCurrentAnswer(read, unlocked.Count, teleportListOwner, CharacterHelper.LocalContentId));
    }

    internal static bool IsCurrentAnswer(bool read, int attunedCount, ulong listOwner, ulong character)
        => read && attunedCount > 0 && listOwner != 0 && listOwner == character;

    private static ulong teleportListOwner;

    /// <summary>
    /// Asks the game to refill its teleport list.<br/>
    /// Framework thread only, and only while the player object is in the world. Calling it without one access-violates inside game code.
    /// </summary>
    /// <returns>True when the game was asked.</returns>
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

    /// <summary>The game's teleport list: fares, favoured and residence entries, and the discount.</summary>
    /// <param name="Fares">The gil fare per aetheryte id, discounts applied.</param>
    /// <param name="Favoured">The aetherytes charged half as favourites.</param>
    /// <param name="Residences">The aetherytes charged a quarter as the character's home.</param>
    /// <param name="Multiplier">What a discount leaves of the full price, as a percentage. 100 when there is none.</param>
    public readonly record struct TeleportPrices(
        IReadOnlyDictionary<uint, int> Fares,
        IReadOnlySet<uint> Favoured,
        IReadOnlySet<uint> Residences,
        int Multiplier);

    /// <summary>
    /// Reads the whole teleport list. The discount is measured against the fare formula.<br/>
    /// Framework thread only, once <see cref="CharacterHelper.IsStateReady"/>.
    /// </summary>
    /// <returns>The prices, empty when the list cannot be read.</returns>
    public static TeleportPrices ReadTeleportPrices()
    {
        var fares = new Dictionary<uint, int>();
        var favoured = new HashSet<uint>();
        var residences = new HashSet<uint>();
        var multiplier = 100;

        if (!CharacterHelper.IsStateReady)
            return new TeleportPrices(fares, favoured, residences, multiplier);

        SafeExecutor.ExecuteSafely(() =>
        {
            RefreshTeleportList();

            var from = NoireService.ClientState.TerritoryType;
            var measured = new List<int>();

            foreach (var entry in NoireService.AetheryteList)
            {
                var cost = checked((int)entry.GilCost);
                fares[entry.AetheryteId] = fares.TryGetValue(entry.AetheryteId, out var existing)
                    ? Math.Min(existing, cost)
                    : cost;

                if (entry.IsFavourite)
                    favoured.Add(entry.AetheryteId);

                var residence = entry.IsApartment || entry.IsSharedHouse || entry.Plot != 0;
                if (residence)
                    residences.Add(entry.AetheryteId);

                measured.Add(MeasuredMultiplier(from, entry.AetheryteId, cost, entry.IsFavourite, residence));
            }

            multiplier = Agreed(measured);
        });

        return new TeleportPrices(fares, favoured, residences, multiplier);
    }

    /// <inheritdoc cref="ReadTeleportPrices"/>
    /// <returns>The fare per aetheryte id.</returns>
    public static IReadOnlyDictionary<uint, int> ReadTeleportFares() => ReadTeleportPrices().Fares;

    // -1 when the fare formula has nothing to compare against.
    private static int MeasuredMultiplier(uint fromTerritory, uint aetheryteId, int charged, bool favoured, bool residence)
    {
        if (!ExcelSheetHelper.TryGetRow<Aetheryte>(aetheryteId, out var row) || row is not { } aetheryte)
            return -1;

        var discount = residence ? TeleportDiscount.ResidentDistrict
            : favoured ? TeleportDiscount.Favoured
            : TeleportDiscount.None;

        var full = TeleportFareHelper.Fare(fromTerritory, aetheryte.Territory.RowId, discount);
        return full > 0 ? charged * 100 / full : -1;
    }

    // A single disagreeing entry falls back to the full price.
    private static int Agreed(List<int> measured)
    {
        var agreed = -1;
        foreach (var value in measured)
        {
            if (value < 0)
                continue;

            if (agreed < 0)
            {
                agreed = value;
                continue;
            }

            if (Math.Abs(agreed - value) > 1)
                return 100;
        }

        return agreed < 0 ? 100 : agreed;
    }

    /// <summary>Whether a shared-group asset path is a residential aethernet crystal.</summary>
    /// <param name="assetPath">The shared-group asset path (.sgb).</param>
    /// <returns>True when the asset is an aethernet crystal.</returns>
    public static bool IsResidentialCrystal(string assetPath)
        => assetPath.StartsWith(ResidentialCrystalAssetPrefix, StringComparison.OrdinalIgnoreCase)
           && assetPath.EndsWith(".sgb", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads a residential district's aethernet shards, labelled with the ward each one serves. A district's level
    /// file places no Aetheryte-type objects. Its crystals are shared-group placements recognised by asset path and
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

    // An empty result marks the placeholder row.
    private static string ResolveName(Aetheryte row)
    {
        var name = row.AethernetName.ValueNullable?.Name.ExtractText();
        if (string.IsNullOrWhiteSpace(name))
            name = row.PlaceName.ValueNullable?.Name.ExtractText();

        return string.IsNullOrWhiteSpace(name) ? string.Empty : name;
    }
}
