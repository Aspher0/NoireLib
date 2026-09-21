using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// How to buy one item from one NPC: the menu entries to choose in order, the shop they lead to and the line it sells.
/// A menu the game skips, such as an NPC with a single entry, is skipped at run time.
/// </summary>
/// <param name="NpcBaseId">The ENpcBase row id of the vendor.</param>
/// <param name="NpcName">The vendor's name in the client's language.</param>
/// <param name="ShopId">The GilShop or SpecialShop row id that sells the item.</param>
/// <param name="ContainerShopId">The InclusionShop row id holding the special shop, zero otherwise.</param>
/// <param name="ShopKind">The shop's handler family.</param>
/// <param name="ShopName">The shop's name in the client's language.</param>
/// <param name="Window">The window the shop opens in.</param>
/// <param name="MenuSteps">The entries to choose, first menu first.</param>
/// <param name="Offer">The line selling the item.</param>
/// <param name="Requirements">The gates the shop, the menus and the line wait on.</param>
/// <param name="IsScripted">Whether a CustomTalk script sits on the way, whose menu labels the sheets may not name.</param>
/// <param name="MayShowDialogue">Whether a dialogue can show before the shop opens.</param>
public sealed record VendorPurchasePath(
    uint NpcBaseId,
    string NpcName,
    uint ShopId,
    uint ContainerShopId,
    EventHandlerContent ShopKind,
    string ShopName,
    VendorShopWindow Window,
    IReadOnlyList<VendorMenuStep> MenuSteps,
    ShopOffer Offer,
    VendorRequirements Requirements,
    bool IsScripted,
    bool MayShowDialogue)
{
    /// <summary>The quests the shop, the menus and the line wait on.</summary>
    public IReadOnlyList<uint> RequiredQuestIds => Requirements.QuestIds;

    /// <summary>Whether the path ends in a gil shop line reached through named menus.</summary>
    public bool CanBuyWithGilShop => Window == VendorShopWindow.Shop && !IsScripted && Offer.IsGilPurchase;

    /// <summary>Whether the path ends in a line paid for in currencies. No runner drives one yet.</summary>
    public bool IsCurrencyExchange => Offer.PurchaseKind == PurchaseKind.CurrencyExchange;
}
