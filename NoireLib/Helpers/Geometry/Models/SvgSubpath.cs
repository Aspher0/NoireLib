using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>One subpath of an SVG path, its curves flattened to straight segments.</summary>
/// <param name="Points">The points, in path units, in drawing order.</param>
/// <param name="Closed">Whether the subpath ended with a close command, joining its last point back to its first.</param>
public readonly record struct SvgSubpath(Vector2[] Points, bool Closed);
