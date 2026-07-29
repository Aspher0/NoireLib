namespace NoireLib.ObservedStore;

public partial class NoireObservedStore
{
    /// <summary>
    /// Mirrors a store event onto the configured EventBus, when there is one and mirroring is on. All three event
    /// types publish together: they fire on deliberate writes rather than on a poll, so there is no firehose to
    /// guard against.
    /// </summary>
    /// <typeparam name="TEvent">The event type.</typeparam>
    /// <param name="evt">The event.</param>
    private void PublishToEventBus<TEvent>(TEvent evt) where TEvent : notnull
    {
        var active = ActiveOptions;

        if (!active.PublishModuleEvents || active.EventBus == null)
            return;

        active.EventBus.Publish(evt);
    }
}
