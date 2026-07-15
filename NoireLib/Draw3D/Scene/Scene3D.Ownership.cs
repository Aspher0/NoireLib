using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Ownership scope of a scene: a scene is <see cref="IDisposable"/>, and <see cref="Dispose"/> frees everything it
/// owns - every node (which frees its owned meshes, see <see cref="SceneNode.SetMesh(Geometry.MeshData, Materials.Material, bool)"/>),
/// everything handed to <see cref="Own{T}"/> (shared meshes, textures, imported models, editors), and the scene's
/// own registration with the renderer. No parallel bookkeeping lists: build into a scene, then <c>scene.Dispose()</c>.
/// </summary>
public sealed partial class Scene3D : IDisposable
{
    private readonly List<IDisposable> ownedDisposables = new();
    private bool disposed;

    /// <summary>The always-there <see cref="NoireDraw3D.MainScene"/> is owned by the library (disposed at shutdown, never by the consumer). Extra scenes are yours.</summary>
    internal bool IsHubOwned { get; set; }

    /// <summary>True once <see cref="Dispose"/> has run. A disposed scene rejects new node creation.</summary>
    public bool IsDisposed => disposed;

    /// <summary>
    /// Hands the scene responsibility for a disposable - a mesh shared across several of the scene's nodes, a texture,
    /// an imported <see cref="Assets.Model3D"/>, an editor, any custom <see cref="IDisposable"/>. Returns it unchanged
    /// so it can be captured inline. <see cref="Dispose"/> frees everything owned this way (idempotent - freeing a
    /// disposable twice is safe).
    /// </summary>
    /// <typeparam name="T">The disposable type (returned unchanged).</typeparam>
    /// <param name="disposable">The disposable to hand to the scene.</param>
    public T Own<T>(T disposable) where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(disposable);
        lock (GraphLock)
        {
            if (disposed)
            {
                // The scene is already gone: don't silently leak the straggler - free it now.
                disposable.Dispose();
                return disposable;
            }

            if (!ownedDisposables.Contains(disposable))
                ownedDisposables.Add(disposable);
        }

        return disposable;
    }

    /// <summary>Stops the scene owning a disposable (so a later <see cref="Dispose"/> won't free it). Returns whether it was owned.</summary>
    /// <param name="disposable">The disposable to release from the scene's ownership.</param>
    public bool Disown(IDisposable disposable)
    {
        if (disposable == null)
            return false;

        lock (GraphLock)
            return ownedDisposables.Remove(disposable);
    }

    /// <summary>
    /// Frees everything the scene owns: every node (and its owned meshes), every <see cref="Own{T}"/>-registered
    /// disposable, and the scene's registration with the renderer (so it stops drawing). Idempotent. The library's
    /// <see cref="NoireDraw3D.MainScene"/> ignores this - it lives for the library's lifetime.
    /// </summary>
    public void Dispose()
    {
        if (IsHubOwned)
        {
            NoireLogger.LogWarning("Draw3D: MainScene is owned by the library and disposed at shutdown; Dispose() ignored. Use scene.Clear() to empty it.", "Draw3D");
            return;
        }

        if (!DisposeContentsInternal())
            return;

        NoireDraw3D.RemoveScene(this);
    }

    /// <summary>
    /// Frees the scene's contents (owned disposables + all nodes) without touching the hub registration. Shared by
    /// <see cref="Dispose"/> and the hub's own shutdown. Returns false when it was already disposed (idempotent).
    /// </summary>
    internal bool DisposeContentsInternal()
    {
        IDisposable[] toDispose;
        lock (GraphLock)
        {
            if (disposed)
                return false;

            disposed = true;
            toDispose = ownedDisposables.ToArray();
            ownedDisposables.Clear();
        }

        // Owned disposables first (an imported model detaches its own root), then any remaining nodes + owned meshes.
        foreach (var d in toDispose)
        {
            try
            {
                d.Dispose();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError<Scene3D>(ex, $"Scene '{Name}': an owned disposable threw during Dispose; continuing.", "Draw3D");
            }
        }

        Selection.Clear();
        Clear();
        return true;
    }
}
