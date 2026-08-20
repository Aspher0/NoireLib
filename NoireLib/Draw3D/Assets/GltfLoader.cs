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
/// glTF 2.0 importer. Decoding runs on the thread pool and meshes and textures are created where decoding finishes,
/// so the returned <see cref="Model3D"/> is ready to attach. The node tree maps to a <see cref="SceneNode"/> subtree
/// with one mesh and renderer per primitive, and triangle winding is reversed since glTF is counter-clockwise-front
/// while this renderer is clockwise-front. Materials with an authored metallic, a metallic-roughness or normal
/// texture, an emissive factor or an alpha-mask cutoff are shaded by <see cref="GltfPbrPipeline"/>; emissive
/// textures, separate occlusion maps, texture transforms, specular-glossiness, transmission and clearcoat
/// extensions, skins, animations, cameras and lights are dropped and logged once per file.
/// </summary>
public static class GltfLoader
{
    /// <summary>Loads a .gltf or .glb file into a detached, ready-to-attach model.</summary>
    /// <param name="path">Absolute file path.</param>
    /// <param name="keepCpuData">Whether CPU-side geometry is retained on the meshes for exact picking.</param>
    /// <param name="importVertexColors">Whether <c>COLOR_0</c> is applied as an albedo tint; off by default, since FFXIV-derived exports store shader data there.</param>
    /// <param name="generateLods">Whether large primitives get a level-of-detail chain, tuned with <see cref="NoireDraw3D.Performance"/>.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The loaded model, detached from any scene.</returns>
    public static Task<Model3D> LoadAsync(string path, bool keepCpuData = false, bool importVertexColors = false, bool generateLods = false, CancellationToken ct = default)
        => Task.Run(() => Import(ModelRoot.Load(path), System.IO.Path.GetFileName(path), keepCpuData, importVertexColors, generateLods, ct), ct);

    /// <summary>Loads a binary .glb from memory into a detached, ready-to-attach model.</summary>
    /// <param name="glbBytes">GLB file contents.</param>
    /// <param name="keepCpuData">Whether CPU-side geometry is retained on the meshes for exact picking.</param>
    /// <param name="importVertexColors">Whether <c>COLOR_0</c> is applied as an albedo tint; off by default, since FFXIV-derived exports store shader data there.</param>
    /// <param name="generateLods">Whether large primitives get a level-of-detail chain, tuned with <see cref="NoireDraw3D.Performance"/>.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The loaded model, detached from any scene.</returns>
    public static Task<Model3D> LoadGlbAsync(byte[] glbBytes, bool keepCpuData = false, bool importVertexColors = false, bool generateLods = false, CancellationToken ct = default)
        => Task.Run(() => Import(ModelRoot.ParseGLB(glbBytes), "glb", keepCpuData, importVertexColors, generateLods, ct), ct);

    /// <summary>Below this triangle count a mesh is left at full detail.</summary>
    private const int LodMinTriangles = 4000;

    /// <summary>Target triangle fractions for the LOD levels (finest first): 50%, 25%, 12% of the original.</summary>
    private static readonly float[] LodTargetRatios = { 0.5f, 0.25f, 0.12f };

    /// <summary>Counts what the import did, reported as a single log line.</summary>
    private sealed class ImportStats
    {
        public int Primitives;
        public int TexturedMaterials;
        public int PbrMaterials;
        public int TextureDecodeFailures;
        public bool SawVertexColors;
        public int LodLevels;

        /// <summary>Shared 1x1 white base texture for factor-only PBR materials, owned by the model's texture list.</summary>
        public GpuTexture? WhitePixel;
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

        var summary = $"glTF '{sourceName}': {stats.Primitives} primitive(s), {stats.TexturedMaterials} textured / {stats.Primitives - stats.TexturedMaterials} flat, {stats.PbrMaterials} PBR-shaded.";
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
        // Transforms are taken as authored so they agree with the vertices; only triangle winding is reversed.
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
        var tangents = primitive.GetVertexAccessor("TANGENT")?.AsVector4Array();
        var colors = primitive.GetVertexAccessor("COLOR_0") != null ? primitive.GetVertexAccessor("COLOR_0").AsColorArray() : null;
        if (colors != null)
            stats.SawVertexColors = true;
        if (!importVertexColors)
            colors = null; // COLOR_0 is shader data on FFXIV-derived models, not albedo, so nothing is tinted by default.
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
                colors != null && i < colors.Count ? colors[i] : new Vector4(1f, 1f, 1f, 1f),
                tangents != null && i < tangents.Count ? tangents[i] : default);
        }

        // glTF is counter-clockwise-front and this renderer is clockwise-front, so the winding is reversed here.
        var triangles = new List<uint>();
        foreach (var (a, b, c) in primitive.GetTriangleIndices())
        {
            triangles.Add((uint)a);
            triangles.Add((uint)c);
            triangles.Add((uint)b);
        }

        if (triangles.Count == 0)
            return;

        // Applied inside the loader so this path matches the game-model one; a no-op unless a flip is enabled.
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
    /// Builds and attaches a quadric-error LOD chain for a large primitive, skipping small meshes and leaving the
    /// mesh at full detail when decimation fails.
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
        GpuTexture? normalTexture = null;
        GpuTexture? ormTexture = null;
        var blend = BlendMode.Opaque;
        var cull = CullMode.Back;
        var metallic = 0f;
        var roughness = 1f;
        var normalScale = 0f;
        var ormMode = 0f;
        var emissive = Vector3.Zero;
        var alphaControl = 0f;
        var wantsPbr = false;

        if (gltfMaterial != null)
        {
            // Specular-glossiness models carry their color in the Diffuse channel.
            var baseColor = gltfMaterial.FindChannel("BaseColor") ?? gltfMaterial.FindChannel("Diffuse");
            if (gltfMaterial.FindChannel("SpecularGlossiness") != null)
                dropped.Add("specular-glossiness workflow (approximated as metallic-roughness)");

            if (baseColor.HasValue)
            {
                color = baseColor.Value.Color;
                if (baseColor.Value.TextureTransform != null)
                    dropped.Add("texture transforms");
                if (baseColor.Value.Texture != null)
                {
                    texture = ResolveTexture(baseColor.Value.Texture, textures, textureCache);
                    if (texture == null)
                        stats.TextureDecodeFailures++;
                }
            }

            var mr = gltfMaterial.FindChannel("MetallicRoughness");
            if (mr.HasValue)
            {
                // The spec defaults metallic to 1, so only an authored metallic engages PBR.
                metallic = ChannelFactor(mr.Value, "MetallicFactor", 1f, out var metallicIsDefault);
                roughness = ChannelFactor(mr.Value, "RoughnessFactor", 1f, out _);
                if (mr.Value.Texture != null)
                {
                    ormTexture = ResolveTexture(mr.Value.Texture, textures, textureCache);
                    if (ormTexture != null)
                    {
                        ormMode = 1f;
                        wantsPbr = true;
                    }
                    else
                    {
                        stats.TextureDecodeFailures++;
                    }
                }

                if (!metallicIsDefault && metallic > 0f)
                    wantsPbr = true;
            }

            var normal = gltfMaterial.FindChannel("Normal");
            if (normal is { Texture: not null })
            {
                normalTexture = ResolveTexture(normal.Value.Texture, textures, textureCache);
                if (normalTexture != null)
                {
                    normalScale = ChannelFactor(normal.Value, "NormalScale", 1f, out _);
                    if (normalScale <= 0f)
                        normalScale = 1f;
                    wantsPbr = true;
                }
                else
                {
                    stats.TextureDecodeFailures++;
                }
            }

            var occlusion = gltfMaterial.FindChannel("Occlusion");
            if (occlusion is { Texture: not null })
            {
                // The usual packing is one ORM image; a separate occlusion map has no texture slot left.
                if (ormTexture != null && mr.HasValue && ReferenceEquals(occlusion.Value.Texture, mr.Value.Texture))
                    ormMode = 2f;
                else
                    dropped.Add("separate occlusion maps");
            }

            var emissiveChannel = gltfMaterial.FindChannel("Emissive");
            if (emissiveChannel.HasValue)
            {
                var factor = emissiveChannel.Value.Color;
                emissive = new Vector3(factor.X, factor.Y, factor.Z) * ChannelFactor(emissiveChannel.Value, "EmissiveStrength", 1f, out _);
                if (emissiveChannel.Value.Texture != null)
                    dropped.Add("emissive textures (the factor still applies)");
                if (emissive != Vector3.Zero)
                    wantsPbr = true;
            }

            switch (gltfMaterial.Alpha)
            {
                case AlphaMode.BLEND:
                    blend = BlendMode.Premultiplied;
                    alphaControl = -1f;
                    break;
                case AlphaMode.MASK:
                    // A cutout is opaque with a kill threshold; spec default 0.5.
                    blend = BlendMode.Opaque;
                    alphaControl = gltfMaterial.AlphaCutoff;
                    wantsPbr = true;
                    break;
                default:
                    blend = BlendMode.Opaque;
                    alphaControl = 0f;
                    break;
            }

            if (gltfMaterial.DoubleSided)
                cull = CullMode.None;

            // The KHR unlit extension asks for exactly what the standard Unlit domain does.
            if (gltfMaterial.Unlit)
            {
                if (texture != null)
                    stats.TexturedMaterials++;
                return new Materials.Material
                {
                    Domain = MaterialDomain.Unlit,
                    Blend = blend,
                    Color = color,
                    Texture = texture,
                    Cull = cull,
                };
            }
        }

        if (texture != null)
            stats.TexturedMaterials++;

        if (wantsPbr && GltfPbrPipeline.EnsureRegistered())
        {
            stats.PbrMaterials++;
            return new Materials.Material
            {
                // Lit is the fallback look if the pipeline ever unregisters. An unbound base texture samples
                // black, so factor-only materials get the shared 1x1 white.
                Domain = MaterialDomain.Lit,
                CustomPipeline = GltfPbrPipeline.Name,
                Blend = blend,
                Color = color,
                Texture = texture ?? WhitePixel(textures, stats),
                AuxTexture0 = normalTexture,
                AuxTexture1 = ormTexture,
                Cull = cull,
                ShapeParams = new Vector4(metallic, roughness, normalScale, ormMode),
                SurfaceParams = new Vector4(emissive, alphaControl),
            };
        }

        if (wantsPbr)
            dropped.Add($"PBR shading ({GltfPbrPipeline.Unavailable ?? "pipeline unavailable"})");

        return new Materials.Material
        {
            Domain = MaterialDomain.Lit,
            Blend = blend,
            Color = color,
            Texture = texture,
            Cull = cull,
        };
    }

    /// <summary>Reads a channel factor by name; <paramref name="isDefault"/> says whether it was authored or is the spec default.</summary>
    private static float ChannelFactor(in MaterialChannel channel, string name, float fallback, out bool isDefault)
    {
        isDefault = true;
        foreach (var parameter in channel.Parameters)
        {
            if (parameter.Name != name)
                continue;

            isDefault = parameter.IsDefault;
            if (parameter.Value is IConvertible value)
                return System.Convert.ToSingle(value, System.Globalization.CultureInfo.InvariantCulture);
            break;
        }

        return fallback;
    }

    /// <summary>The import's shared 1x1 white texture, created on first use and owned by the model's texture list.</summary>
    private static GpuTexture WhitePixel(List<GpuTexture> textures, ImportStats stats)
    {
        if (stats.WhitePixel != null)
            return stats.WhitePixel;

        stats.WhitePixel = TextureLoader.FromRgba(stackalloc byte[] { 255, 255, 255, 255 }, 1, 1);
        textures.Add(stats.WhitePixel);
        return stats.WhitePixel;
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
                // Dalamud decodes the PNG/JPG bytes; blocking is fine, this runs on the thread pool.
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
