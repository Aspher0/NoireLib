namespace NoireLib.Draw3D.Interaction;

/// <summary>
/// How game-world geometry in front of a 3D object affects picking it (see <see cref="NoireInteract.WallOcclusionMode"/>).
/// The distance to the nearest game surface under the cursor comes from the game's own screen raycast.
/// </summary>
public enum WallOcclusion
{
    /// <summary>Ignore obstacles: a 3D object is always hoverable/clickable, even straight through a wall (x-ray picking).</summary>
    Off,

    /// <summary>A wall / terrain in front of an object always blocks picking it. No override.</summary>
    Always,

    /// <summary>Obstacles block by default, but objects behind them become clickable while <see cref="NoireInteract.ClickThroughHeld"/> is held (default: Alt).</summary>
    HoldToClickThrough,
}
