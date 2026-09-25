using NoireLib.Configuration;
using System;
using System.Collections.Generic;
using System.Threading;

namespace NoireLib.Core.Modules;

/// <summary>
/// Base class for modules, several of one type told apart by ID and instance counter. See <see cref="INoireModule"/>
/// for the construction order.
/// </summary>
/// <typeparam name="TModule">The module type.</typeparam>
public abstract partial class NoireModuleBase<TModule> : INoireModule
    where TModule : NoireModuleBase<TModule>, new()
{
    // Per type and ID: the next counter, and how many live modules hold one.
    private static readonly Dictionary<(Type, string), (int Next, int Live)> ModuleInstanceCounters = new();
    private static readonly object CounterLock = new();

    // Kept: ModuleId can change after construction.
    private (Type, string) instanceCounterKey;

    // Volatile: timers and worker threads read it while the framework thread activates.
    private volatile bool isActive = false;

    private int disposeState = 0;

    /// <summary>
    /// Whether the module is active. Assigning skips <see cref="OnActivated"/> and <see cref="OnDeactivated"/>: call
    /// <see cref="SetActive"/> for the transition. Not a disposal guard, use <see cref="IsDisposed"/>.
    /// </summary>
    public bool IsActive
    {
        get => isActive;
        set => isActive = value;
    }

    /// <summary>Whether Dispose has run or started. Guard anything that must not outlive disposal on it.</summary>
    protected internal bool IsDisposed => Volatile.Read(ref disposeState) != 0;

    /// <summary>The module ID, can be null.</summary>
    public string? ModuleId { get; set; } = null;

    /// <summary>Tells instances of one type and ID apart. Restarts at zero once they are all disposed.</summary>
    public int InstanceCounter { get; private set; }

    /// <summary>Defines whether to log this module's actions.</summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>Constructor for the module base class.</summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="args">Arguments for module initialization.</param>
    public NoireModuleBase(string? moduleId = null, bool active = true, bool enableLogging = true, params object?[] args)
    {
        ModuleId = moduleId;
        InstanceCounter = GetNextInstanceCounter();
        EnableLogging = enableLogging;
        InitializeModule(args);
        SetActive(active);
    }

    /// <summary>The constructor every module mirrors for <see cref="NoireLibMain.AddModule{T}(string?)"/>.</summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether the module is active on creation.</param>
    /// <param name="enableLogging">Whether this module logs.</param>
    public NoireModuleBase(ModuleId? moduleId = null, bool active = true, bool enableLogging = true)
    {
        ModuleId = moduleId?.Id;
        InstanceCounter = GetNextInstanceCounter();
        EnableLogging = enableLogging;
        InitializeModule();
        SetActive(active);
    }

    /// <summary>Initializes the module, before <see cref="OnActivated"/> and before the derived constructor body.</summary>
    /// <param name="args">The initialization arguments.</param>
    protected abstract void InitializeModule(params object?[] args);

    /// <summary>
    /// Called when the module switches from <see cref="IsActive"/> false to true, hence when activated.
    /// </summary>
    protected abstract void OnActivated();

    /// <summary>
    /// Called when the module switches from <see cref="IsActive"/> true to false, hence when deactivated.
    /// </summary>
    protected abstract void OnDeactivated();

    private int GetNextInstanceCounter()
    {
        var key = (GetType(), ModuleId ?? string.Empty);
        instanceCounterKey = key;

        lock (CounterLock)
        {
            ModuleInstanceCounters.TryGetValue(key, out var count);
            ModuleInstanceCounters[key] = (count.Next + 1, count.Live + 1);
            return count.Next;
        }
    }

    private void ReleaseInstanceCounter()
    {
        lock (CounterLock)
        {
            if (!ModuleInstanceCounters.TryGetValue(instanceCounterKey, out var count))
                return;

            if (count.Live <= 1)
                ModuleInstanceCounters.Remove(instanceCounterKey);
            else
                ModuleInstanceCounters[instanceCounterKey] = (count.Next, count.Live - 1);
        }
    }

    /// <summary>
    /// Gets a unique identifier string combining the ModuleId and InstanceCounter, used for window IDs and other
    /// unique identification needs.
    /// </summary>
    /// <returns>The unique identifier string.</returns>
    public string GetUniqueIdentifier()
    {
        string identifier = $"{(NoireService.IsInitialized() ? NoireService.PluginInterface.InternalName : "NoireLib")}_";

        if (!string.IsNullOrWhiteSpace(ModuleId))
            identifier += InstanceCounter > 0 ? $"{ModuleId}_{InstanceCounter}" : ModuleId;
        else
            identifier += $"{GetType().Name}_{InstanceCounter}";

        return identifier;
    }

    /// <summary>Sets whether to log this module's actions.</summary>
    /// <param name="enableLogging">Whether to enable logging.</param>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule SetEnableLogging(bool enableLogging)
    {
        EnableLogging = enableLogging;
        return (TModule)this;
    }

    /// <summary>
    /// Sets the active state, running the hook only on a change. A disposed module cannot be activated. Not atomic:
    /// drive it from one thread.
    /// </summary>
    /// <param name="active">Whether to activate the module.</param>
    /// <returns>This module.</returns>
    public virtual TModule SetActive(bool active)
    {
        if (IsActive == active)
            return (TModule)this;

        // Teardown released everything OnActivated would wire. Deactivating stays allowed for a self-deactivating teardown.
        if (active && IsDisposed)
        {
            NoireLogger.LogWarning((TModule)this, "Cannot activate a disposed module. Create a new instance instead.");
            return (TModule)this;
        }

        IsActive = active;

        if (IsActive)
            OnActivated();
        else
            OnDeactivated();

        return (TModule)this;
    }

    /// <summary>Activates the module. Does nothing once disposed.</summary>
    /// <returns>This module.</returns>
    public virtual TModule Activate()
    {
        if (IsActive)
            return (TModule)this;
        SetActive(true);
        return (TModule)this;
    }

    /// <summary>Deactivates the module.</summary>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule Deactivate()
    {
        if (!IsActive)
            return (TModule)this;
        SetActive(false);
        return (TModule)this;
    }

    /// <summary>Releases the module's own resources. Never call <see cref="Dispose"/> from here.</summary>
    protected abstract void DisposeInternal();

    // Called once by Dispose, before IsActive clears. Bases owning resources override it.
    private protected virtual void DisposeCore() => DisposeInternal();

    /// <summary>
    /// Disposes the module. A second call does nothing. Only call it for a module not added through
    /// <see cref="NoireLibMain.AddModule{T}(T)"/>.
    /// </summary>
    public virtual void Dispose()
    {
        // Claimed first: no second call re-enters a teardown, racing or after a partial throw.
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
            return;

        try
        {
            DisposeCore();
        }
        finally
        {
            // Cleared after teardown: a self-deactivating teardown still reaches OnDeactivated. Assigned directly, never firing a hook.
            isActive = false;
            ReleaseInstanceCounter();
        }
    }
}

/// <summary>Base class for modules with a configuration, loaded eagerly by the static constructor.</summary>
/// <typeparam name="TModule">The module type.</typeparam>
/// <typeparam name="TConfiguration">The module's configuration type.</typeparam>
public abstract class NoireModuleBase<TModule, TConfiguration> : NoireModuleBase<TModule>
    where TModule : NoireModuleBase<TModule, TConfiguration>, new()
    where TConfiguration : NoireConfigBase, new()
{
    static NoireModuleBase()
    {
        NoireConfigManager.GetConfig<TConfiguration>();
    }

    /// <summary>Constructor for the module base class.</summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="args">Arguments for module initialization.</param>
    public NoireModuleBase(string? moduleId = null, bool active = true, bool enableLogging = true, params object?[] args)
        : base(moduleId, active, enableLogging, args) { }

    /// <summary>The constructor every module mirrors for <see cref="NoireLibMain.AddModule{T}(string?)"/>.</summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether the module is active on creation.</param>
    /// <param name="enableLogging">Whether this module logs.</param>
    public NoireModuleBase(ModuleId? moduleId = null, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }
}
