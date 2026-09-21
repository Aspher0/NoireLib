using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading;

namespace NoireLib.Helpers;

internal readonly record struct IndexedPlacement(
    PlacementKinds Kind,
    uint BaseId,
    uint TerritoryId,
    uint InstanceId,
    Vector3 Position,
    float Yaw,
    byte FileIndex,
    int AssetPathIndex,
    ushort FestivalId,
    ushort FestivalPhase,
    uint[] LayerTerritories);

internal sealed class WorldObjectIndexFile
{
    public byte[] Data { get; set; } = [];

    public string[] AssetPaths { get; set; } = [];
}

internal sealed class WorldObjectIndex
{
    // Bump whenever the encoding or the content changes.
    internal const int SchemaVersion = 1;

    // Some cities place interactables in bg.lgb, Tuliyollal's market board among them.
    internal static readonly string[] Files =
    [
        LevelFileHelper.Files.PlanMap,
        LevelFileHelper.Files.PlanEvent,
        LevelFileHelper.Files.Planner,
        LevelFileHelper.Files.PlanLive,
        LevelFileHelper.Files.Background,
    ];

    private static readonly LevelObjectFilter IndexedKinds = new()
    {
        Kinds = new HashSet<LevelObjectKind>
        {
            LevelObjectKind.EventObject,
            LevelObjectKind.EventNpc,
            LevelObjectKind.Aetheryte,
            LevelObjectKind.SharedGroup,
        },
    };

    private readonly Dictionary<(PlacementKinds Kind, uint BaseId), List<int>> byRow = new();

    internal WorldObjectIndex(IReadOnlyList<IndexedPlacement> placements, IReadOnlyList<string> assetPaths)
    {
        Placements = placements;
        AssetPaths = assetPaths;

        for (var index = 0; index < placements.Count; index++)
        {
            var placement = placements[index];

            if (placement.Kind == PlacementKinds.SharedGroup)
                continue;

            if (!byRow.TryGetValue((placement.Kind, placement.BaseId), out var positions))
                byRow[(placement.Kind, placement.BaseId)] = positions = [];

            positions.Add(index);
        }
    }

    internal IReadOnlyList<IndexedPlacement> Placements { get; }

    internal IReadOnlyList<string> AssetPaths { get; }

    internal static WorldObjectIndex Build(IEnumerable<uint> territoryIds, CancellationToken cancellationToken)
    {
        var placements = new List<IndexedPlacement>();
        var assetPaths = new List<string>();
        var assetPathIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var readLevelPaths = new HashSet<string>(StringComparer.Ordinal);
        var processed = 0;

        foreach (var territoryId in territoryIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var levelPath = TerritoryHelper.Bg(territoryId);
            if (levelPath.Length == 0 || !readLevelPaths.Add(levelPath))
                continue;

            for (var fileIndex = 0; fileIndex < Files.Length; fileIndex++)
            {
                foreach (var levelObject in LevelFileHelper.ReadObjects(levelPath, Files[fileIndex], IndexedKinds))
                {
                    var kind = WorldObjectHelper.KindOf(levelObject.Kind);
                    if (kind == PlacementKinds.None || !levelObject.BelongsTo(territoryId))
                        continue;

                    var assetPathIndex = -1;
                    if (kind == PlacementKinds.SharedGroup)
                    {
                        if (!assetPathIndexes.TryGetValue(levelObject.AssetPath, out assetPathIndex))
                        {
                            assetPathIndex = assetPaths.Count;
                            assetPathIndexes[levelObject.AssetPath] = assetPathIndex;
                            assetPaths.Add(levelObject.AssetPath);
                        }
                    }

                    placements.Add(new IndexedPlacement(
                        kind,
                        levelObject.BaseId,
                        territoryId,
                        levelObject.InstanceId,
                        levelObject.Position,
                        levelObject.Yaw,
                        (byte)fileIndex,
                        assetPathIndex,
                        levelObject.FestivalId,
                        levelObject.FestivalPhase,
                        levelObject.LayerTerritories is { Count: > 0 } layers ? [.. layers] : []));
                }
            }

            // Lets the game's own file reads interleave.
            if ((++processed & 7) == 0)
                Thread.Sleep(1);
        }

        return new WorldObjectIndex(placements, assetPaths);
    }

    internal IEnumerable<IndexedPlacement> PlacementsOf(PlacementKinds kind, uint baseId, IReadOnlySet<uint>? territoryIds)
    {
        if (!byRow.TryGetValue((kind, baseId), out var positions))
            yield break;

        foreach (var position in positions)
        {
            var placement = Placements[position];

            if (territoryIds == null || territoryIds.Contains(placement.TerritoryId))
                yield return placement;
        }
    }

    internal IEnumerable<IndexedPlacement> SharedGroupsMatching(string assetPathFragment, IReadOnlySet<uint>? territoryIds)
    {
        var matchingPaths = new HashSet<int>();

        for (var index = 0; index < AssetPaths.Count; index++)
        {
            if (AssetPaths[index].Contains(assetPathFragment, StringComparison.OrdinalIgnoreCase))
                matchingPaths.Add(index);
        }

        if (matchingPaths.Count == 0)
            yield break;

        foreach (var placement in Placements)
        {
            if (placement.Kind == PlacementKinds.SharedGroup
                && matchingPaths.Contains(placement.AssetPathIndex)
                && (territoryIds == null || territoryIds.Contains(placement.TerritoryId)))
            {
                yield return placement;
            }
        }
    }

    internal WorldObjectIndexFile ToFile()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(Placements.Count);

            foreach (var placement in Placements)
            {
                writer.Write((byte)placement.Kind);
                writer.Write(placement.BaseId);
                writer.Write(placement.TerritoryId);
                writer.Write(placement.InstanceId);
                writer.Write(placement.Position.X);
                writer.Write(placement.Position.Y);
                writer.Write(placement.Position.Z);
                writer.Write(placement.Yaw);
                writer.Write(placement.FileIndex);
                writer.Write(placement.AssetPathIndex);
                writer.Write(placement.FestivalId);
                writer.Write(placement.FestivalPhase);
                writer.Write((byte)placement.LayerTerritories.Length);

                foreach (var territory in placement.LayerTerritories)
                    writer.Write(territory);
            }
        }

        return new WorldObjectIndexFile { Data = stream.ToArray(), AssetPaths = [.. AssetPaths] };
    }

    internal static WorldObjectIndex FromFile(WorldObjectIndexFile file)
    {
        using var reader = new BinaryReader(new MemoryStream(file.Data));
        var count = reader.ReadInt32();
        var placements = new List<IndexedPlacement>(count);

        for (var index = 0; index < count; index++)
        {
            var kind = (PlacementKinds)reader.ReadByte();
            var baseId = reader.ReadUInt32();
            var territoryId = reader.ReadUInt32();
            var instanceId = reader.ReadUInt32();
            var position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            var yaw = reader.ReadSingle();
            var fileIndex = reader.ReadByte();
            var assetPathIndex = reader.ReadInt32();
            var festivalId = reader.ReadUInt16();
            var festivalPhase = reader.ReadUInt16();
            var layers = new uint[reader.ReadByte()];

            for (var layer = 0; layer < layers.Length; layer++)
                layers[layer] = reader.ReadUInt32();

            placements.Add(new IndexedPlacement(kind, baseId, territoryId, instanceId, position, yaw, fileIndex, assetPathIndex, festivalId, festivalPhase, layers));
        }

        return new WorldObjectIndex(placements, file.AssetPaths);
    }
}
