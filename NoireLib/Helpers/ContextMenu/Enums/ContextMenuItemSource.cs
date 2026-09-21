namespace NoireLib.Helpers;

/// <summary>
/// Where the item a context menu opened on was read from.
/// </summary>
public enum ContextMenuItemSource
{
    /// <summary>The menu opened on no item.</summary>
    None,

    /// <summary>An inventory slot, read from the inventory menu.</summary>
    Inventory,

    /// <summary>An item link in the chat log.</summary>
    ChatLink,

    /// <summary>A recipe in the crafting log or one of its material lists.</summary>
    Recipe,

    /// <summary>The item under the cursor when the menu opened, such as a shop line or a market board result.</summary>
    Hovered,
}
