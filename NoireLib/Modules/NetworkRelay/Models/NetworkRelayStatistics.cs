namespace NoireLib.NetworkRelay;

/// <summary>
/// Statistics about the NetworkRelay module's activity.
/// </summary>
/// <param name="ActivePeers">The number of currently tracked peers.</param>
/// <param name="ActiveSubscriptions">The number of currently active relay subscriptions.</param>
/// <param name="ActiveEventBridges">The number of currently active EventBus bridge registrations.</param>
/// <param name="ReliableTransportEnabled"><see langword="true"/> if the reliable TCP transport listener is enabled; otherwise, <see langword="false"/>.</param>
/// <param name="TotalMessagesSent">The total number of relay messages sent.</param>
/// <param name="TotalMessagesReceived">The total number of relay messages received.</param>
/// <param name="TotalBestEffortMessagesSent">The total number of best-effort UDP relay messages sent.</param>
/// <param name="TotalBestEffortMessagesReceived">The total number of best-effort UDP relay messages received.</param>
/// <param name="TotalReliableMessagesSent">The total number of reliable TCP relay messages sent.</param>
/// <param name="TotalReliableMessagesReceived">The total number of reliable TCP relay messages received.</param>
/// <param name="TotalReliableConnectionsAccepted">The total number of reliable TCP client connections accepted.</param>
/// <param name="TotalBytesSent">The total number of bytes sent.</param>
/// <param name="TotalBytesReceived">The total number of bytes received.</param>
/// <param name="TotalMessagesDropped">The total number of dropped messages.</param>
/// <param name="TotalDuplicateMessagesDropped">The total number of dropped duplicate messages.</param>
/// <param name="TotalPeerAnnouncementsReceived">The total number of received peer announcements.</param>
/// <param name="TotalSendFailures">The total number of send failures.</param>
/// <param name="TotalReceiveFailures">The total number of receive failures.</param>
/// <param name="TotalDispatchExceptionsCaught">The total number of exceptions caught while dispatching callbacks and events.</param>
/// <param name="TotalExceptionsCaught">The total number of exceptions caught by the relay.</param>
/// <param name="TotalEventBusEventsRelayed">The total number of local EventBus events relayed over the network.</param>
/// <param name="TotalEventBusEventsPublishedLocally">The total number of received relay events republished into the local EventBus.</param>
/// <param name="TotalSubscriptionsCreated">The total number of relay subscriptions created.</param>
/// <param name="TotalPeersRegistered">The total number of peers registered.</param>
/// <param name="TotalPeersRemoved">The total number of peers removed.</param>
public sealed record NetworkRelayStatistics(
    int ActivePeers,
    int ActiveSubscriptions,
    int ActiveEventBridges,
    bool ReliableTransportEnabled,
    long TotalMessagesSent,
    long TotalMessagesReceived,
    long TotalBestEffortMessagesSent,
    long TotalBestEffortMessagesReceived,
    long TotalReliableMessagesSent,
    long TotalReliableMessagesReceived,
    long TotalReliableConnectionsAccepted,
    long TotalBytesSent,
    long TotalBytesReceived,
    long TotalMessagesDropped,
    long TotalDuplicateMessagesDropped,
    long TotalPeerAnnouncementsReceived,
    long TotalSendFailures,
    long TotalReceiveFailures,
    long TotalDispatchExceptionsCaught,
    long TotalExceptionsCaught,
    long TotalEventBusEventsRelayed,
    long TotalEventBusEventsPublishedLocally,
    long TotalSubscriptionsCreated,
    long TotalPeersRegistered,
    long TotalPeersRemoved);
