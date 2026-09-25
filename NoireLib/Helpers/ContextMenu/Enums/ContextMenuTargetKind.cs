namespace NoireLib.Helpers;

/// <summary>What kind of character a context menu opened on.</summary>
public enum ContextMenuTargetKind
{
    /// <summary>No character: an item, an empty addon row or a menu opened on nothing.</summary>
    None,

    /// <summary>A character that is not a player: an NPC, a companion, a retainer.</summary>
    Npc,

    /// <summary>A player, in the world or named by a chat line, a party list row or a social list.</summary>
    Player,
}
