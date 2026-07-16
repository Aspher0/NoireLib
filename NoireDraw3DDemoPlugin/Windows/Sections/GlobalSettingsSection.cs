using System;
using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Enums;
using NoireLib.Draw3D.Interaction;
using NoireLib.Helpers;

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

    /// <summary>
    /// Whether the demo window keeps drawing while the game UI is hidden. Read by <see cref="DemoWindow.DrawConditions"/>,
    /// which is what actually hides it: Dalamud cannot, because keeping the 3D layer alive means telling it not to.
    /// </summary>
    public bool KeepImGuiWhenUiHidden { get; set; } = true;

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
            SectionUi.Hint("Renders every scene mesh as wireframe (diagnostic rasterizer state). Ground decals have no mesh of their own to wireframe - the shape lives in their pixel shader - so they trace the outline of what they actually paint instead.");
            SectionUi.Toggle("Decal shape outlines", static () => NoireDraw3D.Diagnostics.DecalShapeOutlines, static v => NoireDraw3D.Diagnostics.DecalShapeOutlines = v,
                "Traces what every decal paints as a closed 3D line, over normal rendering - retained decals and immediate-layer grounded shapes alike. The global 'where is this decal actually landing'; SceneNode.ShowDecalShape is the per-node version, which an immediate-mode shape has no node to use. Always on while Wireframe is.");
            SectionUi.Toggle("Keep drawing when UI hidden", static () => NoireDraw3D.KeepDrawingWhenUiHidden, static v => NoireDraw3D.KeepDrawingWhenUiHidden = v,
                "Keeps the 3D layer rendering while the game UI is hidden (hotkey / cutscene / gpose). Affects the 3D only - this window is governed by the checkbox below, independently. Watch '/noire3d stats' -> skipped (ui-hidden) to see it take effect.");
            SectionUi.Toggle("Keep this window when UI hidden", () => KeepImGuiWhenUiHidden, v => KeepImGuiWhenUiHidden = v,
                "Keeps THIS demo window up while the game UI is hidden. Affects the window only - the 3D is governed by the checkbox above, independently. Dalamud cannot hide the window for us (NoireDraw3D holds its UI-hide overrides so the render callback keeps firing), so the window checks NoireDraw3D.IsGameUiHidden in DrawConditions() instead.");
        }

        if (ImGui.CollapsingHeader("Decals", ImGuiTreeNodeFlags.DefaultOpen))
        {
            SectionUi.Toggle("Collision height-map", static () => NoireDraw3D.CollisionHeightMap, static v => NoireDraw3D.CollisionHeightMap = v,
                "Renders the collision world into a top-down height-map (highest surface per column) on frames that have ground decals.\n\n"
                + "ONLY DecalProjection.HighestOnly reads it - it is how a decal tells a tabletop from the floor under it. Off, a HighestOnly decal degrades to AllSurfaces and paints the floor too. Every other decal is unaffected.\n\n"
                + "So if nothing on screen is set to HighestOnly, this does nothing visible - it just skips the height-map pass. Set a decal's Projection to HighestOnly in Scenes & decals, stand it over a table or a step, then toggle this.\n\n"
                + "It does NOT cut characters out of decals - that is ExcludeObjects plus the character stencil below, and it works either way.");
            var stencil = (int)NoireDraw3D.CharacterStencilValue;
            if (ImGui.InputInt("Character stencil value", ref stencil))
                NoireDraw3D.CharacterStencilValue = (uint)Math.Max(0, stencil);
            SectionUi.Hint("The game stencil value that marks characters, used to cut them out of decals along their exact silhouette. Default 0x08 (8); 0 disables. Discover it with /noire3d stencil.");
            SectionUi.Slider("Top-surface threshold (m)", static () => NoireDraw3D.TopSurfaceThreshold, static v => NoireDraw3D.TopSurfaceThreshold = v, 0f, 1f,
                "The elevation band used by DecalProjection.HighestOnly: a surface more than this far below its column's highest collision surface gets skipped.\n\n"
                + "Larger tolerates coarser collision; smaller is tighter but can nibble real ground where collision sits slightly off the visual floor.\n\n"
                + "Same story as the toggle above: ONLY HighestOnly decals read it, so it does nothing visible unless one is on screen.\n\n"
                + "0 turns HighestOnly off entirely - the shader tests this same value to decide whether the feature is available. Keep it above zero.");
        }

        if (ImGui.CollapsingHeader("Native UI layering", ImGuiTreeNodeFlags.DefaultOpen))
        {
            var over = NoireDraw3D.NativeUi.Layering == Draw3DLayering.OverEverything;
            ImGui.TextDisabled("Where the 3D layer lands in the game's frame, and what it does about the UI it finds");
            ImGui.TextDisabled("there. Both modes are raw D3D blits - neither one uses ImGui.");

            SectionUi.EnumCombo("Layering", static () => NoireDraw3D.NativeUi.Layering, static v => NoireDraw3D.NativeUi.Layering = v,
                "Picks WHEN the finished layer is composited, which decides whether the game's UI is something the layer simply loses to, or something it can make decisions about.\n\n"
                + "UnderGameUi (default): a render-thread hook blits the layer into the game's present buffer after the world is drawn but BEFORE the native UI. The game then paints its HUD, addons and nameplates over the layer itself - letter-exact, free, nothing to configure. The UI is always on top here.\n\n"
                + "OverEverything: the layer is blitted over the backbuffer at present time, after the game already drew its UI. This is the only mode that can decide per element - cover a nameplate outright, or dim it - because the UI exists by the time it runs.\n\n"
                + "Falls back to OverEverything automatically on any frame the injection can't run (no camera, hook not installed yet).");

            using (SectionUi.Disabled(!over))
            {
                SectionUi.Toggle("Keep game UI on top", static () => NoireDraw3D.NativeUi.KeepUiOnTop, static v => NoireDraw3D.NativeUi.KeepUiOnTop = v,
                    "Masks the layer per-pixel so the HUD, addons and nameplates read on top of it. Only applies under OverEverything - under the game UI the game paints over the layer by itself, so there is nothing to configure.\n\n"
                    + "The mask is letter-exact and cuts no rectangles: Draw3D photographs the game's present buffer before and after the native UI is drawn into it, and wherever the two differ is exactly where the UI painted - antialiased glyph edges included.\n\n"
                    + "This rides the same render-thread hook the under-UI path uses, so on a frame where the injection point can't fire there is no 'before' photo and the layer composites unmasked for that frame.\n\n"
                    + "Run '/noire3d uimask' to see whether the difference is actually finding the UI.");

                SectionUi.Slider("Nameplate dim", static () => NoireDraw3D.NativeUi.NameplateDim, static v => NoireDraw3D.NativeUi.NameplateDim = v, 0f, 1f,
                    "How much a nameplate your content covers still shows through it: 0 (default) fully covered, toward 1 faintly readable.\n\n"
                    + "Needs 'keep game UI on top' on, and only ever applies to a plate the mode below decided is covered - so with AlwaysVisible it never applies.\n\n"
                    + "Greys out under UnderGameUi: there a plate is drawn by the game against a depth test, which can only occlude it or not, so there is no partial value to apply.");
            }

            SectionUi.EnumCombo("Nameplates", static () => NoireDraw3D.NativeUi.Nameplates, static v => NoireDraw3D.NativeUi.Nameplates = v,
                "Whether the game's own nameplates are occluded by 3D objects in front of them. Honoured in BOTH layering modes, by different mechanisms - the game draws the plates either way, so it is letter-exact regardless.\n\n"
                + "DepthAware (default): a plate behind your content is covered by it, one in front stays readable. Under the game UI that is the game's own depth test, against depth Draw3D stamps before the plate pass. Over everything it compares each plate's world distance against the content covering it, then lets the UI mask do the cutting.\n\n"
                + "AlwaysVisible: plate letters read on top at any distance.\n\n"
                + "Covered: the layer covers plate letters everywhere. Needs OverEverything - under the game UI the plates are drawn after the layer by the game, so there is no way to paint over them and this behaves as AlwaysVisible.\n\n"
                + "Over everything it also needs 'keep game UI on top' on (it gates where that mask applies) and nameplates actually on screen.");
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

        SectionUi.SeparatorText("Obstacle occlusion");
        SectionUi.EnumCombo("Obstacle occlusion", () => it.ObstacleOcclusion, v => it.ObstacleOcclusion = v,
            "How anything the game draws in front of a 3D object affects hovering / clicking it: Off = always clickable straight through; Always = an obstacle blocks the click; HoldToClickThrough = an obstacle blocks it unless the modifier below is held.\n\n"
            + "An obstacle is any visible surface, not just level geometry: walls, terrain and furnishings, but equally characters, mounts and NPCs - the occluder depth is read from the game depth buffer, which holds everything that was rendered.");
        SectionUi.Slider("Obstacle-occlusion bias (m)", () => it.ObstacleOcclusionBias, v => it.ObstacleOcclusionBias = v, 0f, 2f,
            "Slack added to the obstacle distance before an object counts as occluded (avoids flicker at grazing angles, and keeps a ground-hugging decal from being blocked by its own ground).");

        SectionUi.SeparatorText("Deselect");
        SectionUi.Flags("Deselect on", () => it.DeselectOn, v => it.DeselectOn = v,
            "How selections clear, as flags - tick both to have both. ClickEmpty (the default) = clicking empty world; Key = pressing the deselect key below, which does nothing until this is ticked. Clears every scene's selection.");
        using (SectionUi.Disabled((it.DeselectOn & DeselectMode.Key) == 0))
        {
            if (SectionUi.EnumCombo<DeselectKeyChoice>("Deselect key", ref deselectKeyIdx,
                "The key that clears the selection. Greyed out until \"Deselect on\" has Key ticked.\n\n"
                + "Read straight from the OS rather than through ImGui: Dalamud only hands a key to ImGui while a text field is focused, so an ImGui key test never fires while you are playing. The modifiers below are the exception - Dalamud always forwards those - which is why they can be read from ImGui and this cannot."))
                it.DeselectKeyHeld = DeselectKeyFunc((DeselectKeyChoice)deselectKeyIdx);
        }

        SectionUi.SeparatorText("Selection & click-through modifiers");
        if (SectionUi.EnumCombo<ModifierChoice>("Toggle-select held", ref toggleModIdx,
            "Held while left-clicking to toggle a node in/out of a multi-select (needs the editor in multi-select mode)."))
            it.ToggleSelectionHeld = ModifierFunc((ModifierChoice)toggleModIdx);
        if (SectionUi.EnumCombo<ModifierChoice>("Add-select held", ref addModIdx,
            "Held while left-clicking to add a node to a multi-select without removing others."))
            it.AddSelectionHeld = ModifierFunc((ModifierChoice)addModIdx);
        if (SectionUi.EnumCombo<ModifierChoice>("Click-through held", ref clickThroughModIdx,
            "Held to click through an obstacle when Obstacle occlusion is set to HoldToClickThrough."))
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

    /// <summary>
    /// Maps a deselect-key choice to the held-key predicate the interaction layer polls each frame (it takes the press
    /// edge itself). The key is read from the OS through <see cref="KeybindsHelper.IsAsyncKeyDown"/>, not from ImGui:
    /// Dalamud only forwards a key to ImGui while a text field is focused, so <c>ImGui.IsKeyPressed</c> is dead during
    /// normal play. Modifiers are exempt from that, which is why <see cref="ModifierFunc"/> can read ImGui's IO.
    /// </summary>
    private static Func<bool> DeselectKeyFunc(DeselectKeyChoice choice) => choice switch
    {
        DeselectKeyChoice.Escape => static () => KeybindsHelper.IsAsyncKeyDown((int)VirtualKey.ESCAPE),
        DeselectKeyChoice.Delete => static () => KeybindsHelper.IsAsyncKeyDown((int)VirtualKey.DELETE),
        DeselectKeyChoice.Backspace => static () => KeybindsHelper.IsAsyncKeyDown((int)VirtualKey.BACK),
        _ => static () => false,
    };
}
