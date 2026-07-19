namespace NoireLib.Helpers;

/// <summary>
/// A unit of time, ordered from the smallest to the largest.
/// </summary>
/// <remarks>
/// The order is what <see cref="DurationHelper"/> reads a bare number against: in "1h30" the 30 takes the unit one step
/// below the hour, because that is what someone typing it meant.
/// </remarks>
public enum DurationUnit
{
    /// <summary>Milliseconds, written <c>ms</c>.</summary>
    Milliseconds,

    /// <summary>Seconds, written <c>s</c>, <c>sec</c> or <c>second(s)</c>.</summary>
    Seconds,

    /// <summary>Minutes, written <c>m</c>, <c>min</c> or <c>minute(s)</c>.</summary>
    Minutes,

    /// <summary>Hours, written <c>h</c>, <c>hr</c> or <c>hour(s)</c>.</summary>
    Hours,

    /// <summary>Days, written <c>d</c> or <c>day(s)</c>.</summary>
    Days,
}
