using FFXIVClientStructs.FFXIV.Client.Game;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>One world, and where it sits in the world tree.</summary>
/// <param name="RowId">The World row id.</param>
/// <param name="Name">The world's display name.</param>
/// <param name="InternalName">The world's internal name.</param>
/// <param name="DataCenterId">The WorldDCGroupType row id.</param>
/// <param name="DataCenterName">The data centre's name.</param>
/// <param name="RegionId">The physical region hosting it.</param>
/// <param name="IsPublic">Whether players can be on it. Most sheet rows are not.</param>
public sealed record WorldInfo(
    ushort RowId,
    string Name,
    string InternalName,
    uint DataCenterId,
    string DataCenterName,
    byte RegionId,
    bool IsPublic);

/// <summary>Worlds and data centres, where the character is against where they live, and running festivals.</summary>
public static unsafe class WorldHelper
{
    private static IReadOnlyDictionary<ushort, uint>? worldDataCenters;
    private static IReadOnlyList<WorldInfo>? cachedWorlds;

    #region The world sheet

    /// <summary>Reads one world.</summary>
    /// <param name="worldId">The World row id.</param>
    /// <returns>The world, or null when the id names none.</returns>
    public static WorldInfo? Read(ushort worldId)
    {
        if (worldId == 0)
            return null;

        return SafeExecutor.ExecuteSafely<WorldInfo?>(
            () => ExcelSheetHelper.TryGetRow<World>(worldId, out var row) && row.HasValue ? Describe(row.Value) : null,
            null);
    }

    /// <summary>Every world. Cached.</summary>
    /// <param name="publicOnly">Whether to skip the worlds players can never be on.</param>
    /// <returns>The worlds, in ascending row order.</returns>
    public static IReadOnlyList<WorldInfo> ReadAll(bool publicOnly = true)
    {
        cachedWorlds ??= SafeExecutor.ExecuteSafely(() =>
        {
            var found = new List<WorldInfo>();
            var sheet = ExcelSheetHelper.GetSheet<World>();
            if (sheet == null)
                return found;

            foreach (var row in sheet)
            {
                if (row.RowId != 0)
                    found.Add(Describe(row));
            }

            return found;
        }, []) ?? [];

        if (!publicOnly)
            return cachedWorlds;

        var visible = new List<WorldInfo>();

        foreach (var world in cachedWorlds)
        {
            if (world.IsPublic)
                visible.Add(world);
        }

        return visible;
    }

    /// <summary>A world's display name.</summary>
    /// <param name="worldId">The World row id.</param>
    /// <returns>The name, or an empty string.</returns>
    public static string Name(ushort worldId) => Read(worldId)?.Name ?? string.Empty;

    /// <summary>Finds a world by name, matching the display and internal names alike.</summary>
    /// <param name="name">The name to match, case insensitively.</param>
    /// <returns>The world, or null when nothing matches.</returns>
    public static WorldInfo? Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var wanted = name.Trim();

        foreach (var world in ReadAll(false))
        {
            if (string.Equals(world.Name, wanted, StringComparison.OrdinalIgnoreCase)
                || string.Equals(world.InternalName, wanted, StringComparison.OrdinalIgnoreCase))
                return world;
        }

        return null;
    }

    /// <summary>The data centres holding public worlds, for picking one by name.</summary>
    /// <returns>Each data centre's WorldDCGroupType row id and name, in ascending row order.</returns>
    public static IReadOnlyList<(uint DataCenterId, string Name)> ReadDataCentreList()
    {
        var centres = new List<(uint DataCenterId, string Name)>();
        var seen = new HashSet<uint>();

        foreach (var world in ReadAll())
        {
            if (world.DataCenterId != 0 && seen.Add(world.DataCenterId))
                centres.Add((world.DataCenterId, world.DataCenterName));
        }

        centres.Sort(static (first, second) => first.DataCenterId.CompareTo(second.DataCenterId));

        return centres;
    }

    /// <summary>The public worlds on a data centre, whose id comes from <see cref="ReadDataCentreList"/>.</summary>
    /// <param name="dataCenterId">The WorldDCGroupType row id.</param>
    /// <returns>The worlds, in ascending row order.</returns>
    public static IReadOnlyList<WorldInfo> WorldsOn(uint dataCenterId)
    {
        var found = new List<WorldInfo>();

        if (dataCenterId == 0)
            return found;

        foreach (var world in ReadAll())
        {
            if (world.DataCenterId == dataCenterId)
                found.Add(world);
        }

        return found;
    }

    /// <summary>A data centre's name.</summary>
    /// <param name="dataCenterId">The WorldDCGroupType row id.</param>
    /// <returns>The name, or an empty string.</returns>
    public static string DataCenterName(uint dataCenterId)
    {
        if (dataCenterId == 0)
            return string.Empty;

        return SafeExecutor.ExecuteSafely(
            () => ExcelSheetHelper.TryGetRow<WorldDCGroupType>(dataCenterId, out var row) && row.HasValue
                ? row.Value.Name.ExtractText()
                : string.Empty,
            string.Empty) ?? string.Empty;
    }

    /// <summary>The data centre each world belongs to. Cached.</summary>
    /// <returns>The WorldDCGroupType row id for each World row id.</returns>
    public static IReadOnlyDictionary<ushort, uint> ReadDataCenters()
    {
        if (worldDataCenters != null)
            return worldDataCenters;

        var map = SafeExecutor.ExecuteSafely(() =>
        {
            var found = new Dictionary<ushort, uint>();
            var sheet = ExcelSheetHelper.GetSheet<World>();
            if (sheet == null)
                return found;

            foreach (var world in sheet)
            {
                if (world.RowId != 0)
                    found[(ushort)world.RowId] = world.DataCenter.RowId;
            }

            return found;
        }, []) ?? [];

        return worldDataCenters = map;
    }

    /// <summary>Whether two worlds share a data centre.</summary>
    /// <param name="first">The first World row id.</param>
    /// <param name="second">The second World row id.</param>
    /// <returns>True when both worlds are on the same data centre.</returns>
    public static bool ShareDataCenter(ushort first, ushort second)
    {
        var centers = ReadDataCenters();

        return centers.TryGetValue(first, out var a) && centers.TryGetValue(second, out var b) && a == b;
    }

    #endregion

    #region Where the character is

    /// <summary>The world the character is standing on right now, which is not their home world while visiting.</summary>
    /// <returns>The World row id, or zero when there is no loaded character.</returns>
    public static ushort CurrentId()
        => CharacterHelper.IsStateReady
            ? SafeExecutor.ExecuteSafely(() => (ushort)NoireService.PlayerState.CurrentWorld.RowId)
            : (ushort)0;

    /// <summary>The world the character belongs to.</summary>
    /// <returns>The World row id, or zero when there is no loaded character.</returns>
    public static ushort HomeId()
        => CharacterHelper.IsStateReady
            ? SafeExecutor.ExecuteSafely(() => (ushort)NoireService.PlayerState.HomeWorld.RowId)
            : (ushort)0;

    /// <summary>The world the character is standing on right now.</summary>
    /// <returns>The world, or null when there is no loaded character.</returns>
    public static WorldInfo? Current() => Read(CurrentId());

    /// <summary>The world the character belongs to.</summary>
    /// <returns>The world, or null when there is no loaded character.</returns>
    public static WorldInfo? Home() => Read(HomeId());

    /// <summary>Whether the character is standing on a world other than their own.</summary>
    /// <returns>True when visiting.</returns>
    public static bool IsVisiting()
    {
        var current = CurrentId();
        var home = HomeId();

        return current != 0 && home != 0 && current != home;
    }

    /// <summary>
    /// Whether the character is visiting a world on another data centre; reaching it takes a different route than an
    /// ordinary world visit.
    /// </summary>
    /// <returns>True when the character is away from their home data centre.</returns>
    public static bool IsTravelling()
    {
        var current = CurrentId();
        var home = HomeId();

        return current != 0 && home != 0 && current != home && !ShareDataCenter(current, home);
    }

    #endregion

    /// <summary>
    /// The seasonal events running right now, with the phase each is in.<br/>
    /// Every event's placements sit in a level-file layer tagged with that event's festival, one layer per event per
    /// year. Ignoring this set shows every past year's event placement as permanently present.
    /// </summary>
    /// <returns>The phase of each running festival, keyed by festival id.</returns>
    public static IReadOnlyDictionary<ushort, ushort> ReadActiveFestivals()
    {
        var active = new Dictionary<ushort, ushort>();

        SafeExecutor.ExecuteSafely(() =>
        {
            var gameMain = GameMain.Instance();
            if (gameMain == null)
                return;

            foreach (var festival in gameMain->ActiveFestivals)
            {
                if (festival.Id != 0)
                    active[(ushort)festival.Id] = (ushort)festival.Phase;
            }
        });

        return active;
    }

    private static WorldInfo Describe(World row) => new(
        (ushort)row.RowId,
        row.Name.ExtractText(),
        row.InternalName.ExtractText(),
        row.DataCenter.RowId,
        row.DataCenter.ValueNullable?.Name.ExtractText() ?? string.Empty,
        row.Region,
        row.IsPublic);
}
