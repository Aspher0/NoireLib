using System.Numerics;

namespace NoireLib.Draw3D.Interaction.Gizmo;

/// <summary>Tuning for a <see cref="NoireGizmo"/>: space, snapping, backend and on-screen sizing.</summary>
public sealed class GizmoOptions
{
    /// <summary>The frame translate/rotate handles align to. Default <see cref="GizmoSpace.World"/>. Both backends honour it identically (scale handles are always object-local).</summary>
    public GizmoSpace Space { get; set; } = GizmoSpace.World;

    /// <summary>
    /// Which backend draws and drives the handles. <see cref="GizmoBackend.ImGuizmo"/> (default) is the classic flat 2D
    /// handles, always on top; <see cref="GizmoBackend.Native"/> is in-world depth handles, screen-hit-tested. Flip it
    /// without touching call sites.
    /// </summary>
    public GizmoBackend Backend { get; set; } = GizmoBackend.ImGuizmo;

    /// <summary>
    /// Per-axis translation snap in world units (a component of 0 or less means no snap on that axis). Translation is
    /// snapped per axis because a grid can differ along X/Y/Z; rotation and scale snap are single values by nature
    /// (see <see cref="RotateSnapDeg"/> and <see cref="ScaleSnap"/>).
    /// </summary>
    public Vector3 Snap { get; set; } = Vector3.Zero;

    /// <summary>Rotation snap, in degrees (0 or less means free).</summary>
    public float RotateSnapDeg { get; set; }

    /// <summary>Scale snap increment (0 or less means free).</summary>
    public float ScaleSnap { get; set; }

    /// <summary>
    /// How the native gizmo's handles are occluded. Default <see cref="GizmoDepth.OnTopOfObjects"/>: hidden behind the
    /// game world (walls / terrain) but always on top of other 3D objects, so a handle is never buried inside the object
    /// it edits yet still reads as in-world. <see cref="GizmoDepth.AlwaysOnTop"/> restores full x-ray;
    /// <see cref="GizmoDepth.Occluded"/> is fully depth-tested. Any mode other than <see cref="GizmoDepth.AlwaysOnTop"/>
    /// also makes a handle behind a wall un-grabbable, under <see cref="NoireInteract.WallOcclusionMode"/>. The ImGuizmo
    /// backend is flat-on-top regardless.
    /// </summary>
    public GizmoDepth Depth { get; set; } = GizmoDepth.OnTopOfObjects;

    /// <summary>
    /// Optional hold-to-occlude override for the native gizmo: while it returns true the handles are occluded by the
    /// game world (on top of objects), and while false they draw full x-ray, overriding <see cref="Depth"/>. Leave null
    /// (default) to use the static <see cref="Depth"/>. For example <c>() =&gt; ImGui.GetIO().KeyAlt</c> occludes while Alt is held.
    /// </summary>
    public System.Func<bool>? OcclusionHeld { get; set; }

    /// <summary>Handle arm length in screen pixels (kept constant regardless of distance). Default 105.</summary>
    public float HandlePixelLength { get; set; } = 105f;

    /// <summary>Handle line/arrow thickness in screen pixels. Default 4.5.</summary>
    public float HandlePixelThickness { get; set; } = 4.5f;

    /// <summary>Grab tolerance in screen pixels around a handle. Default 10.</summary>
    public float GrabPixelTolerance { get; set; } = 10f;

    /// <summary>
    /// Whether the gizmo draws the drag preview overlay (a fixed anchor at the pre-drag center, a guide line to the
    /// current center, and the live amount moved / rotated / scaled). Default <b>true</b>. Both backends draw the same
    /// overlay: the ImGuizmo backend uses it in place of ImGuizmo's built-in text, which is suppressed because it reports
    /// a world-space delta that reads wrong in Local space. Set false to draw no drag readout on either backend.
    /// </summary>
    public bool ShowDragFeedback { get; set; } = true;
}
