using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using NoireLib;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;
using System;
using System.Numerics;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>
/// A front-end over the render-internal validators that stay in the library (<see cref="NoireDraw3D.Diagnostics"/>):
/// projection parity, the depth probe, the camera-phase "swim" trace, the live stats and the fault feed. Plus a
/// world-collision preview rebuilt here on the public API (it owns its own scene). Everything here only reads or
/// measures - nothing on this page changes what you see in the world.
/// </summary>
public sealed class DiagnosticsPage : IDisposable
{
    private readonly Action<Draw3DFault> onFault;

    private int camTraceFrames = 120;
    private Scene3D? worldGeoScene;
    private string worldGeoStatus = string.Empty;
    private string lastFault = string.Empty;
    private int faultCount;

    /// <summary>Subscribes to the self-disable fault feed so the page can show the last fault.</summary>
    public DiagnosticsPage()
    {
        onFault = fault =>
        {
            lastFault = $"[{fault.Kind}] {fault.Message}";
            faultCount++;
        };

        NoireDraw3D.OnFault += onFault;
    }

    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        DrawValidators();
        DrawWorldGeometry();
        DrawStats();
        DrawFaults();
    }

    // ---------------------------------------------------------------- validators

    private void DrawValidators()
    {
        var diag = NoireDraw3D.Diagnostics;
        var buttonSize = new Vector2(220f * Ui.Scale, 0f);

        Ui.Section("Validators");
        Ui.Note("Verdicts go to /xllog. Run them in awkward camera poses - orbiting, grazing, first-person, max zoom - since "
                + "that is where projection bugs show up.");
        Ui.Gap();

        if (ImGui.Button("Run projection parity", buttonSize))
            diag.RunValidate();
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Projects sample world points with Draw3D's own math and compares them against the game's own WorldToScreen, over 10 frames. The gate is a maximum of 1 pixel of disagreement.\n\nThis is the wobble-class killer: if the overlay ever drifts from the world, this is what catches it.");

        ImGui.SameLine();
        if (ImGui.Button("Run depth ground-truth", buttonSize))
            diag.RunProbe();
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Reads real depth-buffer values back and compares them against the analytic depth map rendering uses.\n\nAlso reports the UI-mask alpha health, which tells you whether the per-pixel 'Keep game UI on top' mask can work at all on this frame.");

        Ui.Gap();
        Ui.Note("Camera-phase trace: how far the camera an overlay was projected with drifts from a live read later in the same "
                + "frame. That gap is the visible swim under motion. Start it, then pan and orbit hard.");
        Ui.Gap();
        using (Ui.Form("diag.camtrace"))
        {
            Ui.Int("Frames to trace", () => camTraceFrames, v => camTraceFrames = Math.Clamp(v, 1, 6000),
                "How many frames to sample before reporting. 120 is about two seconds; raise it if you need more time to provoke the motion.");
        }

        if (ImGui.Button("Run camera-phase trace", buttonSize))
            diag.RunCameraPhaseTrace(Math.Clamp(camTraceFrames, 1, 6000));

        Ui.Gap();
        Ui.Note("/noire3d carries the rest: stencil, heightmap, uimask, plates, decalshapes, rtlog, reset. The library never "
                + "claims that name on its own - this demo opts in with NoireDraw3D.EnableDiagnosticsCommand().");
    }

    // ---------------------------------------------------------------- world geometry

    private void DrawWorldGeometry()
    {
        var on = worldGeoScene is { IsDisposed: false };

        Ui.Section("World collision");
        Ui.Note("The real collision around you, translucent. This is what the height-map is built from, so it is ground truth "
                + "for a HighestOnly decal. Decals themselves project onto the DEPTH surface, not this - the two can disagree, "
                + "and seeing both is the point.");
        Ui.Gap();

        if (ImGui.Button(on ? "Hide world collision" : "Show world collision", new Vector2(220f * Ui.Scale, 0f)))
            NoireService.Framework.RunOnFrameworkThread(ToggleWorldGeometry); // the collision scene is framework-thread only

        Ui.Gap();
        Ui.Status(worldGeoStatus);
    }

    /// <summary>Toggles a translucent shaded preview of the game's real collision world near the player (framework thread only).</summary>
    private void ToggleWorldGeometry()
    {
        if (worldGeoScene is { IsDisposed: false } existing)
        {
            existing.Dispose();
            worldGeoScene = null;
            worldGeoStatus = string.Empty;
            return;
        }

        var center = NoireService.ObjectTable.LocalPlayer?.Position ?? Vector3.Zero;
        var scene = worldGeoScene = NoireDraw3D.CreateScene("worldgeo");
        var mat = Material.Lit(new Vector4(0.35f, 0.75f, 1f, 0.4f)) with { Cull = CullMode.None, Blend = BlendMode.Premultiplied };
        var node = scene.SpawnWorldGeometry(center, 20f, mat, includeAnalytic: true, name: "WorldGeo");
        if (node == null)
        {
            scene.Dispose();
            worldGeoScene = null;
            worldGeoStatus = "No collision found near you - you may be in an open area or airborne, or the read faulted (see /xllog).";
            return;
        }

        worldGeoStatus = "Showing the real collision around you, in translucent blue.";
    }

    // ---------------------------------------------------------------- stats

    /// <summary>
    /// The live stats, broken out field by field rather than dumped as one string, so each number carries its own
    /// explanation of what it means and when it is a problem.
    /// </summary>
    private static void DrawStats()
    {
        var s = NoireDraw3D.Stats;

        Ui.Section("This frame");
        using (Ui.Form("diag.frame"))
        {
            Ui.Counter("Draw calls", s.DrawCalls, "Batching and instancing pull this well below the object count.");
            Ui.Counter("Instances", s.Instances);
            Ui.Counter("Batches", s.Batches);
            Ui.Counter("Triangles", s.Triangles);
            Ui.Value("Visible / culled", $"{s.VisibleItems} / {s.CulledItems}",
                "Zero visible with content spawned means it is all off screen or invisible.");
            Ui.Value("Scene GPU", $"{s.SceneGpuMs:F3} ms");
            Ui.Value("Composite GPU", $"{s.CompositeGpuMs:F3} ms", "Includes the UI mask.");
            Ui.Counter("Plate rects", s.ProtectRects);
        }

        Ui.Section("Depth");
        using (Ui.Form("diag.depth"))
        {
            Ui.Value("Available", YesNo(s.DepthAvailable), s.DepthAvailable ? ImGuiColors.HealerGreen : ImGuiColors.DalamudYellow,
                "Without it, nothing hides behind world geometry and decals have no surface to land on.");
            Ui.Value("Source", s.DepthSource);
            Ui.Value("Fallback camera", YesNo(s.UsedFallbackCamera), s.UsedFallbackCamera ? ImGuiColors.DalamudYellow : ImGuiColors.HealerGreen,
                "This frame guessed a view-projection instead of reading the real camera. Placement is approximate; ImGuizmo drops to Native.");
            Ui.Counter("Frames without", s.DepthOffFrames);
        }

        Ui.Section("Frames");
        using (Ui.Form("diag.frames"))
        {
            Ui.Counter("Rendered", s.FramesRendered);
            Ui.Counter("Skip: disabled", s.FramesSkippedDisabled);
            Ui.Counter("Skip: init", s.FramesSkippedInitPending);
            Ui.Counter("Skip: no device", s.FramesSkippedNoDevice);
            Ui.Counter("Skip: no camera", s.FramesSkippedNoCamera, "Loading or title screen.");
            Ui.Counter("Skip: zero size", s.FramesSkippedZeroSize);
            Ui.Counter("Skip: empty", s.FramesSkippedEmpty, "Nothing to draw. Expected while no scene has content.");
            Ui.Counter("Skip: ui hidden", s.FramesSkippedUiHidden, "Moves when 'Keep 3D layer' is off and the game UI is hidden.");
        }

        if (s.DisposedAssetDraws == 0 && s.ImCommandsDropped == 0)
            return;

        Ui.Section("Warnings");
        using (Ui.Form("diag.warnings"))
        {
            if (s.DisposedAssetDraws > 0)
                Ui.Value("Disposed-asset draws", s.DisposedAssetDraws.ToString("N0"), ImGuiColors.DalamudYellow,
                    "Draws referencing an already-disposed mesh or texture. Skipped rather than crashed, but something is being freed while still in use.");

            if (s.ImCommandsDropped > 0)
                Ui.Value("Im commands dropped", s.ImCommandsDropped.ToString("N0"), ImGuiColors.DalamudYellow,
                    "The per-frame command buffer filled. Draw fewer, or retain them as nodes.");
        }
    }

    // ---------------------------------------------------------------- faults

    private void DrawFaults()
    {
        Ui.Section("Faults");
        Ui.Note("Draw3D disables itself rather than risk the game. Toggling Enabled on the Renderer page re-arms it.");
        Ui.Gap();

        using (Ui.Form("diag.faults"))
        {
            if (faultCount == 0)
            {
                Ui.Value("Faults this session", "none", ImGuiColors.HealerGreen, "The renderer has not self-disabled since the plugin loaded.");
                return;
            }

            Ui.Value("Faults this session", faultCount.ToString(), ImGuiColors.DalamudRed);
            Ui.Value("Most recent", lastFault, ImGuiColors.DalamudRed);
        }
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    /// <inheritdoc/>
    public void Dispose()
    {
        NoireDraw3D.OnFault -= onFault;
        worldGeoScene?.Dispose();
        worldGeoScene = null;
    }
}
