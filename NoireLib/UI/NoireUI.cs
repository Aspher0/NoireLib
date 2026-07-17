using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NoireLib.UI;

/// <summary>
/// The central hub of the NoireLib UI helpers.<br/>
/// Manages the shared draw hook used by <see cref="NoireOverlayButton"/> instances and provides frame-scoped services to the other UI helpers.
/// </summary>
public static class NoireUI
{
    private const string DisposeCallbackKey = "NoireLib.UI.NoireUI";

    // Dalamud gates a plugin's draw callback for the whole plugin at once, so the only way an overlay can stay visible in
    // a state without dragging the rest of the plugin's windows along with it is to not be drawn from that callback at
    // all. Every plugin's callback is itself invoked from one ungated event inside Dalamud, and these name the private
    // field holding the object that raises it. See TryHookIndependently.
    private const string InterfaceManagerFieldName = "interfaceManager";
    private const string InterfaceManagerDrawEventName = "Draw";

    private static readonly object SyncRoot = new();
    private static readonly List<NoireOverlayButton> OverlayButtons = new();

    private static OverlayHookMode hookMode = OverlayHookMode.None;
    private static Action? removeIndependentHook;

    // Tracks whether NoireLib is the one forcing each per-plugin UiBuilder "keep UI visible" switch on, so it can restore
    // them without clobbering a value the host plugin set itself. Only ever used in OverlayHookMode.Shared.
    private static bool forcedCutsceneOverride;
    private static bool forcedGposeOverride;
    private static bool forcedUserHideOverride;

    private static int tooltipFrame = -1;
    private static int tooltipCounter;

    /// <summary>
    /// How the registered overlay buttons reach the screen.
    /// </summary>
    private enum OverlayHookMode
    {
        /// <summary>
        /// Nothing is registered, so no hook is installed.
        /// </summary>
        None,

        /// <summary>
        /// Overlays are drawn straight from Dalamud's own frame, independently of the host plugin's draw callback.
        /// Dalamud's per-plugin UI hiding therefore never applies to them, and each overlay decides for itself.
        /// </summary>
        Independent,

        /// <summary>
        /// Overlays are drawn from the host plugin's draw callback, which Dalamud gates for the whole plugin at once.
        /// Only used when the independent hook could not be installed.
        /// </summary>
        Shared,
    }

    /// <summary>
    /// Whether overlay buttons are drawn independently of the rest of the host plugin's UI.<br/>
    /// When <see langword="true"/> (the normal case), an overlay's <see cref="NoireOverlayButton.DrawConditions"/> affect
    /// that overlay and nothing else: keeping one visible during a cutscene, in group pose or while the game UI is hidden
    /// leaves every window the plugin draws hiding exactly as it would have.<br/>
    /// When <see langword="false"/>, NoireLib could not install its own draw hook and falls back to drawing overlays from
    /// the plugin's own draw callback, where Dalamud decides for the whole plugin at once. In that mode, and only in that
    /// mode, setting a draw condition on any single overlay also keeps the rest of the plugin's UI visible in that state.
    /// The reason is logged once when it happens.
    /// </summary>
    public static bool OverlaysDrawIndependently => hookMode == OverlayHookMode.Independent;

    /// <summary>
    /// Gets a snapshot of all currently registered overlay buttons.
    /// </summary>
    /// <returns>A snapshot list of the registered overlay buttons.</returns>
    public static IReadOnlyList<NoireOverlayButton> GetOverlayButtons()
    {
        lock (SyncRoot)
            return OverlayButtons.ToArray();
    }

    /// <summary>
    /// Disposes and unregisters every registered overlay button.
    /// </summary>
    public static void RemoveAllOverlayButtons()
    {
        foreach (var button in GetOverlayButtons())
            button.Dispose();
    }

    /// <summary>
    /// Registers an overlay button so it gets drawn every frame. Called automatically by the <see cref="NoireOverlayButton"/> constructor.
    /// </summary>
    /// <param name="button">The overlay button to register.</param>
    /// <exception cref="InvalidOperationException">Thrown when NoireLib has not been initialized yet.</exception>
    internal static void RegisterOverlayButton(NoireOverlayButton button)
    {
        if (!NoireService.IsInitialized())
            throw new InvalidOperationException("NoireLib must be initialized before using NoireLib.UI overlay buttons.");

        lock (SyncRoot)
        {
            if (!OverlayButtons.Contains(button))
                OverlayButtons.Add(button);

            EnsureDrawHook();
        }

        RefreshUiHideOverrides();
    }

    /// <summary>
    /// Unregisters an overlay button so it stops being drawn. Called automatically by <see cref="NoireOverlayButton.Dispose"/>.
    /// </summary>
    /// <param name="button">The overlay button to unregister.</param>
    internal static void UnregisterOverlayButton(NoireOverlayButton button)
    {
        lock (SyncRoot)
            OverlayButtons.Remove(button);

        RefreshUiHideOverrides();
    }

    /// <summary>
    /// Returns a unique ImGui window id for a custom tooltip drawn this frame.<br/>
    /// Ids are stable across frames as long as tooltips are shown in the same order.
    /// </summary>
    /// <returns>A unique ImGui window id for the current frame.</returns>
    internal static string NextTooltipId()
    {
        var frame = ImGui.GetFrameCount();
        if (frame != tooltipFrame)
        {
            tooltipFrame = frame;
            tooltipCounter = 0;
        }

        return $"###NoireTooltip_{tooltipCounter++}";
    }

    /// <summary>
    /// Recomputes the per-plugin UiBuilder "keep UI visible" switches from the <see cref="NoireOverlayButton.DrawConditions"/> of every
    /// registered overlay button and applies them.<br/>
    /// Only does anything in <see cref="OverlayHookMode.Shared"/>, where overlays are drawn from the host plugin's own draw callback and
    /// Dalamud would otherwise not call it at all in those states. A switch is forced on as soon as one button needs it; each individual
    /// button still hides itself when its own conditions do not include the current state.<br/>
    /// NoireLib only ever forces a switch on, and reverts exactly the ones it turned on, to avoid overriding a value the host plugin set itself.<br/>
    /// In <see cref="OverlayHookMode.Independent"/> the switches are left completely alone: overlays are not drawn from the callback those
    /// switches gate, so exempting the plugin would buy nothing and would keep the plugin's own windows on screen for no reason.
    /// </summary>
    internal static void RefreshUiHideOverrides()
    {
        if (!NoireService.IsInitialized() || hookMode == OverlayHookMode.Independent)
            return;

        bool needCutscene = false, needGpose = false, needUserHide = false;

        lock (SyncRoot)
        {
            foreach (var button in OverlayButtons)
            {
                var conditions = button.DrawConditions;
                needCutscene |= (conditions & OverlayDrawConditions.DrawInCutscenes) != 0;
                needGpose |= (conditions & OverlayDrawConditions.DrawInGpose) != 0;
                needUserHide |= (conditions & OverlayDrawConditions.DrawWhenGameUiHidden) != 0;
            }
        }

        var uiBuilder = NoireService.PluginInterface.UiBuilder;
        ApplyOverride(ref forcedCutsceneOverride, needCutscene, uiBuilder.DisableCutsceneUiHide, v => uiBuilder.DisableCutsceneUiHide = v);
        ApplyOverride(ref forcedGposeOverride, needGpose, uiBuilder.DisableGposeUiHide, v => uiBuilder.DisableGposeUiHide = v);
        ApplyOverride(ref forcedUserHideOverride, needUserHide, uiBuilder.DisableUserUiHide, v => uiBuilder.DisableUserUiHide = v);
    }

    /// <summary>
    /// Applies a single "keep UI visible" switch: forces it on when needed (remembering it did), and reverts it only if NoireLib was the one
    /// that forced it. A value the host plugin set to <see langword="true"/> itself is never touched.
    /// </summary>
    private static void ApplyOverride(ref bool forced, bool needed, bool current, Action<bool> setter)
    {
        if (needed)
        {
            if (!current)
            {
                setter(true);
                forced = true;
            }
        }
        else if (forced)
        {
            setter(false);
            forced = false;
        }
    }

    private static void EnsureDrawHook()
    {
        if (hookMode != OverlayHookMode.None)
            return;

        if (TryHookIndependently())
        {
            hookMode = OverlayHookMode.Independent;
        }
        else
        {
            NoireService.PluginInterface.UiBuilder.Draw += DrawOverlayButtons;
            hookMode = OverlayHookMode.Shared;

            NoireLogger.LogWarning(
                "Overlay buttons fall back to being drawn from the plugin's own draw callback, which Dalamud hides for the whole plugin at once. " +
                $"Setting {nameof(NoireOverlayButton.DrawConditions)} on a single overlay will therefore also keep the rest of this plugin's UI visible in that state. " +
                $"See {nameof(NoireUI)}.{nameof(OverlaysDrawIndependently)}.",
                nameof(NoireUI));
        }

        if (!NoireLibMain.IsRegisteredOnDispose(DisposeCallbackKey))
            NoireLibMain.RegisterOnDispose(DisposeCallbackKey, Cleanup);
    }

    /// <summary>
    /// Installs a draw hook that belongs to NoireLib rather than to the host plugin, and reports whether it worked.
    /// </summary>
    /// <remarks>
    /// Dalamud decides whether to hide plugin UI once per plugin, inside the callback it invokes to draw that plugin: an
    /// overlay drawn from there can only be exempted by exempting the whole plugin along with it, which is what the
    /// per-plugin <c>DisableUserUiHide</c>, <c>DisableCutsceneUiHide</c> and <c>DisableGposeUiHide</c> switches do.<br/>
    /// Every plugin's callback is in turn invoked from a single event inside Dalamud that carries no such gate. Subscribing
    /// to it directly puts the overlays beside the plugin's callback rather than inside it, so nothing Dalamud decides about
    /// this plugin's UI reaches them and each overlay is free to answer for itself
    /// (see <see cref="NoireOverlayButton.ShouldHideForGameState(OverlayDrawConditions, bool, bool, bool)"/>).<br/>
    /// That event is only reachable by reflection, so this is written to fail into the shared hook rather than to throw:
    /// the names are checked against the running Dalamud every time, and a Dalamud that no longer matches costs the
    /// per-overlay independence, not the overlays.<br/>
    /// Nothing else is lost by bypassing the plugin's callback. It gates, draws Dalamud's own error window and keeps draw
    /// statistics; it establishes no ImGui state (no font and no id scope) that an overlay drawn beside it would miss.
    /// </remarks>
    /// <returns>True if the independent hook is installed, false to fall back to the plugin's draw callback.</returns>
    private static bool TryHookIndependently()
    {
        try
        {
            var uiBuilder = NoireService.PluginInterface.UiBuilder;

            var managerField = uiBuilder.GetType().GetField(InterfaceManagerFieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (managerField?.GetValue(uiBuilder) is not { } manager)
                return false;

            var drawEvent = manager.GetType().GetEvent(InterfaceManagerDrawEventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (drawEvent?.EventHandlerType == null)
                return false;

            var handler = Delegate.CreateDelegate(drawEvent.EventHandlerType, typeof(NoireUI), nameof(DrawOverlayButtons), ignoreCase: false, throwOnBindFailure: false);
            if (handler == null)
                return false;

            drawEvent.AddEventHandler(manager, handler);
            removeIndependentHook = () => drawEvent.RemoveEventHandler(manager, handler);
            return true;
        }
        catch (Exception ex)
        {
            NoireLogger.LogWarning(ex, "Failed to install the independent overlay draw hook.", nameof(NoireUI));
            return false;
        }
    }

    /// <summary>
    /// Draws every registered overlay button that asks to be drawn automatically.<br/>
    /// In <see cref="OverlayHookMode.Independent"/> this is called by Dalamud directly rather than through the host
    /// plugin, so nothing above it will catch an exception on the plugin's behalf: every button is contained on its own.
    /// </summary>
    private static void DrawOverlayButtons()
    {
        if (!NoireService.IsInitialized())
            return;

        NoireOverlayButton[] snapshot;
        lock (SyncRoot)
            snapshot = OverlayButtons.ToArray();

        foreach (var button in snapshot)
        {
            if (!button.AutoDraw)
                continue;

            try
            {
                button.Draw();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Failed to draw overlay button '{button.Id}'.", nameof(NoireUI));
            }
        }
    }

    private static void Cleanup()
    {
        var previousMode = hookMode;

        lock (SyncRoot)
        {
            OverlayButtons.Clear();

            switch (hookMode)
            {
                case OverlayHookMode.Independent:
                    // Detaching matters more here than for the shared hook: this one is attached to an object that
                    // outlives the plugin, so a handler left behind would keep being invoked after unload.
                    try
                    {
                        removeIndependentHook?.Invoke();
                    }
                    catch (Exception ex)
                    {
                        NoireLogger.LogError(ex, "Failed to remove the independent overlay draw hook.", nameof(NoireUI));
                    }

                    removeIndependentHook = null;
                    break;

                case OverlayHookMode.Shared:
                    if (NoireService.IsInitialized())
                        NoireService.PluginInterface.UiBuilder.Draw -= DrawOverlayButtons;
                    break;
            }

            hookMode = OverlayHookMode.None;
        }

        // No buttons remain, so revert any UiBuilder switch NoireLib forced on. The mode is cleared above, so this is
        // resolved against the mode that was actually in effect.
        if (previousMode == OverlayHookMode.Shared)
            RefreshUiHideOverrides();
    }
}
