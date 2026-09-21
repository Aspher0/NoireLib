using FFXIVClientStructs.FFXIV.Client.Game.Event;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Helpers;

internal readonly record struct VendorHandlerNode(
    uint HandlerId,
    EventHandlerContent Kind,
    string Label,
    IReadOnlyList<uint> Children,
    uint QuestId,
    bool ShowsDialogue,
    VendorRequirements? Gates = null);

internal sealed record VendorHandlerRoute(
    IReadOnlyList<VendorMenuStep> Steps,
    uint ShopId,
    EventHandlerContent ShopKind,
    string ShopName,
    VendorRequirements Requirements,
    bool IsScripted,
    bool MayShowDialogue);

internal static class VendorPathResolver
{
    private const int DepthLimit = 6;

    internal static List<VendorHandlerRoute> Resolve(IEnumerable<uint> npcHandlerIds, Func<uint, VendorHandlerNode?> readNode)
    {
        var routes = new List<VendorHandlerRoute>();

        foreach (var handlerId in npcHandlerIds)
        {
            if (handlerId == 0 || readNode(handlerId) is not { } node)
                continue;

            Walk(node, [Step(node, readNode)], VendorRequirements.None, false, false, 0, routes, readNode, []);
        }

        return routes;
    }

    internal static VendorMenuStep Step(VendorHandlerNode node, Func<uint, VendorHandlerNode?> readNode)
        => new(node.HandlerId, node.Kind, LabelOf(node, readNode, 0));

    // A PreHandler or an array handler shows no label of its own.
    internal static string LabelOf(VendorHandlerNode node, Func<uint, VendorHandlerNode?> readNode, int depth)
    {
        if (node.Label.Length > 0 || depth >= DepthLimit)
            return node.Label;

        if (node.Kind is EventHandlerContent.PreHandler or EventHandlerContent.Array
            && node.Children.Count > 0
            && readNode(node.Children[0]) is { } child)
        {
            return LabelOf(child, readNode, depth + 1);
        }

        return string.Empty;
    }

    private static void Walk(
        VendorHandlerNode node,
        List<VendorMenuStep> steps,
        VendorRequirements gates,
        bool isScripted,
        bool mayShowDialogue,
        int depth,
        List<VendorHandlerRoute> routes,
        Func<uint, VendorHandlerNode?> readNode,
        HashSet<uint> path)
    {
        if (depth > DepthLimit || !path.Add(node.HandlerId))
            return;

        if (node.QuestId != 0 && !gates.QuestIds.Contains(node.QuestId))
            gates = gates with { QuestIds = [.. gates.QuestIds, node.QuestId] };

        if (node.Gates is { } nodeGates)
            gates = gates.Merge(nodeGates);

        mayShowDialogue |= node.ShowsDialogue;

        switch (node.Kind)
        {
            case EventHandlerContent.Shop:
            case EventHandlerContent.SpecialShop:
            case EventHandlerContent.InclusionShop:
            case EventHandlerContent.GrandCompanyShop:
            case EventHandlerContent.FreeCompanyCreditShop:
                routes.Add(new VendorHandlerRoute([.. steps], node.HandlerId, node.Kind, node.Label, gates, isScripted, mayShowDialogue));
                break;

            case EventHandlerContent.PreHandler:
                foreach (var childNode in Children(node, readNode))
                    Walk(childNode, steps, gates, isScripted, mayShowDialogue, depth + 1, routes, readNode, path);

                break;

            case EventHandlerContent.Array:
                foreach (var childNode in Children(node, readNode))
                    Walk(childNode, [.. steps[..^1], Step(childNode, readNode)], gates, isScripted, mayShowDialogue, depth + 1, routes, readNode, path);

                break;

            case EventHandlerContent.TopicSelect:
                foreach (var childNode in Children(node, readNode))
                    Walk(childNode, [.. steps, Step(childNode, readNode)], gates, isScripted, mayShowDialogue, depth + 1, routes, readNode, path);

                break;

            case EventHandlerContent.CustomTalk:
                foreach (var childNode in Children(node, readNode))
                    Walk(childNode, [.. steps, Step(childNode, readNode)], gates, true, mayShowDialogue, depth + 1, routes, readNode, path);

                break;
        }

        path.Remove(node.HandlerId);
    }

    private static IEnumerable<VendorHandlerNode> Children(VendorHandlerNode node, Func<uint, VendorHandlerNode?> readNode)
    {
        foreach (var childId in node.Children)
        {
            if (childId != 0 && readNode(childId) is { } child)
                yield return child;
        }
    }
}

/// <summary>
/// Resolves how to buy an item from an NPC vendor from the sheets: the NPC, the menu entries to choose, the shop and
/// the line. Menus come from TopicSelect submenus, PreHandlers, array handlers and CustomTalk scripts.
/// </summary>
public static class VendorHelper
{
    private const int HandlerContentShift = 16;

    private static readonly object CacheLock = new();
    private static VendorRouteIndex? cachedRoutes;

    [ThreadStatic]
    private static bool scanning;

    /// <summary>Finds every NPC path that sells an item. The index behind it is built once and kept.</summary>
    /// <param name="itemId">The Item row id.</param>
    /// <returns>The paths, gil shops reached through named menus first, empty when no vendor sells the item.</returns>
    public static IReadOnlyList<VendorPurchasePath> FindPurchasePaths(uint itemId) => ScanRoutes().PathsFor(itemId);

    /// <summary>Reads every purchase path one NPC offers.</summary>
    /// <param name="npcBaseId">The ENpcBase row id.</param>
    /// <returns>The paths in the NPC's handler order, empty when the NPC sells nothing.</returns>
    public static IReadOnlyList<VendorPurchasePath> ReadPurchasePaths(uint npcBaseId)
    {
        return SafeExecutor.ExecuteSafely(() =>
        {
            if (!ExcelSheetHelper.TryGetRow<ENpcBase>(npcBaseId, out var row) || row is not { } npc)
                return (IReadOnlyList<VendorPurchasePath>)[];

            var routes = RoutesOfNpc(npcBaseId, HandlerIdsOf(npc), new ScanState());

            return new VendorRouteIndex(routes).PathsOf(npcBaseId);
        }, []) ?? [];
    }

    /// <summary>
    /// Walks every ENpcBase once and indexes the route each NPC takes to each shop. Cached, the sheets cannot change
    /// while the client runs.
    /// </summary>
    /// <param name="refresh">Whether to rebuild, never answer from the cache.</param>
    /// <returns>The route index, or <see cref="VendorRouteIndex.Empty"/> when the sheets could not be read.</returns>
    public static VendorRouteIndex ScanRoutes(bool refresh = false)
    {
        // A re-entrant walk answers nothing. It never waits on a lock its own thread holds.
        if (scanning)
            return VendorRouteIndex.Empty;

        lock (CacheLock)
        {
            if (!refresh && cachedRoutes != null)
                return cachedRoutes;

            scanning = true;

            try
            {
                var built = SafeExecutor.ExecuteSafely(() =>
                {
                    var routes = new List<VendorRoute>();
                    var sheet = ExcelSheetHelper.GetSheet<ENpcBase>();

                    if (sheet == null)
                        return routes;

                    var scan = new ScanState();

                    foreach (var npc in sheet)
                    {
                        if (npc.RowId != 0)
                            routes.AddRange(RoutesOfNpc(npc.RowId, HandlerIdsOf(npc), scan));
                    }

                    return routes;
                }, []) ?? [];

                var index = new VendorRouteIndex(built);

                if (built.Count > 0)
                    cachedRoutes = index;

                return index;
            }
            finally
            {
                scanning = false;
            }
        }
    }

    private static List<uint> HandlerIdsOf(ENpcBase npc)
    {
        var handlerIds = new List<uint>();

        foreach (var data in npc.ENpcData)
        {
            if (data.RowId != 0)
                handlerIds.Add(data.RowId);
        }

        return handlerIds;
    }

    internal static List<VendorRoute> RoutesOfNpc(uint npcBaseId, List<uint> handlerIds, ScanState scan)
    {
        var routes = new List<VendorRoute>();
        var roots = scan.WithFateShops(npcBaseId, handlerIds);

        if (!roots.Any(static id => IsVendorFamily(id >> HandlerContentShift)))
            return routes;

        var walked = VendorPathResolver.Resolve(roots, scan.ReadNode);

        if (walked.Count == 0)
            return routes;

        var npcName = scan.NpcName(npcBaseId);

        foreach (var route in walked)
        {
            if (route.ShopKind == EventHandlerContent.InclusionShop)
            {
                foreach (var entry in scan.InclusionShops.TryGetValue(route.ShopId, out var entries) ? entries : [])
                {
                    var gates = entry.ClassJobCategoryId == 0
                        ? route.Requirements
                        : route.Requirements.Merge(new VendorRequirements([], ClassJobCategoryId: entry.ClassJobCategoryId));

                    routes.Add(Build(npcBaseId, npcName, route, entry.SpecialShopId, route.ShopId, EventHandlerContent.SpecialShop, VendorShopWindow.InclusionShop, gates, scan));
                }

                continue;
            }

            routes.Add(Build(npcBaseId, npcName, route, route.ShopId, 0, route.ShopKind, WindowOf(route.ShopKind), route.Requirements, scan));
        }

        return routes;
    }

    private static VendorShopWindow WindowOf(EventHandlerContent shopKind) => shopKind switch
    {
        EventHandlerContent.Shop => VendorShopWindow.Shop,
        EventHandlerContent.GrandCompanyShop => VendorShopWindow.GrandCompanyExchange,
        EventHandlerContent.FreeCompanyCreditShop => VendorShopWindow.FreeCompanyExchange,
        _ => VendorShopWindow.ShopExchange,
    };

    private static VendorRoute Build(
        uint npcBaseId,
        string npcName,
        VendorHandlerRoute route,
        uint shopId,
        uint containerShopId,
        EventHandlerContent shopKind,
        VendorShopWindow window,
        VendorRequirements gates,
        ScanState scan)
    {
        var shopName = route.ShopName;

        if (shopId != route.ShopId)
        {
            var node = scan.ReadNode(shopId);
            shopName = node?.Label ?? ShopHelper.Name(shopId);

            if (node is { } inner)
            {
                if (inner.Gates is { } shopGates)
                    gates = gates.Merge(shopGates);

                if (inner.QuestId != 0 && !gates.QuestIds.Contains(inner.QuestId))
                    gates = gates with { QuestIds = [.. gates.QuestIds, inner.QuestId] };
            }
        }

        return new VendorRoute(
            npcBaseId,
            npcName,
            shopId,
            containerShopId,
            shopKind,
            shopName,
            window,
            route.Steps,
            gates,
            route.IsScripted,
            route.MayShowDialogue);
    }

    private static bool IsVendorFamily(uint content)
        => (EventHandlerContent)content is EventHandlerContent.Shop
            or EventHandlerContent.SpecialShop
            or EventHandlerContent.TopicSelect
            or EventHandlerContent.PreHandler
            or EventHandlerContent.InclusionShop
            or EventHandlerContent.CustomTalk
            or EventHandlerContent.Array
            or EventHandlerContent.GrandCompanyShop
            or EventHandlerContent.FreeCompanyCreditShop;

    internal sealed class ScanState(
        Func<uint, VendorHandlerNode?>? readNode = null,
        IReadOnlyDictionary<uint, IReadOnlyList<InclusionShopEntry>>? inclusionShops = null,
        Func<uint, string>? readNpcName = null,
        IReadOnlyDictionary<uint, IReadOnlyList<uint>>? fateShops = null)
    {
        private readonly Dictionary<uint, VendorHandlerNode?> nodes = [];
        private readonly Dictionary<uint, string> names = [];

        public IReadOnlyDictionary<uint, IReadOnlyList<InclusionShopEntry>> InclusionShops
            => inclusionShops ??= ShopHelper.ReadInclusionShops();

        public IReadOnlyDictionary<uint, IReadOnlyList<uint>> FateShops
            => fateShops ??= ShopHelper.ReadFateShops();

        public List<uint> WithFateShops(uint npcBaseId, List<uint> handlerIds)
        {
            if (!FateShops.TryGetValue(npcBaseId, out var shops))
                return handlerIds;

            var roots = new List<uint>(handlerIds);

            foreach (var shopId in shops)
            {
                if (!roots.Contains(shopId))
                    roots.Add(shopId);
            }

            return roots;
        }

        public VendorHandlerNode? ReadNode(uint handlerId)
        {
            if (readNode != null)
                return readNode(handlerId);

            if (nodes.TryGetValue(handlerId, out var known))
                return known;

            var node = ReadHandlerNode(handlerId);
            nodes[handlerId] = node;
            return node;
        }

        public string NpcName(uint npcBaseId)
        {
            if (!names.TryGetValue(npcBaseId, out var name))
                names[npcBaseId] = name = readNpcName == null ? EventNpcHelper.Name(npcBaseId) : readNpcName(npcBaseId);

            return name;
        }
    }

    private static VendorHandlerNode? ReadHandlerNode(uint handlerId)
    {
        var kind = (EventHandlerContent)(handlerId >> HandlerContentShift);

        switch (kind)
        {
            case EventHandlerContent.Shop when ExcelSheetHelper.TryGetRow<GilShop>(handlerId, out var gilShopRow) && gilShopRow is { } gilShop:
                return new VendorHandlerNode(
                    handlerId,
                    kind,
                    gilShop.Name.ExtractText(),
                    [],
                    gilShop.Quest.RowId,
                    gilShop.AcceptTalk.RowId != 0,
                    new VendorRequirements([], FestivalId: gilShop.FestivalId, FestivalPhase: gilShop.FestivalPhase));

            case EventHandlerContent.SpecialShop when ExcelSheetHelper.TryGetRow<SpecialShop>(handlerId, out var specialShopRow) && specialShopRow is { } specialShop:
                return new VendorHandlerNode(
                    handlerId,
                    kind,
                    specialShop.Name.ExtractText(),
                    [],
                    specialShop.Quest.RowId,
                    false,
                    new VendorRequirements(
                        [],
                        FestivalId: specialShop.RequiredFestival.RowId,
                        FestivalPhase: specialShop.RequiredFestivalPhase,
                        ContentFinderConditionId: specialShop.RequiredContentFinderCondition.RowId,
                        ContentFinderMustBeComplete: specialShop.RequiredContentFinderConditionComplete));

            case EventHandlerContent.InclusionShop when ExcelSheetHelper.TryGetRow<InclusionShop>(handlerId, out var inclusionRow) && inclusionRow is { } inclusion:
                return new VendorHandlerNode(handlerId, kind, inclusion.ShopName.ExtractText(), [], inclusion.UnlockQuest.RowId, false);

            case EventHandlerContent.GrandCompanyShop when ExcelSheetHelper.TryGetRow<GCShop>(handlerId, out var companyRow) && companyRow is { } companyShop:
                return new VendorHandlerNode(
                    handlerId,
                    kind,
                    string.Empty,
                    [],
                    0,
                    false,
                    new VendorRequirements([], GrandCompanyId: companyShop.GrandCompany.RowId));

            case EventHandlerContent.FreeCompanyCreditShop when ExcelSheetHelper.TryGetRow<FccShop>(handlerId, out var creditRow) && creditRow is { } creditShop:
                return new VendorHandlerNode(handlerId, kind, creditShop.Name.ExtractText(), [], 0, false);

            case EventHandlerContent.TopicSelect when ExcelSheetHelper.TryGetRow<TopicSelect>(handlerId, out var topicRow) && topicRow is { } topic:
                return new VendorHandlerNode(handlerId, kind, topic.Name.ExtractText(), [.. topic.Shop.Select(static shop => shop.RowId).Where(static id => id != 0)], 0, false);

            case EventHandlerContent.PreHandler when ExcelSheetHelper.TryGetRow<PreHandler>(handlerId, out var preRow) && preRow is { } pre:
                return new VendorHandlerNode(handlerId, kind, pre.Unknown0.ExtractText(), pre.Target.RowId == 0 ? [] : [pre.Target.RowId], pre.UnlockQuest.RowId, pre.AcceptMessage.RowId != 0);

            case EventHandlerContent.Array when ExcelSheetHelper.TryGetRow<ArrayEventHandler>(handlerId, out var arrayRow) && arrayRow is { } array:
                return new VendorHandlerNode(handlerId, kind, string.Empty, [.. array.Data.Select(static entry => entry.RowId).Where(static id => id != 0)], 0, false);

            case EventHandlerContent.CustomTalk when ExcelSheetHelper.TryGetRow<CustomTalk>(handlerId, out var talkRow) && talkRow is { } talk:
                return new VendorHandlerNode(handlerId, kind, talk.MainOption.ExtractText(), ReadTalkChildren(talk), 0, false);

            default:
                return null;
        }
    }

    // A CustomTalk names its shops in its script arguments and, through SpecialLinks, in CustomTalkNestHandlers.
    private static List<uint> ReadTalkChildren(CustomTalk talk)
    {
        var children = new List<uint>();

        foreach (var script in talk.Script)
        {
            var argument = script.ScriptArg;

            if (argument >= 1u << HandlerContentShift
                && (EventHandlerContent)(argument >> HandlerContentShift) is EventHandlerContent.Shop
                    or EventHandlerContent.SpecialShop
                    or EventHandlerContent.TopicSelect
                    or EventHandlerContent.PreHandler
                    or EventHandlerContent.InclusionShop
                && !children.Contains(argument))
            {
                children.Add(argument);
            }
        }

        if (talk.SpecialLinks.RowId != 0 && ExcelSheetHelper.TryGetSubrows<CustomTalkNestHandlers>(talk.SpecialLinks.RowId, out var nests))
        {
            foreach (var nest in nests)
            {
                if (nest.NestHandler.RowId != 0 && !children.Contains(nest.NestHandler.RowId))
                    children.Add(nest.NestHandler.RowId);
            }
        }

        return children;
    }
}
