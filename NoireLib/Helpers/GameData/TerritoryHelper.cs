using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace NoireLib.Helpers;

/// <summary>
/// Reads what the game's sheets say about a territory: its name, the level files it is built from, whether it is a
/// real place, whether it is queued for rather than walked into, and which crossings out of it a quest gates. Every
/// read is guarded, and a missing sheet yields an empty result.
/// </summary>
public static class TerritoryHelper
{
    private static IReadOnlySet<uint>? flightUnlocked;
    private static IReadOnlySet<uint>? teleportBarred;
    private static IReadOnlyList<(uint TerritoryId, uint CompFlgSet)>? aetherCurrentZones;

    /// <summary>
    /// Resolves a territory id to its place name in the client's language, falling back to the housing sheets for an
    /// unnamed interior design and then to the bare row id.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The display name, or the id fallback.</returns>
    public static string Name(uint territoryId)
    {
        var fallback = $"#{territoryId}";
        return SafeExecutor.ExecuteSafely(() =>
        {
            var name = SheetPlaceName(territoryId);
            if (name.Length > 0)
                return name;

            var housing = HousingHelper.InteriorName(territoryId);
            return housing.Length > 0 ? housing : fallback;
        }, fallback) ?? fallback;
    }

    /// <summary>
    /// The place name the TerritoryType sheet itself carries, with no housing fallback, for callers where
    /// <see cref="Name"/>'s fallback would be circular.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The name, or empty when the sheet carries none.</returns>
    public static string SheetPlaceName(uint territoryId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (territoryId != 0 && ExcelSheetHelper.TryGetRow<TerritoryType>(territoryId, out var row) && row is { } territory)
            {
                var name = territory.PlaceName.ValueNullable?.Name.ExtractText();
                if (!string.IsNullOrWhiteSpace(name))
                    return name!;
            }

            return string.Empty;
        }, string.Empty) ?? string.Empty;
    }

    /// <summary>Resolves a PlaceName row id to its display name.</summary>
    /// <param name="placeNameRowId">The PlaceName row id.</param>
    /// <returns>The name, or empty when the row does not resolve.</returns>
    public static string PlaceName(uint placeNameRowId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (placeNameRowId != 0 && ExcelSheetHelper.TryGetRow<PlaceName>(placeNameRowId, out var row) && row is { } place)
            {
                var name = place.Name.ExtractText();
                if (!string.IsNullOrEmpty(name))
                    return name;
            }

            return string.Empty;
        }, string.Empty) ?? string.Empty;
    }

    /// <summary>The territory's PlaceName row id, which names the place language-independently.</summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The PlaceName row id, or zero.</returns>
    public static uint PlaceNameId(uint territoryId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (territoryId != 0 && ExcelSheetHelper.TryGetRow<TerritoryType>(territoryId, out var row) && row is { } territory)
                return territory.PlaceName.RowId;

            return 0u;
        }, 0u);
    }

    /// <summary>
    /// The aetheryte the territory is bound to, which for a residential district is the city crystal that offers its
    /// wards and elsewhere is the crystal a map teleport lands on.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The Aetheryte row id, or zero when the territory names none.</returns>
    public static uint AetheryteOf(uint territoryId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (territoryId != 0 && ExcelSheetHelper.TryGetRow<TerritoryType>(territoryId, out var row) && row is { } territory)
                return territory.Aetheryte.RowId;

            return 0u;
        }, 0u);
    }

    /// <summary>
    /// The quests the territory's own event handler names, which for a residential district is the unlock quest its
    /// aetheryte ward travel and ward changes are gated on.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The Quest row ids, or empty when the handler names none.</returns>
    public static IReadOnlyList<uint> ReadHandlerQuests(uint territoryId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (territoryId == 0 || !ExcelSheetHelper.TryGetRow<TerritoryType>(territoryId, out var row)
                || row is not { } territory)
                return (IReadOnlyList<uint>)[];

            if (!ExcelSheetHelper.TryGetRow<ArrayEventHandler>(territory.ArrayEventHandler.RowId, out var handlerRow)
                || handlerRow is not { } handler)
                return [];

            // The handler mixes quests with other event kinds, so an id only counts when the Quest sheet has it.
            var quests = new List<uint>();
            foreach (var entry in handler.Data)
            {
                if (entry.RowId != 0 && ExcelSheetHelper.TryGetRow<Quest>(entry.RowId, out var quest) && quest != null)
                    quests.Add(entry.RowId);
            }

            return (IReadOnlyList<uint>)quests;
        }, []) ?? [];
    }

    /// <summary>
    /// The territory's <c>Bg</c> string, which is the path its level files sit under and the thing
    /// <see cref="LevelFileHelper"/> reads them by.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The Bg string, or empty when the row does not resolve.</returns>
    public static string Bg(uint territoryId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (territoryId != 0 && ExcelSheetHelper.TryGetRow<TerritoryType>(territoryId, out var row) && row is { } territory)
                return territory.Bg.ExtractText();

            return string.Empty;
        }, string.Empty) ?? string.Empty;
    }

    /// <summary>Reads every territory that names a level file, flattened to the fields that identify the place it is.</summary>
    /// <returns>The territory entries.</returns>
    public static IReadOnlyList<TerritoryEntry> ReadAll()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var rows = new List<TerritoryEntry>();
            var sheet = ExcelSheetHelper.GetSheet<TerritoryType>();
            if (sheet == null)
                return (IReadOnlyList<TerritoryEntry>)rows;

            foreach (var territory in sheet)
            {
                if (territory.RowId == 0)
                    continue;

                var bg = territory.Bg.ExtractText();
                if (!string.IsNullOrEmpty(bg))
                    rows.Add(new TerritoryEntry(territory.RowId, bg, territory.PlaceName.RowId));
            }

            return rows;
        }, []) ?? [];
    }

    /// <summary>
    /// Reads the territories that describe a real place, being those with either a place name or a level file. The
    /// sheet carries a long tail of placeholder rows with neither that other sheets still point at.
    /// </summary>
    /// <returns>The territory row ids that describe a real place.</returns>
    public static IReadOnlySet<uint> ReadReal()
        => ReadWhere(static territory => territory.PlaceName.RowId != 0 || territory.Bg.ExtractText().Length > 0);

    /// <summary>
    /// Reads the territories entered from the duty finder rather than walked into, being those with a
    /// <c>ContentFinderCondition</c>, which leaves out the instanced rooms a character walks into through a door.
    /// </summary>
    /// <returns>The territory row ids that are queueable duties.</returns>
    public static IReadOnlySet<uint> ReadQueueableDuties()
        => ReadWhere(static territory => territory.ContentFinderCondition.RowId != 0);

    /// <summary>
    /// Reads the territories a mount can be summoned in, which is also the set flight can ever be unlocked in.
    /// Cached, since the sheet cannot change while the client runs.
    /// </summary>
    /// <returns>The territory row ids that allow mounts.</returns>
    public static IReadOnlySet<uint> ReadMountable()
        => flightUnlocked ??= ReadWhere(static territory => territory.Mount);

    /// <summary>
    /// Reads the territories whose intended use bars casting Teleport, which the game states as
    /// <c>TerritoryIntendedUse.EnableTeleport</c>. The Diadem's rows bar it; the Cosmic Exploration planets allow it.
    /// Cached, since the sheet cannot change while the client runs.
    /// </summary>
    /// <returns>The territory row ids Teleport cannot be cast from.</returns>
    public static IReadOnlySet<uint> ReadTeleportBarred()
        => teleportBarred ??= ReadWhere(static territory => territory.TerritoryIntendedUse.ValueNullable is { } use && !use.EnableTeleport);

    /// <summary>
    /// Reads the mountable territories that have aether currents, paired with the completion flag set that says
    /// whether the character has attuned to all of them. Cached, since the sheet cannot change while the client runs.
    /// </summary>
    /// <returns>Each territory with its AetherCurrentCompFlgSet row id.</returns>
    public static IReadOnlyList<(uint TerritoryId, uint CompFlgSet)> ReadAetherCurrentZones()
    {
        if (aetherCurrentZones != null)
            return aetherCurrentZones;

        var list = SafeExecutor.ExecuteSafely(() =>
        {
            var found = new List<(uint, uint)>();
            var sheet = ExcelSheetHelper.GetSheet<TerritoryType>();
            if (sheet == null)
                return found;

            foreach (var territory in sheet)
            {
                var compFlgSet = territory.AetherCurrentCompFlgSet.RowId;
                if (territory.RowId != 0 && compFlgSet != 0 && territory.Mount)
                    found.Add((territory.RowId, compFlgSet));
            }

            return found;
        }, []) ?? [];

        return aetherCurrentZones = list;
    }

    /// <summary>
    /// Reads the quest conditions the <c>ZoneSharedGroup</c> sheet puts on zone crossings. Every requirement row on a
    /// shared group is a condition, not just the first, since a barrier can sit behind several quests at once.
    /// </summary>
    /// <returns>The gates keyed by the gated level object's instance id; a crossing can carry several.</returns>
    public static IReadOnlyDictionary<uint, IReadOnlyList<ZoneCrossingGate>> ReadZoneCrossingGates()
    {
        var empty = (IReadOnlyDictionary<uint, IReadOnlyList<ZoneCrossingGate>>)new Dictionary<uint, IReadOnlyList<ZoneCrossingGate>>();

        return SafeExecutor.ExecuteSafely(() =>
        {
            var result = new Dictionary<uint, IReadOnlyList<ZoneCrossingGate>>();
            var sheet = ExcelSheetHelper.GetSubrowSheet<ZoneSharedGroup>();
            if (sheet == null)
                return empty;

            foreach (var set in sheet)
            {
                foreach (var row in set)
                {
                    var instanceId = row.LGBSharedGroup;
                    if (instanceId == 0)
                        continue;

                    var gates = new List<ZoneCrossingGate>();
                    for (var i = 0; i < row.RequirementRow.Count; i++)
                    {
                        var questId = row.RequirementRow[i].RowId;
                        if (questId == 0)
                            continue;

                        // A missing sequence column means the quest has to be complete, marked as 255.
                        var step = i < row.RequirementQuestSequence.Count ? (byte)row.RequirementQuestSequence[i] : (byte)255;
                        gates.Add(new ZoneCrossingGate(questId, step));
                    }

                    if (gates.Count > 0)
                        result[instanceId] = gates;
                }
            }

            return (IReadOnlyDictionary<uint, IReadOnlyList<ZoneCrossingGate>>)result;
        }, empty) ?? empty;
    }

    /// <summary>
    /// Picks the one territory per place worth reading and names the rest as its variants. A row is a variant only
    /// when it shares both the level path and the PlaceName row id, since an apartment and its private chambers are
    /// built from the same file but are different destinations.
    /// </summary>
    /// <param name="preferred">Territories that must win their group when present, whatever their row id.</param>
    /// <returns>Each non-canonical territory pointing at the canonical one, with canonical rows omitted.</returns>
    public static IReadOnlyDictionary<uint, uint> BuildAliases(IReadOnlySet<uint>? preferred = null)
        => BuildAliases(ReadAll(), preferred);

    /// <inheritdoc cref="BuildAliases(IReadOnlySet{uint})"/>
    /// <param name="territories">The territories to group, typically from <see cref="ReadAll"/>.</param>
    /// <param name="preferred">Territories that must win their group when present, whatever their row id.</param>
    /// <returns>Each non-canonical territory pointing at the canonical one, with canonical rows omitted.</returns>
    public static IReadOnlyDictionary<uint, uint> BuildAliases(
        IEnumerable<TerritoryEntry> territories,
        IReadOnlySet<uint>? preferred = null)
    {
        var canonical = new Dictionary<(string, uint), uint>();
        var members = new Dictionary<(string, uint), List<uint>>();

        foreach (var (territoryId, levelPath, placeNameId) in territories)
        {
            if (territoryId == 0 || string.IsNullOrEmpty(levelPath))
                continue;

            var key = (levelPath, placeNameId);
            if (!members.TryGetValue(key, out var list))
            {
                list = [];
                members[key] = list;
            }

            list.Add(territoryId);

            if (!canonical.TryGetValue(key, out var existing))
            {
                canonical[key] = territoryId;
                continue;
            }

            // Failing a preferred entry, the lowest row id wins: variants are added by later patches and take later ids.
            var candidateIsPreferred = preferred != null && preferred.Contains(territoryId);
            var existingIsPreferred = preferred != null && preferred.Contains(existing);
            if (candidateIsPreferred && !existingIsPreferred)
                canonical[key] = territoryId;
            else if (candidateIsPreferred == existingIsPreferred && territoryId < existing)
                canonical[key] = territoryId;
        }

        var aliases = new Dictionary<uint, uint>();
        foreach (var (key, list) in members)
        {
            var target = canonical[key];
            foreach (var territoryId in list)
            {
                if (territoryId != target)
                    aliases[territoryId] = target;
            }
        }

        return aliases;
    }

    /// <summary>Resolves a territory onto its canonical one, returning it unchanged when it is its own.</summary>
    /// <param name="aliases">The alias map from <see cref="BuildAliases(IReadOnlySet{uint})"/>.</param>
    /// <param name="territoryId">The territory to resolve.</param>
    /// <returns>The canonical territory id.</returns>
    public static uint ResolveAlias(IReadOnlyDictionary<uint, uint>? aliases, uint territoryId)
        => aliases != null && aliases.TryGetValue(territoryId, out var canonical) ? canonical : territoryId;

    /// <summary>Collects the row ids of every non-zero TerritoryType row the predicate accepts.</summary>
    /// <param name="predicate">The test each row is put to.</param>
    /// <returns>The matching territory row ids, or an empty set when the sheet cannot be read.</returns>
    private static IReadOnlySet<uint> ReadWhere(System.Func<TerritoryType, bool> predicate)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var found = new HashSet<uint>();
            var sheet = ExcelSheetHelper.GetSheet<TerritoryType>();
            if (sheet == null)
                return (IReadOnlySet<uint>)found;

            foreach (var territory in sheet)
            {
                if (territory.RowId != 0 && predicate(territory))
                    found.Add(territory.RowId);
            }

            return (IReadOnlySet<uint>)found;
        }, new HashSet<uint>()) ?? new HashSet<uint>();
    }
}
