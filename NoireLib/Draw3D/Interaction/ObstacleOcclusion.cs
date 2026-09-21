namespace NoireLib.Draw3D.Interaction;

/// <summary>How what the game draws in front of an object affects picking it. Read from the game depth, or the collision raycast when depth is unreadable.</summary>
public enum ObstacleOcclusion
{
    /// <summary>Ignore obstacles: a 3D object is always pickable, even through one.</summary>
    Off,

    /// <summary>An obstacle in front of an object always blocks picking it. No override.</summary>
    Always,

    /// <summary>Obstacles block unless <see cref="NoireInteract.ClickThroughHeld"/> is held (default Alt).</summary>
    HoldToClickThrough,
}
