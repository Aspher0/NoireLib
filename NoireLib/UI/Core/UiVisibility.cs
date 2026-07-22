using System;

namespace NoireLib.UI;

/// <summary>
/// Decides, per window, which normally-hidden game states it keeps drawing in.
/// </summary>
/// <remarks>
/// Dalamud hides plugin UI <b>once per plugin</b>: its four switches are on the plugin's <c>UiBuilder</c>, so turning
/// one off to keep a single window up in gpose keeps <em>every</em> window of that plugin up in gpose. This inverts
/// that. The library tells Dalamud to stop hiding anything it has been asked about, and then each window answers for
/// itself, which is the only way one window can differ from another.<br/>
/// The consequence worth knowing: once a plugin asks for any of this, hiding is the library's job rather than Dalamud's,
/// so a window that does not ask is hidden by <see cref="NoireUI.ShouldHide"/> instead. A plain Dalamud window that
/// never consults it would simply stay visible. See <see cref="NoireUI.RequiredVisibility"/>.
/// </remarks>
[Flags]
public enum UiVisibility
{
    /// <summary>
    /// The default: hidden during cutscenes, in group pose, and while the game UI is hidden, like ordinary plugin UI.
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
