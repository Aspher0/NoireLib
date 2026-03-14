namespace NoireLib.IPC;

/// <summary>
/// Defines how an attributed IPC member should be processed.
/// </summary>
public enum NoireIpcMode
{
    /// <summary>
    /// Infers the mode from the annotated member shape.
    /// Public methods remain providers. Non-public void methods are treated as consumer subscriptions.
    /// </summary>
    Auto,

    /// <summary>
    /// Processes the attributed member as a provider registration or binding.
    /// </summary>
    Provider,

    /// <summary>
    /// Processes the attributed member as a consumer binding or message subscription.
    /// </summary>
    Consumer,
}
