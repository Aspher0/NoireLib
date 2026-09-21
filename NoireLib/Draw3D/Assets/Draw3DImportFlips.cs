using NoireLib.Draw3D.Geometry;
using System.Numerics;

namespace NoireLib.Draw3D.Assets;

/// <summary>
/// Orientation overrides applied to every imported mesh.<br/>
/// Off by default. A change affects models imported afterwards.
/// </summary>
public sealed class Draw3DImportFlips
{
    /// <summary>Reflect through the YZ plane.</summary>
    public bool MirrorX { get; set; }

    /// <summary>Reflect through the XY plane.</summary>
    public bool MirrorZ { get; set; }

    /// <summary>Reverse the triangle index order again, for a file whose winding was already converted to clockwise-front.</summary>
    public bool ReverseWinding { get; set; }

    /// <summary>Mirror the texture horizontally.</summary>
    public bool FlipU { get; set; }

    /// <summary>Mirror the texture vertically.</summary>
    public bool FlipV { get; set; }

    /// <summary>Whether any override is enabled.</summary>
    public bool Any => MirrorX || MirrorZ || ReverseWinding || FlipU || FlipV;

    /// <summary>Turns everything off, restoring each loader's own conversion.</summary>
    public void Reset()
    {
        MirrorX = false;
        MirrorZ = false;
        ReverseWinding = false;
        FlipU = false;
        FlipV = false;
    }

    internal void Apply(Vertex3D[] vertices, ushort[] indices)
    {
        if (!Any)
            return;

        ApplyToVertices(vertices);

        if (!ReverseWinding)
            return;

        for (var i = 0; i + 2 < indices.Length; i += 3)
            (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
    }

    internal void Apply(Vertex3D[] vertices, System.Collections.Generic.List<uint> indices)
    {
        if (!Any)
            return;

        ApplyToVertices(vertices);

        if (!ReverseWinding)
            return;

        for (var i = 0; i + 2 < indices.Count; i += 3)
            (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
    }

    // A hierarchical model must mirror vertices and transforms together.
    internal Matrix4x4 Apply(Matrix4x4 local)
    {
        if (!MirrorX && !MirrorZ)
            return local;

        // M' = F * M * F with diagonal F.
        var f = new[] { MirrorX ? -1f : 1f, 1f, MirrorZ ? -1f : 1f, 1f };
        var result = local;

        result.M11 *= f[0] * f[0]; result.M12 *= f[0] * f[1]; result.M13 *= f[0] * f[2]; result.M14 *= f[0] * f[3];
        result.M21 *= f[1] * f[0]; result.M22 *= f[1] * f[1]; result.M23 *= f[1] * f[2]; result.M24 *= f[1] * f[3];
        result.M31 *= f[2] * f[0]; result.M32 *= f[2] * f[1]; result.M33 *= f[2] * f[2]; result.M34 *= f[2] * f[3];
        result.M41 *= f[3] * f[0]; result.M42 *= f[3] * f[1]; result.M43 *= f[3] * f[2]; result.M44 *= f[3] * f[3];

        return result;
    }

    private void ApplyToVertices(Vertex3D[] vertices)
    {
        var sx = MirrorX ? -1f : 1f;
        var sz = MirrorZ ? -1f : 1f;

        // One mirror flips handedness, two do not.
        var handedness = sx * sz;

        for (var i = 0; i < vertices.Length; i++)
        {
            ref var v = ref vertices[i];
            v.Position = new Vector3(v.Position.X * sx, v.Position.Y, v.Position.Z * sz);
            v.Normal = new Vector3(v.Normal.X * sx, v.Normal.Y, v.Normal.Z * sz);
            v.Tangent = new Vector4(v.Tangent.X * sx, v.Tangent.Y, v.Tangent.Z * sz, v.Tangent.W * handedness);

            if (FlipU)
                v.Uv = v.Uv with { X = 1f - v.Uv.X };
            if (FlipV)
                v.Uv = v.Uv with { Y = 1f - v.Uv.Y };
        }
    }
}
