using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// Reads what the game's WKS sheets say about Cosmic Exploration: which planets exist, the intra-planet aethernet,
/// the warps its objects run, and the travel services its NPCs offer. Every read is guarded; a missing sheet
/// yields empty.
/// </summary>
public static class CosmicExplorationHelper
{
    // A CustomTalk's name is its script identifier and is never localised, so it is safe to match on. The names
    // carry a numeric suffix, hence the prefix match.
    private const string EntranceTalkName = "CtsWksEntrance";
    private const string ExitTalkName = "CtsWksExit";

    /// <summary>Reads the planets, in release order, from the WKSTerritoryInfo rows that name a territory.</summary>
    /// <returns>The planets, empty when the sheet is missing or names none.</returns>
    public static IReadOnlyList<CosmicPlanet> ReadPlanets()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var planets = new List<CosmicPlanet>();
            var sheet = ExcelSheetHelper.GetSheet<WKSTerritoryInfo>();
            if (sheet == null)
                return (IReadOnlyList<CosmicPlanet>)planets;

            foreach (var info in sheet)
            {
                if (info.TerritoryType.RowId != 0)
                    planets.Add(new CosmicPlanet(info.TerritoryType.RowId, planets.Count));
            }

            return planets;
        }, []) ?? [];
    }

    /// <summary>
    /// Reads each WKSAetheryte with its name and the EObj rows placed for it, resolved through its
    /// WKSAetheryteObjectGroup. The result carries no territory, since the planet a shard serves follows from
    /// where its object stands.
    /// </summary>
    /// <returns>The shards, empty when the sheets are missing.</returns>
    public static IReadOnlyList<CosmicShardInfo> ReadAethernetShards()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var shards = new List<CosmicShardInfo>();
            var aetherytes = ExcelSheetHelper.GetSheet<WKSAetheryte>();
            var groups = ExcelSheetHelper.GetSubrowSheet<WKSAetheryteObjectGroup>();
            if (aetherytes == null || groups == null)
                return (IReadOnlyList<CosmicShardInfo>)shards;

            foreach (var aetheryte in aetherytes)
            {
                if (aetheryte.RowId == 0 || aetheryte.Name.RowId == 0)
                    continue;

                var objects = new List<uint>();
                if (groups.HasRow(aetheryte.ObjectGroup.RowId))
                {
                    foreach (var member in groups[aetheryte.ObjectGroup.RowId])
                    {
                        // The group's first column is the placed EObj row, the remaining columns display data.
                        if (member.Unknown0 != 0)
                            objects.Add(member.Unknown0);
                    }
                }

                if (objects.Count > 0)
                    shards.Add(new CosmicShardInfo(aetheryte.RowId, aetheryte.Name.RowId, objects));
            }

            return shards;
        }, []) ?? [];
    }

    /// <summary>
    /// Reads the warps the WKSWarp sheet binds to placed objects, keyed by EObj row id. These objects run a
    /// CustomTalk rather than the warp itself, so the ordinary object-warp scan cannot see them.
    /// </summary>
    /// <returns>The Warp row each bound EObj triggers, empty when the sheet is missing.</returns>
    public static IReadOnlyDictionary<uint, uint> ScanWarpObjects()
    {
        var empty = (IReadOnlyDictionary<uint, uint>)new Dictionary<uint, uint>();

        return SafeExecutor.ExecuteSafely(() =>
        {
            var sheet = ExcelSheetHelper.GetSheet<WKSWarp>();
            if (sheet == null)
                return empty;

            var result = new Dictionary<uint, uint>();
            foreach (var row in sheet)
            {
                // The first column is the EObj row, the second the Warp row it triggers.
                if (row.Unknown0 != 0 && row.Unknown1 != 0)
                    result[row.Unknown0] = row.Unknown1;
            }

            return (IReadOnlyDictionary<uint, uint>)result;
        }, empty) ?? empty;
    }

    /// <summary>Finds the cosmoport travel services by their script names, for the NPC scan that locates who runs them.</summary>
    /// <returns>The talk ids, empty sets when the sheet is missing.</returns>
    public static CosmicTravelTalks ReadTravelTalks()
    {
        var empty = new CosmicTravelTalks(new HashSet<uint>(), new HashSet<uint>());

        return SafeExecutor.ExecuteSafely(() =>
        {
            var sheet = ExcelSheetHelper.GetSheet<CustomTalk>();
            if (sheet == null)
                return empty;

            var entrances = new HashSet<uint>();
            var exits = new HashSet<uint>();
            foreach (var talk in sheet)
            {
                var name = talk.Name.ExtractText();
                if (name.StartsWith(EntranceTalkName, StringComparison.Ordinal))
                    entrances.Add(talk.RowId);
                else if (name.StartsWith(ExitTalkName, StringComparison.Ordinal))
                    exits.Add(talk.RowId);
            }

            return new CosmicTravelTalks(entrances, exits);
        }, empty);
    }
}
