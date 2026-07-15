using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using SharpGLTF.Schema2;
using System;
using Mesh = NoireLib.Draw3D.Geometry.Mesh;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Draw3D.Assets;

/// <summary>
/// glTF 2.0 importer - the "FF14 Blender" on-ramp. Decoding runs on the thread pool; meshes and
/// textures are created synchronously wherever decoding finishes (devices are free-threaded), so the
/// returned <see cref="Model3D"/> is ready to attach.<br/>
/// Mapping: node tree → <see cref="SceneNode"/> subtree; one mesh+renderer per primitive;
/// baseColor factor/texture → material color/texture; alphaMode BLEND → translucent; doubleSided →
/// no culling. Metallic/roughness/normal maps, KHR extensions, skins, animations, cameras and lights
/// are ignored (logged once per file so users know exactly what was dropped).<br/>
/// Handedness: glTF is right-handed, this renderer is left-handed - the loader negates Z (positions,
/// normals, transforms) and flips triangle winding, one documented transform.<br/>
/// <b>FBX:</b> never natively - convert once with FBX2glTF or Blender export; the result is a
/// better-specified asset.
/// </summary>
public static class GltfLoader
{
    /// <summary>Loads a .gltf or .glb file into a detached, ready-to-attach model.</summary>
    /// <param name="path">Absolute file path.</param>
    /// <param name="keepCpuData">Retain CPU-side geometry on the meshes for exact picking.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<Model3D> LoadAsync(string path, bool keepCpuData = false, CancellationToken ct = default)
        => Task.Run(() => Import(ModelRoot.Load(path), System.IO.Path.GetFileName(path), keepCpuData, ct), ct);

    /// <summary>Loads a binary .glb from memory into a detached, ready-to-attach model.</summary>
    /// <param name="glbBytes">GLB file contents.</param>
    /// <param name="keepCpuData">Retain CPU-side geometry on the meshes for exact picking.</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static Task<Model3D> LoadGlbAsync(byte[] glbBytes, bool keepCpuData = false, CancellationToken ct = default)
        => Task.Run(() => Import(ModelRoot.ParseGLB(glbBytes), "glb", keepCpuData, ct), ct);

    private static Model3D Import(ModelRoot root, string sourceName, bool keepCpuData, CancellationToken ct)
    {
        NoireDraw3D.EnsureInitialized();

        var meshes = new List<Mesh>();
        var textures = new List<GpuTexture>();
        var textureCache = new Dictionary<SharpGLTF.Schema2.Texture, GpuTexture?>();
        var dropped = new HashSet<string>();

        var modelRoot = new SceneNode(null, sourceName);

        var scene = root.DefaultScene;
        if (scene != null)
        {
            foreach (var child in scene.VisualChildren)
            {
                ct.ThrowIfCancellationRequested();
                ImportNode(child, modelRoot, meshes, textures, textureCache, dropped, keepCpuData, ct);
            }
        }

        if (root.LogicalAnimations.Count > 0)
            dropped.Add("animations");
        if (root.LogicalSkins.Count > 0)
            dropped.Add("skins");
        if (root.LogicalCameras.Count > 0)
            dropped.Add("cameras");

        if (dropped.Count > 0)
            NoireLogger.LogInfo($"glTF '{sourceName}': imported without {string.Join(", ", dropped)} (unsupported by the Draw3D core).", "Draw3D");

        return new Model3D(modelRoot, meshes, textures);
    }

    private static void ImportNode(
        Node gltfNode,
        SceneNode parent,
        List<Mesh> meshes,
        List<GpuTexture> textures,
        Dictionary<SharpGLTF.Schema2.Texture, GpuTexture?> textureCache,
        HashSet<string> dropped,
        bool keepCpuData,
        CancellationToken ct)
    {
        var node = parent.CreateChild(gltfNode.Name);
        ApplyTransform(node, gltfNode.LocalMatrix);

        if (gltfNode.Mesh != null)
        {
            foreach (var primitive in gltfNode.Mesh.Primitives)
            {
                ct.ThrowIfCancellationRequested();
                ImportPrimitive(primitive, node, meshes, textures, textureCache, dropped, keepCpuData);
            }
        }

        foreach (var child in gltfNode.VisualChildren)
            ImportNode(child, node, meshes, textures, textureCache, dropped, keepCpuData, ct);
    }

    private static void ApplyTransform(SceneNode node, Matrix4x4 local)
    {
        // Right-handed → left-handed: M' = F · M · F with F = diag(1, 1, -1, 1).
        local.M13 = -local.M13;
        local.M23 = -local.M23;
        local.M43 = -local.M43;
        local.M31 = -local.M31;
        local.M32 = -local.M32;
        local.M34 = -local.M34;

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
        bool keepCpuData)
    {
        var positions = primitive.GetVertexAccessor("POSITION")?.AsVector3Array();
        if (positions == null || positions.Count == 0)
            return;

        var normals = primitive.GetVertexAccessor("NORMAL")?.AsVector3Array();
        var uvs = primitive.GetVertexAccessor("TEXCOORD_0")?.AsVector2Array();
        var colors = primitive.GetVertexAccessor("COLOR_0")?.AsColorArray();
        if (primitive.GetVertexAccessor("JOINTS_0") != null)
            dropped.Add("skinning attributes");

        var vertices = new Vertex3D[positions.Count];
        for (var i = 0; i < positions.Count; i++)
        {
            var p = positions[i];
            var n = normals != null && i < normals.Count ? normals[i] : Vector3.UnitY;
            vertices[i] = new Vertex3D(
                new Vector3(p.X, p.Y, -p.Z),
                new Vector3(n.X, n.Y, -n.Z),
                uvs != null && i < uvs.Count ? uvs[i] : Vector2.Zero,
                colors != null && i < colors.Count ? colors[i] : new Vector4(1f, 1f, 1f, 1f));
        }

        // Winding flips with the handedness change (a, c, b).
        var triangles = new List<uint>();
        foreach (var (a, b, c) in primitive.GetTriangleIndices())
        {
            triangles.Add((uint)a);
            triangles.Add((uint)c);
            triangles.Add((uint)b);
        }

        if (triangles.Count == 0)
            return;

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

        var material = BuildMaterial(primitive.Material, textures, textureCache, dropped);
        var renderNode = node.CreateChild($"{primitive.LogicalParent?.Name}#prim");
        renderNode.SetMesh(mesh, material);
    }

    private static Materials.Material BuildMaterial(
        SharpGLTF.Schema2.Material? gltfMaterial,
        List<GpuTexture> textures,
        Dictionary<SharpGLTF.Schema2.Texture, GpuTexture?> textureCache,
        HashSet<string> dropped)
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
                    texture = ResolveTexture(baseColor.Value.Texture, textures, textureCache);
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
