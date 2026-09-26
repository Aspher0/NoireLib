using System;
using System.Collections.Generic;

namespace NoireLib.Draw3D.Scene;

public sealed partial class Scene3D : IDisposable
{
    private readonly List<IDisposable> ownedDisposables = new();
    private bool disposed;

    // Owned by the library, disposed at shutdown.
    internal bool IsHubOwned { get; set; }

    /// <summary>True once <see cref="Dispose"/> has run. A disposed scene rejects new node creation.</summary>
    public bool IsDisposed => disposed;

    /// <summary>Hands the scene a disposable to free on <see cref="Dispose"/>, freeing it at once if the scene is already disposed.</summary>
    /// <typeparam name="T">The disposable type.</typeparam>
    /// <param name="disposable">The disposable to hand to the scene.</param>
    /// <returns>The same disposable, for inline capture.</returns>
    public T Own<T>(T disposable) where T : IDisposable
    {
        ArgumentNullException.ThrowIfNull(disposable);
        lock (GraphLock)
        {
            if (disposed)
            {
                disposable.Dispose();
                return disposable;
            }

            if (!ownedDisposables.Contains(disposable))
                ownedDisposables.Add(disposable);
        }

        return disposable;
    }

    /// <summary>Stops the scene owning a disposable. A later <see cref="Dispose"/> does not free it.</summary>
    /// <param name="disposable">The disposable to release from the scene's ownership.</param>
    /// <returns>Whether it was owned.</returns>
    public bool Disown(IDisposable disposable)
    {
        if (disposable == null)
            return false;

        lock (GraphLock)
            return ownedDisposables.Remove(disposable);
    }

    /// <summary>Frees every node, owned mesh and <see cref="Own{T}"/> disposable, and unregisters the scene. Idempotent. <see cref="NoireDraw3D.MainScene"/> ignores it.</summary>
    public void Dispose()
    {
        if (IsHubOwned)
        {
            NoireLogger.LogWarning("Draw3D: MainScene is owned by the library and disposed at shutdown; Dispose() ignored. Use scene.Clear() to empty it.", "[Draw3D] ");
            return;
        }

        if (!DisposeContentsInternal())
            return;

        NoireDraw3D.RemoveScene(this);
    }

    // False when already disposed.
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

        // An imported model detaches its own root.
        foreach (var d in toDispose)
        {
            try
            {
                d.Dispose();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError<Scene3D>(ex, $"Scene '{Name}': an owned disposable threw during Dispose; continuing.", "[Draw3D] ");
            }
        }

        Selection.Clear();
        Clear();
        return true;
    }
}
