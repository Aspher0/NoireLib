namespace NoireLib.Draw3D.Enums;

/// <summary>What a depth-tested material does on frames where the game's depth buffer cannot be read.</summary>
public enum DepthUnavailableBehavior
{
    /// <summary>Renders without world occlusion until depth returns.</summary>
    Ignore = 0,

    /// <summary>Renders nothing until depth returns.</summary>
    Hide = 1,
}
