using System.Numerics;

namespace NoireLib.Draw3D.Interaction.Gizmo;

/// <summary>Tuning for a <see cref="NoireGizmo"/> — space, snapping, backend and on-screen sizing.</summary>
public sealed class GizmoOptions
{
    /// <summary>The frame translate/rotate handles align to. Default <see cref="GizmoSpace.World"/>.</summary>
    public GizmoSpace Space { get; set; } = GizmoSpace.World;

    /// <summary>
    /// Which backend draws and drives the handles. <see cref="GizmoBackend.Native"/> (default) = in-world depth handles,
    /// ray-hit-tested; <see cref="GizmoBackend.ImGuizmo"/> = the classic flat 2D handles, always on top. Flip it without
    /// touching call sites.
    /// </summary>
    public GizmoBackend Backend { get; set; } = GizmoBackend.Native;

    /// <summary>Per-axis translation snap in world units (component ≤ 0 = no snap on that axis). Also applied while a snap modifier is held.</summary>
    public Vector3 Snap { get; set; } = Vector3.Zero;

    /// <summary>Rotation snap in degrees (≤ 0 = free).</summary>
    public float RotateSnapDeg { get; set; }

    /// <summary>Scale snap increment (≤ 0 = free).</summary>
    public float ScaleSnap { get; set; }

    /// <summary>
    /// Draw handles on top of everything — the game world <i>and</i> other 3D objects — so a handle is always grabbable,
    /// never buried inside the object it edits or hidden behind terrain. Default true, the practical choice for an editor
    /// gizmo. Turn off for physically-correct occlusion (a handle can then hide behind geometry, including its object).
    /// </summary>
    public bool AlwaysOnTop { get; set; } = true;

    /// <summary>Handle arm length in screen pixels (kept constant regardless of distance). Default 90.</summary>
    public float HandlePixelLength { get; set; } = 90f;

    /// <summary>Handle line/arrow thickness in screen pixels. Default 5.</summary>
    public float HandlePixelThickness { get; set; } = 5f;

    /// <summary>Grab tolerance in screen pixels around a handle. Default 8.</summary>
    public float GrabPixelTolerance { get; set; } = 8f;
}
