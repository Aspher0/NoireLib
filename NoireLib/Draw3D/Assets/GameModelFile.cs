using Lumina.Data;
using System;
using System.Collections.Generic;
using System.Text;

namespace NoireLib.Draw3D.Assets;

/// <summary>Storage format of one vertex element inside a game model's vertex buffer.</summary>
public enum GameVertexType : byte
{
    /// <summary>One 32-bit float.</summary>
    Single1 = 0,
    /// <summary>Two 32-bit floats.</summary>
    Single2 = 1,
    /// <summary>Three 32-bit floats.</summary>
    Single3 = 2,
    /// <summary>Four 32-bit floats.</summary>
    Single4 = 3,
    /// <summary>Four bytes, used unscaled (bone indices).</summary>
    UByte4 = 5,
    /// <summary>Two 16-bit signed integers.</summary>
    Short2 = 6,
    /// <summary>Four 16-bit signed integers.</summary>
    Short4 = 7,
    /// <summary>Four bytes normalized to 0..1.</summary>
    NByte4 = 8,
    /// <summary>Two 16-bit signed integers normalized to -1..1.</summary>
    NShort2 = 9,
    /// <summary>Four 16-bit signed integers normalized to -1..1.</summary>
    NShort4 = 10,
    /// <summary>Two half floats.</summary>
    Half2 = 13,
    /// <summary>Four half floats.</summary>
    Half4 = 14,
    /// <summary>Two 16-bit unsigned integers.</summary>
    UShort2 = 16,
    /// <summary>Four 16-bit unsigned integers.</summary>
    UShort4 = 17,
}

/// <summary>What a vertex element means. A single UV element carries two sets, packed as xy and zw.</summary>
public enum GameVertexUsage : byte
{
    /// <summary>Model-space position.</summary>
    Position = 0,
    /// <summary>Skinning weights.</summary>
    BlendWeights = 1,
    /// <summary>Skinning bone indices into the mesh's bone table.</summary>
    BlendIndices = 2,
    /// <summary>Model-space normal.</summary>
    Normal = 3,
    /// <summary>Texture coordinates, two sets per element.</summary>
    Uv = 4,
    /// <summary>Secondary tangent frame.</summary>
    Tangent2 = 5,
    /// <summary>Primary tangent frame, stored as a normalized byte vector.</summary>
    Tangent1 = 6,
    /// <summary>Per-vertex color, used by the game as shader data rather than albedo.</summary>
    Color = 7,
}

/// <summary>One field of a vertex, describing where it sits in a stream and how it is stored.</summary>
/// <param name="Stream">Which of the mesh's vertex streams holds this element.</param>
/// <param name="Offset">Byte offset within that stream's vertex stride.</param>
/// <param name="Type">Storage format.</param>
/// <param name="Usage">Semantic meaning.</param>
/// <param name="UsageIndex">Distinguishes multiple elements sharing a usage.</param>
public readonly record struct GameVertexElement(
    byte Stream,
    byte Offset,
    GameVertexType Type,
    GameVertexUsage Usage,
    byte UsageIndex);

/// <summary>A drawable range of a game model, addressing a slice of one level of detail's buffers.</summary>
public sealed class GameModelMeshInfo
{
    /// <summary>Number of vertices.</summary>
    public ushort VertexCount { get; init; }

    /// <summary>Number of indices.</summary>
    public uint IndexCount { get; init; }

    /// <summary>Index into the model's material path list.</summary>
    public ushort MaterialIndex { get; init; }

    /// <summary>First index of this mesh within its level of detail's index buffer.</summary>
    public uint StartIndex { get; init; }

    /// <summary>Byte offset of each vertex stream, relative to the level of detail's vertex data.</summary>
    public uint[] VertexBufferOffset { get; init; } = new uint[3];

    /// <summary>Vertex stride of each stream in bytes.</summary>
    public byte[] VertexBufferStride { get; init; } = new byte[3];

    /// <summary>How many of the three streams this mesh actually uses.</summary>
    public byte VertexStreamCount { get; init; }
}

/// <summary>One level of detail: which meshes belong to it and where its buffers start.</summary>
public sealed class GameModelLod
{
    /// <summary>Index of the first mesh belonging to this level.</summary>
    public ushort MeshIndex { get; init; }

    /// <summary>How many meshes belong to this level.</summary>
    public ushort MeshCount { get; init; }

    /// <summary>Byte offset of this level's vertex data within the file.</summary>
    public uint VertexDataOffset { get; init; }

    /// <summary>Byte offset of this level's index data within the file.</summary>
    public uint IndexDataOffset { get; init; }
}

/// <summary>
/// A parsed FFXIV model file, read straight out of the game's archives.<br/>
/// Loaded through Lumina's archive reader (which reassembles the file's independently compressed
/// regions) but parsed here, because the shipped parser reads only the older bone-table layout and
/// mis-tracks everything that follows it in current files.<br/>
/// This is the granular surface: every structure needed to decode geometry is public. For the
/// one-call path use <see cref="GameModelLoader"/>.
/// </summary>
public sealed class GameModelFile : FileResource
{
    /// <summary>Fixed size of the file header, after which the vertex declarations begin.</summary>
    private const int HeaderSize = 0x44;

    /// <summary>Every declaration reserves seventeen element slots whether or not it uses them.</summary>
    private const int ElementsPerDeclaration = 17;

    /// <summary>Size of one vertex element record in bytes.</summary>
    private const int ElementSize = 8;

    /// <summary>A stream index of 255 terminates a declaration's element list.</summary>
    private const byte ElementTerminator = 255;

    /// <summary>Files at or above this version store bone tables as an indirection into a pooled array.</summary>
    private const uint IndirectBoneTableVersion = 0x01000006;

    /// <summary>File format version.</summary>
    public uint Version { get; private set; }

    /// <summary>Number of levels of detail that carry real geometry.</summary>
    public byte LodCount { get; private set; }

    /// <summary>Vertex declarations, one per mesh, each listing the fields of that mesh's vertices.</summary>
    public GameVertexElement[][] Declarations { get; private set; } = [];

    /// <summary>The three level-of-detail slots. Only the first <see cref="LodCount"/> are meaningful.</summary>
    public GameModelLod[] Lods { get; private set; } = [];

    /// <summary>Every mesh in the file, addressed by the level-of-detail ranges.</summary>
    public GameModelMeshInfo[] Meshes { get; private set; } = [];

    /// <summary>Material paths referenced by the meshes. Character models store these relative, beginning with a slash.</summary>
    public string[] MaterialPaths { get; private set; } = [];

    /// <summary>
    /// Bytes at the end of the runtime block the layout walk did not identify, zero for most models.
    /// Everything decoded was addressed correctly regardless; this exists so an unread tail is a fact a
    /// caller can report instead of a silence.
    /// </summary>
    public int UnreadRuntimeBytes { get; private set; }

    /// <inheritdoc/>
    public override void LoadFile()
    {
        var data = Data;
        var cursor = new ByteCursor(data);

        Version = cursor.U32();
        var stackSize = cursor.U32();
        var runtimeSize = cursor.U32();
        var declarationCount = cursor.U16();
        cursor.Skip(2);                                     // material count, re-read from the runtime header
        cursor.Skip(sizeof(uint) * 12);                     // per-level vertex and index offsets and sizes
        LodCount = cursor.U8();
        cursor.Skip(3);                                     // two flags and padding

        Declarations = ReadDeclarations(data, ref cursor, declarationCount);

        var runtimeStart = cursor.Position;
        var strings = ReadStringTable(data, ref cursor, out var stringBase);

        // Runtime header. The counts drive every variable-length block that follows, and the blocks
        // must be walked in order because none of them is addressed by an offset.
        cursor.Skip(sizeof(float));                         // bounding radius
        var meshCount = cursor.U16();
        var attributeCount = cursor.U16();
        var submeshCount = cursor.U16();
        var materialCount = cursor.U16();
        var boneCount = cursor.U16();
        var boneTableCount = cursor.U16();
        var shapeCount = cursor.U16();
        var shapeMeshCount = cursor.U16();
        var shapeValueCount = cursor.U16();
        cursor.Skip(2);                                     // level count and flags
        var elementIdCount = cursor.U16();
        var terrainShadowMeshCount = cursor.U8();
        var flags2 = cursor.U8();
        cursor.Skip(sizeof(float) * 2);                     // model and shadow clip-out distances
        cursor.Skip(2);                                     // culling grid count
        var terrainShadowSubmeshCount = cursor.U16();
        cursor.Skip(4);                                     // flags and background change material indices
        var boneTableArrayCountTotal = cursor.U16();
        cursor.Skip(4 + 6);                                 // unknown counts and padding

        cursor.Skip(elementIdCount * 32);
        Lods = ReadLods(ref cursor);

        // The extra level-of-detail block only exists when the model opts into it.
        if ((flags2 & 0x10) != 0)
            cursor.Skip(3 * 40);

        Meshes = ReadMeshes(ref cursor, meshCount);

        cursor.Skip(attributeCount * sizeof(uint));
        cursor.Skip(terrainShadowMeshCount * 20);
        cursor.Skip(submeshCount * 16);
        cursor.Skip(terrainShadowSubmeshCount * 10);

        MaterialPaths = new string[materialCount];
        for (var i = 0; i < materialCount; i++)
            MaterialPaths[i] = ReadStringAt(data, stringBase, cursor.U32());

        cursor.Skip(boneCount * sizeof(uint));

        if (Version >= IndirectBoneTableVersion)
        {
            cursor.Skip(boneTableCount * sizeof(uint));
            cursor.Skip(boneTableArrayCountTotal * sizeof(ushort));
        }
        else
        {
            cursor.Skip(boneTableCount * ((64 * sizeof(ushort)) + sizeof(uint)));
        }

        cursor.Skip(shapeCount * 16);
        cursor.Skip(shapeMeshCount * 12);
        cursor.Skip(shapeValueCount * sizeof(uint));

        cursor.Skip((int)cursor.U32());                     // submesh bone map
        cursor.Skip(cursor.U8());                           // length-prefixed padding run
        cursor.Skip((4 + boneCount) * 32);                  // global and per-bone bounding boxes

        // The runtime block's size is declared in the header. Walking PAST it means a block above was
        // mis-sized and every offset derived from the walk is wrong, so that stays fatal. Falling short is
        // different: some models carry trailing data this walk does not identify - the Hingan Sofa's model
        // leaves 96 bytes here, the size of three more bounding boxes, gated by a count in the header this
        // layout has not named - and everything read above was addressed correctly, so the surplus is
        // recorded and skipped rather than refusing a model the game itself renders.
        var consumed = cursor.Position - runtimeStart;
        if (consumed > runtimeSize)
        {
            throw new InvalidOperationException(
                $"Model layout walk consumed {consumed} bytes of a declared {runtimeSize}-byte runtime block in '{FilePath}'.");
        }

        UnreadRuntimeBytes = (int)(runtimeSize - consumed);
        cursor.Skip(UnreadRuntimeBytes);

        _ = strings;
        _ = stackSize;
    }

    private static GameVertexElement[][] ReadDeclarations(byte[] data, ref ByteCursor cursor, ushort count)
    {
        var declarations = new GameVertexElement[count][];
        for (var i = 0; i < count; i++)
        {
            var elements = new List<GameVertexElement>(ElementsPerDeclaration);
            for (var e = 0; e < ElementsPerDeclaration; e++)
            {
                var at = cursor.Position + (e * ElementSize);
                var stream = data[at];
                if (stream == ElementTerminator)
                    continue;

                elements.Add(new GameVertexElement(
                    stream,
                    data[at + 1],
                    (GameVertexType)data[at + 2],
                    (GameVertexUsage)data[at + 3],
                    data[at + 4]));
            }

            declarations[i] = elements.ToArray();
            cursor.Skip(ElementsPerDeclaration * ElementSize);
        }

        return declarations;
    }

    private static string[] ReadStringTable(byte[] data, ref ByteCursor cursor, out int stringBase)
    {
        var count = cursor.U32();
        var size = cursor.U32();
        stringBase = cursor.Position;

        var strings = new string[count];
        var at = stringBase;
        for (var i = 0; i < count && at < stringBase + size; i++)
        {
            var end = at;
            while (end < stringBase + size && data[end] != 0)
                end++;

            strings[i] = Encoding.UTF8.GetString(data, at, end - at);
            at = end + 1;
        }

        cursor.Skip((int)size);
        return strings;
    }

    private static GameModelLod[] ReadLods(ref ByteCursor cursor)
    {
        var lods = new GameModelLod[3];
        for (var i = 0; i < 3; i++)
        {
            var meshIndex = cursor.U16();
            var meshCount = cursor.U16();
            cursor.Skip(sizeof(float) * 2);                 // model and texture range
            cursor.Skip(sizeof(ushort) * 8);                // water, shadow, terrain and fog mesh ranges
            cursor.Skip(sizeof(uint) * 6);                  // edge geometry, polygon count and buffer sizes

            lods[i] = new GameModelLod
            {
                MeshIndex = meshIndex,
                MeshCount = meshCount,
                VertexDataOffset = cursor.U32(),
                IndexDataOffset = cursor.U32(),
            };
        }

        return lods;
    }

    private static GameModelMeshInfo[] ReadMeshes(ref ByteCursor cursor, ushort count)
    {
        var meshes = new GameModelMeshInfo[count];
        for (var i = 0; i < count; i++)
        {
            var vertexCount = cursor.U16();
            cursor.Skip(2);
            var indexCount = cursor.U32();
            var materialIndex = cursor.U16();
            cursor.Skip(sizeof(ushort) * 3);                // submesh range and bone table index
            var startIndex = cursor.U32();

            var offsets = new uint[3];
            for (var s = 0; s < 3; s++)
                offsets[s] = cursor.U32();

            var strides = new byte[3];
            for (var s = 0; s < 3; s++)
                strides[s] = cursor.U8();

            meshes[i] = new GameModelMeshInfo
            {
                VertexCount = vertexCount,
                IndexCount = indexCount,
                MaterialIndex = materialIndex,
                StartIndex = startIndex,
                VertexBufferOffset = offsets,
                VertexBufferStride = strides,
                VertexStreamCount = cursor.U8(),
            };
        }

        return meshes;
    }

    private static string ReadStringAt(byte[] data, int stringBase, uint offset)
    {
        var start = stringBase + (int)offset;
        var end = start;
        while (end < data.Length && data[end] != 0)
            end++;

        return Encoding.UTF8.GetString(data, start, end - start);
    }

    /// <summary>Sequential little-endian reader over the model's bytes.</summary>
    private struct ByteCursor(byte[] data)
    {
        private readonly byte[] data = data;

        /// <summary>Current read position.</summary>
        public int Position { get; private set; }

        public byte U8() => data[Position++];

        public ushort U16()
        {
            var value = BitConverter.ToUInt16(data, Position);
            Position += sizeof(ushort);
            return value;
        }

        public uint U32()
        {
            var value = BitConverter.ToUInt32(data, Position);
            Position += sizeof(uint);
            return value;
        }

        public void Skip(int count) => Position += count;
    }
}
