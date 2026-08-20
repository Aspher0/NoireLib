using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System.Collections.Generic;

namespace NoireLib.Helpers;

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
