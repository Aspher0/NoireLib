using NoireLib.Draw3D.Enums;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Geometry;

/// <summary>
/// Traces the shape a ground decal actually paints - the outline of the SDF the decal shader evaluates - as world-space
/// closed loops. The single source of truth for "what does this decal look like as a line", shared by the opt-in
/// per-node outline (<see cref="Scene.SceneNode.ShowDecalShape"/>) and by wireframe mode.
/// <br/>
/// A decal has no geometry of its own: its box is only the volume the SDF is evaluated in, and the shape lives in the
/// pixel shader. Anything that wants to draw a decal as lines has to re-derive the shape from
/// <see cref="Materials.Material.Shape"/> / <see cref="Materials.Material.ShapeParams"/>, which is what this does.
/// </summary>
internal static class DecalOutline
{
    /// <summary>Segment count for a full turn of a curved outline; a partial arc gets a proportional share.</summary>
    public const int Segments = 64;

    /// <summary>How many separate closed loops <paramref name="shape"/> traces: two for a ring with a real inner radius, one otherwise.</summary>
    public static int LoopCount(DecalShape shape, Vector4 shapeParams) => shape switch
    {
        DecalShape.Ring => HasInner(shapeParams.X) ? 2 : 1,
        DecalShape.Sector => IsFullTurn(shapeParams.X) && HasInner(shapeParams.Y) ? 2 : 1,
        _ => 1,
    };

    /// <summary>
    /// Fills <paramref name="points"/> (cleared first) with loop <paramref name="index"/> of the shape's outline, in world
    /// space. Every loop is closed: the caller joins the last point back to the first rather than repeating it.
    /// </summary>
    /// <param name="shape">The decal's shape.</param>
    /// <param name="shapeParams">The decal's shape parameters (see <see cref="DecalShape"/> members).</param>
    /// <param name="world">The decal's world matrix, constraint already applied.</param>
    /// <param name="index">Loop index, 0 based (see <see cref="LoopCount"/>). Loop 0 is the outer edge.</param>
    /// <param name="points">Receives the loop's world-space points.</param>
    public static void BuildLoop(DecalShape shape, Vector4 shapeParams, in Matrix4x4 world, int index, List<Vector3> points)
    {
        points.Clear();
        switch (shape)
        {
            case DecalShape.Ring:
                Circle(index == 0 ? 1f : Math.Clamp(shapeParams.X, 0f, 1f), in world, points);
                break;

            case DecalShape.Sector:
                // A slice half a turn or wider closes on itself: the angular edges vanish and it is a ring (or a disc).
                if (IsFullTurn(shapeParams.X))
                    Circle(index == 0 ? 1f : Math.Clamp(shapeParams.Y, 0f, 1f), in world, points);
                else
                    Sector(MathF.Abs(shapeParams.X), Math.Clamp(shapeParams.Y, 0f, 1f), in world, points);
                break;

            case DecalShape.Rect:
            case DecalShape.Texture:
                points.Add(ToWorld(-1f, -1f, in world));
                points.Add(ToWorld(+1f, -1f, in world));
                points.Add(ToWorld(+1f, +1f, in world));
                points.Add(ToWorld(-1f, +1f, in world));
                break;

            default: // Circle
                Circle(1f, in world, points);
                break;
        }
    }

    private static bool HasInner(float ratio) => ratio > 1e-3f;

    private static bool IsFullTurn(float halfAngle) => MathF.Abs(halfAngle) >= MathF.PI - 1e-4f;

    private static void Circle(float radius, in Matrix4x4 world, List<Vector3> points)
    {
        for (var i = 0; i < Segments; i++)
        {
            var (sin, cos) = MathF.SinCos(MathF.Tau * i / Segments);
            points.Add(ToWorld(radius * sin, radius * cos, in world));
        }
    }

    /// <summary>The outer arc, then the inner arc back (or the apex), which the caller's closing segment joins into a wedge.</summary>
    private static void Sector(float halfAngle, float inner, in Matrix4x4 world, List<Vector3> points)
    {
        var arc = Math.Max(2, (int)MathF.Ceiling(Segments * halfAngle / MathF.PI));
        for (var i = 0; i <= arc; i++)
        {
            var (sin, cos) = MathF.SinCos(-halfAngle + 2f * halfAngle * i / arc);
            points.Add(ToWorld(sin, cos, in world));
        }

        if (!HasInner(inner))
        {
            points.Add(ToWorld(0f, 0f, in world)); // no inner radius: the apex closes the wedge
            return;
        }

        for (var i = arc; i >= 0; i--)
        {
            var (sin, cos) = MathF.SinCos(-halfAngle + 2f * halfAngle * i / arc);
            points.Add(ToWorld(inner * sin, inner * cos, in world));
        }
    }

    /// <summary>
    /// Footprint space to world. The shader evaluates its SDF on <c>p = local.xz * 2</c>, putting the shape's outer edge at
    /// |p| = 1, so a footprint point maps back to local <c>(p.x / 2, 0, p.y / 2)</c> - local Y 0 being the decal's own
    /// plane, the anchor its projection sweeps from. The angle runs from local +Z, matching the shader's <c>atan2(p.x, p.y)</c>.
    /// </summary>
    private static Vector3 ToWorld(float px, float pz, in Matrix4x4 world)
        => Vector3.Transform(new Vector3(px * 0.5f, 0f, pz * 0.5f), world);
}
