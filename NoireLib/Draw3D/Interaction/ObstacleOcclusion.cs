namespace NoireLib.Draw3D.Interaction;

/// <summary>
/// How anything the game draws in front of a 3D object affects picking it (see
/// <see cref="NoireInteract.ObstacleOcclusionMode"/>). An obstacle is any visible surface, not only level geometry:
/// walls, terrain and furnishings, but equally characters, mounts and NPCs. The occluder depth under the cursor is read
/// from the game depth buffer, which holds every rendered surface; the collision raycast it falls back to on frames
/// where depth is unreadable sees level geometry only.
/// </summary>
public enum ObstacleOcclusion
{
    /// <summary>Ignore obstacles: a 3D object is always hoverable/clickable, even straight through one (x-ray picking).</summary>
    Off,

    /// <summary>An obstacle in front of an object always blocks picking it. No override.</summary>
    Always,

    /// <summary>Obstacles block by default, but objects behind them become clickable while <see cref="NoireInteract.ClickThroughHeld"/> is held (default: Alt).</summary>
    HoldToClickThrough,
}
