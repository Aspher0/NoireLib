using System;
using System.Numerics;

namespace NoireLib.GameWatcher;

/// <summary>
/// A territory-bound spatial shape used by region watchers: circles, boxes or arbitrary predicates.
/// </summary>
public abstract class RegionShape
{
    private protected RegionShape() { }

    /// <summary>
    /// Determines whether a world-space position is inside the shape.
    /// </summary>
    /// <param name="position">The position to test.</param>
    /// <returns>True when the position is inside.</returns>
    public abstract bool Contains(Vector3 position);

    /// <summary>
    /// Determines whether a position is inside the shape expanded by a margin — used for exit hysteresis
    /// so a subject oscillating on the boundary does not flap.
    /// </summary>
    /// <param name="position">The position to test.</param>
    /// <param name="margin">The expansion margin in yalms.</param>
    /// <returns>True when the position is inside the expanded shape.</returns>
    public abstract bool ContainsWithMargin(Vector3 position, float margin);

    /// <summary>
    /// A circle on the horizontal plane (Y is ignored), centered at a world position.
    /// </summary>
    /// <param name="center">The circle center.</param>
    /// <param name="radius">The radius in yalms.</param>
    /// <returns>The shape.</returns>
    public static RegionShape Circle(Vector3 center, float radius) => new CircleShape(center, radius);

    /// <summary>
    /// An axis-aligned box between two world positions (inclusive on all three axes).
    /// </summary>
    /// <param name="cornerA">One corner of the box.</param>
    /// <param name="cornerB">The opposite corner of the box.</param>
    /// <returns>The shape.</returns>
    public static RegionShape Box(Vector3 cornerA, Vector3 cornerB) => new BoxShape(cornerA, cornerB);

    /// <summary>
    /// An arbitrary predicate shape. Hysteresis margins do not apply to predicate shapes.
    /// </summary>
    /// <param name="contains">The membership predicate.</param>
    /// <returns>The shape.</returns>
    public static RegionShape Predicate(Func<Vector3, bool> contains) => new PredicateShape(contains);

    private sealed class CircleShape : RegionShape
    {
        private readonly Vector3 center;
        private readonly float radius;

        public CircleShape(Vector3 center, float radius)
        {
            if (radius <= 0)
                throw new ArgumentOutOfRangeException(nameof(radius), "Radius must be positive.");

            this.center = center;
            this.radius = radius;
        }

        public override bool Contains(Vector3 position)
            => HorizontalDistanceSquared(position, center) <= radius * radius;

        public override bool ContainsWithMargin(Vector3 position, float margin)
        {
            var r = radius + margin;
            return HorizontalDistanceSquared(position, center) <= r * r;
        }

        private static float HorizontalDistanceSquared(Vector3 a, Vector3 b)
        {
            var dx = a.X - b.X;
            var dz = a.Z - b.Z;
            return dx * dx + dz * dz;
        }
    }

    private sealed class BoxShape : RegionShape
    {
        private readonly Vector3 min;
        private readonly Vector3 max;

        public BoxShape(Vector3 a, Vector3 b)
        {
            min = Vector3.Min(a, b);
            max = Vector3.Max(a, b);
        }

        public override bool Contains(Vector3 p)
            => p.X >= min.X && p.X <= max.X
            && p.Y >= min.Y && p.Y <= max.Y
            && p.Z >= min.Z && p.Z <= max.Z;

        public override bool ContainsWithMargin(Vector3 p, float margin)
            => p.X >= min.X - margin && p.X <= max.X + margin
            && p.Y >= min.Y - margin && p.Y <= max.Y + margin
            && p.Z >= min.Z - margin && p.Z <= max.Z + margin;
    }

    private sealed class PredicateShape : RegionShape
    {
        private readonly Func<Vector3, bool> contains;

        public PredicateShape(Func<Vector3, bool> contains)
            => this.contains = contains ?? throw new ArgumentNullException(nameof(contains));

        public override bool Contains(Vector3 position) => contains(position);

        public override bool ContainsWithMargin(Vector3 position, float margin) => contains(position);
    }
}
