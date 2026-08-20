using System;

namespace NoireLib.Helpers;

/// <summary>One weather window: what the weather is, when it starts, and how long it lasts.</summary>
/// <param name="WeatherId">The Weather row id.</param>
/// <param name="TerritoryId">The territory the window was computed for.</param>
/// <param name="Start">The real moment the window begins.</param>
/// <param name="End">The real moment the window ends, which is the next window's start.</param>
/// <param name="Chance">
/// The roll the window was decided by, 0 to 99. Two territories sharing a moment share this number and can still
/// land on different weather, because each has its own rate table.
/// </param>
public readonly record struct WeatherWindow(
    uint WeatherId,
    uint TerritoryId,
    DateTimeOffset Start,
    DateTimeOffset End,
    int Chance)
{
    /// <summary>The window's length, which is always one weather window.</summary>
    public TimeSpan Duration => End - Start;

    /// <summary>Whether a real moment falls inside this window.</summary>
    /// <param name="realTime">The moment to test.</param>
    /// <returns>True when it falls inside.</returns>
    public bool Contains(DateTimeOffset realTime) => realTime >= Start && realTime < End;
}
