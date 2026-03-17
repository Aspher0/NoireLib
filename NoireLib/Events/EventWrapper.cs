using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace NoireLib.Events;

/// <summary>
/// Provides a high-level wrapper around any .NET event subscription.
/// </summary>
public class EventWrapper : IEventWrapper
{
    private readonly Dictionary<string, Delegate> callbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action<IEventWrapper, EventCallbackKind>> stateCallbacks = new(StringComparer.Ordinal);
    private readonly string disposeRegistrationKey;
    private readonly Action<Delegate> subscribe;
    private readonly Action<Delegate> unsubscribe;
    private bool isDisposed;
    private bool isEnabled;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventWrapper"/> class from explicit subscribe and unsubscribe actions.
    /// </summary>
    /// <param name="handlerType">The delegate type used by the wrapped event.</param>
    /// <param name="subscribe">The action used to subscribe the generated handler.</param>
    /// <param name="unsubscribe">The action used to unsubscribe the generated handler.</param>
    /// <param name="autoEnable">Whether the wrapper should be enabled immediately after creation.</param>
    /// <param name="name">An optional friendly name for the wrapper.</param>
    public EventWrapper(Type handlerType, Action<Delegate> subscribe, Action<Delegate> unsubscribe, bool autoEnable = false, string? name = null)
        : this(handlerType, subscribe, unsubscribe, autoEnable, name, handlerType.Name, handlerType.FullName)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EventWrapper"/> class from an event name on a target object.
    /// </summary>
    /// <param name="target">The object exposing the event to wrap.</param>
    /// <param name="eventName">The name of the event to wrap.</param>
    /// <param name="autoEnable">Whether the wrapper should be enabled immediately after creation.</param>
    /// <param name="name">An optional friendly name for the wrapper.</param>
    public EventWrapper(object target, string eventName, bool autoEnable = false, string? name = null)
        : this(CreateRegistration(target, eventName), autoEnable, name)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EventWrapper"/> class from reflected event metadata.
    /// </summary>
    /// <param name="eventInfo">The reflected event metadata.</param>
    /// <param name="target">The object exposing the event to wrap.</param>
    /// <param name="autoEnable">Whether the wrapper should be enabled immediately after creation.</param>
    /// <param name="name">An optional friendly name for the wrapper.</param>
    public EventWrapper(EventInfo eventInfo, object target, bool autoEnable = false, string? name = null)
        : this(CreateRegistration(target, eventInfo), autoEnable, name)
    {
    }

    /// <summary>
    /// Gets the friendly wrapper name.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets the wrapped event name.
    /// </summary>
    public string EventName { get; }

    /// <summary>
    /// Gets the full wrapped event name.
    /// </summary>
    public string? EventFullName { get; }

    /// <summary>
    /// Gets the handler delegate type used by the wrapped event.
    /// </summary>
    public Type HandlerType { get; }

    /// <summary>
    /// Gets the internal dispatch handler subscribed to the wrapped event.
    /// </summary>
    public Delegate Handler { get; }

    /// <summary>
    /// Gets a value indicating whether the wrapped event is currently subscribed.
    /// </summary>
    public bool IsEnabled => isEnabled;

    /// <summary>
    /// Gets a value indicating whether the wrapper has been disposed.
    /// </summary>
    public bool IsDisposed => isDisposed;

    /// <summary>
    /// Gets the registered callback keys.
    /// </summary>
    public IReadOnlyCollection<string> CallbackKeys
    {
        get
        {
            lock (callbacks)
                return callbacks.Keys.ToArray();
        }
    }

    /// <summary>
    /// Gets the registered state callback keys.
    /// </summary>
    public IReadOnlyCollection<string> StateCallbackKeys
    {
        get
        {
            lock (stateCallbacks)
                return stateCallbacks.Keys.ToArray();
        }
    }

    /// <summary>
    /// Enables the wrapped event subscription if it is not already enabled.
    /// </summary>
    public void Enable()
    {
        ThrowIfDisposed();

        if (IsEnabled)
            return;

        subscribe(Handler);
        isEnabled = true;
        InvokeStateCallbacks(EventCallbackKind.Enabled);
    }

    /// <summary>
    /// Disables the wrapped event subscription if it is currently enabled.
    /// </summary>
    public void Disable()
    {
        ThrowIfDisposed();

        if (!IsEnabled)
            return;

        unsubscribe(Handler);
        isEnabled = false;
        InvokeStateCallbacks(EventCallbackKind.Disabled);
    }

    /// <summary>
    /// Sets the enabled state of the wrapped event subscription.
    /// </summary>
    /// <param name="enabled">The desired enabled state.</param>
    /// <returns>True if the subscription state changed; otherwise, false.</returns>
    public bool SetEnabled(bool enabled)
    {
        if (enabled)
        {
            if (IsEnabled)
                return false;

            Enable();
            return true;
        }

        if (!IsEnabled)
            return false;

        Disable();
        return true;
    }

    /// <summary>
    /// Registers or replaces a keyed callback.
    /// </summary>
    /// <param name="key">The unique key associated with the callback.</param>
    /// <param name="callback">The callback to register.</param>
    public void AddCallback(string key, Delegate callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(callback);

        EnsureCompatibleCallback(callback);

        lock (callbacks)
            callbacks[key] = callback;
    }

    /// <summary>
    /// Determines whether a callback has been registered with the specified key.
    /// </summary>
    /// <param name="key">The callback key to look up.</param>
    /// <returns>True if a callback exists for the key; otherwise, false.</returns>
    public bool ContainsCallback(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (callbacks)
            return callbacks.ContainsKey(key);
    }

    /// <summary>
    /// Removes the callback associated with the specified key.
    /// </summary>
    /// <param name="key">The callback key to remove.</param>
    /// <returns>True if a callback was removed; otherwise, false.</returns>
    public bool RemoveCallback(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (callbacks)
            return callbacks.Remove(key);
    }

    /// <summary>
    /// Removes all registered callbacks.
    /// </summary>
    public void ClearCallbacks()
    {
        lock (callbacks)
            callbacks.Clear();
    }

    /// <summary>
    /// Invokes all registered callbacks using the provided event arguments.
    /// </summary>
    /// <param name="arguments">The arguments to forward to each registered callback.</param>
    public void InvokeCallbacks(params object?[] arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        ValidateArguments(arguments);

        var callbacksSnapshot = GetCallbacksSnapshot();

        foreach (var callback in callbacksSnapshot)
        {
            try
            {
                callback.DynamicInvoke(arguments);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError<EventWrapper>(ex, $"An event callback failed for '{Name}'.");
            }
        }
    }

    /// <summary>
    /// Registers or replaces a keyed callback that is invoked when the wrapper state changes.
    /// </summary>
    /// <param name="key">The unique key associated with the callback.</param>
    /// <param name="callback">The callback to register.</param>
    public void AddStateCallback(string key, Action<IEventWrapper, EventCallbackKind> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(callback);

        lock (stateCallbacks)
            stateCallbacks[key] = callback;
    }

    /// <summary>
    /// Determines whether a state callback has been registered with the specified key.
    /// </summary>
    /// <param name="key">The state callback key to look up.</param>
    /// <returns>True if a state callback exists for the key; otherwise, false.</returns>
    public bool ContainsStateCallback(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (stateCallbacks)
            return stateCallbacks.ContainsKey(key);
    }

    /// <summary>
    /// Removes the state callback associated with the specified key.
    /// </summary>
    /// <param name="key">The state callback key to remove.</param>
    /// <returns>True if a state callback was removed; otherwise, false.</returns>
    public bool RemoveStateCallback(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (stateCallbacks)
            return stateCallbacks.Remove(key);
    }

    /// <summary>
    /// Removes all registered state callbacks.
    /// </summary>
    public void ClearStateCallbacks()
    {
        lock (stateCallbacks)
            stateCallbacks.Clear();
    }

    /// <summary>
    /// Disposes the wrapper and unregisters it from NoireLib disposal.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        try
        {
            if (IsEnabled)
            {
                unsubscribe(Handler);
                isEnabled = false;
            }
        }
        finally
        {
            isDisposed = true;
            NoireLibMain.UnregisterOnDispose(disposeRegistrationKey);
            InvokeStateCallbacks(EventCallbackKind.Disposed);
            ClearCallbacks();
            ClearStateCallbacks();
            GC.SuppressFinalize(this);

            NoireLogger.LogInfo(this, $"Event wrapper '{Name}' disposed ({HandlerType.Name}).");
        }
    }

    /// <summary>
    /// Returns a snapshot of the currently registered callbacks.
    /// </summary>
    /// <returns>An array containing the registered callbacks at the time of the call.</returns>
    protected Delegate[] GetCallbacksSnapshot()
    {
        lock (callbacks)
            return callbacks.Values.ToArray();
    }

    private EventWrapper(ResolvedEventRegistration registration, bool autoEnable, string? name)
        : this(registration.HandlerType, registration.Subscribe, registration.Unsubscribe, autoEnable, name, registration.EventName, registration.EventFullName)
    {
    }

    private EventWrapper(Type handlerType, Action<Delegate> subscribe, Action<Delegate> unsubscribe, bool autoEnable, string? name, string eventName, string? eventFullName)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);

        if (!typeof(Delegate).IsAssignableFrom(handlerType))
            throw new ArgumentException($"Type '{handlerType.FullName}' must derive from {nameof(Delegate)}.", nameof(handlerType));

        HandlerType = handlerType;
        this.subscribe = subscribe;
        this.unsubscribe = unsubscribe;
        EventName = eventName;
        EventFullName = eventFullName;
        Name = string.IsNullOrWhiteSpace(name) ? eventName : name;
        Handler = CreateDispatchHandler(handlerType);
        disposeRegistrationKey = $"NoireLib.EventWrapper::{eventFullName ?? eventName}::{Guid.NewGuid():N}";

        NoireLibMain.RegisterOnDispose(disposeRegistrationKey, Dispose);

        if (autoEnable)
            Enable();

        NoireLogger.LogInfo(this, $"Event wrapper '{Name}' created ({HandlerType.Name}).");
    }

    private static ResolvedEventRegistration CreateRegistration(object target, string eventName)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);

        var eventInfo = target.GetType().GetEvent(eventName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new ArgumentException($"Event '{eventName}' was not found on type '{target.GetType().FullName}'.", nameof(eventName));

        return CreateRegistration(target, eventInfo);
    }

    private static ResolvedEventRegistration CreateRegistration(object target, EventInfo eventInfo)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(eventInfo);

        var handlerType = eventInfo.EventHandlerType
            ?? throw new InvalidOperationException($"Event '{eventInfo.Name}' does not expose a handler type.");

        var addMethod = eventInfo.GetAddMethod(true)
            ?? throw new InvalidOperationException($"Event '{eventInfo.Name}' does not expose an add accessor.");

        var removeMethod = eventInfo.GetRemoveMethod(true)
            ?? throw new InvalidOperationException($"Event '{eventInfo.Name}' does not expose a remove accessor.");

        return new ResolvedEventRegistration(
            handlerType,
            callback => addMethod.Invoke(target, [callback]),
            callback => removeMethod.Invoke(target, [callback]),
            eventInfo.Name,
            $"{eventInfo.DeclaringType?.FullName}.{eventInfo.Name}");
    }

    private Delegate CreateDispatchHandler(Type handlerType)
    {
        var invokeMethod = handlerType.GetMethod(nameof(Action.Invoke))
            ?? throw new InvalidOperationException($"Delegate type '{handlerType.FullName}' does not expose an Invoke method.");

        if (invokeMethod.ReturnType != typeof(void))
            throw new InvalidOperationException($"Event handler delegate '{handlerType.FullName}' must return void.");

        var parameters = invokeMethod.GetParameters()
            .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
            .ToArray();

        var argumentsExpression = Expression.NewArrayInit(
            typeof(object),
            parameters.Select(parameter => Expression.Convert(parameter, typeof(object))));

        var dispatchMethod = typeof(EventWrapper).GetMethod(nameof(Dispatch), BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Failed to locate {nameof(Dispatch)}.");

        var body = Expression.Call(Expression.Constant(this), dispatchMethod, argumentsExpression);

        return Expression.Lambda(handlerType, body, parameters).Compile();
    }

    private void Dispatch(object?[] arguments)
    {
        InvokeCallbacks(arguments);
    }

    private void EnsureCompatibleCallback(Delegate callback)
    {
        if (!HasCompatibleSignature(callback.GetType()))
            throw new ArgumentException($"Callback delegate type '{callback.GetType().FullName}' is not compatible with event handler type '{HandlerType.FullName}'.", nameof(callback));
    }

    private bool HasCompatibleSignature(Type callbackType)
    {
        var eventInvokeMethod = HandlerType.GetMethod(nameof(Action.Invoke));
        var callbackInvokeMethod = callbackType.GetMethod(nameof(Action.Invoke));

        if (eventInvokeMethod == null || callbackInvokeMethod == null)
            return false;

        if (eventInvokeMethod.ReturnType != callbackInvokeMethod.ReturnType)
            return false;

        var eventParameters = eventInvokeMethod.GetParameters();
        var callbackParameters = callbackInvokeMethod.GetParameters();

        if (eventParameters.Length != callbackParameters.Length)
            return false;

        for (var index = 0; index < eventParameters.Length; index++)
        {
            if (!callbackParameters[index].ParameterType.IsAssignableFrom(eventParameters[index].ParameterType))
                return false;
        }

        return true;
    }

    private void ValidateArguments(object?[] arguments)
    {
        var invokeMethod = HandlerType.GetMethod(nameof(Action.Invoke))
            ?? throw new InvalidOperationException($"Delegate type '{HandlerType.FullName}' does not expose an Invoke method.");

        var parameters = invokeMethod.GetParameters();

        if (arguments.Length != parameters.Length)
            throw new ArgumentException($"Expected {parameters.Length} event argument(s) for '{EventName}', but received {arguments.Length}.", nameof(arguments));

        for (var index = 0; index < parameters.Length; index++)
        {
            var argument = arguments[index];
            var parameterType = parameters[index].ParameterType;

            if (argument == null)
            {
                if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                    throw new ArgumentException($"Argument at index {index} cannot be null for parameter type '{parameterType.FullName}'.", nameof(arguments));

                continue;
            }

            if (!parameterType.IsInstanceOfType(argument))
                throw new ArgumentException($"Argument at index {index} is of type '{argument.GetType().FullName}', but parameter type '{parameterType.FullName}' was expected.", nameof(arguments));
        }
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(Name);
    }

    private void InvokeStateCallbacks(EventCallbackKind callbackKind)
    {
        Action<IEventWrapper, EventCallbackKind>[] callbacksSnapshot;

        lock (stateCallbacks)
            callbacksSnapshot = stateCallbacks.Values.ToArray();

        foreach (var callback in callbacksSnapshot)
        {
            try
            {
                callback(this, callbackKind);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError<EventWrapper>(ex, $"A state callback failed while handling '{callbackKind}' for event wrapper '{Name}'.");
            }
        }
    }

    private readonly record struct ResolvedEventRegistration(
        Type HandlerType,
        Action<Delegate> Subscribe,
        Action<Delegate> Unsubscribe,
        string EventName,
        string? EventFullName);
}
