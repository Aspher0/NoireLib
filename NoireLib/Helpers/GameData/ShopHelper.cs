using FFXIVClientStructs.FFXIV.Client.Game.Event;
using Lumina.Excel.Sheets;
using System.Collections.Generic;
using System.Threading;
using GrandCompany = FFXIVClientStructs.FFXIV.Client.UI.Agent.GrandCompany;

namespace NoireLib.Helpers;

/// <summary>
/// Reads what the game's shops sell and what they charge.
/// <br/>
/// A shop row id is also the NPC's event handler id. Feeding <see cref="FindShopsSelling"/> to
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

    /// <summary>The ItemSortCategory row holding the game's own Currency tab.</summary>
    public const uint CurrencySortCategoryId = 3;

    /// <summary>The ItemUICategory row holding the currencies the interface groups together.</summary>
    public const uint CurrencyUiCategoryId = 100;

    /// <summary>The patch a shop line carries once a patch has turned it off. The window no longer shows it.</summary>
    public const ushort RetiredPatch = 9999;

    private const int HandlerContentShift = 16;

    private static readonly IReadOnlySet<uint> FallbackCurrencyItemIds = new HashSet<uint>
    {
        GilItemId,
        StormSealItemId,
        SerpentSealItemId,
        FlameSealItemId,
    };

    private static readonly object CurrencyLock = new();

    private static readonly object CatalogLock = new();

    private static ShopCatalog? cachedCatalog;
    private static IReadOnlySet<uint>? cachedCurrencies;

    #region Currencies

    /// <summary>
    /// Every item the game files as a currency: gil, the seals, the tomestones, the scrips, the marks and MGP. Read
    /// once from the Item sheet.
    /// </summary>
    public static IReadOnlySet<uint> CurrencyItemIds
    {
        get
        {
            lock (CurrencyLock)
                return cachedCurrencies ??= BuildCurrencyItemIds();
        }
    }

    /// <summary>Whether an item is a currency, never something a list can ask for.</summary>
    /// <param name="itemId">The Item row id.</param>
    /// <returns>True when the item is a currency, false otherwise.</returns>
    public static bool IsCurrency(uint itemId) => itemId != 0 && CurrencyItemIds.Contains(itemId);

    private static IReadOnlySet<uint> BuildCurrencyItemIds()
    {
        var found = SafeExecutor.ExecuteSafely(() =>
        {
            var currencies = new HashSet<uint>(FallbackCurrencyItemIds);
            var sheet = ExcelSheetHelper.GetSheet<Item>();

            if (sheet == null)
                return currencies;

            foreach (var item in sheet)
            {
                if (item.RowId != 0
                    && (item.ItemSortCategory.RowId == CurrencySortCategoryId || item.ItemUICategory.RowId == CurrencyUiCategoryId))
                {
                    currencies.Add(item.RowId);
                }
            }

            return currencies;
        }, null);

        return found ?? FallbackCurrencyItemIds;
    }

    #endregion

    #region One shop

    /// <summary>Which kind of shop a row id names, taken from the handler content in its high word.</summary>
    /// <param name="shopId">The shop row id.</param>
    /// <returns>The handler content, or null when it is not a shop this helper reads.</returns>
    public static EventHandlerContent? KindOf(uint shopId)
    {
        if (shopId == 0)
            return null;

        var content = (EventHandlerContent)(shopId >> HandlerContentShift);

        return content is EventHandlerContent.Shop
            or EventHandlerContent.SpecialShop
            or EventHandlerContent.GrandCompanyShop
            or EventHandlerContent.FreeCompanyCreditShop
            ? content
            : null;
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
            EventHandlerContent.FreeCompanyCreditShop => ExcelSheetHelper.TryGetRow<FccShop>(shopId, out var creditShop) && creditShop.HasValue
                ? creditShop.Value.Name.ExtractText()
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
            EventHandlerContent.GrandCompanyShop => ReadGrandCompanyShopOffers(shopId),
            EventHandlerContent.FreeCompanyCreditShop => ReadCompanyCreditOffers(shopId),
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
    /// <param name="refresh">Whether to rebuild, never answer from the cache.</param>
    /// <returns>The catalog, or <see cref="ShopCatalog.Empty"/> when the sheets could not be read.</returns>
    public static ShopCatalog ScanCatalog(bool refresh = false)
    {
        if (!refresh && Volatile.Read(ref cachedCatalog) is { } cached)
            return cached;

        lock (CatalogLock)
            return BuildCatalog(refresh);
    }

    private static ShopCatalog BuildCatalog(bool refresh)
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

            var grandCompanyShops = ExcelSheetHelper.GetSheet<GCShop>();
            if (grandCompanyShops != null)
            {
                foreach (var shop in grandCompanyShops)
                {
                    if (shop.RowId != 0)
                        Index(shop.RowId, EventHandlerContent.GrandCompanyShop, ReadGrandCompanyShopOffers(shop.RowId));
                }
            }

            var creditShops = ExcelSheetHelper.GetSheet<FccShop>();
            if (creditShops != null)
            {
                foreach (var shop in creditShops)
                {
                    if (shop.RowId != 0)
                        Index(shop.RowId, EventHandlerContent.FreeCompanyCreditShop, ReadCompanyCreditOffers(shop.RowId));
                }
            }

            return new ShopCatalog(shopsByItem, offersByShop, kindsByShop, NpcsByShop(offersByShop.Keys));

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

        Volatile.Write(ref cachedCatalog, built);
        return built;
    }

    /// <summary>The shops that sell an item.</summary>
    /// <param name="itemId">The Item row id.</param>
    /// <returns>The shop row ids, in ascending order.</returns>
    public static IReadOnlyList<uint> FindShopsSelling(uint itemId) => ScanCatalog().ShopsSelling(itemId);

    private static Dictionary<uint, IReadOnlyList<uint>>? NpcsByShop(IEnumerable<uint> shopIds)
    {
        var index = VendorHelper.ScanRoutes();

        if (index.Count == 0)
            return null;

        var reaching = new Dictionary<uint, IReadOnlyList<uint>>();

        foreach (var shopId in shopIds)
        {
            var npcs = index.NpcsReaching(shopId);

            if (npcs.Count > 0)
                reaching[shopId] = npcs;
        }

        return reaching;
    }

    #endregion

    #region Shops an NPC does not run directly

    /// <summary>The shops each <c>TopicSelect</c> menu leads to. An NPC fronting a menu runs the menu's handler.</summary>
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

    /// <summary>
    /// The special shops a <c>FateShop</c> row hands an NPC. The row is keyed by the ENpcBase and no handler on that
    /// NPC points at it. A walk over its handlers alone misses these.
    /// </summary>
    /// <returns>The special shop row ids behind each ENpcBase row id.</returns>
    public static IReadOnlyDictionary<uint, IReadOnlyList<uint>> ReadFateShops()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var found = new Dictionary<uint, IReadOnlyList<uint>>();
            var sheet = ExcelSheetHelper.GetSheet<FateShop>();
            if (sheet == null)
                return found;

            foreach (var row in sheet)
            {
                if (row.RowId == 0)
                    continue;

                List<uint>? shops = null;

                foreach (var shop in row.SpecialShop)
                {
                    if (shop.RowId != 0 && (shops == null || !shops.Contains(shop.RowId)))
                        (shops ??= []).Add(shop.RowId);
                }

                if (shops != null)
                    found[row.RowId] = shops;
            }

            return found;
        }, []) ?? [];
    }

    /// <summary>The special shops each <c>InclusionShop</c> leads to, through its categories and series.</summary>
    /// <returns>The entries behind each InclusionShop row id, each naming its special shop and its job restriction.</returns>
    public static IReadOnlyDictionary<uint, IReadOnlyList<InclusionShopEntry>> ReadInclusionShops()
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var found = new Dictionary<uint, IReadOnlyList<InclusionShopEntry>>();
            var sheet = ExcelSheetHelper.GetSheet<InclusionShop>();
            if (sheet == null)
                return found;

            foreach (var inclusion in sheet)
            {
                if (inclusion.RowId == 0)
                    continue;

                var shops = new List<InclusionShopEntry>();

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
                        if (shopId == 0 || shops.Exists(known => known.SpecialShopId == shopId))
                            continue;

                        shops.Add(new InclusionShopEntry(shopId, category.Value.RowId, category.Value.ClassJobCategory.RowId));
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

    /// <summary>What a grand company's quartermaster sells, from every <c>GCScripShopCategory</c> belonging to it.</summary>
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

    private static IReadOnlyList<ShopOffer> ReadGrandCompanyShopOffers(uint shopId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (!ExcelSheetHelper.TryGetRow<GCShop>(shopId, out var shop) || shop is not { } row || row.GrandCompany.RowId == 0)
                return (IReadOnlyList<ShopOffer>)[];

            var offers = new List<ShopOffer>();

            foreach (var offer in ReadGrandCompanyOffers((GrandCompany)row.GrandCompany.RowId))
                offers.Add(offer with { ShopId = shopId });

            return offers;
        }, []) ?? [];
    }

    private static IReadOnlyList<ShopOffer> ReadCompanyCreditOffers(uint shopId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            var offers = new List<ShopOffer>();

            if (!ExcelSheetHelper.TryGetRow<FccShop>(shopId, out var shop) || shop is not { } row)
                return offers;

            foreach (var line in row.ItemData)
            {
                if (line.Item.RowId == 0)
                    continue;

                offers.Add(new ShopOffer(
                    shopId,
                    EventHandlerContent.FreeCompanyCreditShop,
                    line.Item.RowId,
                    1,
                    false,
                    [new ShopCost(0, line.Cost, 0, ShopCostKind.CompanyCredit)],
                    [],
                    0,
                    0));
            }

            return offers;
        }, []) ?? [];
    }

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
                if (item == null || item.Value.RowId == 0 || line.Patch == RetiredPatch)
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
                    // A gil shop's price is the item's vendor price.
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
                if (entry.PatchNumber == RetiredPatch)
                    continue;

                var costs = new List<ShopCost>();

                foreach (var cost in entry.ItemCosts)
                {
                    if (cost.ItemCost.RowId == 0 || cost.CurrencyCost == 0)
                        continue;

                    // For those two kinds the cost column holds a Tomestones row or a scrip slot.
                    var kind = ShopCurrencyHelper.KindOf(cost.CostType);
                    var slot = kind is ShopCostKind.Tomestone or ShopCostKind.Scrip ? cost.ItemCost.RowId : 0;

                    costs.Add(new ShopCost(
                        ShopCurrencyHelper.Resolve(kind, cost.ItemCost.RowId),
                        cost.CurrencyCost,
                        cost.CollectabilityCost,
                        kind,
                        slot));
                }

                var quests = entry.Quest.RowId != 0 ? new List<uint> { entry.Quest.RowId } : [];

                // One entry can hand over several items for one price. Entries handing over nothing are the sheet's padding.
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
