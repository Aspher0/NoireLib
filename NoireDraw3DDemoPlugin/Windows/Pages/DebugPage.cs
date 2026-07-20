#if DEBUG
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using NoireLib;
using NoireLib.Draw3D;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>
/// Development tooling: render-pass probes, the G-buffer injection's raw channels, the live G-buffer
/// comparison and the import orientation overrides. Compiled only into debug builds; a release build has no
/// trace of this page.
/// </summary>
internal sealed class DebugPage
{
    private enum MiscRed
    {
        /// <summary>65504, what the channel holds where the game's geometry pass has not written it.</summary>
        Sentinel,

        /// <summary>1, the top of the range every other channel uses.</summary>
        One,

        /// <summary>0, the value the game's own geometry carries.</summary>
        Zero,
    }

    private MiscRed miscRed = MiscRed.Zero;
    private bool flatAlbedo;
    private Vector3 flatAlbedoColor = Vector3.Zero;

    private bool compareGBuffer;
    private readonly List<Vector4> cursorSample = [];
    private readonly List<Vector4> referenceSample = [];
    private bool sampleValid;

    private static readonly string[] TargetNames = ["normal + id", "material", "albedo", "misc", "geo normal"];

    /// <summary>Draws the page.</summary>
    public void Draw()
    {
        Ui.Section("Debug");
        Ui.Note("Development tooling. This page only exists in debug builds.");
        Ui.Gap();

        // The tab bar stays pinned; each tab scrolls its own body. The window hosts this page in a
        // non-scrolling child for exactly that reason.
        using var tabs = ImRaii.TabBar("##debugtabs");
        if (!tabs)
            return;

        using (var tab = ImRaii.TabItem("Probes"))
        {
            if (tab)
            {
                using var body = Ui.Scroll("##probesbody");
                if (body)
                    DrawProbes();
            }
        }

        using (var tab = ImRaii.TabItem("G-buffer"))
        {
            if (tab)
            {
                using var body = Ui.Scroll("##gbufferbody");
                if (body)
                {
                    DrawGameLitChannels();
                    Ui.Gap();
                    DrawGBufferCompare();
                }
            }
        }

        using (var tab = ImRaii.TabItem("Import"))
        {
            if (tab)
            {
                using var body = Ui.Scroll("##importbody");
                if (body)
                {
                    Ui.Section("Import orientation");
                    Ui.ImportFlips("debug.flips");
                }
            }
        }
    }

    // ---------------------------------------------------------------- probes

    /// <summary>One-shot diagnostics, each a chat command under the hood. Output lands in the Dalamud log.</summary>
    private static void DrawProbes()
    {
        Ui.Section("Render probes");
        Ui.Note("Each runs once and reports to the log.");
        Ui.Gap();

        ProbeButton("Bind log", "rtlog", "Captures one frame's render-target bind sequence.");
        ProbeButton("Frame dump", "framedump sweep", "Writes images of what each bind produced across one frame. Stalls that frame.");
        ProbeButton("G-buffer dump", "gbuffer", "Reads back the five G-buffer targets as images. Needs a bind log first.");
        ProbeButton("Shadow probe", "shadowprobe", "Records the depth-only binds and the VS constants at each one's first draw.");
        ProbeButton("Camera trace", "camtrace", "Reports the camera constant capture's state and discovery table.");

        Ui.Gap();
        Ui.Section("Toggles");
        Ui.Gap();

        ProbeButton("Stencil overlay", "stencil", "Logs the game stencil values in view until toggled off.");
        ProbeButton("Wireframe", "wire", "Draws this renderer's geometry as wireframe.");
        ProbeButton("GPU camera", "gpucam", "Switches between the captured camera constants and the struct snapshot.");
        ProbeButton("Stats", "stats", "Prints the renderer's counters.");
    }

    private static void ProbeButton(string label, string command, string description)
    {
        if (Ui.Button(label, new Vector2(140f * Ui.Scale, 0f)))
            NoireService.CommandManager.ProcessCommand($"/noire3d {command}");

        if (ImGui.IsItemHovered())
            Ui.Tooltip($"/noire3d {command}\n{description}");

        ImGui.SameLine();
        Ui.Mono($"/noire3d {command}", ImGuiColors.DalamudGrey3);
    }

    // ---------------------------------------------------------------- G-buffer channels

    /// <summary>The values the G-buffer injection writes. Defaults are the measured ones.</summary>
    private void DrawGameLitChannels()
    {
        Ui.Section("Injection channels");
        Ui.Note("What game-lit objects write into the game's frame. Defaults are the measured values; changes apply next frame.");
        Ui.Gap();

        using (Ui.Form("debug.gamelit.writes"))
        {
            Ui.Toggle("Write the color targets", () => NoireDraw3D.GameLit.WriteColor, v => NoireDraw3D.GameLit.WriteColor = v,
                "Off writes depth only, leaving an undescribed surface in the frame.");
            Ui.Toggle("Write depth", () => NoireDraw3D.GameLit.WriteDepth, v => NoireDraw3D.GameLit.WriteDepth = v,
                "Off removes the object entirely: later passes draw over its pixels.");
        }

        if (NoireDraw3D.GameLit.CastShadows)
        {
            var (entered, drawn, skipped, meshes) = NoireDraw3D.ShadowCastStats;
            Ui.Mono($"shadow binds: entered {entered}  drawn {drawn}  skipped {skipped}  meshes {meshes}", ImGuiColors.DalamudGrey3);
        }

        Ui.Gap();

        using (Ui.Form("debug.gamelit"))
        {
            Ui.Enum<MiscRed>("Misc red", () => miscRed, v =>
            {
                miscRed = v;
                var misc = NoireDraw3D.GameLit.Misc;
                NoireDraw3D.GameLit.Misc = misc with { X = RedValue(v) };
            }, "rtv3 red. 0 is what the game's geometry writes; 65504 is what the channel holds where nothing wrote it.");

            Ui.Slider("Misc green", () => NoireDraw3D.GameLit.Misc.Y, v => NoireDraw3D.GameLit.Misc = NoireDraw3D.GameLit.Misc with { Y = v }, 0f, 1f,
                "Measured 0 across the game's buffer.");
            Ui.Slider("Misc blue", () => NoireDraw3D.GameLit.Misc.Z, v => NoireDraw3D.GameLit.Misc = NoireDraw3D.GameLit.Misc with { Z = v }, 0f, 1f,
                "Scales the model's baked per-vertex occlusion. 1 writes what the game writes.");
            Ui.Slider("Misc alpha", () => NoireDraw3D.GameLit.Misc.W, v => NoireDraw3D.GameLit.Misc = NoireDraw3D.GameLit.Misc with { W = v }, 0f, 1f,
                "Reads 1 on the game's geometry.");

            Ui.Slider("Replace the material map", () => NoireDraw3D.GameLit.MaterialOverride, v => NoireDraw3D.GameLit.MaterialOverride = v, 0f, 1f,
                "Blends the specular map toward the flat values below. 0 writes the map as authored.");
            Ui.Slider3("Material values", () => NoireDraw3D.GameLit.MaterialParams, v => NoireDraw3D.GameLit.MaterialParams = v, 0f, 1f,
                "rtv1 scalars: red = reflection strength, green = highlight, blue = darkening.");
            Ui.Slider("Material ceiling", () => NoireDraw3D.GameLit.MaterialCeiling, v => NoireDraw3D.GameLit.MaterialCeiling = v, 0.5f, 1f,
                "rtv1 clamp. The top of red's range selects a mode: 1.0 turns the reflection green.");

            Ui.Int("Shading model", () => NoireDraw3D.GameLit.ShadingModelId, v => NoireDraw3D.GameLit.ShadingModelId = (byte)Math.Clamp(v, 0, 255),
                "rtv0 alpha. 128 = world geometry, 32 = characters.");

            Ui.Int("Stencil value", () => (int)NoireDraw3D.GameLit.Stencil, v => NoireDraw3D.GameLit.Stencil = (uint)Math.Clamp(v, 0, 255),
                "The geometry-pass mark the deferred lights test. 16 is what the game's world writes; without a lit mark the object receives no light.");

            Ui.Toggle("Force a flat albedo", () => flatAlbedo, v =>
            {
                flatAlbedo = v;
                NoireDraw3D.GameLit.AlbedoOverride = new Vector4(flatAlbedoColor, v ? 1f : 0f);
            }, "Replaces the material's albedo with the color below.");

            Ui.Color3("Flat albedo color", () => flatAlbedoColor, v =>
            {
                flatAlbedoColor = v;
                if (flatAlbedo)
                    NoireDraw3D.GameLit.AlbedoOverride = new Vector4(v, 1f);
            }, "The forced albedo.");
        }

        Ui.Gap();
        if (Ui.IconButton(FontAwesomeIcon.Undo, "Restore the measured defaults"))
        {
            NoireDraw3D.GameLit.Reset();
            miscRed = MiscRed.Zero;
            flatAlbedo = false;
            flatAlbedoColor = Vector3.Zero;
        }
    }

    // ---------------------------------------------------------------- G-buffer compare

    /// <summary>Reads the game's G-buffer under the cursor and diffs it against a held reading.</summary>
    private void DrawGBufferCompare()
    {
        Ui.Section("G-buffer compare");
        Ui.Note("Samples all five targets under the cursor. Hold a reading, then hover another surface to diff. Values are pre-lighting. Needs one bind log run first.");
        Ui.Gap();

        using (Ui.Form("debug.gbuffer.compare"))
        {
            Ui.Toggle("Sample under the cursor", () => compareGBuffer, v => compareGBuffer = v,
                "Reads a small patch at the mouse position every frame while on.");
        }

        if (!compareGBuffer)
            return;

        // Sampling freezes while the cursor is over a window, so reaching for the hold button does not
        // overwrite the reading it is about to hold.
        var overWorld = !ImGui.GetIO().WantCaptureMouse;
        if (overWorld)
            sampleValid = NoireDraw3D.TrySampleGameGBuffer(ImGui.GetMousePos(), cursorSample);

        Ui.Gap();

        if (!sampleValid || cursorSample.Count == 0)
        {
            Ui.Callout(
                cursorSample.Count == 0 && overWorld
                    ? "No G-buffer identified yet. Run the bind log once."
                    : "Hover a surface in the world to take a reading.",
                ImGuiColors.DalamudOrange);
            return;
        }

        if (Ui.IconButton(FontAwesomeIcon.Crosshairs, "Hold this as the reference"))
        {
            referenceSample.Clear();
            referenceSample.AddRange(cursorSample);
        }

        ImGui.SameLine();
        Ui.Mono(overWorld ? "reading live" : "held", overWorld ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey3);

        Ui.Gap();

        for (var i = 0; i < cursorSample.Count; i++)
        {
            var name = i < TargetNames.Length ? TargetNames[i] : $"rtv{i}";
            var v = cursorSample[i];
            Ui.Mono($"rtv{i} {name,-12} {v.X,7:F3} {v.Y,7:F3} {v.Z,7:F3} {v.W,7:F3}");

            if (i >= referenceSample.Count)
                continue;

            var r = referenceSample[i];
            var d = v - r;
            var worst = MathF.Max(MathF.Abs(d.X), MathF.Max(MathF.Abs(d.Y), MathF.Max(MathF.Abs(d.Z), MathF.Abs(d.W))));
            Ui.Mono($"     vs reference {d.X,7:F3} {d.Y,7:F3} {d.Z,7:F3} {d.W,7:F3}",
                worst < 0.01f ? ImGuiColors.HealerGreen : worst < 0.05f ? ImGuiColors.DalamudYellow : ImGuiColors.DalamudRed);

            // The ratio makes readings comparable without landing on the same texel twice: steady across a
            // region means the texture cancels and what remains is a factor one side is not applying.
            Ui.Mono($"     ratio        {Ratio(v.X, r.X)} {Ratio(v.Y, r.Y)} {Ratio(v.Z, r.Z)} {Ratio(v.W, r.W)}",
                ImGuiColors.DalamudGrey3);
        }
    }

    private static string Ratio(float sample, float reference) =>
        MathF.Abs(reference) < 1e-3f ? "      -" : $"{sample / reference,7:F3}";

    private static float RedValue(MiscRed choice) => choice switch
    {
        MiscRed.One => 1f,
        MiscRed.Zero => 0f,
        _ => Draw3DGameLit.MiscRedSentinel,
    };
}
#endif
