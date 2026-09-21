using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using NoireDraw3DDemoPlugin.Services;
using System;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>
/// The showcase scene page: spawns and clears <see cref="ShowcaseSceneService"/>'s gallery and forwards the gizmo
/// backend and model-import controls to it.
/// </summary>
public sealed class ShowcasePage : IDisposable
{
    private readonly ShowcaseSceneService showcase = new();
    private string modelPath = string.Empty;

    public void Draw()
    {
        Ui.Section("Scene");
        if (Ui.IconButton(FontAwesomeIcon.Cubes, showcase.IsSpawned ? "Respawn here" : "Spawn here", 150f))
            showcase.Spawn();
        if (ImGui.IsItemHovered())
            Ui.Tooltip("Builds the gallery around where you are standing. Respawning moves it to you.");

        ImGui.SameLine();
        using (Ui.Disabled(!showcase.IsSpawned))
        {
            if (Ui.IconButton(FontAwesomeIcon.TrashAlt, "Clear", 110f))
                showcase.Clear();
        }

        if (!showcase.IsSpawned)
        {
            Ui.Gap();
            Ui.Note("Every primitive, every decal footprint, the material families, a custom pipeline, a textured quad, an "
                    + "RTT portal and the immediate layer, all in one scene, all of it selectable.");
            return;
        }

        ImGui.SameLine();
        Ui.Status(showcase.Status);

        Ui.Section("Stations");
        using (Ui.Form("showcase.stations", 110f))
        {
            Station("North", "Mesh primitives",
                "Box, sphere, cylinder, cone, torus, disc, ring, arrow, sector, an extruded path, and one mesh combining two builders. All Lit. The Lighting page moves them together.");
            Station("South", "Decal footprints",
                "Every decal shape with a constant-thickness rim, cut around characters standing in them. Three additive circles overlap to white.");
            Station("Far south", "Immediate layer",
                "Donut, sweeping pie, orbiting additive orb, spinning line. Redrawn every frame from OnPrepareFrame. There is no node to select.");
            Station("West", "Materials",
                "Additive sphere, a depth-faded quad softening where it meets the ground, and a box on a custom HLSL pipeline pulsing with EyePosTime.w.");
            Station("West, up", "RTT portal",
                "This scene rendered from a second camera above it. The texture only exists after the view's first frame. It starts as a dark placeholder.");
            Station("East", "Depth, textures",
                "A stack of rotated opaque boxes occluding each other through Draw3D's private depth, and a quad stamped with a game icon.");
        }

        if (showcase.Editor is { } editor)
        {
            Ui.Section("Gizmo");
            using (Ui.Form("showcase.gizmo"))
            {
                Ui.Enum("Backend", () => editor.Gizmo.Backend, v => editor.Gizmo.Backend = v,
                    "Native: in-world handles that occlude correctly. ImGuizmo: flat handles, always on top.");
                Ui.Value("Attached to", editor.Selection.Count switch
                {
                    0 => "-",
                    1 => editor.Selection.Primary?.Name ?? "?",
                    var n => $"{n} objects, primary {editor.Selection.Primary?.Name ?? "?"}",
                }, "Click an object in the world; Shift-click to add more.");
            }
        }

        Ui.Section("Model");
        using (Ui.Form("showcase.model"))
        {
            Ui.Text("glTF / glb", () => modelPath, v => modelPath = v, @"Absolute path", 512,
                "Blender: File > Export > glTF 2.0. Base colour and textures import; PBR maps, skins and animations are skipped and logged. Surrounding quotes are stripped.");
        }

        if (Ui.IconButton(FontAwesomeIcon.FileImport, "Load", 110f))
            showcase.SpawnModel(modelPath);
    }

    private static void Station(string where, string what, string detail)
    {
        Ui.Row(where);
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(what);
        Ui.HelpMarker(detail);
    }

    public void Dispose() => showcase.Dispose();
}
