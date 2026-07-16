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

        ImGui.SameLine();
        ImGui.TextDisabled("Opt-in in-game console diagnostics.");

        ImGui.Separator();
        ImGui.TextUnformatted("Validators (results go to /xllog)");
        if (ImGui.Button("Run validate (projection parity)"))
            diag.RunValidate();
        ImGui.SameLine();
        if (ImGui.Button("Run probe (depth ground-truth)"))
            diag.RunProbe();

        ImGui.Separator();
        ImGui.TextWrapped("Camera-phase trace (\"swim\" investigation): measures overlay-vs-world drift under camera motion. Run it, then pan / zoom / orbit the camera hard.");
        ImGui.SetNextItemWidth(140f);
        ImGui.InputInt("Frames", ref camTraceFrames);
        ImGui.SameLine();
        if (ImGui.Button("Run camera-phase trace"))
            diag.RunCameraPhaseTrace(Math.Clamp(camTraceFrames, 1, 6000));

        ImGui.Separator();
        ImGui.TextUnformatted("World-geometry preview (real collision around you)");
        if (ImGui.Button(worldGeoScene is { IsDisposed: false } ? "Hide world geometry" : "Show world geometry"))
            NoireService.Framework.RunOnFrameworkThread(ToggleWorldGeometry); // collision scene is framework-thread only
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
