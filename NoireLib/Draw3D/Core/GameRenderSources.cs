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

// Every read goes through named fields on Instance() singletons. COM lifetime is the callers' concern.
internal static unsafe class GameRenderSources
{
    internal readonly record struct BackBufferInfo(nint Texture, uint Width, uint Height);

    internal readonly record struct DepthTextureInfo(nint Texture, nint GameSrv, uint ActualWidth, uint ActualHeight, uint AllocatedWidth, uint AllocatedHeight);

    internal struct CameraData
    {
        // One frame ahead of the camera the game draws with.
        public Matrix4x4 View;
        public Matrix4x4 Proj;
        // Role unknown. Diagnostics only.
        public Matrix4x4 Proj2;
        public Matrix4x4 ControlViewProj;
        public Vector3 Origin;
        public bool HasRenderCamera;
        public bool HasControlViewProj;
        // Expected false/false: reversed-Z, infinite far.
        public bool StandardZ, FiniteFarPlane;
        public float NearPlane, FarPlane, Fov, AspectRatio;
    }

    // Unvalidated IUnknown. Callers QueryInterface.
    public static void* GetDeviceUnknown()
    {
        var kernel = KernelDevice.Instance();
        void* raw = kernel != null ? kernel->D3D11Forwarder : null;

        if (raw == null && NoireService.IsInitialized())
            raw = (void*)NoireService.PluginInterface.UiBuilder.DeviceHandle;

        return raw;
    }

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

    private const float PlateRectPadding = 6f;

    private static Vector4 Union(in Vector4 a, in Vector4 b)
        => new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Max(a.Z, b.Z), MathF.Max(a.W, b.W));

    // Any failure returns 0 rects, leaving plates on top for that frame.
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

            if (!AddonHelper.TryGetAddon<AddonNamePlate>("NamePlate", out var addon) || !addon->AtkUnitBase.IsVisible)
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

                // Container and collision box. Undershooting leaves an overhanging name on top of a covered plate.
                var hasRect = new NoireAddonNode(plateAddon, (AtkResNode*)plate.NameplateCollision).TryGetScreenRect(out var rect);
                if (new NoireAddonNode(plateAddon, plate.NameContainer).TryGetScreenRect(out var containerRect))
                {
                    rect = hasRect ? Union(rect, containerRect) : containerRect;
                    hasRect = true;
                }

                if (!hasRect)
                    continue;

                // Absorbs the plate's drift between this framework-thread read and the present-time composite.
                rect = new Vector4(rect.X - PlateRectPadding, rect.Y - PlateRectPadding, rect.Z + PlateRectPadding, rect.W + PlateRectPadding);

                // DistanceFromCamera is squared. NamePlatePos reads as the world origin.
                var plateDistanceSq = info->DistanceFromCamera;
                if (plateDistanceSq <= 0f)
                    continue;

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
            return 0;
        }
    }

    public static int CollectVisibleAddonRects(Vector4[] rects, int startIndex, int max, Vector2 displaySize)
    {
        if (displaySize.X <= 0 || displaySize.Y <= 0)
            return 0;

        try
        {
            var count = 0;

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

    // Framework thread only.
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

                // Only selects where the stencil exclusion applies. Must contain the whole XZ footprint.
                var radius = (obj.HitboxRadius > 0f ? obj.HitboxRadius : 0.5f) + 0.8f;

                into.Add(new ExcludeVolume(obj.Position, radius * radiusScale));
            }
        }
        catch (System.Exception)
        {
        }
    }

    // Framework thread only.
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
        }
    }

    private static bool DefaultActorInclude(IGameObject o)
        => o.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc;
}
