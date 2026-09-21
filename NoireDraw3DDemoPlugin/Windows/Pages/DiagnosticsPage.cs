using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using NoireDraw3DDemoPlugin.Services;
using NoireLib;
using NoireLib.Draw3D;
using System;
using System.Numerics;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>
/// A front-end over the render validators in <see cref="NoireDraw3D.Diagnostics"/>, the live stats and the fault feed,
/// plus a world-collision preview built on the public API.
/// </summary>
public sealed class DiagnosticsPage : IDisposable
{
    private readonly Action<Draw3DFault> onFault;
    private readonly WorldGeometryPreviewService worldGeo = new();

    private string lastFault = string.Empty;
    private int faultCount;

    public DiagnosticsPage()
    {
        onFault = fault =>
        {
            lastFault = $"[{fault.Kind}] {fault.Message}";
            faultCount++;
        };

        NoireDraw3D.OnFault += onFault;
    }

    public void Draw()
    {
        DrawValidators();
        DrawWorldGeometry();
        DrawStats();
        DrawFaults();
    }

    private void DrawValidators()
    {
        var diag = NoireDraw3D.Diagnostics;
        var buttonSize = new Vector2(220f * Ui.Scale, 0f);

        Ui.Section("Validators");
        Ui.Note("Verdicts go to /xllog. Run them in awkward camera poses (orbiting, grazing, first-person, max zoom), since "
                + "that is where projection bugs show up.");
        Ui.Gap();

        if (Ui.Button("Run projection parity", buttonSize))
            diag.RunValidate();
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Compares this renderer's projection against the game's over 10 frames; passes within 1 pixel.");

        ImGui.SameLine();
        if (Ui.Button("Run depth ground-truth", buttonSize))
            diag.RunProbe();
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Validates depth-buffer readback against the analytic depth map, and reports UI-mask health.");

        Ui.Gap();
        Ui.Note("Pixel-convention calibration. Take a snapshot, turn the camera a quarter turn around your character, then "
                + "compare. The offset with the smallest error is the game's raster convention. Aligned, it is (0, 0).");
        Ui.Gap();
        if (Ui.Button("Calibration snapshot", buttonSize))
            diag.SnapshotDepthCalibration();
        ImGui.SameLine();
        if (Ui.Button("Calibration compare", buttonSize))
            diag.CompareDepthCalibration();

        if (Ui.Button("Ground height grid", buttonSize) && NoireService.ObjectTable.LocalPlayer is { } player)
            diag.ProbeGroundGrid(player.Position, 2f);
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Logs the game's rendered ground height under a 3 x 3 grid around you. On flat ground it must not change with the camera.");

        Ui.Gap();
        using (Ui.Form("diag.camera"))
        {
            Ui.Toggle("Remove temporal jitter", static () => NoireDraw3D.Diagnostics.RemoveTemporalJitter, static v => NoireDraw3D.Diagnostics.RemoveTemporalJitter = v,
                "Projects the layer with the centred camera when the game jitters its own.");
            Ui.Toggle("Temporal stabilization", static () => NoireDraw3D.Diagnostics.TemporalStabilization, static v => NoireDraw3D.Diagnostics.TemporalStabilization = v,
                "Accumulates the layer over frames so occlusion edges against jittered game depth settle.");
        }

        Ui.Gap();
        Ui.Note("/noire3d carries the rest (stencil, heightmap, uimask, plates, decalshapes, rtlog, reset). The library never "
                + "claims that name on its own. This demo opts in with NoireDraw3D.EnableDiagnosticsCommand().");
    }

    private void DrawWorldGeometry()
    {
        Ui.Section("World collision");
        Ui.Note("The real collision around you, translucent. This is what the height-map is built from, the ground truth "
                + "for a HighestOnly decal. Decals project onto the depth surface, and the two can disagree.");
        Ui.Gap();

        if (Ui.Button(worldGeo.IsOn ? "Hide world collision" : "Show world collision", new Vector2(220f * Ui.Scale, 0f)))
            NoireService.Framework.RunOnFrameworkThread(worldGeo.Toggle); // the collision scene is framework-thread only

        Ui.Gap();
        Ui.Status(worldGeo.Status);
    }

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
            Ui.Counter("ObjectCb updates", s.ObjectCbUpdates,
                "Constant-buffer uploads in the scene pass. Near the draw count means per-draw uploads. The batched-constants toggle on the Renderer page collapses it to the number of distinct material params.");
            Ui.Value("Visible / culled", $"{s.VisibleItems} / {s.CulledItems}",
                "Zero visible with content spawned means it is all off screen or invisible.");
            Ui.Value("Scene GPU", $"{s.SceneGpuMs:F3} ms");
            Ui.Value("Composite GPU", $"{s.CompositeGpuMs:F3} ms", "Includes the UI mask.");
            Ui.Value("Last pick", $"{s.LastPickMicros} us, {s.LastPickNodes} nodes, {s.LastPickRefined} refined",
                "Cost of the most recent hit test (hover picks every frame). High while hovering a dense scene = the pick side is the lag.");
            Ui.Counter("Plate rects", s.ProtectRects);
        }

        Ui.Section("Depth");
        using (Ui.Form("diag.depth"))
        {
            Ui.Value("Available", YesNo(s.DepthAvailable), s.DepthAvailable ? ImGuiColors.HealerGreen : ImGuiColors.DalamudYellow,
                "Without it, nothing hides behind world geometry and decals have no surface to land on.");
            Ui.Value("Source", s.DepthSource);
            Ui.Value("Fallback camera", YesNo(s.UsedFallbackCamera), s.UsedFallbackCamera ? ImGuiColors.DalamudYellow : ImGuiColors.HealerGreen,
                "This frame used a guessed view-projection. The real camera was unavailable. Placement is approximate. ImGuizmo drops to Native.");
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
                    "Draws referencing an already-disposed mesh or texture. These are skipped. Something is being freed while still in use.");

            if (s.ImCommandsDropped > 0)
                Ui.Value("Im commands dropped", s.ImCommandsDropped.ToString("N0"), ImGuiColors.DalamudYellow,
                    "The per-frame command buffer filled. Draw fewer, or retain them as nodes.");
        }
    }

    private void DrawFaults()
    {
        Ui.Section("Faults");
        Ui.Note("Draw3D disables itself to avoid risking the game. Toggling Enabled on the Renderer page re-arms it.");
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

    public void Dispose()
    {
        NoireDraw3D.OnFault -= onFault;
        worldGeo.Dispose();
    }
}
