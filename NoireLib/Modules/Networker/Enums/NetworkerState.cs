namespace NoireLib.Networker;

/// <summary>
/// Represents the connection state of a <see cref="NoireNetworker"/> instance.
/// </summary>
public enum NetworkerState
{
    /// <summary>
    /// The networker is not running.
    /// </summary>
    Stopped = 0,

    /// <summary>
    /// The networker is starting up and joining the network for the first time.
    /// </summary>
    Starting = 1,

    /// <summary>
    /// The networker is connected to the network and fully operational.
    /// </summary>
    Ready = 2,

    /// <summary>
    /// The hub was lost and a new one is being elected; outbound messages are buffered until <see cref="Ready"/>.
    /// </summary>
    Reelecting = 3,
}
