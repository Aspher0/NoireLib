using NoireLib.Helpers;
using System;
using System.Collections.Generic;

namespace NoireLib.Models.Gambling;

/// <summary>
/// Represents a deck of playing cards that can be used for card games.
/// Supports multiple decks and tracks remaining cards with no replacement.
/// </summary>
public class Deck
{
    private List<PlayingCard> _remainingCards { get; set; }

    /// <summary>
    /// Gets the initial number of decks this Deck was created with.
    /// </summary>
    public int DeckCount { get; }

    /// <summary>
    /// Gets the number of cards remaining in the deck.
    /// </summary>
    public int RemainingCardsCount => _remainingCards.Count;

    /// <summary>
    /// Gets the total number of cards initially in the deck (52 * DeckCount).
    /// </summary>
    public int TotalCardsCount { get; }

    /// <summary>
    /// Gets whether the deck has any cards remaining.
    /// </summary>
    public bool HasCards => _remainingCards.Count > 0;

    /// <summary>
    /// Gets the percentage of cards remaining in the deck (0.0 to 1.0).
    /// </summary>
    public double PercentageRemaining => TotalCardsCount > 0 ? (double)RemainingCardsCount / TotalCardsCount : 0.0;

    /// <summary>
    /// Determines whether the deck should automatically refill when low on cards.
    /// </summary>
    public bool ShouldAutoRefill { get; set; } = true;

    /// <summary>
    /// The threshold percentage (0.0 to 1.0) at which the deck will auto-refill if enabled.
    /// </summary>
    public double AutoRefillThresholdPercentage { get; set; } = 0;

    /// <summary>
    /// Initializes a new deck with the specified cards.
    /// </summary>
    /// <param name="cards">The cards to initialize the deck with.</param>
    /// <param name="deckCount">The number of standard decks represented.</param>
    internal Deck(List<PlayingCard> cards, int deckCount)
    {
        _remainingCards = cards;
        DeckCount = deckCount;
        TotalCardsCount = cards.Count;
    }

    /// <summary>
    /// Generates a new deck with the specified number of standard 52-card decks.
    /// </summary>
    /// <param name="deckCount">The number of standard decks to include in the deck.</param>
    /// <param name="shouldShuffle">Determines whether the deck should be shuffled upon creation.</param>
    /// <returns></returns>
    public static Deck New(int deckCount = 1, bool shouldShuffle = true)
    {
        return RandomGenerator.GenerateCardDeck(deckCount, shouldShuffle);
    }

    /// <summary>
    /// Draws a single card from the deck. The card is removed from the deck.
    /// </summary>
    /// <returns>A randomly drawn card.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the deck has no cards remaining.</exception>
    public PlayingCard DrawCard()
    {
        CheckRefillDeck();

        if (_remainingCards.Count == 0)
            throw new InvalidOperationException("Cannot draw from an empty deck. No cards remaining.");

        int index = RandomGenerator.GenerateRandomInt(_remainingCards.Count);
        var card = _remainingCards[index];
        _remainingCards.RemoveAt(index);

        // Maybe we want to check after drawing as well?
        //CheckRefillDeck();

        return card;
    }

    /// <summary>
    /// Draws multiple cards from the deck. The cards are removed from the deck.
    /// </summary>
    /// <param name="count">The number of cards to draw.</param>
    /// <returns>A list of randomly drawn cards.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when count is less than 0 or greater than remaining cards and if <see cref="ShouldAutoRefill"/> is false.</exception>
    /// <exception cref="InvalidOperationException">Thrown when attempting to draw from an empty deck and if <see cref="ShouldAutoRefill"/> is false.</exception>
    public List<PlayingCard> DrawCards(int count)
    {
        if (!ShouldAutoRefill)
        {
            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than or equal to 0.");

            if (count > _remainingCards.Count)
                throw new ArgumentOutOfRangeException(nameof(count),
                   $"Cannot draw {count} cards. Only {_remainingCards.Count} cards remaining in the deck.");
        }

        var drawnCards = new List<PlayingCard>(count);
        for (int i = 0; i < count; i++)
        {
            drawnCards.Add(DrawCard());
        }

        return drawnCards;
    }

    /// <summary>
    /// Shuffles the remaining cards in the deck.
    /// </summary>
    public void Shuffle()
    {
        _remainingCards = RandomGenerator.Shuffle(_remainingCards);
    }

    /// <summary>
    /// Determines whether the deck needs to be refilled based on the threshold percentage.
    /// </summary>
    /// <returns>True if the percentage of remaining cards is less than the threshold; otherwise, false.</returns>
    public bool NeedsRefill()
    {
        if (!ShouldAutoRefill)
            return false;

        if (AutoRefillThresholdPercentage < 0.0 || AutoRefillThresholdPercentage > 1.0)
            throw new ArgumentOutOfRangeException(nameof(AutoRefillThresholdPercentage), "Threshold percentage must be between 0.0 and 1.0.");

        return PercentageRemaining <= AutoRefillThresholdPercentage;
    }

    /// <summary>
    /// Will check if the deck needs refilling and refills it if necessary.
    /// </summary>
    public void CheckRefillDeck()
    {
        if (NeedsRefill())
            RefillDeck();
    }

    /// <summary>
    /// Refills the deck, perfect for when there's too few cards left.
    /// </summary>
    public void RefillDeck()
    {
        var newDeck = RandomGenerator.GenerateCardDeck(DeckCount, true);
        _remainingCards = newDeck._remainingCards;
    }

    /// <summary>
    /// Peeks at the remaining cards without removing them.
    /// </summary>
    /// <returns>A read-only copy of the remaining cards.</returns>
    public IReadOnlyList<PlayingCard> PeekRemainingCards()
    {
        return _remainingCards.AsReadOnly();
    }

    /// <summary>
    /// Creates a copy of this deck with the same remaining cards.
    /// </summary>
    /// <returns>A new Deck instance with a copy of the remaining cards.</returns>
    public Deck Clone()
    {
        return new Deck(_remainingCards, DeckCount);
    }

    /// <summary>
    /// Returns a string representation of the deck showing deck count and remaining cards.
    /// </summary>
    public override string ToString()
    {
        return $"Deck ({DeckCount} deck{(DeckCount != 1 ? "s" : "")}): {RemainingCardsCount}/{TotalCardsCount} cards remaining ({PercentageRemaining:P1})";
    }
}
