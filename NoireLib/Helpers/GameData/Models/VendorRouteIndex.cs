using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Helpers;

/// <summary>
/// Every route an NPC takes to a shop, one per NPC and shop pair. Purchase paths are built per item on demand out of
/// the routes and the shop's offers.
/// </summary>
public sealed class VendorRouteIndex
{
    private readonly List<VendorRoute> routes;
    private readonly Dictionary<uint, List<int>> routesByShop = [];
    private readonly Dictionary<uint, List<int>> routesByNpc = [];
    private readonly Func<uint, IReadOnlyList<ShopOffer>> readOffers;
    private readonly Func<uint, IReadOnlyList<uint>> readShopsSelling;

    /// <summary>Builds the index over a set of routes.</summary>
    /// <param name="routes">The routes, in the order the walk found them.</param>
    /// <param name="readOffers">Reads a shop's offers. Defaults to the shop catalog.</param>
    /// <param name="readShopsSelling">Reads the shops selling an item. Defaults to the shop catalog.</param>
    public VendorRouteIndex(
        IReadOnlyList<VendorRoute> routes,
        Func<uint, IReadOnlyList<ShopOffer>>? readOffers = null,
        Func<uint, IReadOnlyList<uint>>? readShopsSelling = null)
    {
        ArgumentNullException.ThrowIfNull(routes);

        this.routes = [.. routes];
        this.readOffers = readOffers ?? (static shopId => ShopHelper.ScanCatalog().OffersByShop.TryGetValue(shopId, out var offers) ? offers : []);
        this.readShopsSelling = readShopsSelling ?? (static itemId => ShopHelper.ScanCatalog().ShopsSelling(itemId));

        for (var index = 0; index < this.routes.Count; index++)
        {
            var route = this.routes[index];

            if (!routesByShop.TryGetValue(route.ShopId, out var byShop))
                routesByShop[route.ShopId] = byShop = [];

            byShop.Add(index);

            if (!routesByNpc.TryGetValue(route.NpcBaseId, out var byNpc))
                routesByNpc[route.NpcBaseId] = byNpc = [];

            byNpc.Add(index);
        }
    }

    /// <summary>An index holding no route.</summary>
    public static VendorRouteIndex Empty { get; } = new([], static _ => [], static _ => []);

    /// <summary>Every route, in the order the walk found them.</summary>
    public IReadOnlyList<VendorRoute> Routes => routes;

    /// <summary>How many NPC and shop pairs the walk found.</summary>
    public int Count => routes.Count;

    /// <summary>Every shop at least one NPC reaches.</summary>
    public IReadOnlyCollection<uint> ReachableShopIds => routesByShop.Keys;

    /// <summary>The NPCs whose menus reach a shop.</summary>
    /// <param name="shopId">The shop row.</param>
    /// <returns>The ENpcBase rows, in ascending order, or empty.</returns>
    public IReadOnlyList<uint> NpcsReaching(uint shopId)
    {
        if (!routesByShop.TryGetValue(shopId, out var indexes))
            return [];

        var npcs = new List<uint>();

        foreach (var index in indexes)
        {
            if (npcs.Count == 0 || npcs[^1] != routes[index].NpcBaseId)
                npcs.Add(routes[index].NpcBaseId);
        }

        return npcs;
    }

    /// <summary>Whether an NPC opens a shop.</summary>
    /// <param name="shopId">The shop row.</param>
    /// <returns>True when at least one NPC reaches it, false otherwise.</returns>
    public bool IsReachable(uint shopId) => routesByShop.ContainsKey(shopId);

    /// <summary>Every route one NPC offers.</summary>
    /// <param name="npcBaseId">The ENpcBase row.</param>
    /// <returns>The routes in the NPC's handler order, or empty.</returns>
    public IReadOnlyList<VendorRoute> RoutesOf(uint npcBaseId)
    {
        if (!routesByNpc.TryGetValue(npcBaseId, out var indexes))
            return [];

        var found = new List<VendorRoute>(indexes.Count);

        foreach (var index in indexes)
            found.Add(routes[index]);

        return found;
    }

    /// <summary>The paths that buy an item, built out of the routes and the shops' offers.</summary>
    /// <param name="itemId">The Item row.</param>
    /// <returns>The paths, gil shop lines reached through named menus first, empty when no vendor sells the item.</returns>
    public IReadOnlyList<VendorPurchasePath> PathsFor(uint itemId)
    {
        var ordered = new List<int>();

        foreach (var shopId in readShopsSelling(itemId))
        {
            if (routesByShop.TryGetValue(shopId, out var indexes))
                ordered.AddRange(indexes);
        }

        if (ordered.Count == 0)
            return [];

        ordered.Sort();

        var paths = new List<VendorPurchasePath>();

        foreach (var index in ordered)
            AddPaths(paths, routes[index], itemId);

        return [.. paths.OrderByDescending(static path => path.CanBuyWithGilShop).ThenBy(static path => path.MenuSteps.Count)];
    }

    /// <summary>Every path one NPC offers, over every item its shops sell.</summary>
    /// <param name="npcBaseId">The ENpcBase row.</param>
    /// <returns>The paths in the NPC's handler order, or empty.</returns>
    public IReadOnlyList<VendorPurchasePath> PathsOf(uint npcBaseId)
    {
        var paths = new List<VendorPurchasePath>();

        foreach (var route in RoutesOf(npcBaseId))
            AddPaths(paths, route, null);

        return paths;
    }

    private void AddPaths(List<VendorPurchasePath> paths, VendorRoute route, uint? itemId)
    {
        foreach (var offer in readOffers(route.ShopId))
        {
            if (itemId != null && offer.ItemId != itemId.Value)
                continue;

            paths.Add(new VendorPurchasePath(
                route.NpcBaseId,
                route.NpcName,
                route.ShopId,
                route.ContainerShopId,
                route.ShopKind,
                route.ShopName,
                route.Window,
                route.MenuSteps,
                offer,
                route.Requirements.WithLine(offer.RequiredQuests, offer.RequiredAchievement),
                route.IsScripted,
                route.MayShowDialogue));
        }
    }
}
