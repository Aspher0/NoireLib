using FFXIVClientStructs.FFXIV.Client.LayoutEngine.Layer;
using Lumina.Data.Parsing.Layer;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace NoireLib.Helpers;

/// <summary>
/// Reads .lgb and .sgb files into layers and entries, expanding nested shared groups with composed transforms.
/// Fail-soft: a bad file yields what was read and never throws.
/// </summary>
public static class LayerGroupHelper
{
    /// <summary>How deep <see cref="Flatten(string, Func{LayerGroupLayer, bool}, int)"/> follows shared groups nested inside shared groups by default.</summary>
    public const int DefaultMaxDepth = 8;

    // { int Id. Int NameOffset. Int LayersOffset. Int LayerCount; }
    internal const int LayerGroupHeaderSize = 16;

    // Also the value its instance-table offset always holds.
    internal const int LayerHeaderSize = 52;

    internal const int InstanceBodyOffset = 0x30;

    internal const int ChunkOffset = 12;

    internal const int SceneHeaderOffset = ChunkOffset + 8;

    private static readonly uint[] NoLayerSets = [];

    // ClientStructs' WaterRangeLayoutInstance.
    /// <summary>The layer entry type of a water range, which Lumina's <see cref="LayerEntryType"/> does not name.</summary>
    public const LayerEntryType WaterRangeEntryType = (LayerEntryType)86;

    /// <summary>Reads a level or shared group file, told apart by its magic.</summary>
    /// <param name="file">The whole file.</param>
    /// <returns>The layers in file order, or empty when the bytes are neither container.</returns>
    public static IReadOnlyList<LayerGroupLayer> Read(ReadOnlySpan<byte> file)
    {
        var level = ReadLevel(file);
        return level.Count > 0 ? level : ReadSharedGroup(file);
    }

    /// <summary>Reads a level or shared group file out of the game archives.</summary>
    /// <param name="gamePath">The archive path of the <c>.lgb</c> or <c>.sgb</c>.</param>
    /// <returns>The layers in file order, or empty when the file is missing or unreadable.</returns>
    public static IReadOnlyList<LayerGroupLayer> Read(string gamePath)
    {
        if (string.IsNullOrWhiteSpace(gamePath))
            return [];

        return SafeExecutor.ExecuteSafely(() =>
        {
            var data = NoireService.DataManager.GetFile(gamePath)?.Data;
            return data == null ? (IReadOnlyList<LayerGroupLayer>)[] : Read(data);
        }, []) ?? [];
    }

    /// <summary>Reads a file and every shared group it places, recursively, into one list with composed world transforms.</summary>
    /// <param name="gamePath">The archive path of the <c>.lgb</c> or <c>.sgb</c>.</param>
    /// <param name="layerFilter">Which of the file's own layers to read. Null for all. Nested groups are always read whole.</param>
    /// <param name="maxDepth">How many levels of nested groups to follow.</param>
    /// <returns>Every entry reached, or empty when the file is missing or unreadable.</returns>
    public static IReadOnlyList<LayerGroupEntry> Flatten(
        string gamePath,
        Func<LayerGroupLayer, bool>? layerFilter = null,
        int maxDepth = DefaultMaxDepth)
        => Flatten(gamePath, Read, layerFilter, maxDepth);

    internal static IReadOnlyList<LayerGroupEntry> Flatten(
        string gamePath,
        Func<string, IReadOnlyList<LayerGroupLayer>> read,
        Func<LayerGroupLayer, bool>? layerFilter,
        int maxDepth)
    {
        var into = new List<LayerGroupEntry>();
        var parsed = new Dictionary<string, IReadOnlyList<LayerGroupLayer>>(StringComparer.Ordinal);
        var ancestors = new HashSet<string>(StringComparer.Ordinal) { gamePath };

        foreach (var layer in read(gamePath))
        {
            if (layerFilter != null && !layerFilter(layer))
                continue;

            foreach (var entry in layer.Entries)
                Add(entry, Matrix4x4.Identity, 0, maxDepth, into, read, parsed, ancestors);
        }

        return into;
    }

    /// <summary>Builds a placement matrix: scale, then rotation about X, Y and Z in that order, then translation.</summary>
    /// <param name="translation">The stored translation.</param>
    /// <param name="rotation">The stored Euler angles, in radians.</param>
    /// <param name="scale">The stored scale.</param>
    /// <returns>The matrix, in row-vector convention.</returns>
    public static Matrix4x4 Compose(Vector3 translation, Vector3 rotation, Vector3 scale)
    {
        var rotate = Matrix4x4.CreateRotationX(rotation.X)
                     * Matrix4x4.CreateRotationY(rotation.Y)
                     * Matrix4x4.CreateRotationZ(rotation.Z);
        return Matrix4x4.CreateScale(scale) * rotate * Matrix4x4.CreateTranslation(translation);
    }

    // Seventeen stub files carry the file magic with no chunk behind it. The chunk magic is checked too.
    internal static IReadOnlyList<LayerGroupLayer> ReadLevel(ReadOnlySpan<byte> file)
    {
        if (file.Length < ChunkOffset + 8
            || !file[..4].SequenceEqual("LGB1"u8)
            || !file.Slice(ChunkOffset, 4).SequenceEqual("LGP1"u8))
            return [];

        return ReadLayerGroup(file, ChunkOffset + 8);
    }

    // Its layer group sits wherever the scene header's first word points.
    internal static IReadOnlyList<LayerGroupLayer> ReadSharedGroup(ReadOnlySpan<byte> file)
    {
        if (file.Length < SceneHeaderOffset + 4
            || !file[..4].SequenceEqual("SGB1"u8)
            || !file.Slice(ChunkOffset, 4).SequenceEqual("SCN1"u8))
            return [];

        return ReadLayerGroup(file, SceneHeaderOffset + BitConverter.ToInt32(file[SceneHeaderOffset..]));
    }

    private static void Add(
        LayerGroupEntry entry,
        in Matrix4x4 parent,
        int depth,
        int maxDepth,
        List<LayerGroupEntry> into,
        Func<string, IReadOnlyList<LayerGroupLayer>> read,
        Dictionary<string, IReadOnlyList<LayerGroupLayer>> parsed,
        HashSet<string> ancestors)
    {
        var placed = depth == 0 ? entry : entry with { World = entry.World * parent };
        into.Add(placed);

        if (entry.Type != LayerEntryType.SharedGroup || entry.AssetPath.Length == 0 || depth >= maxDepth)
            return;

        if (!ancestors.Add(entry.AssetPath))
            return;

        if (!parsed.TryGetValue(entry.AssetPath, out var layers))
        {
            layers = read(entry.AssetPath);
            parsed[entry.AssetPath] = layers;
        }

        var world = placed.World;
        foreach (var layer in layers)
        {
            foreach (var child in layer.Entries)
                Add(child, world, depth + 1, maxDepth, into, read, parsed, ancestors);
        }

        ancestors.Remove(entry.AssetPath);
    }

    private static List<LayerGroupLayer> ReadLayerGroup(ReadOnlySpan<byte> file, int group)
    {
        var layers = new List<LayerGroupLayer>();
        if (group < 0 || group + LayerGroupHeaderSize > file.Length)
            return layers;

        var layersOffset = BitConverter.ToInt32(file[(group + 8)..]);
        var layerCount = BitConverter.ToInt32(file[(group + 12)..]);
        var layersBase = group + layersOffset;

        if (layersOffset <= 0 || layersBase < 0 || layerCount <= 0 || layerCount > file.Length / 4)
            return layers;
        if (layersBase + layerCount * 4 > file.Length)
            return layers;

        for (var i = 0; i < layerCount; i++)
        {
            var layerStart = layersBase + BitConverter.ToInt32(file[(layersBase + i * 4)..]);
            if (layerStart < 0 || layerStart + LayerHeaderSize > file.Length)
                continue;

            layers.Add(ReadLayer(file, layerStart));
        }

        return layers;
    }

    private static LayerGroupLayer ReadLayer(ReadOnlySpan<byte> file, int layer)
    {
        var instanceOffset = BitConverter.ToInt32(file[(layer + 8)..]);
        var instanceCount = BitConverter.ToInt32(file[(layer + 12)..]);
        var layerSetOffset = BitConverter.ToInt32(file[(layer + 20)..]);

        var reference = LayerSetReferencedType.All;
        var setIds = NoLayerSets;
        if (layerSetOffset > 0 && layer + layerSetOffset + 12 <= file.Length)
        {
            var list = layer + layerSetOffset;
            var idsBase = list + BitConverter.ToInt32(file[(list + 4)..]);
            var idCount = BitConverter.ToInt32(file[(list + 8)..]);
            if (idCount > 0 && idCount < 4096 && idsBase > 0 && idsBase + idCount * 4 <= file.Length)
            {
                reference = (LayerSetReferencedType)BitConverter.ToInt32(file[list..]);
                var ids = new uint[idCount];
                for (var k = 0; k < idCount; k++)
                    ids[k] = BitConverter.ToUInt32(file[(idsBase + k * 4)..]);
                setIds = ids;
            }
        }

        var entries = new List<LayerGroupEntry>();
        var table = layer + instanceOffset;

        // Done wide. A corrupt count could wrap the multiplication past the bounds check.
        if (instanceCount > 0 && instanceCount <= file.Length / 4 && table > 0
            && table + (long)instanceCount * 4 <= file.Length)
        {
            for (var j = 0; j < instanceCount; j++)
            {
                var instance = table + BitConverter.ToInt32(file[(table + j * 4)..]);
                if (instance >= 0 && instance + InstanceBodyOffset <= file.Length
                    && ReadEntry(file, instance) is { } entry)
                    entries.Add(entry);
            }
        }

        return new LayerGroupLayer
        {
            Id = BitConverter.ToInt32(file[layer..]),
            Name = ReadString(file, layer + BitConverter.ToInt32(file[(layer + 4)..])),
            Reference = reference,
            LayerSetIds = setIds,
            FestivalId = BitConverter.ToUInt16(file[(layer + 24)..]),
            FestivalPhase = BitConverter.ToUInt16(file[(layer + 26)..]),
            Entries = entries,
        };
    }

    private static LayerGroupEntry? ReadEntry(ReadOnlySpan<byte> file, int instance)
    {
        var type = (LayerEntryType)BitConverter.ToInt32(file[instance..]);
        var required = type switch
        {
            LayerEntryType.BG => 0x49,
            LayerEntryType.SharedGroup => 0x38,
            LayerEntryType.CollisionBox => 0x54,
            LayerEntryType.ExitRange => 0x50,
            LayerEntryType.Aetheryte or LayerEntryType.EventNPC or LayerEntryType.EventObject => 0x34,
            LayerEntryType.PopRange => 0x3C,
            WaterRangeEntryType => 0x40,
            LayerEntryType.MapRange => 0x6A,
            _ => InstanceBodyOffset,
        };
        if (instance + required > file.Length)
            return null;

        var assetPath = string.Empty;
        var collisionPath = string.Empty;
        var collisionType = ModelCollisionType.None;
        uint attribute = 0, attributeMask = 0, baseId = 0, destInstance = 0, returnInstance = 0;
        var visible = false;
        TriggerBoxShape shape = 0;
        DoorState doorState = 0;
        var notCreateNavimeshDoor = false;
        ExitRangeType exitType = 0;
        short priority = 0;
        uint waterFlags = 0;
        bool flyingDisabled = false, mountsDisabled = false, lalafellOnly = false;
        ushort destTerritory = 0;
        IReadOnlyList<Vector3> spawnOffsets = [];

        switch (type)
        {
            case LayerEntryType.BG:
                assetPath = ReadStringAt(file, instance, 0x30);
                collisionPath = ReadStringAt(file, instance, 0x34);
                collisionType = (ModelCollisionType)BitConverter.ToInt32(file[(instance + 0x38)..]);
                attributeMask = BitConverter.ToUInt32(file[(instance + 0x3C)..]);
                attribute = BitConverter.ToUInt32(file[(instance + 0x40)..]);
                visible = file[instance + 0x48] != 0;
                break;

            case LayerEntryType.SharedGroup:
                assetPath = ReadStringAt(file, instance, 0x30);
                doorState = ToDoorState(BitConverter.ToInt32(file[(instance + 0x34)..]));

                // The last byte of the body. The word before it is a move-path offset.
                notCreateNavimeshDoor = instance + 0x50 < file.Length && file[instance + 0x50] != 0;
                break;

            case LayerEntryType.CollisionBox:
                shape = (TriggerBoxShape)BitConverter.ToInt32(file[(instance + 0x30)..]);
                attributeMask = BitConverter.ToUInt32(file[(instance + 0x3C)..]);
                attribute = BitConverter.ToUInt32(file[(instance + 0x40)..]);

                // Mesh shape only, eight bytes past where a general-purpose parse looks for it.
                if (shape == TriggerBoxShape.TriggerBoxShapeMesh)
                    assetPath = ReadStringAt(file, instance, 0x50);
                break;

            case LayerEntryType.ExitRange:
                shape = (TriggerBoxShape)BitConverter.ToInt32(file[(instance + 0x30)..]);
                exitType = (ExitRangeType)BitConverter.ToInt32(file[(instance + 0x3C)..]);
                destTerritory = BitConverter.ToUInt16(file[(instance + 0x42)..]);
                destInstance = BitConverter.ToUInt32(file[(instance + 0x48)..]);
                returnInstance = BitConverter.ToUInt32(file[(instance + 0x4C)..]);
                break;

            case LayerEntryType.Aetheryte or LayerEntryType.EventNPC or LayerEntryType.EventObject:
                baseId = BitConverter.ToUInt32(file[(instance + 0x30)..]);
                break;

            case LayerEntryType.PopRange:
                spawnOffsets = ReadSpawnOffsets(file, instance + 0x34);
                break;

            case WaterRangeEntryType:
                shape = (TriggerBoxShape)BitConverter.ToInt32(file[(instance + 0x30)..]);
                priority = BitConverter.ToInt16(file[(instance + 0x34)..]);
                waterFlags = BitConverter.ToUInt32(file[(instance + 0x3C)..]);
                break;

            // ClientStructs' MapRangeLayoutInstance flag bytes; Lumina leaves 0x67 unnamed.
            case LayerEntryType.MapRange:
                shape = (TriggerBoxShape)BitConverter.ToInt32(file[(instance + 0x30)..]);
                priority = BitConverter.ToInt16(file[(instance + 0x34)..]);
                flyingDisabled = file[instance + 0x67] != 0;
                mountsDisabled = file[instance + 0x68] != 0;
                lalafellOnly = file[instance + 0x69] != 0;
                break;
        }

        var translation = ReadVector(file, instance + 0x0C);
        var rotation = ReadVector(file, instance + 0x18);
        var scale = ReadVector(file, instance + 0x24);

        return new LayerGroupEntry
        {
            Type = type,
            InstanceId = BitConverter.ToUInt32(file[(instance + 4)..]),
            Name = ReadStringAt(file, instance, 0x08),
            Translation = translation,
            Rotation = rotation,
            Scale = scale,
            World = Compose(translation, rotation, scale),
            AssetPath = assetPath,
            CollisionPath = collisionPath,
            CollisionType = collisionType,
            Attribute = attribute,
            AttributeMask = attributeMask,
            IsVisible = visible,
            Shape = shape,
            InitialDoorState = doorState,
            NotCreateNavimeshDoor = notCreateNavimeshDoor,
            BaseId = baseId,
            ExitType = exitType,
            DestTerritoryId = destTerritory,
            DestInstanceId = destInstance,
            ReturnInstanceId = returnInstance,
            SpawnOffsets = spawnOffsets,
            Priority = priority,
            WaterRangeFlags = waterFlags,
            FlyingDisabled = flyingDisabled,
            MountsAndOrnamentsDisabled = mountsDisabled,
            LalafellOnly = lalafellOnly,
        };
    }

    // The offset to the list counts from the field holding it, and the count follows it. Every PopRange in the game holds 20.
    private static IReadOnlyList<Vector3> ReadSpawnOffsets(ReadOnlySpan<byte> file, int field)
    {
        var start = field + BitConverter.ToInt32(file[field..]);
        var count = BitConverter.ToInt32(file[(field + 4)..]);
        if (count <= 0 || count > MaxSpawnOffsets || start <= field || start + (long)count * 12 > file.Length)
            return [];

        var offsets = new Vector3[count];
        for (var i = 0; i < count; i++)
            offsets[i] = ReadVector(file, start + i * 12);

        return offsets;
    }

    // Well past the 20 every PopRange holds: a damaged count cannot ask for a large allocation.
    private const int MaxSpawnOffsets = 256;

    // Only 1, 2 and 3 appear in the files.
    private static DoorState ToDoorState(int state)
        => state is >= 1 and <= 3 ? (DoorState)state : 0;

    private static Vector3 ReadVector(ReadOnlySpan<byte> file, int at)
        => at + 12 > file.Length
            ? Vector3.Zero
            : new Vector3(
                BitConverter.ToSingle(file[at..]),
                BitConverter.ToSingle(file[(at + 4)..]),
                BitConverter.ToSingle(file[(at + 8)..]));

    private static string ReadStringAt(ReadOnlySpan<byte> file, int instance, int field)
    {
        if (instance + field + 4 > file.Length)
            return string.Empty;

        var offset = BitConverter.ToInt32(file[(instance + field)..]);
        return offset <= 0 ? string.Empty : ReadString(file, instance + offset);
    }

    private static string ReadString(ReadOnlySpan<byte> file, int at)
    {
        if (at < 0 || at >= file.Length)
            return string.Empty;

        var end = file[at..].IndexOf((byte)0);
        var slice = end < 0 ? file[at..] : file.Slice(at, end);
        return slice.IsEmpty ? string.Empty : Encoding.UTF8.GetString(slice);
    }
}
