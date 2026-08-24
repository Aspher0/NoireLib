using System;

namespace NoireLib.UI;

public static partial class NoireUI
{
    private static UiVisibility requiredVisibility = UiVisibility.Default;

    /// <summary>
    /// The union of everything this plugin's windows need to stay visible through, handed to Dalamud so it stops
    /// hiding them.
    /// </summary>
    /// <remarks>Every window must then consult <see cref="ShouldHide"/>; <see cref="NoireWindow"/> does it for you.</remarks>
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

    // Puts Dalamud's hiding back the way it was, on teardown.
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
