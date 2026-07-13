using NoireLib.Draw3D.Geometry;
using NoireLib.Draw3D.Scene;
using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Assets;

/// <summary>
/// An imported model: a detached node subtree plus the meshes and textures the import created.<br/>
/// <b>Ownership:</b> the model owns its imported GPU assets and releases them on dispose; the scene it
/// gets attached to never does. Attach with <see cref="AttachTo(Scene3D)"/> or <see cref="AttachTo(SceneNode)"/> —
/// an O(1) reparent, callable from any thread.
/// </summary>
public sealed class Model3D : IDisposable
{
    private readonly List<Mesh> meshes;
    private readonly List<GpuTexture> textures;
    private bool disposed;

    /// <summary>The root node of the imported hierarchy (detached until attached to a scene).</summary>
    public SceneNode Root { get; }

    /// <summary>The meshes this import created (owned by the model).</summary>
    public IReadOnlyList<Mesh> Meshes => meshes;

    /// <summary>The textures this import created (owned by the model).</summary>
    public IReadOnlyList<GpuTexture> Textures => textures;

    internal Model3D(SceneNode root, List<Mesh> meshes, List<GpuTexture> textures)
    {
        Root = root;
        this.meshes = meshes;
        this.textures = textures;
    }

    /// <summary>Attaches the model's root to a scene.</summary>
    /// <param name="scene">The target scene.</param>
    public void AttachTo(Scene3D scene)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ThrowIfDisposed();
        scene.AdoptRoot(Root);
    }

    /// <summary>Attaches the model's root under an existing node.</summary>
    /// <param name="parent">The new parent node.</param>
    public void AttachTo(SceneNode parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ThrowIfDisposed();
        Root.SetParent(parent);
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
            throw new ObjectDisposedException(nameof(Model3D));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        Root.Destroy();
        foreach (var mesh in meshes)
            mesh.Dispose();
        meshes.Clear();
        foreach (var texture in textures)
            texture.Dispose();
        textures.Clear();
    }
}
