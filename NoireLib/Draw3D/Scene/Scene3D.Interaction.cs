using NoireLib.Draw3D.Interaction;
using NoireLib.Draw3D.Interaction.Gizmo;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Per-scene interaction state. Selection is a property of the scene, not a process-global: each scene has its own
/// <see cref="Selection"/>, so multiple scenes select independently and there is no global mode to set and reset.
/// A left-click on an interactable, selectable node routes into that node's scene selection automatically.
/// </summary>
public sealed partial class Scene3D
{
    /// <summary>
    /// This scene's selection - the set the scene's <see cref="SceneEditor"/> and gizmo read from. Own to the scene:
    /// two scenes have two independent selections. Single by default; drive multi-select through
    /// <see cref="SceneEditor.MultiSelect"/> (scoped) or <see cref="InteractSelection.Mode"/> directly.
    /// </summary>
    public InteractSelection Selection { get; } = new();

    /// <summary>
    /// Creates a <see cref="SceneEditor"/> - "click to select, gizmo follows the selection" - bound to this scene and
    /// <b>owned</b> by it: <see cref="Dispose"/> tears it down. Configure via <c>editor.Gizmo</c> / <c>editor.MultiSelect</c>;
    /// make nodes pickable with <see cref="SceneNode.MakeSelectable"/>. Disposing the editor yourself is optional.
    /// </summary>
    /// <param name="op">Which transform operations the gizmo exposes. Default <see cref="GizmoOp.Universal"/>.</param>
    public SceneEditor CreateEditor(GizmoOp op = GizmoOp.Universal)
    {
        var editor = new SceneEditor(this, op);
        return Own(editor);
    }
}
