using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace NoireLib.Hooking;

using HookBackend = IGameInteropProvider.HookBackend;

/// <summary>
/// A hook on a game function, resolved from the XIVClientStructs delegate it is declared with unless an address, a
/// signature or a <see cref="HookTarget"/> says otherwise. The delegate is checked against the function at the resolved
/// address before the hook is created, and the detour runs inside a fault guard so an exception cannot reach the game.
/// </summary>
/// <typeparam name="TDelegate">The delegate type of the hooked function.</typeparam>
public sealed class NoireHook<TDelegate> : INoireHook<TDelegate>
    where TDelegate : Delegate
{
    private readonly Dictionary<string, Action<INoireHook, HookEvent>> stateCallbacks = new(StringComparer.Ordinal);
    private readonly HookOptions options;
    private readonly HookGuardContext<TDelegate> guard;
    private readonly string disposeKey;
    private readonly object gate = new();

    private Hook<TDelegate>? hook;
    private HookState state = HookState.Pending;
    private Action? retry;
    private long deferredDeadline;
    private bool disposed;

    /// <summary>Creates a hook on the function the delegate describes, resolving its address from XIVClientStructs.</summary>
    /// <param name="detour">The detour to install.</param>
    /// <param name="autoEnable">Whether the hook is enabled as soon as it installs.</param>
    /// <param name="name">A name for logs and diagnostics, defaulting to the delegate type name.</param>
    /// <param name="backend">The Dalamud hook backend.</param>
    public NoireHook(TDelegate detour, bool autoEnable = false, string? name = null, HookBackend backend = HookBackend.Automatic)
        : this(detour, null, Configure(autoEnable, name, backend), null)
    {
    }

    /// <summary>Creates a hook on the function the delegate describes, resolving its address from XIVClientStructs.</summary>
    /// <param name="detour">The detour to install.</param>
    /// <param name="options">The options to apply.</param>
    public NoireHook(TDelegate detour, HookOptions options)
        : this(detour, null, options, null)
    {
    }

    /// <summary>Creates a hook at an explicit function address.</summary>
    /// <param name="procAddress">The function address.</param>
    /// <param name="detour">The detour to install.</param>
    /// <param name="autoEnable">Whether the hook is enabled as soon as it installs.</param>
    /// <param name="name">A name for logs and diagnostics.</param>
    /// <param name="backend">The Dalamud hook backend.</param>
    public NoireHook(nint procAddress, TDelegate detour, bool autoEnable = false, string? name = null, HookBackend backend = HookBackend.Automatic)
        : this(detour, HookTarget.Address(procAddress), Configure(autoEnable, name, backend), null)
    {
    }

    /// <summary>Creates a hook on the function a byte signature resolves to.</summary>
    /// <param name="signature">The byte signature to scan for.</param>
    /// <param name="detour">The detour to install.</param>
    /// <param name="autoEnable">Whether the hook is enabled as soon as it installs.</param>
    /// <param name="name">A name for logs and diagnostics.</param>
    /// <param name="backend">The Dalamud hook backend.</param>
    public NoireHook(string signature, TDelegate detour, bool autoEnable = false, string? name = null, HookBackend backend = HookBackend.Automatic)
        : this(detour, HookTarget.Signature(signature), Configure(autoEnable, name, backend), null)
    {
    }

    /// <summary>Creates a hook at an explicit function address.</summary>
    /// <param name="procAddress">The function address.</param>
    /// <param name="detour">The detour to install.</param>
    /// <param name="options">The options to apply.</param>
    public NoireHook(nint procAddress, TDelegate detour, HookOptions options)
        : this(detour, HookTarget.Address(procAddress), options, null)
    {
    }

    /// <summary>Creates a hook on the function a byte signature resolves to.</summary>
    /// <param name="signature">The byte signature to scan for.</param>
    /// <param name="detour">The detour to install.</param>
    /// <param name="options">The options to apply.</param>
    public NoireHook(string signature, TDelegate detour, HookOptions options)
        : this(detour, HookTarget.Signature(signature), options, null)
    {
    }

    /// <summary>Creates a hook on the function a target points at.</summary>
    /// <param name="target">Where the function lives.</param>
    /// <param name="detour">The detour to install.</param>
    /// <param name="autoEnable">Whether the hook is enabled as soon as it installs.</param>
    /// <param name="name">A name for logs and diagnostics.</param>
    /// <param name="backend">The Dalamud hook backend.</param>
    public NoireHook(HookTarget target, TDelegate detour, bool autoEnable = false, string? name = null, HookBackend backend = HookBackend.Automatic)
        : this(detour, target, Configure(autoEnable, name, backend), null)
    {
    }

    /// <summary>Creates a hook on the function a target points at.</summary>
    /// <param name="target">Where the function lives.</param>
    /// <param name="detour">The detour to install.</param>
    /// <param name="options">The options to apply.</param>
    public NoireHook(HookTarget target, TDelegate detour, HookOptions options)
        : this(detour, target, options, null)
    {
    }

    /// <summary>
    /// Adopts an existing Dalamud hook so it joins the registry, the groups and the diagnostics. Its detour was fixed
    /// at creation, so no fault guard can be applied to it.
    /// </summary>
    /// <param name="existing">The hook to adopt.</param>
    /// <param name="detour">The detour it was created with.</param>
    /// <param name="autoEnable">Whether the hook is enabled immediately.</param>
    /// <param name="name">A name for logs and diagnostics.</param>
    public NoireHook(Hook<TDelegate> existing, TDelegate detour, bool autoEnable = false, string? name = null)
        : this(detour, null, Configure(autoEnable, name, HookBackend.Automatic), existing ?? throw new ArgumentNullException(nameof(existing)))
    {
    }

    private NoireHook(TDelegate detour, HookTarget? target, HookOptions? options, Hook<TDelegate>? existing)
    {
        ArgumentNullException.ThrowIfNull(detour);

        this.options = (options ?? NoireHook.DefaultOptions).Clone();
        Detour = detour;
        Target = target ?? HookTarget.ClientStructs<TDelegate>();
        Name = string.IsNullOrWhiteSpace(this.options.Name) ? typeof(TDelegate).Name : this.options.Name!;
        Verification = HookVerificationResult.Skipped(typeof(TDelegate));
        disposeKey = $"NoireLib.NoireHook::{typeof(TDelegate).FullName}::{Guid.NewGuid():N}";

        guard = new HookGuardContext<TDelegate>
        {
            Detour = detour,
            Stats = Stats,
            Name = Name,
            FaultLimit = this.options.FaultLimit,
            CollectStats = this.options.CollectStats,
            FaultLogInterval = this.options.FaultLogInterval,
            OnFaultLimitReached = Disable,
        };

        HookRegistry.Register(this);

        if (this.options.AutoDispose)
            NoireLibMain.RegisterOnDispose(disposeKey, Dispose);

        try
        {
            if (existing != null)
                Adopt(existing);
            else
                Install();
        }
        catch
        {
            // The caller never receives an instance that threw here, so leaving it registered would keep a hook that
            // does not exist in the registry, the diagnostics, the duplicate-address check and the shutdown callbacks.
            HookRegistry.Unregister(this);
            NoireLibMain.UnregisterOnDispose(disposeKey);
            throw;
        }
    }

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string? Group
    {
        get => options.Group;
        set
        {
            if (string.Equals(options.Group, value, StringComparison.Ordinal))
                return;

            options.Group = value;
            HookRegistry.NotifyChanged();
        }
    }

    /// <inheritdoc/>
    public HookTarget Target { get; }

    /// <inheritdoc/>
    public HookState State
    {
        get
        {
            lock (gate)
                return state;
        }
    }

    /// <inheritdoc/>
    public nint Address { get; private set; }

    /// <inheritdoc/>
    public HookIdentity? Identity { get; private set; }

    /// <inheritdoc/>
    public HookVerificationResult Verification { get; private set; }

    /// <inheritdoc/>
    public HookStats Stats { get; } = new();

    /// <inheritdoc/>
    public bool CollectsStats
    {
        get => guard.CollectStats;
        set
        {
            if (guard.CollectStats == value)
                return;

            guard.CollectStats = value;
            options.CollectStats = value;
            HookRegistry.NotifyChanged();
        }
    }

    /// <inheritdoc/>
    public Type DelegateType => typeof(TDelegate);

    /// <inheritdoc/>
    public bool IsGuarded { get; private set; }

    /// <inheritdoc/>
    public TDelegate Detour { get; }

    /// <summary>Gets the options the hook was created with.</summary>
    public HookOptions Options => options;

    /// <inheritdoc/>
    public TDelegate Original => hook?.Original
        ?? throw new InvalidOperationException($"Hook '{Name}' is {State} and has no original function yet.");

    /// <inheritdoc/>
    public TDelegate OriginalDisposeSafe => hook?.OriginalDisposeSafe
        ?? throw new InvalidOperationException($"Hook '{Name}' is {State} and has no original function yet.");

    /// <inheritdoc/>
    public bool IsEnabled => hook is { IsDisposed: false, IsEnabled: true };

    /// <inheritdoc/>
    public bool IsDisposed => disposed;

    /// <inheritdoc/>
    public string BackendName => hook?.BackendName ?? string.Empty;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> StateCallbackKeys
    {
        get
        {
            lock (stateCallbacks)
                return stateCallbacks.Keys.ToArray();
        }
    }

    /// <inheritdoc/>
    public event Action<INoireHook, HookEvent>? OnHookEvent;

    /// <inheritdoc/>
    public void Enable()
    {
        lock (gate)
        {
            if (disposed)
                throw new ObjectDisposedException(Name, "Cannot enable a disposed hook.");

            if (hook == null)
            {
                // Still pending, so record the intent and let the hook come up enabled once its address resolves.
                options.AutoEnable = true;
                return;
            }

            if (hook.IsEnabled)
                return;

            hook.Enable();
        }

        HookRegistry.NotifyChanged();
        Raise(HookEvent.Enabled);
    }

    /// <inheritdoc/>
    public void Disable()
    {
        lock (gate)
        {
            if (disposed || hook is not { IsEnabled: true })
            {
                options.AutoEnable = false;
                return;
            }

            hook.Disable();
        }

        HookRegistry.NotifyChanged();
        Raise(HookEvent.Disabled);
    }

    /// <inheritdoc/>
    public bool SetEnabled(bool enabled)
    {
        if (enabled == IsEnabled)
            return false;

        if (enabled)
            Enable();
        else
            Disable();

        return true;
    }

    /// <inheritdoc/>
    public bool Toggle()
    {
        SetEnabled(!IsEnabled);
        return IsEnabled;
    }

    /// <summary>Puts the hook in a group, so it can be enabled, disabled or disposed with the rest of that group.</summary>
    /// <param name="group">The group name, or null to remove it from its group.</param>
    /// <returns>This hook, for chaining.</returns>
    public NoireHook<TDelegate> SetGroup(string? group)
    {
        Group = group;
        return this;
    }

    /// <inheritdoc/>
    public IDisposable EnabledScope() => new HookStateScope([this], true);

    /// <inheritdoc/>
    public IDisposable DisabledScope() => new HookStateScope([this], false);

    /// <inheritdoc/>
    public void AddStateCallback(string key, Action<INoireHook, HookEvent> callback)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(callback);

        lock (stateCallbacks)
            stateCallbacks[key] = callback;
    }

    /// <inheritdoc/>
    public bool ContainsStateCallback(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (stateCallbacks)
            return stateCallbacks.ContainsKey(key);
    }

    /// <inheritdoc/>
    public bool RemoveStateCallback(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (stateCallbacks)
            return stateCallbacks.Remove(key);
    }

    /// <inheritdoc/>
    public void ClearStateCallbacks()
    {
        lock (stateCallbacks)
            stateCallbacks.Clear();
    }

    /// <summary>Disposes the underlying hook and removes it from the registry.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
                return;

            disposed = true;

            if (retry != null)
            {
                HookRegistry.RemovePending(retry);
                retry = null;
            }

            hook?.Dispose();
        }

        HookRegistry.Unregister(this);
        NoireLibMain.UnregisterOnDispose(disposeKey);
        SetState(HookState.Disposed);
        ClearStateCallbacks();
        GC.SuppressFinalize(this);

        if (options.EnableLogging)
            NoireLogger.LogDebug($"Hook '{Name}' disposed.", HookLog.Prefix);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Name} [{State}] {HookSignatureFormatter.FormatAddress(Address)}";

    private static HookOptions Configure(bool autoEnable, string? name, HookBackend backend)
    {
        var configured = NoireHook.DefaultOptions.Clone();
        configured.AutoEnable = autoEnable;
        configured.Name = name;
        configured.Backend = backend;
        return configured;
    }

    private void Adopt(Hook<TDelegate> existing)
    {
        Address = existing.Address;

        lock (gate)
        {
            hook = existing;
            guard.Original = existing.Original;
        }

        RunVerification(existing.Address);
        SetState(HookState.Installed);

        if (options.AutoEnable)
            Enable();
    }

    private void Install()
    {
        var resolvesLate = Target.Kind is HookTargetKind.Symbol or HookTargetKind.Import;

        if (!resolvesLate)
        {
            var resolved = HookAddressResolver.Resolve(Target);

            if (resolved == 0)
            {
                if (Target.Kind == HookTargetKind.Deferred)
                {
                    BeginDeferredRetry();
                    return;
                }

                Fail($"Hook '{Name}' could not resolve an address for {Target.Describe()}.");
                throw new InvalidOperationException($"Hook '{Name}' could not resolve an address for {Target.Describe()}.");
            }

            Address = resolved;
            RunVerification(resolved);
        }

        var installedDetour = DetourGuardFactory.Wrap(guard, options.Guard, out var guarded);
        IsGuarded = guarded;

        var created = CreateHook(installedDetour);

        if (resolvesLate)
        {
            Address = created.Address;

            try
            {
                RunVerification(created.Address);
            }
            catch
            {
                created.Dispose();
                throw;
            }
        }

        lock (gate)
        {
            hook = created;
            guard.Original = created.Original;
        }

        SetState(HookState.Installed);

        if (options.EnableLogging)
        {
            NoireLogger.LogDebug(
                $"Hook '{Name}' installed on {HookSignatureFormatter.FormatAddress(Address)}{(Identity == null ? string.Empty : $" ({Identity.Name})")}.",
                HookLog.Prefix);
        }

        if (options.AutoEnable)
            Enable();
    }

    private Hook<TDelegate> CreateHook(TDelegate installedDetour) => Target.Kind switch
    {
        HookTargetKind.Symbol => NoireService.GameInteropProvider.HookFromSymbol(Target.ModuleName!, Target.ExportName!, installedDetour, options.Backend),
        HookTargetKind.Import => NoireService.GameInteropProvider.HookFromImport(ResolveImportModule(), Target.ModuleName!, Target.ExportName!, Target.ImportHintOrOrdinal, installedDetour),
        HookTargetKind.FunctionPointerVariable => NoireService.GameInteropProvider.HookFromFunctionPointerVariable(Target.Pointer, installedDetour),
        _ => NoireService.GameInteropProvider.HookFromAddress(Address, installedDetour, options.Backend),
    };

    private ProcessModule ResolveImportModule()
        => Target.ImportModule
        ?? Process.GetCurrentProcess().MainModule
        ?? throw new InvalidOperationException($"Hook '{Name}' could not resolve the process main module for an import target.");

    private void RunVerification(nint resolvedAddress)
    {
        if (options.Verification == HookVerificationPolicy.Ignore)
            return;

        Verification = Target.Kind == HookTargetKind.ClientStructs
            ? VerifyDeclaredDelegate(resolvedAddress)
            : ClientStructsIndex.Verify(typeof(TDelegate), resolvedAddress, options.StrictVerification);

        Identity = Verification.Identity;

        if (!Verification.IsMismatch)
            return;

        var report = $"Hook '{Name}' was declared with a delegate that does not describe the function at the address it resolved to.{Environment.NewLine}{Verification.Describe()}";

        switch (options.Verification)
        {
            case HookVerificationPolicy.Throw:
                throw new InvalidOperationException(report);
            case HookVerificationPolicy.LogError:
                NoireLogger.LogError(report, HookLog.Prefix);
                break;
            case HookVerificationPolicy.LogWarning:
                NoireLogger.LogWarning(report, HookLog.Prefix);
                break;
        }
    }

    private HookVerificationResult VerifyDeclaredDelegate(nint resolvedAddress)
    {
        // The identity comes from the delegate the address was resolved from, which is not always TDelegate: a target
        // may name one XIVClientStructs function while the hook declares its own delegate for it, and reading the
        // identity off TDelegate would make that case unverifiable.
        var sourceDelegate = Target.DelegateType ?? typeof(TDelegate);
        var identity = ClientStructsIndex.IdentifyDelegate(sourceDelegate, resolvedAddress);
        var passed = HookSignatureFormatter.Format(typeof(TDelegate));

        return identity == null
            ? HookVerificationResult.Unverifiable(typeof(TDelegate), passed)
            : ClientStructsIndex.Compare(typeof(TDelegate), identity, passed, options.StrictVerification);
    }

    private void BeginDeferredRetry()
    {
        deferredDeadline = Stopwatch.GetTimestamp() + (long)(options.ResolveTimeout.TotalSeconds * Stopwatch.Frequency);
        retry = RetryInstall;
        HookRegistry.AddPending(this, retry);

        if (options.EnableLogging)
            NoireLogger.LogDebug($"Hook '{Name}' is waiting for its address to become available.", HookLog.Prefix);
    }

    private void RetryInstall()
    {
        if (disposed)
            return;

        nint resolved;

        try
        {
            resolved = HookAddressResolver.Resolve(Target);
        }
        catch (Exception ex)
        {
            NoireLogger.LogDebug($"Hook '{Name}' could not resolve its address this frame: {ex.Message}", HookLog.Prefix);
            resolved = 0;
        }

        if (resolved == 0)
        {
            if (Stopwatch.GetTimestamp() < deferredDeadline)
                return;

            StopRetrying();
            Fail($"Hook '{Name}' gave up waiting for {Target.Describe()} after {options.ResolveTimeout.TotalSeconds:0.#} seconds.");
            return;
        }

        StopRetrying();
        Address = resolved;

        try
        {
            RunVerification(resolved);

            var installedDetour = DetourGuardFactory.Wrap(guard, options.Guard, out var guarded);
            IsGuarded = guarded;

            var created = NoireService.GameInteropProvider.HookFromAddress(resolved, installedDetour, options.Backend);

            lock (gate)
            {
                hook = created;
                guard.Original = created.Original;
            }

            SetState(HookState.Installed);

            if (options.AutoEnable)
                Enable();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Hook '{Name}' resolved its address but could not install.", HookLog.Prefix);
            SetState(HookState.Failed);
        }
    }

    private void StopRetrying()
    {
        if (retry == null)
            return;

        HookRegistry.RemovePending(retry);
        retry = null;
    }

    private void Fail(string message)
    {
        NoireLogger.LogError(message, HookLog.Prefix);
        SetState(HookState.Failed);
    }

    private void SetState(HookState next)
    {
        lock (gate)
        {
            if (state == next)
                return;

            state = next;
        }

        HookRegistry.NotifyChanged();

        switch (next)
        {
            case HookState.Installed:
                Raise(HookEvent.Installed);
                break;
            case HookState.Failed:
                Raise(HookEvent.Failed);
                break;
            case HookState.Disposed:
                Raise(HookEvent.Disposed);
                break;
        }
    }

    private void Raise(HookEvent hookEvent)
    {
        Action<INoireHook, HookEvent>[] snapshot;

        lock (stateCallbacks)
            snapshot = stateCallbacks.Values.ToArray();

        foreach (var callback in snapshot)
        {
            try
            {
                callback(this, hookEvent);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"A state callback for hook '{Name}' threw.", HookLog.Prefix);
            }
        }

        try
        {
            OnHookEvent?.Invoke(this, hookEvent);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"A hook event handler for '{Name}' threw.", HookLog.Prefix);
        }
    }
}
