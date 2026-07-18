using NoireLib.Draw3D.Enums;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Geometry;

/// <summary>
/// Traces a ground decal as world-space lines. The single source of truth for "what does this decal look like as a
/// line", in both senses a decal has:
/// <br/>
/// - <see cref="BuildLoop"/> traces the <b>shape it paints</b> - the outline of the SDF the decal shader evaluates -
///   shared by the opt-in per-node outline (<see cref="Scene.SceneNode.ShowDecalShape"/>) and by wireframe mode.
/// <br/>
/// - <see cref="BuildVolumeCorners"/> traces the <b>projection box</b> - the volume the SDF is evaluated in - which is
///   what <see cref="Scene.SceneNode.ShowDecalVolume"/> draws. The box is deliberately not the shape: its footprint is
///   the SDF's bounding square and its sweep runs above and below the painted surface, so it answers "how far does this
///   projection reach" rather than "what does it look like".
/// <br/>
/// A decal has no geometry of its own, so anything that wants to draw one as lines has to re-derive it from
/// <see cref="Materials.Material.Shape"/> / <see cref="Materials.Material.ShapeParams"/> and the world matrix, which is
/// what this does.
/// </summary>
internal static class DecalOutline
{
    /// <summary>Corner count of a decal's projection box (<see cref="BuildVolumeCorners"/>).</summary>
    public const int VolumeCorners = 8;

    /// <summary>
    /// Fills <paramref name="corners"/> (length <see cref="VolumeCorners"/>) with the decal's projection-box corners in
    /// world space: 0-3 are the bottom face in loop order, 4-7 the top face directly above them. The 12 edges are
    /// therefore the two 4-point loops plus corner <c>i</c> to corner <c>i + 4</c>.
    /// </summary>
    /// <param name="world">The decal's world matrix, constraint already applied.</param>
    /// <param name="corners">Receives the 8 world-space corners.</param>
    public static void BuildVolumeCorners(in Matrix4x4 world, Span<Vector3> corners)
    {
        // The decal volume is the unit box the shader tests lp against (any(abs(lp) > 0.5) rejects), so the corners are
        // the eight combinations of +/-0.5 - bottom face first, both faces wound the same way so i -> i+4 is a vertical.
        corners[0] = Vector3.Transform(new Vector3(-0.5f, -0.5f, -0.5f), world);
        corners[1] = Vector3.Transform(new Vector3(+0.5f, -0.5f, -0.5f), world);
        corners[2] = Vector3.Transform(new Vector3(+0.5f, -0.5f, +0.5f), world);
        corners[3] = Vector3.Transform(new Vector3(-0.5f, -0.5f, +0.5f), world);
        corners[4] = Vector3.Transform(new Vector3(-0.5f, +0.5f, -0.5f), world);
        corners[5] = Vector3.Transform(new Vector3(+0.5f, +0.5f, -0.5f), world);
        corners[6] = Vector3.Transform(new Vector3(+0.5f, +0.5f, +0.5f), world);
        corners[7] = Vector3.Transform(new Vector3(-0.5f, +0.5f, +0.5f), world);
    }

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
