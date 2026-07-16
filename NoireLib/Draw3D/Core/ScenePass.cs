using NoireLib.Draw3D.Assets;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
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
    public Vector4 OutlineColor; // selection outline color (w > 0 = outlined); drives the outline coverage mask
    public float OutlineWidth;   // outline thickness in screen pixels
}

/// <summary>Per-frame constants - must match FrameCB in Common.hlsli exactly (240 bytes).</summary>
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

/// <summary>Per-object constants - must match ObjectCB in Common.hlsli exactly (192 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ObjectCBData
{
    public Matrix4x4 World;
    public Matrix4x4 InvWorld;
    public Vector4 BaseColor;
    public Vector4 Params0;
    public Vector4 Params1;
    public Vector4 Params2; // x = ground-decal projection mode (0 = all surfaces, 1 = highest only); y = box top world Y
}

/// <summary>
/// A ground decal's per-actor exclusion volumes (<c>ExcludeVolumes</c>) - must match ActorCB in Common.hlsli
/// exactly. Each actor is a vertical cylinder packed as (worldX, worldZ, radius, feetY).
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
/// The world pass: collects visible items (retained scenes + immediate layer), sorts them into
/// opaque → decal → transparent buckets, batches identical runs into instanced draws, and renders
/// into the offscreen premultiplied scene target.
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

    // Collection state (CPU, pooled).
    private DrawItem[] items = new DrawItem[256];
    private ulong[] keys = new ulong[256];
    private int itemCount;
    private int sequence;
    private uint currentCharacterStencil; // the game stencil value marking characters, this Execute (0 = exclusion off)
    private FrustumPlanes frustum;
    private Vector3 eyePos;
    private bool collectingForMainPass;
    private bool hasOutlined;
    private float maxOutlineWidth;
    private bool lastPrivateDepthWritten;

    /// <summary>Whether any collected item this frame has a selection outline (drives the optional outline pass).</summary>
    public bool HasOutlinedItems => hasOutlined;

    /// <summary>Whether any collected item this frame is a ground decal (retained or immediate) - gates the world-occlusion depth pass so decal-less scenes pay nothing.</summary>
    public bool AnyGroundDecals()
    {
        for (var i = 0; i < itemCount; i++)
            if (items[i].Mat.Domain == MaterialDomain.GroundDecal)
                return true;
        return false;
    }

    /// <summary>
    /// The highest box-top world Y across this frame's ground decals - the ceiling the top-down height-map is clipped to
    /// (<see cref="RenderWorldHeight"/>), so overhead geometry (a room's roof/upper floor) above every decal's box never
    /// masks the ground below. <see cref="float.NegativeInfinity"/> when no ground decals are present.
    /// </summary>
    public float MaxGroundDecalBoxTopY()
    {
        var top = float.NegativeInfinity;
        for (var i = 0; i < itemCount; i++)
        {
            ref var item = ref items[i];
            if (item.Mat.Domain == MaterialDomain.GroundDecal)
                top = MathF.Max(top, BoxTopY(in item.World));
        }
        return top;
    }

    /// <summary>World-space top Y of a decal's unit box (local [-0.5,0.5]³ transformed by <paramref name="world"/>): the
    /// AABB-max Y, i.e. the vertical bound of what the decal paints. Row-vector convention (world = local·M).</summary>
    private static float BoxTopY(in Matrix4x4 world)
        => world.M42 + 0.5f * (MathF.Abs(world.M12) + MathF.Abs(world.M22) + MathF.Abs(world.M32));

    /// <summary>Whether the last <see cref="Execute"/> populated the private depth buffer (opaque content) - so the outline mask can GE-test it for 3D-object occlusion. False means no private depth to read, so the mask falls back to world-only occlusion.</summary>
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

    /// <summary>Remaining dynamic-vertex budget this frame (immediate layer checks before writing shapes).</summary>
    public int DynamicVertexBudget => MaxDynamicVertices - DynVertices.Count;

    /// <summary>Begins collection for a frame (or a render view). Resets pooled lists and derives the frustum.</summary>
    public void BeginCollect(in FrameContext frame, bool mainPass)
    {
        itemCount = 0;
        sequence = 0;
        frustum = FrustumPlanes.FromViewProj(frame.ViewProj);
        eyePos = frame.EyePos;
        collectingForMainPass = mainPass;
        hasOutlined = false;
        maxOutlineWidth = 0f;
        if (mainPass)
        {
            DynVertices.Clear();
            DynIndices.Clear();
            KeyedTextures.Clear();
        }
    }

    /// <summary>Collects a retained scene's visible renderers (under the graph lock, resolving world matrices via dirty flags).</summary>
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

    /// <summary>Adds one mesh item (retained or immediate) with culling, depth-off policy, and sort-key derivation.</summary>
    public void AddMeshItem(Mesh mesh, in MaterialData mat, GpuTexture? texture, in Matrix4x4 world, Vector4 color, int layer, bool castsDepth, RenderStats stats, bool depthAvailable, IReadOnlyList<ExcludeVolume>? excludeVolumes = null, Vector4 outlineColor = default, float outlineWidth = 0f)
    {
        if (!depthAvailable && ShouldHideWithoutDepth(mat))
        {
            stats.CulledItems++;
            return;
        }

        var bounds = mesh.LocalBounds.Transform(world);
        // Ground decals reconstruct from depth across their whole volume box; the mesh bounds are the volume bounds - same test.
        if (!frustum.Intersects(bounds))
        {
            stats.CulledItems++;
            return;
        }

        if (texture != null && texture.HasKeyedMutex && collectingForMainPass && !KeyedTextures.Contains(texture))
            KeyedTextures.Add(texture);

        // Only solid meshes are outlined; a ground decal's OutlineColor is inert (decal outlining was removed).
        if (outlineColor.W > 0f && mat.Domain != MaterialDomain.GroundDecal)
        {
            hasOutlined = true;
            maxOutlineWidth = MathF.Max(maxOutlineWidth, outlineWidth);
        }

        var distance = Vector3.Distance(bounds.Center, eyePos);
        Append(new DrawItem
        {
            Mesh = mesh,
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

    /// <summary>Adds a dynamic-geometry range (already appended to <see cref="DynVertices"/>/<see cref="DynIndices"/>).</summary>
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
    /// Depth-aware nameplate policy: for each plate rect, decides whether the plate is in front of or
    /// behind the Draw3D content covering it. Output factors feed the composite as <i>UI visibility</i>
    /// inside the rect: 1 = the plate's letters keep reading on top (plate in front, or nothing covers
    /// it); <paramref name="behindFactor"/> = the plate is behind your shape, so the shape covers its
    /// letters (0 = fully, toward 1 = letters faintly showing through).<br/>
    /// The rects are never visible - they only gate WHERE the per-pixel UI mask applies, so every
    /// boundary on screen is the letters' own shape.
    /// </summary>
    public void ComputeRectOcclusion(in FrameContext frame, Vector4[] rects, float[] plateDistances, float[] factors, int count, float behindFactor)
    {
        // Projection scale for a conservative screen-space radius; the fallback constant only matters
        // when the wholesale-VP camera is active (Proj is identity there).
        var gy = frame.Proj.M22 is > 0.1f and < 20f ? frame.Proj.M22 : 1.4f;
        var gx = frame.Proj.M11 is > 0.05f and < 20f ? frame.Proj.M11 : gy * (frame.ViewportSize.Y / MathF.Max(frame.ViewportSize.X, 1f));

        for (var r = 0; r < count; r++)
            factors[r] = 1f;

        for (var i = 0; i < itemCount; i++)
        {
            ref var item = ref items[i];
            var clip = Vector4.Transform(new Vector4(item.BoundsCenter, 1f), frame.ViewProj);
            if (clip.W <= 0.05f)
                continue; // behind the camera - cannot cover a visible plate

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

                // The plate is covered only when it sits behind the item's farthest possible
                // surface - ties go to the letters (readability beats strictness).
                if (plateDistances[r] >= item.EyeDistance + item.BoundsRadius)
                    factors[r] = behindFactor;
            }
        }
    }

    private static bool ShouldHideWithoutDepth(in MaterialData mat)
        => mat.Domain == MaterialDomain.GroundDecal // decals have nothing to project onto without depth
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

        // Key composition per bucket:
        //  opaque      - state-grouped (pipeline/material above depth): depth order is only an early-z hint there.
        //  decal       - layer then creation order (deterministic, decals never instance).
        //  transparent - strict back-to-front, unless the material opted into unordered batching.
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
    /// Renders the collected items into the scene target (§9.8 draw-call anatomy). The caller has
    /// already captured the StateGuard and bound nothing - this method owns all bindings it makes.
    /// </summary>
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
        float worldOcclusionThreshold,
        Vector4 worldHeightRegion,
        Vector4 depthCal,
        ShaderLibrary shaders,
        StateCache cache,
        RenderStats stats,
        bool wireframe,
        Draw3DLighting lighting)
    {
        EnsureBuffers(device);
        currentCharacterStencil = sceneStencilSrv != null ? characterStencil : 0u; // 0 => decal skips the stencil test (paints as before)

        Array.Sort(keys, items, 0, itemCount);

        instanceRing.BeginFrame();

        // Upload the frame's dynamic geometry once.
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
                if (!dynVertexRing.TryWrite(device, ctx, v, (uint)(vSpan.Length * sizeof(Vertex3D)), 48, out dynVbOffset))
                    hasDynamic = false;
            }

            fixed (ushort* i = iSpan)
            {
                if (hasDynamic && !dynIndexRing.TryWrite(device, ctx, i, (uint)(iSpan.Length * sizeof(ushort)), 2, out dynIbOffset))
                    hasDynamic = false;
            }
        }

        // Frame constants (transpose-on-upload - the One Convention, §7.2).
        // DepthUv.zw carries OUR projection's z map (deviceZ = z + w/clipW). It must match the reversed-Z
        // Z column rebuilt in NoireDraw3D.RenderMainScene (clip.z = near ⇒ deviceZ = 0 + near/clipW), NOT the
        // game's exposed projection whose device-z we discard - so SceneWorldPos round-trips through
        // InvViewProj exactly.
        var frameData = new FrameCBData
        {
            ViewProj = Matrix4x4.Transpose(frame.ViewProj),
            InvViewProj = Matrix4x4.Transpose(frame.InvViewProj),
            EyePosTime = new Vector4(frame.EyePos, frame.Time),
            Viewport = new Vector4(frame.ViewportSize.X, frame.ViewportSize.Y, 1f / frame.ViewportSize.X, 1f / frame.ViewportSize.Y),
            DepthUv = new Vector4(frame.DepthUvScale.X, frame.DepthUvScale.Y, 0f, frame.NearPlane),
            // DepthCal.w carries the world-occlusion elevation band (world units) for ground decals; 0 = feature off
            // (decals fall back to the ExcludeVolumes cylinder). WorldHeight (t2) is only sampled when this is > 0.
            DepthCal = new Vector4(depthCal.X, depthCal.Y, depthCal.Z, worldHeightSrv != null ? worldOcclusionThreshold : 0f),
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

        // Targets: scene RT always; private depth only on frames with opaque content (stale-depth rule §9.3).
        var dsv = (ID3D11DepthStencilView*)null;
        if (hasOpaque && privateDepth.EnsureSize(device, sceneRt.Width, sceneRt.Height))
            dsv = privateDepth.Dsv;
        lastPrivateDepthWritten = collectingForMainPass && dsv != null; // the outline mask may GE-test this depth afterwards

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

        // Common bindings.
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
        ctx->PSSetShaderResources(3, 1, &sceneStencilSrv); // t3: game stencil plane (marks characters) for silhouette-exact decal exclusion (null = off)
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
        nint curDepthSrv = -1;
        Mesh? curMesh = null;
        var dynBound = false;

        foreach (var tex in KeyedTextures)
            tex.AcquireSync();

        var i0 = 0;
        while (i0 < itemCount)
        {
            ref var item = ref items[i0];
            var bucket = item.Mat.Bucket;

            // Find the instanced run: same mesh + identical material data, batchable domain.
            var run = 1;
            if (item.Mesh != null && item.Mat.Domain != MaterialDomain.GroundDecal && item.Mat.CustomPipeline == null)
            {
                while (i0 + run < itemCount
                       && ReferenceEquals(items[i0 + run].Mesh, item.Mesh)
                       && items[i0 + run].Mat.Equals(item.Mat)
                       && items[i0 + run].WritesPrivateDepth == item.WritesPrivateDepth)
                    run++;
            }

            var instanced = run > 1;

            var pipeline = item.Mat.CustomPipeline != null
                ? shaders.GetCustom(device, item.Mat.CustomPipeline)
                : shaders.GetStandard(device, item.Mat.Domain, item.Mat.Textured, instanced, opaqueDomain: bucket == 0);
            if (pipeline == null)
            {
                i0 += run;
                continue; // pipeline self-disabled (rung 1) - renders nothing
            }

            // States.
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
                // Decals test (never write) the private depth via the ground device-z their PS emits,
                // so nearer opaque 3D objects occlude them instead of the decal painting on top.
                1 => DepthKey.ReadGE,
                _ => hasDepthWrites ? DepthKey.ReadGE : DepthKey.Disabled,
            };
            // A transparent item that opts out of the private V2↔V2 depth test stays in front of other 3D objects:
            //  • DepthMode.Ignore   - full x-ray: also skips the world-depth SRV below, so it draws over everything.
            //  • DepthMode.WorldOnly - on top of objects but STILL world-occluded: it keeps the world-depth SRV, so a
            //    wall/terrain hides it while a nearer 3D object does not (the editor-gizmo mix).
            // Opaque (must write depth) and decal (projects via device-z) buckets are unaffected.
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
                curMesh = null; // input layout change does not unbind buffers, but keep binding logic simple
                dynBound = false;
            }

            // Scene depth SRV: null for DepthMode.Ignore materials (sampling null returns 0 ⇒ fully visible).
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

            // Geometry.
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

                var objData = new ObjectCBData
                {
                    World = Matrix4x4.Identity,
                    InvWorld = Matrix4x4.Identity,
                    BaseColor = new Vector4(1f, 1f, 1f, 1f),
                    Params0 = item.Mat.Params0,
                    Params1 = item.Mat.Params1,
                };
                objectCb.UpdateConstant(ctx, in objData);

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
                    // x = projection mode; y = this decal's box top world Y (the world-occlusion vertical search bound).
                    Params2 = new Vector4(item.Mat.ProjectionMode, BoxTopY(in item.World), 0f, 0f),
                };
                objectCb.UpdateConstant(ctx, in objData);

                // Ground decals carry their own per-actor exclusion list; upload it (or clear to 0) before the
                // draw so each decal cuts only around the actors it was given. Non-instanced by construction.
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
    /// Opt-in native-UI depth-write (<see cref="NoireDraw3D.NativeUiDepthWrite"/>): re-rasterizes this frame's
    /// opaque, depth-casting mesh items into an external depth-stencil view - the game's scene depth - depth-only
    /// (no colour), greater-equal tested so the world still occludes them. Run right AFTER <see cref="Execute"/>:
    /// it reuses the already-collected, already-sorted items. The caller owns the StateGuard; this binds its own
    /// target/states and restores nothing. Skips decals, transparents and dynamic markers - only solid geometry
    /// should hide a nameplate.
    /// </summary>
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

        // Re-upload the frame VP (the composite rebound cbuffers after Execute). The rebuilt reversed-Z Z column
        // makes SV_Position.z = near/clipW, which /noire3d probe confirmed matches the game's own depth buffer -
        // so our writes are directly comparable to the world's.
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

        // Depth-only: no colour target, the game's scene depth as DSV, greater-equal test+write (reversed-Z),
        // null pixel shader. Where the world is nearer the test fails and the world keeps the buffer (correct
        // occlusion); where our object is nearer it writes our depth, and the later nameplate pass is occluded.
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

            // Any opaque VS emits SV_Position from World*ViewProj; one non-instanced unlit pipeline serves all.
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
            };
            objectCb.UpdateConstant(ctx, in objData);

            ctx->DrawIndexed((uint)item.Mesh.IndexCount, 0, 0);
            stats.DrawCalls++;
        }
    }

    /// <summary>
    /// Renders the cached collision-world mesh top-down into an R32F height-map (each texel = the highest collision Y in
    /// that XZ column, via MAX blend) up to <paramref name="heightCeiling"/> - collision above it (a room's roof/upper
    /// floor) is discarded so it never masks the ground below. Ground decals sample it (bounded further to their own box
    /// top) to tell an elevated body from the ground/furniture surface - see the world-occlusion branch in
    /// GroundDecal.hlsl. Standalone (own target and states) so it runs before <see cref="Execute"/>.
    /// <paramref name="heightMatrix"/> is the CPU-built affine world-XZ→clip map (matching <c>WorldHeightRegion</c>);
    /// the mesh's vertices are relative to <paramref name="meshCenter"/>.
    /// </summary>
    public void RenderWorldHeight(
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
            return;

        var vb = collisionMesh.Vb;
        if (vb == null || collisionMesh.IndexCount == 0)
            return;

        var pipeline = shaders.GetWorldHeight(device);
        if (pipeline == null)
            return;

        EnsureBuffers(device);

        var frameData = new FrameCBData
        {
            ViewProj = Matrix4x4.Transpose(heightMatrix), // affine XZ->clip; the VS does mul(wp, ViewProj)
            DepthCal = new Vector4(heightCeiling, 0f, 0f, 0f), // x = ceiling: the PS discards collision above it (roof)
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
        var ocb = objectCb.Buffer;
        ctx->VSSetConstantBuffers(1, 1, &ocb);

        ctx->IASetInputLayout(pipeline.Layout);
        ctx->VSSetShader(pipeline.Vs, null, 0);
        ctx->PSSetShader(pipeline.Ps, null, 0);

        var blendFactor = stackalloc float[4];
        ctx->OMSetBlendState(cache.GetBlend(device, BlendKey.Max), blendFactor, 0xFFFFFFFF); // keep the highest Y per texel
        ctx->OMSetDepthStencilState(cache.GetDepth(device, DepthKey.Disabled), 0);
        ctx->RSSetState(cache.GetRaster(device, RasterKey.TwoSided)); // collision winding is arbitrary - two-sided

        uint stride = (uint)sizeof(Vertex3D), offset = 0;
        ctx->IASetVertexBuffers(0, 1, &vb, &stride, &offset);
        ctx->IASetIndexBuffer(collisionMesh.Ib, collisionMesh.IndexFormat, 0);
        ctx->DrawIndexed((uint)collisionMesh.IndexCount, 0, 0);
        stats.DrawCalls++;
    }

    /// <summary>
    /// Draws the outlined items into two targets, reusing the already-collected+sorted items:
    /// <paramref name="maskRt"/> (rgb = outline colour, a = coverage) holds each object's <b>FULL silhouette, ignoring
    /// occlusion</b>, so the composite outlines the whole object rather than every fragment poking through a fence;
    /// <paramref name="visRt"/> (r = worldVisible) marks, per silhouette pixel, whether it is in front of the game world
    /// - the composite then hides the finished outline wherever the nearest silhouette pixel is behind a wall/character.
    /// Solid meshes draw their whole silhouette with no depth test; ground decals GE-test their emitted ground device-z
    /// against the private depth (<paramref name="privateDepth"/>) so nearer 3D objects remove them, and their footprint
    /// already excludes the caller's actors. Run right AFTER <see cref="Execute"/> (the private depth still holds this
    /// frame's scene). Fail-soft.
    /// </summary>
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

        // Re-upload the frame constants (the composite/execute path may have rebound b0). Matches Execute's mapping.
        var frameData = new FrameCBData
        {
            ViewProj = Matrix4x4.Transpose(frame.ViewProj),
            InvViewProj = Matrix4x4.Transpose(frame.InvViewProj),
            EyePosTime = new Vector4(frame.EyePos, frame.Time),
            Viewport = new Vector4(frame.ViewportSize.X, frame.ViewportSize.Y, 1f / frame.ViewportSize.X, 1f / frame.ViewportSize.Y),
            DepthUv = new Vector4(frame.DepthUvScale.X, frame.DepthUvScale.Y, 0f, frame.NearPlane),
            DepthCal = depthCal,
            Ambient = Vector4.Zero,
            LightDirIntensity = new Vector4(0f, 1f, 0f, 0f),
            LightColor = Vector4.Zero,
        };
        frameCb!.UpdateConstant(ctx, in frameData);

        // Two colour targets (silhouette+coverage, worldVisible). The private depth is bound read-only for the decal
        // GE-test only (meshes draw with no depth test); it is not cleared, so this frame's scene depth is preserved.
        var dsv = privateDepthValid ? privateDepth.Dsv : null;
        var rtvs = stackalloc ID3D11RenderTargetView*[2] { maskRt.Rtv, visRt.Rtv };
        ctx->OMSetRenderTargets(2, rtvs, dsv);

        var viewport = new D3D11_VIEWPORT { Width = maskRt.Width, Height = maskRt.Height, MaxDepth = 1f };
        ctx->RSSetViewports(1, &viewport);
        var scissor = new TerraFX.Interop.Windows.RECT { right = (int)maskRt.Width, bottom = (int)maskRt.Height };
        ctx->RSSetScissorRects(1, &scissor);

        var clear = stackalloc float[4];
        ctx->ClearRenderTargetView(maskRt.Rtv, clear); // colour targets only - the depth is read, never cleared
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
            // Only solid meshes are outlined - ground decals are not (their projected footprint has no meaningful
            // screen silhouette; a decal outline is being redesigned separately). Immediate markers are not outlined.
            if (item.OutlineColor.W <= 0f || item.Mesh == null || item.Mat.Domain == MaterialDomain.GroundDecal)
                continue;

            // Meshes draw the FULL silhouette (no depth test) - occlusion is applied later from the worldVisible target
            // the PS writes, so the outline stays whole instead of fragmenting behind a fence.
            var depthState = cache.GetDepth(device, DepthKey.Disabled);
            if (depthState != curDepthState)
            {
                ctx->OMSetDepthStencilState(depthState, 0);
                curDepthState = depthState;
            }

            // t0: game depth for the worldVisible test. Null on an x-ray mesh, so DepthVisibility reports visible
            // everywhere and its outline is never occluded.
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
                BaseColor = item.OutlineColor, // outline colour drives the mask rgb + coverage-alpha
                Params0 = item.Mat.Params0,
                Params1 = item.Mat.Params1,
            };
            objectCb!.UpdateConstant(ctx, in objData);

            ctx->DrawIndexed((uint)item.Mesh.IndexCount, 0, 0);
            stats.DrawCalls++;
        }

        // Unbind the private depth (so it is free to serve as a render target again) and leave t0 clear so the mask
        // textures the outline composite is about to read are never also an input here.
        ctx->OMSetRenderTargets(2, rtvs, null);
        ID3D11ShaderResourceView* nullSrv = null;
        ctx->PSSetShaderResources(0, 1, &nullSrv);
    }

    /// <summary>
    /// Uploads a ground decal's per-actor exclusion cylinders into the decal shader's ActorCB (b2): each is
    /// packed (worldX, worldZ, radius, feetY). ActorCount = 0 (empty/null) clears any previous decal's list.
    /// </summary>
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
            actorData.Actors[i * 4 + 3] = v.Position.Y; // feet height - separates the body from the ground
        }

        actorData.ActorCount = (uint)n;
        actorData.CharacterStencil = currentCharacterStencil; // the game stencil value that marks characters (0 = off)
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
