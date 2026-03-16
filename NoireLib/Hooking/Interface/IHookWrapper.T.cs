using System;

namespace NoireLib.Hooking;

/// <summary>
/// Defines the common contract for high-level hook wrappers with strongly typed detours.
/// </summary>
/// <typeparam name="TDelegate">The delegate type used by the underlying hook.</typeparam>
public interface IHookWrapper<TDelegate> : IHookWrapper
    where TDelegate : Delegate
{
    /// <summary>
    /// Gets the name of the <typeparamref name="TDelegate"/> type used by the hook.
    /// </summary>
    string? HookName { get; }

    /// <summary>
    /// Gets the full name of the <typeparamref name="TDelegate"/> type used by the hook.
    /// </summary>
    string? HookFullName { get; }

    /// <summary>
    /// Gets the original, unhooked delegate.
    /// </summary>
    TDelegate Original { get; }

    /// <summary>
    /// Gets the original, unhooked delegate that remains available after disposal.
    /// </summary>
    TDelegate OriginalDisposeSafe { get; }

    /// <summary>
    /// Gets the detour delegate assigned to the hook.
    /// </summary>
    TDelegate Detour { get; }

    /// <summary>
    /// Registers or replaces a keyed detour callback.
    /// </summary>
    /// <param name="key">The unique key associated with the callback.</param>
    /// <param name="callback">The detour callback to register.</param>
    void AddCallback(string key, TDelegate callback);

    /// <summary>
    /// Determines whether a detour callback has been registered with the specified key.
    /// </summary>
    /// <param name="key">The detour callback key to look up.</param>
    /// <returns>True if a detour callback exists for the key; otherwise, false.</returns>
    bool ContainsCallback(string key);

    /// <summary>
    /// Removes the detour callback associated with the specified key.
    /// </summary>
    /// <param name="key">The detour callback key to remove.</param>
    /// <returns>True if a detour callback was removed; otherwise, false.</returns>
    bool RemoveCallback(string key);

    /// <summary>
    /// Removes all registered detour callbacks.
    /// </summary>
    void ClearCallbacks();

    /// <summary>
    /// Invokes all registered detour callbacks using the provided invoker.
    /// </summary>
    /// <param name="invoker">The action used to invoke each registered callback.</param>
    void InvokeCallbacks(Action<TDelegate> invoker);
}
