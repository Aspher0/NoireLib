namespace NoireLib.ObservedStore;

/// <summary>
/// Who an observation is about.
/// </summary>
public enum ObservationScope : byte
{
    /// <summary>
    /// The observation belongs to one character and is keyed by that character's content id. Two characters
    /// recording the same key hold two separate observations.
    /// </summary>
    Character = 0,

    /// <summary>
    /// The observation is the same whoever saw it, so it is stored once and read by every character. Use this for
    /// facts about the world rather than about a character: a housing interior's layout, a shop's stock, a route.
    /// </summary>
    Shared = 1,
}
