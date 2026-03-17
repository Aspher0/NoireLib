using System;
using System.Collections.Generic;

namespace NoireLib.Events;

/// <summary>
/// Defines the common contract for high-level event wrappers.
/// </summary>
public interface IEventWrapper : IDisposable
{
    /// <summary>
    /// Gets the friendly wrapper name.
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// Gets the wrapped event name.
    /// </summary>
    string EventName { get; }

    /// <summary>
    /// Gets the full wrapped event name.
    /// </summary>
    string? EventFullName { get; }

    /// <summary>
    /// Gets the handler delegate type used by the wrapped event.
    /// </summary>
    Type HandlerType { get; }

    /// <summary>
    /// Gets the internal dispatch handler subscribed to the wrapped event.
    /// </summary>
    Delegate Handler { get; }

    /// <summary>
    /// Gets a value indicating whether the wrapped event is currently subscribed.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets a value indicating whether the wrapper has been disposed.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Gets the registered callback keys.
    /// </summary>
    IReadOnlyCollection<string> CallbackKeys { get; }

    /// <summary>
    /// Gets the registered state callback keys.
    /// </summary>
    IReadOnlyCollection<string> StateCallbackKeys { get; }

    /// <summary>
    /// Enables the wrapped event subscription if it is not already enabled.
    /// </summary>
    void Enable();

    /// <summary>
    /// Disables the wrapped event subscription if it is currently enabled.
    /// </summary>
    void Disable();

    /// <summary>
    /// Sets the enabled state of the wrapped event subscription.
    /// </summary>
    /// <param name="enabled">The desired enabled state.</param>
    /// <returns>True if the subscription state changed; otherwise, false.</returns>
    bool SetEnabled(bool enabled);

    /// <summary>
    /// Registers or replaces a keyed callback.
    /// </summary>
    /// <param name="key">The unique key associated with the callback.</param>
    /// <param name="callback">The callback to register.</param>
    void AddCallback(string key, Delegate callback);

    /// <summary>
    /// Determines whether a callback has been registered with the specified key.
    /// </summary>
    /// <param name="key">The callback key to look up.</param>
    /// <returns>True if a callback exists for the key; otherwise, false.</returns>
    bool ContainsCallback(string key);

    /// <summary>
    /// Removes the callback associated with the specified key.
    /// </summary>
    /// <param name="key">The callback key to remove.</param>
    /// <returns>True if a callback was removed; otherwise, false.</returns>
    bool RemoveCallback(string key);

    /// <summary>
    /// Removes all registered callbacks.
    /// </summary>
    void ClearCallbacks();

    /// <summary>
    /// Invokes all registered callbacks using the provided event arguments.
    /// </summary>
    /// <param name="arguments">The arguments to forward to each registered callback.</param>
    void InvokeCallbacks(params object?[] arguments);

    /// <summary>
    /// Registers or replaces a keyed callback that is invoked when the wrapper state changes.
    /// </summary>
    /// <param name="key">The unique key associated with the callback.</param>
    /// <param name="callback">The callback to register.</param>
    void AddStateCallback(string key, Action<IEventWrapper, EventCallbackKind> callback);

    /// <summary>
    /// Determines whether a state callback has been registered with the specified key.
    /// </summary>
    /// <param name="key">The state callback key to look up.</param>
    /// <returns>True if a state callback exists for the key; otherwise, false.</returns>
    bool ContainsStateCallback(string key);

    /// <summary>
    /// Removes the state callback associated with the specified key.
    /// </summary>
    /// <param name="key">The state callback key to remove.</param>
    /// <returns>True if a state callback was removed; otherwise, false.</returns>
    bool RemoveStateCallback(string key);

    /// <summary>
    /// Removes all registered state callbacks.
    /// </summary>
    void ClearStateCallbacks();
}
