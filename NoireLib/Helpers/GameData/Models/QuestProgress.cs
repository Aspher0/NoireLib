using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// Where a character stands on a set of quests: which are finished, which are running, and how far each running one
/// has got. Reading a known set rather than the whole journal keeps this cheap to call often.
/// </summary>
/// <param name="Completed">The quests that are complete.</param>
/// <param name="Accepted">The quests that are accepted but not yet complete.</param>
/// <param name="Sequence">The sequence step each accepted quest has reached.</param>
public sealed record QuestProgress(
    IReadOnlySet<uint> Completed,
    IReadOnlySet<uint> Accepted,
    IReadOnlyDictionary<uint, byte> Sequence)
{
    /// <summary>Progress with nothing in it, which every lookup misses.</summary>
    public static QuestProgress Empty { get; } = new(new HashSet<uint>(), new HashSet<uint>(), new Dictionary<uint, byte>());
}
