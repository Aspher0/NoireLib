using FFXIVClientStructs.FFXIV.Client.Game.Event;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;

namespace NoireLib.Helpers;

/// <summary>
/// Reads what the game's shops sell and what they charge.
/// <br/>
/// A shop row id is also the NPC's event handler id, so feeding <see cref="FindShopsSelling"/> to
/// <see cref="EventNpcHelper.ScanHandlers"/> gives the NPCs selling an item.
/// </summary>
public static class ShopHelper
{
    /// <summary>Gil.</summary>
    public const uint GilItemId = 1;

    /// <summary>The Maelstrom's storm seals.</summary>
    public const uint StormSealItemId = 20;

    /// <summary>The Order of the Twin Adder's serpent seals.</summary>
    public const uint SerpentSealItemId = 21;

    /// <summary>The Immortal Flames' flame seals.</summary>
    public const uint FlameSealItemId = 22;

    private const int HandlerContentShift = 16;

    private static ShopCatalog? cachedCatalog;

    #region One shop

    /// <summary>Which kind of shop a row id names, taken from the handler content in its high word.</summary>
    /// <param name="shopId">The shop row id.</param>
    /// <returns>The handler content, or null when it is not a shop this helper reads.</returns>
    public static EventHandlerContent? KindOf(uint shopId)
    {
        if (shopId == 0)
            return null;

        var content = (EventHandlerContent)(shopId >> HandlerContentShift);

        return content is EventHandlerContent.Shop or EventHandlerContent.SpecialShop ? content : null;
    }

    /// <summary>A shop's own name, which most vendors leave empty.</summary>
    /// <param name="shopId">The shop row id.</param>
    /// <returns>The name, or an empty string.</returns>
    public static string Name(uint shopId)
    {
        return SafeExecutor.ExecuteSafely(() => KindOf(shopId) switch
        {
            EventHandlerContent.Shop => ExcelSheetHelper.TryGetRow<GilShop>(shopId, out var gilShop) && gilShop.HasValue
                ? gilShop.Value.Name.ExtractText()
                : string.Empty,
            EventHandlerContent.SpecialShop => ExcelSheetHelper.TryGetRow<SpecialShop>(shopId, out var specialShop) && specialShop.HasValue
                ? specialShop.Value.Name.ExtractText()
                : string.Empty,
            _ => string.Empty,
        }, string.Empty) ?? string.Empty;
    }

    /// <summary>Everything a shop sells, read straight from the sheets.</summary>
    /// <param name="shopId">The shop row id.</param>
    /// <returns>The offers in sheet order, or an empty list when the id names no shop.</returns>
    public static IReadOnlyList<ShopOffer> ReadOffers(uint shopId)
    {
        return KindOf(shopId) switch
        {
            EventHandlerContent.Shop => ReadGilShopOffers(shopId),
            EventHandlerContent.SpecialShop => ReadSpecialShopOffers(shopId),
            _ => [],
        };
    }

    /// <summary>A shop, its kind, its name and its offers in one read.</summary>
    /// <param name="shopId">The shop row id.</param>
    /// <returns>The shop, or null when the id names no shop.</returns>
    public static ShopInfo? ReadShop(uint shopId)
    {
        var kind = KindOf(shopId);

        if (kind == null)
            return null;

        var offers = ReadOffers(shopId);

        return offers.Count == 0 && Name(shopId).Length == 0
            ? null
            : new ShopInfo(shopId, kind.Value, Name(shopId), offers);
    }

    #endregion

    #region The catalog

    /// <summary>Indexes every gil and special shop. Cached.</summary>
    /// <param name="refresh">Whether to rebuild rather than answer from the cache.</param>
    /// <returns>The catalog, or <see cref="ShopCatalog.Empty"/> when the sheets could not be read.</returns>
    public static ShopCatalog ScanCatalog(bool refresh = false)
    {
        if (!refresh && cachedCatalog != null)
            return cachedCatalog;

        var built = SafeExecutor.ExecuteSafely(() =>
        {
            var shopsByItem = new Dictionary<uint, IReadOnlyList<uint>>();
            var offersByShop = new Dictionary<uint, IReadOnlyList<ShopOffer>>();
            var kindsByShop = new Dictionary<uint, EventHandlerContent>();

            var gilShops = ExcelSheetHelper.GetSheet<GilShop>();
            if (gilShops != null)
            {
                foreach (var shop in gilShops)
                {
                    if (shop.RowId != 0)
                        Index(shop.RowId, EventHandlerContent.Shop, ReadGilShopOffers(shop.RowId));
                }
            }

            var specialShops = ExcelSheetHelper.GetSheet<SpecialShop>();
            if (specialShops != null)
            {
                foreach (var shop in specialShops)
                {
                    if (shop.RowId != 0)
                        Index(shop.RowId, EventHandlerContent.SpecialShop, ReadSpecialShopOffers(shop.RowId));
                }
            }

            return new ShopCatalog(shopsByItem, offersByShop, kindsByShop);

            void Index(uint shopId, EventHandlerContent kind, IReadOnlyList<ShopOffer> offers)
            {
                if (offers.Count == 0)
                    return;

                offersByShop[shopId] = offers;
                kindsByShop[shopId] = kind;

                foreach (var offer in offers)
                {
                    if (shopsByItem.TryGetValue(offer.ItemId, out var existing))
                    {
                        var shops = (List<uint>)existing;
                        if (shops[^1] != shopId)
                            shops.Add(shopId);

                        continue;
                    }

                    shopsByItem[offer.ItemId] = new List<uint> { shopId };
                }
            }
        }, ShopCatalog.Empty) ?? ShopCatalog.Empty;

        return cachedCatalog = built;
    }

    /// <summary>The shops that sell an item.</summary>
    /// <param name="itemId">The Item row id.</param>
    /// <returns>The shop row ids, in ascending order.</returns>
    public static IReadOnlyList<uint> FindShopsSelling(uint itemId) => ScanCatalog().ShopsSelling(itemId);

    #endregion

    #region Shops an NPC does not run directly

    /// <summary>
    /// The shops each <c>TopicSelect</c> menu leads to. An NPC fronting a menu runs the menu's handler, not the
    /// shop's, so a handler scan alone misses these.
    /// </summary>
    /// <returns>The shop row ids behind each TopicSelect row id.</returns>
    public static IReadOnlyDictionary<uint, IReadOnlyList<uint>> ReadTopicSelectShops()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var found = new Dictionary<uint, IReadOnlyList<uint>>();
            var sheet = ExcelSheetHelper.GetSheet<TopicSelect>();
            if (sheet == null)
                return found;

            foreach (var topic in sheet)
            {
                if (topic.RowId == 0)
                    continue;

                List<uint>? shops = null;

                foreach (var shop in topic.Shop)
                {
                    if (shop.RowId != 0)
                        (shops ??= []).Add(shop.RowId);
                }

                if (shops != null)
                    found[topic.RowId] = shops;
            }

            return found;
        }, []) ?? [];
    }

    /// <summary>The special shops each <c>InclusionShop</c> leads to, through its categories and series.</summary>
    /// <returns>The SpecialShop row ids behind each InclusionShop row id.</returns>
    public static IReadOnlyDictionary<uint, IReadOnlyList<uint>> ReadInclusionShops()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var found = new Dictionary<uint, IReadOnlyList<uint>>();
            var sheet = ExcelSheetHelper.GetSheet<InclusionShop>();
            if (sheet == null)
                return found;

            foreach (var inclusion in sheet)
            {
                if (inclusion.RowId == 0)
                    continue;

                var shops = new List<uint>();

                foreach (var categoryRef in inclusion.Category)
                {
                    var category = categoryRef.ValueNullable;
                    if (category == null)
                        continue;

                    if (!ExcelSheetHelper.TryGetSubrows<InclusionShopSeries>(category.Value.InclusionShopSeries.RowId, out var series))
                        continue;

                    foreach (var entry in series)
                    {
                        var shopId = entry.SpecialShop.RowId;
                        if (shopId != 0 && !shops.Contains(shopId))
                            shops.Add(shopId);
                    }
                }

                if (shops.Count > 0)
                    found[inclusion.RowId] = shops;
            }

            return found;
        }, []) ?? [];
    }

    #endregion

    #region Grand company quartermasters

    /// <summary>
    /// What a grand company's quartermaster sells. Addressed by company rather than by shop row, since the stock is
    /// assembled from every <c>GCScripShopCategory</c> belonging to it.
    /// </summary>
    /// <param name="grandCompany">The grand company.</param>
    /// <returns>The offers, priced in that company's seals.</returns>
    public static IReadOnlyList<ShopOffer> ReadGrandCompanyOffers(GrandCompany grandCompany)
    {
        if (grandCompany == GrandCompany.None)
            return [];

        var sealItemId = SealItemId(grandCompany);

        return SafeExecutor.ExecuteSafely(() =>
        {
            var offers = new List<ShopOffer>();
            var categories = ExcelSheetHelper.GetSheet<GCScripShopCategory>();
            if (categories == null)
                return offers;

            foreach (var category in categories)
            {
                if (category.GrandCompany.RowId != (uint)grandCompany)
                    continue;

                if (!ExcelSheetHelper.TryGetSubrows<GCScripShopItem>(category.RowId, out var items))
                    continue;

                foreach (var item in items)
                {
                    if (item.Item.RowId == 0)
                        continue;

                    offers.Add(new ShopOffer(
                        category.RowId,
                        EventHandlerContent.GrandCompanyShop,
                        item.Item.RowId,
                        1,
                        false,
                        sealItemId == 0 ? [] : [new ShopCost(sealItemId, item.CostGCSeals)],
                        [],
                        0,
                        0));
                }
            }

            return offers;
        }, []) ?? [];
    }

    /// <summary>
    /// The seal item a grand company's quartermaster charges in.
    /// </summary>
    /// <param name="grandCompany">The grand company.</param>
    /// <returns>The seal Item row id, or zero when it could not be found.</returns>
    public static uint SealItemId(GrandCompany grandCompany) => grandCompany switch
    {
        GrandCompany.Maelstrom => StormSealItemId,
        GrandCompany.TwinAdder => SerpentSealItemId,
        GrandCompany.ImmortalFlames => FlameSealItemId,
        _ => 0,
    };

    #endregion

    #region Sheet reading

    private static IReadOnlyList<ShopOffer> ReadGilShopOffers(uint shopId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var offers = new List<ShopOffer>();

            if (!ExcelSheetHelper.TryGetSubrows<GilShopItem>(shopId, out var subrows))
                return offers;

            foreach (var line in subrows)
            {
                var item = line.Item.ValueNullable;
                if (item == null || item.Value.RowId == 0)
                    continue;

                var quests = new List<uint>();
                foreach (var quest in line.QuestRequired)
                {
                    if (quest.RowId != 0)
                        quests.Add(quest.RowId);
                }

                offers.Add(new ShopOffer(
                    shopId,
                    EventHandlerContent.Shop,
                    item.Value.RowId,
                    1,
                    line.IsHQ,
                    // A gil shop carries no price of its own; the item's vendor price is the price.
                    [new ShopCost(GilItemId, item.Value.PriceMid)],
                    quests,
                    line.AchievementRequired.RowId,
                    line.Patch));
            }

            return offers;
        }, []) ?? [];
    }

    private static IReadOnlyList<ShopOffer> ReadSpecialShopOffers(uint shopId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var offers = new List<ShopOffer>();

            if (!ExcelSheetHelper.TryGetRow<SpecialShop>(shopId, out var shop) || !shop.HasValue)
                return offers;

            foreach (var entry in shop.Value.Item)
            {
                var costs = new List<ShopCost>();

                foreach (var cost in entry.ItemCosts)
                {
                    if (cost.ItemCost.RowId != 0 && cost.CurrencyCost != 0)
                        costs.Add(new ShopCost(cost.ItemCost.RowId, cost.CurrencyCost, cost.CollectabilityCost));
                }

                var quests = entry.Quest.RowId != 0 ? new List<uint> { entry.Quest.RowId } : [];

                // One entry can hand over several items for one price, so each becomes its own offer sharing the
                // costs. Skipping entries that hand over nothing drops the sheet's fixed-length padding; filtering
                // on cost instead would also drop the free exchanges.
                foreach (var received in entry.ReceiveItems)
                {
                    if (received.Item.RowId == 0 || received.ReceiveCount == 0)
                        continue;

                    offers.Add(new ShopOffer(
                        shopId,
                        EventHandlerContent.SpecialShop,
                        received.Item.RowId,
                        received.ReceiveCount,
                        received.ReceiveHq,
                        costs,
                        quests,
                        entry.AchievementUnlock.RowId,
                        entry.PatchNumber));
                }
            }

            return offers;
        }, []) ?? [];
    }

    #endregion
}
