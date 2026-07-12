namespace NoireLib.Networker;

/// <summary>
/// Defines which directions an EventBus event type is shared with the network by <see cref="NoireNetworker.ShareEvent{TEvent}(NetworkerShareDirection)"/>.
/// </summary>
public enum NetworkerShareDirection
{
    /// <summary>
    /// Local EventBus publishes are sent to all peers, and events received from peers are published on the local EventBus.
    /// </summary>
    Both = 0,

    /// <summary>
    /// Only local EventBus publishes are sent to all peers.
    /// </summary>
    Outbound = 1,

    /// <summary>
    /// Only events received from peers are published on the local EventBus.
    /// </summary>
    Inbound = 2,
}
