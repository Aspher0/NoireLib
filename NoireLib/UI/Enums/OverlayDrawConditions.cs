using System;

namespace NoireLib.UI;

/// <summary>
/// Controls in which normally-hidden game states a <see cref="NoireOverlayButton"/> keeps being drawn.<br/>
/// By default (<see cref="None"/>), Dalamud hides all plugin UI during cutscenes, group pose and while the game UI is hidden by the user;
/// an overlay button follows that behavior. Setting one or more of these flags keeps the button visible in the matching state.
/// </summary>
/// <remarks>
/// These flags apply to the overlay button that carries them and to nothing else. NoireLib draws overlays independently of the host plugin's
/// own UI, so keeping one visible in a state has no effect on the plugin's windows: they keep hiding exactly as they would have, and other
/// overlay buttons keep answering for themselves.<br/>
/// The one exception is a Dalamud that NoireLib cannot install its own draw hook into, where overlays fall back to being drawn with the rest
/// of the plugin's UI and Dalamud's per-plugin hiding applies to them all at once. Setting any of these flags then also keeps the rest of the
/// plugin's UI visible in that state. NoireLib logs it when it happens; see <see cref="NoireUI.OverlaysDrawIndependently"/>.
/// </remarks>
[Flags]
public enum OverlayDrawConditions
{
    /// <summary>
    /// The default behavior: the button is hidden during cutscenes, group pose and while the game UI is hidden, like regular plugin UI.
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
    /// The button keeps being drawn while the user has hidden the game UI (e.g. via the game's "Hide UI" keybind).
    /// </summary>
    DrawWhenGameUiHidden = 1 << 2,

    /// <summary>
    /// The button is always drawn, no matter the game state (the combination of every other flag).
    /// </summary>
    AlwaysDraw = DrawInCutscenes | DrawInGpose | DrawWhenGameUiHidden,
}
