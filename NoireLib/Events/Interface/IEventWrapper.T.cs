using System;

namespace NoireLib.Events;

/// <summary>
/// Defines the common contract for high-level event wrappers with strongly typed callbacks.
/// </summary>
/// <typeparam name="TDelegate">The delegate type used by the wrapped event.</typeparam>
public interface IEventWrapper<TDelegate> : IEventWrapper
    where TDelegate : Delegate
{
    /// <summary>
    /// Gets the internal dispatch handler subscribed to the wrapped event.
    /// </summary>
    new TDelegate Handler { get; }

    /// <summary>
    /// Registers or replaces a keyed callback.
    /// </summary>
    /// <param name="key">The unique key associated with the callback.</param>
    /// <param name="callback">The callback to register.</param>
    void AddCallback(string key, TDelegate callback);

    /// <summary>
    /// Invokes all registered callbacks using the provided invoker.
    /// </summary>
    /// <param name="invoker">The action used to invoke each registered callback.</param>
    void InvokeCallbacks(Action<TDelegate> invoker);
}
