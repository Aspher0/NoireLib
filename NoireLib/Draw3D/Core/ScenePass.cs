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
    public IReadOnlyList<ExcludeVolume>? ExcludeVolumes;
    public Vector4 OutlineColor; // w > 0 = outlined
    public float OutlineWidth;   // screen pixels
}

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
    public Vector4 WorldHeightRegion; // xy = region min XZ, z = 1/regionSize, w = 1 when the height map is valid
    public Vector4 DepthJitter;       // xy = display-uv offset of this pixel's world ray in the game's jittered depth
}

[StructLayout(LayoutKind.Sequential)]
internal struct ObjectCBData
{
    public Matrix4x4 World;
    public Matrix4x4 InvWorld;
    public Vector4 BaseColor;
    public Vector4 Params0;
    public Vector4 Params1;
    public Vector4 Params2; // x = decal projection (0 = all surfaces, 1 = highest only), y = box top world Y,
                            // z = outline reference footprint scale (0 = constant-thickness rim)
    public Vector4 OutlineColor; // straight alpha, alpha 0 falls back to BaseColor
    public Vector4 Params3;      // G-buffer injection puts dye colour in rgb and strength in w
}

// Matches ActorCB in Common.hlsli. Each actor packs as (worldX, worldZ, radius, unused).
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct ActorCBData
{
    public uint ActorCount;
    public uint CharacterStencil; // 0 = feature off
    public uint Pad1, Pad2;
    public fixed float Actors[ScenePass.MaxActorVolumes * 4];
}

internal sealed unsafe class ScenePass : IDisposable
{
    private const int MaxDynamicVertices = 65535;

    // Matches MAX_DECAL_ACTORS in Common.hlsli.
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
    private uint currentCharacterStencil;
    private FrustumPlanes frustum;
    private Vector3 eyePos;
    private bool collectingForMainPass;
    private long collectFrameId;

    // Main pass only. A render-to-texture view has no usable vertical focal length and draws full detail.
    private Draw3DPerformance.Snapshot perfSnapshot;
    private bool perfApplicable;
    private float projFocalY;   // NDC vertical units per view-space unit at unit depth
    private float halfViewportH;
    private bool hasOutlined;
    private float maxOutlineWidth;
    private bool lastPrivateDepthWritten;

    // Where this frame's dynamic geometry landed in the rings, for passes that replay it.
    private bool lastHasDynamic;
    private uint lastDynVbOffset;
    private uint lastDynIbOffset;

    public bool HasOutlinedItems => hasOutlined;

    public int CountTopSurfaceDecals()
    {
        var n = 0;
        for (var i = 0; i < itemCount; i++)
            if (IsTopSurfaceDecal(in items[i]))
                n++;
        return n;
    }

    // NegativeInfinity when there are no HighestOnly decals.
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

    private static bool IsTopSurfaceDecal(in DrawItem item)
        => item.Mat.Domain == MaterialDomain.GroundDecal && item.Mat.ProjectionMode > 0.5f;

    private static float BoxTopY(in Matrix4x4 world)
        => world.M42 + 0.5f * (MathF.Abs(world.M12) + MathF.Abs(world.M22) + MathF.Abs(world.M32));

    public bool LastPrivateDepthWritten => lastPrivateDepthWritten;

    public float MaxOutlineWidthPixels => maxOutlineWidth;

    public readonly List<Vertex3D> DynVertices = new(4096);

    public readonly List<ushort> DynIndices = new(8192);

    public readonly List<GpuTexture> KeyedTextures = new();

    private InstanceData[] instanceScratch = new InstanceData[256];

    // Instanced draws only change the material params. Any non-instanced write invalidates the cache.
    private bool objectCbCacheValid;
    private Vector4 objectCbParams0;
    private Vector4 objectCbParams1;
    private Vector4 objectCbParams2;

    public int DynamicVertexBudget => MaxDynamicVertices - DynVertices.Count;

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

        // Already drawn by the G-buffer injection this frame. Its children were not submitted and still draw.
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

    public void AddMeshItem(Mesh mesh, in MaterialData mat, GpuTexture? texture, in Matrix4x4 world, Vector4 color, int layer, bool castsDepth, RenderStats stats, bool depthAvailable, IReadOnlyList<ExcludeVolume>? excludeVolumes = null, Vector4 outlineColor = default, float outlineWidth = 0f)
    {
        if (!depthAvailable && ShouldHideWithoutDepth(mat))
        {
            stats.CulledItems++;
            return;
        }

        var bounds = mesh.LocalBounds.Transform(world);
        if (!frustum.Intersects(bounds))
        {
            stats.CulledItems++;
            return;
        }

        var distance = Vector3.Distance(bounds.Center, eyePos);

        var drawMesh = mesh;
        if (perfApplicable)
        {
            if (perfSnapshot.MaxDrawDistance > 0f && distance > perfSnapshot.MaxDrawDistance)
            {
                stats.CulledItems++;
                return;
            }

            var screenRadius = distance > 1e-3f ? bounds.Radius * projFocalY * halfViewportH / distance : float.MaxValue;

            if (perfSnapshot.MinScreenPixels > 0f && outlineColor.W <= 0f && screenRadius < perfSnapshot.MinScreenPixels)
            {
                stats.CulledItems++;
                return;
            }

            if (mesh.LodCount > 0 && mat.Domain != MaterialDomain.GroundDecal)
                drawMesh = mesh.SelectLod(Draw3DPerformance.SelectLevel(screenRadius, mesh.LodCount, in perfSnapshot));
        }

        if (texture != null && texture.HasKeyedMutex && collectingForMainPass && !KeyedTextures.Contains(texture))
            KeyedTextures.Add(texture);

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

    // The range must already be appended to DynVertices and DynIndices.
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

    // Rects are UVs as (minX, minY, maxX, maxY).
    public void ComputeRectOcclusion(in FrameContext frame, Vector4[] rects, float[] plateDistances, float[] factors, int count, float behindFactor, float[]? coveringItemFar = null)
    {
        // Only the wholesale-VP camera reaches the fallback. Its Proj is identity.
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
                continue;

            var uvX = clip.X / clip.W * 0.5f + 0.5f;
            var uvY = 0.5f - clip.Y / clip.W * 0.5f;
            var radiusU = item.BoundsRadius * gx / clip.W * 0.5f * 1.25f; // 1.25 = conservative slack
            var radiusV = item.BoundsRadius * gy / clip.W * 0.5f * 1.25f;

            for (var r = 0; r < count; r++)
            {
                if (factors[r] != 1f)
                    continue;

                var rect = rects[r];
                var overlaps = uvX + radiusU >= rect.X && uvX - radiusU <= rect.Z
                            && uvY + radiusV >= rect.Y && uvY - radiusV <= rect.W;
                if (!overlaps)
                    continue;

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
        => mat.Domain == MaterialDomain.GroundDecal
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

        // Opaque: state-grouped. Decal: layer then creation order. Transparent: back-to-front unless opted into batching.
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

    // The caller holds the StateGuard.
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
        currentCharacterStencil = sceneStencilSrv != null ? characterStencil : 0u; // 0 skips the stencil test

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
                if (!dynVertexRing.TryWrite(device, ctx, v, (uint)(vSpan.Length * sizeof(Vertex3D)), (uint)sizeof(Vertex3D), out dynVbOffset))
                    hasDynamic = false;
            }

            fixed (ushort* i = iSpan)
            {
                if (hasDynamic && !dynIndexRing.TryWrite(device, ctx, i, (uint)(iSpan.Length * sizeof(ushort)), 2, out dynIbOffset))
                    hasDynamic = false;
            }
        }

        lastHasDynamic = hasDynamic;
        lastDynVbOffset = dynVbOffset;
        lastDynIbOffset = dynIbOffset;

        // DepthUv.zw must match the reversed-Z column rebuilt in NoireDraw3D.RenderMainScene.
        var frameData = new FrameCBData
        {
            ViewProj = Matrix4x4.Transpose(frame.ViewProj),
            InvViewProj = Matrix4x4.Transpose(frame.InvViewProj),
            EyePosTime = new Vector4(frame.EyePos, frame.Time),
            Viewport = new Vector4(sceneRt.Width, sceneRt.Height, 1f / sceneRt.Width, 1f / sceneRt.Height),
            DepthUv = new Vector4(frame.DepthUvScale.X, frame.DepthUvScale.Y, 0f, frame.NearPlane),
            // DepthCal.w = top-surface elevation band. 0 degrades HighestOnly decals to AllSurfaces.
            DepthCal = new Vector4(depthCal.X, depthCal.Y, depthCal.Z, worldHeightSrv != null ? topSurfaceThreshold : 0f),
            Ambient = new Vector4(lighting.AmbientColor, lighting.AmbientIntensity),
            LightDirIntensity = new Vector4(Vector3.Normalize(lighting.LightDirection), lighting.LightIntensity),
            LightColor = new Vector4(lighting.LightColor, 0f),
            WorldHeightRegion = worldHeightSrv != null ? worldHeightRegion : Vector4.Zero,
            DepthJitter = NoireDraw3D.DepthSampleJitterUv,
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

        // Bound only on frames with opaque content. A leftover depth must never read as valid.
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
            ctx->ClearDepthStencilView(dsv, (uint)D3D11_CLEAR_FLAG.D3D11_CLEAR_DEPTH, 0.0f, 0); // reversed-Z far = 0

        ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        var cb = frameCb.Buffer;
        ctx->VSSetConstantBuffers(0, 1, &cb);
        ctx->PSSetConstantBuffers(0, 1, &cb);
        var ocb = objectCb!.Buffer;
        ctx->VSSetConstantBuffers(1, 1, &ocb);
        ctx->PSSetConstantBuffers(1, 1, &ocb);
        var acb = actorCb!.Buffer;
        ctx->PSSetConstantBuffers(2, 1, &acb); // b2: decal actor exclusion volumes
        ctx->PSSetShaderResources(2, 1, &worldHeightSrv); // t2: collision height map (null = off)
        ctx->PSSetShaderResources(3, 1, &sceneStencilSrv); // t3: game stencil (null = off)
        var pointClamp = cache.GetSampler(device, SamplerKey.PointClamp);
        ctx->PSSetSamplers(0, 1, &pointClamp);
        var linearWrap = cache.GetSampler(device, SamplerKey.LinearWrap);
        ctx->PSSetSamplers(1, 1, &linearWrap);

        var blendFactor = stackalloc float[4];

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

        objectCbCacheValid = false;

        var i0 = 0;
        while (i0 < itemCount)
        {
            ref var item = ref items[i0];
            var bucket = item.Mat.Bucket;

            // A decal's box is its SDF volume.
            if (wireframe && item.Mat.Domain == MaterialDomain.GroundDecal)
            {
                i0++;
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

            var instanced = run > 1 || (batchable && perfSnapshot.BatchedObjectConstants);

            var pipeline = item.Mat.CustomPipeline != null
                ? shaders.GetCustom(device, item.Mat.CustomPipeline)
                : shaders.GetStandard(device, item.Mat.Domain, item.Mat.Textured, instanced, opaqueDomain: bucket == 0);
            if (pipeline == null)
            {
                i0 += run;
                continue;
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
                // Decals test the private depth through the ground device-z their pixel shader emits. They never write it.
                1 => DepthKey.ReadGE,
                _ => hasDepthWrites ? DepthKey.ReadGE : DepthKey.Disabled,
            };
            // Ignore also drops the world-depth SRV. WorldOnly keeps it.
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

            // Sampling a null SRV returns 0: fully visible.
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

            // Unbound when absent. A pipeline must never read what the previous draw left in the slot.
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
                    // Decals: x = projection mode, y = box top world Y, z = outline reference footprint scale.
                    Params2 = item.Mat.Domain == MaterialDomain.GroundDecal
                        ? new Vector4(item.Mat.ProjectionMode, BoxTopY(in item.World), item.Mat.OutlineScaleRef, 0f)
                        : item.Mat.SurfaceParams,
                    OutlineColor = item.Mat.DecalOutlineColor,
                };
                objectCb.UpdateConstant(ctx, in objData);
                stats.ObjectCbUpdates++;
                objectCbCacheValid = false;

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

    // The nearest surface of the whole layer, translucent items included, for the temporal resolve to reproject
    // through. Decals are left out: they lie on the game's surface and reproject through its depth. The caller holds
    // the StateGuard.
    public bool RenderLayerDepth(RenderDevice device, ID3D11DeviceContext* ctx, DepthTarget target, uint width, uint height, in Matrix4x4 viewProj, ShaderLibrary shaders, StateCache cache)
    {
        if (!collectingForMainPass || frameCb == null || objectCb == null || !target.EnsureSize(device, width, height) || target.Dsv == null)
            return false;

        var pipeline = shaders.GetOutlineMaskMesh(device);
        if (pipeline == null)
            return false;

        var frameData = new FrameCBData { ViewProj = Matrix4x4.Transpose(viewProj) };
        frameCb.UpdateConstant(ctx, in frameData);

        ctx->OMSetRenderTargets(0, null, target.Dsv);
        ctx->ClearDepthStencilView(target.Dsv, (uint)D3D11_CLEAR_FLAG.D3D11_CLEAR_DEPTH, 0f, 0);
        var viewport = new D3D11_VIEWPORT { Width = width, Height = height, MaxDepth = 1f };
        ctx->RSSetViewports(1, &viewport);
        var scissor = new TerraFX.Interop.Windows.RECT { right = (int)width, bottom = (int)height };
        ctx->RSSetScissorRects(1, &scissor);
        ctx->OMSetDepthStencilState(cache.GetDepth(device, DepthKey.WriteGE), 0);
        ctx->RSSetState(cache.GetRaster(device, RasterKey.TwoSided));

        ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        ctx->IASetInputLayout(pipeline.Layout);
        ctx->VSSetShader(pipeline.Vs, null, 0);
        ctx->PSSetShader(null, null, 0);
        var cb = frameCb.Buffer;
        ctx->VSSetConstantBuffers(0, 1, &cb);
        var ocb = objectCb.Buffer;
        ctx->VSSetConstantBuffers(1, 1, &ocb);

        Mesh? curMesh = null;
        var dynBound = false;
        for (var i = 0; i < itemCount; i++)
        {
            ref var item = ref items[i];
            if (item.Mat.Domain == MaterialDomain.GroundDecal)
                continue;

            uint indexCount;
            int startIndex;
            if (item.Mesh != null)
            {
                var vb = item.Mesh.Vb;
                if (vb == null)
                    continue;

                if (!ReferenceEquals(item.Mesh, curMesh))
                {
                    uint stride = (uint)sizeof(Vertex3D), offset = 0;
                    ctx->IASetVertexBuffers(0, 1, &vb, &stride, &offset);
                    ctx->IASetIndexBuffer(item.Mesh.Ib, item.Mesh.IndexFormat, 0);
                    curMesh = item.Mesh;
                    dynBound = false;
                }

                indexCount = (uint)item.Mesh.IndexCount;
                startIndex = 0;
            }
            else
            {
                if (!lastHasDynamic)
                    continue;

                if (!dynBound)
                {
                    var vb = dynVertexRing.Buffer;
                    uint stride = (uint)sizeof(Vertex3D);
                    var vbOffset = lastDynVbOffset;
                    ctx->IASetVertexBuffers(0, 1, &vb, &stride, &vbOffset);
                    ctx->IASetIndexBuffer(dynIndexRing.Buffer, DXGI_FORMAT.DXGI_FORMAT_R16_UINT, lastDynIbOffset);
                    curMesh = null;
                    dynBound = true;
                }

                indexCount = (uint)item.DynIndexCount;
                startIndex = item.DynStartIndex;
            }

            var objData = new ObjectCBData { World = Matrix4x4.Transpose(item.World), InvWorld = Matrix4x4.Identity };
            objectCb.UpdateConstant(ctx, in objData);
            ctx->DrawIndexed(indexCount, (uint)startIndex, 0);
        }

        objectCbCacheValid = false;
        ctx->OMSetRenderTargets(0, null, null);
        return true;
    }

    // The caller holds the StateGuard.
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

        // The composite rebound the constant buffers after Execute.
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

    // On false the caller must leave the height-map SRV unbound. The target is only cleared on the drawing path.
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
            ViewProj = Matrix4x4.Transpose(heightMatrix),
            DepthCal = new Vector4(heightCeiling, 0f, 0f, 0f), // x = ceiling
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
        var clear = stackalloc float[4] { -1e30f, -1e30f, -1e30f, -1e30f };
        ctx->ClearRenderTargetView(rtv, clear);

        var viewport = new D3D11_VIEWPORT { Width = target.Width, Height = target.Height, MaxDepth = 1f };
        ctx->RSSetViewports(1, &viewport);
        var scissor = new TerraFX.Interop.Windows.RECT { right = (int)target.Width, bottom = (int)target.Height };
        ctx->RSSetScissorRects(1, &scissor);

        ctx->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        var cb = frameCb.Buffer;
        ctx->VSSetConstantBuffers(0, 1, &cb);
        // The PS slots otherwise hold the game's buffers.
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

    // maskRt: rgb = colour, a = coverage. visRt: in front of the game world or not.
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

        // The composite may have rebound b0.
        var frameData = new FrameCBData
        {
            ViewProj = Matrix4x4.Transpose(frame.ViewProj),
            InvViewProj = Matrix4x4.Transpose(frame.InvViewProj),
            EyePosTime = new Vector4(frame.EyePos, frame.Time),
            Viewport = new Vector4(maskRt.Width, maskRt.Height, 1f / maskRt.Width, 1f / maskRt.Height),
            DepthUv = new Vector4(frame.DepthUvScale.X, frame.DepthUvScale.Y, 0f, frame.NearPlane),
            DepthCal = depthCal,
            Ambient = Vector4.Zero,
            LightDirIntensity = new Vector4(0f, 1f, 0f, 0f),
            LightColor = Vector4.Zero,
        };
        frameCb!.UpdateConstant(ctx, in frameData);

        var dsv = privateDepthValid ? privateDepth.Dsv : null;
        var rtvs = stackalloc ID3D11RenderTargetView*[2] { maskRt.Rtv, visRt.Rtv };
        ctx->OMSetRenderTargets(2, rtvs, dsv);

        var viewport = new D3D11_VIEWPORT { Width = maskRt.Width, Height = maskRt.Height, MaxDepth = 1f };
        ctx->RSSetViewports(1, &viewport);
        var scissor = new TerraFX.Interop.Windows.RECT { right = (int)maskRt.Width, bottom = (int)maskRt.Height };
        ctx->RSSetScissorRects(1, &scissor);

        var clear = stackalloc float[4];
        ctx->ClearRenderTargetView(maskRt.Rtv, clear);
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
        ctx->OMSetBlendState(cache.GetBlend(device, BlendKey.Opaque), blendFactor, 0xFFFFFFFF);

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
            if (item.OutlineColor.W <= 0f || item.Mesh == null || item.Mat.Domain == MaterialDomain.GroundDecal)
                continue;

            // Occlusion comes from the visibility target. The outline must not fragment behind a fence.
            var depthState = cache.GetDepth(device, DepthKey.Disabled);
            if (depthState != curDepthState)
            {
                ctx->OMSetDepthStencilState(depthState, 0);
                curDepthState = depthState;
            }

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
                BaseColor = item.OutlineColor,
                Params0 = item.Mat.Params0,
                Params1 = item.Mat.Params1,
            };
            objectCb!.UpdateConstant(ctx, in objData);

            ctx->DrawIndexed((uint)item.Mesh.IndexCount, 0, 0);
            stats.DrawCalls++;
        }

        ctx->OMSetRenderTargets(2, rtvs, null);
        ID3D11ShaderResourceView* nullSrv = null;
        ctx->PSSetShaderResources(0, 1, &nullSrv);
    }

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
            actorData.Actors[i * 4 + 3] = 0f;
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
