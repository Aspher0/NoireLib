namespace NoireLib.Helpers;

/// <summary>Which openings of the game's context menus an entry belongs to.</summary>
public enum ContextMenuScope
{
    /// <summary>The menu opened on a player, an NPC, a party list row, a chat name or anything an addon lists.</summary>
    Default,

    /// <summary>The menu opened on an inventory item.</summary>
    Inventory,

    /// <summary>Both menus.</summary>
    Everywhere,

    /// <summary>Any menu opened on an item: an inventory slot, a chat link, a shop line, a recipe or an item list.</summary>
    Item,

    /// <summary>Any menu opened on a character: a player or an NPC.</summary>
    Character,

    /// <summary>Any menu opened on a player, in the world or named by a chat line, a party list row or a social list.</summary>
    Player,
}
