using System;
using System.Collections.Generic;

namespace NoireLib.Hooking;

/// <summary>
/// Defines the common contract for high-level hook wrappers.
/// </summary>
public interface IHookWrapper : IDisposable
{
    /// <summary>
    /// Gets the friendly hook name.
    /// </summary>
    string? Name { get; }

    /// <summary>
    /// Gets the address of the hooked function.
    /// </summary>
    IntPtr Address { get; }

    /// <summary>
    /// Gets the backend name used by the underlying hook.
    /// </summary>
    string BackendName { get; }

    /// <summary>
    /// Gets a value indicating whether the hook is currently enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets a value indicating whether the hook has been disposed.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Gets the registered state callback keys.
    /// </summary>
    IReadOnlyCollection<string> StateCallbackKeys { get; }

    /// <summary>
    /// Enables the hook if it is not already enabled.
    /// </summary>
    void Enable();

    /// <summary>
    /// Disables the hook if it is currently enabled.
    /// </summary>
    void Disable();

    /// <summary>
    /// Sets the enabled state of the hook.
    /// </summary>
    /// <param name="enabled">The desired enabled state.</param>
    /// <returns>True if the hook state changed; otherwise, false.</returns>
    bool SetEnabled(bool enabled);

    /// <summary>
    /// Registers or replaces a keyed callback that is invoked when the hook state changes.
    /// </summary>
    /// <param name="key">The unique key associated with the callback.</param>
    /// <param name="callback">The callback to register.</param>
    void AddStateCallback(string key, Action<IHookWrapper, HookCallbackKind> callback);

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
