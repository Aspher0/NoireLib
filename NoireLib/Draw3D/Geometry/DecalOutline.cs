using NoireLib.Draw3D.Enums;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace NoireLib.Draw3D.Geometry;

// A decal has no geometry of its own. BuildLoop traces the painted SDF outline, BuildVolumeCorners the projection box.
internal static class DecalOutline
{
    public const int VolumeCorners = 8;

    // Corners 0-3 are the bottom face in loop order, 4-7 the top. Corner i joins corner i + 4.
    public static void BuildVolumeCorners(in Matrix4x4 world, Span<Vector3> corners)
    {
        // The shader rejects any(abs(lp) > 0.5).
        corners[0] = Vector3.Transform(new Vector3(-0.5f, -0.5f, -0.5f), world);
        corners[1] = Vector3.Transform(new Vector3(+0.5f, -0.5f, -0.5f), world);
        corners[2] = Vector3.Transform(new Vector3(+0.5f, -0.5f, +0.5f), world);
        corners[3] = Vector3.Transform(new Vector3(-0.5f, -0.5f, +0.5f), world);
        corners[4] = Vector3.Transform(new Vector3(-0.5f, +0.5f, -0.5f), world);
        corners[5] = Vector3.Transform(new Vector3(+0.5f, +0.5f, -0.5f), world);
        corners[6] = Vector3.Transform(new Vector3(+0.5f, +0.5f, +0.5f), world);
        corners[7] = Vector3.Transform(new Vector3(-0.5f, +0.5f, +0.5f), world);
    }

    public const int Segments = 64;

    public static int LoopCount(DecalShape shape, Vector4 shapeParams) => shape switch
    {
        DecalShape.Ring => HasInner(shapeParams.X) ? 2 : 1,
        DecalShape.Sector => IsFullTurn(shapeParams.X) && HasInner(shapeParams.Y) ? 2 : 1,
        _ => 1,
    };

    // Loop 0 is the outer edge. The last point is not repeated.
    public static void BuildLoop(DecalShape shape, Vector4 shapeParams, in Matrix4x4 world, int index, List<Vector3> points)
    {
        points.Clear();
        switch (shape)
        {
            case DecalShape.Ring:
                Circle(index == 0 ? 1f : Math.Clamp(shapeParams.X, 0f, 1f), in world, points);
                break;

            case DecalShape.Sector:
                // Half a turn or wider closes into a ring or disc.
                if (IsFullTurn(shapeParams.X))
                    Circle(index == 0 ? 1f : Math.Clamp(shapeParams.Y, 0f, 1f), in world, points);
                else
                    Sector(MathF.Abs(shapeParams.X), Math.Clamp(shapeParams.Y, 0f, 1f), in world, points);
                break;

            case DecalShape.Chevron:
                Chevron(shapeParams.X, in world, points);
                break;

            case DecalShape.Rect:
            case DecalShape.Texture:
                points.Add(ToWorld(-1f, -1f, in world));
                points.Add(ToWorld(+1f, -1f, in world));
                points.Add(ToWorld(+1f, +1f, in world));
                points.Add(ToWorld(-1f, +1f, in world));
                break;

            default:
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
            points.Add(ToWorld(0f, 0f, in world));
            return;
        }

        for (var i = arc; i >= 0; i--)
        {
            var (sin, cos) = MathF.SinCos(-halfAngle + 2f * halfAngle * i / arc);
            points.Add(ToWorld(inner * sin, inner * cos, in world));
        }
    }

    private static void Chevron(float stroke, in Matrix4x4 world, List<Vector3> points)
    {
        Span<Vector2> corners = stackalloc Vector2[6];
        ChevronOutline(stroke, corners);

        foreach (var corner in corners)
            points.Add(ToWorld(corner.X, corner.Y, in world));
    }

    // Mitred at the tip like the shader. Stroke 0 means 0.22.
    public static void ChevronOutline(float stroke, Span<Vector2> corners)
    {
        var half = stroke > 0f ? stroke : 0.22f;
        var reach = 1f - half;
        var left = new Vector2(-reach, -0.6f * reach);
        var tip = new Vector2(0f, 0.6f * reach);
        var right = new Vector2(reach, -0.6f * reach);

        var up = Vector2.Normalize(tip - left);
        var down = Vector2.Normalize(right - tip);
        var leftNormal = new Vector2(-up.Y, up.X);
        var rightNormal = new Vector2(-down.Y, down.X);
        var mitre = (leftNormal + rightNormal) * (half / (1f + Vector2.Dot(leftNormal, rightNormal)));

        corners[0] = left + leftNormal * half;
        corners[1] = tip + mitre;
        corners[2] = right + rightNormal * half;
        corners[3] = right - rightNormal * half;
        corners[4] = tip - mitre;
        corners[5] = left - leftNormal * half;
    }

    // The shader's SDF runs on p = local.xz * 2 with the angle from local +Z.
    private static Vector3 ToWorld(float px, float pz, in Matrix4x4 world)
        => Vector3.Transform(new Vector3(px * 0.5f, 0f, pz * 0.5f), world);
}
