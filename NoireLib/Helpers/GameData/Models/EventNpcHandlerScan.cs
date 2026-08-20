using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// One pass over the <c>ENpcBase</c> sheet, indexed both ways: which event handlers an NPC runs, and which NPCs run
/// a given handler.
/// </summary>
/// <param name="HandlersByNpc">The handler ids each ENpcBase row references.</param>
/// <param name="NpcsByHandler">The ENpcBase rows that reference each handler id, in ascending row order.</param>
public sealed record EventNpcHandlerScan(
    IReadOnlyDictionary<uint, IReadOnlyList<uint>> HandlersByNpc,
    IReadOnlyDictionary<uint, IReadOnlyList<uint>> NpcsByHandler)
{
    /// <summary>An empty scan, which every lookup misses.</summary>
    public static EventNpcHandlerScan Empty { get; } = new(
        new Dictionary<uint, IReadOnlyList<uint>>(),
        new Dictionary<uint, IReadOnlyList<uint>>());
}
