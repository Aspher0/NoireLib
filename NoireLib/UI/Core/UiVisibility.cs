using System;

namespace NoireLib.UI;

/// <summary>
/// Decides, per window, which normally-hidden game states it keeps drawing in.
/// A window that never consults <see cref="NoireUI.ShouldHide"/> stays visible.
/// </summary>
[Flags]
public enum UiVisibility
{
    /// <summary>
    /// The default: hidden during cutscenes, in group pose, and while the game UI is hidden.
    /// </summary>
    Default = 0,

    /// <summary>Keeps drawing while a cutscene is playing.</summary>
    InCutscenes = 1 << 0,

    /// <summary>Keeps drawing while group pose is active.</summary>
    InGpose = 1 << 1,

    /// <summary>Keeps drawing while the user has hidden the game UI.</summary>
    WhenGameUiHidden = 1 << 2,

    /// <summary>Always drawn, whatever the game is doing.</summary>
    Always = InCutscenes | InGpose | WhenGameUiHidden,
}
