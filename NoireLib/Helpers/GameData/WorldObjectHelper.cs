using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.Game.Event;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// Finds event objects, event NPCs, aetherytes and shared groups from any mix of id, name, script, handler or asset
/// path, and returns everything the sheets and level files say about each, placements included. World-wide reads
/// go through an index built once per game build and kept on disk.
/// </summary>
public static class WorldObjectHelper
{
    private const string CacheFileName = "NoireLibWorldObjects.json";
    private const int HandlerContentShift = 16;

    // A scope this small reads its own level files while the world index is not ready.
    private const int DirectReadTerritoryLimit = 8;

    private static readonly object CacheLock = new();
    private static readonly SemaphoreSlim IndexGate = new(1, 1);
    private static readonly Dictionary<string, IReadOnlySet<uint>> ScriptObjectIds = new(StringComparer.Ordinal);

    private static IReadOnlyDictionary<uint, uint>? variantAliases;
    private static WorldObjectIndex? worldIndex;

    #region Index

    /// <summary>Whether the world index is loaded.</summary>
    public static bool IsIndexReady => worldIndex != null;

    /// <summary>
    /// Loads the world index from disk, or builds it from the level files and writes it when the stored one belongs to
    /// another game build. Building reads the whole world and takes seconds.
    /// </summary>
    /// <param name="cancellationToken">Stops a build between two territories.</param>
    /// <returns>A task that completes once the index is ready.</returns>
    public static Task PrepareIndexAsync(CancellationToken cancellationToken = default)
        => AsyncHelper.RunInBackgroundAsync(() => { EnsureIndex(cancellationToken); }, "world object index");

    /// <summary>Drops the world index from memory and deletes its file, for the next lookup to build it again.</summary>
    public static void ResetIndex()
    {
        IndexGate.Wait();

        try
        {
            worldIndex = null;
            OpenCache()?.Invalidate();
        }
        finally
        {
            IndexGate.Release();
        }
    }

    #endregion

    #region Lookups

    /// <summary>
    /// Finds every row meeting all the query's criteria.<br/>
    /// Placements outside a few territories wait for the world index, built by the first call. <see cref="FindAsync"/> runs in the background.
    /// </summary>
    /// <param name="query">What to look for and where.</param>
    /// <returns>One model per row found, event objects first, then NPCs, aetherytes and shared groups.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="query"/> is null.</exception>
    /// <exception cref="ArgumentException">The query names no criterion.</exception>
    public static IReadOnlyList<WorldObject> Find(WorldObjectQuery query)
        => Find(query, CancellationToken.None);

    /// <summary>Finds every row meeting all the query's criteria and describes it, off the framework thread.</summary>
    /// <param name="query">What to look for and where.</param>
    /// <param name="cancellationToken">Stops an index build between two territories.</param>
    /// <returns>One model per row found.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="query"/> is null.</exception>
    /// <exception cref="ArgumentException">If the query names no criterion.</exception>
    public static async Task<IReadOnlyList<WorldObject>> FindAsync(WorldObjectQuery query, CancellationToken cancellationToken = default)
    {
        Validate(query);

        var found = await AsyncHelper.RunInBackgroundAsync(
            () => Find(query, cancellationToken),
            (IReadOnlyList<WorldObject>)[],
            "world object lookup").ConfigureAwait(false);

        return found ?? [];
    }

    /// <summary>Describes one row.</summary>
    /// <param name="kind">The kind the row belongs to.</param>
    /// <param name="baseId">The row id.</param>
    /// <param name="includePlacements">Whether to read its placements across the world.</param>
    /// <returns>The model, or null when the sheet holds no such row.</returns>
    public static WorldObject? Describe(PlacementKinds kind, uint baseId, bool includePlacements = false)
    {
        if (baseId == 0 || kind is not (PlacementKinds.EventObject or PlacementKinds.EventNpc or PlacementKinds.Aetheryte))
            return null;

        var found = Find(WorldObjectQuery.ByBaseId(kind, baseId) with { IncludePlacements = includePlacements });
        return found.Count == 0 ? null : found[0];
    }

    /// <summary>
    /// Describes a loaded event object, NPC or aetheryte, with its placements in the loaded territory ordered nearest
    /// first to where the object stands.
    /// </summary>
    /// <param name="gameObject">The loaded object.</param>
    /// <returns>The model, or null for another kind of object or a row the sheets do not hold.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="gameObject"/> is null.</exception>
    public static WorldObject? DescribeLoaded(IGameObject gameObject)
    {
        ArgumentNullException.ThrowIfNull(gameObject);

        var kind = gameObject.ObjectKind switch
        {
            ObjectKind.EventObj => PlacementKinds.EventObject,
            ObjectKind.EventNpc => PlacementKinds.EventNpc,
            ObjectKind.Aetheryte => PlacementKinds.Aetheryte,
            _ => PlacementKinds.None,
        };

        if (kind == PlacementKinds.None || gameObject.BaseId == 0)
            return null;

        var territoryId = LayoutHelper.LoadedTerritory();
        var scope = territoryId == 0 ? PlacementScope.World : PlacementScope.Territory(territoryId);
        var found = Find(WorldObjectQuery.ByBaseId(kind, gameObject.BaseId) with { Scope = scope });

        return found.Count == 0 ? null : found[0] with { Placements = OrderByDistance(found[0].Placements, gameObject.Position) };
    }

    /// <summary>The rows of one kind placed somewhere in the level files. The first call builds the world index and takes seconds.</summary>
    /// <param name="kind">The kind to read. One flag.</param>
    /// <param name="territoryIds">The territories a placement has to stand in, or null for any.</param>
    /// <param name="includeFestivalOnly">Whether a row only a seasonal event places counts.</param>
    /// <param name="cancellationToken">Stops an index build between two territories.</param>
    /// <returns>The row ids, or an empty set for another kind or an index that could not be built.</returns>
    public static IReadOnlySet<uint> PlacedRowIds(
        PlacementKinds kind,
        IReadOnlySet<uint>? territoryIds = null,
        bool includeFestivalOnly = false,
        CancellationToken cancellationToken = default)
    {
        var placed = new HashSet<uint>();

        if (kind is not (PlacementKinds.EventObject or PlacementKinds.EventNpc or PlacementKinds.Aetheryte))
            return placed;

        SafeExecutor.ExecuteSafely(() =>
        {
            foreach (var placement in EnsureIndex(cancellationToken).Placements)
            {
                if (placement.Kind != kind || placement.TerritoryId == 0)
                    continue;

                if (!includeFestivalOnly && placement.FestivalId != 0)
                    continue;

                if (territoryIds == null || territoryIds.Contains(placement.TerritoryId))
                    placed.Add(placement.BaseId);
            }
        });

        return placed;
    }

    /// <summary>
    /// Checks whether an EObj row runs a CustomTalk whose script name starts with a prefix. The objects of each prefix
    /// are read once and kept.
    /// </summary>
    /// <param name="eventObjectId">The EObj row id, the base id a loaded event object reports.</param>
    /// <param name="scriptPrefix">The start of the script name, compared case sensitively.</param>
    /// <returns>True when the object runs a matching script, false otherwise or when the sheets cannot be read.</returns>
    public static bool RunsScript(uint eventObjectId, string scriptPrefix)
    {
        if (eventObjectId == 0 || string.IsNullOrEmpty(scriptPrefix))
            return false;

        IReadOnlySet<uint>? objectIds;

        lock (CacheLock)
            ScriptObjectIds.TryGetValue(scriptPrefix, out objectIds);

        if (objectIds == null)
        {
            var talkIds = ReadTalkIds(scriptPrefix);
            objectIds = ReadRowsRunning(talkId => talkIds.Contains(talkId), PlacementKinds.EventObject)
                .Select(static row => row.BaseId)
                .ToHashSet();

            // An empty answer is also what an unreadable sheet gives.
            if (objectIds.Count > 0)
            {
                lock (CacheLock)
                    ScriptObjectIds[scriptPrefix] = objectIds;
            }
        }

        return objectIds.Contains(eventObjectId);
    }

    #endregion

    #region Internals

    internal static bool NameMatches(string? candidate, string name, NameMatch match)
    {
        if (string.IsNullOrEmpty(candidate))
            return false;

        return match == NameMatch.Contains
            ? candidate.Contains(name, StringComparison.OrdinalIgnoreCase)
            : candidate.Equals(name, StringComparison.OrdinalIgnoreCase);
    }

    internal static PlacementKinds KindOf(LevelObjectKind kind) => kind switch
    {
        LevelObjectKind.EventObject => PlacementKinds.EventObject,
        LevelObjectKind.EventNpc => PlacementKinds.EventNpc,
        LevelObjectKind.Aetheryte => PlacementKinds.Aetheryte,
        LevelObjectKind.SharedGroup => PlacementKinds.SharedGroup,
        _ => PlacementKinds.None,
    };

    internal static IReadOnlyList<uint> FoldVariants(IEnumerable<uint> territoryIds)
        => FoldVariants(territoryIds, ReadVariantAliases());

    internal static IReadOnlyList<uint> FoldVariants(IEnumerable<uint> territoryIds, IReadOnlyDictionary<uint, uint>? aliases)
    {
        var folded = new SortedSet<uint>();

        foreach (var territoryId in territoryIds)
        {
            if (territoryId != 0)
                folded.Add(TerritoryHelper.ResolveAlias(aliases, territoryId));
        }

        return folded.ToArray();
    }

    internal static IReadOnlyList<WorldObjectPlacement> OrderByDistance(IReadOnlyList<WorldObjectPlacement> placements, Vector3 position)
        => placements.OrderBy(placement => Vector3.DistanceSquared(placement.Position, position)).ToArray();

    internal static void Validate(WorldObjectQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (!query.NamesARowCriterion && query.AssetPathFragment == null)
            throw new ArgumentException("The query names no criterion to look for.", nameof(query));
    }

    private static IReadOnlyList<WorldObject> Find(WorldObjectQuery query, CancellationToken cancellationToken)
    {
        Validate(query);

        var wantsSharedGroups = query.AssetPathFragment != null
            && query.IncludePlacements
            && query.Kinds.HasFlag(PlacementKinds.SharedGroup);

        var rows = query.NamesARowCriterion ? ReadRows(query) : [];
        var objects = new List<WorldObject>(rows.Count);

        WorldObjectIndex? index = null;
        IReadOnlySet<uint>? territoryIds = null;

        if (query.IncludePlacements && (rows.Count > 0 || wantsSharedGroups))
            (index, territoryIds) = ResolvePlacementSource(query.Scope, cancellationToken);

        var territories = new TerritoryCache();

        foreach (var (kind, baseId) in rows)
        {
            var placements = index == null
                ? []
                : index.PlacementsOf(kind, baseId, territoryIds).Select(placement => ToPlacement(placement, index, territories)).ToArray();

            objects.Add(DescribeRow(kind, baseId, placements));
        }

        if (wantsSharedGroups && index != null)
        {
            foreach (var group in index.SharedGroupsMatching(query.AssetPathFragment!, territoryIds).GroupBy(static placement => placement.AssetPathIndex))
            {
                var assetPath = index.AssetPaths[group.Key];
                var placements = group.Select(placement => ToPlacement(placement, index, territories)).ToArray();

                objects.Add(new WorldObject(PlacementKinds.SharedGroup, 0, Path.GetFileNameWithoutExtension(assetPath), string.Empty,
                    string.Empty, [], null, null, null, placements, assetPath));
            }
        }

        return objects;
    }

    private static (WorldObjectIndex Index, IReadOnlySet<uint>? TerritoryIds) ResolvePlacementSource(PlacementScope scope, CancellationToken cancellationToken)
    {
        if (ReferenceEquals(scope, PlacementScope.World))
            return (EnsureIndex(cancellationToken), null);

        var territories = scope.ResolveTerritories();
        var territoryIds = territories.ToHashSet();

        if (worldIndex is { } ready)
            return (ready, territoryIds);

        if (territories.Count <= DirectReadTerritoryLimit)
            return (WorldObjectIndex.Build(territories, cancellationToken), territoryIds);

        return (EnsureIndex(cancellationToken), territoryIds);
    }

    private static WorldObjectIndex EnsureIndex(CancellationToken cancellationToken)
    {
        if (worldIndex is { } ready)
            return ready;

        IndexGate.Wait(cancellationToken);

        try
        {
            if (worldIndex is { } loaded)
                return loaded;

            var cache = OpenCache();
            var stored = cache?.Read(WorldObjectIndex.SchemaVersion);

            WorldObjectIndex index;

            if (stored is { Data.Length: > 0 })
            {
                index = WorldObjectIndex.FromFile(stored);
            }
            else
            {
                index = WorldObjectIndex.Build(PlacementScope.World.ResolveTerritories(), cancellationToken);

                if (index.Placements.Count > 0)
                    cache?.Write(index.ToFile(), WorldObjectIndex.SchemaVersion);
            }

            // An empty index is also what an unreadable archive gives.
            if (index.Placements.Count > 0)
                worldIndex = index;

            return index;
        }
        finally
        {
            IndexGate.Release();
        }
    }

    private static VersionedJsonCache<WorldObjectIndexFile>? OpenCache()
        => FileHelper.GetPluginConfigDirectory() is { } directory
            ? new VersionedJsonCache<WorldObjectIndexFile>(Path.Combine(directory, CacheFileName))
            : null;

    internal static List<(PlacementKinds Kind, uint BaseId)> ReadRows(WorldObjectQuery query)
    {
        HashSet<(PlacementKinds Kind, uint BaseId)>? rows = null;

        void Narrow(IEnumerable<(PlacementKinds Kind, uint BaseId)> found)
        {
            var kept = found.Where(row => query.Kinds.HasFlag(row.Kind)).ToHashSet();

            if (rows == null)
                rows = kept;
            else
                rows.IntersectWith(kept);
        }

        if (query.BaseIds != null)
            Narrow(ReadExistingRows(query.BaseIds, query.Kinds));

        if (query.Name != null)
            Narrow(ReadNamedRows(query.Name, query.NameMatch, query.Kinds, query.Language));

        if (query.ScriptPrefix != null)
        {
            var talkIds = ReadTalkIds(query.ScriptPrefix);
            Narrow(ReadRowsRunning(handlerId => talkIds.Contains(handlerId), query.Kinds));
        }

        if (query.HandlerIds != null)
            Narrow(ReadRowsRunning(query.HandlerIds.Contains, query.Kinds));

        if (query.HandlerContent is { } content)
            Narrow(ReadRowsRunning(handlerId => (EventHandlerContent)(handlerId >> HandlerContentShift) == content, query.Kinds));

        return rows == null
            ? []
            : rows.OrderBy(static row => row.Kind).ThenBy(static row => row.BaseId).ToList();
    }

    private static IEnumerable<(PlacementKinds Kind, uint BaseId)> ReadExistingRows(IReadOnlySet<uint> baseIds, PlacementKinds kinds)
    {
        var rows = new List<(PlacementKinds Kind, uint BaseId)>();

        SafeExecutor.ExecuteSafely(() =>
        {
            foreach (var baseId in baseIds)
            {
                if (kinds.HasFlag(PlacementKinds.EventObject) && ExcelSheetHelper.TryGetRow<EObj>(baseId, out _))
                    rows.Add((PlacementKinds.EventObject, baseId));

                if (kinds.HasFlag(PlacementKinds.EventNpc) && ExcelSheetHelper.TryGetRow<ENpcBase>(baseId, out _))
                    rows.Add((PlacementKinds.EventNpc, baseId));

                if (kinds.HasFlag(PlacementKinds.Aetheryte) && ExcelSheetHelper.TryGetRow<Aetheryte>(baseId, out _))
                    rows.Add((PlacementKinds.Aetheryte, baseId));
            }
        });

        return rows;
    }

    private static IEnumerable<(PlacementKinds Kind, uint BaseId)> ReadNamedRows(string name, NameMatch match, PlacementKinds kinds, Dalamud.Game.ClientLanguage? language)
    {
        var rows = new List<(PlacementKinds Kind, uint BaseId)>();

        SafeExecutor.ExecuteSafely(() =>
        {
            if (kinds.HasFlag(PlacementKinds.EventObject) && ExcelSheetHelper.GetSheet<EObjName>(language) is { } objectNames)
            {
                foreach (var row in objectNames)
                {
                    if (row.RowId != 0 && NameMatches(row.Singular.ExtractText(), name, match))
                        rows.Add((PlacementKinds.EventObject, row.RowId));
                }
            }

            if (kinds.HasFlag(PlacementKinds.EventNpc) && ExcelSheetHelper.GetSheet<ENpcResident>(language) is { } residents)
            {
                foreach (var row in residents)
                {
                    if (row.RowId != 0 && NameMatches(row.Singular.ExtractText(), name, match))
                        rows.Add((PlacementKinds.EventNpc, row.RowId));
                }
            }

            if (kinds.HasFlag(PlacementKinds.Aetheryte) && ExcelSheetHelper.GetSheet<Aetheryte>(language) is { } aetherytes)
            {
                foreach (var row in aetherytes)
                {
                    if (row.RowId != 0 && NameMatches(row.PlaceName.ValueNullable?.Name.ExtractText(), name, match))
                        rows.Add((PlacementKinds.Aetheryte, row.RowId));
                }
            }
        });

        return rows;
    }

    private static IEnumerable<(PlacementKinds Kind, uint BaseId)> ReadRowsRunning(Func<uint, bool> handlerMatches, PlacementKinds kinds)
    {
        var rows = new List<(PlacementKinds Kind, uint BaseId)>();

        SafeExecutor.ExecuteSafely(() =>
        {
            var arrays = ReadArrayHandlers();

            bool Runs(uint handlerId)
                => handlerId != 0
                    && (handlerMatches(handlerId)
                        || (arrays.TryGetValue(handlerId, out var listed) && listed.Any(handlerMatches)));

            if (kinds.HasFlag(PlacementKinds.EventObject) && ExcelSheetHelper.GetSheet<EObj>() is { } objects)
            {
                foreach (var row in objects)
                {
                    if (row.RowId != 0 && Runs(row.Data.RowId))
                        rows.Add((PlacementKinds.EventObject, row.RowId));
                }
            }

            if (kinds.HasFlag(PlacementKinds.EventNpc) && ExcelSheetHelper.GetSheet<ENpcBase>() is { } npcs)
            {
                foreach (var row in npcs)
                {
                    if (row.RowId == 0)
                        continue;

                    foreach (var data in row.ENpcData)
                    {
                        if (Runs(data.RowId))
                        {
                            rows.Add((PlacementKinds.EventNpc, row.RowId));
                            break;
                        }
                    }
                }
            }
        });

        return rows;
    }

    private static IReadOnlyDictionary<uint, IReadOnlyList<uint>> ReadArrayHandlers()
    {
        var arrays = new Dictionary<uint, IReadOnlyList<uint>>();

        if (ExcelSheetHelper.GetSheet<ArrayEventHandler>() is not { } sheet)
            return arrays;

        foreach (var row in sheet)
        {
            var listed = new List<uint>();

            foreach (var entry in row.Data)
            {
                if (entry.RowId != 0)
                    listed.Add(entry.RowId);
            }

            if (listed.Count > 0)
                arrays[row.RowId] = listed;
        }

        return arrays;
    }

    private static IReadOnlySet<uint> ReadTalkIds(string scriptPrefix)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var talkIds = new HashSet<uint>();
            var sheet = ExcelSheetHelper.GetSheet<CustomTalk>();
            if (sheet == null)
                return (IReadOnlySet<uint>)talkIds;

            foreach (var talk in sheet)
            {
                if (talk.Name.ExtractText().StartsWith(scriptPrefix, StringComparison.Ordinal))
                    talkIds.Add(talk.RowId);
            }

            return talkIds;
        }, new HashSet<uint>()) ?? new HashSet<uint>();
    }

    private static WorldObject DescribeRow(PlacementKinds kind, uint baseId, IReadOnlyList<WorldObjectPlacement> placements)
    {
        var fallback = new WorldObject(kind, baseId, string.Empty, string.Empty, string.Empty, [], null, null, null, placements, string.Empty);

        return SafeExecutor.ExecuteSafely(() => kind switch
        {
            PlacementKinds.EventObject => DescribeEventObject(baseId, placements),
            PlacementKinds.EventNpc => DescribeEventNpc(baseId, placements),
            PlacementKinds.Aetheryte => DescribeAetheryte(baseId, placements),
            _ => fallback,
        }, fallback) ?? fallback;
    }

    private static WorldObject DescribeEventObject(uint baseId, IReadOnlyList<WorldObjectPlacement> placements)
    {
        var name = string.Empty;
        var plural = string.Empty;

        if (ExcelSheetHelper.TryGetRow<EObjName>(baseId, out var nameRow) && nameRow is { } objectName)
        {
            name = objectName.Singular.ExtractText();
            plural = objectName.Plural.ExtractText();
        }

        if (!ExcelSheetHelper.TryGetRow<EObj>(baseId, out var row) || row is not { } eventObject)
            return new WorldObject(PlacementKinds.EventObject, baseId, name, plural, string.Empty, [], null, null, null, placements, string.Empty);

        var sgbPath = eventObject.SgbPath.RowId != 0 && ExcelSheetHelper.TryGetRow<ExportedSG>(eventObject.SgbPath.RowId, out var scene) && scene is { } exported
            ? exported.SgbPath.ExtractText()
            : string.Empty;

        var details = new EventObjectDetails(
            eventObject.PopType,
            eventObject.Invisibility,
            eventObject.Target,
            eventObject.EyeCollision,
            eventObject.DirectorControl,
            sgbPath);

        var handlers = ReadHandlers([eventObject.Data.RowId], baseId, WarpTriggerKind.EventObject);

        return new WorldObject(PlacementKinds.EventObject, baseId, name, plural, string.Empty, handlers, details, null, null, placements, string.Empty);
    }

    private static WorldObject DescribeEventNpc(uint baseId, IReadOnlyList<WorldObjectPlacement> placements)
    {
        var name = string.Empty;
        var plural = string.Empty;
        var title = string.Empty;
        byte residentMap = 0;

        if (ExcelSheetHelper.TryGetRow<ENpcResident>(baseId, out var residentRow) && residentRow is { } resident)
        {
            name = resident.Singular.ExtractText();
            plural = resident.Plural.ExtractText();
            title = resident.Title.ExtractText();
            residentMap = resident.Map;
        }

        if (!ExcelSheetHelper.TryGetRow<ENpcBase>(baseId, out var row) || row is not { } npc)
            return new WorldObject(PlacementKinds.EventNpc, baseId, name, plural, title, [], null, null, null, placements, string.Empty);

        var details = new EventNpcDetails(
            npc.Invisibility,
            npc.Important,
            npc.Scale,
            npc.ModelChara.RowId,
            npc.NpcEquip.RowId,
            npc.Race.RowId,
            npc.Tribe.RowId,
            npc.Gender,
            npc.Balloon.RowId,
            residentMap);

        var handlerIds = new List<uint>();
        foreach (var data in npc.ENpcData)
            handlerIds.Add(data.RowId);

        var handlers = ReadHandlers(handlerIds, baseId, WarpTriggerKind.EventNpc);

        return new WorldObject(PlacementKinds.EventNpc, baseId, name, plural, title, handlers, null, details, null, placements, string.Empty);
    }

    private static WorldObject DescribeAetheryte(uint baseId, IReadOnlyList<WorldObjectPlacement> placements)
    {
        if (!ExcelSheetHelper.TryGetRow<Aetheryte>(baseId, out var row) || row is not { } aetheryte)
            return new WorldObject(PlacementKinds.Aetheryte, baseId, string.Empty, string.Empty, string.Empty, [], null, null, null, placements, string.Empty);

        var details = new AetheryteDetails(
            aetheryte.IsAetheryte,
            aetheryte.Invisible,
            aetheryte.AethernetGroup,
            aetheryte.AethernetName.ValueNullable?.Name.ExtractText() ?? string.Empty,
            aetheryte.PlaceName.RowId,
            aetheryte.Territory.RowId,
            aetheryte.Map.RowId,
            aetheryte.RequiredQuest.RowId,
            aetheryte.Order);

        var name = aetheryte.PlaceName.ValueNullable?.Name.ExtractText() ?? string.Empty;

        return new WorldObject(PlacementKinds.Aetheryte, baseId, name, string.Empty, string.Empty, [], null, null, details, placements, string.Empty);
    }

    private static IReadOnlyList<WorldObjectHandler> ReadHandlers(IEnumerable<uint> handlerIds, uint baseId, WarpTriggerKind triggerKind)
    {
        var handlers = new List<WorldObjectHandler>();

        foreach (var handlerId in handlerIds)
        {
            if (handlerId == 0)
                continue;

            handlers.Add(ReadHandler(handlerId, 0, baseId, triggerKind));

            if ((EventHandlerContent)(handlerId >> HandlerContentShift) != EventHandlerContent.Array
                || !ExcelSheetHelper.TryGetRow<ArrayEventHandler>(handlerId, out var arrayRow) || arrayRow is not { } array)
            {
                continue;
            }

            foreach (var entry in array.Data)
            {
                if (entry.RowId != 0)
                    handlers.Add(ReadHandler(entry.RowId, handlerId, baseId, triggerKind));
            }
        }

        return handlers;
    }

    private static WorldObjectHandler ReadHandler(uint handlerId, uint parentHandlerId, uint baseId, WarpTriggerKind triggerKind)
    {
        var content = (EventHandlerContent)(handlerId >> HandlerContentShift);

        var scriptName = content == EventHandlerContent.CustomTalk
            && ExcelSheetHelper.TryGetRow<CustomTalk>(handlerId, out var talkRow) && talkRow is { } talk
                ? talk.Name.ExtractText()
                : string.Empty;

        var shop = ShopHelper.KindOf(handlerId) != null ? ShopHelper.ReadShop(handlerId) : null;
        var warp = content == EventHandlerContent.Warp ? WarpHelper.ReadDefinition(handlerId, baseId, triggerKind) : null;

        return new WorldObjectHandler(handlerId, content, parentHandlerId, scriptName, shop?.Name ?? string.Empty, shop?.Offers.Count ?? 0, warp);
    }

    private static WorldObjectPlacement ToPlacement(IndexedPlacement placement, WorldObjectIndex index, TerritoryCache territories)
    {
        var territory = territories.Read(placement.TerritoryId);
        var map = MapCoordinateHelper.PickMap(territory.Maps, territory.OwnMapId, placement.Position);
        var coordinate = map is { } chosen
            ? MapCoordinateHelper.WorldToMapCoordinate(placement.Position, chosen)
            : (0f, 0f);

        return new WorldObjectPlacement(
            placement.Kind,
            placement.BaseId,
            placement.TerritoryId,
            territory.Name,
            territory.PlaceNameId,
            map?.MapId ?? 0,
            new Vector2(coordinate.Item1, coordinate.Item2),
            placement.Position,
            placement.Yaw,
            placement.InstanceId,
            WorldObjectIndex.Files[placement.FileIndex],
            placement.AssetPathIndex >= 0 ? index.AssetPaths[placement.AssetPathIndex] : string.Empty,
            placement.FestivalId,
            placement.FestivalPhase,
            placement.LayerTerritories);
    }

    private static IReadOnlyDictionary<uint, uint>? ReadVariantAliases()
    {
        lock (CacheLock)
        {
            if (variantAliases != null)
                return variantAliases;
        }

        var aliases = TerritoryHelper.BuildAliases(TerritoryHelper.ReadReal());

        if (aliases.Count > 0)
        {
            lock (CacheLock)
                variantAliases = aliases;
        }

        return aliases;
    }

    private sealed class TerritoryCache
    {
        private readonly Dictionary<uint, (string Name, uint PlaceNameId, uint OwnMapId, IReadOnlyList<MapProjection> Maps)> territories = new();

        public (string Name, uint PlaceNameId, uint OwnMapId, IReadOnlyList<MapProjection> Maps) Read(uint territoryId)
        {
            if (territories.TryGetValue(territoryId, out var known))
                return known;

            var ownMapId = ExcelSheetHelper.TryGetRow<TerritoryType>(territoryId, out var row) && row is { } territory
                ? territory.Map.RowId
                : 0u;

            var read = (TerritoryHelper.Name(territoryId), TerritoryHelper.PlaceNameId(territoryId), ownMapId, MapCoordinateHelper.ReadMaps(territoryId));
            territories[territoryId] = read;
            return read;
        }
    }

    #endregion
}
