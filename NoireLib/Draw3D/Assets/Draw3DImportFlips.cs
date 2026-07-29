using NoireLib.Draw3D.Geometry;
using System.Numerics;

namespace NoireLib.Draw3D.Assets;

/// <summary>
/// Orientation overrides applied to every imported mesh, for a file authored in a convention the loaders do
/// not expect.<br/>
/// <b>Everything here is off by default and should stay off.</b> Both loaders take positions, normals and
/// transforms exactly as authored and reverse only the triangle winding, which is correct for the game's own
/// models and for a conforming glTF; these exist because exporters disagree (a tool that writes Z-up, or a
/// pipeline that has already converted handedness once), and such a file cannot be told apart from a correct
/// one by reading it.
/// </summary>
/// <remarks>
/// A single mirror is a reflection and turns a model into its mirror image; <see cref="MirrorX"/> and
/// <see cref="MirrorZ"/> together are a 180 degree turn about Y instead, changing only which way the model
/// faces.<br/>
/// Applied inside the loaders so both import paths behave identically, and reached through
/// <see cref="Draw3DDiagnostics.ImportFlips"/>; a change affects models imported afterwards, not what is
/// already on screen.
/// </remarks>
public sealed class Draw3DImportFlips
{
    /// <summary>Reflect through the YZ plane.</summary>
    public bool MirrorX { get; set; }

    /// <summary>Reflect through the XY plane.</summary>
    public bool MirrorZ { get; set; }

    /// <summary>
    /// Reverse the triangle index order again, swapping which side of every face is the front; undoes the
    /// loaders' own conversion to this renderer's clockwise-front convention, needed only for a file whose
    /// winding was already converted (any other file renders inside out with it on).
    /// </summary>
    public bool ReverseWinding { get; set; }

    /// <summary>Mirror the texture horizontally.</summary>
    public bool FlipU { get; set; }

    /// <summary>Mirror the texture vertically.</summary>
    public bool FlipV { get; set; }

    /// <summary>Whether anything at all is being changed, so the default path can skip the work entirely.</summary>
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

    /// <summary>
    /// Applies the selected transforms to a decoded mesh in place; does nothing when none is selected, so an
    /// import that wants no transform pays nothing for the option existing.
    /// </summary>
    /// <param name="vertices">The decoded vertices, modified in place.</param>
    /// <param name="indices">The decoded indices, modified in place.</param>
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

    /// <summary>
    /// The same transforms for a loader that has not yet narrowed its indices to 16 bits, kept beside the
    /// other overload so the two import paths cannot drift.
    /// </summary>
    /// <param name="vertices">The decoded vertices, modified in place.</param>
    /// <param name="indices">The decoded indices, modified in place.</param>
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

    /// <summary>
    /// The same mirrors applied to a node's local transform, as a change of basis rather than a multiply.<br/>
    /// <b>A hierarchical model needs both halves or neither:</b> mirroring only the vertices reflects each mesh
    /// in its own local space while the transforms that place those meshes stay put, mirroring the parts
    /// without changing their arrangement (a flat mesh list has no transforms, so it never shows the discrepancy).
    /// </summary>
    /// <param name="local">The node's local transform, already in this renderer's convention.</param>
    /// <returns>The transform with the selected mirrors applied.</returns>
    internal Matrix4x4 Apply(Matrix4x4 local)
    {
        if (!MirrorX && !MirrorZ)
            return local;

        // M' = F * M * F with F = diag(sx, 1, sz, 1), which for a diagonal F is elementwise: each cell is
        // scaled by the factor of its row times the factor of its column.
        var f = new[] { MirrorX ? -1f : 1f, 1f, MirrorZ ? -1f : 1f, 1f };
        var result = local;

        result.M11 *= f[0] * f[0]; result.M12 *= f[0] * f[1]; result.M13 *= f[0] * f[2]; result.M14 *= f[0] * f[3];
        result.M21 *= f[1] * f[0]; result.M22 *= f[1] * f[1]; result.M23 *= f[1] * f[2]; result.M24 *= f[1] * f[3];
        result.M31 *= f[2] * f[0]; result.M32 *= f[2] * f[1]; result.M33 *= f[2] * f[2]; result.M34 *= f[2] * f[3];
        result.M41 *= f[3] * f[0]; result.M42 *= f[3] * f[1]; result.M43 *= f[3] * f[2]; result.M44 *= f[3] * f[3];

        return result;
    }

    /// <summary>Positions, normals, tangents and texture coordinates - everything that does not depend on the index type.</summary>
    private void ApplyToVertices(Vertex3D[] vertices)
    {
        var sx = MirrorX ? -1f : 1f;
        var sz = MirrorZ ? -1f : 1f;

        // A single mirror is a reflection and flips the frame's handedness; two mirrors are a rotation and
        // do not. The tangent's w carries that handedness, so it flips exactly when the transform reflects.
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
