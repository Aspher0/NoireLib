using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using NoireLib;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Materials;
using NoireLib.Draw3D.Scene;

namespace NoireDraw3DDemoPlugin.Windows.Sections;

/// <summary>
/// A front-end over the render-internal validators that stay in the library (<see cref="NoireDraw3D.Diagnostics"/>):
/// projection parity, the depth probe, the camera-phase "swim" trace, and the live stats. Also a world-geometry
/// collision preview, rebuilt here on the public API (it owns its own scene). All of these only read / measure.
/// </summary>
public sealed class DiagnosticsSection : IDisposable
{
    private int camTraceFrames = 120;
    private Scene3D? worldGeoScene;
    private string worldGeoStatus = "World-geometry preview off.";
    private bool commandEnabled;
    private string lastFault = "(none)";
    private readonly Action<Draw3DFault> onFault;

    /// <summary>Subscribes to the self-disable fault feed so the panel can show the last fault.</summary>
    public DiagnosticsSection()
    {
        onFault = fault => lastFault = $"[{fault.Kind}] {fault.Message}";
        NoireDraw3D.OnFault += onFault;
    }

    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        var diag = NoireDraw3D.Diagnostics;

        ImGui.TextUnformatted("Diagnostics command");
        using (SectionUi.Disabled(commandEnabled))
        {
            if (ImGui.Button(commandEnabled ? "/noire3d registered" : "Register /noire3d command"))
            {
                NoireDraw3D.EnableDiagnosticsCommand();
                commandEnabled = true;
            }
        }

        SectionUi.Hint("Registers the opt-in /noire3d chat command (stats, probe, stencil, heightmap, camtrace...). Off by default so the library never claims a command name behind your back.");
        ImGui.SameLine();
        ImGui.TextDisabled("Opt-in in-game console diagnostics.");

        ImGui.Separator();
        ImGui.TextUnformatted("Validators (results go to /xllog)");
        if (ImGui.Button("Run validate (projection parity)"))
            diag.RunValidate();
        SectionUi.Hint("Projects sample world points with Draw3D's own math and compares against the game's WorldToScreen over 10 frames. Gate: max <= 1 px. This is the wobble-class killer - run it in odd camera poses (orbit, grazing, first-person, max zoom).");
        ImGui.SameLine();
        if (ImGui.Button("Run probe (depth ground-truth)"))
            diag.RunProbe();
        SectionUi.Hint("Reads real depth-buffer values back and compares them against the analytic depth map rendering uses. Also reports the UI-mask alpha health - which tells you whether per-pixel 'Protect (game UI on top)' can work at all this frame.");

        ImGui.Separator();
        ImGui.TextWrapped("Camera-phase trace (\"swim\" investigation): measures overlay-vs-world drift under camera motion. Run it, then pan / zoom / orbit the camera hard.");
        ImGui.SetNextItemWidth(140f);
        ImGui.InputInt("Frames", ref camTraceFrames);
        ImGui.SameLine();
        if (ImGui.Button("Run camera-phase trace"))
            diag.RunCameraPhaseTrace(Math.Clamp(camTraceFrames, 1, 6000));
        SectionUi.Hint("Measures how far the camera the overlay was projected with drifts from a live read taken later in the same frame - the visible \"swim\" magnitude. Read-only; it only measures.");

        ImGui.Separator();
        ImGui.TextUnformatted("World-geometry preview (real collision around you)");
        if (ImGui.Button(worldGeoScene is { IsDisposed: false } ? "Hide world geometry" : "Show world geometry"))
            NoireService.Framework.RunOnFrameworkThread(ToggleWorldGeometry); // collision scene is framework-thread only
        SectionUi.Hint("Shows the game's real collision around you as a translucent mesh. This is what the collision height-map is built from, so it is the ground truth for a DecalProjection.HighestOnly decal. Note decals themselves project onto the DEPTH-buffer surface, not this. The Scenes tab has a per-scene version you can inspect and edit.");
        ImGui.SameLine();
        ImGui.TextDisabled(worldGeoStatus);

        ImGui.Separator();
        ImGui.TextUnformatted("Live stats");
        ImGui.TextUnformatted(diag.GetStatsText());

        ImGui.Separator();
        SectionUi.LabelValue("Last self-disable fault", lastFault);
    }

    /// <summary>Toggles a translucent shaded preview of the game's real collision world near the player (framework thread only).</summary>
    private void ToggleWorldGeometry()
    {
        if (worldGeoScene is { IsDisposed: false } existing)
        {
            existing.Dispose();
            worldGeoScene = null;
            worldGeoStatus = "World-geometry preview off.";
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
            worldGeoStatus = "No collision found near you (open area / airborne, or the read faulted - see /xllog).";
            return;
        }

        worldGeoStatus = "World-geometry preview ON - the real collision around you, translucent blue.";
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        NoireDraw3D.OnFault -= onFault;
        worldGeoScene?.Dispose();
        worldGeoScene = null;
    }
}
