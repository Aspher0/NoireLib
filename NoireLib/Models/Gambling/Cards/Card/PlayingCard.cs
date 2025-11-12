namespace NoireLib.Models.Gambling;

/// <summary>
/// Represents a playing card used in card games such as Blackjack and Poker.
/// </summary>
public class PlayingCard
{
    /// <summary>
    /// Gets the rank of the card (Ace, 2-10, Jack, Queen, King).
    /// </summary>
    public CardRank Rank { get; init; }

    /// <summary>
    /// Gets the suit of the card (Hearts, Diamonds, Clubs, Spades).
    /// </summary>
    public CardSuit Suit { get; init; }

    /// <summary>
    /// Gets the blackjack value of the card. Aces return 11 (can be treated as 1 in gameplay logic).
    /// Face cards (Jack, Queen, King) return 10.
    /// </summary>
    public int Value => Rank switch
    {
        CardRank.Ace => 11,
        CardRank.Two => 2,
        CardRank.Three => 3,
        CardRank.Four => 4,
        CardRank.Five => 5,
        CardRank.Six => 6,
        CardRank.Seven => 7,
        CardRank.Eight => 8,
        CardRank.Nine => 9,
        CardRank.Ten => 10,
        CardRank.Jack => 10,
        CardRank.Queen => 10,
        CardRank.King => 10,
        _ => 0
    };

    /// <summary>
    /// Gets the display name of the card (e.g., "Ace of Spades", "King of Hearts").
    /// </summary>
    public string DisplayName => $"{Rank} of {Suit}";

    /// <summary>
    /// Gets the short notation of the card (e.g., "A♠", "K♥").
    /// </summary>
    public string ShortNotation => $"{GetRankSymbol()}{GetSuitSymbol()}";

    /// <summary>
    /// Returns true if the card is a face card (Jack, Queen, or King).
    /// </summary>
    public bool IsFaceCard => Rank is CardRank.Jack or CardRank.Queen or CardRank.King;

    /// <summary>
    /// Returns true if the card is an Ace.
    /// </summary>
    public bool IsAce => Rank == CardRank.Ace;

    private string GetRankSymbol() => Rank switch
    {
        CardRank.Ace => "A",
        CardRank.Two => "2",
        CardRank.Three => "3",
        CardRank.Four => "4",
        CardRank.Five => "5",
        CardRank.Six => "6",
        CardRank.Seven => "7",
        CardRank.Eight => "8",
        CardRank.Nine => "9",
        CardRank.Ten => "10",
        CardRank.Jack => "J",
        CardRank.Queen => "Q",
        CardRank.King => "K",
        _ => "?"
    };

    private string GetSuitSymbol() => Suit switch
    {
        CardSuit.Hearts => "♥",
        CardSuit.Diamonds => "♦",
        CardSuit.Clubs => "♣",
        CardSuit.Spades => "♠",
        _ => "?"
    };

    /// <summary>
    /// ToString override to return the short notation of the card.
    /// </summary>
    /// <returns>The short notation of the card.</returns>
    public override string ToString() => ShortNotation;
}
