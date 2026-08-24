using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace NoireLib.UI;

/// <summary>
/// The central hub of the NoireLib UI helpers. It owns the per-frame pass every other helper leans on: the drawable
/// registry and the automatic-drawing policy (<see cref="AutoDraw"/>), the draw-thread queue (<see cref="RunOnDraw"/>),
/// the frame clock, transient widget state, the reduced-motion switch and <see cref="Diagnostics"/>.
/// </summary>
public static partial class NoireUI
{
    private const string DisposeCallbackKey = "NoireLib.UI.NoireUI";

    // Dalamud gates a plugin's draw callback for the whole plugin at once, so the only way an overlay can stay visible in
    // a state without dragging the rest of the plugin's windows along with it is to not be drawn from that callback at
    // all. Every plugin's callback is itself invoked from one ungated event inside Dalamud, and these name the private
    // field holding the object that raises it. See TryHookIndependently.
    private const string InterfaceManagerFieldName = "interfaceManager";
    private const string InterfaceManagerDrawEventName = "Draw";

    private static OverlayHookMode hookMode = OverlayHookMode.None;
    private static Action? removeIndependentHook;

    // Tracks whether NoireLib is the one forcing each per-plugin UiBuilder "keep UI visible" switch on, so it can restore
    // them without clobbering a value the host plugin set itself. Only ever used in OverlayHookMode.Shared.
    private static bool forcedCutsceneOverride;
    private static bool forcedGposeOverride;
    private static bool forcedUserHideOverride;

    private static int tooltipFrame = -1;
    private static int tooltipCounter;

    // How the hub's per-frame pass reaches the screen.
    private enum OverlayHookMode
    {
        // Nothing needs a frame yet, so no hook is installed.
        None,

        // Straight from Dalamud's own frame: Dalamud's per-plugin UI hiding never applies to overlays, and each one
        // decides for itself.
        Independent,

        // From the host plugin's draw callback, which Dalamud gates for the whole plugin at once. Only used when the
        // independent hook could not be installed.
        Shared,
    }

    /// <summary>
    /// Whether overlay buttons are drawn independently of the rest of the host plugin's UI.
    /// </summary>
    /// <remarks>
    /// Reads <see langword="false"/> before anything has needed a frame, when there is no hook of either kind rather
    /// than a fallback.
    /// </remarks>
    public static bool OverlaysDrawIndependently => hookMode == OverlayHookMode.Independent;

    /// <summary>
    /// Gets a snapshot of all currently registered overlay buttons.
    /// </summary>
    /// <returns>A snapshot list of the registered overlay buttons.</returns>
    public static IReadOnlyList<NoireOverlayButton> GetOverlayButtons()
    {
        var overlays = new List<NoireOverlayButton>();

        lock (SyncRoot)
        {
            foreach (var drawable in Drawables)
            {
                if (drawable is NoireOverlayButton button)
                    overlays.Add(button);
            }
        }

        return overlays;
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
    /// Disposes and unregisters every registered drawable, overlay buttons included.
    /// </summary>
    public static void RemoveAllDrawables()
    {
        foreach (var drawable in GetDrawables())
            drawable.Dispose();
    }

    // A unique ImGui window id for a custom tooltip drawn this frame. Ids are stable across frames as long as tooltips
    // are shown in the same order.
    internal static string NextTooltipId()
    {
        var frame = ImGui.GetFrameCount();
        if (frame != tooltipFrame)
        {
            tooltipFrame = frame;
            tooltipCounter = 0;
        }

        // Built through the id cache rather than interpolated, so the tooltips a frame shows cost nothing after the
        // first frame that showed that many. The empty owner puts the separator in the right place: the shape
        // is {prefix}{owner}_{index}, so a prefix without its own trailing underscore composes the same string this
        // replaced, which the tooltip id test asserts against the literal.
        return UiIds.For("###NoireTooltip", string.Empty, tooltipCounter++);
    }

    // Recomputes the per-plugin UiBuilder "keep UI visible" switches from every registered overlay button's
    // DrawConditions. Only does anything in OverlayHookMode.Shared; NoireLib only ever forces a switch on, and reverts
    // exactly the ones it turned on, to avoid overriding a value the host plugin set itself.
    internal static void RefreshUiHideOverrides()
    {
        if (!NoireService.IsInitialized() || hookMode == OverlayHookMode.Independent)
            return;

        bool needCutscene = false, needGpose = false, needUserHide = false;

        lock (SyncRoot)
        {
            foreach (var drawable in Drawables)
            {
                if (drawable is not NoireOverlayButton button)
                    continue;

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

    // Forces the switch on when needed (remembering it did), and reverts it only if NoireLib was the one that forced
    // it: a value the host plugin set to true itself is never touched.
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

    // Installs the hub's per-frame hook if it is not installed yet. Callers hold SyncRoot.
    private static void EnsureFrameHook()
    {
        if (hookMode != OverlayHookMode.None)
            return;

        if (TryHookIndependently())
        {
            hookMode = OverlayHookMode.Independent;
        }
        else
        {
            NoireService.PluginInterface.UiBuilder.Draw += OnFrame;
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

    // Installs a draw hook that belongs to NoireLib rather than to the host plugin, and reports whether it worked. The
    // event is only reachable by reflection, so a Dalamud that no longer matches costs the per-overlay independence.
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

            var handler = Delegate.CreateDelegate(drawEvent.EventHandlerType, typeof(NoireUI), nameof(OnFrame), ignoreCase: false, throwOnBindFailure: false);
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

    private static void Cleanup()
    {
        var previousMode = hookMode;

        lock (SyncRoot)
        {
            Drawables.Clear();
            DrawPump.Clear();
            frameServicesReady = false;

            // Dalamud's hiding is handed back here: it was switched off so windows could answer for themselves, and
            // with the windows gone there is nothing left to answer.
            ReleaseRequiredVisibility();

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
                        NoireService.PluginInterface.UiBuilder.Draw -= OnFrame;
                    break;
            }

            hookMode = OverlayHookMode.None;
        }

        UiFrameState.Clear();

        // No buttons remain, so revert any UiBuilder switch NoireLib forced on. The mode is cleared above, so this is
        // resolved against the mode that was actually in effect.
        if (previousMode == OverlayHookMode.Shared)
            RefreshUiHideOverrides();
    }
}
