using NoireLib.Configuration;
using System;
using System.Collections.Generic;
using System.Threading;

namespace NoireLib.Core.Modules;

/// <summary>
/// Base class for modules within the NoireLib library.<br/>
/// Allows for multiple instances of the same module type with unique identifiers and instance counters.
/// </summary>
/// <typeparam name="TModule">The type of the module.</typeparam>
public abstract partial class NoireModuleBase<TModule> : INoireModule
    where TModule : NoireModuleBase<TModule>, new()
{
    private static readonly Dictionary<(Type, string), int> ModuleInstanceCounters = new();
    private static readonly object CounterLock = new();

    // Volatile: activation hooks run on the framework thread while modules read this flag from timers, thread pool
    // continuations and worker threads. A bool never tears, so the risk is a stale read, not a corrupt one.
    private volatile bool isActive = false;

    // Interlocked rather than a bool so that the first caller of Dispose can be identified atomically.
    private int disposeState = 0;

    /// <summary>
    /// Whether the module is currently active. Reads <see langword="false"/> once disposed.<br/>
    /// Assigning this property records the state without running <see cref="OnActivated"/> or
    /// <see cref="OnDeactivated"/>; call <see cref="SetActive"/> to run the transition too. This is how an
    /// <see cref="OnActivated"/> implementation can refuse its own activation.<br/>
    /// Not a disposal guard: a caller can switch a disposed module back on through this setter. Guard against
    /// outliving disposal on <see cref="IsDisposed"/> instead.
    /// </summary>
    public bool IsActive
    {
        get => isActive;
        set => isActive = value;
    }

    /// <summary>
    /// Whether <see cref="Dispose"/> has run on this module. Disposal is terminal: guard anything that must not
    /// outlive it (a timer callback, a queued delivery, a public entry point) on this.<br/>
    /// Reads <see langword="true"/> as soon as disposal is claimed, so a guard here also turns away work racing a
    /// teardown still in progress.
    /// </summary>
    protected internal bool IsDisposed => Volatile.Read(ref disposeState) != 0;

    /// <summary>
    /// The module ID, can be null.
    /// </summary>
    public string? ModuleId { get; set; } = null;

    /// <summary>
    /// The instance counter for this specific module instance, used to differentiate multiple instances of the
    /// same module type and ID.
    /// </summary>
    public int InstanceCounter { get; private set; }

    /// <summary>
    /// Defines whether to log this module's actions.
    /// </summary>
    public bool EnableLogging { get; set; } = true;

    /// <summary>
    /// Constructor for the module base class.
    /// </summary>
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

    /// <summary>
    /// Every derived class (module class) shall implement a constructor like this, calling base(moduleId, active, enableLogging)<br/>
    /// Used in <see cref="NoireLibMain.AddModule{T}(string?)"/> to create modules with specific IDs.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    public NoireModuleBase(ModuleId? moduleId = null, bool active = true, bool enableLogging = true)
    {
        ModuleId = moduleId?.Id;
        InstanceCounter = GetNextInstanceCounter();
        EnableLogging = enableLogging;
        InitializeModule();
        SetActive(active);
    }

    /// <summary>
    /// Initializes the module. Called in the constructor.
    /// </summary>
    /// <param name="args">Arguments for module initialization.</param>
    protected abstract void InitializeModule(params object?[] args);

    /// <summary>
    /// Called when the module switches from <see cref="IsActive"/> false to true, hence when activated.
    /// </summary>
    protected abstract void OnActivated();

    /// <summary>
    /// Called when the module switches from <see cref="IsActive"/> true to false, hence when deactivated.
    /// </summary>
    protected abstract void OnDeactivated();

    /// <summary>
    /// Gets the next instance counter for this specific module type and ID combination, so multiple instances with
    /// the same ID get unique counters.
    /// </summary>
    /// <returns>The next instance counter.</returns>
    private int GetNextInstanceCounter()
    {
        var moduleType = GetType();
        var key = (moduleType, ModuleId ?? string.Empty);

        lock (CounterLock)
        {
            if (!ModuleInstanceCounters.ContainsKey(key))
                ModuleInstanceCounters[key] = 0;

            return ModuleInstanceCounters[key]++;
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

    /// <summary>
    /// Sets whether to log this module's actions.
    /// </summary>
    /// <param name="enableLogging">Whether to enable logging.</param>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule SetEnableLogging(bool enableLogging)
    {
        EnableLogging = enableLogging;
        return (TModule)this;
    }

    /// <summary>
    /// Sets the active state of the module, running <see cref="OnActivated"/> or <see cref="OnDeactivated"/> only
    /// when the state actually changes.<br/>
    /// Activating a disposed module is refused; deactivating one is not, so a module can deactivate itself from
    /// its own teardown.<br/>
    /// Drive this from one thread: reading the current state and transitioning are not atomic, so concurrent
    /// callers can both run the hooks.
    /// </summary>
    /// <param name="active">Whether to activate the module.</param>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule SetActive(bool active)
    {
        if (IsActive == active)
            return (TModule)this;

        // Everything OnActivated would wire up again was released by the teardown; allowing this would attach a
        // disposed module to resources that are gone. Deactivation is still allowed because a teardown that
        // deactivates itself must still reach OnDeactivated.
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

    /// <summary>
    /// Activates the module.<br/>
    /// Does nothing on a disposed module, which <see cref="IsDisposed"/> reports.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule Activate()
    {
        if (IsActive)
            return (TModule)this;
        SetActive(true);
        return (TModule)this;
    }

    /// <summary>
    /// Deactivates the module.
    /// </summary>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule Deactivate()
    {
        if (!IsActive)
            return (TModule)this;
        SetActive(false);
        return (TModule)this;
    }

    /// <summary>
    /// Disposes the module's own resources; distinct from the public <see cref="Dispose"/>. Do not call
    /// <see cref="Dispose"/> from here, which would recurse infinitely.
    /// </summary>
    protected abstract void DisposeInternal();

    /// <summary>
    /// Runs the module's teardown. Called by <see cref="Dispose"/> exactly once, after disposal is claimed and
    /// before <see cref="IsActive"/> is cleared. Overridden by bases that own their own resources, so every
    /// module keeps <see cref="Dispose"/>'s guarantees regardless of base.
    /// </summary>
    private protected virtual void DisposeCore() => DisposeInternal();

    /// <summary>
    /// Disposes the module completely. Idempotent: a module is reachable for disposal both from its owner and
    /// from the library disposing its modules, so a second call does nothing.<br/>
    /// Once this returns, <see cref="IsDisposed"/> reads <see langword="true"/> and <see cref="IsActive"/> reads
    /// <see langword="false"/>.<br/>
    /// Do not call manually unless you manage module lifecycles yourself, without <see cref="NoireLibMain.AddModule{T}(T)"/>.
    /// </summary>
    public virtual void Dispose()
    {
        // Claimed before any teardown runs, so a second call cannot re-enter a teardown: one racing this call, or
        // one arriving after a teardown that threw partway and left the module half torn down.
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
            return;

        try
        {
            DisposeCore();
        }
        finally
        {
            // Cleared after teardown, not before: a module whose teardown deactivates itself needs IsActive still
            // true for its own SetActive(false) to reach OnDeactivated. Assigned directly rather than through
            // SetActive, so disposal never fires a deactivation hook a module did not already trigger itself.
            // The finally stops a teardown that throws partway from leaving a disposed module reporting itself as active.
            isActive = false;
        }
    }
}

/// <summary>
/// Base class for modules within the NoireLib library.<br/>
/// Allows for multiple instances of the same module type with unique identifiers and instance counters. Eagerly
/// loads <typeparamref name="TConfiguration"/> in the static constructor.
/// </summary>
/// <typeparam name="TModule">The type of the module.</typeparam>
/// <typeparam name="TConfiguration">The type of the configuration associated with the module.</typeparam>
public abstract class NoireModuleBase<TModule, TConfiguration> : NoireModuleBase<TModule>
    where TModule : NoireModuleBase<TModule, TConfiguration>, new()
    where TConfiguration : NoireConfigBase, new()
{
    static NoireModuleBase()
    {
        NoireConfigManager.GetConfig<TConfiguration>();
    }

    /// <summary>
    /// Constructor for the module base class.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="args">Arguments for module initialization.</param>
    public NoireModuleBase(string? moduleId = null, bool active = true, bool enableLogging = true, params object?[] args)
        : base(moduleId, active, enableLogging, args) { }

    /// <summary>
    /// Every derived class (module class) shall implement a constructor like this, calling base(moduleId, active, enableLogging)<br/>
    /// Used in <see cref="NoireLibMain.AddModule{T}(string?)"/> to create modules with specific IDs.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    public NoireModuleBase(ModuleId? moduleId = null, bool active = true, bool enableLogging = true)
        : base(moduleId, active, enableLogging) { }
}
