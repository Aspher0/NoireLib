using System.Numerics;

namespace NoireLib.Draw3D.Interaction.Gizmo;

/// <summary>Tuning for a <see cref="NoireGizmo"/>: space, snapping, backend and on-screen sizing.</summary>
public sealed class GizmoOptions
{
    /// <summary>The frame translate/rotate handles align to (default <see cref="GizmoSpace.World"/>); both backends honour it identically, though scale handles are always object-local.</summary>
    public GizmoSpace Space { get; set; } = GizmoSpace.World;

    /// <summary>
    /// Which backend draws and drives the handles: <see cref="GizmoBackend.ImGuizmo"/> (default) is the classic flat 2D
    /// handles, always on top; <see cref="GizmoBackend.Native"/> is in-world depth handles, screen-hit-tested; flip it
    /// without touching call sites.
    /// </summary>
    public GizmoBackend Backend { get; set; } = GizmoBackend.ImGuizmo;

    /// <summary>
    /// Per-axis translation snap in world units (a component of 0 or less means no snap on that axis); translation is
    /// snapped per axis since a grid can differ along X/Y/Z, while rotation and scale snap are single values
    /// (see <see cref="RotateSnapDeg"/> and <see cref="ScaleSnap"/>).
    /// </summary>
    public Vector3 Snap { get; set; } = Vector3.Zero;

    /// <summary>Rotation snap, in degrees (0 or less means free).</summary>
    public float RotateSnapDeg { get; set; }

    /// <summary>Scale snap increment (0 or less means free).</summary>
    public float ScaleSnap { get; set; }

    /// <summary>
    /// How the native gizmo's handles are occluded: default <see cref="GizmoDepth.OnTopOfObjects"/> hides them behind
    /// the game world (walls / terrain) but keeps them on top of other 3D objects, so a handle is never buried inside
    /// the object it edits yet still reads as in-world; <see cref="GizmoDepth.AlwaysOnTop"/> restores full x-ray,
    /// <see cref="GizmoDepth.Occluded"/> is fully depth-tested, and any mode but <see cref="GizmoDepth.AlwaysOnTop"/>
    /// also makes a handle behind an obstacle un-grabbable, under <see cref="NoireInteract.ObstacleOcclusionMode"/>;
    /// the ImGuizmo backend is flat-on-top regardless.
    /// </summary>
    public GizmoDepth Depth { get; set; } = GizmoDepth.OnTopOfObjects;

    /// <summary>
    /// Optional hold-to-occlude override for the native gizmo: while it returns true the handles are occluded by the
    /// game world (on top of objects), while false they draw full x-ray, overriding <see cref="Depth"/>; null
    /// (default) uses the static <see cref="Depth"/>, e.g. <c>() =&gt; ImGui.GetIO().KeyAlt</c> occludes while Alt is held.
    /// </summary>
    public System.Func<bool>? OcclusionHeld { get; set; }

    /// <summary>Handle arm length in screen pixels (kept constant regardless of distance); default 105.</summary>
    public float HandlePixelLength { get; set; } = 105f;

    /// <summary>Handle line/arrow thickness in screen pixels; default 4.5.</summary>
    public float HandlePixelThickness { get; set; } = 4.5f;

    /// <summary>Grab tolerance in screen pixels around a handle; default 10.</summary>
    public float GrabPixelTolerance { get; set; } = 10f;

    /// <summary>
    /// Whether the gizmo draws the drag preview overlay (a fixed anchor at the pre-drag center, a guide line to the
    /// current center, and the live amount moved / rotated / scaled), default <b>true</b> on both backends; the
    /// ImGuizmo backend uses it in place of ImGuizmo's built-in text, suppressed since it reports a world-space delta
    /// that reads wrong in Local space, and false draws no readout on either backend.
    /// </summary>
    public bool ShowDragFeedback { get; set; } = true;
}
