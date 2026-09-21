using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// How one NPC opens one shop: the menu entries to choose in order and the gates on the way. One route covers every
/// line that shop sells.
/// </summary>
/// <param name="NpcBaseId">The ENpcBase row id of the vendor.</param>
/// <param name="NpcName">The vendor's name in the client's language.</param>
/// <param name="ShopId">The GilShop or SpecialShop row id the route ends in.</param>
/// <param name="ContainerShopId">The InclusionShop row id holding the special shop, zero otherwise.</param>
/// <param name="ShopKind">The shop's handler family.</param>
/// <param name="ShopName">The shop's name in the client's language.</param>
/// <param name="Window">The window the shop opens in.</param>
/// <param name="MenuSteps">The entries to choose, first menu first.</param>
/// <param name="Requirements">The gates the shop and the menus wait on.</param>
/// <param name="IsScripted">Whether a CustomTalk script sits on the way, whose menu labels the sheets may not name.</param>
/// <param name="MayShowDialogue">Whether a dialogue can show before the shop opens.</param>
public sealed record VendorRoute(
    uint NpcBaseId,
    string NpcName,
    uint ShopId,
    uint ContainerShopId,
    EventHandlerContent ShopKind,
    string ShopName,
    VendorShopWindow Window,
    IReadOnlyList<VendorMenuStep> MenuSteps,
    VendorRequirements Requirements,
    bool IsScripted,
    bool MayShowDialogue);
