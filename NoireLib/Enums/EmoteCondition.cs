using System;

namespace NoireLib.Enums;

/// <summary>
/// The character states an emote can be performed in.
/// </summary>
[Flags]
public enum EmoteCondition
{
    None = 0,
    Standing = 1,
    Swimming = 2,
    Diving = 4,
    SittingOnGround = 8,
    SittingInChair = 16,
    Mounted = 32,
    HoldingUmbrella = 64,
    HoldingTorch = 128,
    WearingFashionAccessory = 256,
    Fishing = 512,
}
