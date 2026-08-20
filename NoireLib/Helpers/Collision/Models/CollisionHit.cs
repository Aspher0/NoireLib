using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>What a ray struck in the game's collision world.</summary>
public readonly record struct CollisionHit
{
    /// <summary>Whether anything was struck.</summary>
    public required bool Found { get; init; }

    /// <summary>Where the ray met the surface.</summary>
    public required Vector3 Point { get; init; }

    /// <summary>The surface normal there, as the triangle is wound.</summary>
    public required Vector3 Normal { get; init; }

    /// <summary>How far along the ray it is, in world units.</summary>
    public required float Distance { get; init; }

    /// <summary>The material of the collider that owns the triangle.</summary>
    public required ulong Material { get; init; }

    /// <summary>The struck triangle's first corner.</summary>
    public required Vector3 A { get; init; }

    /// <summary>Its second corner.</summary>
    public required Vector3 B { get; init; }

    /// <summary>Its third corner.</summary>
    public required Vector3 C { get; init; }

    /// <summary>The collider that owns it.</summary>
    public required ColliderInfo Collider { get; init; }
}
