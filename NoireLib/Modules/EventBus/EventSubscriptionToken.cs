using System;

namespace NoireLib.EventBus;

/// <summary>
/// A token representing an event subscription, used to unsubscribe from events.
/// </summary>
public readonly struct EventSubscriptionToken : IEquatable<EventSubscriptionToken>
{
    private readonly Guid ID;

    internal EventSubscriptionToken(Guid id)
    {
        ID = id;
    }

    public bool Equals(EventSubscriptionToken other) => ID.Equals(other.ID);

    public override bool Equals(object? obj) => obj is EventSubscriptionToken token && Equals(token);

    public override int GetHashCode() => ID.GetHashCode();

    public static bool operator ==(EventSubscriptionToken left, EventSubscriptionToken right) => left.Equals(right);

    public static bool operator !=(EventSubscriptionToken left, EventSubscriptionToken right) => !left.Equals(right);

    public override string ToString() => ID.ToString();
}
