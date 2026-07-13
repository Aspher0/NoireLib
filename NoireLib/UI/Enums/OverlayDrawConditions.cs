using System;

namespace NoireLib.UI;

/// <summary>
/// Controls in which normally-hidden game states a <see cref="NoireOverlayButton"/> keeps being drawn.<br/>
/// By default (<see cref="None"/>), Dalamud hides all plugin UI during cutscenes, group pose and while the game UI is hidden by the user;
/// an overlay button follows that behavior. Setting one or more of these flags keeps the button visible in the matching state.
/// </summary>
/// <remarks>
/// These flags map to the per-plugin <c>DisableCutsceneUiHide</c> / <c>DisableGposeUiHide</c> / <c>DisableUserUiHide</c> switches of the
/// Dalamud <c>UiBuilder</c>, which <see cref="NoireUI"/> enables automatically as soon as at least one registered overlay button needs them.<br/>
/// Because those switches are per-plugin (a Dalamud limitation), enabling any of these flags on a single overlay button also prevents Dalamud
/// from auto-hiding the rest of the plugin's UI in that state. Buttons that do not carry the matching flag are still hidden individually.
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
