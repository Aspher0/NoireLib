using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Models.Gambling;

/// <summary>
/// Represents the result of dealing a Blackjack hand, including the dealt cards and the updated deck.
/// </summary>
public class BlackjackDealResult
{
    /// <summary>
    /// Gets the dealt hand of Blackjack cards.
    /// </summary>
    public List<PlayingCard> Hand { get; init; } = [];

    /// <summary>
    /// Gets the deck after cards have been dealt from it.
    /// </summary>
    public Deck Deck { get; init; } = null!;

    /// <summary>
    /// Gets the total value of the hand (treating all Aces as 11).
    /// </summary>
    public int HandValue => CalculateHandValue();

    /// <summary>
    /// Gets the best possible value of the hand, accounting for Aces as 1 or 11.
    /// </summary>
    public int BestHandValue => CalculateBestHandValue();

    /// <summary>
    /// Returns true if the hand is a "Blackjack" (Ace + 10-value card with exactly 2 cards).
    /// </summary>
    public bool IsBlackjack => Hand.Count == 2 && BestHandValue == 21;

    /// <summary>
    /// Returns true if the hand is "bust" (value over 21).
    /// </summary>
    public bool IsBust => BestHandValue > 21;

    private int CalculateHandValue()
    {
        return Hand.Sum(card => card.Value);
    }

    private int CalculateBestHandValue()
    {
        int total = 0;
        int aceCount = 0;

        foreach (var card in Hand)
        {
            if (card.IsAce)
            {
                aceCount++;
                total += 11;
            }
            else
            {
                total += card.Value;
            }
        }

        // Convert Aces from 11 to 1 as needed to avoid bust
        while (total > 21 && aceCount > 0)
        {
            total -= 10; // Convert one Ace from 11 to 1
            aceCount--;
        }

        return total;
    }
}
