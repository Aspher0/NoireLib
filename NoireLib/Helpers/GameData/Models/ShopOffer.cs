using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>One purchasable line in a shop.</summary>
/// <param name="ShopId">The shop this line belongs to.</param>
/// <param name="Kind">Which kind of shop it came from.</param>
/// <param name="ItemId">The Item row being sold.</param>
/// <param name="Quantity">How many per purchase.</param>
/// <param name="IsHq">Whether it is handed over high quality.</param>
/// <param name="Costs">What one purchase costs.</param>
/// <param name="RequiredQuests">Quests that must be complete for the line to appear.</param>
/// <param name="RequiredAchievement">Achievement that must be earned, or zero.</param>
/// <param name="Patch">The patch the line was added in.</param>
public sealed record ShopOffer(
    uint ShopId,
    EventHandlerContent Kind,
    uint ItemId,
    uint Quantity,
    bool IsHq,
    IReadOnlyList<ShopCost> Costs,
    IReadOnlyList<uint> RequiredQuests,
    uint RequiredAchievement,
    ushort Patch)
{
    /// <summary>The gil part of the price, or zero.</summary>
    public uint GilCost
    {
        get
        {
            foreach (var cost in Costs)
            {
                if (cost.IsGil)
                    return cost.Amount;
            }

            return 0;
        }
    }

    /// <summary>Whether gil is the whole price.</summary>
    public bool IsGilPurchase => Costs.Count == 1 && Costs[0].IsGil;
}
