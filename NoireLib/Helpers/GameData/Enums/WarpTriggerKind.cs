namespace NoireLib.Helpers;

/// <summary>
/// What kind of placed interactable triggers a warp.
/// </summary>
public enum WarpTriggerKind : byte
{
    /// <summary>An event NPC the character talks to, such as a ferry ticketer or a lift attendant.</summary>
    EventNpc,

    /// <summary>
    /// An event object the character touches, such as the "exit to somewhere" object that leaves an instance.
    /// </summary>
    EventObject,
}
