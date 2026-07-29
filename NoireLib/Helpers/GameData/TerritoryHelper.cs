using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace NoireLib.Helpers;

/// <summary>One territory reduced to the three fields that identify the place it is.</summary>
/// <param name="TerritoryId">The TerritoryType row id.</param>
/// <param name="LevelPath">The territory's <c>Bg</c> string, which is the path its level files sit under.</param>
/// <param name="PlaceNameId">The territory's PlaceName row id, or zero when it has none.</param>
public readonly record struct TerritoryEntry(uint TerritoryId, string LevelPath, uint PlaceNameId);

/// <summary>
/// One quest condition standing on a zone crossing, in the form the <c>ZoneSharedGroup</c> sheet states it.
/// </summary>
/// <param name="QuestId">The quest row id.</param>
/// <param name="Step">The quest sequence step the crossing opens at; 255 means the quest must be complete.</param>
public readonly record struct ZoneCrossingGate(uint QuestId, byte Step);

/// <summary>
/// Answers what the game's own sheets say about a territory: its name, the level files it is built from, whether it
/// is a real place at all, whether it is queued for rather than walked into, and which crossings out of it are locked
/// behind a quest. Every read is guarded; a missing sheet yields an empty result.
/// </summary>
public static class TerritoryHelper
{
    private static IReadOnlySet<uint>? flightUnlocked;
    private static IReadOnlyList<(uint TerritoryId, uint CompFlgSet)>? aetherCurrentZones;

    /// <summary>
    /// Resolves a territory id to its place name in the client's own language, falling back to the bare id when the
    /// sheet carries no name. The fallback is the number alone rather than a worded one, so it reads the same
    /// whatever language the client runs in.<br/>
    /// A housing interior the sheet leaves unnamed is named from the housing sheets instead, since the interior
    /// designs an estate can be renovated into belong to no district and so carry no place name of their own.
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
    /// The place name the TerritoryType sheet itself carries, with no housing fallback. Use it when the fallback
    /// would be circular, and <see cref="Name"/> everywhere else.
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

    /// <summary>Resolves a PlaceName row id to its display name, or empty when it does not resolve.</summary>
    /// <param name="placeNameRowId">The PlaceName row id.</param>
    /// <returns>The name, or empty.</returns>
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
    /// The territory's <c>Bg</c> string, which is the path its level files sit under and the thing
    /// <see cref="LevelFileHelper"/> reads them by.
    /// </summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The Bg string, or empty.</returns>
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
    /// Reads the territories the game actually describes, being those with either a place name or a level file. The
    /// TerritoryType sheet carries a long tail of pure placeholder rows that have neither, and other sheets still
    /// point at them, so without this a row naming one of them names a nameless zone. The test is the sheet's own
    /// emptiness rather than a list, so a row that gains content later is picked up on its own.
    /// </summary>
    /// <returns>The territory row ids that describe a real place.</returns>
    public static IReadOnlySet<uint> ReadReal()
        => ReadWhere(static territory => territory.PlaceName.RowId != 0 || territory.Bg.ExtractText().Length > 0);

    /// <summary>
    /// Reads the territories entered from the duty finder rather than walked into, being those with a
    /// <c>ContentFinderCondition</c>. That is the game's own statement that a place is queued for, and it covers
    /// every dungeon, trial, raid, and quest battle while correctly leaving out the many instanced rooms a character
    /// simply walks into through a door.
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
    /// Reads the quest conditions the <c>ZoneSharedGroup</c> sheet puts on zone crossings, keyed by the level-file
    /// instance id of the gated object. Every requirement row on a shared group is a condition on the crossing, not
    /// just the first: a barrier can sit behind several quests at once.
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

                        // A missing sequence column means the quest simply has to be complete. 255 marks that case.
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
    /// Picks the one territory per place worth reading, and names the rest as its variants. Many TerritoryType rows
    /// share a place's level files: the open-world zone plus its duty, quest-battle, and PvP versions (Central Shroud
    /// alone has nineteen).<br/>
    /// Sharing a level file is <b>not</b> enough to call two rows the same place: a residential district's apartment
    /// and its private chambers are built from the same file but are different destinations. A row is a variant only
    /// when it also shares the same place name, compared as the PlaceName row id rather than text.
    /// </summary>
    /// <param name="preferred">
    /// Territories that must win their group when present, whatever their row id. A residential district is the case
    /// that needs this: the instanced district the character actually stands in shares its path with an unused legacy
    /// row of a lower id.
    /// </param>
    /// <returns>The variant map: each non-canonical territory pointing at the canonical one; canonical rows are omitted.</returns>
    public static IReadOnlyDictionary<uint, uint> BuildAliases(IReadOnlySet<uint>? preferred = null)
        => BuildAliases(ReadAll(), preferred);

    /// <inheritdoc cref="BuildAliases(IReadOnlySet{uint})"/>
    /// <param name="territories">The territories to group, typically from <see cref="ReadAll"/>.</param>
    /// <param name="preferred">The territories that must win their group when present.</param>
    /// <returns>The variant map: each non-canonical territory pointing at the canonical one.</returns>
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

            // A preferred territory wins its group outright; otherwise the lowest row id wins, which is always the
            // base zone because the variants are added by later patches and take later ids.
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
    /// <param name="aliases">The alias map from <see cref="BuildAliases"/>.</param>
    /// <param name="territoryId">The territory to resolve.</param>
    /// <returns>The canonical territory id.</returns>
    public static uint ResolveAlias(IReadOnlyDictionary<uint, uint>? aliases, uint territoryId)
        => aliases != null && aliases.TryGetValue(territoryId, out var canonical) ? canonical : territoryId;

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
