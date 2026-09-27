using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Animations.Helpers;

/// <summary>
/// Builds the game path a human skeleton's copy of an animation lives at, and the fallback chain to try when a
/// skeleton has no copy of its own. The chains come from <see cref="PapLoadTable"/>, which must have been warmed.
/// </summary>
public static class EmotePathHelper
{
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
    /// The skeletons to try an animation on, the skeleton itself first, as derived by <see cref="PapLoadTable"/>.
    /// Falls back to the id alone when the table is unread or has no chain for it.
    /// </summary>
    /// <param name="skeletonId">The human skeleton id to start from.</param>
    /// <returns>The chain to walk, closest first.</returns>
    public static IReadOnlyList<string> GetFallbackOrder(string skeletonId)
        => PapLoadTable.Current?.FallbackOrderFor(skeletonId) ?? [skeletonId];

    /// <summary>
    /// The skeletons to try one animation on, closest first. When <see cref="PapLoadTable"/> names the skeleton
    /// the game loads the file from, that skeleton comes first, and alone when the game ships its copy.
    /// </summary>
    /// <param name="skeletonId">The human skeleton id to start from.</param>
    /// <param name="relativePath">The path under the skeleton's a0001 folder, such as "bt_common/emote/hum.pap".</param>
    /// <returns>The chain to walk, closest first.</returns>
    public static IReadOnlyList<string> GetFallbackOrder(string skeletonId, string relativePath)
    {
        var chain = GetFallbackOrder(skeletonId);

        if (PapLoadTable.Current?.SkeletonFor(skeletonId, relativePath) is not { } loaded)
            return chain;

        if (NoireService.DataManager.FileExists(GetSkeletonPath(loaded, relativePath)))
            return [loaded];

        return [loaded, .. chain.Where(skeleton => !string.Equals(skeleton, loaded, StringComparison.OrdinalIgnoreCase))];
    }

    /// <summary>
    /// Narrows a chain built by <see cref="GetFallbackOrder(string)"/> to one animation, through
    /// <see cref="GetFallbackOrder(string, string)"/> on the chain's first skeleton.
    /// </summary>
    /// <param name="fallbackSkeletons">The chain, the drawn skeleton first.</param>
    /// <param name="relativePath">The path under the skeleton's a0001 folder.</param>
    /// <returns>The chain to walk for that animation.</returns>
    public static IReadOnlyList<string> GetFallbackOrder(IReadOnlyList<string> fallbackSkeletons, string relativePath)
        => fallbackSkeletons is { Count: > 0 } ? GetFallbackOrder(fallbackSkeletons[0], relativePath) : [];

    /// <summary>Every playable human skeleton <see cref="PapLoadTable"/> knows, in id order, or none while it is unread.</summary>
    public static IReadOnlyList<string> AllHumanSkeletons => PapLoadTable.Current?.HumanSkeletons ?? [];

    /// <summary>
    /// Walks a fallback chain and returns the full game path of the first skeleton that has the animation.
    /// </summary>
    /// <param name="relativePath">The path under a skeleton's a0001 folder, such as "bt_common/emote/beesknees.pap".</param>
    /// <param name="fallbackSkeletons">The chain to walk, closest first, as <see cref="GetFallbackOrder(string)"/> returns it.</param>
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
