using System;

namespace NoireLib.Draw3D.Interaction.Gizmo;

/// <summary>Which transform operations a <see cref="NoireGizmo"/> exposes. Combine for a universal gizmo.</summary>
[Flags]
public enum GizmoOp
{
    /// <summary>No handles.</summary>
    None = 0,

    /// <summary>Axis arrows + plane quads + a screen-plane center for moving.</summary>
    Translate = 1,

    /// <summary>Axis rings + a screen-facing ring for rotating.</summary>
    Rotate = 2,

    /// <summary>Axis knobs + a center knob for scaling.</summary>
    Scale = 4,

    /// <summary>Translate + Rotate + Scale at once.</summary>
    Universal = Translate | Rotate | Scale,
}

/// <summary>The frame the translate/rotate handles are aligned to.</summary>
public enum GizmoSpace
{
    /// <summary>Axis-aligned to the world.</summary>
    World,

    /// <summary>Aligned to the target's own rotation (for a group, to the first selected node).</summary>
    Local,
}

/// <summary>How the native gizmo's handles are occluded (the ImGuizmo backend is always flat-on-top, unaffected).</summary>
public enum GizmoDepth
{
    /// <summary>
    /// Occluded by the game world (walls / terrain) but always drawn on top of other 3D objects. The default and the
    /// editor-friendly choice: the handle stays visible over the object it edits, yet hides behind a wall like real
    /// geometry.
    /// </summary>
    OnTopOfObjects,

    /// <summary>Drawn on top of absolutely everything: world and 3D objects alike (x-ray). A handle is never hidden.</summary>
    AlwaysOnTop,

    /// <summary>Fully depth-tested: occluded by the world <i>and</i> by other 3D objects (can be buried inside the object it edits).</summary>
    Occluded,
}

/// <summary>Which gizmo implementation draws and solves the handles.</summary>
public enum GizmoBackend
{
    /// <summary>
    /// In-world depth gizmos: real depth-tested geometry drawn through <see cref="Im.ImDraw3D"/> and hit-tested with the
    /// render-time camera: occludes correctly, never wobbles under camera motion, can render through walls, and supports
    /// per-axis universal snapping. The right backend for the V2 renderer, and the default.
    /// </summary>
    Native,

    /// <summary>
    /// The classic 2D-projected ImGui gizmo, drawn by <c>Dalamud.Bindings.ImGuizmo</c> and fed the render camera's
    /// view/projection. Same API surface as <see cref="Native"/>. Familiar look, but flat (no depth occlusion) and
    /// coarser universal snapping. Pick it when you want the ImGuizmo feel over depth-correctness.
    /// </summary>
    ImGuizmo,
}

/// <summary>One grabbable element of a gizmo. Axis handles carry an axis index (0 = X, 1 = Y, 2 = Z).</summary>
public enum GizmoHandle
{
    /// <summary>Nothing grabbed.</summary>
    None,

    /// <summary>Translate along X / Y / Z.</summary>
    TranslateX, TranslateY, TranslateZ,

    /// <summary>Translate on the YZ / ZX / XY plane.</summary>
    TranslateYZ, TranslateZX, TranslateXY,

    /// <summary>Translate freely on the camera-facing plane.</summary>
    TranslateScreen,

    /// <summary>Rotate about X / Y / Z.</summary>
    RotateX, RotateY, RotateZ,

    /// <summary>Rotate about the camera-facing axis.</summary>
    RotateScreen,

    /// <summary>Scale along X / Y / Z (object space).</summary>
    ScaleX, ScaleY, ScaleZ,

    /// <summary>Uniform scale (center knob).</summary>
    ScaleUniform,
}

/// <summary>Helpers to classify a <see cref="GizmoHandle"/>.</summary>
internal static class GizmoHandleInfo
{
    /// <summary>The axis index (0/1/2) an axis-bound handle addresses, or -1 for screen/uniform handles.</summary>
    public static int AxisIndex(GizmoHandle h) => h switch
    {
        GizmoHandle.TranslateX or GizmoHandle.RotateX or GizmoHandle.ScaleX or GizmoHandle.TranslateYZ => 0,
        GizmoHandle.TranslateY or GizmoHandle.RotateY or GizmoHandle.ScaleY or GizmoHandle.TranslateZX => 1,
        GizmoHandle.TranslateZ or GizmoHandle.RotateZ or GizmoHandle.ScaleZ or GizmoHandle.TranslateXY => 2,
        _ => -1,
    };

    public static bool IsTranslate(GizmoHandle h) => h is >= GizmoHandle.TranslateX and <= GizmoHandle.TranslateScreen;

    public static bool IsRotate(GizmoHandle h) => h is >= GizmoHandle.RotateX and <= GizmoHandle.RotateScreen;

    public static bool IsScale(GizmoHandle h) => h is >= GizmoHandle.ScaleX and <= GizmoHandle.ScaleUniform;

    public static bool IsPlane(GizmoHandle h) => h is GizmoHandle.TranslateYZ or GizmoHandle.TranslateZX or GizmoHandle.TranslateXY;
}
