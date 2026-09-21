using System;
using System.Threading;

namespace NoireLib.Websocket;

/// <summary>
/// The handle every <c>On</c> call returns. Disposing it removes that one handler and leaves every other subscriber
/// of the same event in place.
/// </summary>
public sealed class NoireSocketSubscription : IDisposable
{
    private Action? unsubscribe;

    internal NoireSocketSubscription(Action unsubscribe)
        => this.unsubscribe = unsubscribe;

    /// <summary>
    /// Gets whether the subscription has already been removed.
    /// </summary>
    public bool IsDisposed => Volatile.Read(ref unsubscribe) == null;

    /// <inheritdoc/>
    public void Dispose()
        => Interlocked.Exchange(ref unsubscribe, null)?.Invoke();
}
