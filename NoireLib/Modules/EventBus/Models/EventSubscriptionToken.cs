using System;

namespace NoireLib.EventBus;

/// <summary>
/// A token representing an event subscription, used to unsubscribe from events.
/// </summary>
public readonly struct EventSubscriptionToken : IEquatable<EventSubscriptionToken>
{
    /// <summary>
    /// The unique identifier for this subscription token.
    /// </summary>
    private readonly Guid ID;

    internal EventSubscriptionToken(Guid id)
    {
        ID = id;
    }

    /// <summary>
    /// Equals method to compare two EventSubscriptionToken instances.
    /// </summary>
    /// <param name="other">The other <see cref="EventSubscriptionToken"/> to compare with.</param>
    /// <returns>True if both tokens are equal; otherwise, false.</returns>
    public bool Equals(EventSubscriptionToken other) => ID.Equals(other.ID);

    /// <summary>
    /// Equals method to compare with an object.
    /// </summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>True if the object is an <see cref="EventSubscriptionToken"/> and both tokens are equal; otherwise, false.</returns>
    public override bool Equals(object? obj) => obj is EventSubscriptionToken token && Equals(token);

    /// <summary>
    /// Gets the hash code for this token.
    /// </summary>
    /// <returns>The hash code of the token.</returns>
    public override int GetHashCode() => ID.GetHashCode();

    /// <summary>
    /// Equality operator to compare two EventSubscriptionToken instances.
    /// </summary>
    /// <param name="left">The left <see cref="EventSubscriptionToken"/>.</param>
    /// <param name="right">The right <see cref="EventSubscriptionToken"/>.</param>
    /// <returns>The result of the equality comparison.</returns>
    public static bool operator ==(EventSubscriptionToken left, EventSubscriptionToken right) => left.Equals(right);

    /// <summary>
    /// Inequality operator to compare two EventSubscriptionToken instances.
    /// </summary>
    /// <param name="left">The left <see cref="EventSubscriptionToken"/>.</param>
    /// <param name="right">The right <see cref="EventSubscriptionToken"/>.</param>
    /// <returns>The result of the inequality comparison.</returns>
    public static bool operator !=(EventSubscriptionToken left, EventSubscriptionToken right) => !left.Equals(right);

    /// <summary>
    /// ToString override to provide a string representation of the token.
    /// </summary>
    /// <returns>The string representation of the token.</returns>
    public override string ToString() => ID.ToString();
}
