using System;

namespace NoireLib.NetworkRelay;

/// <summary>
/// A token representing a relay subscription, used to unsubscribe from channel handlers.
/// </summary>
public readonly struct NetworkRelaySubscriptionToken : IEquatable<NetworkRelaySubscriptionToken>
{
    private readonly Guid ID;

    internal NetworkRelaySubscriptionToken(Guid id)
    {
        ID = id;
    }

    /// <inheritdoc/>
    public bool Equals(NetworkRelaySubscriptionToken other) => ID.Equals(other.ID);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NetworkRelaySubscriptionToken token && Equals(token);

    /// <inheritdoc/>
    public override int GetHashCode() => ID.GetHashCode();

    /// <inheritdoc/>
    public static bool operator ==(NetworkRelaySubscriptionToken left, NetworkRelaySubscriptionToken right) => left.Equals(right);

    /// <inheritdoc/>
    public static bool operator !=(NetworkRelaySubscriptionToken left, NetworkRelaySubscriptionToken right) => !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() => ID.ToString();
}
