using System;
using System.Reflection;

namespace NoireLib.Events;

/// <summary>
/// Provides a high-level wrapper around any .NET event subscription with strongly typed callbacks.
/// </summary>
/// <typeparam name="TDelegate">The delegate type used by the wrapped event.</typeparam>
public sealed class EventWrapper<TDelegate> : EventWrapper, IEventWrapper<TDelegate>
    where TDelegate : Delegate
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EventWrapper{TDelegate}"/> class from explicit subscribe and unsubscribe actions.
    /// </summary>
    /// <param name="subscribe">The action used to subscribe the generated handler.</param>
    /// <param name="unsubscribe">The action used to unsubscribe the generated handler.</param>
    /// <param name="autoEnable">Whether the wrapper should be enabled immediately after creation.</param>
    /// <param name="name">An optional friendly name for the wrapper.</param>
    public EventWrapper(Action<TDelegate> subscribe, Action<TDelegate> unsubscribe, bool autoEnable = false, string? name = null)
        : base(typeof(TDelegate), callback => subscribe((TDelegate)callback), callback => unsubscribe((TDelegate)callback), autoEnable, name)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EventWrapper{TDelegate}"/> class from an event name on a target object and immediately registers the provided callback.
    /// </summary>
    /// <param name="target">The object exposing the event to wrap.</param>
    /// <param name="eventName">The name of the event to wrap.</param>
    /// <param name="callback">The initial callback to register.</param>
    /// <param name="autoEnable">Whether the wrapper should be enabled immediately after creation.</param>
    /// <param name="name">An optional friendly name for the wrapper.</param>
    public EventWrapper(object target, string eventName, TDelegate callback, bool autoEnable = false, string? name = null)
        : base(target, eventName, false, name)
    {
        ArgumentNullException.ThrowIfNull(callback);

        EnsureExpectedHandlerType();
        AddCallback(callback.Method.Name, callback);

        if (autoEnable)
            Enable();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EventWrapper{TDelegate}"/> class from an event name on a target object.
    /// </summary>
    /// <param name="target">The object exposing the event to wrap.</param>
    /// <param name="eventName">The name of the event to wrap.</param>
    /// <param name="autoEnable">Whether the wrapper should be enabled immediately after creation.</param>
    /// <param name="name">An optional friendly name for the wrapper.</param>
    public EventWrapper(object target, string eventName, bool autoEnable = false, string? name = null)
        : base(target, eventName, autoEnable, name)
    {
        EnsureExpectedHandlerType();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EventWrapper{TDelegate}"/> class from reflected event metadata.
    /// </summary>
    /// <param name="eventInfo">The reflected event metadata.</param>
    /// <param name="target">The object exposing the event to wrap.</param>
    /// <param name="autoEnable">Whether the wrapper should be enabled immediately after creation.</param>
    /// <param name="name">An optional friendly name for the wrapper.</param>
    public EventWrapper(EventInfo eventInfo, object target, bool autoEnable = false, string? name = null)
        : base(eventInfo, target, autoEnable, name)
    {
        EnsureExpectedHandlerType();
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EventWrapper{TDelegate}"/> class from reflected event metadata and immediately registers the provided callback.
    /// </summary>
    /// <param name="eventInfo">The reflected event metadata.</param>
    /// <param name="target">The object exposing the event to wrap.</param>
    /// <param name="callback">The initial callback to register.</param>
    /// <param name="autoEnable">Whether the wrapper should be enabled immediately after creation.</param>
    /// <param name="name">An optional friendly name for the wrapper.</param>
    public EventWrapper(EventInfo eventInfo, object target, TDelegate callback, bool autoEnable = false, string? name = null)
        : base(eventInfo, target, false, name)
    {
        ArgumentNullException.ThrowIfNull(callback);

        EnsureExpectedHandlerType();
        AddCallback(callback.Method.Name, callback);

        if (autoEnable)
            Enable();
    }

    /// <summary>
    /// Gets the internal dispatch handler subscribed to the wrapped event.
    /// </summary>
    public new TDelegate Handler => (TDelegate)base.Handler;

    /// <summary>
    /// Registers or replaces a keyed callback.
    /// </summary>
    /// <param name="key">The unique key associated with the callback.</param>
    /// <param name="callback">The callback to register.</param>
    public void AddCallback(string key, TDelegate callback)
    {
        base.AddCallback(key, callback);
    }

    /// <summary>
    /// Invokes all registered callbacks using the provided invoker.
    /// </summary>
    /// <param name="invoker">The action used to invoke each registered callback.</param>
    public void InvokeCallbacks(Action<TDelegate> invoker)
    {
        ArgumentNullException.ThrowIfNull(invoker);

        var callbacksSnapshot = GetCallbacksSnapshot();

        foreach (var callback in callbacksSnapshot)
        {
            try
            {
                invoker((TDelegate)callback);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError<EventWrapper<TDelegate>>(ex, $"An event callback failed for '{Name}'.");
            }
        }
    }

    private void EnsureExpectedHandlerType()
    {
        if (HandlerType != typeof(TDelegate))
            throw new InvalidOperationException($"Wrapped event handler type '{HandlerType.FullName}' does not match expected delegate type '{typeof(TDelegate).FullName}'.");
    }
}
