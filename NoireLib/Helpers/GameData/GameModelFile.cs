using Lumina.Data;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// A parsed FFXIV model file, read straight out of the game's archives. Lumina's own parser is not used because
/// it reads only the older bone-table layout and mis-tracks everything after it.
/// </summary>
public sealed class GameModelFile : FileResource
{
    private const int HeaderSize = 0x44;

    // Every declaration reserves seventeen element slots.
    private const int ElementsPerDeclaration = 17;

    private const int ElementSize = 8;

    // A stream index of 255 terminates a declaration's element list.
    private const byte ElementTerminator = 255;

    // From this version on, bone tables are an indirection into a pooled array.
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

    /// <summary>Lower corner of the model's own bounding box, in the model's local space.</summary>
    public Vector3 BoundingBoxMin { get; private set; }

    /// <summary>Upper corner of the model's own bounding box, in the model's local space.</summary>
    public Vector3 BoundingBoxMax { get; private set; }

    /// <summary>Bytes at the end of the runtime block the layout walk did not identify, zero for most models.</summary>
    public int UnreadRuntimeBytes { get; private set; }

    /// <summary>Reads one vertex element of one mesh, widened to four floats. Unused components are zero.</summary>
    /// <param name="lod">The level of detail the mesh belongs to.</param>
    /// <param name="mesh">The mesh.</param>
    /// <param name="element">The element, from the mesh's entry in <see cref="Declarations"/>.</param>
    /// <param name="vertex">The vertex index within the mesh.</param>
    /// <returns>The element's value.</returns>
    public Vector4 ReadVertexElement(GameModelLod lod, GameModelMeshInfo mesh, GameVertexElement element, int vertex)
    {
        var data = Data;
        var stream = element.Stream;
        var at = (int)lod.VertexDataOffset
                 + (int)mesh.VertexBufferOffset[stream]
                 + (vertex * mesh.VertexBufferStride[stream])
                 + element.Offset;

        return element.Type switch
        {
            GameVertexType.Single1 => new Vector4(F(data, at), 0f, 0f, 0f),
            GameVertexType.Single2 => new Vector4(F(data, at), F(data, at + 4), 0f, 0f),
            GameVertexType.Single3 => new Vector4(F(data, at), F(data, at + 4), F(data, at + 8), 0f),
            GameVertexType.Single4 => new Vector4(F(data, at), F(data, at + 4), F(data, at + 8), F(data, at + 12)),
            GameVertexType.Half2 => new Vector4(H(data, at), H(data, at + 2), 0f, 0f),
            GameVertexType.Half4 => new Vector4(H(data, at), H(data, at + 2), H(data, at + 4), H(data, at + 6)),
            GameVertexType.UByte4 => new Vector4(data[at], data[at + 1], data[at + 2], data[at + 3]),
            GameVertexType.NByte4 => new Vector4(data[at] / 255f, data[at + 1] / 255f, data[at + 2] / 255f, data[at + 3] / 255f),
            GameVertexType.Short2 => new Vector4(S(data, at), S(data, at + 2), 0f, 0f),
            GameVertexType.Short4 => new Vector4(S(data, at), S(data, at + 2), S(data, at + 4), S(data, at + 6)),
            GameVertexType.NShort2 => new Vector4(S(data, at) / 32767f, S(data, at + 2) / 32767f, 0f, 0f),
            GameVertexType.NShort4 => new Vector4(S(data, at) / 32767f, S(data, at + 2) / 32767f, S(data, at + 4) / 32767f, S(data, at + 6) / 32767f),
            GameVertexType.UShort2 => new Vector4(U(data, at), U(data, at + 2), 0f, 0f),
            GameVertexType.UShort4 => new Vector4(U(data, at), U(data, at + 2), U(data, at + 4), U(data, at + 6)),
            _ => Vector4.Zero,
        };

        static float F(byte[] d, int at) => BitConverter.ToSingle(d, at);
        static float H(byte[] d, int at) => (float)BitConverter.ToHalf(d, at);
        static float S(byte[] d, int at) => BitConverter.ToInt16(d, at);
        static float U(byte[] d, int at) => BitConverter.ToUInt16(d, at);
    }

    /// <summary>Reads one mesh's triangle-list indices, in the game's counter-clockwise winding.</summary>
    /// <param name="lod">The level of detail the mesh belongs to.</param>
    /// <param name="mesh">The mesh.</param>
    /// <returns>The indices, into the mesh's own vertices.</returns>
    public ushort[] ReadIndices(GameModelLod lod, GameModelMeshInfo mesh)
    {
        var data = Data;
        var indexBase = (int)lod.IndexDataOffset + ((int)mesh.StartIndex * sizeof(ushort));
        var indices = new ushort[mesh.IndexCount];
        for (var i = 0; i < mesh.IndexCount; i++)
            indices[i] = BitConverter.ToUInt16(data, indexBase + (i * sizeof(ushort)));

        return indices;
    }

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

        // No block below is addressed by an offset. They are walked in order from these counts.
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

        // Only present when the model opts into it.
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

        // Four global bounding boxes, then one per bone. The first is the model's own extent.
        BoundingBoxMin = ReadVector4AsVector3(data, cursor.Position);
        BoundingBoxMax = ReadVector4AsVector3(data, cursor.Position + 16);
        cursor.Skip((4 + boneCount) * 32);

        // Past the declared runtime size means a mis-sized block. Short of it is an unidentified tail, skipped.
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

        var table = data.AsSpan(0, stringBase + (int)size);
        var strings = new string[count];
        var at = stringBase;
        for (var i = 0; i < count && at < stringBase + size; i++)
            strings[i] = BufferHelper.ReadNullTerminatedString(table, at, out at);

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
        => BufferHelper.ReadNullTerminatedString(data, stringBase + (int)offset);

    private static Vector3 ReadVector4AsVector3(byte[] data, int at)
        => at + 12 > data.Length ? Vector3.Zero : BufferHelper.ReadVector3(data, at);

}
