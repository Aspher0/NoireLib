using System;
using System.Numerics;

namespace NoireLib.Helpers;

/// <summary>
/// Rotation and matrix helpers for placing objects in a scene.<br/>
/// Every method is a pure function of its arguments, so it runs on any thread and unit-tests without a game.
/// </summary>
public static class TransformHelper
{
    /// <summary>
    /// Builds the left-handed rotation whose +Z axis aims along a direction.
    /// </summary>
    /// <param name="forward">The direction to face; need not be normalized.</param>
    /// <param name="up">Up hint, used to resolve the roll about <paramref name="forward"/>.</param>
    /// <returns>The rotation, with a substitute up chosen when the hint is parallel to <paramref name="forward"/>.</returns>
    public static Quaternion LookRotation(Vector3 forward, Vector3 up)
    {
        var f = Vector3.Normalize(forward);
        var r = Vector3.Cross(up, f);
        if (r.LengthSquared() < 1e-9f)
            r = Vector3.Cross(MathF.Abs(f.Y) < 0.99f ? Vector3.UnitY : Vector3.UnitX, f);
        r = Vector3.Normalize(r);
        var u = Vector3.Cross(f, r);

        var m = new Matrix4x4(
            r.X, r.Y, r.Z, 0f,
            u.X, u.Y, u.Z, 0f,
            f.X, f.Y, f.Z, 0f,
            0f, 0f, 0f, 1f);
        return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(m));
    }

    /// <summary>
    /// The shortest rotation turning one direction into another, for aiming a mesh built along a fixed axis.
    /// </summary>
    /// <param name="from">Start direction; need not be normalized.</param>
    /// <param name="to">Target direction; need not be normalized.</param>
    /// <returns>The rotation, or a half turn about an arbitrary perpendicular when the two directions are opposed.</returns>
    public static Quaternion FromToRotation(Vector3 from, Vector3 to)
    {
        from = Geometry3DHelper.SafeNormalize(from, Vector3.UnitY);
        to = Geometry3DHelper.SafeNormalize(to, Vector3.UnitY);

        var dot = Vector3.Dot(from, to);
        if (dot > 0.99999f)
            return Quaternion.Identity;

        if (dot < -0.99999f)
        {
            // Opposed directions leave the axis undetermined, so any perpendicular does. The reference is picked to
            // stay well away from `from`, which keeps the cross product conditioned.
            var reference = MathF.Abs(from.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitZ;
            return Quaternion.CreateFromAxisAngle(Vector3.Normalize(Vector3.Cross(from, reference)), MathF.PI);
        }

        var axis = Vector3.Normalize(Vector3.Cross(from, to));
        return Quaternion.CreateFromAxisAngle(axis, MathF.Acos(Math.Clamp(dot, -1f, 1f)));
    }

    /// <summary>
    /// Decomposes a transform, substituting unit scale and an identity rotation when the matrix cannot be decomposed.
    /// <see cref="Matrix4x4.Decompose"/> refuses a matrix with no orthonormal basis to recover and does not specify
    /// what it leaves in its outputs, so a caller reading them regardless collapses the object it was editing.
    /// </summary>
    /// <param name="world">The matrix to decompose.</param>
    /// <param name="scale">Scale, or one on every axis when the decomposition fails.</param>
    /// <param name="rotation">Rotation, or identity when the decomposition fails.</param>
    /// <param name="translation">Translation, always the matrix's own.</param>
    public static void DecomposeSafe(in Matrix4x4 world, out Vector3 scale, out Quaternion rotation, out Vector3 translation)
    {
        if (Matrix4x4.Decompose(world, out scale, out rotation, out translation))
            return;

        scale = Vector3.One;
        rotation = Quaternion.Identity;
        translation = world.Translation;
    }
}
