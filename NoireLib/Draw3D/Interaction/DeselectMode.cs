using System;

namespace NoireLib.Draw3D.Interaction;

/// <summary>
/// How a scene's <see cref="InteractSelection"/> is cleared. Flags: combine freely (for example
/// <c>ClickEmpty | Key</c>). Selecting is separate (a left-click on an object, gated by
/// <see cref="NoireInteract.SelectOnClick"/>); this only governs <i>de</i>selection.
/// </summary>
[Flags]
public enum DeselectMode
{
    /// <summary>Never auto-deselect; the consumer clears the selection itself.</summary>
    None = 0,

    /// <summary>
    /// A left click on empty world (no object under the cursor and not over game/plugin UI) that did not become a
    /// camera pan clears the selection. A click-and-drag (a camera pan) never deselects.
    /// </summary>
    ClickEmpty = 1,

    /// <summary>Pressing the <see cref="NoireInteract.DeselectKey"/> (default Escape) clears the selection.</summary>
    Key = 2,
}
