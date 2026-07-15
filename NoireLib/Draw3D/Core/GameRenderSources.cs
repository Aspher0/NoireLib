using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using System;
using System.Collections.Generic;
using System.Numerics;
using GameControl = FFXIVClientStructs.FFXIV.Client.Game.Control.Control;
using GameCameraManager = FFXIVClientStructs.FFXIV.Client.Game.Control.CameraManager;
using KernelDevice = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Device;
using RenderTargetManager = FFXIVClientStructs.FFXIV.Client.Graphics.Render.RenderTargetManager;

namespace NoireLib.Draw3D.Core;

/// <summary>
/// The ONLY file in Draw3D that touches FFXIVClientStructs. Every read goes through named fields
/// on <c>Instance()</c> singletons (Law 8: zero signatures, zero offsets, zero hooks).<br/>
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
        /// <summary>The game's own combined view-projection (world→screen path), used as cross-check and wholesale fallback.</summary>
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
    /// Reads the camera once (Law 2: one snapshot per presented frame). The One-Camera-Object rule:
    /// view and projection come from the single active RenderCamera; the Control combined VP is the
    /// wholesale fallback and validator cross-check - sources are never mixed.
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

    /// <summary>
    /// Collects the screen rects (display-UV space: xy = min, zw = max) of the currently visible
    /// nameplates plus each plate's world-space distance from the camera. The rects are invisible
    /// policy regions for the composite's per-pixel UI mask (depth-aware nameplate layering).
    /// Fails soft: any inconsistency returns 0 rects - plates read on top for this frame only.
    /// </summary>
    public static int CollectNamePlateRects(Vector4[] rects, float[] distances, int max, Vector2 displaySize, Vector3 eyePos)
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

                // The collision node hugs the interactable plate area - much tighter than the container.
                var node = (AtkResNode*)plate.NameplateCollision;
                if (node == null || !node->IsVisible())
                    node = plate.NameContainer;
                if (node == null || !node->IsVisible())
                    continue;

                var x = node->ScreenX;
                var y = node->ScreenY;
                var w = node->Width * node->ScaleX;
                var h = node->Height * node->ScaleY;
                if (w <= 0 || h <= 0)
                    continue;

                var plateDistance = info->DistanceFromCamera;
                if (plateDistance <= 0f)
                    plateDistance = Vector3.Distance(info->NamePlatePos, eyePos);

                distances[count] = plateDistance;
                rects[count++] = new Vector4(x / displaySize.X, y / displaySize.Y, (x + w) / displaySize.X, (y + h) / displaySize.Y);
            }

            return count;
        }
        catch (System.Exception)
        {
            return 0; // protection off this frame - never let nameplate reads take the frame down
        }
    }

    /// <summary>
    /// Collects the screen rects of every visible game addon (HUD windows). Used as force-on-top policy
    /// regions inside the composite: where a HUD window overlaps a "covered" nameplate region, the HUD
    /// still reads on top. Near-fullscreen roots (the NamePlate/fly-text style transparent overlays)
    /// are skipped - they would swallow every plate region. Fails soft to 0 appended rects.
    /// </summary>
    /// <returns>The number of rects appended starting at <paramref name="startIndex"/>.</returns>
    public static int CollectVisibleAddonRects(Vector4[] rects, int startIndex, int max, Vector2 displaySize)
    {
        if (displaySize.X <= 0 || displaySize.Y <= 0)
            return 0;

        try
        {
            var manager = RaptureAtkUnitManager.Instance();
            if (manager == null)
                return 0;

            var count = 0;
            ref var list = ref manager->AllLoadedUnitsList;
            var entries = list.Entries;
            int loaded = list.Count;
            for (var i = 0; i < loaded && i < entries.Length && count < max && startIndex + count < rects.Length; i++)
            {
                var unit = entries[i].Value;
                if (unit == null || !unit->IsVisible)
                    continue;

                var root = unit->RootNode;
                if (root == null || !root->IsVisible())
                    continue;

                var x = root->ScreenX;
                var y = root->ScreenY;
                var w = root->Width * root->ScaleX * unit->Scale;
                var h = root->Height * root->ScaleY * unit->Scale;
                if (w <= 1 || h <= 1)
                    continue;

                // Skip near-fullscreen transparent overlay roots (nameplates, fly text, screen info):
                // they cover the whole viewport and would cut the entire layer.
                if (w >= displaySize.X * 0.9f && h >= displaySize.Y * 0.9f)
                    continue;

                rects[startIndex + count] = new Vector4(
                    x / displaySize.X, y / displaySize.Y,
                    (x + w) / displaySize.X, (y + h) / displaySize.Y);
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
    /// Whether a screen point (framebuffer pixels) lies over an interactive part of a visible game addon, i.e. the
    /// cursor is over native game UI a click should belong to. The point must fall inside the addon's root rect (a quick
    /// reject that keeps the near-fullscreen overlay skip) and over one of its visible <b>collision nodes</b>, the
    /// regions the game itself hit-tests. Testing collision nodes rather than the padded root rect means the transparent
    /// margins around HUD elements (the empty space beside action-bar slots, a window's padding) do not falsely block
    /// picking a 3D object behind them. Fails soft to false. Read on the main/draw thread.
    /// </summary>
    /// <param name="pointPx">The point to test, in framebuffer pixels (the ImGui mouse space Dalamud shares with the game).</param>
    /// <param name="displaySize">The framebuffer size, for the near-fullscreen overlay skip.</param>
    public static bool IsPointOverVisibleAddon(Vector2 pointPx, Vector2 displaySize)
        => IsPointOverVisibleAddon(pointPx, displaySize, out _);

    /// <summary>
    /// The diagnostic form of <see cref="IsPointOverVisibleAddon(Vector2, Vector2)"/>: also reports the name of the
    /// addon whose collision node caught the point (null when the point is over no game UI).
    /// </summary>
    public static bool IsPointOverVisibleAddon(Vector2 pointPx, Vector2 displaySize, out string? addonName)
    {
        addonName = null;
        if (displaySize.X <= 0 || displaySize.Y <= 0)
            return false;

        try
        {
            // When the player hides the native UI (the game's own hide-UI toggle), the addons stay loaded with their
            // collision nodes live, so they would keep intercepting clicks on 3D objects even though nothing is drawn.
            // Treat a hidden UI as "no addon under the cursor" so the whole interface stops blocking picks while hidden.
            var atkModule = RaptureAtkModule.Instance();
            if (atkModule != null && !atkModule->IsUiVisible)
                return false;

            var manager = RaptureAtkUnitManager.Instance();
            if (manager == null)
                return false;

            ref var list = ref manager->AllLoadedUnitsList;
            var entries = list.Entries;
            int loaded = list.Count;
            for (var i = 0; i < loaded && i < entries.Length; i++)
            {
                var unit = entries[i].Value;
                if (unit == null || !unit->IsVisible)
                    continue;

                var root = unit->RootNode;
                if (root == null || !root->IsVisible())
                    continue;

                var w = root->Width * root->ScaleX * unit->Scale;
                var h = root->Height * root->ScaleY * unit->Scale;
                if (w <= 1 || h <= 1)
                    continue;

                // Quick reject: the cursor must be inside the addon's root bounds before its nodes are worth walking.
                var x = root->ScreenX;
                var y = root->ScreenY;
                if (pointPx.X < x || pointPx.X >= x + w || pointPx.Y < y || pointPx.Y >= y + h)
                    continue;

                // Skip near-fullscreen transparent overlay roots (nameplates, fly text, screen info): the same rule the
                // composite UI mask uses, so a fullscreen overlay never blanket-blocks every pointer interaction.
                if (w >= displaySize.X * 0.9f && h >= displaySize.Y * 0.9f)
                    continue;

                // Block only over an actual collision node (the game's own hit region), not the addon's transparent padding.
                if (PointOverCollisionNode(unit, pointPx))
                {
                    addonName = ReadAddonName(unit);
                    return true;
                }
            }

            return false;
        }
        catch (System.Exception)
        {
            addonName = null;
            return false; // read faulted this frame: do not let a UI probe take the frame down
        }
    }

    /// <summary>Whether the point falls inside a visible collision node of the addon (its own node list, recursing through component nodes).</summary>
    private static bool PointOverCollisionNode(AtkUnitBase* unit, Vector2 pointPx)
    {
        // Action bars keep a badge's collision node live even when its content (the bar-number label and arrows) is
        // switched off, so those addons get the extra "collision has visible content beside it" gate below.
        var isActionBar = unit->Name.StartsWith("_ActionBar"u8);
        return NodeListHit(unit->UldManager.NodeList, unit->UldManager.NodeListCount, unit->Scale, pointPx, isActionBar, depth: 0);
    }

    /// <summary>
    /// Whether the point falls in a visible collision node reachable from this node list. Component nodes are recursed
    /// into to any depth (their collision nodes live in their own list, so a component's inner controls, such as an
    /// action-bar slot or a window button, are only found this way), and a collision node counts only when its whole
    /// ancestor chain is visible. The game leaves a node's own visibility flag set while a hidden parent hides it on
    /// screen, so the parent-chain check is what stops a switched-off element (a hidden hotbar-number badge) from
    /// blocking. A display-only addon (a job gauge) carries no collision nodes, so it never blocks; interactive UI does.
    /// </summary>
    private static bool NodeListHit(AtkResNode** nodes, int nodeCount, float unitScale, Vector2 pointPx, bool isActionBar, int depth)
    {
        if (nodes == null || depth > 8)
            return false;

        for (var n = 0; n < nodeCount; n++)
        {
            var node = nodes[n];
            if (node == null || !node->IsVisible())
                continue;

            if (node->Type == NodeType.Collision)
            {
                if (!NodeAncestorsVisible(node))
                    continue;

                // On action bars, a collision whose parent has no other visible child is a phantom badge (its label and
                // arrows are switched off, only the always-visible collision remains); a real control keeps visible
                // content beside its collision, so this only skips the empty badge, never a live button or slot.
                if (isActionBar && CollisionLacksVisibleSibling(nodes, nodeCount, node))
                    continue;

                var w = node->Width * node->ScaleX * unitScale;
                var h = node->Height * node->ScaleY * unitScale;
                if (w > 1 && h > 1)
                {
                    var x = node->ScreenX;
                    var y = node->ScreenY;
                    if (pointPx.X >= x && pointPx.X < x + w && pointPx.Y >= y && pointPx.Y < y + h)
                        return true;
                }

                continue;
            }

            var compNode = node->GetAsAtkComponentNode();
            if (compNode != null && NodeAncestorsVisible(node))
            {
                var comp = compNode->Component;
                if (comp != null && NodeListHit(comp->UldManager.NodeList, comp->UldManager.NodeListCount, unitScale, pointPx, isActionBar, depth + 1))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a visible collision node has no visible non-collision sibling under the same parent. True marks a phantom
    /// hit region: a hotbar keeps the number-badge collision node visible while the label / arrows beside it are hidden,
    /// so "nothing visible next to the collision" is the signal that the control is switched off and should not block.
    /// </summary>
    private static bool CollisionLacksVisibleSibling(AtkResNode** nodes, int nodeCount, AtkResNode* collision)
    {
        var parent = collision->ParentNode;
        if (parent == null)
            return false;

        for (var i = 0; i < nodeCount; i++)
        {
            var node = nodes[i];
            if (node == null || node == collision || node->ParentNode != parent)
                continue;
            if (node->Type != NodeType.Collision && node->IsVisible())
                return false; // real content sits beside the collision
        }

        return true;
    }

    /// <summary>Whether every ancestor of the node (up to its list's root) is visible. The node's own flag is checked by the caller.</summary>
    private static bool NodeAncestorsVisible(AtkResNode* node)
    {
        var parent = node->ParentNode;
        var guard = 0;
        while (parent != null && guard++ < 64)
        {
            if (!parent->IsVisible())
                return false;

            parent = parent->ParentNode;
        }

        return true;
    }

    /// <summary>Reads an addon's short name from its fixed name buffer, for diagnostics. Returns "?" on any fault.</summary>
    private static string ReadAddonName(AtkUnitBase* unit)
    {
        try
        {
            var name = unit->Name; // Span over the fixed name buffer
            var len = 0;
            while (len < name.Length && name[len] != 0)
                len++;
            return len == 0 ? "?" : System.Text.Encoding.UTF8.GetString(name[..len]);
        }
        catch (System.Exception)
        {
            return "?";
        }
    }

    /// <summary>
    /// Appends nearby game objects to <paramref name="into"/> as <see cref="ExcludeVolume"/>s (position + hitbox
    /// radius × <paramref name="radiusScale"/>) for a ground decal's <c>ExcludeVolumes</c>. Filtered by
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

                var radius = obj.HitboxRadius;
                if (radius <= 0f)
                    radius = 0.5f;

                into.Add(new ExcludeVolume(obj.Position, radius * radiusScale));
            }
        }
        catch (System.Exception)
        {
            // exclusion unavailable this call - the decal reads over actors rather than taking the frame down
        }
    }

    /// <summary>Default <see cref="CollectActorExclusions"/> filter: characters (players), monsters and NPCs.</summary>
    private static bool DefaultActorInclude(IGameObject o)
        => o.ObjectKind is ObjectKind.Pc or ObjectKind.BattleNpc or ObjectKind.EventNpc;
}
