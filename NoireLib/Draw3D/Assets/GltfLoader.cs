using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using SharpGLTF.Schema2;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Mesh = NoireLib.Draw3D.Geometry.Mesh;

namespace NoireLib.Draw3D.Assets;

/// <summary>
/// glTF 2.0 importer, the interchange point for models built in external 3D tools. Decoding runs on
/// the thread pool; meshes and textures are created synchronously wherever decoding finishes (devices
/// are free-threaded), so the returned <see cref="Model3D"/> is ready to attach.<br/>
/// Mapping: node tree to <see cref="SceneNode"/> subtree; one mesh+renderer per primitive;
/// baseColor factor/texture maps to material color/texture; alphaMode BLEND maps to translucent;
/// doubleSided maps to no culling. Metallic/roughness/normal maps, KHR extensions, skins, animations, cameras and lights
/// are ignored (logged once per file so users know exactly what was dropped).<br/>
/// Handedness: glTF is counter-clockwise-front and this renderer is clockwise-front, so the loader reverses
/// the <b>triangle winding</b> and takes positions, normals and transforms exactly as the file authored them.
/// It does not reflect the geometry: an earlier version negated Z and leaned on that reflection to flip the
/// winding for it, which fixed the culling and left every imported model a mirror image of itself.<br/>
/// <b>Vertex colors:</b> glTF <c>COLOR_0</c> is <i>not</i> imported by default. FFXIV-derived character
/// exports carry a per-vertex color channel that the game uses as shader <i>data</i> (wetness / wind /
/// blend masks), not albedo; multiplying it into the base color paints the model in psychedelic tints.
/// Pass <c>importVertexColors: true</c> for assets that genuinely author vertex colors.<br/>
/// <b>FBX:</b> never natively - convert once with FBX2glTF or Blender export; the result is a
/// better-specified asset.<br/>
/// <b>Level of detail is off by default.</b> Pass <c>generateLods: true</c> to build a quadric-error LOD chain for large
/// primitives (drawn coarser as they shrink on screen); tune or disable it via <see cref="NoireDraw3D.Performance"/>.
/// Rendering a full-detail model is cheap; the LODs are for scenes with many heavy models at once.
/// </summary>
public static class GltfLoader
{
    /// <summary>Loads a .gltf or .glb file into a detached, ready-to-attach model.</summary>
    /// <param name="path">Absolute file path.</param>
    /// <param name="keepCpuData">Retain CPU-side geometry on the meshes for exact picking.</param>
    /// <param name="importVertexColors">Apply the glTF <c>COLOR_0</c> vertex-color channel as an albedo tint. Off by default - FFXIV-derived exports store shader data there, not colors (see the type remarks).</param>
    /// <param name="generateLods">Build a level-of-detail chain for large primitives so they draw a coarser mesh as they shrink on screen. <b>Off by default</b> (see the type remarks); tune with <see cref="NoireDraw3D.Performance"/>.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<Model3D> LoadAsync(string path, bool keepCpuData = false, bool importVertexColors = false, bool generateLods = false, CancellationToken ct = default)
        => Task.Run(() => Import(ModelRoot.Load(path), System.IO.Path.GetFileName(path), keepCpuData, importVertexColors, generateLods, ct), ct);

    /// <summary>Loads a binary .glb from memory into a detached, ready-to-attach model.</summary>
    /// <param name="glbBytes">GLB file contents.</param>
    /// <param name="keepCpuData">Retain CPU-side geometry on the meshes for exact picking.</param>
    /// <param name="importVertexColors">Apply the glTF <c>COLOR_0</c> vertex-color channel as an albedo tint. Off by default (see the type remarks).</param>
    /// <param name="generateLods">Build a level-of-detail chain for large primitives (off by default; see the type remarks).</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<Model3D> LoadGlbAsync(byte[] glbBytes, bool keepCpuData = false, bool importVertexColors = false, bool generateLods = false, CancellationToken ct = default)
        => Task.Run(() => Import(ModelRoot.ParseGLB(glbBytes), "glb", keepCpuData, importVertexColors, generateLods, ct), ct);

    /// <summary>Below this triangle count a mesh is left at full detail - a small mesh gains nothing from a LOD chain.</summary>
    private const int LodMinTriangles = 4000;

    /// <summary>Target triangle fractions for the LOD levels (finest first): 50%, 25%, 12% of the original.</summary>
    private static readonly float[] LodTargetRatios = { 0.5f, 0.25f, 0.12f };

    /// <summary>Accumulates what the import actually did, so "the model looks wrong" is answerable from one log line.</summary>
    private sealed class ImportStats
    {
        public int Primitives;
        public int TexturedMaterials;
        public int TextureDecodeFailures;
        public bool SawVertexColors;
        public int LodLevels;
    }

    private static Model3D Import(ModelRoot root, string sourceName, bool keepCpuData, bool importVertexColors, bool generateLods, CancellationToken ct)
    {
        NoireDraw3D.EnsureInitialized();

        var meshes = new List<Mesh>();
        var textures = new List<GpuTexture>();
        var textureCache = new Dictionary<SharpGLTF.Schema2.Texture, GpuTexture?>();
        var dropped = new HashSet<string>();
        var stats = new ImportStats();

        var modelRoot = new SceneNode(null, sourceName);

        var scene = root.DefaultScene;
        if (scene != null)
        {
            foreach (var child in scene.VisualChildren)
            {
                ct.ThrowIfCancellationRequested();
                ImportNode(child, modelRoot, meshes, textures, textureCache, dropped, stats, keepCpuData, importVertexColors, generateLods, ct);
            }
        }

        if (root.LogicalAnimations.Count > 0)
            dropped.Add("animations");
        if (root.LogicalSkins.Count > 0)
            dropped.Add("skins");
        if (root.LogicalCameras.Count > 0)
            dropped.Add("cameras");

        // One summary line makes a wrong-looking import self-diagnosing: textured vs. flat materials, decode
        // failures, and whether a vertex-color channel was present (the usual cause of psychedelic tints).
        var summary = $"glTF '{sourceName}': {stats.Primitives} primitive(s), {stats.TexturedMaterials} textured / {stats.Primitives - stats.TexturedMaterials} flat.";
        if (stats.LodLevels > 0)
            summary += $" Generated {stats.LodLevels} LOD level(s) for large primitives (NoireDraw3D.Performance.Lod).";
        if (stats.TextureDecodeFailures > 0)
            summary += $" {stats.TextureDecodeFailures} base texture(s) failed to decode (those render flat).";
        if (stats.SawVertexColors && !importVertexColors)
            summary += " COLOR_0 vertex colors present but ignored (treated as shader data; pass importVertexColors:true to apply).";
        if (dropped.Count > 0)
            summary += $" Dropped {string.Join(", ", dropped)} (unsupported by the Draw3D core).";
        NoireLogger.LogInfo(summary, "Draw3D");

        return new Model3D(modelRoot, meshes, textures);
    }

    private static void ImportNode(
        Node gltfNode,
        SceneNode parent,
        List<Mesh> meshes,
        List<GpuTexture> textures,
        Dictionary<SharpGLTF.Schema2.Texture, GpuTexture?> textureCache,
        HashSet<string> dropped,
        ImportStats stats,
        bool keepCpuData,
        bool importVertexColors,
        bool generateLods,
        CancellationToken ct)
    {
        var node = parent.CreateChild(gltfNode.Name);
        ApplyTransform(node, gltfNode.LocalMatrix);

        if (gltfNode.Mesh != null)
        {
            foreach (var primitive in gltfNode.Mesh.Primitives)
            {
                ct.ThrowIfCancellationRequested();
                ImportPrimitive(primitive, node, meshes, textures, textureCache, dropped, stats, keepCpuData, importVertexColors, generateLods);
            }
        }

        foreach (var child in gltfNode.VisualChildren)
            ImportNode(child, node, meshes, textures, textureCache, dropped, stats, keepCpuData, importVertexColors, generateLods, ct);
    }

    private static void ApplyTransform(SceneNode node, Matrix4x4 local)
    {
        // Taken as authored, to match the vertices. An earlier version conjugated by diag(1, 1, -1, 1) here
        // and negated Z on the vertices, which mirrored the model; both halves now leave the file's own
        // convention alone and only the triangle winding is changed.
        //
        // A hierarchical model needs these two halves to agree or it comes apart: transforming the meshes
        // without the transforms that place them reflects each part inside its own local space and leaves the
        // arrangement untouched, which arrives as a vehicle whose left and right doors are each individually
        // flipped but still on the side they started.
        local = NoireDraw3D.Diagnostics.ImportFlips.Apply(local);

        if (Matrix4x4.Decompose(local, out var scale, out var rotation, out var translation))
        {
            node.LocalScale = scale;
            node.LocalRotation = rotation;
            node.LocalPosition = translation;
        }
        else
        {
            node.LocalPosition = local.Translation;
        }
    }

    private static void ImportPrimitive(
        MeshPrimitive primitive,
        SceneNode node,
        List<Mesh> meshes,
        List<GpuTexture> textures,
        Dictionary<SharpGLTF.Schema2.Texture, GpuTexture?> textureCache,
        HashSet<string> dropped,
        ImportStats stats,
        bool keepCpuData,
        bool importVertexColors,
        bool generateLods)
    {
        var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
        if (positions == null || positions.Count == 0)
            return;

        var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
        var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        var colors = primitive.GetVertexAccessor("COLOR_0") != null ? primitive.GetVertexAccessor("COLOR_0").AsColorArray() : null;
        if (colors != null)
            stats.SawVertexColors = true;
        if (!importVertexColors)
            colors = null; // COLOR_0 is shader data on FFXIV-derived models, not albedo - do not tint by default.
        if (primitive.GetVertexAccessor("JOINTS_0") != null)
            dropped.Add("skinning attributes");

        var vertices = new Vertex3D[positions.Count];
        for (var i = 0; i < positions.Count; i++)
        {
            var p = positions[i];
            var n = normals != null && i < normals.Count ? normals[i] : Vector3.UnitY;
            vertices[i] = new Vertex3D(
                new Vector3(p.X, p.Y, p.Z),
                new Vector3(n.X, n.Y, n.Z),
                uvs != null && i < uvs.Count ? uvs[i] : Vector2.Zero,
                colors != null && i < colors.Count ? colors[i] : new Vector4(1f, 1f, 1f, 1f));
        }

        // glTF is counter-clockwise-front and this renderer is clockwise-front, so the winding is reversed
        // here. An earlier version negated Z on the positions instead and relied on that reflection to flip
        // the winding for it - which fixed the culling and mirrored the model, arriving as text on a texture
        // reading backwards and, confirmed in game, the whole shape reversed.
        var triangles = new List<uint>();
        foreach (var (a, b, c) in primitive.GetTriangleIndices())
        {
            triangles.Add((uint)a);
            triangles.Add((uint)c);
            triangles.Add((uint)b);
        }

        if (triangles.Count == 0)
            return;

        // Driven from inside the loader rather than by the caller, so this path and the game-model one answer
        // the mirrored-import question the same way. Does nothing unless something is turned on.
        NoireDraw3D.Diagnostics.ImportFlips.Apply(vertices, triangles);

        Mesh mesh;
        if (vertices.Length <= ushort.MaxValue)
        {
            var indices16 = new ushort[triangles.Count];
            for (var i = 0; i < triangles.Count; i++)
                indices16[i] = (ushort)triangles[i];
            mesh = new Mesh(vertices, indices16, keepCpuData, primitive.LogicalParent?.Name);
        }
        else
        {
            mesh = new Mesh(vertices, triangles.ToArray(), keepCpuData, primitive.LogicalParent?.Name);
        }

        meshes.Add(mesh);
        stats.Primitives++;
        if (generateLods)
            GenerateLods(mesh, vertices, triangles, stats);

        var material = BuildMaterial(primitive.Material, textures, textureCache, dropped, stats);
        var renderNode = node.CreateChild($"{primitive.LogicalParent?.Name}#prim");
        renderNode.SetMesh(mesh, material);
    }

    /// <summary>
    /// Builds and attaches a quadric-error LOD chain for a large primitive, so it draws a coarser mesh as it shrinks on
    /// screen (used/tuned via <see cref="NoireDraw3D.Performance"/>). Skipped for small meshes; fail-soft - a decimation
    /// fault leaves the model at full detail.
    /// </summary>
    private static void GenerateLods(Mesh mesh, Vertex3D[] vertices, List<uint> triangles, ImportStats stats)
    {
        if (triangles.Count / 3 < LodMinTriangles)
            return;

        try
        {
            var lods = MeshSimplifier.BuildLods(vertices, triangles.ToArray(), LodTargetRatios, mesh.Name);
            if (lods.Length > 0)
            {
                mesh.SetLods(lods);
                stats.LodLevels += lods.Length;
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "glTF: LOD generation failed for a primitive; it draws at full detail.", "Draw3D");
        }
    }

    private static Materials.Material BuildMaterial(
        SharpGLTF.Schema2.Material? gltfMaterial,
        List<GpuTexture> textures,
        Dictionary<SharpGLTF.Schema2.Texture, GpuTexture?> textureCache,
        HashSet<string> dropped,
        ImportStats stats)
    {
        var color = new Vector4(1f, 1f, 1f, 1f);
        GpuTexture? texture = null;
        var blend = BlendMode.Opaque;
        var cull = CullMode.Back;

        if (gltfMaterial != null)
        {
            var baseColor = gltfMaterial.FindChannel("BaseColor");
            if (baseColor.HasValue)
            {
                color = baseColor.Value.Color;
                if (baseColor.Value.Texture != null)
                {
                    texture = ResolveTexture(baseColor.Value.Texture, textures, textureCache);
                    if (texture == null)
                        stats.TextureDecodeFailures++;
                }
            }

            if (gltfMaterial.FindChannel("MetallicRoughness")?.Texture != null || gltfMaterial.FindChannel("Normal")?.Texture != null)
                dropped.Add("metallic/roughness/normal maps");

            blend = gltfMaterial.Alpha switch
            {
                AlphaMode.BLEND => BlendMode.Premultiplied,
                _ => BlendMode.Opaque, // MASK renders opaque (cutoff unsupported in core; logged)
            };
            if (gltfMaterial.Alpha == AlphaMode.MASK)
                dropped.Add("alpha-mask cutoff");
            if (gltfMaterial.DoubleSided)
                cull = CullMode.None;
        }

        if (texture != null)
            stats.TexturedMaterials++;

        return new Materials.Material
        {
            Domain = MaterialDomain.Lit,
            Blend = blend,
            Color = color,
            Texture = texture,
            Cull = cull,
        };
    }

    private static GpuTexture? ResolveTexture(
        SharpGLTF.Schema2.Texture gltfTexture,
        List<GpuTexture> textures,
        Dictionary<SharpGLTF.Schema2.Texture, GpuTexture?> textureCache)
    {
        if (textureCache.TryGetValue(gltfTexture, out var cached))
            return cached;

        GpuTexture? result = null;
        try
        {
            var content = gltfTexture.PrimaryImage?.Content;
            if (content is { IsValid: true })
            {
                // Dalamud decodes the PNG/JPG bytes; blocking here is fine - we are on the thread pool.
                using var wrap = NoireService.TextureProvider.CreateFromImageAsync(content.Value.Content.ToArray()).GetAwaiter().GetResult();
                result = TextureLoader.FromWrap(wrap);
                if (result != null)
                    textures.Add(result);
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "glTF: base color texture failed to decode; the material renders untextured.", "Draw3D");
        }

        textureCache[gltfTexture] = result;
        return result;
    }
}
