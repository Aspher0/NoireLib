namespace NoireLib.Helpers;

/// <summary>What kind of collider the game registered, matching its own <c>ColliderType</c>.</summary>
public enum ColliderKind
{
    /// <summary>A streaming controller, which owns no geometry of its own.</summary>
    Streamed = 1,

    /// <summary>A collision mesh: streamed terrain, a placed background part, furniture, a dynamic object.</summary>
    Mesh = 2,

    /// <summary>A box, spanning minus one to plus one on every axis of its transform.</summary>
    Box = 3,

    /// <summary>A cylinder about the Y axis, unit radius and unit half-height.</summary>
    Cylinder = 4,

    /// <summary>A sphere of unit radius.</summary>
    Sphere = 5,

    /// <summary>A flat quad in the local XY plane, solid from the front only.</summary>
    Plane = 6,

    /// <summary>A flat quad in the local XY plane, solid from both sides.</summary>
    PlaneTwoSided = 7,
}
