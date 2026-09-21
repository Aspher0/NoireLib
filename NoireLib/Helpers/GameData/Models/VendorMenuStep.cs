using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace NoireLib.Helpers;

/// <summary>One entry to choose in a vendor's selection menu on the way to a shop.</summary>
/// <param name="HandlerId">The event handler the entry runs.</param>
/// <param name="Kind">The handler family, such as a shop, a TopicSelect submenu or a PreHandler.</param>
/// <param name="Label">The entry label in the client's language. Empty when the sheets name none.</param>
public readonly record struct VendorMenuStep(uint HandlerId, EventHandlerContent Kind, string Label);
