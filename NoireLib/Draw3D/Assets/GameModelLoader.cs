using NoireLib.Draw3D.Geometry;
using NoireLib.Helpers;
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
/// Loads models from the game's archives into renderer-only geometry.<br/>
/// Vertex colors hold shader data and are not imported by default. Materials are not resolved. See <see cref="GameMaterialLoader"/>.
/// </summary>
public static class GameModelLoader
{
    /// <summary>Loads and decodes a model from the game archives.</summary>
    /// <param name="gamePath">Archive path of the model, such as <c>bgcommon/.../fun_b0_m0001.mdl</c>.</param>
    /// <param name="lod">Level of detail to decode, 0 being the most detailed.</param>
    /// <param name="importVertexColors">Whether to apply the vertex color channel.</param>
    /// <returns>One entry per mesh in the requested level of detail, or an empty array if the file does not exist.</returns>
    public static GameModelMesh[] Load(string gamePath, int lod = 0, bool importVertexColors = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gamePath);

        var file = NoireService.DataManager.GetFile<GameModelFile>(gamePath);
        return file is null ? [] : Decode(file, lod, importVertexColors);
    }

    /// <summary>Loads and decodes a model from the game archives off the calling thread.</summary>
    /// <param name="gamePath">Archive path of the model.</param>
    /// <param name="lod">Level of detail to decode, 0 being the most detailed.</param>
    /// <param name="importVertexColors">Whether to apply the model's vertex color channel.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>One entry per mesh, or an empty array if the file does not exist.</returns>
    public static Task<GameModelMesh[]> LoadAsync(string gamePath, int lod = 0, bool importVertexColors = false, CancellationToken ct = default)
        => Task.Run(() => Load(gamePath, lod, importVertexColors), ct);

    /// <summary>Decodes one level of detail of an already-parsed model.</summary>
    /// <param name="file">The parsed model.</param>
    /// <param name="lod">Level of detail to decode, 0 being the most detailed.</param>
    /// <param name="importVertexColors">Whether to apply the vertex color channel.</param>
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
            var p = file.ReadVertexElement(level, mesh, position.Value, v);
            var n = normal is null ? new Vector4(0f, 1f, 0f, 0f) : file.ReadVertexElement(level, mesh, normal.Value, v);
            var t = uv is null ? Vector4.Zero : file.ReadVertexElement(level, mesh, uv.Value, v);

            // Position w is baked occlusion. It rides in color alpha, which background models leave free.
            var c = importVertexColors && color is not null
                ? file.ReadVertexElement(level, mesh, color.Value, v)
                : new Vector4(1f, 1f, 1f, OcclusionFrom(position.Value.Type, p.W));

            var vertexNormal = Vector3.Normalize(new Vector3(n.X, n.Y, n.Z));

            vertices[v] = new Vertex3D(
                new Vector3(p.X, p.Y, p.Z),
                vertexNormal,
                new Vector2(t.X, t.Y),
                c,
                tangentFrame is null
                    ? default
                    : DecodeTangentFrame(file.ReadVertexElement(level, mesh, tangentFrame.Value, v), vertexNormal));
        }

        var indices = file.ReadIndices(level, mesh);

        // The game is counter-clockwise-front and this renderer clockwise-front.
        for (var i = 0; i + 2 < indices.Length; i += 3)
            (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);

        NoireDraw3D.Diagnostics.ImportFlips.Apply(vertices, indices);
        return new MeshData(vertices, indices);
    }

    // Bitangent as normalized bytes (v*2-1) with handedness in w. Tangent = cross(bitangent, normal) * h. Zero means "no authored frame".
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

    // The input assembler pads a missing fourth component to 1.
    private static float OcclusionFrom(GameVertexType type, float w) => type switch
    {
        GameVertexType.Single4 or GameVertexType.Half4 => Math.Clamp(w, 0f, 1f),
        _ => 1f,
    };
}
