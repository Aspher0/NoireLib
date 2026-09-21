using NoireLib.Draw3D.Interaction;
using NoireLib.Draw3D.Interaction.Gizmo;

namespace NoireLib.Draw3D.Scene;

public sealed partial class Scene3D
{
    /// <summary>This scene's own selection, which its <see cref="SceneEditor"/> and gizmo read from.</summary>
    public InteractSelection Selection { get; } = new();

    /// <summary>Creates a click-to-select <see cref="SceneEditor"/> bound to and owned by this scene.</summary>
    /// <param name="op">Which transform operations the gizmo exposes.</param>
    /// <returns>The new editor.</returns>
    public SceneEditor CreateEditor(GizmoOp op = GizmoOp.Universal)
    {
        var editor = new SceneEditor(this, op);
        return Own(editor);
    }
}
