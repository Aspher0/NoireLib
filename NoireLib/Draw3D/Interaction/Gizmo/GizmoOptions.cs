using System.Numerics;

namespace NoireLib.Draw3D.Interaction.Gizmo;

/// <summary>Tuning for a <see cref="NoireGizmo"/>: space, snapping, backend and on-screen sizing.</summary>
public sealed class GizmoOptions
{
    /// <summary>The frame translate and rotate handles align to (default <see cref="GizmoSpace.World"/>). Scale handles are always object-local.</summary>
    public GizmoSpace Space { get; set; } = GizmoSpace.World;

    /// <summary>Which backend draws and drives the handles (default <see cref="GizmoBackend.ImGuizmo"/>).</summary>
    public GizmoBackend Backend { get; set; } = GizmoBackend.ImGuizmo;

    /// <summary>Per-axis translation snap in world units. A component of 0 or less leaves that axis free.</summary>
    public Vector3 Snap { get; set; } = Vector3.Zero;

    /// <summary>Rotation snap, in degrees (0 or less means free).</summary>
    public float RotateSnapDeg { get; set; }

    /// <summary>Scale snap increment (0 or less means free).</summary>
    public float ScaleSnap { get; set; }

    /// <summary>How the native gizmo's handles are occluded (default <see cref="GizmoDepth.OnTopOfObjects"/>). Under obstacle occlusion a hidden handle is also ungrabbable.</summary>
    public GizmoDepth Depth { get; set; } = GizmoDepth.OnTopOfObjects;

    /// <summary>Optional override of <see cref="Depth"/> for the native gizmo. Occluded by the world while it returns true, always on top while false.</summary>
    public System.Func<bool>? OcclusionHeld { get; set; }

    /// <summary>Handle arm length in screen pixels, constant at any distance (default 105).</summary>
    public float HandlePixelLength { get; set; } = 105f;

    /// <summary>Handle line and arrow thickness in screen pixels (default 4.5).</summary>
    public float HandlePixelThickness { get; set; } = 4.5f;

    /// <summary>Grab tolerance in screen pixels around a handle (default 10).</summary>
    public float GrabPixelTolerance { get; set; } = 10f;

    /// <summary>Whether the gizmo draws the drag preview, a pre-drag anchor, a guide line and the live amount moved, rotated or scaled (default true).</summary>
    public bool ShowDragFeedback { get; set; } = true;
}
