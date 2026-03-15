using NoireLib.EventBus;

namespace NoireLib.NetworkRelay;

/// <summary>
/// Represents a paired EventBus-to-network bridge registration.
/// </summary>
/// <param name="Channel">The relay channel used by the bridge.</param>
/// <param name="EventBusSubscriptionToken">The outbound EventBus subscription token.</param>
/// <param name="RelaySubscriptionToken">The inbound relay subscription token.</param>
public readonly record struct NetworkRelayEventBridgeHandle(
    string Channel,
    EventSubscriptionToken EventBusSubscriptionToken,
    NetworkRelaySubscriptionToken RelaySubscriptionToken)
{
    /// <summary>
    /// Whether this bridge includes an outbound EventBus subscription.
    /// </summary>
    public bool HasEventBusSubscription => EventBusSubscriptionToken != default;

    /// <summary>
    /// Whether this bridge includes an inbound relay subscription.
    /// </summary>
    public bool HasRelaySubscription => RelaySubscriptionToken != default;
}
