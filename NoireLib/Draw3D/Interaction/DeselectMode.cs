using System;

namespace NoireLib.Draw3D.Interaction;

/// <summary>How a scene's <see cref="InteractSelection"/> is cleared. Flags combine (for example <c>ClickEmpty | Key</c>).</summary>
[Flags]
public enum DeselectMode
{
    /// <summary>Never auto-deselect. The consumer clears the selection itself.</summary>
    None = 0,

    /// <summary>A left click on empty world, not over UI and not turned into a drag, clears the selection.</summary>
    ClickEmpty = 1,

    /// <summary>Pressing the <see cref="NoireInteract.DeselectKeyHeld"/> key (default Escape) clears the selection.</summary>
    Key = 2,
}
