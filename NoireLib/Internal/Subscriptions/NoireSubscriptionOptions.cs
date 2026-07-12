using System;

namespace NoireLib.Core.Subscriptions;

/// <summary>
/// Optional settings for a subscription in a <see cref="NoireSubscriptionRegistry{TKey, TContext}"/>.
/// </summary>
/// <typeparam name="TContext">The context type passed to handlers on dispatch.</typeparam>
public sealed class NoireSubscriptionOptions<TContext>
{
    /// <summary>
    /// An optional string key identifying this subscription.<br/>
    /// Subscribing again with the same key replaces the previous subscription.
    /// </summary>
    public string? Key { get; set; }

    /// <summary>
    /// The priority of the subscription. Higher values are invoked first; equal values run in subscription order.
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// An optional filter; the handler is only invoked when the filter returns true for the dispatched context.
    /// </summary>
    public Func<TContext, bool>? Filter { get; set; }

    /// <summary>
    /// Whether the subscription is automatically removed after its first invocation.
    /// </summary>
    public bool Once { get; set; }

    /// <summary>
    /// An optional owner object, allowing bulk unsubscription via <see cref="NoireSubscriptionRegistry{TKey, TContext}.UnsubscribeOwner(object)"/>.
    /// </summary>
    public object? Owner { get; set; }

    /// <summary>
    /// The thread the handler is invoked on. Defaults to <see cref="SubscriptionDelivery.Inline"/>.
    /// </summary>
    public SubscriptionDelivery Delivery { get; set; } = SubscriptionDelivery.Inline;
}
