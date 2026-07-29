using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>One thing an offer costs; gil is represented as an item id.</summary>
/// <param name="ItemId">The Item row being spent.</param>
/// <param name="Amount">How many per purchase.</param>
/// <param name="CollectabilityRating">Required collectability, or zero.</param>
public readonly record struct ShopCost(uint ItemId, uint Amount, ushort CollectabilityRating = 0)
{
    /// <summary>Whether this cost is paid in gil.</summary>
    public bool IsGil => ItemId == ShopHelper.GilItemId;
}

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

/// <summary>A shop and everything it offers.</summary>
/// <param name="ShopId">The shop row, which is also the NPC's event handler id.</param>
/// <param name="Kind">Which kind of shop it is.</param>
/// <param name="Name">The shop's name, usually empty.</param>
/// <param name="Offers">Its offers, in sheet order.</param>
public sealed record ShopInfo(uint ShopId, EventHandlerContent Kind, string Name, IReadOnlyList<ShopOffer> Offers);

/// <summary>Every shop indexed by item and by shop.</summary>
/// <param name="ShopsByItem">The shops selling each item, in ascending shop order.</param>
/// <param name="OffersByShop">Each shop's offers, in sheet order.</param>
/// <param name="KindsByShop">Which kind each shop is.</param>
public sealed record ShopCatalog(
    IReadOnlyDictionary<uint, IReadOnlyList<uint>> ShopsByItem,
    IReadOnlyDictionary<uint, IReadOnlyList<ShopOffer>> OffersByShop,
    IReadOnlyDictionary<uint, EventHandlerContent> KindsByShop)
{
    /// <summary>An empty catalog.</summary>
    public static ShopCatalog Empty { get; } = new(
        new Dictionary<uint, IReadOnlyList<uint>>(),
        new Dictionary<uint, IReadOnlyList<ShopOffer>>(),
        new Dictionary<uint, EventHandlerContent>());

    /// <summary>The shops selling an item.</summary>
    /// <param name="itemId">The Item row.</param>
    /// <returns>The shop rows, or empty.</returns>
    public IReadOnlyList<uint> ShopsSelling(uint itemId)
        => ShopsByItem.TryGetValue(itemId, out var shops) ? shops : [];

    /// <summary>Every offer for an item, across every shop.</summary>
    /// <param name="itemId">The Item row.</param>
    /// <returns>One entry per shop line.</returns>
    public IReadOnlyList<ShopOffer> OffersFor(uint itemId)
    {
        var found = new List<ShopOffer>();

        foreach (var shopId in ShopsSelling(itemId))
        {
            if (!OffersByShop.TryGetValue(shopId, out var offers))
                continue;

            foreach (var offer in offers)
            {
                if (offer.ItemId == itemId)
                    found.Add(offer);
            }
        }

        return found;
    }

    /// <summary>The lowest gil price an item is sold for.</summary>
    /// <param name="itemId">The Item row.</param>
    /// <returns>The cost and the shop charging it, or null when nothing sells it for gil alone.</returns>
    public (uint ShopId, uint GilCost)? CheapestGilPrice(uint itemId)
    {
        (uint ShopId, uint GilCost)? best = null;

        foreach (var offer in OffersFor(itemId))
        {
            if (!offer.IsGilPurchase)
                continue;

            if (best == null || offer.GilCost < best.Value.GilCost)
                best = (offer.ShopId, offer.GilCost);
        }

        return best;
    }
}
