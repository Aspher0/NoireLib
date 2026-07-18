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
    /// <summary>
    /// Attaches an already-loaded model to the scene and hands the scene ownership of it. Returns the model's root node
    /// (chainable). Throws on a disposed scene rather than adopting into it, matching <see cref="Scene3D.CreateNode"/>:
    /// <see cref="Scene3D.Own{T}"/> frees anything handed to a dead scene, so adopting there would hand back a model
    /// whose GPU buffers are already gone and which silently draws nothing.
    /// </summary>
    /// <param name="scene">The target scene.</param>
    /// <param name="model">The imported model to attach and own.</param>
    /// <param name="position">Local position for the model root (scene root space).</param>
    /// <param name="name">Optional name override for the model root.</param>
    /// <exception cref="ObjectDisposedException">
    /// The scene is disposed. The caller's <paramref name="model"/> is left untouched, unless the scene was disposed
    /// concurrently with this call, in which case <see cref="Scene3D.Own{T}"/> has already freed it.
    /// </exception>
    public static SceneNode AddModel(this Scene3D scene, Model3D model, Vector3 position = default, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(model);
        ObjectDisposedException.ThrowIf(scene.IsDisposed, scene);

        model.AttachTo(scene);
        scene.Own(model);

        // A scene disposed concurrently with this call means Own has just freed the model, which is its contract for a
        // dead scene. Report that instead of positioning and returning a root whose GPU buffers no longer exist.
        ObjectDisposedException.ThrowIf(scene.IsDisposed, scene);

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
    /// <param name="generateLods">Build a level-of-detail chain for large primitives (off by default; tune via <see cref="NoireDraw3D.Performance"/>).</param>
    /// <exception cref="ObjectDisposedException">The scene is disposed. The imported model is freed before the throw.</exception>
    public static Model3D LoadModel(this Scene3D scene, string path, Vector3 position = default, string? name = null, bool keepCpuData = false, bool importVertexColors = false, bool generateLods = false)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var model = GltfLoader.LoadAsync(path, keepCpuData, importVertexColors, generateLods).GetAwaiter().GetResult();
        return AttachOrFree(scene, model, position, name);
    }

    /// <summary>
    /// Loads a glTF/glb model from disk on the thread pool, then attaches it to the scene and hands the scene ownership
    /// (scene-graph mutation is thread-safe). Returns the imported <see cref="Model3D"/>, ready and attached.
    /// <br/>
    /// A large file takes long enough to parse that the scene can be disposed while the load is still running. That
    /// case throws <see cref="ObjectDisposedException"/> and frees the imported model first, so a teardown during a
    /// load costs neither a leak nor a silently dead model.
    /// </summary>
    /// <param name="scene">The target scene.</param>
    /// <param name="path">Absolute path to a .gltf or .glb file.</param>
    /// <param name="position">Local position for the model root.</param>
    /// <param name="name">Optional name override for the model root.</param>
    /// <param name="keepCpuData">Retain CPU-side geometry on the imported meshes for exact picking.</param>
    /// <param name="importVertexColors">Apply the glTF <c>COLOR_0</c> channel as an albedo tint. Off by default (see <see cref="GltfLoader"/>).</param>
    /// <param name="generateLods">Build a level-of-detail chain for large primitives (off by default; tune via <see cref="NoireDraw3D.Performance"/>).</param>
    /// <param name="ct">Optional cancellation token.</param>
    /// <exception cref="ObjectDisposedException">The scene was disposed before or during the load. The imported model is freed before the throw.</exception>
    public static async Task<Model3D> LoadModelAsync(this Scene3D scene, string path, Vector3 position = default, string? name = null, bool keepCpuData = false, bool importVertexColors = false, bool generateLods = false, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scene);
        var model = await GltfLoader.LoadAsync(path, keepCpuData, importVertexColors, generateLods, ct).ConfigureAwait(false);
        return AttachOrFree(scene, model, position, name);
    }

    /// <summary>
    /// Hands a just-imported model to the scene, freeing it if the scene will not take it. The load owns the model
    /// until the scene does, and a scene that died while the file was being parsed leaves nobody else to release its
    /// GPU resources, so the failure path frees them here rather than leaking them.
    /// </summary>
    private static Model3D AttachOrFree(Scene3D scene, Model3D model, Vector3 position, string? name)
    {
        try
        {
            scene.AddModel(model, position, name);
            return model;
        }
        catch
        {
            model.Dispose(); // idempotent: harmless when the scene's own Own() already freed it
            throw;
        }
    }
}
