using Dalamud.Game.ClientState.Conditions;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// Why the local player cannot be made to act right now, read by <see cref="CharacterHelper.ReadLocalPlayerOccupancy"/>.
/// Every member is a separate cause and several can hold at once.
/// </summary>
/// <param name="IsPlayerMissing">Whether no local player is loaded.</param>
/// <param name="IsDead">Whether the local player is dead.</param>
/// <param name="IsCasting">Whether the local player is casting.</param>
/// <param name="IsUntargetable">Whether the local player reports itself untargetable.</param>
/// <param name="IsAirborne">Whether the local player is jumping or falling.</param>
/// <param name="ActiveConditions">The occupying condition flags raised, minus the ones the caller ignored.</param>
public sealed record PlayerOccupancy(
    bool IsPlayerMissing,
    bool IsDead,
    bool IsCasting,
    bool IsUntargetable,
    bool IsAirborne,
    IReadOnlyList<ConditionFlag> ActiveConditions)
{
    /// <summary>A player free to act.</summary>
    public static PlayerOccupancy Free { get; } = new(false, false, false, false, false, []);

    /// <summary>No local player loaded.</summary>
    public static PlayerOccupancy NoPlayer { get; } = new(true, false, false, false, false, []);

    /// <summary>Whether any cause holds.</summary>
    public bool IsOccupied
        => IsPlayerMissing || IsDead || IsCasting || IsUntargetable || IsAirborne || ActiveConditions.Count > 0;

    /// <summary>Lists the causes that hold, for a log or a refusal message.</summary>
    /// <returns>The causes separated by commas, such as "airborne, Jumping, InCombat", empty for a free player.</returns>
    public string Describe()
    {
        var causes = new List<string>();

        if (IsPlayerMissing)
            causes.Add("no local player");

        if (IsDead)
            causes.Add("dead");

        if (IsCasting)
            causes.Add("casting");

        if (IsUntargetable)
            causes.Add("untargetable");

        if (IsAirborne)
            causes.Add("airborne");

        foreach (var condition in ActiveConditions)
            causes.Add(condition.ToString());

        return string.Join(", ", causes);
    }
}
