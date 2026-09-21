namespace NoireLib.Draw3D.Scene;

/// <summary>A per-frame scene behavior registered via <see cref="Scene3D.AddFeature"/>. A throwing feature is logged once and detached.</summary>
public interface ISceneFeature
{
    /// <summary>
    /// Called once per frame on the render thread before culling. Changes made here render this frame.<br/>
    /// It can run inside a game D3D call. Touch only the scene graph, <see cref="NoireDraw3D.Im"/> and your own state.
    /// </summary>
    /// <param name="scene">The scene the feature is registered on.</param>
    /// <param name="frame">The immutable frame snapshot.</param>
    void OnPrepareFrame(Scene3D scene, in FrameContext frame);
}
