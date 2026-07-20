using Dalamud.Bindings.ImGui;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Colors;
using NoireLib.Draw3D;
using NoireLib.Draw3D.Interaction;
using NoireLib.Helpers;
using System;

namespace NoireDraw3DDemoPlugin.Windows.Pages;

/// <summary>Pointer input: hover, click, drag, selection keys, and a live readout of who owns the mouse.</summary>
internal sealed class InteractionPage
{
    private enum ModifierChoice { Ctrl, Shift, Alt, None }
    private enum DeselectKeyChoice { Escape, Delete, Backspace, None }

    // Local mirrors of the Func<bool> predicates: a lambda can't be read back, so the choice is tracked here, seeded to
    // the library defaults (Ctrl toggle, Shift add, Alt click-through, Escape deselect).
    private int toggleModIdx = (int)ModifierChoice.Ctrl;
    private int addModIdx = (int)ModifierChoice.Shift;
    private int clickThroughModIdx = (int)ModifierChoice.Alt;
    private int deselectKeyIdx = (int)DeselectKeyChoice.Escape;

    /// <inheritdoc cref="DemoWindow.Draw"/>
    public void Draw()
    {
        var it = NoireDraw3D.Interaction;

        Ui.Section("Pointer");
        using (Ui.Form("interact.pointer"))
        {
            Ui.Toggle("Enabled", () => it.Enabled, v => it.Enabled = v,
                "Off, the game keeps every click.");
            Ui.Toggle("Select on click", () => it.SelectOnClick, v => it.SelectOnClick = v,
                "Whether a left-click on a selectable object updates its scene's selection.");
            Ui.Slider("Drag threshold (px)", () => it.DragThresholdPixels, v => it.DragThresholdPixels = v, 0f, 20f,
                "Movement before a left press counts as a drag rather than a click. This is what tells a click apart from the game's click-and-drag camera pan.");
        }

        Ui.Section("Mouse sharing");
        using (Ui.Form("interact.mouse"))
        {
            Ui.Toggle("Hover claims mouse", () => it.BlockGameMouseOnHover, v => it.BlockGameMouseOnHover = v,
                "Off, the camera still pans over highlighted objects; a drag always claims the mouse. On blocks camera and zoom whenever the cursor rests on an object.");
            Ui.Toggle("Game UI blocks", () => it.GameUiBlocksInteraction, v => it.GameUiBlocksInteraction = v,
                "Whether native UI under the cursor stops picking an object behind it.");
        }

        Ui.Section("Occlusion");
        using (Ui.Form("interact.occlusion"))
        {
            Ui.Enum("Obstacles", () => it.ObstacleOcclusion, v => it.ObstacleOcclusion = v,
                "Whether world geometry in front of an object blocks clicking it. HoldToClickThrough blocks unless the click-through key is held.");
            Ui.Slider("Bias (m)", () => it.ObstacleOcclusionBias, v => it.ObstacleOcclusionBias = v, 0f, 2f,
                "Slack before an object counts as occluded. Stops flicker at grazing angles and keeps a ground-hugging decal off its own ground.");
        }

        Ui.Section("Keys");
        using (Ui.Form("interact.keys"))
        {
            Ui.Flags("Deselect on", () => it.DeselectOn, v => it.DeselectOn = v,
                "ClickEmpty deselects on a left click on empty world; Key deselects on the key below. Tick both for both.");

            using (Ui.Disabled((it.DeselectOn & DeselectMode.Key) == 0))
            {
                if (Ui.Enum<DeselectKeyChoice>("Deselect key", ref deselectKeyIdx,
                    "The key that clears the selection while Key is ticked above."))
                    it.DeselectKeyHeld = DeselectKeyFunc((DeselectKeyChoice)deselectKeyIdx);
            }

            if (Ui.Enum<ModifierChoice>("Toggle-select", ref toggleModIdx,
                "Held, a click toggles an object in and out of a multi-selection. Needs the scene's editor in multi-select."))
                it.ToggleSelectionHeld = ModifierFunc((ModifierChoice)toggleModIdx);

            if (Ui.Enum<ModifierChoice>("Add-select", ref addModIdx,
                "Held, a click adds without removing the others."))
                it.AddSelectionHeld = ModifierFunc((ModifierChoice)addModIdx);

            if (Ui.Enum<ModifierChoice>("Click-through", ref clickThroughModIdx,
                "Held, reaches an object behind an obstacle. Only under HoldToClickThrough."))
                it.ClickThroughHeld = ModifierFunc((ModifierChoice)clickThroughModIdx);
        }

        Ui.Section("Live");
        using (Ui.Form("interact.status"))
        {
            Ui.Value("Hovered", it.HoveredNode?.Name ?? "-",
                it.HoveredNode != null ? ImGuiColors.HealerGreen : ImGuiColors.DalamudGrey3);
            Ui.Value("Interacting", YesNo(it.IsInteracting), "A gesture Draw3D owns is in progress: a click resolving, or a drag.");
            Ui.Value("Capturing mouse", YesNo(it.IsCapturingMouse), "Draw3D is claiming the mouse from the game this frame.");
            Ui.Value("Foreign UI has mouse", YesNo(it.ForeignUiHasMouse),
                "Another surface owns it: a different plugin's window, native UI under the cursor, or the cursor outside the viewport. While true, nothing hovers, picks or captures.");
        }

        Ui.Section("Debug");
        using (Ui.Form("interact.debug"))
        {
            Ui.Toggle("Log pipeline", () => it.DebugLog, v => it.DebugLog = v,
                "Click / hover / capture / gate to /xllog, including an [Interact/Gate] line naming why a spot isn't interactable. Verbose.");
            Ui.Toggle("Auto-run", () => it.AutoRun, v => it.AutoRun = v,
                "Off only if you call NoireDraw3D.Interaction.Update() yourself.");
        }
    }

    private static string YesNo(bool value) => value ? "yes" : "no";

    /// <summary>Maps a modifier choice to the held-key predicate the interaction layer polls each frame.</summary>
    private static Func<bool> ModifierFunc(ModifierChoice choice) => choice switch
    {
        ModifierChoice.Ctrl => static () => ImGui.GetIO().KeyCtrl,
        ModifierChoice.Shift => static () => ImGui.GetIO().KeyShift,
        ModifierChoice.Alt => static () => ImGui.GetIO().KeyAlt,
        _ => static () => false,
    };

    /// <summary>
    /// Maps a deselect-key choice to the held-key predicate (the library takes the press edge). Read from the OS through
    /// <see cref="KeybindsHelper.IsAsyncKeyDown"/>: Dalamud only forwards a key to ImGui while a text field is focused,
    /// so <c>ImGui.IsKeyPressed</c> is dead during play. Modifiers are exempt, hence <see cref="ModifierFunc"/>.
    /// </summary>
    private static Func<bool> DeselectKeyFunc(DeselectKeyChoice choice) => choice switch
    {
        DeselectKeyChoice.Escape => static () => KeybindsHelper.IsAsyncKeyDown((int)VirtualKey.ESCAPE),
        DeselectKeyChoice.Delete => static () => KeybindsHelper.IsAsyncKeyDown((int)VirtualKey.DELETE),
        DeselectKeyChoice.Backspace => static () => KeybindsHelper.IsAsyncKeyDown((int)VirtualKey.BACK),
        _ => static () => false,
    };
}
