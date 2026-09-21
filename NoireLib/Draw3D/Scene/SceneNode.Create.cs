using NoireLib.Helpers;
using System.Numerics;

namespace NoireLib.Draw3D.Scene;

public sealed partial class SceneNode
{
    /// <summary>Whether a renderer is attached.</summary>
    public bool HasRenderer => Renderer != null;

    /// <summary>Proxy for <see cref="MeshRenderer.Tint"/>, reading opaque white and ignoring writes while no renderer is attached.</summary>
    public Vector4 Tint
    {
        get => Renderer?.Tint ?? new Vector4(1f, 1f, 1f, 1f);
        set
        {
            var renderer = Renderer;
            if (renderer == null)
            {
                NoireLogger.LogWarning($"Draw3D: SceneNode '{Name ?? "(unnamed)"}'.Tint set with no renderer attached - ignored. Attach a mesh first (SetMesh / Spawn).", "Draw3D");
                return;
            }

            renderer.Tint = value;
        }
    }

    /// <summary>Sets the local position (relative to the parent). Fluent.</summary>
    /// <param name="position">The new local position.</param>
    public SceneNode At(Vector3 position)
    {
        LocalPosition = position;
        return this;
    }

    /// <summary>Sets the local position (relative to the parent). Fluent alias of <see cref="At"/>.</summary>
    /// <param name="position">The new local position.</param>
    public SceneNode MoveTo(Vector3 position) => At(position);

    /// <summary>Applies a rotation about the local X axis. Fluent.</summary>
    /// <param name="radians">Angle in radians.</param>
    public SceneNode RotateX(float radians) => Rotate(Vector3.UnitX, radians);

    /// <summary>Applies a rotation about the local Y axis. Fluent.</summary>
    /// <param name="radians">Angle in radians.</param>
    public SceneNode RotateY(float radians) => Rotate(Vector3.UnitY, radians);

    /// <summary>Applies a rotation about the local Z axis. Fluent.</summary>
    /// <param name="radians">Angle in radians.</param>
    public SceneNode RotateZ(float radians) => Rotate(Vector3.UnitZ, radians);

    /// <summary>Applies a rotation about an arbitrary axis. Fluent.</summary>
    /// <param name="axis">Rotation axis (normalized internally).</param>
    /// <param name="radians">Angle in radians.</param>
    public SceneNode Rotate(Vector3 axis, float radians)
    {
        var len = axis.Length();
        if (len < 1e-9f || radians == 0f)
            return this;

        return Rotate(Quaternion.CreateFromAxisAngle(axis / len, radians));
    }

    /// <summary>Composes an additional rotation onto the node's current local rotation. Fluent.</summary>
    /// <param name="rotation">The rotation to apply.</param>
    public SceneNode Rotate(Quaternion rotation)
    {
        lock (Scene3D.GraphLock)
        {
            ThrowIfDestroyed();
            localRotation = Quaternion.Normalize(rotation * localRotation);
            MarkDirty();
        }

        return this;
    }

    /// <summary>Sets a uniform local scale. Fluent.</summary>
    /// <param name="uniform">The scale to apply on every axis.</param>
    public SceneNode Scale(float uniform) => Scale(new Vector3(uniform));

    /// <summary>Sets a per-axis local scale. Fluent.</summary>
    /// <param name="scale">The scale per axis.</param>
    public SceneNode Scale(Vector3 scale)
    {
        LocalScale = scale;
        return this;
    }

    /// <summary>Orients the node so its local +Z points at a world-space target. Fluent.</summary>
    /// <param name="target">The world-space point to face.</param>
    /// <param name="up">Optional world up hint (default +Y).</param>
    public SceneNode LookAt(Vector3 target, Vector3? up = null)
    {
        lock (Scene3D.GraphLock)
        {
            ThrowIfDestroyed();

            var worldPos = ResolveWorld().Translation;
            var forward = target - worldPos;
            if (forward.LengthSquared() < 1e-12f)
                return this;

            var worldRot = TransformHelper.LookRotation(forward, up ?? Vector3.UnitY);
            if (parent != null)
            {
                var parentWorld = parent.ResolveWorld();
                if (Matrix4x4.Decompose(parentWorld, out _, out var parentRot, out _))
                    worldRot = Quaternion.Normalize(worldRot * Quaternion.Inverse(parentRot));
            }

            localRotation = worldRot;
            MarkDirty();
        }

        return this;
    }

}
