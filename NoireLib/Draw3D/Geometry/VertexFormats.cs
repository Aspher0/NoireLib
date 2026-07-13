using System.Numerics;
using System.Runtime.InteropServices;

namespace NoireLib.Draw3D.Geometry;

/// <summary>
/// The one vertex format of the Draw3D core: 48 bytes — position, normal, UV, straight-alpha color.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct Vertex3D
{
    /// <summary>Model-space position.</summary>
    public Vector3 Position;

    /// <summary>Outward model-space normal.</summary>
    public Vector3 Normal;

    /// <summary>Texture coordinates in [0,1].</summary>
    public Vector2 Uv;

    /// <summary>Vertex color, straight alpha (premultiplied inside the shader).</summary>
    public Vector4 Color;

    /// <summary>Creates a vertex.</summary>
    public Vertex3D(Vector3 position, Vector3 normal, Vector2 uv, Vector4 color)
    {
        Position = position;
        Normal = normal;
        Uv = uv;
        Color = color;
    }
}

/// <summary>
/// Per-instance stream data (input slot 1): the world matrix as four UNtransposed rows plus an instance tint.<br/>
/// Rows are untransposed because HLSL's <c>float4x4(r0..r3)</c> attribute constructor builds logical rows directly,
/// bypassing constant-buffer packing — see the note in Unlit.hlsl.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct InstanceData
{
    /// <summary>World matrix row 1 (M11..M14).</summary>
    public Vector4 W0;
    /// <summary>World matrix row 2 (M21..M24).</summary>
    public Vector4 W1;
    /// <summary>World matrix row 3 (M31..M34).</summary>
    public Vector4 W2;
    /// <summary>World matrix row 4 (M41..M44).</summary>
    public Vector4 W3;
    /// <summary>Per-instance color multiplier, straight alpha.</summary>
    public Vector4 Color;

    /// <summary>Builds instance data from a world matrix and tint.</summary>
    public static InstanceData From(in Matrix4x4 world, Vector4 color) => new()
    {
        W0 = new Vector4(world.M11, world.M12, world.M13, world.M14),
        W1 = new Vector4(world.M21, world.M22, world.M23, world.M24),
        W2 = new Vector4(world.M31, world.M32, world.M33, world.M34),
        W3 = new Vector4(world.M41, world.M42, world.M43, world.M44),
        Color = color,
    };
}

/// <summary>
/// A conservative bounding sphere used for frustum culling and picking.
/// </summary>
public readonly struct BoundingSphere
{
    /// <summary>Sphere center.</summary>
    public readonly Vector3 Center;

    /// <summary>Sphere radius.</summary>
    public readonly float Radius;

    /// <summary>Creates a bounding sphere.</summary>
    public BoundingSphere(Vector3 center, float radius)
    {
        Center = center;
        Radius = radius;
    }

    /// <summary>Computes a conservative sphere for a vertex set: center = middle of the AABB, radius = max distance to it.</summary>
    /// <param name="vertices">The vertices to bound.</param>
    public static BoundingSphere FromVertices(System.ReadOnlySpan<Vertex3D> vertices)
    {
        if (vertices.Length == 0)
            return new BoundingSphere(Vector3.Zero, 0f);

        Vector3 min = vertices[0].Position, max = vertices[0].Position;
        foreach (ref readonly var v in vertices)
        {
            min = Vector3.Min(min, v.Position);
            max = Vector3.Max(max, v.Position);
        }

        var center = (min + max) * 0.5f;
        var radiusSq = 0f;
        foreach (ref readonly var v in vertices)
            radiusSq = System.MathF.Max(radiusSq, Vector3.DistanceSquared(center, v.Position));

        return new BoundingSphere(center, System.MathF.Sqrt(radiusSq));
    }

    /// <summary>Transforms the sphere by a world matrix (radius scaled by the largest axis scale — conservative).</summary>
    /// <param name="world">The world matrix to apply.</param>
    public BoundingSphere Transform(in Matrix4x4 world)
    {
        var center = Vector3.Transform(Center, world);
        var sx = new Vector3(world.M11, world.M12, world.M13).LengthSquared();
        var sy = new Vector3(world.M21, world.M22, world.M23).LengthSquared();
        var sz = new Vector3(world.M31, world.M32, world.M33).LengthSquared();
        var maxScale = System.MathF.Sqrt(System.MathF.Max(sx, System.MathF.Max(sy, sz)));
        return new BoundingSphere(center, Radius * maxScale);
    }
}

/// <summary>
/// CPU-side mesh data produced by <see cref="MeshBuilder"/>: a vertex array and a 16-bit index array (clockwise-front winding).
/// </summary>
/// <param name="Vertices">Vertex array.</param>
/// <param name="Indices">Index array (triangle list, clockwise front).</param>
public readonly record struct MeshData(Vertex3D[] Vertices, ushort[] Indices);
