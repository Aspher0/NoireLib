using Dalamud.Bindings.ImGui;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.UI;

/// <summary>
/// The central hub of the NoireLib UI helpers.<br/>
/// Manages the shared draw hook used by <see cref="NoireOverlayButton"/> instances and provides frame-scoped services to the other UI helpers.
/// </summary>
public static class NoireUI
{
    private const string DisposeCallbackKey = "NoireLib.UI.NoireUI";

    private static readonly object SyncRoot = new();
    private static readonly List<NoireOverlayButton> OverlayButtons = new();
    private static bool drawHookRegistered;

    // Tracks whether NoireLib is the one forcing each per-plugin UiBuilder "keep UI visible" switch on, so it can restore
    // them without clobbering a value the host plugin set itself. See RefreshUiHideOverrides.
    private static bool forcedCutsceneOverride;
    private static bool forcedGposeOverride;
    private static bool forcedUserHideOverride;

    private static int tooltipFrame = -1;
    private static int tooltipCounter;

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
    /// A switch is forced on as soon as one button needs it (so Dalamud keeps calling the draw hook in that state, allowing the button to be
    /// drawn); each individual button still hides itself when its own conditions do not include the current state.<br/>
    /// NoireLib only ever forces a switch on, and reverts exactly the ones it turned on, to avoid overriding a value the host plugin set itself.
    /// </summary>
    internal static void RefreshUiHideOverrides()
    {
        if (!NoireService.IsInitialized())
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
        if (drawHookRegistered)
            return;

        NoireService.PluginInterface.UiBuilder.Draw += DrawOverlayButtons;
        drawHookRegistered = true;

        if (!NoireLibMain.IsRegisteredOnDispose(DisposeCallbackKey))
            NoireLibMain.RegisterOnDispose(DisposeCallbackKey, Cleanup);
    }

    private static void DrawOverlayButtons()
    {
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
                NoireLogger.LogError(ex, $"Failed to draw overlay button '{button.Id}'.", "NoireUI");
            }
        }
    }

    private static void Cleanup()
    {
        lock (SyncRoot)
        {
            OverlayButtons.Clear();

            if (drawHookRegistered)
            {
                if (NoireService.IsInitialized())
                    NoireService.PluginInterface.UiBuilder.Draw -= DrawOverlayButtons;

                drawHookRegistered = false;
            }
        }

        // No buttons remain, so revert any UiBuilder switch NoireLib forced on.
        RefreshUiHideOverrides();
    }
}
