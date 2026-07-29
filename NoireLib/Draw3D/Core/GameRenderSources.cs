using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using NoireLib.Helpers;
using System;
using System.Collections.Generic;
using System.Numerics;
using GameCameraManager = FFXIVClientStructs.FFXIV.Client.Game.Control.CameraManager;
using GameControl = FFXIVClientStructs.FFXIV.Client.Game.Control.Control;
using KernelDevice = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device;
using RenderTargetManager = FFXIVClientStructs.FFXIV.Client.Graphics.Render.RenderTargetManager;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// Draw3D's render sources: the game's D3D11 device, backbuffer, scene depth and camera, plus the composite's
/// nameplate and HUD policy rects and the decal actor exclusions. Every read goes through named fields on
/// <c>Instance()</c> singletons: no signatures, no offsets, no hooks.<br/>
/// Addon geometry is not read here; it comes from <see cref="AddonHelper"/>.<br/>
/// All methods return raw values only; COM lifetime management happens in the callers.
/// </summary>
internal static unsafe class GameRenderSources
{
    /// <summary>Raw backbuffer information for the current frame.</summary>
    internal readonly record struct BackBufferInfo(nint Texture, uint Width, uint Height);

    /// <summary>Raw scene-depth texture information for the current frame. Value-equality is used for change detection.</summary>
    internal readonly record struct DepthTextureInfo(nint Texture, nint GameSrv, uint ActualWidth, uint ActualHeight, uint AllocatedWidth, uint AllocatedHeight);

    /// <summary>Raw camera state for the current frame.</summary>
    internal struct CameraData
    {
        /// <summary>Render camera view matrix (valid when <see cref="HasRenderCamera"/>).</summary>
        public Matrix4x4 View;
        /// <summary>Render camera projection matrix - the game's exact reversed-Z, infinite-far projection.</summary>
        public Matrix4x4 Proj;
        /// <summary>The render camera's second projection matrix (role unknown) - diagnostics/probe only, never a render source.</summary>
        public Matrix4x4 Proj2;
        /// <summary>The game's own combined view-projection (world-to-screen path), used as cross-check and wholesale fallback.</summary>
        public Matrix4x4 ControlViewProj;
        /// <summary>Camera origin in world space.</summary>
        public Vector3 Origin;
        /// <summary>True when the RenderCamera pair was read successfully.</summary>
        public bool HasRenderCamera;
        /// <summary>True when Control's combined view-projection was read successfully.</summary>
        public bool HasControlViewProj;
        /// <summary>Depth convention flags straight from the render camera (expected false/false: reversed-Z, infinite far).</summary>
        public bool StandardZ, FiniteFarPlane;
        /// <summary>Camera frustum parameters (diagnostics + culling).</summary>
        public float NearPlane, FarPlane, Fov, AspectRatio;
    }

    /// <summary>
    /// The game's D3D11 device as an unvalidated IUnknown: Kernel.Device's forwarder primary,
    /// Dalamud's <c>UiBuilder.DeviceHandle</c> fallback. Callers must QueryInterface (see <see cref="RenderDevice.TryCreate"/>).
    /// </summary>
    public static void* GetDeviceUnknown()
    {
        var kernel = KernelDevice.Instance();
        void* raw = kernel != null ? kernel->D3D11Forwarder : null;

        if (raw == null && NoireService.IsInitialized())
            raw = (void*)NoireService.PluginInterface.UiBuilder.DeviceHandle;

        return raw;
    }

    /// <summary>Reads the current backbuffer texture pointer and swapchain dimensions. False when anything on the path is null or zero-sized.</summary>
    public static bool TryGetBackBuffer(out BackBufferInfo info)
    {
        info = default;

        var kernel = KernelDevice.Instance();
        if (kernel == null || kernel->SwapChain == null)
            return false;

        var swapChain = kernel->SwapChain;
        var backBuffer = swapChain->BackBuffer;
        if (backBuffer == null || backBuffer->D3D11Texture2D == null)
            return false;

        if (swapChain->Width == 0 || swapChain->Height == 0)
            return false;

        info = new BackBufferInfo((nint)backBuffer->D3D11Texture2D, swapChain->Width, swapChain->Height);
        return true;
    }

    /// <summary>
    /// Reads the game's scene depth texture ("Unscaled scene reverse-Z depth stencil") from RenderTargetManager.<br/>
    /// False when unavailable - the frame runs in depth-off mode.
    /// </summary>
    public static bool TryGetDepthTexture(out DepthTextureInfo info)
    {
        info = default;

        var rtm = RenderTargetManager.Instance();
        if (rtm == null)
            return false;

        var depth = rtm->DepthStencil;
        if (depth == null || depth->D3D11Texture2D == null)
            return false;

        if (depth->ActualWidth == 0 || depth->ActualHeight == 0)
            return false;

        info = new DepthTextureInfo(
            (nint)depth->D3D11Texture2D,
            (nint)depth->D3D11ShaderResourceView,
            depth->ActualWidth,
            depth->ActualHeight,
            depth->AllocatedWidth == 0 ? depth->ActualWidth : depth->AllocatedWidth,
            depth->AllocatedHeight == 0 ? depth->ActualHeight : depth->AllocatedHeight);
        return true;
    }

    /// <summary>
    /// Reads the swapchain's depth texture - the probe's diagnostics alternate for answering
    /// "which buffer really holds this frame's scene depth at present time". Never a render source
    /// unless the probe proves it should be.
    /// </summary>
    public static bool TryGetSwapChainDepthTexture(out DepthTextureInfo info)
    {
        info = default;

        var kernel = KernelDevice.Instance();
        if (kernel == null || kernel->SwapChain == null)
            return false;

        var depth = kernel->SwapChain->DepthStencil;
        if (depth == null || depth->D3D11Texture2D == null || depth->ActualWidth == 0 || depth->ActualHeight == 0)
            return false;

        info = new DepthTextureInfo(
            (nint)depth->D3D11Texture2D,
            (nint)depth->D3D11ShaderResourceView,
            depth->ActualWidth,
            depth->ActualHeight,
            depth->AllocatedWidth == 0 ? depth->ActualWidth : depth->AllocatedWidth,
            depth->AllocatedHeight == 0 ? depth->ActualHeight : depth->AllocatedHeight);
        return true;
    }

    /// <summary>
    /// Reads the camera once, as a single immutable snapshot per presented frame. View and projection come
    /// from the single active RenderCamera; the Control combined VP is the wholesale fallback and validator
    /// cross-check - sources are never mixed.
    /// </summary>
    public static bool TryGetCamera(out CameraData data)
    {
        data = default;

        var manager = GameCameraManager.Instance();
        if (manager != null)
        {
            var active = manager->GetActiveCamera();
            if (active != null)
            {
                var sceneCamera = active->SceneCamera;
                var renderCamera = sceneCamera.RenderCamera;
                if (renderCamera != null)
                {
                    data.View = renderCamera->ViewMatrix;
                    data.Proj = renderCamera->ProjectionMatrix;
                    data.Proj2 = renderCamera->ProjectionMatrix2;
                    data.Origin = renderCamera->Origin;
                    data.StandardZ = renderCamera->StandardZ;
                    data.FiniteFarPlane = renderCamera->FiniteFarPlane;
                    data.NearPlane = renderCamera->NearPlane;
                    data.FarPlane = renderCamera->FarPlane;
                    data.Fov = renderCamera->FoV;
                    data.AspectRatio = renderCamera->AspectRatio;
                    data.HasRenderCamera = true;
                }
            }
        }

        var control = GameControl.Instance();
        if (control != null)
        {
            data.ControlViewProj = control->ViewProjectionMatrix;
            data.HasControlViewProj = true;
        }

        return data.HasRenderCamera || data.HasControlViewProj;
    }

    /// <summary>Slack (framebuffer pixels) added around each nameplate policy rect - see the padding note at its use.</summary>
    private const float PlateRectPadding = 6f;

    /// <summary>The smallest rect containing both (xy = min, zw = max).</summary>
    private static Vector4 Union(in Vector4 a, in Vector4 b)
        => new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Max(a.Z, b.Z), MathF.Max(a.W, b.W));

    /// <summary>
    /// Collects the screen rects (display-UV space: xy = min, zw = max) of the currently visible
    /// nameplates plus each plate's world-space distance from the camera. The rects are invisible
    /// policy regions for the composite's per-pixel UI mask (nameplate layering over everything).
    /// Fails soft: any inconsistency returns 0 rects - plates read on top for this frame only.
    /// </summary>
    /// <param name="rawDistances">
    /// Optional diagnostics: receives the game's own <c>DistanceFromCamera</c> per plate, unconverted. Reported next to
    /// the measured distance so the squared-units finding stays checkable rather than a claim in a comment.
    /// </param>
    public static int CollectNamePlateRects(Vector4[] rects, float[] distances, int max, Vector2 displaySize, float[]? rawDistances = null)
    {
        if (displaySize.X <= 0 || displaySize.Y <= 0)
            return 0;

        try
        {
            var uiModule = UIModule.Instance();
            if (uiModule == null)
                return 0;

            var ui3d = uiModule->GetUI3DModule();
            if (ui3d == null)
                return 0;

            var addon = (AddonNamePlate*)NoireService.GameGui.GetAddonByName("NamePlate").Address;
            if (addon == null || !addon->AtkUnitBase.IsVisible)
                return 0;

            var count = 0;
            var plateAddon = (AtkUnitBase*)addon;
            var infoCount = ui3d->NamePlateObjectInfoCount;
            var infoPointers = ui3d->NamePlateObjectInfoPointers;
            for (var i = 0; i < infoCount && i < infoPointers.Length && count < max && count < rects.Length; i++)
            {
                var info = infoPointers[i].Value;
                if (info == null)
                    continue;

                int plateIndex = info->NamePlateIndex;
                if (plateIndex < 0 || plateIndex >= 50)
                    continue;

                ref var plate = ref addon->NamePlateObjectArray[plateIndex];
                if (!plate.IsVisible)
                    continue;

                // The UNION of the plate's container and its interactable collision box, never the tighter of the two.
                // The rect is only a policy gate, so overshooting is free: outside the plate's actual pixels the UI
                // mask reads no coverage and the layer draws there regardless of what the rect says. Undershooting is
                // not free - whatever part of the plate falls outside (its icon, a name overhanging the interactable
                // box, status markers) keeps the default "UI reads on top" and survives a plate meant to be covered.
                var hasRect = new NoireAddonNode(plateAddon, (AtkResNode*)plate.NameplateCollision).TryGetScreenRect(out var rect);
                if (new NoireAddonNode(plateAddon, plate.NameContainer).TryGetScreenRect(out var containerRect))
                {
                    rect = hasRect ? Union(rect, containerRect) : containerRect;
                    hasRect = true;
                }

                if (!hasRect)
                    continue;

                // Same reasoning as the union, applied to time rather than space: these node positions are read on the
                // framework thread, but the composite that uses them runs at present time, so under camera motion the
                // plate has moved a little by then. Padding absorbs that drift for free - a rect reaching past the
                // plate covers pixels the UI never drew, where the mask reads no coverage and nothing changes.
                rect = new Vector4(rect.X - PlateRectPadding, rect.Y - PlateRectPadding, rect.Z + PlateRectPadding, rect.W + PlateRectPadding);

                // DistanceFromCamera is a SQUARED distance and must be rooted before it can be compared against the
                // linear world distances the occlusion test works in. Used raw it made every plate read as impossibly
                // far (15.0 for a character standing 3.9m away) and lose every comparison, so nameplates were covered
                // by content sitting well behind them and DepthAware behaved identically to Covered.
                // NamePlatePos is not a usable substitute here: it reads as the world origin, which turns a distance
                // measured from it into the camera's distance from (0,0,0) - a large number that fails just as badly.
                var plateDistanceSq = info->DistanceFromCamera;
                if (plateDistanceSq <= 0f)
                    continue; // no usable distance - leave this plate reading on top rather than guess at its depth

                if (rawDistances != null)
                    rawDistances[count] = plateDistanceSq;

                distances[count] = MathF.Sqrt(plateDistanceSq);
                rects[count++] = new Vector4(
                    rect.X / displaySize.X, rect.Y / displaySize.Y,
                    rect.Z / displaySize.X, rect.W / displaySize.Y);
            }

            return count;
        }
        catch (System.Exception)
        {
            return 0; // protection off this frame - never let nameplate reads take the frame down
        }
    }

    /// <summary>
    /// Collects the screen rects (display-UV space) of every visible game addon (HUD windows). Used as force-on-top
    /// policy regions inside the composite: where a HUD window overlaps a "covered" nameplate region, the HUD still
    /// reads on top. Fails soft to 0 appended rects.
    /// </summary>
    /// <returns>The number of rects appended starting at <paramref name="startIndex"/>.</returns>
    public static int CollectVisibleAddonRects(Vector4[] rects, int startIndex, int max, Vector2 displaySize)
    {
        if (displaySize.X <= 0 || displaySize.Y <= 0)
            return 0;

        try
        {
            var count = 0;

            // The helper skips near-fullscreen transparent overlay roots (nameplates, fly text, screen info) by default:
            // they cover the whole viewport and would cut the entire layer.
            foreach (var addon in AddonHelper.VisibleAddons(displaySize))
            {
                if (count >= max || startIndex + count >= rects.Length)
                    break;

                var rect = addon.ScreenRect;
                rects[startIndex + count] = new Vector4(
                    rect.X / displaySize.X, rect.Y / displaySize.Y,
                    rect.Z / displaySize.X, rect.W / displaySize.Y);
                count++;
            }

            return count;
        }
        catch (System.Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// Appends nearby game objects to <paramref name="into"/> as <see cref="ExcludeVolume"/>s (position + hitbox
    /// radius * <paramref name="radiusScale"/>) for a ground decal's <c>ExcludeVolumes</c>. Filtered by
    /// <paramref name="include"/> (default: players, battle NPCs, event NPCs) and capped at <paramref name="max"/>.
    /// Reads the object table, so call it on the framework thread. Fails soft (appends nothing) on error.
    /// </summary>
    public static void CollectActorExclusions(List<ExcludeVolume> into, int max, Func<IGameObject, bool>? include, float radiusScale)
    {
        if (into == null || max <= 0 || !NoireService.IsInitialized())
            return;

        try
        {
            var predicate = include ?? DefaultActorInclude;
            var objects = NoireService.ObjectTable;
            for (var i = 0; i < objects.Length && into.Count < max; i++)
            {
                var obj = objects[i];
                if (obj == null || !predicate(obj))
                    continue;

                // A GENEROUS gate: this radius does not cut anything itself - it only selects which characters the
                // stencil exclusion applies to, so it must comfortably contain the character's whole XZ footprint
                // (arms, a tail). A margin over the hitbox is safe (only stencil-character pixels inside it are ever removed).
                var radius = (obj.HitboxRadius > 0f ? obj.HitboxRadius : 0.5f) + 0.8f;

                into.Add(new ExcludeVolume(obj.Position, radius * radiusScale));
            }
        }
        catch (System.Exception)
        {
            // exclusion unavailable this call - the decal reads over actors rather than taking the frame down
        }
    }

    /// <summary>
    /// Appends exclusion volumes chosen by a per-object <paramref name="selector"/> (return null to skip an object) -
    /// the full-control path where the caller decides the exact volume. Walks the whole object table (capped at
    /// <paramref name="max"/>). Reads the object table, so call it on the framework thread. Fails soft on error.
    /// </summary>
    /// <param name="into">The list to append to.</param>
    /// <param name="max">Cap on the number of volumes.</param>
    /// <param name="selector">Per-object volume selector; null result skips the object.</param>
    public static void CollectActorExclusions(List<ExcludeVolume> into, int max, Func<IGameObject, ExcludeVolume?> selector)
    {
        if (into == null || selector == null || max <= 0 || !NoireService.IsInitialized())
            return;

        try
        {
            var objects = NoireService.ObjectTable;
            for (var i = 0; i < objects.Length && into.Count < max; i++)
            {
                var obj = objects[i];
                if (obj == null)
                    continue;

                if (selector(obj) is { } volume)
                    into.Add(volume);
            }
        }
        catch (System.Exception)
        {
            // exclusion unavailable this call - the decal reads over actors rather than taking the frame down
        }
    }

    /// <summary>Default <see cref="CollectActorExclusions(List{ExcludeVolume}, int, Func{IGameObject, bool}, float)"/> filter: characters (players), monsters and NPCs.</summary>
    private static bool DefaultActorInclude(IGameObject o)
        => o.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc;
}
