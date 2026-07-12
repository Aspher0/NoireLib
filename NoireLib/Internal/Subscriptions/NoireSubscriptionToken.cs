using System;
using System.Threading;

namespace NoireLib.Core.Subscriptions;

/// <summary>
/// A disposable token representing a subscription in a <see cref="NoireSubscriptionRegistry{TKey, TContext}"/>.<br/>
/// Disposing the token unsubscribes the handler. Disposing is idempotent and thread-safe.
/// </summary>
public sealed class NoireSubscriptionToken : IDisposable
{
    private Action<NoireSubscriptionToken>? unsubscribeAction;

    internal NoireSubscriptionToken(string? key, int priority, Action<NoireSubscriptionToken> unsubscribeAction)
    {
        Id = Guid.NewGuid();
        Key = key;
        Priority = priority;
        this.unsubscribeAction = unsubscribeAction;
    }

    /// <summary>
    /// The unique identifier of this subscription.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// The optional string key this subscription was registered under, used for keyed replacement and unsubscription.
    /// </summary>
    public string? Key { get; }

    /// <summary>
    /// The priority of this subscription. Higher values are invoked first.
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Whether this subscription is still registered.
    /// </summary>
    public bool IsActive => Volatile.Read(ref unsubscribeAction) != null;

    /// <summary>
    /// Marks the token as no longer registered without invoking the unsubscribe action.<br/>
    /// Called by the owning registry when the subscription is removed through another path.
    /// </summary>
    internal void Invalidate()
        => Interlocked.Exchange(ref unsubscribeAction, null);

    /// <summary>
    /// Unsubscribes the handler associated with this token. Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        var action = Interlocked.Exchange(ref unsubscribeAction, null);
        action?.Invoke(this);
    }

    /// <summary>
    /// Returns a string representation of this token.
    /// </summary>
    /// <returns>The string representation.</returns>
    public override string ToString()
        => Key != null ? $"{Id} (Key: {Key})" : Id.ToString();
}
