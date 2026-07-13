namespace NoireLib.Draw3D.Enums;

/// <summary>
/// The rung of the self-disable ladder a fault landed on. Each rung disables the narrowest responsible feature and keeps everything below it alive.
/// </summary>
public enum Draw3DFaultKind
{
    /// <summary>A shader pipeline failed to compile or bind; that pipeline renders nothing.</summary>
    Pipeline = 0,

    /// <summary>An <see cref="Scene.ISceneFeature"/> threw and was detached.</summary>
    Feature = 1,

    /// <summary>Game depth acquisition/validation failed; rendering continues in depth-off mode.</summary>
    Depth = 2,

    /// <summary>The scene pass threw; the layer was skipped this frame.</summary>
    Pass = 3,

    /// <summary>Repeated pass failures; the renderer tore down its device objects and disabled itself. Requires an explicit re-enable.</summary>
    Renderer = 4,
}
