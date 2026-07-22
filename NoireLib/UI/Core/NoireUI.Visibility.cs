using System;

namespace NoireLib.UI;

public static partial class NoireUI
{
    private static UiVisibility requiredVisibility = UiVisibility.Default;

    /// <summary>
    /// The union of everything this plugin's windows need to stay visible through, handed to Dalamud so it stops
    /// hiding them.
    /// </summary>
    /// <remarks>
    /// Dalamud decides hiding once per plugin, so the only way one window can stay up in gpose while another goes away
    /// is for Dalamud to stop hiding either and for each window to answer for itself. Setting this switches off
    /// Dalamud's hiding for the states named, and from that point <see cref="ShouldHide"/> is what hides a window.<br/>
    /// <b>Every window then has to consult <see cref="ShouldHide"/>,</b> including the ones that want the ordinary
    /// behaviour: a window that asks nothing is no longer hidden by anyone. <see cref="NoireWindow"/> does it for you.
    /// </remarks>
    public static UiVisibility RequiredVisibility
    {
        get => requiredVisibility;
        set
        {
            requiredVisibility = value;
            ApplyRequiredVisibility();
        }
    }

    /// <summary>
    /// Adds to <see cref="RequiredVisibility"/> without disturbing what another window already asked for.
    /// </summary>
    /// <param name="visibility">The states to keep drawing through.</param>
    public static void RequireVisibility(UiVisibility visibility)
    {
        if ((requiredVisibility & visibility) == visibility)
            return;

        RequiredVisibility = requiredVisibility | visibility;
    }

    /// <summary>
    /// Whether a window with these conditions should be hidden right now, given what the game is doing.
    /// </summary>
    /// <remarks>
    /// The per-window replacement for Dalamud's per-plugin hiding. Mirrors the same three states, so a window asking
    /// for <see cref="UiVisibility.Default"/> behaves exactly as it did before anything asked for anything.
    /// </remarks>
    /// <param name="visibility">What the window keeps drawing through.</param>
    /// <returns>True when the window should not draw this frame.</returns>
    public static bool ShouldHide(UiVisibility visibility)
    {
        if (!NoireService.IsInitialized())
            return false;

        return ShouldHide(
            visibility,
            NoireService.PluginInterface.UiBuilder.CutsceneActive,
            NoireService.ClientState.IsGPosing,
            NoireService.GameGui.GameUiHidden);
    }

    /// <summary>
    /// Whether a window with these conditions should be hidden in the given game state.
    /// </summary>
    /// <remarks>Pure, so the decision can be checked without a game behind it.</remarks>
    /// <param name="visibility">What the window keeps drawing through.</param>
    /// <param name="cutsceneActive">Whether a cutscene is playing.</param>
    /// <param name="gposing">Whether group pose is active.</param>
    /// <param name="gameUiHidden">Whether the user has hidden the game UI.</param>
    /// <returns>True when the window should not draw.</returns>
    public static bool ShouldHide(UiVisibility visibility, bool cutsceneActive, bool gposing, bool gameUiHidden)
    {
        if (cutsceneActive && (visibility & UiVisibility.InCutscenes) == 0)
            return true;

        if (gposing && (visibility & UiVisibility.InGpose) == 0)
            return true;

        if (gameUiHidden && (visibility & UiVisibility.WhenGameUiHidden) == 0)
            return true;

        return false;
    }

    /// <summary>
    /// Tells Dalamud to stop hiding this plugin for whatever any window has asked about.
    /// </summary>
    /// <remarks>
    /// Only ever turns hiding <em>off</em>. A state nobody asked about is left with Dalamud, so a plugin that never
    /// touches any of this is not changed by the feature existing.
    /// </remarks>
    private static void ApplyRequiredVisibility()
    {
        if (!NoireService.IsInitialized())
            return;

        var builder = NoireService.PluginInterface.UiBuilder;

        if ((requiredVisibility & UiVisibility.InCutscenes) != 0)
            builder.DisableCutsceneUiHide = true;

        if ((requiredVisibility & UiVisibility.InGpose) != 0)
            builder.DisableGposeUiHide = true;

        if ((requiredVisibility & UiVisibility.WhenGameUiHidden) != 0)
            builder.DisableUserUiHide = true;
    }

    /// <summary>Puts Dalamud's hiding back the way it was, on teardown.</summary>
    internal static void ReleaseRequiredVisibility()
    {
        if (requiredVisibility == UiVisibility.Default || !NoireService.IsInitialized())
            return;

        var builder = NoireService.PluginInterface.UiBuilder;

        builder.DisableCutsceneUiHide = false;
        builder.DisableGposeUiHide = false;
        builder.DisableUserUiHide = false;

        requiredVisibility = UiVisibility.Default;
    }
}
