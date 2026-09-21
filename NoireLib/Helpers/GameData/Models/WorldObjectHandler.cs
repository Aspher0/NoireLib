using FFXIVClientStructs.FFXIV.Client.Game.Event;

namespace NoireLib.Helpers;

/// <summary>One event handler a world object runs, with what the sheets say about it.</summary>
/// <param name="HandlerId">The handler row id. Its high word is the handler family.</param>
/// <param name="Content">The handler family, such as a shop, a CustomTalk or a warp.</param>
/// <param name="ParentHandlerId">The array handler this one was listed in, zero when the object runs it directly.</param>
/// <param name="ScriptName">The CustomTalk script name, never localised. Empty for other families.</param>
/// <param name="ShopName">The shop's own name for a gil or special shop, usually empty.</param>
/// <param name="ShopOfferCount">How many offers the shop lists, zero for other families.</param>
/// <param name="Warp">The warp definition for a warp handler, null for other families.</param>
public readonly record struct WorldObjectHandler(
    uint HandlerId,
    EventHandlerContent Content,
    uint ParentHandlerId,
    string ScriptName,
    string ShopName,
    int ShopOfferCount,
    WarpDefinition? Warp);
