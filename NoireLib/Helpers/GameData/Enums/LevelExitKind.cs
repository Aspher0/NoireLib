namespace NoireLib.Helpers;

/// <summary>
/// What an <see cref="LevelObjectKind.ExitRange"/> trigger volume does when the character walks into it. The two
/// kinds read out of the same object and differ only in whether a territory is named; without the kind carried
/// alongside, one that names none looks like a broken zone line.
/// </summary>
public enum LevelExitKind : byte
{
    /// <summary>The object is not an exit range, or its trigger type is one no level file in the game authors.</summary>
    None,

    /// <summary>
    /// A seamless zone boundary: walking into it loads the territory the object names and lands the character on
    /// that territory's own arrival volume.
    /// </summary>
    ZoneLine,

    /// <summary>
    /// A teleport within the territory the trigger already stands in; it names no destination territory. The game
    /// authors these in facing pairs a short distance apart, each sending the character to where its partner sends
    /// them back from. This is how an underwater passage is crossed; a route that only follows zone lines can never
    /// find one.
    /// </summary>
    IntraZoneTeleport,
}
