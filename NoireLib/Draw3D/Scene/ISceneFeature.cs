namespace NoireLib.Draw3D.Scene;

/// <summary>
/// A per-frame scene behavior: billboarding, tether stretching, pulse animations, LOD swaps, label anchoring.<br/>
/// Registered via <see cref="Scene3D.AddFeature"/>. A feature that throws is detached (logged once) -
/// the rest of the scene keeps rendering.
/// </summary>
public interface ISceneFeature
{
    /// <summary>
    /// Called once per frame on the render thread after the frame snapshot, before culling.<br/>
    /// Mutate your nodes here - mutations (and <see cref="NoireDraw3D.Im"/> calls) made here render this same frame.
    /// </summary>
    /// <param name="scene">The scene the feature is registered on.</param>
    /// <param name="frame">The immutable frame snapshot.</param>
    void OnPrepareFrame(Scene3D scene, in FrameContext frame);
}
