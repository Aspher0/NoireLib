using System;
using Dalamud.Bindings.ImGui;
using NoireLib.Draw3D;

namespace NoireDraw3DDemoPlugin.Windows.Sections;

/// <summary>
/// A live editor over every global Draw3D knob - the render master switches, decal behavior, the grouped native-UI
/// layering, stylized lighting, and the interaction tuning (including key/modifier pickers for the gesture predicates).
/// Each control reads and writes the live setting directly, and carries a "(?)" hint explaining what it does.
/// </summary>
public sealed class GlobalSettingsSection
{
    private enum ModifierChoice { Ctrl, Shift, Alt, None }
    private enum DeselectKeyChoice { Escape, Delete, Backspace, None }

    private bool wireframe; // mirror of the internal NoireDraw3D.Wireframe (toggled via Diagnostics.ToggleWireframe)

    // Local mirrors of the Func<bool> gesture predicates (a lambda can't be read back, so we track the choice here,
    // seeded to the library defaults: Ctrl toggle, Shift add, Alt click-through, Escape deselect).
    private int toggleModIdx = (int)ModifierChoice.Ctrl;
    private int addModIdx = (int)ModifierChoice.Shift;
    private int clickThroughModIdx = (int)ModifierChoice.Alt;
    private int deselectKeyIdx = (int)DeselectKeyChoice.Escape;

    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        if (ImGui.CollapsingHeader("Renderer", ImGuiTreeNodeFlags.DefaultOpen))
        {
            SectionUi.Toggle("Enabled", static () => NoireDraw3D.Enabled, static v => NoireDraw3D.Enabled = v,
                "Master switch for the whole 3D layer. Off draws nothing (and re-arms the renderer after a fault when set back on).");
            SectionUi.Slider("Layer opacity", static () => NoireDraw3D.LayerOpacity, static v => NoireDraw3D.LayerOpacity = v, 0f, 1f,
                "0-1 opacity applied to the entire 3D layer at composite time.");
            if (ImGui.Checkbox("Wireframe", ref wireframe))
                wireframe = NoireDraw3D.Diagnostics.ToggleWireframe();
            SectionUi.Hint("Renders every scene mesh as wireframe (diagnostic rasterizer state).");
            SectionUi.Toggle("Keep drawing when UI hidden", static () => NoireDraw3D.KeepDrawingWhenUiHidden, static v => NoireDraw3D.KeepDrawingWhenUiHidden = v,
                "Keeps the 3D layer rendering while the game UI is hidden (hotkey / cutscene / gpose). The 3D layer draws inside the plugin-UI callback, so this necessarily keeps THIS plugin's ImGui windows drawable too - they are coupled by the render mechanism. Turn it off if you want the whole plugin (windows and 3D) to vanish with the game UI.");
        }

        if (ImGui.CollapsingHeader("Decals", ImGuiTreeNodeFlags.DefaultOpen))
        {
            SectionUi.Toggle("World-occluded decals", static () => NoireDraw3D.WorldOccludedDecals, static v => NoireDraw3D.WorldOccludedDecals = v,
                "Ground decals cut actors by their real elevation using the cached collision world (needed for DecalProjection.HighestOnly and exact stencil cut-outs). Off falls back to the coarse ExcludeVolumes cylinders.");
            var stencil = (int)NoireDraw3D.CharacterStencilValue;
            if (ImGui.InputInt("Character stencil value", ref stencil))
                NoireDraw3D.CharacterStencilValue = (uint)Math.Max(0, stencil);
            SectionUi.Hint("The game stencil value that marks characters, used to cut them out of decals along their exact silhouette. Default 0x08 (8); 0 disables. Discover it with /noire3d stencil.");
            SectionUi.Slider("World-occlusion threshold (m)", static () => NoireDraw3D.WorldOcclusionThreshold, static v => NoireDraw3D.WorldOcclusionThreshold = v, 0f, 1f,
                "How far above the local ground a surface must be before a world-occluded decal stops painting it (tabletop vs floor separation).");
        }

        if (ImGui.CollapsingHeader("Native UI layering", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.TextDisabled("These govern how the game's HUD / nameplates layer over the 3D. They only do something on the");
            ImGui.TextDisabled("\"render under native UI\" path below - with it off, the whole layer is simply on top of the game UI.");
            SectionUi.Toggle("Render under native UI", static () => NoireDraw3D.NativeUi.RenderUnder, static v => NoireDraw3D.NativeUi.RenderUnder = v,
                "Composite the 3D layer before the game draws its native UI, so the HUD / nameplates read on top per-pixel (uses a render-thread present-composition hook). Off = the whole layer draws over everything, no hook. The four settings below only take effect while this is on.");
            SectionUi.Toggle("Protect (game UI on top)", static () => NoireDraw3D.NativeUi.Protect, static v => NoireDraw3D.NativeUi.Protect = v,
                "Keeps the game's native UI drawn on top of the 3D layer (per-pixel). Note: in FFXIV the native UI frequently writes no coverage alpha, so per-pixel protection can read as inert even when on - a documented engine limitation (check /noire3d probe's ui-mask health).");
            SectionUi.EnumCombo("Protection mode", static () => NoireDraw3D.NativeUi.Protection, static v => NoireDraw3D.NativeUi.Protection = v,
                "How nameplates layer against 3D content: DepthAware lets a nearer 3D object cover a plate; AlwaysVisible always keeps plates on top. Needs nameplates in view to observe a difference.");
            SectionUi.Slider("Dim factor", static () => NoireDraw3D.NativeUi.DimFactor, static v => NoireDraw3D.NativeUi.DimFactor = v, 0f, 1f,
                "How much a nameplate behind your content still shows through it: 0 = fully covered, toward 1 = faintly readable.");
            SectionUi.Toggle("Native-UI depth write", static () => NoireDraw3D.NativeUi.DepthWrite, static v => NoireDraw3D.NativeUi.DepthWrite = v,
                "Writes the layer's opaque depth into the game's scene depth so nameplates are occluded by 3D objects standing in front of characters. Needs \"render under native UI\" on.");
        }

        if (ImGui.CollapsingHeader("Lighting (Lit materials)"))
        {
            var light = NoireDraw3D.Lighting;
            SectionUi.Color3("Ambient color", () => light.AmbientColor, v => light.AmbientColor = v, "Ambient light color for Material.Lit shading.");
            SectionUi.Slider("Ambient intensity", () => light.AmbientIntensity, v => light.AmbientIntensity = v, 0f, 1f, "Ambient light strength.");
            SectionUi.Float3("Light direction", () => light.LightDirection, v => light.LightDirection = v, -1f, 1f, "Direction TOWARD the light source (normalized on upload).");
            SectionUi.Color3("Light color", () => light.LightColor, v => light.LightColor = v, "Directional light color.");
            SectionUi.Slider("Light intensity", () => light.LightIntensity, v => light.LightIntensity = v, 0f, 2f, "Directional light strength.");
        }

        if (ImGui.CollapsingHeader("Interaction"))
            DrawInteraction();
    }

    private void DrawInteraction()
    {
        var it = NoireDraw3D.Interaction;
        SectionUi.Toggle("Enabled", () => it.Enabled, v => it.Enabled = v,
            "Master switch for pointer interaction (hover / click / drag / gizmo). Off = the game keeps every click.");
        SectionUi.Toggle("Auto-run (drive from UiBuilder.Draw)", () => it.AutoRun, v => it.AutoRun = v,
            "Interaction advances itself every frame. Turn off only if you call NoireDraw3D.Interaction.Update() yourself.");
        SectionUi.Toggle("Block game mouse on hover", () => it.BlockGameMouseOnHover, v => it.BlockGameMouseOnHover = v,
            "Whether merely hovering an interactable node claims the mouse from the game (blocks camera pan / targeting). Off is the world-overlay-friendly default; the mouse is only claimed once you actually drag.");
        SectionUi.Toggle("Select on click", () => it.SelectOnClick, v => it.SelectOnClick = v,
            "Whether a left-click on a selectable node updates its scene selection (with the toggle / add modifiers below).");
        SectionUi.Toggle("Game UI blocks interaction", () => it.GameUiBlocksInteraction, v => it.GameUiBlocksInteraction = v,
            "Whether native game UI under the cursor stops Draw3D from hovering / picking a 3D object behind it.");
        SectionUi.Toggle("Debug log", () => it.DebugLog, v => it.DebugLog = v,
            "Logs the click / hover / capture / game-UI-gate pipeline to /xllog for diagnosing why a click did or didn't register in-game. Verbose; leave off normally.");
        SectionUi.Slider("Drag threshold (px)", () => it.DragThresholdPixels, v => it.DragThresholdPixels = v, 0f, 20f,
            "How far (in screen pixels) a left press must move before it counts as a drag rather than a click. Governs gizmo-handle and draggable-node drags: below this it's treated as a click and the camera isn't claimed.");

        SectionUi.SeparatorText("Wall occlusion");
        SectionUi.EnumCombo("Wall occlusion", () => it.WallOcclusion, v => it.WallOcclusion = v,
            "How game-world geometry (walls / terrain) in front of a 3D object affects hovering / clicking it: Off = always clickable through walls; Occlude = a wall blocks the click; HoldToClickThrough = a wall blocks it unless the modifier below is held.");
        SectionUi.Slider("Wall-occlusion bias (m)", () => it.WallOcclusionBias, v => it.WallOcclusionBias = v, 0f, 2f,
            "Slack added to the wall distance before an object counts as occluded (avoids flicker at grazing angles).");

        SectionUi.SeparatorText("Deselect");
        SectionUi.EnumCombo("Deselect on", () => it.DeselectOn, v => it.DeselectOn = v,
            "How selections clear: ClickEmpty = clicking empty world; Key = pressing the deselect key below; combine both. Clears every scene's selection.");
        if (SectionUi.EnumCombo<DeselectKeyChoice>("Deselect key", ref deselectKeyIdx,
            "The key that clears the selection when \"Deselect on\" includes Key."))
            it.DeselectKey = DeselectKeyFunc((DeselectKeyChoice)deselectKeyIdx);

        SectionUi.SeparatorText("Selection & click-through modifiers");
        if (SectionUi.EnumCombo<ModifierChoice>("Toggle-select held", ref toggleModIdx,
            "Held while left-clicking to toggle a node in/out of a multi-select (needs the editor in multi-select mode)."))
            it.ToggleSelectionHeld = ModifierFunc((ModifierChoice)toggleModIdx);
        if (SectionUi.EnumCombo<ModifierChoice>("Add-select held", ref addModIdx,
            "Held while left-clicking to add a node to a multi-select without removing others."))
            it.AddSelectionHeld = ModifierFunc((ModifierChoice)addModIdx);
        if (SectionUi.EnumCombo<ModifierChoice>("Click-through held", ref clickThroughModIdx,
            "Held to click through a wall when Wall occlusion is set to HoldToClickThrough."))
            it.ClickThroughHeld = ModifierFunc((ModifierChoice)clickThroughModIdx);

        SectionUi.SeparatorText("Live status (read-only)");
        SectionUi.LabelValue("Hovered node", it.HoveredNode?.Name ?? "(none)");
        SectionUi.LabelValue("Interacting", it.IsInteracting ? "yes" : "no");
        SectionUi.LabelValue("Capturing mouse", it.IsCapturingMouse ? "yes" : "no");
        SectionUi.LabelValue("Foreign UI has mouse", it.ForeignUiHasMouse ? "yes" : "no");
    }

    /// <summary>Maps a modifier choice to the held-key predicate the interaction layer polls each frame.</summary>
    private static Func<bool> ModifierFunc(ModifierChoice choice) => choice switch
    {
        ModifierChoice.Ctrl => static () => ImGui.GetIO().KeyCtrl,
        ModifierChoice.Shift => static () => ImGui.GetIO().KeyShift,
        ModifierChoice.Alt => static () => ImGui.GetIO().KeyAlt,
        _ => static () => false,
    };

    /// <summary>Maps a deselect-key choice to the edge-triggered predicate the interaction layer polls each frame.</summary>
    private static Func<bool> DeselectKeyFunc(DeselectKeyChoice choice) => choice switch
    {
        DeselectKeyChoice.Escape => static () => ImGui.IsKeyPressed(ImGuiKey.Escape, false),
        DeselectKeyChoice.Delete => static () => ImGui.IsKeyPressed(ImGuiKey.Delete, false),
        DeselectKeyChoice.Backspace => static () => ImGui.IsKeyPressed(ImGuiKey.Backspace, false),
        _ => static () => false,
    };
}
