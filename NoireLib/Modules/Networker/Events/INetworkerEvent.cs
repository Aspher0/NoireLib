namespace NoireLib.Networker;

/// <summary>
/// Optional interface for EventBus event classes shared through <see cref="NoireNetworker.ShareEvent{TEvent}(NetworkerShareDirection)"/>.<br/>
/// When implemented, the networker populates <see cref="Origin"/> with the sending peer on events received from the network.
/// </summary>
public interface INetworkerEvent
{
    /// <summary>
    /// The peer the event was received from, or null when the event was published locally.
    /// </summary>
    NetworkerPeer? Origin { get; set; }
}
