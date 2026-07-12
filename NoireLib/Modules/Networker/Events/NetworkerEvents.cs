namespace NoireLib.Networker;

/// <summary>
/// EventBus event published when a peer joins the network.
/// </summary>
/// <param name="Networker">The networker instance that observed the join.</param>
/// <param name="Peer">The peer that joined.</param>
public record NetworkerPeerJoinedEvent(NoireNetworker Networker, NetworkerPeer Peer);

/// <summary>
/// EventBus event published when a peer leaves the network.
/// </summary>
/// <param name="Networker">The networker instance that observed the departure.</param>
/// <param name="Peer">The peer that left.</param>
public record NetworkerPeerLeftEvent(NoireNetworker Networker, NetworkerPeer Peer);

/// <summary>
/// EventBus event published when a peer's metadata or flags change.
/// </summary>
/// <param name="Networker">The networker instance that observed the update.</param>
/// <param name="Peer">The peer that changed.</param>
/// <param name="Key">The metadata key that changed, or "flag:&lt;name&gt;" for flag changes.</param>
public record NetworkerPeerUpdatedEvent(NoireNetworker Networker, NetworkerPeer Peer, string Key);

/// <summary>
/// EventBus event published when the networker's connection state changes.
/// </summary>
/// <param name="Networker">The networker instance whose state changed.</param>
/// <param name="OldState">The previous state.</param>
/// <param name="NewState">The new state.</param>
public record NetworkerStateChangedEvent(NoireNetworker Networker, NetworkerState OldState, NetworkerState NewState);
