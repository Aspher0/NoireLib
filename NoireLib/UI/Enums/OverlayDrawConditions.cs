using System;

namespace NoireLib.UI;

/// <summary>
/// Controls in which normally-hidden game states a <see cref="NoireOverlayButton"/> keeps being drawn.
/// </summary>
/// <remarks>Without its own draw hook, any of these flags also keeps the rest of the plugin's UI visible in that state; see <see cref="NoireUI.OverlaysDrawIndependently"/>.</remarks>
[Flags]
public enum OverlayDrawConditions
{
    /// <summary>
    /// The default behavior: the button is hidden during cutscenes, group pose and while the game UI is hidden.
    /// </summary>
    None = 0,

    /// <summary>
    /// The button keeps being drawn while a cutscene is playing.
    /// </summary>
    DrawInCutscenes = 1 << 0,

    /// <summary>
    /// The button keeps being drawn while group pose (gpose) is active.
    /// </summary>
    DrawInGpose = 1 << 1,

    /// <summary>
    /// The button keeps being drawn while the user has hidden the game UI.
    /// </summary>
    DrawWhenGameUiHidden = 1 << 2,

    /// <summary>
    /// The button is always drawn, no matter the game state.
    /// </summary>
    AlwaysDraw = DrawInCutscenes | DrawInGpose | DrawWhenGameUiHidden,
}
