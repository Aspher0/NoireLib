namespace NoireLib.Helpers;

internal static class ContextMenuTargetResolver
{
    // A content id belongs to players only, and names a player the client has no object for, such as a chat sender.
    internal static ContextMenuTargetKind Resolve(bool isPlayerObject, bool isCharacterObject, ulong contentId)
    {
        if (isPlayerObject || contentId != 0)
            return ContextMenuTargetKind.Player;

        return isCharacterObject ? ContextMenuTargetKind.Npc : ContextMenuTargetKind.None;
    }
}
