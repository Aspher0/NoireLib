using NoireLib.Draw3D.Geometry;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Draw3D.Assets;

/// <summary>One drawable piece of a game model: its geometry and the material path it asks for.</summary>
/// <param name="Geometry">Decoded vertices and indices, ready for <see cref="Scene.Scene3D.Spawn(MeshData, Materials.Material, Vector3, string, bool)"/>.</param>
/// <param name="MaterialPath">The material this piece references. Character models store this relative, beginning with a slash.</param>
public readonly record struct GameModelMesh(MeshData Geometry, string MaterialPath);

/// <summary>
/// Loads models out of the game's own archives and decodes them into renderer geometry.<br/>
/// Reads files by their game path (for example <c>bgcommon/hou/indoor/general/0001/bgparts/fun_b0_m0001.mdl</c>);
/// nothing is spawned in the game and no game function is called, so a loaded model is inert geometry
/// that exists only in this renderer.<br/>
/// <b>Vertex colors are not imported.</b> The game stores shader data there (blend and wetness masks)
/// rather than albedo, so applying it as a tint paints models in false colors. Pass
/// <c>importVertexColors: true</c> only for assets known to author real colors.<br/>
/// Materials are not resolved yet: each piece reports the path it wants and is drawn with whatever
/// material the caller supplies. Use <see cref="GameModelFile"/> directly for full access to levels of
/// detail, vertex declarations and per-mesh buffer layout.
/// </summary>
public static class GameModelLoader
{
    /// <summary>Loads and decodes a model from the game archives.</summary>
    /// <param name="gamePath">Archive path of the model, such as <c>bgcommon/.../fun_b0_m0001.mdl</c>.</param>
    /// <param name="lod">Level of detail to decode, 0 being the most detailed.</param>
    /// <param name="importVertexColors">Apply the model's vertex color channel. Off by default (see the type remarks).</param>
    /// <returns>One entry per mesh in the requested level of detail, or an empty array if the file does not exist.</returns>
    public static GameModelMesh[] Load(string gamePath, int lod = 0, bool importVertexColors = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);

        var file = NoireService.DataManager.GetFile<GameModelFile>(gamePath);
        return file is null ? [] : Decode(file, lod, importVertexColors);
    }

    /// <inheritdoc cref="Load"/>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<GameModelMesh[]> LoadAsync(string gamePath, int lod = 0, bool importVertexColors = false, CancellationToken ct = default)
        => Task.Run(() => Load(gamePath, lod, importVertexColors), ct);

    /// <summary>Decodes one level of detail of an already-parsed model.</summary>
    /// <param name="file">The parsed model.</param>
    /// <param name="lod">Level of detail to decode, 0 being the most detailed.</param>
    /// <param name="importVertexColors">Apply the model's vertex color channel. Off by default (see the type remarks).</param>
    public static GameModelMesh[] Decode(GameModelFile file, int lod = 0, bool importVertexColors = false)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentOutOfRangeException.ThrowIfNegative(lod);

        if (lod >= file.Lods.Length || file.Lods[lod].MeshCount == 0)
            return [];

        var level = file.Lods[lod];
        var results = new List<GameModelMesh>(level.MeshCount);

        for (var i = level.MeshIndex; i < level.MeshIndex + level.MeshCount; i++)
        {
            if (i >= file.Meshes.Length || i >= file.Declarations.Length)
                break;

            var geometry = DecodeMesh(file, level, file.Meshes[i], file.Declarations[i], importVertexColors);
            if (geometry.Vertices.Length == 0)
                continue;

            var material = file.Meshes[i].MaterialIndex < file.MaterialPaths.Length
                ? file.MaterialPaths[file.Meshes[i].MaterialIndex]
                : string.Empty;

            results.Add(new GameModelMesh(geometry, material));
        }

        return results.ToArray();
    }

    private static MeshData DecodeMesh(
        GameModelFile file,
        GameModelLod level,
        GameModelMeshInfo mesh,
        GameVertexElement[] declaration,
        bool importVertexColors)
    {
        var data = file.Data;
        var vertices = new Vertex3D[mesh.VertexCount];

        GameVertexElement? position = null, normal = null, uv = null, color = null, tangentFrame = null;
        foreach (var element in declaration)
        {
            switch (element.Usage)
            {
                case GameVertexUsage.Position when position is null: position = element; break;
                case GameVertexUsage.Normal when normal is null: normal = element; break;
                case GameVertexUsage.Uv when uv is null: uv = element; break;
                case GameVertexUsage.Color when color is null: color = element; break;
                case GameVertexUsage.Tangent1 when tangentFrame is null: tangentFrame = element; break;
            }
        }

        if (position is null)
            return new MeshData([], []);

        for (var v = 0; v < mesh.VertexCount; v++)
        {
            var p = ReadElement(data, level, mesh, position.Value, v);
            var n = normal is null ? new Vector4(0f, 1f, 0f, 0f) : ReadElement(data, level, mesh, normal.Value, v);
            var t = uv is null ? Vector4.Zero : ReadElement(data, level, mesh, uv.Value, v);

            // The position's fourth component is baked per-vertex occlusion: the game's own background
            // shaders write it into the G-buffer's occlusion channel (multiplied by a per-instance sky
            // visibility), which is what darkens carved recesses. It rides in the color's alpha, which
            // background models leave free - they declare no color element.
            var c = importVertexColors && color is not null
                ? ReadElement(data, level, mesh, color.Value, v)
                : new Vector4(1f, 1f, 1f, OcclusionFrom(position.Value.Type, p.W));

            // Positions and normals are taken as authored. An earlier version negated Z here, reasoning that
            // one reflection converts the handedness and flips the winding at the same time - which is true
            // about the winding and wrong about the shape, because a reflection mirrors the model. It arrived
            // as text on a texture reading backwards and, confirmed in game, the whole model mirrored. The
            // winding is handled on its own below, where it belongs.
            var vertexNormal = Vector3.Normalize(new Vector3(n.X, n.Y, n.Z));

            vertices[v] = new Vertex3D(
                new Vector3(p.X, p.Y, p.Z),
                vertexNormal,
                new Vector2(t.X, t.Y),
                c,
                tangentFrame is null
                    ? default
                    : DecodeTangentFrame(ReadElement(data, level, mesh, tangentFrame.Value, v), vertexNormal));
        }

        var indexBase = (int)level.IndexDataOffset + ((int)mesh.StartIndex * sizeof(ushort));
        var indices = new ushort[mesh.IndexCount];
        for (var i = 0; i < mesh.IndexCount; i++)
            indices[i] = BitConverter.ToUInt16(data, indexBase + (i * sizeof(ushort)));

        // The game authors its triangles counter-clockwise-front and this renderer is clockwise-front, so the
        // winding is reversed here - on its own, rather than as a side effect of reflecting the geometry.
        // Without it the model renders inside out and its near faces are culled away.
        for (var i = 0; i + 2 < indices.Length; i += 3)
            (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);

        // Applied last, and does nothing unless a caller has asked for it. Both loaders run the same options
        // so a file authored in an unusual convention is corrected the same way whichever path imports it.
        NoireDraw3D.Diagnostics.ImportFlips.Apply(vertices, indices);
        return new MeshData(vertices, indices);
    }

    /// <summary>
    /// Turns the model's stored tangent-frame element into the tangent this renderer's vertices carry.<br/>
    /// The game stores the <b>bitangent</b>, packed into normalized bytes (<c>xyz</c> as <c>v*2-1</c>), with
    /// the handedness flag in <c>w</c>. The tangent is reconstructed as <c>cross(bitangent, normal) * h</c>,
    /// which is the inversion of the standard frame relation <c>bitangent = cross(normal, tangent) * h</c>;
    /// the shader rebuilds the bitangent from that same relation, so the round trip lands on the authored
    /// frame. A degenerate stored vector returns zero, which the shaders treat as "no authored frame" and
    /// answer with the derivative fallback rather than shading with garbage.
    /// </summary>
    /// <param name="packed">The element as read: bytes already normalized to 0..1.</param>
    /// <param name="normal">The vertex normal, already normalized.</param>
    internal static Vector4 DecodeTangentFrame(Vector4 packed, Vector3 normal)
    {
        var stored = new Vector3((packed.X * 2f) - 1f, (packed.Y * 2f) - 1f, (packed.Z * 2f) - 1f);
        var lengthSquared = stored.LengthSquared();
        if (lengthSquared < 1e-6f)
            return default;

        var handedness = packed.W > 0.5f ? 1f : -1f;
        var tangent = Vector3.Cross(stored / MathF.Sqrt(lengthSquared), normal) * handedness;

        lengthSquared = tangent.LengthSquared();
        if (lengthSquared < 1e-6f)
            return default;

        tangent /= MathF.Sqrt(lengthSquared);
        return new Vector4(tangent.X, tangent.Y, tangent.Z, handedness);
    }

    /// <summary>
    /// The baked occlusion a position element carries in its fourth component, or 1 (fully open) for a
    /// format that stores no fourth component - the input assembler pads those to 1 for the game's own
    /// shaders too, so both readings match what the game renders.
    /// </summary>
    private static float OcclusionFrom(GameVertexType type, float w) => type switch
    {
        GameVertexType.Single4 or GameVertexType.Half4 => Math.Clamp(w, 0f, 1f),
        _ => 1f,
    };

    private static Vector4 ReadElement(byte[] data, GameModelLod level, GameModelMeshInfo mesh, GameVertexElement element, int vertex)
    {
        var stream = element.Stream;
        var at = (int)level.VertexDataOffset
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
}
