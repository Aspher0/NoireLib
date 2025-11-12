using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Models.Gambling;

/// <summary>
/// Represents the result of a dice roll.
/// </summary>
public class DiceRoll
{
    /// <summary>
    /// Gets the individual dice values rolled.
    /// </summary>
    public List<int> Dice { get; init; } = new();

    /// <summary>
    /// Gets the total sum of all dice rolled.
    /// </summary>
    public int Total => Dice.Sum();

    /// <summary>
    /// Gets the number of dice rolled.
    /// </summary>
    public int Count => Dice.Count;

    /// <summary>
    /// Gets the highest value rolled.
    /// </summary>
    public int Max => Dice.Count > 0 ? Dice.Max() : 0;

    /// <summary>
    /// Gets the lowest value rolled.
    /// </summary>
    public int Min => Dice.Count > 0 ? Dice.Min() : 0;

    /// <summary>
    /// Returns true if all dice show the same value.
    /// </summary>
    public bool IsAllSame => Dice.Count > 0 && Dice.All(d => d == Dice[0]);

    /// <summary>
    /// Returns the display string of the dice roll (e.g., "[3, 5, 2] = 10").
    /// </summary>
    public string DisplayString => $"[{string.Join(", ", Dice)}] = {Total}";

    /// <summary>
    /// A string representation of the dice roll.
    /// </summary>
    /// <returns>The display string of the dice roll.</returns>
    public override string ToString() => DisplayString;
}
