using System;
using System.Collections.Generic;

namespace NoireLib.Animations.Helpers;

/// <summary>
/// Builds the game path a human skeleton's copy of an animation lives at, and the fallback chain to try when a
/// skeleton has no copy of its own.
/// </summary>
public static class EmotePathHelper
{
    /// <summary>
    /// Which other skeletons' animations a skeleton without its own copy can borrow, closest first, with most
    /// chains ending in c0101, the skeleton every human animation exists for.
    /// </summary>
    private static readonly Dictionary<string, string[]> HumanSkeletonFallbacks = new(StringComparer.OrdinalIgnoreCase)
    {
        ["c0101"] = ["c0101"],
        ["c0201"] = ["c0201", "c0801", "c0101"],
        ["c0301"] = ["c0301", "c0101"],
        ["c0401"] = ["c0401", "c0801", "c0101"],
        ["c0501"] = ["c0501", "c0101"],
        ["c0601"] = ["c0601", "c0801", "c0101"],
        ["c0701"] = ["c0701", "c0101"],
        ["c0801"] = ["c0801", "c0101"],
        ["c0901"] = ["c0901", "c1501", "c0101"],
        ["c1001"] = ["c1001", "c0801", "c0101"],
        ["c1101"] = ["c1101", "c0101"],
        ["c1201"] = ["c1201", "c1101", "c0101"],
        ["c1301"] = ["c1301", "c0101"],
        ["c1401"] = ["c1401", "c0801", "c0101"],
        ["c1501"] = ["c1501", "c0901", "c0101"],
        ["c1601"] = ["c1601", "c0801", "c0101"],
        ["c1701"] = ["c1701", "c0101"],
        ["c1801"] = ["c1801", "c0801", "c0101"],
    };

    /// <summary>The game path a skeleton's copy of an animation lives at.</summary>
    /// <param name="skeletonId">The human skeleton id, such as "c0801".</param>
    /// <param name="relativePath">The path under the skeleton's a0001 folder, such as "bt_common/emote/beesknees.pap".</param>
    /// <returns>The full game path.</returns>
    public static string GetSkeletonPath(string skeletonId, string relativePath) =>
        $"chara/human/{skeletonId}/animation/a0001/{relativePath}";

    /// <summary>
    /// Normalizes a raw customize model id into the "cNNNN" skeleton id it names, such as 101 becoming "c0101".
    /// </summary>
    /// <param name="modelId">The raw customize model id.</param>
    /// <returns>The skeleton id.</returns>
    public static string NormalizeHumanSkeletonId(int modelId)
        => $"c{modelId:D4}";

    /// <summary>
    /// The skeletons to try an animation on, closest first, falling back to the id alone when the table has no
    /// chain for it.
    /// </summary>
    /// <param name="skeletonId">The human skeleton id to start from.</param>
    /// <returns>The chain to walk, closest first.</returns>
    public static IReadOnlyList<string> GetFallbackOrder(string skeletonId)
        => HumanSkeletonFallbacks.TryGetValue(skeletonId, out var fallbacks)
            ? fallbacks
            : [skeletonId];

    /// <summary>Every human skeleton the fallback table knows, in table order.</summary>
    public static IReadOnlyList<string> AllHumanSkeletons { get; } = [.. HumanSkeletonFallbacks.Keys];

    /// <summary>
    /// Walks a fallback chain and returns the full game path of the first skeleton that has the animation.
    /// </summary>
    /// <param name="relativePath">The path under a skeleton's a0001 folder, such as "bt_common/emote/beesknees.pap".</param>
    /// <param name="fallbackSkeletons">The chain to walk, closest first, as <see cref="GetFallbackOrder"/> returns it.</param>
    /// <param name="exists">Predicate answering whether a given full game path can be served.</param>
    /// <returns>The first path <paramref name="exists"/> accepts, or null when none does.</returns>
    public static string? FindExistingPath(
        string relativePath, IReadOnlyList<string> fallbackSkeletons, Func<string, bool> exists)
    {
        ArgumentNullException.ThrowIfNull(exists);

        if (fallbackSkeletons == null)
            return null;

        foreach (var skeleton in fallbackSkeletons)
        {
            var path = GetSkeletonPath(skeleton, relativePath);

            if (exists(path))
                return path;
        }

        return null;
    }

    /// <summary>
    /// Walks a fallback chain and returns the full game path of the first skeleton whose copy either a mod provides
    /// or the game ships, asking both sources per skeleton so a nearer mod wins over a further stock copy.
    /// </summary>
    /// <param name="relativePath">The path under a skeleton's a0001 folder.</param>
    /// <param name="fallbackSkeletons">The chain to walk, closest first.</param>
    /// <param name="providedByMod">Predicate answering whether a mod redirects the given full game path.</param>
    /// <param name="existsInGame">Predicate answering whether the game's own files contain the given full game path.</param>
    /// <returns>The first path either source accepts, or null when none does.</returns>
    public static string? FindExistingPath(
        string relativePath, IReadOnlyList<string> fallbackSkeletons,
        Func<string, bool> providedByMod, Func<string, bool> existsInGame)
    {
        ArgumentNullException.ThrowIfNull(providedByMod);
        ArgumentNullException.ThrowIfNull(existsInGame);

        return FindExistingPath(relativePath, fallbackSkeletons, path => providedByMod(path) || existsInGame(path));
    }
}
