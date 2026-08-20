using System;

namespace NoireLib.Enums;

/// <summary>
/// The character states an emote can be performed in.
/// </summary>
[Flags]
public enum EmoteCondition
{
    /// <summary>
    /// No state; the emote cannot be performed.
    /// </summary>
    None = 0,

    /// <summary>
    /// Standing (default state).
    /// </summary>
    Standing = 1,

    /// <summary>
    /// Swimming at the surface.
    /// </summary>
    Swimming = 2,

    /// <summary>
    /// Diving underwater.
    /// </summary>
    Diving = 4,

    /// <summary>
    /// Sitting on the ground (/sit on the ground).
    /// </summary>
    SittingOnGround = 8,

    /// <summary>
    /// Sitting in a chair (/sit on furniture).
    /// </summary>
    SittingInChair = 16,

    /// <summary>
    /// Riding a mount.
    /// </summary>
    Mounted = 32,

    /// <summary>
    /// Holding an umbrella (parasol).
    /// </summary>
    HoldingUmbrella = 64,

    /// <summary>
    /// Holding a torch.
    /// </summary>
    HoldingTorch = 128,

    /// <summary>
    /// Wearing a fashion accessory.
    /// </summary>
    WearingFashionAccessory = 256,

    /// <summary>
    /// Fishing.
    /// </summary>
    Fishing = 512,

    /// <summary>
    /// Every character state combined.
    /// </summary>
    All = Standing | Swimming | Diving | SittingOnGround | SittingInChair | Mounted
        | HoldingUmbrella | HoldingTorch | WearingFashionAccessory | Fishing,
}
