namespace NoireLib.NetworkRelay;

/// <summary>
/// Represents the underlying transport used by a relay message.
/// </summary>
public enum NetworkRelayTransportKind
{
    /// <summary>
    /// The message used UDP best-effort delivery.
    /// </summary>
    Udp = 0,

    /// <summary>
    /// The message used TCP reliable delivery.
    /// </summary>
    Tcp = 1,
}
