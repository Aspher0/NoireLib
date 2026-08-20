using NoireLib.Draw3D.Assets;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using TerraFX.Interop.DirectX;

namespace NoireLib.Draw3D.Core;

/// <summary>One item to draw this frame: a mesh (or dynamic-geometry range) with resolved material data and world transform.</summary>
internal struct DrawItem
{
    public Mesh? Mesh;
    public int DynStartIndex;
    public int DynIndexCount;
    public MaterialData Mat;
    public Vector4 Color;
    public Matrix4x4 World;
    public bool WritesPrivateDepth;
    public Vector3 BoundsCenter;
    public float BoundsRadius;
    public float EyeDistance;
    public IReadOnlyList<ExcludeVolume>? ExcludeVolumes; // ground-decal per-actor exclusion (null = none)
    public Vector4 OutlineColor; // selection outline colour (w > 0 = outlined)
    public float OutlineWidth;   // outline thickness in screen pixels
}

/// <summary>Per-frame constants, matching FrameCB in Common.hlsli exactly (240 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FrameCBData
{
    public Matrix4x4 ViewProj;
    public Matrix4x4 InvViewProj;
    public Vector4 EyePosTime;
    public Vector4 Viewport;
    public Vector4 DepthUv;
    public Vector4 DepthCal;
    public Vector4 Ambient;
    public Vector4 LightDirIntensity;
    public Vector4 LightColor;
    public Vector4 WorldHeightRegion; // xy = region min XZ (world), z = 1/regionSize, w = 1 when the height-map is valid
}

/// <summary>Per-object constants, matching ObjectCB in Common.hlsli exactly (224 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ObjectCBData
{
    public Matrix4x4 World;
    public Matrix4x4 InvWorld;
    public Vector4 BaseColor;
    public Vector4 Params0;
    public Vector4 Params1;
    public Vector4 Params2; // x = ground-decal projection mode (0 = all surfaces, 1 = highest only); y = box top world Y;
                            // z = outline reference footprint scale (0 = constant-thickness rim)
    public Vector4 OutlineColor; // ground-decal rim colour, straight alpha; alpha 0 = unset, so the rim uses BaseColor
    public Vector4 Params3;      // spare per-shader slot; G-buffer injection puts dye colour in rgb and strength in w
}

/// <summary>
/// A ground decal's per-actor exclusion volumes, matching ActorCB in Common.hlsli exactly. Each actor packs as
/// (worldX, worldZ, radius, unused): a horizontal gate the stencil silhouette then cuts, so the fourth slot is padding.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ActorCBData
{
    public uint ActorCount;
    public uint CharacterStencil; // game stencil value marking characters (0 = feature off); the exact silhouette source
    public uint Pad1, Pad2;
    public fixed float Actors[ScenePass.MaxActorVolumes * 4];
}

/// <summary>
/// The world pass: collects visible items from retained scenes and the immediate layer, sorts them into opaque, decal
/// and transparent buckets, batches identical runs into instanced draws, and renders into the offscreen premultiplied
/// scene target.
/// </summary>
internal sealed unsafe class ScenePass : IDisposable
{
    private const int MaxDynamicVertices = 65535; // 16-bit dynamic index budget per frame

    /// <summary>Max excluded-actor volumes carried to the decal shader per frame (matches MAX_DECAL_ACTORS in Common.hlsli).</summary>
    internal const int MaxActorVolumes = 64;

    private GpuBuffer? frameCb;
    private GpuBuffer? objectCb;
    private GpuBuffer? actorCb;
    private readonly DynamicRing instanceRing = new(D3D11_BIND_FLAG.D3D11_BIND_VERTEX_BUFFER, 4096 * 80, "instance");
    private readonly DynamicRing dynVertexRing = new(D3D11_BIND_FLAG.D3D11_BIND_VERTEX_BUFFER, 16384 * 48, "dynamic-vertex");
    private readonly DynamicRing dynIndexRing = new(D3D11_BIND_FLAG.D3D11_BIND_INDEX_BUFFER, 49152 * 2, "dynamic-index");

    private DrawItem[] items = new DrawItem[256];
    private ulong[] keys = new ulong[256];
    private int itemCount;
    private int sequence;
    private uint currentCharacterStencil; // the game stencil value marking characters, this Execute (0 = exclusion off)
    private FrustumPlanes frustum;
    private Vector3 eyePos;
    private bool collectingForMainPass;
    private long collectFrameId; // matched against a node's G-buffer-injection submission so it is not drawn twice

    // Distance/screen-size culling and LOD selection, captured once per BeginCollect. Applicable only on the main game
    // pass with a real projection scale: a render-to-texture view or the wholesale-VP fallback exposes no usable
    // vertical focal length, so those always draw full detail and never size-cull.
    private Draw3DPerformance.Snapshot perfSnapshot;
    private bool perfApplicable;
    private float projFocalY;   // frame.Proj.M22: NDC vertical units per view-space unit at unit depth
    private float halfViewportH;
    private bool hasOutlined;
    private float maxOutlineWidth;
    private bool lastPrivateDepthWritten;

    /// <summary>Whether any collected item this frame has a selection outline (drives the optional outline pass).</summary>
    public bool HasOutlinedItems => hasOutlined;

    /// <summary>Counts this frame's <see cref="DecalProjection.HighestOnly"/> ground decals, which gate the collision height-map pass.</summary>
    /// <returns>The number of top-surface decals collected this frame.</returns>
    public int CountTopSurfaceDecals()
    {
        var n = 0;
        for (var i = 0; i < itemCount; i++)
            if (IsTopSurfaceDecal(in items[i]))
                n++;
        return n;
    }

    /// <summary>
    /// Gets the highest box-top world Y across this frame's <see cref="DecalProjection.HighestOnly"/> decals, the
    /// ceiling <see cref="RenderWorldHeight"/> clips the height-map to so overhead geometry never masks the ground.
    /// </summary>
    /// <returns>The highest box-top world Y, or <see cref="float.NegativeInfinity"/> when there are no such decals.</returns>
    public float MaxTopSurfaceDecalBoxTopY()
    {
        var top = float.NegativeInfinity;
        for (var i = 0; i < itemCount; i++)
        {
            ref var item = ref items[i];
            if (IsTopSurfaceDecal(in item))
                top = MathF.Max(top, BoxTopY(in item.World));
        }
        return top;
    }

    /// <summary>Whether an item is a ground decal that paints only its column's topmost surface, and so reads the collision height-map.</summary>
    /// <param name="item">The collected item.</param>
    /// <returns>True when the item is a top-surface ground decal.</returns>
    private static bool IsTopSurfaceDecal(in DrawItem item)
        => item.Mat.Domain == MaterialDomain.GroundDecal && item.Mat.ProjectionMode > 0.5f;

    /// <summary>Computes the AABB-max Y of a decal's unit box under <paramref name="world"/> (row-vector convention).</summary>
    /// <param name="world">The decal's world matrix.</param>
    /// <returns>The world-space top Y of the transformed box.</returns>
    private static float BoxTopY(in Matrix4x4 world)
        => world.M42 + 0.5f * (MathF.Abs(world.M12) + MathF.Abs(world.M22) + MathF.Abs(world.M32));

    /// <summary>Whether the last <see cref="Execute"/> populated the private depth buffer, so the outline mask can GE-test it for 3D-object occlusion instead of falling back to world-only occlusion.</summary>
    public bool LastPrivateDepthWritten => lastPrivateDepthWritten;

    /// <summary>The largest outline width (screen pixels) collected this frame (the outline composite kernel size).</summary>
    public float MaxOutlineWidthPixels => maxOutlineWidth;

    /// <summary>Per-frame dynamic geometry, uploaded once at execute (immediate-layer ribbons, flat shapes).</summary>
    public readonly List<Vertex3D> DynVertices = new(4096);

    /// <summary>Per-frame dynamic indices (paired with <see cref="DynVertices"/>).</summary>
    public readonly List<ushort> DynIndices = new(8192);

    /// <summary>External textures with keyed mutexes referenced this frame (acquired/released around the pass).</summary>
    public readonly List<GpuTexture> KeyedTextures = new();

    private InstanceData[] instanceScratch = new InstanceData[256];

    // Object-CB change gating: every instanced-route draw writes the same CB content except the material params, so the
    // upload is skipped until those change. Any non-instanced write invalidates the cache. Render thread only.
    private bool objectCbCacheValid;
    private Vector4 objectCbParams0;
    private Vector4 objectCbParams1;
    private Vector4 objectCbParams2;

    /// <summary>Remaining dynamic-vertex budget this frame (immediate layer checks before writing shapes).</summary>
    public int DynamicVertexBudget => MaxDynamicVertices - DynVertices.Count;

    /// <summary>Begins collection for a frame or a render view, resetting the pooled lists and deriving the frustum.</summary>
    /// <param name="frame">The frame context to collect against.</param>
    /// <param name="mainPass">Whether this is the main game pass rather than a render-to-texture view.</param>
    public void BeginCollect(in FrameContext frame, bool mainPass)
    {
        itemCount = 0;
        sequence = 0;
        frustum = FrustumPlanes.FromViewProj(frame.ViewProj);
        eyePos = frame.EyePos;
        collectingForMainPass = mainPass;
        collectFrameId = frame.FrameId;
        hasOutlined = false;
        maxOutlineWidth = 0f;

        // Snapshot once so a mid-frame settings change never tears the pass.
        perfSnapshot = NoireDraw3D.Performance.Take();
        projFocalY = frame.Proj.M22;
        halfViewportH = frame.ViewportSize.Y * 0.5f;
        perfApplicable = mainPass && !frame.UsedFallbackCamera && projFocalY is > 0.1f and < 20f;
        if (mainPass)
        {
            DynVertices.Clear();
            DynIndices.Clear();
            KeyedTextures.Clear();
        }
    }

    /// <summary>Collects a retained scene's visible renderers under the graph lock, resolving world matrices through the dirty flags.</summary>
    /// <param name="scene">The scene to walk.</param>
    /// <param name="stats">The per-frame counters to accumulate into.</param>
    /// <param name="depthAvailable">Whether the game's scene depth is readable this frame.</param>
    public void AddScene(Scene3D scene, RenderStats stats, bool depthAvailable)
    {
        if (!scene.Visible)
            return;

        lock (Scene3D.GraphLock)
        {
            foreach (var root in scene.Roots)
                CollectNode(root, stats, depthAvailable);
        }
    }

    private void CollectNode(SceneNode node, RenderStats stats, bool depthAvailable)
    {
        if (!node.Visible || node.Destroyed)
            return;

        // Already submitted to the G-buffer injection this frame: drawing it here too would put the mesh on screen
        // twice, the unlit copy overwriting the lit one. Children were not submitted, so they keep drawing. Main pass
        // only, since a render-to-texture view draws into a target the injection never touches.
        if (collectingForMainPass && node.GameLitFrameId == collectFrameId)
        {
            foreach (var skipped in node.Children)
                CollectNode(skipped, stats, depthAvailable);

            return;
        }

        var renderer = node.Renderer;
        if (renderer != null)
        {
            var mesh = renderer.Mesh;
            var material = renderer.Material;
            if (mesh.IsDisposed)
            {
                stats.DisposedAssetDraws++;
            }
            else if (!MaterialData.TryFrom(material, out var mat))
            {
                stats.DisposedAssetDraws++;
            }
            else
            {
                var world = node.ResolveWorld();
                AddMeshItem(mesh, mat, material.Texture, world, renderer.Tint * material.Color, node.Layer, renderer.CastsIntoPrivateDepth, stats, depthAvailable, renderer.ExcludeVolumes, renderer.OutlineColor, renderer.OutlineWidthPixels);
            }
        }

        foreach (var child in node.Children)
            CollectNode(child, stats, depthAvailable);
    }

    /// <summary>Adds one mesh item, applying culling, the depth-unavailable policy, level-of-detail selection and sort-key derivation.</summary>
    /// <param name="mesh">The mesh to draw.</param>
    /// <param name="mat">The resolved material data.</param>
    /// <param name="texture">The material's texture, or null when untextured.</param>
    /// <param name="world">The item's world matrix.</param>
    /// <param name="color">The final tint, premultiplied by the material colour.</param>
    /// <param name="layer">The sort layer.</param>
    /// <param name="castsDepth">Whether the item writes the private depth buffer.</param>
    /// <param name="stats">The per-frame counters to accumulate into.</param>
    /// <param name="depthAvailable">Whether the game's scene depth is readable this frame.</param>
    /// <param name="excludeVolumes">Per-actor exclusion cylinders for a ground decal, or null.</param>
    /// <param name="outlineColor">The selection outline colour, with alpha above zero enabling the outline.</param>
    /// <param name="outlineWidth">The outline thickness in screen pixels.</param>
    public void AddMeshItem(Mesh mesh, in MaterialData mat, GpuTexture? texture, in Matrix4x4 world, Vector4 color, int layer, bool castsDepth, RenderStats stats, bool depthAvailable, IReadOnlyList<ExcludeVolume>? excludeVolumes = null, Vector4 outlineColor = default, float outlineWidth = 0f)
    {
        if (!depthAvailable && ShouldHideWithoutDepth(mat))
        {
            stats.CulledItems++;
            return;
        }

        var bounds = mesh.LocalBounds.Transform(world);
        // A ground decal's mesh bounds are its volume box, so the same frustum test applies.
        if (!frustum.Intersects(bounds))
        {
            stats.CulledItems++;
            return;
        }

        var distance = Vector3.Distance(bounds.Center, eyePos);

        // Bounds, sort and picking always use the full-resolution mesh; only the drawn mesh changes.
        var drawMesh = mesh;
        if (perfApplicable)
        {
            if (perfSnapshot.MaxDrawDistance > 0f && distance > perfSnapshot.MaxDrawDistance)
            {
                stats.CulledItems++;
                return;
            }

            var screenRadius = distance > 1e-3f ? bounds.Radius * projFocalY * halfViewportH / distance : float.MaxValue;

            // Outlined objects are exempt from the sub-pixel cull so a selection highlight never vanishes.
            if (perfSnapshot.MinScreenPixels > 0f && outlineColor.W <= 0f && screenRadius < perfSnapshot.MinScreenPixels)
            {
                stats.CulledItems++;
                return;
            }

            // Only imported models carry a LOD chain; ground decals never take one.
            if (mesh.LodCount > 0 && mat.Domain != MaterialDomain.GroundDecal)
                drawMesh = mesh.SelectLod(Draw3DPerformance.SelectLevel(screenRadius, mesh.LodCount, in perfSnapshot));
        }

        if (texture != null && texture.HasKeyedMutex && collectingForMainPass && !KeyedTextures.Contains(texture))
            KeyedTextures.Add(texture);

        // Only solid meshes are outlined; a ground decal's OutlineColor is consumed by its own shader instead.
        if (outlineColor.W > 0f && mat.Domain != MaterialDomain.GroundDecal)
        {
            hasOutlined = true;
            maxOutlineWidth = MathF.Max(maxOutlineWidth, outlineWidth);
        }

        Append(new DrawItem
        {
            Mesh = drawMesh,
            Mat = mat,
            Color = color,
            World = world,
            WritesPrivateDepth = mat.Bucket == 0 && castsDepth,
            BoundsCenter = bounds.Center,
            BoundsRadius = bounds.Radius,
            EyeDistance = distance,
            ExcludeVolumes = excludeVolumes,
            OutlineColor = outlineColor,
            OutlineWidth = outlineWidth,
        }, layer, distance);
        stats.VisibleItems++;
    }

    /// <summary>Adds a dynamic-geometry range already appended to <see cref="DynVertices"/> and <see cref="DynIndices"/>.</summary>
    /// <param name="startIndex">The first index of the range.</param>
    /// <param name="indexCount">The number of indices in the range.</param>
    /// <param name="mat">The resolved material data.</param>
    /// <param name="color">The tint.</param>
    /// <param name="world">The item's world matrix.</param>
    /// <param name="layer">The sort layer.</param>
    /// <param name="center">The world-space bounds centre.</param>
    /// <param name="radius">The world-space bounds radius.</param>
    /// <param name="stats">The per-frame counters to accumulate into.</param>
    /// <param name="depthAvailable">Whether the game's scene depth is readable this frame.</param>
    public void AddDynamicItem(int startIndex, int indexCount, in MaterialData mat, Vector4 color, in Matrix4x4 world, int layer, Vector3 center, float radius, RenderStats stats, bool depthAvailable)
    {
        if (indexCount == 0)
            return;

        if (!depthAvailable && ShouldHideWithoutDepth(mat))
        {
            stats.CulledItems++;
            return;
        }

        var distance = Vector3.Distance(center, eyePos);
        Append(new DrawItem
        {
            Mesh = null,
            DynStartIndex = startIndex,
            DynIndexCount = indexCount,
            Mat = mat,
            Color = color,
            World = world,
            BoundsCenter = center,
            BoundsRadius = radius,
            EyeDistance = distance,
        }, layer, distance);
        stats.VisibleItems++;
    }

    /// <summary>
    /// Decides, per nameplate rect, whether the plate is in front of or behind the Draw3D content covering it, writing
    /// UI visibility factors for the over-everything composite (1 = the plate reads on top,
    /// <paramref name="behindFactor"/> = a shape covers its letters). Runs only when the composite draws over the game
    /// UI; otherwise the game's own plate pass does the same job.
    /// </summary>
    /// <param name="frame">The frame context supplying the view-projection and viewport.</param>
    /// <param name="rects">Per-plate UV rects as (minX, minY, maxX, maxY).</param>
    /// <param name="plateDistances">Per-plate eye distances.</param>
    /// <param name="factors">Per-plate UI visibility factors, written by this call.</param>
    /// <param name="count">The number of plates in the arrays.</param>
    /// <param name="behindFactor">The visibility factor applied to a covered plate.</param>
    /// <param name="coveringItemFar">Optional per-plate far-surface distance of the covering item (0 = none).</param>
    public void ComputeRectOcclusion(in FrameContext frame, Vector4[] rects, float[] plateDistances, float[] factors, int count, float behindFactor, float[]? coveringItemFar = null)
    {
        // The fallback constants only matter under the wholesale-VP camera, whose Proj is identity.
        var gy = frame.Proj.M22 is > 0.1f and < 20f ? frame.Proj.M22 : 1.4f;
        var gx = frame.Proj.M11 is > 0.05f and < 20f ? frame.Proj.M11 : gy * (frame.ViewportSize.Y / MathF.Max(frame.ViewportSize.X, 1f));

        for (var r = 0; r < count; r++)
        {
            factors[r] = 1f;
            if (coveringItemFar != null)
                coveringItemFar[r] = 0f;
        }

        for (var i = 0; i < itemCount; i++)
        {
            ref var item = ref items[i];
            var clip = Vector4.Transform(new Vector4(item.BoundsCenter, 1f), frame.ViewProj);
            if (clip.W <= 0.05f)
                continue; // behind the camera, so it cannot cover a visible plate

            var uvX = clip.X / clip.W * 0.5f + 0.5f;
            var uvY = 0.5f - clip.Y / clip.W * 0.5f;
            var radiusU = item.BoundsRadius * gx / clip.W * 0.5f * 1.25f; // 1.25: conservative slack
            var radiusV = item.BoundsRadius * gy / clip.W * 0.5f * 1.25f;

            for (var r = 0; r < count; r++)
            {
                if (factors[r] != 1f)
                    continue; // already covered by a nearer item

                var rect = rects[r];
                var overlaps = uvX + radiusU >= rect.X && uvX - radiusU <= rect.Z
                            && uvY + radiusV >= rect.Y && uvY - radiusV <= rect.W;
                if (!overlaps)
                    continue;

                // Covered only when the plate sits behind the item's farthest possible surface; ties go to the letters.
                if (plateDistances[r] >= item.EyeDistance + item.BoundsRadius)
                {
                    factors[r] = behindFactor;
                    if (coveringItemFar != null)
                        coveringItemFar[r] = item.EyeDistance + item.BoundsRadius;
                }
            }
        }
    }

    private static bool ShouldHideWithoutDepth(in MaterialData mat)
        => mat.Domain == MaterialDomain.GroundDecal // nothing to project onto without depth
           || (mat.Depth == DepthMode.TestOnly && mat.WhenDepthUnavailable == DepthUnavailableBehavior.Hide);

    private void Append(in DrawItem item, int layer, float distance)
    {
        if (itemCount == items.Length)
        {
            Array.Resize(ref items, items.Length * 2);
            Array.Resize(ref keys, keys.Length * 2);
        }

        var bucket = item.Mat.Bucket;
        var depthQ = SortKey.QuantizeDistance(distance);
        var pipelineId = (byte)(((int)item.Mat.Domain << 2) | (item.Mat.Textured ? 1 : 0) | (item.Mat.CustomPipeline != null ? 2 : 0));
        var materialId = (ushort)(item.Mat.GetHashCode() & 0xFFFF);

        // Key composition per bucket: opaque is state-grouped (pipeline and material above depth, which is only an
        // early-z hint); decal is layer then creation order; transparent is strict back-to-front unless the material
        // opted into unordered batching.
        ulong key = bucket switch
        {
            0 => SortKey.MakeGrouped(0, layer, pipelineId, materialId, depthQ, sequence),
            1 => SortKey.Make(1, layer, 0, 0, 0, sequence, backToFront: false),
            _ => item.Mat.UnorderedBatching
                ? SortKey.MakeGrouped(2, layer, pipelineId, materialId, (ushort)~depthQ, sequence)
                : SortKey.Make(2, layer, depthQ, pipelineId, materialId, sequence, backToFront: true),
        };

        keys[itemCount] = key;
        items[itemCount] = item;
        itemCount++;
        sequence++;
    }

    /// <summary>
    /// Renders the collected items into the scene target. The caller must hold the StateGuard; every binding this
    /// method makes is its own and none are restored.
    /// </summary>
    /// <param name="device">The render device.</param>
    /// <param name="ctx">The immediate device context.</param>
    /// <param name="frame">The frame context.</param>
    /// <param name="sceneRt">The offscreen premultiplied scene target.</param>
    /// <param name="privateDepth">The pass-owned depth buffer used for object-to-object occlusion.</param>
    /// <param name="sceneDepthSrv">The game's scene depth, or null when unavailable.</param>
    /// <param name="worldHeightSrv">The top-down collision height-map, or null when the pass did not run.</param>
    /// <param name="sceneStencilSrv">The game's stencil plane, or null to disable silhouette-exact decal exclusion.</param>
    /// <param name="characterStencil">The game stencil value marking characters.</param>
    /// <param name="topSurfaceThreshold">The elevation band, in world units, for <see cref="DecalProjection.HighestOnly"/> decals.</param>
    /// <param name="worldHeightRegion">The height-map region as (minX, minZ, 1/size, valid).</param>
    /// <param name="depthCal">The game depth linearization constants.</param>
    /// <param name="shaders">The shader library.</param>
    /// <param name="cache">The pipeline state cache.</param>
    /// <param name="stats">The per-frame counters to accumulate into.</param>
    /// <param name="wireframe">Whether to rasterize meshes as wireframe.</param>
    /// <param name="lighting">The scene lighting constants.</param>
    public void Execute(
        RenderDevice device,
        ID3D11DeviceContext* ctx,
        in FrameContext frame,
        RenderTarget sceneRt,
        DepthTarget privateDepth,
        ID3D11ShaderResourceView* sceneDepthSrv,
        ID3D11ShaderResourceView* worldHeightSrv,
        ID3D11ShaderResourceView* sceneStencilSrv,
        uint characterStencil,
        float topSurfaceThreshold,
        Vector4 worldHeightRegion,
        Vector4 depthCal,
        ShaderLibrary shaders,
        StateCache cache,
        RenderStats stats,
        bool wireframe,
        Draw3DLighting lighting)
    {
        EnsureBuffers(device);
        currentCharacterStencil = sceneStencilSrv != null ? characterStencil : 0u; // 0 makes the decal skip the stencil test

        Array.Sort(keys, items, 0, itemCount);

        instanceRing.BeginFrame();

        uint dynVbOffset = 0, dynIbOffset = 0;
        var hasDynamic = collectingForMainPass && DynVertices.Count > 0 && DynIndices.Count > 0;
        if (hasDynamic)
        {
            dynVertexRing.BeginFrame();
            dynIndexRing.BeginFrame();
            var vSpan = CollectionsMarshal.AsSpan(DynVertices);
            var iSpan = CollectionsMarshal.AsSpan(DynIndices);
            fixed (Vertex3D* v = vSpan)
            {
                // Aligning to the vertex stride keeps the returned offset a whole number of vertices.
                if (!dynVertexRing.TryWrite(device, ctx, v, (uint)(vSpan.Length * sizeof(Vertex3D)), (uint)sizeof(Vertex3D), out dynVbOffset))
                    hasDynamic = false;
            }

            fixed (ushort* i = iSpan)
            {
                if (hasDynamic && !dynIndexRing.TryWrite(device, ctx, i, (uint)(iSpan.Length * sizeof(ushort)), 2, out dynIbOffset))
                    hasDynamic = false;
            }
        }

        // Matrices are transposed on upload to match HLSL's layout. DepthUv.zw carries this projection's z map
        // (deviceZ = z + w/clipW), which must match the reversed-Z column rebuilt in NoireDraw3D.RenderMainScene rather
        // than the game's exposed projection, so SceneWorldPos round-trips through InvViewProj exactly.
        var frameData = new FrameCBData
        {
            ViewProj = Matrix4x4.Transpose(frame.ViewProj),
            InvViewProj = Matrix4x4.Transpose(frame.InvViewProj),
            EyePosTime = new Vector4(frame.EyePos, frame.Time),
            // The render target size, not the display size, so DisplayUv stays a correct [0,1] UV at any supersample
            // factor; the game-depth sample then scales that UV by DepthUv into the game buffer.
            Viewport = new Vector4(sceneRt.Width, sceneRt.Height, 1f / sceneRt.Width, 1f / sceneRt.Height),
            DepthUv = new Vector4(frame.DepthUvScale.X, frame.DepthUvScale.Y, 0f, frame.NearPlane),
            // DepthCal.w is the top-surface elevation band for HighestOnly decals; 0 degrades them to AllSurfaces and
            // leaves the height-map at t2 unsampled.
            DepthCal = new Vector4(depthCal.X, depthCal.Y, depthCal.Z, worldHeightSrv != null ? topSurfaceThreshold : 0f),
            Ambient = new Vector4(lighting.AmbientColor, lighting.AmbientIntensity),
            LightDirIntensity = new Vector4(Vector3.Normalize(lighting.LightDirection), lighting.LightIntensity),
            LightColor = new Vector4(lighting.LightColor, 0f),
            WorldHeightRegion = worldHeightSrv != null ? worldHeightRegion : Vector4.Zero,
        };
        frameCb!.UpdateConstant(ctx, in frameData);

        var hasOpaque = false;
        var hasDepthWrites = false;
        for (var i = 0; i < itemCount; i++)
        {
            if (items[i].Mat.Bucket == 0)
            {
                hasOpaque = true;
                hasDepthWrites |= items[i].WritesPrivateDepth;
            }
        }

        // The private depth is bound only on frames with opaque content, so a frame without any never treats the
        // previous frame's leftover depth as valid.
        var dsv = (ID3D11DepthStencilView*)null;
        if (hasOpaque && privateDepth.EnsureSize(device, sceneRt.Width, sceneRt.Height))
            dsv = privateDepth.Dsv;
        lastPrivateDepthWritten = collectingForMainPass && dsv != null;

        var rtv = sceneRt.Rtv;
        ctx->OMSetRenderTargets(1, &rtv, dsv);

        var viewport = new D3D11_VIEWPORT { Width = sceneRt.Width, Height = sceneRt.Height, MaxDepth = 1f };
        ctx->RSSetViewports(1, &viewport);
        var scissor = new TerraFX.Interop.Windows.RECT { right = (int)sceneRt.Width, bottom = (int)sceneRt.Height };
        ctx->RSSetScissorRects(1, &scissor);

        var clear = stackalloc float[4];
        ctx->ClearRenderTargetView(rtv, clear);
        if (dsv != null)
            ctx->ClearDepthStencilView(dsv, (uint)D3D11_CLEAR_FLAG.D3D11_CLEAR_DEPTH, 0.0f, 0); // reversed-Z "far" = 0

        ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        var cb = frameCb.Buffer;
        ctx->VSSetConstantBuffers(0, 1, &cb);
        ctx->PSSetConstantBuffers(0, 1, &cb);
        var ocb = objectCb!.Buffer;
        ctx->VSSetConstantBuffers(1, 1, &ocb);
        ctx->PSSetConstantBuffers(1, 1, &ocb);
        var acb = actorCb!.Buffer;
        ctx->PSSetConstantBuffers(2, 1, &acb); // b2: per-decal actor exclusion volumes (decal ExcludeVolumes)
        ctx->PSSetShaderResources(2, 1, &worldHeightSrv); // t2: top-down collision height-map for DecalProjection.HighestOnly (null = off)
        ctx->PSSetShaderResources(3, 1, &sceneStencilSrv); // t3: game stencil plane for silhouette-exact decal exclusion (null = off)
        var pointClamp = cache.GetSampler(device, SamplerKey.PointClamp);
        ctx->PSSetSamplers(0, 1, &pointClamp);
        var linearWrap = cache.GetSampler(device, SamplerKey.LinearWrap);
        ctx->PSSetSamplers(1, 1, &linearWrap);

        var blendFactor = stackalloc float[4];

        // Shadow state to skip redundant binds.
        ID3D11BlendState* curBlend = null;
        ID3D11DepthStencilState* curDepthState = null;
        ID3D11RasterizerState* curRaster = null;
        ShaderPipeline? curPipeline = null;
        nint curTexture = -1;
        nint curAux0 = -1;
        nint curAux1 = -1;
        nint curDepthSrv = -1;
        Mesh? curMesh = null;
        var dynBound = false;

        foreach (var tex in KeyedTextures)
            tex.AcquireSync();

        objectCbCacheValid = false; // other passes have written the CB since the last Execute

        var i0 = 0;
        while (i0 < itemCount)
        {
            ref var item = ref items[i0];
            var bucket = item.Mat.Bucket;

            // A decal's box is the volume its SDF is evaluated in, not geometry, so rasterizing its triangles as lines
            // shades noise. Scene3D.TraceDecalShapes traces the real shape into the immediate layer instead.
            if (wireframe && item.Mat.Domain == MaterialDomain.GroundDecal)
            {
                i0++; // decals never instance, so the run is always 1 here
                continue;
            }

            var batchable = item.Mesh != null && item.Mat.Domain != MaterialDomain.GroundDecal && item.Mat.CustomPipeline == null;
            var run = 1;
            if (batchable)
            {
                while (i0 + run < itemCount
                       && ReferenceEquals(items[i0 + run].Mesh, item.Mesh)
                       && items[i0 + run].Mat.Equals(item.Mat)
                       && items[i0 + run].WritesPrivateDepth == item.WritesPrivateDepth)
                    run++;
            }

            // With BatchedObjectConstants on, singles ride the instanced route too: world and tint travel in the
            // per-instance stream, so the object CB stops changing per draw and its upload is gated below.
            var instanced = run > 1 || (batchable && perfSnapshot.BatchedObjectConstants);

            var pipeline = item.Mat.CustomPipeline != null
                ? shaders.GetCustom(device, item.Mat.CustomPipeline)
                : shaders.GetStandard(device, item.Mat.Domain, item.Mat.Textured, instanced, opaqueDomain: bucket == 0);
            if (pipeline == null)
            {
                i0 += run;
                continue; // pipeline self-disabled, so it renders nothing
            }

            var blendKey = bucket == 0 ? BlendKey.Opaque : item.Mat.Blend == BlendMode.Additive ? BlendKey.Additive : BlendKey.Premultiplied;
            var blend = cache.GetBlend(device, blendKey);
            if (blend != curBlend)
            {
                ctx->OMSetBlendState(blend, blendFactor, 0xFFFFFFFF);
                curBlend = blend;
            }

            var depthKey = bucket switch
            {
                0 => item.WritesPrivateDepth ? DepthKey.WriteGE : DepthKey.ReadGE,
                // Decals test but never write the private depth, via the ground device-z their pixel shader emits, so
                // nearer opaque objects occlude them.
                1 => DepthKey.ReadGE,
                _ => hasDepthWrites ? DepthKey.ReadGE : DepthKey.Disabled,
            };
            // A transparent item opting out of the object-to-object depth test stays in front of other 3D objects.
            // DepthMode.Ignore also drops the world-depth SRV below, so it draws over everything; DepthMode.WorldOnly
            // keeps it, so a wall still hides the item while a nearer 3D object does not.
            if (bucket == 2 && item.Mat.Depth is DepthMode.Ignore or DepthMode.WorldOnly)
                depthKey = DepthKey.Disabled;
            if (dsv == null)
                depthKey = DepthKey.Disabled;
            var depthState = cache.GetDepth(device, depthKey);
            if (depthState != curDepthState)
            {
                ctx->OMSetDepthStencilState(depthState, 0);
                curDepthState = depthState;
            }

            var rasterKey = wireframe ? RasterKey.Wire : item.Mat.Cull switch
            {
                CullMode.Front => RasterKey.CullFront,
                CullMode.None => RasterKey.TwoSided,
                _ => RasterKey.CullBack,
            };
            var raster = cache.GetRaster(device, rasterKey);
            if (raster != curRaster)
            {
                ctx->RSSetState(raster);
                curRaster = raster;
            }

            if (!ReferenceEquals(pipeline, curPipeline))
            {
                ctx->IASetInputLayout(pipeline.Layout);
                ctx->VSSetShader(pipeline.Vs, null, 0);
                ctx->PSSetShader(pipeline.Ps, null, 0);
                curPipeline = pipeline;
                curMesh = null;
                dynBound = false;
            }

            // Null for DepthMode.Ignore materials: sampling a null SRV returns 0, which reads as fully visible.
            var wantDepthSrv = item.Mat.Depth == DepthMode.Ignore && item.Mat.Domain != MaterialDomain.GroundDecal ? null : sceneDepthSrv;
            if ((nint)wantDepthSrv != curDepthSrv)
            {
                ctx->PSSetShaderResources(0, 1, &wantDepthSrv);
                curDepthSrv = (nint)wantDepthSrv;
            }

            if (item.Mat.Textured && item.Mat.TexSrv != curTexture)
            {
                var texSrv = (ID3D11ShaderResourceView*)item.Mat.TexSrv;
                ctx->PSSetShaderResources(1, 1, &texSrv);
                curTexture = item.Mat.TexSrv;
            }

            // Auxiliary textures for custom pipelines, unbound when the material carries none so a pipeline that
            // samples one never reads what the previous draw left in the slot.
            if (item.Mat.AuxSrv0 != curAux0)
            {
                var auxSrv = (ID3D11ShaderResourceView*)item.Mat.AuxSrv0;
                ctx->PSSetShaderResources(4, 1, &auxSrv);
                curAux0 = item.Mat.AuxSrv0;
            }

            if (item.Mat.AuxSrv1 != curAux1)
            {
                var auxSrv = (ID3D11ShaderResourceView*)item.Mat.AuxSrv1;
                ctx->PSSetShaderResources(5, 1, &auxSrv);
                curAux1 = item.Mat.AuxSrv1;
            }

            uint indexCount;
            int startIndex, baseVertex;
            if (item.Mesh != null)
            {
                if (!ReferenceEquals(item.Mesh, curMesh))
                {
                    var vb = item.Mesh.Vb;
                    if (vb == null)
                    {
                        stats.DisposedAssetDraws++;
                        i0 += run;
                        continue;
                    }

                    uint stride = (uint)sizeof(Vertex3D), offset = 0;
                    ctx->IASetVertexBuffers(0, 1, &vb, &stride, &offset);
                    ctx->IASetIndexBuffer(item.Mesh.Ib, item.Mesh.IndexFormat, 0);
                    curMesh = item.Mesh;
                    dynBound = false;
                }

                indexCount = (uint)item.Mesh.IndexCount;
                startIndex = 0;
                baseVertex = 0;
            }
            else
            {
                if (!hasDynamic)
                {
                    i0 += run;
                    continue;
                }

                if (!dynBound)
                {
                    var vb = dynVertexRing.Buffer;
                    uint stride = (uint)sizeof(Vertex3D);
                    ctx->IASetVertexBuffers(0, 1, &vb, &stride, &dynVbOffset);
                    ctx->IASetIndexBuffer(dynIndexRing.Buffer, DXGI_FORMAT.DXGI_FORMAT_R16_UINT, dynIbOffset);
                    curMesh = null;
                    dynBound = true;
                }

                indexCount = (uint)item.DynIndexCount;
                startIndex = item.DynStartIndex;
                baseVertex = 0;
            }

            if (instanced)
            {
                if (instanceScratch.Length < run)
                    Array.Resize(ref instanceScratch, Math.Max(run, instanceScratch.Length * 2));
                for (var k = 0; k < run; k++)
                    instanceScratch[k] = InstanceData.From(in items[i0 + k].World, items[i0 + k].Color);

                uint instOffset;
                fixed (InstanceData* inst = instanceScratch)
                {
                    if (!instanceRing.TryWrite(device, ctx, inst, (uint)(run * sizeof(InstanceData)), (uint)sizeof(InstanceData), out instOffset))
                    {
                        i0 += run;
                        continue;
                    }
                }

                var instVb = instanceRing.Buffer;
                uint instStride = (uint)sizeof(InstanceData);
                ctx->IASetVertexBuffers(1, 1, &instVb, &instStride, &instOffset);

                // Every instanced-route draw writes an identical CB apart from the material params, so the upload is
                // skipped while they repeat.
                var params2 = item.Mat.SurfaceParams;
                if (!objectCbCacheValid || item.Mat.Params0 != objectCbParams0 || item.Mat.Params1 != objectCbParams1 || params2 != objectCbParams2)
                {
                    var objData = new ObjectCBData
                    {
                        World = Matrix4x4.Identity,
                        InvWorld = Matrix4x4.Identity,
                        BaseColor = new Vector4(1f, 1f, 1f, 1f),
                        Params0 = item.Mat.Params0,
                        Params1 = item.Mat.Params1,
                        Params2 = params2,
                    };
                    objectCb.UpdateConstant(ctx, in objData);
                    stats.ObjectCbUpdates++;
                    objectCbCacheValid = true;
                    objectCbParams0 = item.Mat.Params0;
                    objectCbParams1 = item.Mat.Params1;
                    objectCbParams2 = params2;
                }

                ctx->DrawIndexedInstanced(indexCount, (uint)run, (uint)startIndex, baseVertex, 0);
                stats.DrawCalls++;
                stats.Batches++;
                stats.Instances += run;
                stats.Triangles += (int)(indexCount / 3) * run;
            }
            else
            {
                Matrix4x4 invWorld = Matrix4x4.Identity;
                if (item.Mat.Domain == MaterialDomain.GroundDecal && !Matrix4x4.Invert(item.World, out invWorld))
                    invWorld = Matrix4x4.Identity;

                var objData = new ObjectCBData
                {
                    World = Matrix4x4.Transpose(item.World),
                    InvWorld = Matrix4x4.Transpose(invWorld),
                    BaseColor = item.Color,
                    Params0 = item.Mat.Params0,
                    Params1 = item.Mat.Params1,
                    // Decals need this register for projection data, so their own values win: x = projection mode,
                    // y = box top world Y (the height-map's vertical search bound), z = outline reference footprint
                    // scale. Every other material passes the caller's surface parameters straight through.
                    Params2 = item.Mat.Domain == MaterialDomain.GroundDecal
                        ? new Vector4(item.Mat.ProjectionMode, BoxTopY(in item.World), item.Mat.OutlineScaleRef, 0f)
                        : item.Mat.SurfaceParams,
                    OutlineColor = item.Mat.DecalOutlineColor,
                };
                objectCb.UpdateConstant(ctx, in objData);
                stats.ObjectCbUpdates++;
                objectCbCacheValid = false; // a real world matrix now sits in the CB

                // Upload (or clear) the exclusion list per decal so each cuts only around the actors it was given.
                if (item.Mat.Domain == MaterialDomain.GroundDecal)
                    UploadActorVolumes(ctx, item.ExcludeVolumes);

                ctx->DrawIndexed(indexCount, (uint)startIndex, baseVertex);
                stats.DrawCalls++;
                stats.Batches++;
                stats.Triangles += (int)(indexCount / 3);
            }

            i0 += run;
        }

        foreach (var tex in KeyedTextures)
            tex.ReleaseSync();
    }

    /// <summary>
    /// Re-rasterizes this frame's opaque, depth-casting mesh items into the game's scene depth, colourless and
    /// greater-equal tested, so <see cref="Enums.NameplateOcclusion.DepthAware"/> plates are occluded by them. Must run
    /// right after <see cref="Execute"/>, which leaves the collected items sorted; the caller owns the StateGuard.
    /// </summary>
    /// <param name="device">The render device.</param>
    /// <param name="ctx">The immediate device context.</param>
    /// <param name="frame">The frame context.</param>
    /// <param name="externalDsv">The game's scene depth-stencil view to write into.</param>
    /// <param name="viewportWidth">The target width in pixels.</param>
    /// <param name="viewportHeight">The target height in pixels.</param>
    /// <param name="shaders">The shader library.</param>
    /// <param name="cache">The pipeline state cache.</param>
    /// <param name="stats">The per-frame counters to accumulate into.</param>
    public void ProjectOpaqueDepth(
        RenderDevice device,
        ID3D11DeviceContext* ctx,
        in FrameContext frame,
        ID3D11DepthStencilView* externalDsv,
        uint viewportWidth,
        uint viewportHeight,
        ShaderLibrary shaders,
        StateCache cache,
        RenderStats stats)
    {
        if (externalDsv == null || viewportWidth == 0 || viewportHeight == 0 || itemCount == 0)
            return;

        EnsureBuffers(device);

        // Re-upload the frame VP, since the composite rebound the constant buffers after Execute. The rebuilt
        // reversed-Z column makes SV_Position.z = near/clipW, directly comparable to the game's depth buffer.
        var frameData = new FrameCBData
        {
            ViewProj = Matrix4x4.Transpose(frame.ViewProj),
            InvViewProj = Matrix4x4.Transpose(frame.InvViewProj),
            EyePosTime = new Vector4(frame.EyePos, frame.Time),
            Viewport = new Vector4(frame.ViewportSize.X, frame.ViewportSize.Y, 1f / frame.ViewportSize.X, 1f / frame.ViewportSize.Y),
            DepthUv = new Vector4(frame.DepthUvScale.X, frame.DepthUvScale.Y, 0f, frame.NearPlane),
            DepthCal = Vector4.Zero,
            Ambient = Vector4.Zero,
            LightDirIntensity = new Vector4(0f, 1f, 0f, 0f),
            LightColor = Vector4.Zero,
        };
        frameCb!.UpdateConstant(ctx, in frameData);

        // Where the world is nearer the greater-equal test fails and the world keeps the buffer; where the item is
        // nearer it writes its own depth, and the later nameplate pass is occluded by it.
        ctx->OMSetRenderTargets(0, null, externalDsv);

        var viewport = new D3D11_VIEWPORT { Width = viewportWidth, Height = viewportHeight, MaxDepth = 1f };
        ctx->RSSetViewports(1, &viewport);
        var scissor = new TerraFX.Interop.Windows.RECT { right = (int)viewportWidth, bottom = (int)viewportHeight };
        ctx->RSSetScissorRects(1, &scissor);

        ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        var cb = frameCb.Buffer;
        ctx->VSSetConstantBuffers(0, 1, &cb);
        var ocb = objectCb!.Buffer;
        ctx->VSSetConstantBuffers(1, 1, &ocb);

        var blendFactor = stackalloc float[4];
        ctx->OMSetBlendState(cache.GetBlend(device, BlendKey.Opaque), blendFactor, 0xFFFFFFFF);
        ctx->OMSetDepthStencilState(cache.GetDepth(device, DepthKey.WriteGE), 0);
        ctx->PSSetShader(null, null, 0);

        ID3D11RasterizerState* curRaster = null;
        ShaderPipeline? curPipeline = null;
        Mesh? curMesh = null;

        for (var i = 0; i < itemCount; i++)
        {
            ref var item = ref items[i];
            if (item.Mat.Bucket != 0 || !item.WritesPrivateDepth || item.Mesh == null)
                continue;

            var vb = item.Mesh.Vb;
            if (vb == null)
                continue;

            // Every opaque vertex shader emits SV_Position from World*ViewProj, so one unlit pipeline serves all.
            var pipeline = shaders.GetStandard(device, MaterialDomain.Unlit, textured: false, instanced: false, opaqueDomain: true);
            if (pipeline == null)
                return;

            if (!ReferenceEquals(pipeline, curPipeline))
            {
                ctx->IASetInputLayout(pipeline.Layout);
                ctx->VSSetShader(pipeline.Vs, null, 0);
                curPipeline = pipeline;
                curMesh = null;
            }

            var rasterKey = item.Mat.Cull switch
            {
                CullMode.Front => RasterKey.CullFront,
                CullMode.None => RasterKey.TwoSided,
                _ => RasterKey.CullBack,
            };
            var raster = cache.GetRaster(device, rasterKey);
            if (raster != curRaster)
            {
                ctx->RSSetState(raster);
                curRaster = raster;
            }

            if (!ReferenceEquals(item.Mesh, curMesh))
            {
                uint stride = (uint)sizeof(Vertex3D), offset = 0;
                ctx->IASetVertexBuffers(0, 1, &vb, &stride, &offset);
                ctx->IASetIndexBuffer(item.Mesh.Ib, item.Mesh.IndexFormat, 0);
                curMesh = item.Mesh;
            }

            var objData = new ObjectCBData
            {
                World = Matrix4x4.Transpose(item.World),
                InvWorld = Matrix4x4.Identity,
                BaseColor = item.Color,
                Params0 = item.Mat.Params0,
                Params1 = item.Mat.Params1,
                Params2 = item.Mat.SurfaceParams,
            };
            objectCb.UpdateConstant(ctx, in objData);

            ctx->DrawIndexed((uint)item.Mesh.IndexCount, 0, 0);
            stats.DrawCalls++;
        }
    }

    /// <summary>
    /// Renders the collision-world mesh top-down into an R32F height-map, each texel holding the highest collision Y in
    /// its XZ column up to <paramref name="heightCeiling"/>, which a <see cref="DecalProjection.HighestOnly"/> decal
    /// samples. Standalone in its target and states, so it runs before <see cref="Execute"/>. On false the caller must
    /// leave the height-map SRV unbound, since the target is cleared only on the drawing path and binding it after a
    /// bail feeds the decal uninitialized heights instead of degrading to <c>AllSurfaces</c>.
    /// </summary>
    /// <param name="device">The render device.</param>
    /// <param name="ctx">The immediate device context.</param>
    /// <param name="collisionMesh">The cached collision-world mesh, with vertices relative to <paramref name="meshCenter"/>.</param>
    /// <param name="meshCenter">The world-space origin the mesh vertices are relative to.</param>
    /// <param name="heightMatrix">The affine world-XZ-to-clip map, matching <c>WorldHeightRegion</c>.</param>
    /// <param name="heightCeiling">The world Y above which collision is discarded.</param>
    /// <param name="target">The R32F height-map target.</param>
    /// <param name="shaders">The shader library.</param>
    /// <param name="cache">The pipeline state cache.</param>
    /// <param name="stats">The per-frame counters to accumulate into.</param>
    /// <returns>False when the map could not be drawn (no mesh, no target, or a self-disabled pipeline).</returns>
    public bool RenderWorldHeight(
        RenderDevice device,
        ID3D11DeviceContext* ctx,
        Mesh collisionMesh,
        Vector3 meshCenter,
        Matrix4x4 heightMatrix,
        float heightCeiling,
        RenderTarget target,
        ShaderLibrary shaders,
        StateCache cache,
        RenderStats stats)
    {
        if (collisionMesh == null || target.Rtv == null || target.Width == 0 || target.Height == 0)
            return false;

        var vb = collisionMesh.Vb;
        if (vb == null || collisionMesh.IndexCount == 0)
            return false;

        var pipeline = shaders.GetWorldHeight(device);
        if (pipeline == null)
            return false;

        EnsureBuffers(device);

        var frameData = new FrameCBData
        {
            ViewProj = Matrix4x4.Transpose(heightMatrix), // affine XZ->clip; the VS does mul(wp, ViewProj)
            DepthCal = new Vector4(heightCeiling, 0f, 0f, 0f), // x = ceiling: the pixel shader discards collision above it
        };
        frameCb!.UpdateConstant(ctx, in frameData);

        var objData = new ObjectCBData
        {
            World = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(meshCenter)), // verts are relative to the region centre
            InvWorld = Matrix4x4.Identity,
            BaseColor = Vector4.One,
        };
        objectCb!.UpdateConstant(ctx, in objData);

        var rtv = target.Rtv;
        ctx->OMSetRenderTargets(1, &rtv, null);
        var clear = stackalloc float[4] { -1e30f, -1e30f, -1e30f, -1e30f }; // MAX-blend baseline: below any real world Y
        ctx->ClearRenderTargetView(rtv, clear);

        var viewport = new D3D11_VIEWPORT { Width = target.Width, Height = target.Height, MaxDepth = 1f };
        ctx->RSSetViewports(1, &viewport);
        var scissor = new TerraFX.Interop.Windows.RECT { right = (int)target.Width, bottom = (int)target.Height };
        ctx->RSSetScissorRects(1, &scissor);

        ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        var cb = frameCb.Buffer;
        ctx->VSSetConstantBuffers(0, 1, &cb);
        // b0 must reach the pixel shader too, which reads the ceiling from DepthCal.x. StateGuard has already restored
        // the game's buffers into the PS slots, so without this bind the shader reads out of range, D3D returns 0, and
        // the ceiling test discards everything above world Y 0.
        ctx->PSSetConstantBuffers(0, 1, &cb);
        var ocb = objectCb.Buffer;
        ctx->VSSetConstantBuffers(1, 1, &ocb);

        ctx->IASetInputLayout(pipeline.Layout);
        ctx->VSSetShader(pipeline.Vs, null, 0);
        ctx->PSSetShader(pipeline.Ps, null, 0);

        var blendFactor = stackalloc float[4];
        ctx->OMSetBlendState(cache.GetBlend(device, BlendKey.Max), blendFactor, 0xFFFFFFFF); // keep the highest Y per texel
        ctx->OMSetDepthStencilState(cache.GetDepth(device, DepthKey.Disabled), 0);
        ctx->RSSetState(cache.GetRaster(device, RasterKey.TwoSided)); // collision winding is arbitrary

        uint stride = (uint)sizeof(Vertex3D), offset = 0;
        ctx->IASetVertexBuffers(0, 1, &vb, &stride, &offset);
        ctx->IASetIndexBuffer(collisionMesh.Ib, collisionMesh.IndexFormat, 0);
        ctx->DrawIndexed((uint)collisionMesh.IndexCount, 0, 0);
        stats.DrawCalls++;
        return true;
    }

    /// <summary>
    /// Draws the outlined items into a silhouette mask and a world-visibility target, reusing the collected and sorted
    /// items. The mask holds each object's full silhouette with no depth test, so the composite outlines the whole
    /// object rather than its unoccluded fragments only, and the visibility target lets the composite hide that outline
    /// where the silhouette sits behind the world. Must run right after <see cref="Execute"/>, while the private depth
    /// still holds this frame's scene. Fail-soft.
    /// </summary>
    /// <param name="device">The render device.</param>
    /// <param name="ctx">The immediate device context.</param>
    /// <param name="frame">The frame context.</param>
    /// <param name="maskRt">The silhouette target, rgb = outline colour and a = coverage.</param>
    /// <param name="visRt">The visibility target, r = whether the silhouette pixel is in front of the game world.</param>
    /// <param name="privateDepth">The pass-owned depth buffer, bound read-only for the decal test.</param>
    /// <param name="privateDepthValid">Whether <paramref name="privateDepth"/> holds this frame's scene.</param>
    /// <param name="sceneDepthSrv">The game's scene depth, or null when unavailable.</param>
    /// <param name="depthCal">The game depth linearization constants.</param>
    /// <param name="shaders">The shader library.</param>
    /// <param name="cache">The pipeline state cache.</param>
    /// <param name="stats">The per-frame counters to accumulate into.</param>
    public void RenderOutlineMask(
        RenderDevice device,
        ID3D11DeviceContext* ctx,
        in FrameContext frame,
        RenderTarget maskRt,
        RenderTarget visRt,
        DepthTarget privateDepth,
        bool privateDepthValid,
        ID3D11ShaderResourceView* sceneDepthSrv,
        Vector4 depthCal,
        ShaderLibrary shaders,
        StateCache cache,
        RenderStats stats)
    {
        if (!hasOutlined || itemCount == 0 || maskRt.Rtv == null || visRt.Rtv == null)
            return;

        EnsureBuffers(device);

        // Re-upload the frame constants, since the composite may have rebound b0. Matches Execute's mapping.
        var frameData = new FrameCBData
        {
            ViewProj = Matrix4x4.Transpose(frame.ViewProj),
            InvViewProj = Matrix4x4.Transpose(frame.InvViewProj),
            EyePosTime = new Vector4(frame.EyePos, frame.Time),
            Viewport = new Vector4(maskRt.Width, maskRt.Height, 1f / maskRt.Width, 1f / maskRt.Height), // render size (supersample-aware)
            DepthUv = new Vector4(frame.DepthUvScale.X, frame.DepthUvScale.Y, 0f, frame.NearPlane),
            DepthCal = depthCal,
            Ambient = Vector4.Zero,
            LightDirIntensity = new Vector4(0f, 1f, 0f, 0f),
            LightColor = Vector4.Zero,
        };
        frameCb!.UpdateConstant(ctx, in frameData);

        // The private depth is never cleared here, so this frame's scene depth survives for later passes.
        var dsv = privateDepthValid ? privateDepth.Dsv : null;
        var rtvs = stackalloc ID3D11RenderTargetView*[2] { maskRt.Rtv, visRt.Rtv };
        ctx->OMSetRenderTargets(2, rtvs, dsv);

        var viewport = new D3D11_VIEWPORT { Width = maskRt.Width, Height = maskRt.Height, MaxDepth = 1f };
        ctx->RSSetViewports(1, &viewport);
        var scissor = new TerraFX.Interop.Windows.RECT { right = (int)maskRt.Width, bottom = (int)maskRt.Height };
        ctx->RSSetScissorRects(1, &scissor);

        var clear = stackalloc float[4];
        ctx->ClearRenderTargetView(maskRt.Rtv, clear); // colour targets only; the depth is read, never cleared
        ctx->ClearRenderTargetView(visRt.Rtv, clear);

        ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        var cb = frameCb.Buffer;
        ctx->VSSetConstantBuffers(0, 1, &cb);
        ctx->PSSetConstantBuffers(0, 1, &cb);
        var ocb = objectCb!.Buffer;
        ctx->VSSetConstantBuffers(1, 1, &ocb);
        ctx->PSSetConstantBuffers(1, 1, &ocb);
        var acb = actorCb!.Buffer;
        ctx->PSSetConstantBuffers(2, 1, &acb);
        var pointClamp = cache.GetSampler(device, SamplerKey.PointClamp);
        ctx->PSSetSamplers(0, 1, &pointClamp);

        var blendFactor = stackalloc float[4];
        ctx->OMSetBlendState(cache.GetBlend(device, BlendKey.Opaque), blendFactor, 0xFFFFFFFF); // overwrite: mask stores (colour, coverage)

        ID3D11RasterizerState* curRaster = null;
        ID3D11DepthStencilState* curDepthState = null;
        Mesh? curMesh = null;
        nint curDepthSrv = -1;

        var pipeline = shaders.GetOutlineMaskMesh(device);
        if (pipeline != null)
        {
            ctx->IASetInputLayout(pipeline.Layout);
            ctx->VSSetShader(pipeline.Vs, null, 0);
            ctx->PSSetShader(pipeline.Ps, null, 0);
        }

        for (var i = 0; pipeline != null && i < itemCount; i++)
        {
            ref var item = ref items[i];
            // A ground decal's projected footprint has no meaningful screen silhouette, and immediate markers have no
            // outline state, so only solid meshes reach the mask.
            if (item.OutlineColor.W <= 0f || item.Mesh == null || item.Mat.Domain == MaterialDomain.GroundDecal)
                continue;

            // No depth test: occlusion is applied later from the visibility target, so the outline stays whole instead
            // of fragmenting behind a fence.
            var depthState = cache.GetDepth(device, DepthKey.Disabled);
            if (depthState != curDepthState)
            {
                ctx->OMSetDepthStencilState(depthState, 0);
                curDepthState = depthState;
            }

            // Null on an x-ray mesh, so the visibility test reports visible everywhere and its outline is never hidden.
            var wantDepthSrv = item.Mat.Depth == DepthMode.Ignore ? null : sceneDepthSrv;
            if ((nint)wantDepthSrv != curDepthSrv)
            {
                ctx->PSSetShaderResources(0, 1, &wantDepthSrv);
                curDepthSrv = (nint)wantDepthSrv;
            }

            var rasterKey = item.Mat.Cull switch
            {
                CullMode.Front => RasterKey.CullFront,
                CullMode.None => RasterKey.TwoSided,
                _ => RasterKey.CullBack,
            };
            var raster = cache.GetRaster(device, rasterKey);
            if (raster != curRaster)
            {
                ctx->RSSetState(raster);
                curRaster = raster;
            }

            var vb = item.Mesh.Vb;
            if (vb == null)
                continue;

            if (!ReferenceEquals(item.Mesh, curMesh))
            {
                uint stride = (uint)sizeof(Vertex3D), offset = 0;
                ctx->IASetVertexBuffers(0, 1, &vb, &stride, &offset);
                ctx->IASetIndexBuffer(item.Mesh.Ib, item.Mesh.IndexFormat, 0);
                curMesh = item.Mesh;
            }

            var objData = new ObjectCBData
            {
                World = Matrix4x4.Transpose(item.World),
                InvWorld = Matrix4x4.Identity,
                BaseColor = item.OutlineColor, // drives the mask rgb and the coverage alpha
                Params0 = item.Mat.Params0,
                Params1 = item.Mat.Params1,
            };
            objectCb!.UpdateConstant(ctx, in objData);

            ctx->DrawIndexed((uint)item.Mesh.IndexCount, 0, 0);
            stats.DrawCalls++;
        }

        // Unbind the private depth so it can serve as a render target again, and clear t0 so the mask textures the
        // outline composite is about to read are never also bound as an input.
        ctx->OMSetRenderTargets(2, rtvs, null);
        ID3D11ShaderResourceView* nullSrv = null;
        ctx->PSSetShaderResources(0, 1, &nullSrv);
    }

    /// <summary>Uploads a ground decal's per-actor exclusion cylinders into the decal shader's ActorCB at b2, clearing the previous decal's list when empty.</summary>
    /// <param name="ctx">The immediate device context.</param>
    /// <param name="vols">The exclusion cylinders, or null for none.</param>
    private void UploadActorVolumes(ID3D11DeviceContext* ctx, IReadOnlyList<ExcludeVolume>? vols)
    {
        var actorData = new ActorCBData();
        var n = vols == null ? 0 : Math.Min(vols.Count, MaxActorVolumes);
        for (var i = 0; i < n; i++)
        {
            var v = vols![i];
            actorData.Actors[i * 4 + 0] = v.Position.X;
            actorData.Actors[i * 4 + 1] = v.Position.Z;
            actorData.Actors[i * 4 + 2] = v.Radius;
            actorData.Actors[i * 4 + 3] = 0f; // unused: the stencil silhouette makes the cut, so the gate is horizontal
        }

        actorData.ActorCount = (uint)n;
        actorData.CharacterStencil = currentCharacterStencil;
        actorCb!.UpdateConstant(ctx, in actorData);
    }

    private void EnsureBuffers(RenderDevice device)
    {
        frameCb ??= GpuBuffer.CreateConstant(device, (uint)sizeof(FrameCBData));
        objectCb ??= GpuBuffer.CreateConstant(device, (uint)sizeof(ObjectCBData));
        actorCb ??= GpuBuffer.CreateConstant(device, (uint)sizeof(ActorCBData));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        frameCb?.Dispose();
        frameCb = null;
        objectCb?.Dispose();
        objectCb = null;
        actorCb?.Dispose();
        actorCb = null;
        instanceRing.Dispose();
        dynVertexRing.Dispose();
        dynIndexRing.Dispose();
        itemCount = 0;
    }
}
