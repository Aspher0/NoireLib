using NoireLib.Models.Gambling;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace NoireLib.Helpers;

/// <summary>
/// A static helper class for generating cryptographically secure random outputs such as GUIDs, strings, numbers, bytes, and more.
/// All methods use <see cref="RandomNumberGenerator"/> for cryptographic security.
/// </summary>
public static class RandomGenerator
{
    /// <summary>
    /// Generates a new GUID string with optional hyphen ("-") removal.
    /// </summary>
    /// <param name="removeHyphens">If true, removes hyphens from the GUID string.</param>
    /// <returns>The generated GUID string.</returns>
    public static string GenerateGuidString(bool removeHyphens = false)
    {
        return removeHyphens ? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString();
    }

    /// <summary>
    /// Generates a random string based on specified criteria.
    /// </summary>
    /// <param name="length">The desired length of the random string. Can change based on <paramref name="moreEntropy"/></param>
    /// <param name="moreEntropy">Adds more entropy to the string by prepending a unique prefix based on a GUID. This will increase the length of the string by 10 characters.</param>
    /// <param name="lowercase">Defines if lowercase letters should be included.</param>
    /// <param name="uppercase">Defines if uppercase letters should be included.</param>
    /// <param name="digits">Defines if digits should be included.</param>
    /// <param name="special">Defines if special characters should be included. Special characters includes "-_#@~|[]{}=+".</param>
    /// <returns>A randomly generated string.</returns>
    public static string GenerateRandomString(int length = 50, bool moreEntropy = false, bool lowercase = true, bool uppercase = true, bool digits = true, bool special = true)
    {
        length = (length <= 0) ? 50 : length;

        List<char> allowedChars = new List<char>();
        if (lowercase)
            allowedChars.AddRange("abcdefghijklmnopqrstuvwxyz".ToCharArray());
        if (uppercase)
            allowedChars.AddRange("ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray());
        if (digits)
            allowedChars.AddRange("0123456789".ToCharArray());
        if (special)
            allowedChars.AddRange("-_#@~|[]{}=+".ToCharArray());

        var result = new StringBuilder(length);

        if (moreEntropy)
        {
            string uniquePrefix = GenerateGuidString(true).Substring(0, 10);
            result.Append(uniquePrefix);
        }

        for (int i = result.Length; i < length; i++)
        {
            int index = RandomNumberGenerator.GetInt32(allowedChars.Count);
            result.Append(allowedChars[index]);
        }

        return result.ToString();
    }

    /// <summary>
    /// Generates a cryptographically secure random integer between 0 (inclusive) and <paramref name="maxValue"/> (exclusive or inclusive based on <paramref name="inclusiveMax"/>).
    /// </summary>
    /// <param name="maxValue">The upper bound of the random number to be generated. Must be greater than 0.</param>
    /// <param name="inclusiveMax">If true, <paramref name="maxValue"/> is inclusive; otherwise, it is exclusive.</param>
    /// <returns>A random integer between 0 (inclusive) and <paramref name="maxValue"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxValue"/> is less than or equal to 0.</exception>
    public static int GenerateRandomInt(int maxValue, bool inclusiveMax = false)
    {
        if (maxValue <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be greater than 0.");

        return inclusiveMax ? RandomNumberGenerator.GetInt32(maxValue + 1) : RandomNumberGenerator.GetInt32(maxValue);
    }

    /// <summary>
    /// Generates a cryptographically secure random integer between <paramref name="minValue"/> (inclusive) and <paramref name="maxValue"/> (exclusive or inclusive based on <paramref name="inclusiveMax"/>).
    /// </summary>
    /// <param name="minValue">The inclusive lower bound of the random number to be generated.</param>
    /// <param name="maxValue">The upper bound of the random number to be generated. Must be greater than <paramref name="minValue"/>.</param>
    /// <param name="inclusiveMax">If true, <paramref name="maxValue"/> is inclusive; otherwise, it is exclusive.</param>
    /// <returns>A random integer between <paramref name="minValue"/> (inclusive) and <paramref name="maxValue"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxValue"/> is less than or equal to <paramref name="minValue"/>.</exception>
    public static int GenerateRandomInt(int minValue, int maxValue, bool inclusiveMax = false)
    {
        if (maxValue <= minValue)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be greater than minValue.");

        return inclusiveMax ? RandomNumberGenerator.GetInt32(minValue, maxValue + 1) : RandomNumberGenerator.GetInt32(minValue, maxValue);
    }

    /// <summary>
    /// Generates a cryptographically secure random long integer between 0 (inclusive) and <paramref name="maxValue"/> (exclusive or inclusive based on <paramref name="inclusiveMax"/>).
    /// </summary>
    /// <param name="maxValue">The upper bound of the random number to be generated. Must be greater than 0.</param>
    /// <param name="inclusiveMax">If true, <paramref name="maxValue"/> is inclusive; otherwise, it is exclusive.</param>
    /// <returns>A random long integer between 0 (inclusive) and <paramref name="maxValue"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxValue"/> is less than or equal to 0.</exception>
    public static long GenerateRandomLong(long maxValue, bool inclusiveMax = false)
    {
        if (maxValue <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be greater than 0.");

        long adjustedMax = inclusiveMax ? maxValue + 1 : maxValue;
        byte[] bytes = new byte[8];
        RandomNumberGenerator.Fill(bytes);
        long randomLong = Math.Abs(BitConverter.ToInt64(bytes, 0));
        return randomLong % adjustedMax;
    }

    /// <summary>
    /// Generates a cryptographically secure random long integer between <paramref name="minValue"/> (inclusive) and <paramref name="maxValue"/> (exclusive or inclusive based on <paramref name="inclusiveMax"/>).
    /// </summary>
    /// <param name="minValue">The inclusive lower bound of the random number to be generated.</param>
    /// <param name="maxValue">The upper bound of the random number to be generated. Must be greater than <paramref name="minValue"/>.</param>
    /// <param name="inclusiveMax">If true, <paramref name="maxValue"/> is inclusive; otherwise, it is exclusive.</param>
    /// <returns>A random long integer between <paramref name="minValue"/> (inclusive) and <paramref name="maxValue"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxValue"/> is less than or equal to <paramref name="minValue"/>.</exception>
    public static long GenerateRandomLong(long minValue, long maxValue, bool inclusiveMax = false)
    {
        if (maxValue <= minValue)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be greater than minValue.");

        long adjustedMax = inclusiveMax ? maxValue + 1 : maxValue;
        long range = adjustedMax - minValue;
        return minValue + GenerateRandomLong(range, false);
    }

    /// <summary>
    /// Generates a cryptographically secure random double between 0.0 (inclusive) and 1.0 (exclusive).
    /// </summary>
    /// <returns>A random double between 0.0 (inclusive) and 1.0 (exclusive).</returns>
    public static double GenerateRandomDouble()
    {
        byte[] bytes = new byte[8];
        RandomNumberGenerator.Fill(bytes);
        ulong randomULong = BitConverter.ToUInt64(bytes, 0);
        return (randomULong >> 11) * (1.0 / (1UL << 53));
    }

    /// <summary>
    /// Generates a cryptographically secure random double between <paramref name="minValue"/> (inclusive) and <paramref name="maxValue"/> (exclusive).
    /// </summary>
    /// <param name="minValue">The inclusive lower bound of the random number to be generated.</param>
    /// <param name="maxValue">The exclusive upper bound of the random number to be generated. Must be greater than <paramref name="minValue"/>.</param>
    /// <returns>A random double between <paramref name="minValue"/> (inclusive) and <paramref name="maxValue"/> (exclusive).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxValue"/> is less than or equal to <paramref name="minValue"/>.</exception>
    public static double GenerateRandomDouble(double minValue, double maxValue)
    {
        if (maxValue <= minValue)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be greater than minValue.");

        double randomValue = GenerateRandomDouble();
        return minValue + (randomValue * (maxValue - minValue));
    }

    /// <summary>
    /// Generates a cryptographically secure random float between 0.0f (inclusive) and 1.0f (exclusive).
    /// </summary>
    /// <returns>A random float between 0.0f (inclusive) and 1.0f (exclusive).</returns>
    public static float GenerateRandomFloat()
    {
        return (float)GenerateRandomDouble();
    }

    /// <summary>
    /// Generates a cryptographically secure random float between <paramref name="minValue"/> (inclusive) and <paramref name="maxValue"/> (exclusive).
    /// </summary>
    /// <param name="minValue">The inclusive lower bound of the random number to be generated.</param>
    /// <param name="maxValue">The exclusive upper bound of the random number to be generated. Must be greater than <paramref name="minValue"/>.</param>
    /// <returns>A random float between <paramref name="minValue"/> (inclusive) and <paramref name="maxValue"/> (exclusive).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxValue"/> is less than or equal to <paramref name="minValue"/>.</exception>
    public static float GenerateRandomFloat(float minValue, float maxValue)
    {
        if (maxValue <= minValue)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be greater than minValue.");

        float randomValue = GenerateRandomFloat();
        return minValue + (randomValue * (maxValue - minValue));
    }

    /// <summary>
    /// Fills the specified byte array with cryptographically secure random bytes.
    /// </summary>
    /// <param name="buffer">The byte array to fill with random bytes.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="buffer"/> is null.</exception>
    public static void GenerateRandomBytes(byte[] buffer)
    {
        if (buffer == null)
            throw new ArgumentNullException(nameof(buffer));

        RandomNumberGenerator.Fill(buffer);
    }

    /// <summary>
    /// Generates a new byte array of the specified length filled with cryptographically secure random bytes.
    /// </summary>
    /// <param name="length">The length of the byte array to generate. Must be greater than 0.</param>
    /// <returns>A byte array filled with random bytes.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="length"/> is less than or equal to 0.</exception>
    public static byte[] GenerateRandomBytes(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "length must be greater than 0.");

        return RandomNumberGenerator.GetBytes(length);
    }

    /// <summary>
    /// Generates a cryptographically secure random byte within the specified range.
    /// </summary>
    /// <param name="minValue">The inclusive lower bound of the random byte to be generated.</param>
    /// <param name="maxValue">The exclusive upper bound of the random byte to be generated. Must be greater than <paramref name="minValue"/>.</param>
    /// <returns>A random byte between <paramref name="minValue"/> (inclusive) and <paramref name="maxValue"/> (exclusive).</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="maxValue"/> is less than or equal to <paramref name="minValue"/>.</exception>
    public static byte GenerateRandomByte(byte minValue, byte maxValue)
    {
        if (maxValue <= minValue)
            throw new ArgumentOutOfRangeException(nameof(maxValue), "maxValue must be greater than minValue.");

        return (byte)GenerateRandomInt(minValue, maxValue);
    }

    /// <summary>
    /// Generates a cryptographically secure random boolean value.
    /// </summary>
    /// <returns>A random boolean value (true or false).</returns>
    public static bool GenerateRandomBool()
    {
        return GenerateRandomInt(2) == 1;
    }

    /// <summary>
    /// Generates a cryptographically secure random boolean value with the specified probability of returning true.
    /// </summary>
    /// <param name="probabilityOfTrue">The probability (0.0 to 1.0) that the method returns true.</param>
    /// <returns>A random boolean value based on the specified probability.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="probabilityOfTrue"/> is not between 0.0 and 1.0.</exception>
    public static bool GenerateRandomBool(double probabilityOfTrue)
    {
        if (probabilityOfTrue < 0.0 || probabilityOfTrue > 1.0)
            throw new ArgumentOutOfRangeException(nameof(probabilityOfTrue), "Probability must be between 0.0 and 1.0.");

        return GenerateRandomDouble() < probabilityOfTrue;
    }

    /// <summary>
    /// Generates a cryptographically secure random enum value of the specified enum type.
    /// </summary>
    /// <typeparam name="TEnum">The enum type to generate a random value for.</typeparam>
    /// <returns>A random value from the specified enum type.</returns>
    /// <exception cref="ArgumentException">Thrown when <typeparamref name="TEnum"/> is not an enum type.</exception>
    public static TEnum GenerateRandomEnum<TEnum>() where TEnum : struct, Enum
    {
        var values = Enum.GetValues<TEnum>();
        if (values.Length == 0)
            throw new ArgumentException($"Enum type {typeof(TEnum).Name} has no values.");

        int index = GenerateRandomInt(values.Length);
        return values[index];
    }

    /// <summary>
    /// Selects a random element from the specified collection using cryptographically secure random generation.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection.</typeparam>
    /// <param name="collection">The collection to select from.</param>
    /// <returns>A randomly selected element from the collection.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="collection"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="collection"/> is empty.</exception>
    public static T SelectRandomElement<T>(IReadOnlyList<T> collection)
    {
        if (collection == null)
            throw new ArgumentNullException(nameof(collection));

        if (collection.Count == 0)
            throw new ArgumentException("Collection cannot be empty.", nameof(collection));

        int index = GenerateRandomInt(collection.Count);
        return collection[index];
    }

    /// <summary>
    /// Selects a random element from the specified enumerable collection using cryptographically secure random generation.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection.</typeparam>
    /// <param name="collection">The collection to select from.</param>
    /// <returns>A randomly selected element from the collection.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="collection"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown when <paramref name="collection"/> is empty.</exception>
    public static T SelectRandomElement<T>(IEnumerable<T> collection)
    {
        if (collection == null)
            throw new ArgumentNullException(nameof(collection));

        var list = collection.ToList();
        if (list.Count == 0)
            throw new ArgumentException("Collection cannot be empty.", nameof(collection));

        return SelectRandomElement<T>(list);
    }

    /// <summary>
    /// Selects multiple random elements from the specified collection with or without replacement using cryptographically secure random generation.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection.</typeparam>
    /// <param name="collection">The collection to select from.</param>
    /// <param name="count">The number of elements to select. Must be less than or equal to the collection count when <paramref name="allowReplacement"/> is false.</param>
    /// <param name="allowReplacement">If true, elements can be selected multiple times; if false, each element can only be selected once.</param>
    /// <returns>A list of randomly selected elements from the collection.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="collection"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is less than 0, or when <paramref name="allowReplacement"/> is false and <paramref name="count"/> is greater than the collection count.</exception>
    public static List<T> SelectRandomElements<T>(IEnumerable<T> collection, int count, bool allowReplacement = false)
    {
        if (collection == null)
            throw new ArgumentNullException(nameof(collection));

        var list = collection.ToList();

        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count), "Count must be greater than or equal to 0.");

        if (!allowReplacement && count > list.Count)
            throw new ArgumentOutOfRangeException(nameof(count), $"Count must be between 0 and {list.Count} when allowReplacement is false.");

        if (allowReplacement)
        {
            var result = new List<T>(count);
            for (int i = 0; i < count; i++)
            {
                result.Add(SelectRandomElement(list));
            }
            return result;
        }
        else
        {
            var shuffled = Shuffle(list);
            return shuffled.Take(count).ToList();
        }
    }

    /// <summary>
    /// Shuffles the elements of the specified collection using the Fisher-Yates algorithm with cryptographically secure random generation.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection.</typeparam>
    /// <param name="collection">The collection to shuffle.</param>
    /// <returns>A new list containing the shuffled elements.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="collection"/> is null.</exception>
    public static List<T> Shuffle<T>(IEnumerable<T> collection)
    {
        if (collection == null)
            throw new ArgumentNullException(nameof(collection));

        var list = collection.ToList();
        int n = list.Count;

        for (int i = n - 1; i > 0; i--)
        {
            int j = GenerateRandomInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }

        return list;
    }

    /// <summary>
    /// Generates a cryptographically secure random hexadecimal string of the specified length.
    /// </summary>
    /// <param name="length">The desired length of the hexadecimal string (in characters, must be even). Must be greater than 0.</param>
    /// <returns>A random hexadecimal string.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="length"/> is less than or equal to 0 or is odd.</exception>
    public static string GenerateRandomHexString(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "length must be greater than 0.");
        if (length % 2 != 0)
            throw new ArgumentOutOfRangeException(nameof(length), "length must be an even number.");

        byte[] bytes = GenerateRandomBytes(length / 2);
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    /// <summary>
    /// Generates a cryptographically secure random Base64 string of the specified byte length.
    /// </summary>
    /// <param name="byteLength">The number of random bytes to generate before Base64 encoding. Must be greater than 0.</param>
    /// <returns>A random Base64-encoded string.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="byteLength"/> is less than or equal to 0.</exception>
    public static string GenerateRandomBase64String(int byteLength)
    {
        if (byteLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(byteLength), "byteLength must be greater than 0.");

        byte[] bytes = GenerateRandomBytes(byteLength);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Generates a cryptographically secure random alphanumeric code (uppercase letters and digits only).
    /// </summary>
    /// <param name="length">The desired length of the code. Must be greater than 0.</param>
    /// <returns>A random alphanumeric code.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="length"/> is less than or equal to 0.</exception>
    public static string GenerateRandomCode(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "length must be greater than 0.");

        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var result = new StringBuilder(length);

        for (int i = 0; i < length; i++)
        {
            int index = GenerateRandomInt(chars.Length);
            result.Append(chars[index]);
        }

        return result.ToString();
    }

    /// <summary>
    /// Generates a cryptographically secure random PIN (digits only).
    /// </summary>
    /// <param name="length">The desired length of the PIN. Must be greater than 0.</param>
    /// <returns>A random PIN as a string.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="length"/> is less than or equal to 0.</exception>
    public static string GenerateRandomPin(int length)
    {
        if (length <= 0)
            throw new ArgumentOutOfRangeException(nameof(length), "length must be greater than 0.");

        var result = new StringBuilder(length);

        for (int i = 0; i < length; i++)
        {
            result.Append(GenerateRandomInt(10));
        }

        return result.ToString();
    }

    #region Gambling Methods

    /* 
     * This is just for fun, it is not meant to be used
     * That being said, let's go gambling, shall we?
     */

    /// <summary>
    /// Generates a shuffled deck of playing cards.
    /// </summary>
    /// <param name="deckCount">The number of standard 52-card decks to include. Must be greater than 0.</param>
    /// <param name="shouldShuffle">Determines whether the deck should be shuffled after creation.</param>
    /// <returns>A shuffled <see cref="Deck"/> containing the specified number of decks.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="deckCount"/> is less than or equal to 0.</exception>
    public static Deck GenerateCardDeck(int deckCount = 1, bool shouldShuffle = true)
    {
        if (deckCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(deckCount), "deckCount must be greater than 0.");

        var allCards = new List<PlayingCard>();

        for (int deck = 0; deck < deckCount; deck++)
        {
            foreach (CardSuit suit in Enum.GetValues<CardSuit>())
            {
                foreach (CardRank rank in Enum.GetValues<CardRank>())
                {
                    allCards.Add(new PlayingCard { Rank = rank, Suit = suit });
                }
            }
        }

        if (!shouldShuffle)
            return new Deck(allCards, deckCount);

        var shuffledCards = Shuffle(allCards);
        return new Deck(shuffledCards, deckCount);
    }

    /// <summary>
    /// Generates a random Blackjack card from the specified number of decks.
    /// This method creates a new deck internally and does not track the deck state.
    /// For proper deck tracking, use <see cref="GenerateRandomPlayingCard(Deck)"/> instead.
    /// </summary>
    /// <param name="deckCount">The number of decks to simulate. Must be greater than 0.</param>
    /// <returns>A randomly selected <see cref="PlayingCard"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="deckCount"/> is less than or equal to 0.</exception>
    public static CardDrawResult GenerateRandomPlayingCard(int deckCount = 1)
    {
        if (deckCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(deckCount), "deckCount must be greater than 0.");

        var deck = GenerateCardDeck(deckCount);
        return GenerateRandomPlayingCard(deck);
    }

    /// <summary>
    /// Draws a random card from the provided deck with no replacement.
    /// </summary>
    /// <param name="deck">The deck to draw from. Must not be null and must have at least one card.</param>
    /// <returns>A <see cref="CardDrawResult"/> containing the drawn card and the updated deck.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="deck"/> is null.</exception>
    /// <exception cref="InvalidOperationException">Thrown when the deck has no cards remaining.</exception>
    public static CardDrawResult GenerateRandomPlayingCard(Deck deck)
    {
        var card = deck.DrawCard();
        return new CardDrawResult { Card = card, Deck = deck };
    }

    /// <summary>
    /// Deals a hand of Blackjack cards from a newly created deck. GAMBAAA!!
    /// </summary>
    /// <param name="cardCount">The number of cards to deal. Must be greater than 0.</param>
    /// <param name="deckCount">The number of decks to create and deal from. Must be greater than 0.</param>
    /// <returns>A <see cref="BlackjackDealResult"/> containing the dealt hand and the remaining deck.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when parameters are invalid.</exception>
    public static BlackjackDealResult DealBlackjackHand(int cardCount = 2, int deckCount = 6)
    {
        if (cardCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(cardCount), "cardCount must be greater than 0.");
        if (deckCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(deckCount), "deckCount must be greater than 0.");

        var deck = GenerateCardDeck(deckCount);
        return DealBlackjackHand(cardCount, deck);
    }

    /// <summary>
    /// Deals a hand of Blackjack cards from the provided deck with no replacement.
    /// </summary>
    /// <param name="cardCount">The number of cards to deal. Must be greater than 0 and not exceed remaining cards in deck.</param>
    /// <param name="deck">The deck to deal from. If null, a new 6-deck shoe will be created.</param>
    /// <returns>A <see cref="BlackjackDealResult"/> containing the dealt hand and the updated deck.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="cardCount"/> is invalid or exceeds remaining cards.</exception>
    public static BlackjackDealResult DealBlackjackHand(int cardCount, Deck deck)
    {
        if (cardCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(cardCount), "cardCount must be greater than 0.");

        var hand = deck.DrawCards(cardCount);
        return new BlackjackDealResult { Hand = hand, Deck = deck };
    }

    /// <summary>
    /// Deals poker hands from a newly created deck.
    /// </summary>
    /// <param name="playerCount">The number of players. Must be between 1 and 10 inclusive.</param>
    /// <param name="cardsPerPlayer">The number of cards per player. Must be greater than 0.</param>
    /// <param name="deckCount">The number of decks to create and deal from. Must be greater than 0.</param>
    /// <returns>A <see cref="PokerDealResult"/> containing the dealt hands and the remaining deck.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when parameters are invalid.</exception>
    public static PokerDealResult DealPokerHands(int playerCount = 4, int cardsPerPlayer = 2, int deckCount = 1)
    {
        if (playerCount <= 0 || playerCount > 10)
            throw new ArgumentOutOfRangeException(nameof(playerCount), "playerCount must be between 1 and 10.");
        if (cardsPerPlayer <= 0)
            throw new ArgumentOutOfRangeException(nameof(cardsPerPlayer), "cardsPerPlayer must be greater than 0.");
        if (deckCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(deckCount), "deckCount must be greater than 0.");

        var deck = GenerateCardDeck(deckCount);
        return DealPokerHands(playerCount, cardsPerPlayer, deck);
    }

    /// <summary>
    /// Deals poker hands from the provided deck with no replacement.
    /// </summary>
    /// <param name="playerCount">The number of players. Must be greater than 0 and less than 11.</param>
    /// <param name="cardsPerPlayer">The number of cards per player. Must be greater than 0.</param>
    /// <param name="deck">The deck to deal from. If null, a new single deck will be created.</param>
    /// <returns>A <see cref="PokerDealResult"/> containing the dealt hands and the updated deck.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when parameters are invalid or insufficient cards remain.</exception>
    public static PokerDealResult DealPokerHands(int playerCount, int cardsPerPlayer, Deck deck)
    {
        if (playerCount <= 0 || playerCount > 10)
            throw new ArgumentOutOfRangeException(nameof(playerCount), "playerCount must be between 1 and 10.");
        if (cardsPerPlayer <= 0)
            throw new ArgumentOutOfRangeException(nameof(cardsPerPlayer), "cardsPerPlayer must be greater than 0.");

        int totalCardsNeeded = playerCount * cardsPerPlayer;
        if (!deck.ShouldAutoRefill && totalCardsNeeded > deck.RemainingCardsCount)
            throw new ArgumentOutOfRangeException(nameof(playerCount),
                $"Not enough cards in deck. Need {totalCardsNeeded} cards but only {deck.RemainingCardsCount} remaining.");

        var hands = new Dictionary<int, List<PlayingCard>>();

        for (int player = 1; player <= playerCount; player++)
        {
            hands[player] = deck.DrawCards(cardsPerPlayer);
        }

        return new PokerDealResult { Hands = hands, Deck = deck };
    }

    /// <summary>
    /// Rolls dice. May the luck be with you.
    /// </summary>
    /// <param name="numberOfDice">The number of dice to roll. Must be greater than 0.</param>
    /// <param name="sidesPerDie">The number of sides on each die. Must be greater than 0.</param>
    /// <returns>A <see cref="DiceRoll"/> containing the results.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="numberOfDice"/> or <paramref name="sidesPerDie"/> is less than or equal to 0.</exception>
    public static DiceRoll RollDice(int numberOfDice = 2, int sidesPerDie = 6)
    {
        if (numberOfDice <= 0)
            throw new ArgumentOutOfRangeException(nameof(numberOfDice), "numberOfDice must be greater than 0.");
        if (sidesPerDie <= 0)
            throw new ArgumentOutOfRangeException(nameof(sidesPerDie), "sidesPerDie must be greater than 0.");

        var dice = new List<int>(numberOfDice);
        for (int i = 0; i < numberOfDice; i++)
        {
            dice.Add(GenerateRandomInt(1, sidesPerDie + 1));
        }

        return new DiceRoll { Dice = dice };
    }

    /// <summary>
    /// Flips a coin. Heads or tails?
    /// </summary>
    /// <returns>The result of the coin flip (<see cref="CoinFlip.Heads"/> or <see cref="CoinFlip.Tails"/>).</returns>
    public static CoinFlip FlipCoin()
    {
        return GenerateRandomBool() ? CoinFlip.Heads : CoinFlip.Tails;
    }

    /// <summary>
    /// Flips multiple coins at once. Surely you'll win with this one.
    /// </summary>
    /// <param name="count">The number of coins to flip. Must be greater than 0.</param>
    /// <returns>A list of coin flip results.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="count"/> is less than or equal to 0.</exception>
    public static List<CoinFlip> FlipCoins(int count)
    {
        if (count <= 0)
            throw new ArgumentOutOfRangeException(nameof(count), "count must be greater than 0.");

        var results = new List<CoinFlip>(count);
        for (int i = 0; i < count; i++)
        {
            results.Add(FlipCoin());
        }

        return results;
    }

    /// <summary>
    /// Spins the roulette wheel. Place your bets.
    /// </summary>
    /// <param name="includeDoubleZero">If true, uses American roulette rules (0 and 00). If false, uses European roulette rules (0 only).</param>
    /// <returns>A <see cref="RouletteResult"/> containing the number and color.</returns>
    public static RouletteResult SpinRoulette(bool includeDoubleZero = false)
    {
        // European: 0-36, American: 0-36 + 00 (represented as 37)
        int maxNumber = includeDoubleZero ? 37 : 36;
        int number = GenerateRandomInt(maxNumber + 1);

        RouletteColor color;
        if (number == 0 || number == 37)
        {
            color = RouletteColor.Green;
        }
        else
        {
            var redNumbers = new HashSet<int> { 1, 3, 5, 7, 9, 12, 14, 16, 18, 19, 21, 23, 25, 27, 30, 32, 34, 36 };
            color = redNumbers.Contains(number) ? RouletteColor.Red : RouletteColor.Black;
        }

        return new RouletteResult { Number = number, Color = color };
    }

    /// <summary>
    /// Generates random lottery numbers. Feeling lucky?
    /// </summary>
    /// <param name="numbersCount">The count of numbers to pick. Must be greater than 0.</param>
    /// <param name="maxNumber">The maximum number in the lottery. Must be greater than <paramref name="numbersCount"/>.</param>
    /// <param name="sorted">If true, returns the numbers in ascending order.</param>
    /// <returns>A list of unique lottery numbers.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when parameters are invalid.</exception>
    public static List<int> GenerateLotteryNumbers(int numbersCount = 6, int maxNumber = 49, bool sorted = true)
    {
        if (numbersCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(numbersCount), "numbersCount must be greater than 0.");
        if (maxNumber < numbersCount)
            throw new ArgumentOutOfRangeException(nameof(maxNumber), "maxNumber must be greater than or equal to numbersCount.");

        var allNumbers = Enumerable.Range(1, maxNumber).ToList();
        var shuffled = Shuffle(allNumbers);
        var selected = shuffled.Take(numbersCount).ToList();

        return sorted ? selected.OrderBy(n => n).ToList() : selected;
    }

    #endregion
}
