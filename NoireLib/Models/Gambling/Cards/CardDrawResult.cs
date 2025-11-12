namespace NoireLib.Models.Gambling;

/// <summary>
/// Represents the result of drawing a single card from a deck.
/// </summary>
public class CardDrawResult
{
    /// <summary>
    /// Gets the card that was drawn.
    /// </summary>
    public PlayingCard Card { get; init; } = null!;

    /// <summary>
    /// Gets the deck after the card has been drawn from it.
    /// </summary>
    public Deck Deck { get; init; } = null!;
}
