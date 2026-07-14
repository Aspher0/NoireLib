using NoireLib.Draw3D.Core;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Interaction;
using NoireLib.Draw3D.Interaction.Gizmo;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using TerraFX.Interop.DirectX;
using TerraFX.Interop.Windows;

namespace NoireLib.Draw3D;

/// <summary>
/// The runtime diagnostics toolkit behind <c>/noire3d</c>, exposed programmatically so any consumer can
/// run it even when another plugin owns the command name. Results go to the plugin log.<br/>
/// <c>RunValidate</c>: projection parity vs the game's own WorldToScreen (the wobble-class killer).<br/>
/// <c>RunProbe</c>: reads actual depth-buffer values back and compares them against expectation.<br/>
/// <c>SpawnSmokeScene</c>: the reference QA scene from the regression matrix.
/// </summary>
public sealed unsafe class Draw3DDiagnostics
{
    private int validateFramesRemaining;
    private float validateMaxDelta;
    private double validateDeltaSum;
    private int validateSamples;
    private float validateMaxMatrixDelta;
    private bool probePending;

    private readonly List<SceneNode> smokeNodes = new();
    private readonly List<Mesh> smokeMeshes = new();
    private readonly List<MeshRenderer> smokeDecalRenderers = new();
    private bool smokeExclusionHooked;
    private NoireGizmo? smokeGizmo;
    private Action? smokeSelectionHandler;

    internal Draw3DDiagnostics() { }

    /// <summary>Arms the projection parity validator for the next 10 rendered frames (results logged). Gate: max ≤ 1 px.</summary>
    public void RunValidate()
    {
        NoireDraw3D.EnsureInitialized();
        validateFramesRemaining = 10;
        validateMaxDelta = 0f;
        validateDeltaSum = 0;
        validateSamples = 0;
        validateMaxMatrixDelta = 0f;
    }

    /// <summary>
    /// Arms the ground-truth depth probe for the next rendered frame (results logged): the analytic depth
    /// map rendering uses, a diagnostic-only raycast fit, a per-point depth table for both candidate
    /// buffers, and the UI-mask alpha health. Read-only — it never disturbs the live depth state.
    /// Gate: ≥ 90 % of hit points within 1e-3.
    /// </summary>
    public void RunProbe()
    {
        NoireDraw3D.EnsureInitialized();
        probePending = true;
    }

    /// <summary>Toggles wireframe rasterization of the scene pass. Returns the new state.</summary>
    public bool ToggleWireframe() => NoireDraw3D.Wireframe = !NoireDraw3D.Wireframe;

    /// <summary>Formats the current stats snapshot.</summary>
    public string GetStatsText() => NoireDraw3D.Stats.ToString();

    /// <summary>
    /// Spawns the reference QA scene around the player: telegraph decals (ring/sector/rect), a lit torus,
    /// an additive orb, an opaque box stack and a flat quad. Every object — decals included — is wired for interaction:
    /// hover highlights, left-click selects, and a <see cref="NoireGizmo"/> (in-world depth handles) attaches to the
    /// selection so you can move/rotate/scale it — exercising the whole NoireInteract spine in-game. Hovering native
    /// game UI (inventory, friend list, HUD) over an object is a hard pass — it never registers.
    /// <see cref="ClearSmokeScene"/> removes it.
    /// </summary>
    public void SpawnSmokeScene()
    {
        ClearSmokeScene();

        var center = NoireService.ObjectTable.LocalPlayer?.Position
            ?? (NoireDraw3D.LastFrameValid ? NoireDraw3D.LastFrame.EyePos : Vector3.Zero);

        var scene = NoireDraw3D.MainScene;

        // Log the click / hover / gizmo pipeline to /xllog while the QA scene is up (turned back off in ClearSmokeScene).
        NoireInteract.DebugLog = true;

        // Ground telegraphs (decal domain — hug the terrain). Interactable too: keep CPU data so the projection volume
        // picks triangle-exact, and wire hover/select so the gizmo can grab and move a decal like any other node.
        // The volume is a generous vertical slab: shape-aware picking gates the XZ footprint precisely, so a tall volume
        // no longer over-catches, and the height absorbs uneven ground / collision-vs-rendered-surface disagreement so
        // every spot the decal visibly covers still picks.
        var ring = scene.CreateNode("Smoke.Ring");
        ring.LocalPosition = center;
        ring.LocalScale = new Vector3(8f, 4f, 8f);
        var ringMesh = new Mesh(MeshBuilder.Box(), keepCpuData: true, name: "Smoke.RingVolume");
        smokeMeshes.Add(ringMesh);
        var ringRenderer = ring.SetMesh(ringMesh, Material.Telegraph(DecalShape.Ring, new Vector4(1f, 0.55f, 0.1f, 0.9f), new Vector4(0.7f, 0f, 0f, 0.5f)));
        smokeDecalRenderers.Add(ringRenderer);
        MakeSmokeInteractable(ring, ringRenderer);
        smokeNodes.Add(ring);

        var sector = scene.CreateNode("Smoke.Sector");
        sector.LocalPosition = center + new Vector3(6f, 0f, 0f);
        sector.LocalScale = new Vector3(10f, 4f, 10f);
        var sectorMesh = new Mesh(MeshBuilder.Box(), keepCpuData: true, name: "Smoke.SectorVolume");
        smokeMeshes.Add(sectorMesh);
        var sectorRenderer = sector.SetMesh(sectorMesh, Material.Telegraph(DecalShape.Sector, new Vector4(0.9f, 0.15f, 0.15f, 0.9f), new Vector4(MathF.PI / 4f, 0f, 0f, 0.55f)));
        smokeDecalRenderers.Add(sectorRenderer);
        MakeSmokeInteractable(sector, sectorRenderer);
        smokeNodes.Add(sector);

        // Lit torus (the donut) floating above the ring. Interactable — keep CPU data so picking is triangle-exact.
        var torus = scene.CreateNode("Smoke.Torus");
        torus.LocalPosition = center + new Vector3(0f, 2f, 0f);
        var torusMesh = new Mesh(MeshBuilder.Torus(1.6f, 0.35f), keepCpuData: true, name: "Smoke.Torus");
        smokeMeshes.Add(torusMesh);
        MakeSmokeInteractable(torus, torus.SetMesh(torusMesh, Material.Lit(new Vector4(0.95f, 0.95f, 1f, 1f))));
        smokeNodes.Add(torus);

        // Additive energy orb.
        var orb = scene.CreateNode("Smoke.Orb");
        orb.LocalPosition = center + new Vector3(-4f, 1.5f, 2f);
        var orbMesh = new Mesh(MeshBuilder.Sphere(0.75f, 32, 20), keepCpuData: true, name: "Smoke.Orb");
        smokeMeshes.Add(orbMesh);
        MakeSmokeInteractable(orb, orb.SetMesh(orbMesh, Material.Unlit(new Vector4(0.2f, 0.6f, 1f, 0.8f)) with { Blend = BlendMode.Additive }));
        smokeNodes.Add(orb);

        // Opaque box stack (private-depth V2↔V2 occlusion).
        var boxMesh = new Mesh(MeshBuilder.Box(), keepCpuData: true, name: "Smoke.Box");
        smokeMeshes.Add(boxMesh);
        for (var i = 0; i < 3; i++)
        {
            var box = scene.CreateNode($"Smoke.Box{i}");
            box.LocalPosition = center + new Vector3(3.5f, 0.5f + i * 1.05f, -3.5f);
            box.LocalRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, i * 0.4f);
            MakeSmokeInteractable(box, box.SetMesh(boxMesh, Material.Lit(new Vector4(0.8f - i * 0.2f, 0.4f + i * 0.25f, 0.35f, 1f)) with { Cull = CullMode.None }));
            smokeNodes.Add(box);
        }

        // Flat translucent quad (world depth test + DepthFade seam).
        var quad = scene.CreateNode("Smoke.Quad");
        quad.LocalPosition = center + new Vector3(-3f, 0.05f, -4f);
        quad.LocalScale = new Vector3(4f, 1f, 4f);
        var quadMesh = new Mesh(MeshBuilder.Quad(), keepCpuData: true, name: "Smoke.Quad");
        smokeMeshes.Add(quadMesh);
        MakeSmokeInteractable(quad, quad.SetMesh(quadMesh, Material.Unlit(new Vector4(0.3f, 1f, 0.5f, 0.5f), depthFade: 0.35f) with { Cull = CullMode.None }));
        smokeNodes.Add(quad);

        // A universal gizmo (native in-world depth handles) that follows the selection: left-click any solid object to
        // select it, then drag the handles — the camera stays put while you drag (NoireInteract owns the mouse).
        smokeGizmo = new NoireGizmo(GizmoOp.Universal);
        smokeGizmo.Options.Backend = GizmoBackend.Native; // flip live with '/noire3d gizmo' to compare with ImGuizmo
        smokeGizmo.Options.Snap = new Vector3(0.5f);
        smokeGizmo.Options.RotateSnapDeg = 15f;
        smokeSelectionHandler = () =>
        {
            var primary = NoireInteract.Selection.Primary;
            if (primary != null)
                smokeGizmo.Attach(primary);
            else
                smokeGizmo.Detach();
        };
        NoireInteract.Selection.Changed += smokeSelectionHandler;

        // Demonstrate ground-decal actor exclusion: refresh the telegraph decals' exclusion cylinders from the
        // nearby characters each tick so the ring/sector cut cleanly around anyone standing in them (no hole).
        if (!smokeExclusionHooked)
        {
            NoireService.Framework.Update += RefreshSmokeExclusions;
            smokeExclusionHooked = true;
        }
    }

    /// <summary>Wires a smoke object for interaction: hover brightens its tint, left-click selects it (the gizmo then attaches).</summary>
    private static void MakeSmokeInteractable(SceneNode node, MeshRenderer renderer)
    {
        var baseTint = renderer.Tint;
        node.Interactable = true;
        node.OnHoverEnter = _ => renderer.Tint = baseTint * new Vector4(1.5f, 1.5f, 1.5f, 1f);
        node.OnHoverExit = _ => renderer.Tint = baseTint;
    }

    /// <summary>Per-tick refresh of the smoke telegraphs' actor exclusions (framework thread — object-table reads belong here).</summary>
    private void RefreshSmokeExclusions(Dalamud.Plugin.Services.IFramework framework)
    {
        if (smokeDecalRenderers.Count == 0)
            return;

        var exclusions = NoireDraw3D.GetActorExclusions();
        foreach (var renderer in smokeDecalRenderers)
            renderer.ExcludeVolumes = exclusions;
    }

    /// <summary>
    /// Flips the smoke scene's gizmo between the native (in-world depth) and ImGuizmo (classic 2D) backends so both can
    /// be compared in-game without a recompile. Select an object afterwards to see the switch; the ImGuizmo init/draw
    /// diagnostics land in /xllog. No-op with a message when the smoke scene isn't up.
    /// </summary>
    public string ToggleSmokeGizmoBackend()
    {
        if (smokeGizmo == null)
            return "Draw3D: no smoke scene — run '/noire3d smoke' first.";

        smokeGizmo.Options.Backend = smokeGizmo.Options.Backend == GizmoBackend.ImGuizmo
            ? GizmoBackend.Native
            : GizmoBackend.ImGuizmo;
        return $"Draw3D: smoke gizmo backend = {smokeGizmo.Options.Backend}. Select an object to see it — if ImGuizmo doesn't appear, check /xllog for the '[Gizmo]' lines.";
    }

    /// <summary>Removes the smoke scene and disposes its meshes.</summary>
    public void ClearSmokeScene()
    {
        NoireInteract.DebugLog = false;

        if (smokeExclusionHooked)
        {
            NoireService.Framework.Update -= RefreshSmokeExclusions;
            smokeExclusionHooked = false;
        }

        if (smokeSelectionHandler != null)
        {
            NoireInteract.Selection.Changed -= smokeSelectionHandler;
            smokeSelectionHandler = null;
        }

        smokeGizmo?.Dispose();
        smokeGizmo = null;
        NoireInteract.Selection.Clear();

        smokeDecalRenderers.Clear();

        foreach (var node in smokeNodes)
            node.Destroy();
        smokeNodes.Clear();

        foreach (var mesh in smokeMeshes)
            mesh.Dispose();
        smokeMeshes.Clear();
    }

    // ---------------------------------------------------------------- frame hooks (called by the hub)

    internal void OnFrame(in FrameContext frame, in GameRenderSources.CameraData cam, bool hasDepth)
    {
        if (validateFramesRemaining <= 0)
            return;

        validateFramesRemaining--;

        // Sample world points: around the player, plus points pushed forward through screen rays.
        Span<Vector3> points = stackalloc Vector3[24];
        var count = 0;

        var player = NoireService.ObjectTable.LocalPlayer?.Position;
        if (player is { } p)
        {
            for (var i = 0; i < 8; i++)
            {
                var (sin, cos) = MathF.SinCos(i * MathF.Tau / 8f);
                points[count++] = p + new Vector3(cos * 3f, (i % 3) * 0.8f, sin * 3f);
            }
        }

        for (var gy = 0; gy < 4; gy++)
        {
            for (var gx = 0; gx < 4; gx++)
            {
                var screen = new Vector2(frame.ViewportSize.X * (0.2f + 0.2f * gx), frame.ViewportSize.Y * (0.2f + 0.2f * gy));
                if (frame.TryScreenToRay(screen, out var origin, out var dir))
                    points[count++] = origin + dir * (5f + gx * 12f + gy * 3f);
            }
        }

        for (var i = 0; i < count; i++)
        {
            if (!frame.TryWorldToScreen(points[i], out var ours))
                continue;
            if (!NoireService.GameGui.WorldToScreen(points[i], out var theirs))
                continue;

            var delta = Vector2.Distance(ours, theirs);
            validateMaxDelta = MathF.Max(validateMaxDelta, delta);
            validateDeltaSum += delta;
            validateSamples++;
        }

        // Cross-check View·Proj against the game's own combined matrix.
        if (cam.HasRenderCamera && cam.HasControlViewProj)
        {
            var ours = cam.View * cam.Proj;
            var theirs = cam.ControlViewProj;
            var maxDelta = 0f;
            var a = ours;
            var b = theirs;
            Span<float> av = stackalloc float[16] { a.M11, a.M12, a.M13, a.M14, a.M21, a.M22, a.M23, a.M24, a.M31, a.M32, a.M33, a.M34, a.M41, a.M42, a.M43, a.M44 };
            Span<float> bv = stackalloc float[16] { b.M11, b.M12, b.M13, b.M14, b.M21, b.M22, b.M23, b.M24, b.M31, b.M32, b.M33, b.M34, b.M41, b.M42, b.M43, b.M44 };
            for (var i = 0; i < 16; i++)
                maxDelta = MathF.Max(maxDelta, MathF.Abs(av[i] - bv[i]));
            validateMaxMatrixDelta = MathF.Max(validateMaxMatrixDelta, maxDelta);
        }

        if (validateFramesRemaining == 0)
        {
            var mean = validateSamples > 0 ? validateDeltaSum / validateSamples : 0;
            var verdict = validateMaxDelta <= 1.0f ? "PASS" : "FAIL";
            var report = $"Draw3D validate [{verdict}]: {validateSamples} samples over 10 frames — max {validateMaxDelta:F3} px, mean {mean:F3} px (gate: max ≤ 1 px). " +
                         $"VP cross-check max element delta: {validateMaxMatrixDelta:E2}. Camera fallback active: {frame.UsedFallbackCamera}. " +
                         "Repeat in the §7.5 poses: orbit, side-on grazing, wall-collision camera, first-person, max zoom.";
            NoireService.ChatGui.Print($"Draw3D validate: {verdict} — max {validateMaxDelta:F3} px (details in log).");
            NoireLogger.LogInfo(report, "Draw3D");
        }
    }

    internal void OnFrameRendered(RenderDevice device, in FrameContext frame, SceneDepth? sceneDepth)
    {
        if (!probePending)
            return;

        probePending = false;

        try
        {
            RunProbeNow(device, in frame, sceneDepth);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Draw3D depth probe failed.", "Draw3D");
        }
    }

    private void RunProbeNow(RenderDevice device, in FrameContext frame, SceneDepth? sceneDepth)
    {
        // Gather ground-truth points: screen positions raycast into the world by the game itself.
        var screens = new List<Vector2>();
        var worlds = new List<Vector3>();
        for (var gy = 0; gy < 4; gy++)
        {
            for (var gx = 0; gx < 4; gx++)
            {
                var screen = new Vector2(frame.ViewportSize.X * (0.2f + 0.2f * gx), frame.ViewportSize.Y * (0.2f + 0.2f * gy));
                if (NoireService.GameGui.ScreenToWorld(screen, out var world))
                {
                    screens.Add(screen);
                    worlds.Add(world);
                }
            }
        }

        if (screens.Count == 0)
        {
            Report("Draw3D probe: no raycast hits (nothing under the sampled screen points?). Aim the camera at terrain and retry.");
            return;
        }

        // Expected buffer value from the analytic depth map (what rendering actually uses), plus our
        // reconstructed device-z as a cross-check. Both come from the camera, never from a fit.
        var cam = NoireDraw3D.LastCameraData;
        var near = cam.NearPlane > 1e-6f ? cam.NearPlane : 0.1f;
        var map = DepthCalibration.AnalyticMap(near, cam.FarPlane, cam.StandardZ, cam.FiniteFarPlane);
        var expectedMap = new float[screens.Count];
        var expectedProjZ = new float[screens.Count];
        var clipWs = new float[screens.Count];
        for (var i = 0; i < screens.Count; i++)
        {
            var clip = Vector4.Transform(new Vector4(worlds[i], 1f), frame.ViewProj);
            clipWs[i] = clip.W;
            expectedMap[i] = clip.W > 1e-6f ? map.X + map.Y / clip.W : float.NaN;
            expectedProjZ[i] = clip.W > 1e-6f ? clip.Z / clip.W : float.NaN;
        }

        // Actual values from both candidate depth buffers.
        float[]? actualMain = null, actualSwap = null;
        string mainDesc = "unavailable", swapDesc = "unavailable";
        if (GameRenderSources.TryGetDepthTexture(out var mainInfo))
            actualMain = DepthReadback.TryReadAtPoints(device, in mainInfo, screens, frame.ViewportSize, out mainDesc);
        if (GameRenderSources.TryGetSwapChainDepthTexture(out var swapInfo))
            actualSwap = DepthReadback.TryReadAtPoints(device, in swapInfo, screens, frame.ViewportSize, out swapDesc);

        // An informational least-squares fit of the same raycast points (diagnostic ONLY — never used for
        // rendering). A gap between this and the analytic map is collision-vs-rendered-surface disagreement,
        // which is exactly why fitting was abandoned in favour of the analytic map.
        var fitXs = new List<float>(screens.Count);
        var fitYs = new List<float>(screens.Count);
        if (actualMain != null)
        {
            for (var i = 0; i < screens.Count; i++)
            {
                if (clipWs[i] > 1e-6f && !float.IsNaN(actualMain[i]) && actualMain[i] is >= 0f and <= 1f)
                {
                    fitXs.Add(1f / clipWs[i]);
                    fitYs.Add(actualMain[i]);
                }
            }
        }

        var fitDesc = DepthCalibration.TrySolve(fitXs, fitYs, out var fitA, out var fitB, out var fitResid, out var fitInliers)
            ? $"z={fitA:E2}{(fitB >= 0 ? "+" : "")}{fitB:F5}/w ({fitInliers} pts, resid {fitResid:E1})"
            : "unfittable this frame";

        var details = new StringBuilder();
        details.AppendLine($"Draw3D probe: {screens.Count} raycast points. Active source: {sceneDepth?.Description ?? "none"}.");
        details.AppendLine($"  analytic map (used by rendering): z={map.X:E2}{(map.Y >= 0 ? "+" : "")}{map.Y:F5}/w");
        details.AppendLine($"  raycast fit (diagnostic only):    {fitDesc}");
        details.AppendLine($"  RenderTargetManager depth: {mainDesc}");
        details.AppendLine($"  SwapChain depth:           {swapDesc}");
        details.AppendLine("  point | expected(map) expected(projZ) | actual(RTM) actual(Swap)");
        for (var i = 0; i < screens.Count; i++)
            details.AppendLine($"  {i,2}: {Fmt(expectedMap, i)} {Fmt(expectedProjZ, i)} | {Fmt(actualMain, i)} {Fmt(actualSwap, i)}");

        var mainVsMap = CountMatches(expectedMap, actualMain);
        var swapVsMap = CountMatches(expectedMap, actualSwap);
        details.AppendLine($"  matches within 1e-3: RTM×map {mainVsMap}/{screens.Count}, Swap×map {swapVsMap}/{screens.Count}");

        // UI-mask alpha health. Per-pixel game-UI-on-top depends on the backbuffer alpha channel holding
        // native-UI coverage; in FFXIV the native UI often writes NO alpha there, in which case these read
        // ~0, the per-pixel mask is inert, and the layer draws over the HUD (a known engine limitation).
        var health = NoireDraw3D.UiMaskHealthState;
        details.AppendLine($"  ui mask: {health?.Description ?? "off"}");
        if (health?.LastSamples is { } alphas)
            details.AppendLine($"  ui alpha samples: {string.Join(" ", Array.ConvertAll(alphas, a => a.ToString("F2")))}");

        var gate = (int)MathF.Ceiling(screens.Count * 0.9f);
        var verdict = mainVsMap >= gate ? "PASS"
            : swapVsMap >= gate ? "FAIL — scene depth lives in the SwapChain buffer at present time (paste the log)"
            : "FAIL — the analytic map does not match the RTM buffer; paste the point table "
              + "(mismatched rows are usually collision-vs-rendered-surface disagreement, harmless if few)";

        Report($"Draw3D probe [{verdict.Split(' ')[0]}]: RTM×map {mainVsMap}/{screens.Count} (gate ≥ {gate}). {(verdict.Contains('—') ? verdict[(verdict.IndexOf('—') + 2)..] : "Analytic depth mapping confirmed against ground truth.")}");
        NoireLogger.LogInfo($"Draw3D probe details:\n{details}", "Draw3D");
    }

    private static string Fmt(float[]? values, int i)
        => values == null || float.IsNaN(values[i]) ? "   n/a  " : values[i].ToString("F6");

    private static int CountMatches(float[] expected, float[]? actual)
    {
        if (actual == null)
            return 0;

        var matches = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            if (!float.IsNaN(expected[i]) && !float.IsNaN(actual[i]) && MathF.Abs(expected[i] - actual[i]) <= 1e-3f)
                matches++;
        }

        return matches;
    }

    private static void Report(string message)
    {
        NoireService.ChatGui.Print(message);
        NoireLogger.LogInfo(message, "Draw3D");
    }
}
