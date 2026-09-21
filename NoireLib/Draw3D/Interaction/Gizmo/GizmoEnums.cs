using System;

namespace NoireLib.Draw3D.Interaction.Gizmo;

/// <summary>Which transform operations a <see cref="NoireGizmo"/> exposes. Flags combine.</summary>
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

/// <summary>How the native gizmo's handles are occluded. The ImGuizmo backend always draws on top.</summary>
public enum GizmoDepth
{
    /// <summary>Occluded by the game world but drawn on top of other 3D objects (the default).</summary>
    OnTopOfObjects,

    /// <summary>Drawn on top of everything. A handle is never hidden.</summary>
    AlwaysOnTop,

    /// <summary>Fully depth-tested against the world and other 3D objects, including the object being edited.</summary>
    Occluded,
}

/// <summary>Which gizmo implementation draws and solves the handles.</summary>
public enum GizmoBackend
{
    /// <summary>Depth-tested in-world handles drawn through <see cref="Im.ImDraw3D"/> and hit-tested with the render-time camera.</summary>
    Native,

    /// <summary>The flat, always-on-top 2D gizmo drawn by <c>Dalamud.Bindings.ImGuizmo</c> from the render camera (the default).</summary>
    ImGuizmo,
}

/// <summary>One grabbable element of a gizmo.</summary>
public enum GizmoHandle
{
    /// <summary>Nothing grabbed.</summary>
    None,

    /// <summary>Translate along X.</summary>
    TranslateX,

    /// <summary>Translate along Y.</summary>
    TranslateY,

    /// <summary>Translate along Z.</summary>
    TranslateZ,

    /// <summary>Translate on the YZ plane.</summary>
    TranslateYZ,

    /// <summary>Translate on the ZX plane.</summary>
    TranslateZX,

    /// <summary>Translate on the XY plane.</summary>
    TranslateXY,

    /// <summary>Translate freely on the camera-facing plane.</summary>
    TranslateScreen,

    /// <summary>Rotate about X.</summary>
    RotateX,

    /// <summary>Rotate about Y.</summary>
    RotateY,

    /// <summary>Rotate about Z.</summary>
    RotateZ,

    /// <summary>Rotate about the camera-facing axis.</summary>
    RotateScreen,

    /// <summary>Scale along the object's X axis.</summary>
    ScaleX,

    /// <summary>Scale along the object's Y axis.</summary>
    ScaleY,

    /// <summary>Scale along the object's Z axis.</summary>
    ScaleZ,

    /// <summary>Uniform scale (center knob).</summary>
    ScaleUniform,
}

internal static class GizmoHandleInfo
{
    // Axis index (0/1/2) of an axis-bound handle, or -1 for screen and uniform handles.
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
