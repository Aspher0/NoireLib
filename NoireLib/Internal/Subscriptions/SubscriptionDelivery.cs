namespace NoireLib.Core.Subscriptions;

/// <summary>
/// Defines on which thread a subscription's handler is invoked when a notification is dispatched.
/// </summary>
public enum SubscriptionDelivery
{
    /// <summary>
    /// The handler runs inline, on whatever thread dispatches the notification.
    /// </summary>
    Inline = 0,

    /// <summary>
    /// The handler is marshaled to the framework (game main) thread, making it safe to touch game state.<br/>
    /// Falls back to inline invocation when NoireLib is not initialized (e.g. in unit tests).
    /// </summary>
    FrameworkThread = 1,
}
