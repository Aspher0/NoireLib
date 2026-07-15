using NoireLib.Draw3D.Assets;
using System;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Draw3D.Scene;

/// <summary>
/// Imported-model shortcuts that fold "load → attach → track for disposal" into one call and hand the model to the
/// scene's ownership scope, so <see cref="Scene3D.Dispose"/> frees its meshes and textures. Thin sugar over
/// <see cref="GltfLoader"/> + <see cref="Model3D.AttachTo(Scene3D)"/> + <see cref="Scene3D.Own{T}"/>.
/// </summary>
public static class Draw3DModels
{
    /// <summary>Attaches an already-loaded model to the scene and hands the scene ownership of it. Returns the model's root node (chainable).</summary>
    /// <param name="scene">The target scene.</param>
    /// <param name="model">The imported model to attach and own.</param>
    /// <param name="position">Local position for the model root (scene root space).</param>
    /// <param name="name">Optional name override for the model root.</param>
    public static SceneNode AddModel(this Scene3D scene, Model3D model, Vector3 position = default, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(model);

        model.AttachTo(scene);
        scene.Own(model);
        model.Root.LocalPosition = position;
        if (name != null)
            model.Root.Name = name;
        return model.Root;
    }

    /// <summary>
    /// Loads a glTF/glb model from disk (blocking), attaches it to the scene and hands the scene ownership. Returns the
    /// imported <see cref="Model3D"/> (its meshes/textures are freed by <see cref="Scene3D.Dispose"/>). Prefer
    /// <see cref="LoadModelAsync"/> off the framework thread for large files.
    /// </summary>
    /// <param name="scene">The target scene.</param>
    /// <param name="path">Absolute path to a .gltf or .glb file.</param>
    /// <param name="position">Local position for the model root.</param>
    /// <param name="name">Optional name override for the model root.</param>
    /// <param name="keepCpuData">Retain CPU-side geometry on the imported meshes for exact picking.</param>
    /// <param name="importVertexColors">Apply the glTF <c>COLOR_0</c> channel as an albedo tint. Off by default - FFXIV-derived exports store shader data there, not colors (see <see cref="GltfLoader"/>).</param>
    public static Model3D LoadModel(this Scene3D scene, string path, Vector3 position = default, string? name = null, bool keepCpuData = false, bool importVertexColors = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var model = GltfLoader.LoadAsync(path, keepCpuData, importVertexColors).GetAwaiter().GetResult();
        scene.AddModel(model, position, name);
        return model;
    }

    /// <summary>
    /// Loads a glTF/glb model from disk on the thread pool, then attaches it to the scene and hands the scene ownership
    /// (scene-graph mutation is thread-safe). Returns the imported <see cref="Model3D"/>, ready and attached.
    /// </summary>
    /// <param name="scene">The target scene.</param>
    /// <param name="path">Absolute path to a .gltf or .glb file.</param>
    /// <param name="position">Local position for the model root.</param>
    /// <param name="name">Optional name override for the model root.</param>
    /// <param name="keepCpuData">Retain CPU-side geometry on the imported meshes for exact picking.</param>
    /// <param name="importVertexColors">Apply the glTF <c>COLOR_0</c> channel as an albedo tint. Off by default (see <see cref="GltfLoader"/>).</param>
    /// <param name="ct">Optional cancellation token.</param>
    public static async Task<Model3D> LoadModelAsync(this Scene3D scene, string path, Vector3 position = default, string? name = null, bool keepCpuData = false, bool importVertexColors = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var model = await GltfLoader.LoadAsync(path, keepCpuData, importVertexColors, ct).ConfigureAwait(false);
        scene.AddModel(model, position, name);
        return model;
    }
}
