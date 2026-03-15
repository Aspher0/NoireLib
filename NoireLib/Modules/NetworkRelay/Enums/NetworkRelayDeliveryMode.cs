namespace NoireLib.NetworkRelay;

/// <summary>
/// Represents the desired delivery behavior for outbound relay messages.
/// </summary>
public enum NetworkRelayDeliveryMode
{
    /// <summary>
    /// Use UDP best-effort delivery.
    /// </summary>
    BestEffort = 0,

    /// <summary>
    /// Use TCP reliable delivery.
    /// </summary>
    Reliable = 1,
}
