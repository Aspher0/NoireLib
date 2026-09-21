using FFXIVClientStructs.FFXIV.Client.Game.Event;
using FluentAssertions;
using NoireLib.Helpers;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks how vendor menus unfold into routes and how a route turns into purchase paths: a submenu adds an entry to
/// choose, a PreHandler reads as the shop it guards, gates gather along the way, cycles stop, and a path is built for
/// one item out of one route and one offer.
/// </summary>
public sealed class VendorHelperTests
{
    private const uint InclusionShopId = ((uint)EventHandlerContent.InclusionShop << 16) | 1;

    private static readonly Dictionary<uint, VendorHandlerNode> Nodes = new()
    {
        [0x00320019] = Topic(0x00320019, "Purchase Disciple of War Gear", 0x0004022C, 0x0004022D),
        [0x0004022C] = GilShop(0x0004022C, "Purchase Gear (Lv. 1-9)"),
        [0x0004022D] = GilShop(0x0004022D, "Purchase Gear (Lv. 10-19)"),
        [0x00320039] = Topic(0x00320039, "Purchase Disciple of War Gear", 0x00360019),
        [0x00360019] = new(0x00360019, EventHandlerContent.PreHandler, string.Empty, [0x0004031A], 69190, true),
        [0x0004031A] = GilShop(0x0004031A, "Purchase Gear (Lv. 62)"),
        [0x00040116] = GilShop(0x00040116, "Purchase Items"),
        [0x001B00D1] = new(0x001B00D1, EventHandlerContent.SpecialShop, "Curious Crop Exchange", [], 0, false),
        [0x000B0001] = new(0x000B0001, EventHandlerContent.CustomTalk, string.Empty, [0x00040116], 0, false),
        [0x000D0001] = new(0x000D0001, EventHandlerContent.Array, string.Empty, [0x00040116, 0x001B00D1], 0, false),
        [0x00320001] = Topic(0x00320001, "Loop", 0x00320002),
        [0x00320002] = Topic(0x00320002, "Loop back", 0x00320001),
        [InclusionShopId] = new(InclusionShopId, EventHandlerContent.InclusionShop, "Scrip Exchange", [], 0, false),
        [0x001B00E0] = new(0x001B00E0, EventHandlerContent.SpecialShop, "Crafters' Scrip Exchange", [], 0, false),
        [0x001B00E1] = new(0x001B00E1, EventHandlerContent.SpecialShop, "Gatherers' Scrip Exchange", [], 0, false),
        [0x00160001] = new(0x00160001, EventHandlerContent.GrandCompanyShop, string.Empty, [], 0, false, new VendorRequirements([], GrandCompanyId: 1)),
        [0x002A0001] = new(0x002A0001, EventHandlerContent.FreeCompanyCreditShop, "Company Credit Exchange", [], 0, false),
        [0x001B00F0] = new(0x001B00F0, EventHandlerContent.SpecialShop, "Resistance Supplies", [], 0, false),
    };

    private static readonly Dictionary<uint, IReadOnlyList<uint>> FateShops = new()
    {
        [1000] = [0x001B00F0],
    };

    private static readonly Dictionary<uint, IReadOnlyList<InclusionShopEntry>> InclusionShops = new()
    {
        [InclusionShopId] = [new InclusionShopEntry(0x001B00E0, 1, 33), new InclusionShopEntry(0x001B00E1, 2, 34)],
    };

    private static VendorHandlerNode? Read(uint handlerId) => Nodes.TryGetValue(handlerId, out var node) ? node : null;

    private static VendorHandlerNode GilShop(uint id, string name) => new(id, EventHandlerContent.Shop, name, [], 0, false);

    private static VendorHandlerNode Topic(uint id, string name, params uint[] children) => new(id, EventHandlerContent.TopicSelect, name, children, 0, false);

    private static string[] Labels(VendorHandlerRoute route) => route.Steps.Select(static step => step.Label).ToArray();

    private static string[] Labels(VendorRoute route) => route.MenuSteps.Select(static step => step.Label).ToArray();

    private static List<VendorRoute> Routes(params uint[] handlerIds)
        => VendorHelper.RoutesOfNpc(
            1000,
            [.. handlerIds],
            new VendorHelper.ScanState(Read, InclusionShops, static _ => "Test Vendor", NoFateShops));

    private static List<VendorRoute> RoutesWithFateShops(params uint[] handlerIds)
        => VendorHelper.RoutesOfNpc(
            1000,
            [.. handlerIds],
            new VendorHelper.ScanState(Read, InclusionShops, static _ => "Test Vendor", FateShops));

    private static readonly Dictionary<uint, IReadOnlyList<uint>> NoFateShops = [];

    private static ShopOffer Offer(uint shopId, uint itemId, uint gil, uint achievement = 0)
        => new(shopId, EventHandlerContent.Shop, itemId, 1, false, [new ShopCost(ShopHelper.GilItemId, gil)], [], achievement, 0);

    #region The menu walk

    [Fact]
    public void Resolve_TopicSelectAddsTheSubmenuEntry()
    {
        var routes = VendorPathResolver.Resolve([0x00010072, 0x00320019], Read);

        routes.Should().HaveCount(2, "because the quest handler leads to no shop and the submenu holds two shops");
        Labels(routes[1]).Should().Equal("Purchase Disciple of War Gear", "Purchase Gear (Lv. 10-19)");
        routes[1].ShopId.Should().Be(0x0004022Du);
    }

    [Fact]
    public void Resolve_PreHandlerReadsAsItsShopAndCarriesItsQuest()
    {
        var route = VendorPathResolver.Resolve([0x00320039], Read).Should().ContainSingle().Subject;

        Labels(route).Should().Equal("Purchase Disciple of War Gear", "Purchase Gear (Lv. 62)");
        route.Requirements.QuestIds.Should().Equal(69190u);
        route.MayShowDialogue.Should().BeTrue();
    }

    [Fact]
    public void Resolve_DirectShopsAreOneEntryEach()
    {
        var routes = VendorPathResolver.Resolve([0x00040116, 0x001B00D1], Read);

        routes.Select(Labels).Should().BeEquivalentTo(new[] { new[] { "Purchase Items" }, new[] { "Curious Crop Exchange" } });
        routes[1].ShopKind.Should().Be(EventHandlerContent.SpecialShop);
    }

    [Fact]
    public void Resolve_CustomTalkMarksTheRouteScripted()
        => VendorPathResolver.Resolve([0x000B0001], Read).Should().ContainSingle().Which.IsScripted.Should().BeTrue();

    [Fact]
    public void Resolve_ArrayChildrenReplaceTheArrayEntry()
    {
        var routes = VendorPathResolver.Resolve([0x000D0001], Read);

        routes.Select(Labels).Should().BeEquivalentTo(new[] { new[] { "Purchase Items" }, new[] { "Curious Crop Exchange" } });
    }

    [Fact]
    public void Resolve_CyclesStop()
        => VendorPathResolver.Resolve([0x00320001], Read).Should().BeEmpty();

    #endregion

    #region The shop families a handler walk alone misses

    [Fact]
    public void RoutesOfNpc_AQuartermasterOpensItsGrandCompanyShop()
    {
        var route = Routes(0x00160001).Should().ContainSingle().Subject;

        route.ShopKind.Should().Be(EventHandlerContent.GrandCompanyShop);
        route.Window.Should().Be(VendorShopWindow.GrandCompanyExchange);
        route.Requirements.GrandCompanyId.Should().Be(1u);
    }

    [Fact]
    public void RoutesOfNpc_ACaretakerOpensItsCompanyCreditShop()
    {
        var route = Routes(0x002A0001).Should().ContainSingle().Subject;

        route.ShopKind.Should().Be(EventHandlerContent.FreeCompanyCreditShop);
        route.Window.Should().Be(VendorShopWindow.FreeCompanyExchange);
        route.ShopName.Should().Be("Company Credit Exchange");
    }

    [Fact]
    public void RoutesOfNpc_AFateShopRowIsWalkedAlongsideTheHandlers()
    {
        RoutesWithFateShops().Should().ContainSingle().Which.ShopId.Should().Be(0x001B00F0u);
        Routes().Should().BeEmpty("because the NPC's own handlers name no shop");
    }

    #endregion

    #region Requirements

    [Fact]
    public void RoutesOfNpc_CarriesAPreHandlersUnlockQuest()
    {
        var route = Routes(0x00320039).Should().ContainSingle().Subject;

        route.Requirements.QuestIds.Should().Equal(69190u);
        Labels(route).Should().Equal("Purchase Disciple of War Gear", "Purchase Gear (Lv. 62)");
    }

    [Fact]
    public void RoutesOfNpc_SplitsAnInclusionShopPerCategoryAndCarriesItsClassJob()
    {
        var routes = Routes(InclusionShopId);

        routes.Select(static route => route.ShopId).Should().Equal(0x001B00E0u, 0x001B00E1u);
        routes.Select(static route => route.Requirements.ClassJobCategoryId).Should().Equal(33u, 34u);
        routes.Should().OnlyContain(route => route.ContainerShopId == InclusionShopId && route.Window == VendorShopWindow.InclusionShop);
    }

    [Fact]
    public void PathsFor_CarriesTheLinesAchievementIntoTheRequirements()
    {
        var route = Routes(0x00040116).Should().ContainSingle().Subject;
        var index = new VendorRouteIndex([route], static _ => [Offer(0x00040116, 4850, 100, achievement: 777)], static _ => [0x00040116u]);

        index.PathsFor(4850).Should().ContainSingle().Which.Requirements.AchievementId.Should().Be(777u);
    }

    #endregion

    #region Building paths on demand

    [Fact]
    public void PathsFor_BuildsTheSamePathsInTheSameOrderAsWalkingEveryRoute()
    {
        var routes = Routes(0x00320019);
        var offers = new Dictionary<uint, IReadOnlyList<ShopOffer>>
        {
            [0x0004022C] = [Offer(0x0004022C, 4850, 1000)],
            [0x0004022D] = [Offer(0x0004022D, 4850, 800)],
        };

        var index = new VendorRouteIndex(
            routes,
            shopId => offers.TryGetValue(shopId, out var found) ? found : [],
            static _ => [0x0004022Cu, 0x0004022Du]);

        var expected = routes
            .SelectMany(route => offers[route.ShopId].Select(offer => (Route: route, Offer: offer)))
            .OrderByDescending(static pair => pair.Route.Window == VendorShopWindow.Shop && pair.Offer.IsGilPurchase)
            .ThenBy(static pair => pair.Route.MenuSteps.Count)
            .Select(static pair => (pair.Route.ShopId, pair.Offer.GilCost))
            .ToList();

        index.PathsFor(4850).Select(static path => (path.ShopId, path.Offer.GilCost)).Should().Equal(expected);
    }

    [Fact]
    public void PathsFor_PutsTheGilShopOfOneNpcBeforeItsExchange()
    {
        var routes = Routes(0x000D0001);
        var offers = new Dictionary<uint, IReadOnlyList<ShopOffer>>
        {
            [0x001B00D1] = [new ShopOffer(0x001B00D1, EventHandlerContent.SpecialShop, 4850, 1, false, [new ShopCost(ShopHelper.StormSealItemId, 5)], [], 0, 0)],
            [0x00040116] = [Offer(0x00040116, 4850, 900)],
        };

        var index = new VendorRouteIndex(
            routes,
            shopId => offers.TryGetValue(shopId, out var found) ? found : [],
            static _ => [0x00040116u, 0x001B00D1u]);

        var paths = index.PathsFor(4850);

        paths.Should().HaveCount(2);
        paths[0].ShopId.Should().Be(0x00040116u);
        paths[0].CanBuyWithGilShop.Should().BeTrue();
        paths[1].IsCurrencyExchange.Should().BeTrue();
    }

    [Fact]
    public void RouteIndex_AnswersWhichNpcsReachAShop()
    {
        var index = new VendorRouteIndex(Routes(0x00040116), static _ => [], static _ => []);

        index.NpcsReaching(0x00040116u).Should().Equal(1000u);
        index.IsReachable(0x00040116u).Should().BeTrue();
        index.IsReachable(0x00040999u).Should().BeFalse();
        index.NpcsReaching(0x00040999u).Should().BeEmpty();
    }

    [Fact]
    public void EmptyRouteIndex_MissesEveryLookup()
    {
        VendorRouteIndex.Empty.Count.Should().Be(0);
        VendorRouteIndex.Empty.PathsFor(4850).Should().BeEmpty();
        VendorRouteIndex.Empty.RoutesOf(1000).Should().BeEmpty();
        VendorRouteIndex.Empty.IsReachable(0x00040116u).Should().BeFalse();
    }

    #endregion
}
