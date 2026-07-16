using System;
using Dalamud.Bindings.ImGui;
using NoireLib.Draw3D;

namespace NoireDraw3DDemoPlugin.Windows.Sections;

/// <summary>
/// A live editor over every global Draw3D knob - the render master switches, decal behavior, the grouped native-UI
/// layering, stylized lighting, and the interaction tuning. Each control reads and writes the live setting directly.
/// </summary>
public sealed class GlobalSettingsSection
{
    private bool wireframe; // mirror of the internal NoireDraw3D.Wireframe (toggled via Diagnostics.ToggleWireframe)

    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        if (ImGui.CollapsingHeader("Renderer", ImGuiTreeNodeFlags.DefaultOpen))
        {
            SectionUi.Toggle("Enabled", static () => NoireDraw3D.Enabled, static v => NoireDraw3D.Enabled = v);
            SectionUi.Slider("Layer opacity", static () => NoireDraw3D.LayerOpacity, static v => NoireDraw3D.LayerOpacity = v, 0f, 1f);
            if (ImGui.Checkbox("Wireframe", ref wireframe))
                wireframe = NoireDraw3D.Diagnostics.ToggleWireframe();
            SectionUi.Toggle("Keep drawing when UI hidden", static () => NoireDraw3D.KeepDrawingWhenUiHidden, static v => NoireDraw3D.KeepDrawingWhenUiHidden = v);
        }

        if (ImGui.CollapsingHeader("Decals", ImGuiTreeNodeFlags.DefaultOpen))
        {
            SectionUi.Toggle("World-occluded decals", static () => NoireDraw3D.WorldOccludedDecals, static v => NoireDraw3D.WorldOccludedDecals = v);
            var stencil = (int)NoireDraw3D.CharacterStencilValue;
            if (ImGui.InputInt("Character stencil value", ref stencil))
                NoireDraw3D.CharacterStencilValue = (uint)Math.Max(0, stencil);
            SectionUi.Slider("World-occlusion threshold (m)", static () => NoireDraw3D.WorldOcclusionThreshold, static v => NoireDraw3D.WorldOcclusionThreshold = v, 0f, 1f);
        }

        if (ImGui.CollapsingHeader("Native UI layering", ImGuiTreeNodeFlags.DefaultOpen))
        {
            SectionUi.Toggle("Protect (game UI on top)", static () => NoireDraw3D.NativeUi.Protect, static v => NoireDraw3D.NativeUi.Protect = v);
            SectionUi.EnumCombo("Protection mode", static () => NoireDraw3D.NativeUi.Protection, static v => NoireDraw3D.NativeUi.Protection = v);
            SectionUi.Slider("Dim factor", static () => NoireDraw3D.NativeUi.DimFactor, static v => NoireDraw3D.NativeUi.DimFactor = v, 0f, 1f);
            SectionUi.Toggle("Render under native UI", static () => NoireDraw3D.NativeUi.RenderUnder, static v => NoireDraw3D.NativeUi.RenderUnder = v);
            SectionUi.Toggle("Native-UI depth write", static () => NoireDraw3D.NativeUi.DepthWrite, static v => NoireDraw3D.NativeUi.DepthWrite = v);
        }

        if (ImGui.CollapsingHeader("Lighting (Lit materials)"))
        {
            var light = NoireDraw3D.Lighting;
            SectionUi.Color3("Ambient color", () => light.AmbientColor, v => light.AmbientColor = v);
            SectionUi.Slider("Ambient intensity", () => light.AmbientIntensity, v => light.AmbientIntensity = v, 0f, 1f);
            SectionUi.Float3("Light direction", () => light.LightDirection, v => light.LightDirection = v, -1f, 1f);
            SectionUi.Color3("Light color", () => light.LightColor, v => light.LightColor = v);
            SectionUi.Slider("Light intensity", () => light.LightIntensity, v => light.LightIntensity = v, 0f, 2f);
        }

        if (ImGui.CollapsingHeader("Interaction"))
        {
            var it = NoireDraw3D.Interaction;
            SectionUi.Toggle("Enabled", () => it.Enabled, v => it.Enabled = v);
            SectionUi.Toggle("Block game mouse on hover", () => it.BlockGameMouseOnHover, v => it.BlockGameMouseOnHover = v);
            SectionUi.Toggle("Select on click", () => it.SelectOnClick, v => it.SelectOnClick = v);
            SectionUi.Toggle("Game UI blocks interaction", () => it.GameUiBlocksInteraction, v => it.GameUiBlocksInteraction = v);
            SectionUi.Slider("Drag threshold (px)", () => it.DragThresholdPixels, v => it.DragThresholdPixels = v, 0f, 20f);
            SectionUi.EnumCombo("Wall occlusion", () => it.WallOcclusion, v => it.WallOcclusion = v);
            SectionUi.Slider("Wall-occlusion bias (m)", () => it.WallOcclusionBias, v => it.WallOcclusionBias = v, 0f, 2f);
            SectionUi.EnumCombo("Deselect on", () => it.DeselectOn, v => it.DeselectOn = v);
            SectionUi.Toggle("Debug log", () => it.DebugLog, v => it.DebugLog = v);
        }
    }
}
