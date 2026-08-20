using System;
using System.Collections.Generic;

namespace NoireLib.Hooking;

/// <summary>
/// The contract every <see cref="NoireHook{TDelegate}"/> satisfies, independent of its delegate type.
/// </summary>
public interface INoireHook : IDisposable
{
    /// <summary>
    /// Gets the friendly name used in logs and diagnostics.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets or sets the group this hook belongs to, or null when it is ungrouped.<br/>
    /// A group handle is a live view, so a hook moves between groups the moment this changes.
    /// </summary>
    string? Group { get; set; }

    /// <summary>
    /// Gets the lifecycle state of the hook.
    /// </summary>
    HookState State { get; }

    /// <summary>
    /// Gets the resolved function address, or zero while the hook is pending.
    /// </summary>
    nint Address { get; }

    /// <summary>
    /// Gets the target the hook resolves its address from.
    /// </summary>
    HookTarget Target { get; }

    /// <summary>
    /// Gets what XIVClientStructs declares at the resolved address, or null when it declares nothing.
    /// </summary>
    HookIdentity? Identity { get; }

    /// <summary>
    /// Gets the outcome of checking the delegate against the resolved address.
    /// </summary>
    HookVerificationResult Verification { get; }

    /// <summary>
    /// Gets the call counters, populated only while <see cref="CollectsStats"/> is set.
    /// </summary>
    HookStats Stats { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the hook counts its calls.
    /// Timings stay empty unless the hook was created with <see cref="HookOptions.CollectStats"/> already set, and a
    /// hook created with no guard and no fault limit has no wrapper to count in at all.
    /// </summary>
    bool CollectsStats { get; set; }

    /// <summary>
    /// Gets the delegate type the hook was declared with.
    /// </summary>
    Type DelegateType { get; }

    /// <summary>
    /// Gets a value indicating whether the hook is currently enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Gets a value indicating whether the hook has been disposed.
    /// </summary>
    bool IsDisposed { get; }

    /// <summary>
    /// Gets a value indicating whether the detour runs inside a fault guard.
    /// </summary>
    bool IsGuarded { get; }

    /// <summary>
    /// Gets the name of the backend used by the underlying hook, or an empty string while pending.
    /// </summary>
    string BackendName { get; }

    /// <summary>
    /// Gets the keys of the registered state callbacks.
    /// </summary>
    IReadOnlyCollection<string> StateCallbackKeys { get; }

    /// <summary>
    /// Raised when the hook installs, fails, is enabled, is disabled, or is disposed.
    /// </summary>
    event Action<INoireHook, HookEvent>? OnHookEvent;

    /// <summary>
    /// Registers or replaces a callback under a key, so it can be removed later without holding the delegate.
    /// </summary>
    /// <param name="key">The unique key.</param>
    /// <param name="callback">The callback.</param>
    void AddStateCallback(string key, Action<INoireHook, HookEvent> callback);

    /// <summary>
    /// Determines whether a state callback is registered under a key.
    /// </summary>
    /// <param name="key">The key to look up.</param>
    /// <returns>True if a callback is registered.</returns>
    bool ContainsStateCallback(string key);

    /// <summary>
    /// Removes the state callback registered under a key.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>True if a callback was removed.</returns>
    bool RemoveStateCallback(string key);

    /// <summary>
    /// Removes every registered state callback.
    /// </summary>
    void ClearStateCallbacks();

    /// <summary>
    /// Enables the hook if it is installed and not already enabled.
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
    /// <returns>True if the state changed.</returns>
    bool SetEnabled(bool enabled);

    /// <summary>
    /// Flips the enabled state of the hook.
    /// </summary>
    /// <returns>The new enabled state.</returns>
    bool Toggle();

    /// <summary>
    /// Enables the hook until the returned scope is disposed, then restores the previous state.
    /// </summary>
    /// <returns>The scope.</returns>
    IDisposable EnabledScope();

    /// <summary>
    /// Disables the hook until the returned scope is disposed, then restores the previous state.
    /// </summary>
    /// <returns>The scope.</returns>
    IDisposable DisabledScope();
}
