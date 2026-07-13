namespace NoireLib.Draw3D.Enums;

/// <summary>
/// What a depth-tested material does on frames where the game's depth buffer cannot be read (depth-off mode).
/// </summary>
public enum DepthUnavailableBehavior
{
    /// <summary>Render without world occlusion (x-ray) until depth returns. Good for screen-space-ish markers.</summary>
    Ignore = 0,

    /// <summary>Do not render until depth returns. Good for world-anchored content — a waymark that suddenly x-rays through a mountain is worse than one that blinks off.</summary>
    Hide = 1,
}
