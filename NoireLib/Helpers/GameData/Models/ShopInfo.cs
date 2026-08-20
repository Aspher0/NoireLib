using FFXIVClientStructs.FFXIV.Client.Game.Event;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>A shop and everything it offers.</summary>
/// <param name="ShopId">The shop row, which is also the NPC's event handler id.</param>
/// <param name="Kind">Which kind of shop it is.</param>
/// <param name="Name">The shop's name, usually empty.</param>
/// <param name="Offers">Its offers, in sheet order.</param>
public sealed record ShopInfo(uint ShopId, EventHandlerContent Kind, string Name, IReadOnlyList<ShopOffer> Offers);
