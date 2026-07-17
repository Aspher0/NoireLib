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
    /// <br/>
    /// This is stricter than "not the framework thread": on the default under-UI path the call happens <b>mid-frame,
    /// from inside one of the game's own D3D calls</b>. Touch only the scene graph, <see cref="NoireDraw3D.Im"/>, and
    /// your own state. Do not read or write game state, print to chat, or call any Dalamud game service from here;
    /// hand that work to the framework thread instead.
    /// </summary>
    /// <param name="scene">The scene the feature is registered on.</param>
    /// <param name="frame">The immutable frame snapshot.</param>
    void OnPrepareFrame(Scene3D scene, in FrameContext frame);
}
