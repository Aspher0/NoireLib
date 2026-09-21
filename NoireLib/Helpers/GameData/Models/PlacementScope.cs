using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Helpers;

/// <summary>
/// The territories <see cref="WorldObjectHelper"/> reads placements in. Rows that share one place, such as a zone and its quest
/// battle copies, fold onto one territory and are read once.
/// </summary>
public sealed class PlacementScope
{
    private readonly Func<IEnumerable<uint>> readTerritories;

    private PlacementScope(string description, Func<IEnumerable<uint>> readTerritories)
    {
        Description = description;
        this.readTerritories = readTerritories;
    }

    /// <summary>Every real territory, answered from the world index.</summary>
    public static PlacementScope World { get; } = new("world", static () => TerritoryHelper.ReadAll().Select(static entry => entry.TerritoryId));

    /// <summary>What the scope covers, for logs and readouts.</summary>
    public string Description { get; }

    /// <summary>Reads one territory.</summary>
    /// <param name="territoryId">The TerritoryType row id.</param>
    /// <returns>The scope.</returns>
    public static PlacementScope Territory(uint territoryId)
        => new($"territory {territoryId}", () => [territoryId]);

    /// <summary>Reads several territories.</summary>
    /// <param name="territoryIds">The TerritoryType row ids.</param>
    /// <returns>The scope.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="territoryIds"/> is null.</exception>
    public static PlacementScope Territories(IEnumerable<uint> territoryIds)
    {
        ArgumentNullException.ThrowIfNull(territoryIds);

        var copy = territoryIds.ToArray();
        return new($"{copy.Length} territories", () => copy);
    }

    /// <summary>
    /// Reads every territory whose place, zone or region name matches, in the client's language. "Limsa Lominsa"
    /// reads both decks, "The Goblet" reads the district.
    /// </summary>
    /// <param name="name">The name to look for.</param>
    /// <param name="match">How the name is compared.</param>
    /// <returns>The scope.</returns>
    /// <exception cref="ArgumentException">If <paramref name="name"/> is null or blank.</exception>
    public static PlacementScope Place(string name, NameMatch match = NameMatch.Contains)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new($"place '{name}'", () => TerritoryHelper.FindByPlaceName(name, match));
    }

    /// <summary>Resolves the scope to the territories it reads, variants folded, in ascending order.</summary>
    /// <returns>The TerritoryType row ids.</returns>
    public IReadOnlyList<uint> ResolveTerritories()
        => WorldObjectHelper.FoldVariants(readTerritories());
}
