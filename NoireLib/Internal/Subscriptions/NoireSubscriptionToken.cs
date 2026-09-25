using System;
using System.Threading;

namespace NoireLib.Core.Subscriptions;

/// <summary>A subscription token. Disposing it unsubscribes, idempotently and thread-safely.</summary>
public sealed class NoireSubscriptionToken : IDisposable
{
    private Action<NoireSubscriptionToken>? unsubscribeAction;

    internal NoireSubscriptionToken(string? key, int priority, Action<NoireSubscriptionToken> unsubscribeAction)
        : this(null, key, priority, unsubscribeAction)
    {
    }

    internal NoireSubscriptionToken(object? issuer, string? key, int priority, Action<NoireSubscriptionToken> unsubscribeAction)
    {
        Issuer = issuer;
        Id = Guid.NewGuid();
        Key = key;
        Priority = priority;
        this.unsubscribeAction = unsubscribeAction;
    }

    // A registry refuses to remove a token it did not issue. Null for a token a module composes itself.
    internal object? Issuer { get; }

    /// <summary>The unique identifier of this subscription.</summary>
    public Guid Id { get; }

    /// <summary>
    /// The optional string key this subscription was registered under, used for keyed replacement and unsubscription.
    /// </summary>
    public string? Key { get; }

    /// <summary>
    /// The priority of this subscription. Higher values are invoked first.
    /// </summary>
    public int Priority { get; }

    /// <summary>Whether this subscription is still registered.</summary>
    public bool IsActive => Volatile.Read(ref unsubscribeAction) != null;

    // Marks the token as no longer registered without invoking the unsubscribe action. Called by the owning registry
    // when the subscription is removed through another path.
    internal void Invalidate()
        => Interlocked.Exchange(ref unsubscribeAction, null);

    /// <summary>
    /// Unsubscribes the handler associated with this token. Safe to call multiple times.
    /// </summary>
    public void Dispose()
        => TryDispose();

    // Unsubscribes and reports whether this call was the one that did it.
    internal bool TryDispose()
    {
        var action = Interlocked.Exchange(ref unsubscribeAction, null);

        if (action == null)
            return false;

        action(this);
        return true;
    }

    /// <summary>Returns a string representation of this token.</summary>
    /// <returns>The string representation.</returns>
    public override string ToString()
        => Key != null ? $"{Id} (Key: {Key})" : Id.ToString();
}
