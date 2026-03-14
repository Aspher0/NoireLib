using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.IPC;

/// <summary>
/// Provides a reusable wrapper around an IPC message channel with event-style subscriptions.
/// </summary>
/// <typeparam name="TDelegate">The delegate type used when subscribing to the IPC event.</typeparam>
public class NoireIpcEventConsumer<TDelegate> where TDelegate : Delegate
{
    private readonly string _fullName;
    private readonly Type _messageResultType;
    private readonly Exception? _bindingError;
    private readonly object _syncRoot = new();
    private readonly Dictionary<TDelegate, Stack<NoireIpcSubscription>> _subscriptions = [];

    internal NoireIpcEventConsumer(string fullName, Type messageResultType, Exception? bindingError = null)
    {
        _fullName = fullName;
        _messageResultType = messageResultType;
        _bindingError = bindingError;
    }

    /// <summary>
    /// Gets the fully qualified IPC channel name.
    /// </summary>
    public string FullName => _fullName;

    /// <summary>
    /// Gets the number of active subscriptions created through this wrapper.
    /// </summary>
    public int SubscriptionCount
    {
        get
        {
            lock (_syncRoot)
                return _subscriptions.Values.Sum(stack => stack.Count);
        }
    }

    /// <summary>
    /// Subscribes a handler to the IPC event.
    /// </summary>
    /// <param name="handler">The handler to invoke when a message is published.</param>
    /// <returns>A subscription handle.</returns>
    public NoireIpcSubscription Subscribe(TDelegate handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        if (_bindingError != null)
            throw new InvalidOperationException($"IPC event '{_fullName}' failed to bind to delegate type '{typeof(TDelegate).FullName}'.", _bindingError);

        var subscription = NoireIPC.Subscribe(_fullName, handler, prefix: null, useDefaultPrefix: false, messageResultType: _messageResultType);

        lock (_syncRoot)
        {
            if (!_subscriptions.TryGetValue(handler, out var stack))
            {
                stack = new Stack<NoireIpcSubscription>();
                _subscriptions[handler] = stack;
            }

            stack.Push(subscription);
        }

        return subscription;
    }

    /// <summary>
    /// Attempts to subscribe a handler to the IPC event safely.
    /// </summary>
    /// <param name="handler">The handler to invoke when a message is published.</param>
    /// <param name="subscription">The created subscription when successful; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the subscription succeeded; otherwise <see langword="false"/>.</returns>
    public bool TrySubscribe(TDelegate handler, out NoireIpcSubscription? subscription)
    {
        try
        {
            subscription = Subscribe(handler);
            return true;
        }
        catch
        {
            subscription = null;
            return false;
        }
    }

    /// <summary>
    /// Unsubscribes the most recent subscription created for the specified handler.
    /// </summary>
    /// <param name="handler">The handler to unsubscribe.</param>
    /// <returns><see langword="true"/> if a subscription was removed; otherwise <see langword="false"/>.</returns>
    public bool Unsubscribe(TDelegate handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        NoireIpcSubscription? subscription = null;

        lock (_syncRoot)
        {
            if (_subscriptions.TryGetValue(handler, out var stack) && stack.Count > 0)
            {
                subscription = stack.Pop();
                if (stack.Count == 0)
                    _subscriptions.Remove(handler);
            }
        }

        if (subscription == null)
            return false;

        subscription.Dispose();
        return true;
    }

    /// <summary>
    /// Attempts to unsubscribe the most recent subscription created for the specified handler.
    /// </summary>
    public bool TryUnsubscribe(TDelegate handler)
    {
        try
        {
            return Unsubscribe(handler);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Unsubscribes every active subscription created through this wrapper.
    /// </summary>
    public void UnsubscribeAll()
    {
        List<NoireIpcSubscription> subscriptions;

        lock (_syncRoot)
        {
            subscriptions = _subscriptions.Values.SelectMany(stack => stack).ToList();
            _subscriptions.Clear();
        }

        foreach (var subscription in subscriptions)
            subscription.Dispose();
    }

    public static NoireIpcEventConsumer<TDelegate> operator +(NoireIpcEventConsumer<TDelegate> ipcEvent, TDelegate handler)
    {
        ArgumentNullException.ThrowIfNull(ipcEvent);
        ipcEvent.Subscribe(handler);
        return ipcEvent;
    }

    public static NoireIpcEventConsumer<TDelegate> operator -(NoireIpcEventConsumer<TDelegate> ipcEvent, TDelegate handler)
    {
        ArgumentNullException.ThrowIfNull(ipcEvent);
        ipcEvent.Unsubscribe(handler);
        return ipcEvent;
    }
}
