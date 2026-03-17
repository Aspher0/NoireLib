using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Hooking;

using HookBackend = IGameInteropProvider.HookBackend;

/// <summary>
/// Provides a high-level wrapper around a Dalamud hook instance.
/// </summary>
/// <typeparam name="TDelegate">The delegate type used by the underlying hook.</typeparam>
public sealed class HookWrapper<TDelegate> : IHookWrapper<TDelegate>
    where TDelegate : Delegate
{
    private readonly Dictionary<string, TDelegate> callbacks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Action<IHookWrapper, HookCallbackKind>> stateCallbacks = new(StringComparer.Ordinal);
    private readonly string disposeRegistrationKey;
    private readonly Hook<TDelegate> hook;

    /// <summary>
    /// Initializes a new instance of the <see cref="HookWrapper{TDelegate}"/> class and automatically resolves the target address from the XIVClientStructs delegate type.
    /// </summary>
    /// <param name="detour">The detour delegate assigned to the hook.</param>
    /// <param name="name">An optional friendly name for the hook.</param>
    /// <param name="backend">The preferred hook backend.</param>
    /// <param name="autoEnable">Whether the hook should be enabled immediately after creation.</param>
    public HookWrapper(TDelegate detour, bool autoEnable = false, string? name = null, HookBackend backend = HookBackend.Automatic)
        : this(HookWrapperFactory.CreateResolvedHook(detour, backend), detour, autoEnable, name)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HookWrapper{TDelegate}"/> class from an explicit function address.
    /// </summary>
    /// <param name="procAddress">The function address to hook.</param>
    /// <param name="detour">The detour delegate assigned to the hook.</param>
    /// <param name="name">An optional friendly name for the hook.</param>
    /// <param name="backend">The preferred hook backend.</param>
    /// <param name="autoEnable">Whether the hook should be enabled immediately after creation.</param>
    public HookWrapper(IntPtr procAddress, TDelegate detour, bool autoEnable = false, string? name = null, HookBackend backend = HookBackend.Automatic)
        : this(NoireService.GameInteropProvider.HookFromAddress(procAddress, detour, backend), detour, autoEnable, name)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HookWrapper{TDelegate}"/> class from a function signature.
    /// </summary>
    /// <param name="signature">The function signature to hook.</param>
    /// <param name="detour">The detour delegate assigned to the hook.</param>
    /// <param name="name">An optional friendly name for the hook.</param>
    /// <param name="backend">The preferred hook backend.</param>
    /// <param name="autoEnable">Whether the hook should be enabled immediately after creation.</param>
    public HookWrapper(string signature, TDelegate detour, bool autoEnable = false, string? name = null, HookBackend backend = HookBackend.Automatic)
        : this(NoireService.GameInteropProvider.HookFromSignature(signature, detour, backend), detour, autoEnable, name)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HookWrapper{TDelegate}"/> class from an already created Dalamud hook.
    /// </summary>
    /// <param name="hook">The underlying Dalamud hook instance.</param>
    /// <param name="detour">The detour delegate assigned to the hook.</param>
    /// <param name="name">An optional friendly name for the hook.</param>
    /// <param name="autoEnable">Whether the hook should be enabled immediately after creation.</param>
    public HookWrapper(Hook<TDelegate> hook, TDelegate detour, bool autoEnable = false, string? name = null)
    {
        this.hook = hook ?? throw new ArgumentNullException(nameof(hook));
        Detour = detour ?? throw new ArgumentNullException(nameof(detour));
        Name = string.IsNullOrWhiteSpace(name) ? typeof(TDelegate).Name : name;
        disposeRegistrationKey = $"NoireLib.HookWrapper::{typeof(TDelegate).FullName}::{Guid.NewGuid():N}";

        NoireLibMain.RegisterOnDispose(disposeRegistrationKey, Dispose);

        if (autoEnable)
            Enable();

        NoireLogger.LogInfo(this, $"Hook '{Name}' created ({typeof(TDelegate).Name}).");
    }

    /// <summary>
    /// Gets the friendly hook name.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Gets the name of the <typeparamref name="TDelegate"/> type used by the hook.
    /// </summary>
    public string? HookName => typeof(TDelegate).Name;

    /// <summary>
    /// Gets the full name of the <typeparamref name="TDelegate"/> type used by the hook.
    /// </summary>
    public string? HookFullName => typeof(TDelegate).FullName;

    /// <summary>
    /// Gets the address of the hooked function.
    /// </summary>
    public IntPtr Address => hook.Address;

    /// <summary>
    /// Gets the original, unhooked delegate.
    /// </summary>
    public TDelegate Original => hook.Original;

    /// <summary>
    /// Gets the original, unhooked delegate that remains available after disposal.
    /// </summary>
    public TDelegate OriginalDisposeSafe => hook.OriginalDisposeSafe;

    /// <summary>
    /// Gets the detour delegate assigned to the hook.
    /// </summary>
    public TDelegate Detour { get; }

    /// <summary>
    /// Gets a value indicating whether the hook is currently enabled.
    /// </summary>
    public bool IsEnabled => hook.IsEnabled;

    /// <summary>
    /// Gets a value indicating whether the hook has been disposed.
    /// </summary>
    public bool IsDisposed => hook.IsDisposed;

    /// <summary>
    /// Gets the name of the backend used by the underlying hook.
    /// </summary>
    public string BackendName => hook.BackendName;

    /// <summary>
    /// Gets the registered detour callback keys.
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
    /// Enables the hook if it is not already enabled.
    /// </summary>
    public void Enable()
    {
        ThrowIfDisposed();

        if (IsEnabled)
            return;

        hook.Enable();
        InvokeStateCallbacks(HookCallbackKind.Enabled);
    }

    /// <summary>
    /// Disables the hook if it is currently enabled.
    /// </summary>
    public void Disable()
    {
        ThrowIfDisposed();

        if (!IsEnabled)
            return;

        hook.Disable();
        InvokeStateCallbacks(HookCallbackKind.Disabled);
    }

    /// <summary>
    /// Sets the enabled state of the hook.
    /// </summary>
    /// <param name="enabled">The desired enabled state.</param>
    /// <returns>True if the hook state changed; otherwise, false.</returns>
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
    /// Registers or replaces a keyed detour callback.
    /// </summary>
    /// <param name="key">The unique key associated with the callback.</param>
    /// <param name="callback">The detour callback to register.</param>
    public void AddCallback(string key, TDelegate callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(callback);

        lock (callbacks)
            callbacks[key] = callback;
    }

    /// <summary>
    /// Determines whether a detour callback has been registered with the specified key.
    /// </summary>
    /// <param name="key">The detour callback key to look up.</param>
    /// <returns>True if a detour callback exists for the key; otherwise, false.</returns>
    public bool ContainsCallback(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (callbacks)
            return callbacks.ContainsKey(key);
    }

    /// <summary>
    /// Removes the detour callback associated with the specified key.
    /// </summary>
    /// <param name="key">The detour callback key to remove.</param>
    /// <returns>True if a detour callback was removed; otherwise, false.</returns>
    public bool RemoveCallback(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (callbacks)
            return callbacks.Remove(key);
    }

    /// <summary>
    /// Removes all registered detour callbacks.
    /// </summary>
    public void ClearCallbacks()
    {
        lock (callbacks)
            callbacks.Clear();
    }

    /// <summary>
    /// Invokes all registered detour callbacks using the provided invoker.
    /// </summary>
    /// <param name="invoker">The action used to invoke each registered callback.</param>
    public void InvokeCallbacks(Action<TDelegate> invoker)
    {
        ArgumentNullException.ThrowIfNull(invoker);

        TDelegate[] callbacksSnapshot;

        lock (callbacks)
            callbacksSnapshot = callbacks.Values.ToArray();

        foreach (var callback in callbacksSnapshot)
        {
            try
            {
                invoker(callback);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError<HookWrapper<TDelegate>>(ex, $"A detour callback failed for hook '{Name}'.");
            }
        }
    }

    /// <summary>
    /// Registers or replaces a keyed callback that is invoked when the hook state changes.
    /// </summary>
    /// <param name="key">The unique key associated with the callback.</param>
    /// <param name="callback">The callback to register.</param>
    public void AddStateCallback(string key, Action<IHookWrapper, HookCallbackKind> callback)
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
    /// Disposes the underlying hook and unregisters it from NoireLib disposal.
    /// </summary>
    public void Dispose()
    {
        if (IsDisposed)
            return;

        hook.Dispose();
        InvokeStateCallbacks(HookCallbackKind.Disposed);
        ClearCallbacks();
        ClearStateCallbacks();
        GC.SuppressFinalize(this);

        NoireLogger.LogInfo(this, $"Hook '{Name}' disposed ({typeof(TDelegate).Name}).");
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(Name);
    }

    private void InvokeStateCallbacks(HookCallbackKind callbackKind)
    {
        Action<IHookWrapper, HookCallbackKind>[] callbacksSnapshot;

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
                NoireLogger.LogError<HookWrapper<TDelegate>>(ex, $"A state callback failed while handling '{callbackKind}' for hook '{Name}'.");
            }
        }
    }
}
