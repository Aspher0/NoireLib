namespace NoireLib.Draw3D.Enums;

/// <summary>The rung of the self-disable ladder a fault landed on, each rung disabling only the narrowest responsible feature.</summary>
public enum Draw3DFaultKind
{
    /// <summary>A shader pipeline failed to compile or bind. That pipeline renders nothing.</summary>
    Pipeline = 0,

    /// <summary>An <see cref="Scene.ISceneFeature"/> threw and was detached.</summary>
    Feature = 1,

    /// <summary>Game depth acquisition or validation failed. Rendering continues without world depth.</summary>
    Depth = 2,

    /// <summary>The scene pass threw. The layer was skipped this frame.</summary>
    Pass = 3,

    /// <summary>Repeated pass failures disabled the renderer until it is explicitly re-enabled.</summary>
    Renderer = 4,
}
