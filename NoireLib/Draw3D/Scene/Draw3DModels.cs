using NoireLib.Draw3D.Assets;
using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Draw3D.Scene;

/// <summary>Imported-model shortcuts that load, attach and hand a model to the scene. <see cref="Scene3D.Dispose"/> frees it.</summary>
public static class Draw3DModels
{
    /// <summary>Attaches an already-loaded model to the scene and hands the scene ownership of it.</summary>
    /// <param name="scene">The target scene.</param>
    /// <param name="model">The imported model to attach and own.</param>
    /// <param name="position">Local position for the model root (scene root space).</param>
    /// <param name="name">Optional name override for the model root.</param>
    /// <returns>The model's root node.</returns>
    /// <exception cref="ObjectDisposedException">The scene is disposed. The model is freed only if disposal raced this call.</exception>
    public static SceneNode AddModel(this Scene3D scene, Model3D model, Vector3 position = default, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(model);
        ObjectDisposedException.ThrowIf(scene.IsDisposed, scene);

        model.AttachTo(scene);
        scene.Own(model);

        // A scene disposed concurrently has just freed the model.
        ObjectDisposedException.ThrowIf(scene.IsDisposed, scene);

        model.Root.LocalPosition = position;
        if (name != null)
            model.Root.Name = name;
        return model.Root;
    }

    /// <summary>Loads a glTF or glb model from disk synchronously, attaches it to the scene and hands the scene ownership.</summary>
    /// <param name="scene">The target scene.</param>
    /// <param name="path">Absolute path to a .gltf or .glb file.</param>
    /// <param name="position">Local position for the model root.</param>
    /// <param name="name">Optional name override for the model root.</param>
    /// <param name="keepCpuData">Retain CPU-side geometry on the imported meshes for exact picking.</param>
    /// <param name="importVertexColors">Whether to apply the glTF <c>COLOR_0</c> channel as an albedo tint (FFXIV-derived exports store shader data there).</param>
    /// <param name="generateLods">Whether to build a level-of-detail chain for large primitives.</param>
    /// <returns>The imported model.</returns>
    /// <exception cref="ObjectDisposedException">The scene is disposed. The imported model is freed before the throw.</exception>
    public static Model3D LoadModel(this Scene3D scene, string path, Vector3 position = default, string? name = null, bool keepCpuData = false, bool importVertexColors = false, bool generateLods = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var model = GltfLoader.LoadAsync(path, keepCpuData, importVertexColors, generateLods).GetAwaiter().GetResult();
        return AttachOrFree(scene, model, position, name);
    }

    /// <summary>Loads a glTF or glb model from disk on the thread pool, then attaches it to the scene and hands the scene ownership.</summary>
    /// <param name="scene">The target scene.</param>
    /// <param name="path">Absolute path to a .gltf or .glb file.</param>
    /// <param name="position">Local position for the model root.</param>
    /// <param name="name">Optional name override for the model root.</param>
    /// <param name="keepCpuData">Retain CPU-side geometry on the imported meshes for exact picking.</param>
    /// <param name="importVertexColors">Whether to apply the glTF <c>COLOR_0</c> channel as an albedo tint.</param>
    /// <param name="generateLods">Whether to build a level-of-detail chain for large primitives.</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <returns>The imported model, attached.</returns>
    /// <exception cref="ObjectDisposedException">The scene was disposed before or during the load. The imported model is freed before the throw.</exception>
    public static async Task<Model3D> LoadModelAsync(this Scene3D scene, string path, Vector3 position = default, string? name = null, bool keepCpuData = false, bool importVertexColors = false, bool generateLods = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var model = await GltfLoader.LoadAsync(path, keepCpuData, importVertexColors, generateLods, ct).ConfigureAwait(false);
        return AttachOrFree(scene, model, position, name);
    }

    // The load owns the model until the scene takes it.
    private static Model3D AttachOrFree(Scene3D scene, Model3D model, Vector3 position, string? name)
    {
        try
        {
            scene.AddModel(model, position, name);
            return model;
        }
        catch
        {
            model.Dispose(); // idempotent
            throw;
        }
    }
}
