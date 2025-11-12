using System.Collections.Generic;

namespace NoireLib.Models.Gambling;

/// <summary>
/// Represents the result of dealing poker hands to multiple players, including the hands and the updated deck.
/// </summary>
public class PokerDealResult
{
    /// <summary>
    /// Gets the dictionary mapping player numbers to their dealt hands.
    /// </summary>
    public Dictionary<int, List<PlayingCard>> Hands { get; init; } = [];

    /// <summary>
    /// Gets the deck after cards have been dealt from it.
    /// </summary>
    public Deck Deck { get; init; } = null!;

    /// <summary>
    /// Gets the number of players dealt cards.
    /// </summary>
    public int PlayerCount => Hands.Count;

    /// <summary>
    /// Gets the number of cards each player received.
    /// </summary>
    public int CardsPerPlayer => Hands.Count > 0 && Hands.TryGetValue(1, out var firstHand) ? firstHand.Count : 0;
}
