namespace NoireLib.Models.Gambling;

/// <summary>
/// Represents the result of a roulette wheel spin.
/// </summary>
public class RouletteResult
{
    /// <summary>
    /// Gets the number that was landed on (0-36, or 37 for double zero).
    /// </summary>
    public int Number { get; init; }

    /// <summary>
    /// Gets the color of the number (Red, Black, or Green for 0/00).
    /// </summary>
    public RouletteColor Color { get; init; }

    /// <summary>
    /// Returns true if the number is even (excluding 0 and 00).
    /// </summary>
    public bool IsEven => Number > 0 && Number <= 36 && Number % 2 == 0;

    /// <summary>
    /// Returns true if the number is odd.
    /// </summary>
    public bool IsOdd => Number > 0 && Number <= 36 && Number % 2 == 1;

    /// <summary>
    /// Returns true if the number is in the low range (1-18).
    /// </summary>
    public bool IsLow => Number >= 1 && Number <= 18;

    /// <summary>
    /// Returns true if the number is in the high range (19-36).
    /// </summary>
    public bool IsHigh => Number >= 19 && Number <= 36;

    /// <summary>
    /// Returns true if the number is zero or double zero.
    /// </summary>
    public bool IsZero => Number == 0 || Number == 37;

    /// <summary>
    /// Gets the display string of the result (e.g., "17 (Black)").
    /// </summary>
    public string DisplayString => Number == 37 ? "00 (Green)" : $"{Number} ({Color})";

    /// <summary>
    /// A string representation of the roulette result.
    /// </summary>
    /// <returns>The display string of the roulette result.</returns>
    public override string ToString() => DisplayString;
}
