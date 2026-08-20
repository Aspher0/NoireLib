using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>One collider standing in the game's loaded collision world, and everything it says about itself.</summary>
public readonly record struct ColliderInfo
{
    /// <summary>The collider's address, which names it for a follow-up query and is stable while it is loaded.</summary>
    public required nint Handle { get; init; }

    /// <summary>What kind of collider it is.</summary>
    public required ColliderKind Kind { get; init; }

    /// <summary>The layout instance it belongs to, the loaded form of a level file's placement.</summary>
    public required ulong LayoutObjectId { get; init; }

    /// <summary>Which collision layers it is on.</summary>
    public required ulong LayerMask { get; init; }

    /// <summary>The material bits the object overrides its geometry's with.</summary>
    public required ulong MaterialValue { get; init; }

    /// <summary>Which material bits that override replaces.</summary>
    public required ulong MaterialMask { get; init; }

    /// <summary>The visibility flags: bit 0 is raycast, bit 1 is the global visit pass.</summary>
    public required byte VisibilityFlags { get; init; }

    /// <summary>How many references the game holds to it.</summary>
    public required uint References { get; init; }

    /// <summary>The collision file it was loaded from, empty for an analytic collider.</summary>
    public required string ResourcePath { get; init; }

    /// <summary>Its position, as the game holds it.</summary>
    public required Vector3 Translation { get; init; }

    /// <summary>Its Euler rotation, as the game holds it. Meaningless for an analytic collider.</summary>
    public required Vector3 Rotation { get; init; }

    /// <summary>Its scale, as the game holds it. Meaningless for an analytic collider.</summary>
    public required Vector3 Scale { get; init; }

    /// <summary>The transform taking its local space into the world, as the game built it.</summary>
    public required Matrix4x4 World { get; init; }

    /// <summary>Lower corner of its world bounding box.</summary>
    public required Vector3 Min { get; init; }

    /// <summary>Upper corner of its world bounding box.</summary>
    public required Vector3 Max { get; init; }

    /// <summary>How many triangles its mesh holds. Zero for an analytic collider.</summary>
    public required int Primitives { get; init; }

    /// <summary>Whether its geometry has finished loading.</summary>
    public required bool Loaded { get; init; }

    /// <summary>Whether a raycast is allowed to hit it, which is the flag the game's own queries honour.</summary>
    public bool Raycastable => (VisibilityFlags & 0x1) != 0;
}
