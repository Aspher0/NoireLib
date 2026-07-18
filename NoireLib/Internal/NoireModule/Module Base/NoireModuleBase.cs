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

    // Volatile because the activation hooks are driven from the framework thread while modules read the flag from
    // timer callbacks, thread pool continuations and their own worker threads. A bool never tears, so the concern
    // is a reader carrying on against a stale value rather than reading a corrupt one.
    private volatile bool isActive = false;

    // Interlocked rather than a bool so that the first caller of Dispose can be identified atomically.
    private int disposeState = 0;

    /// <summary>
    /// Defines whether the module is currently active.<br/>
    /// Reads <see langword="false"/> once the module has been disposed.<br/>
    /// Assigning this property records the state without running <see cref="OnActivated"/> or
    /// <see cref="OnDeactivated"/>; call <see cref="SetActive"/> to run the transition as well. Assigning it is
    /// how an <see cref="OnActivated"/> implementation refuses an activation it cannot carry out.<br/>
    /// This is not a disposal guard: it says whether the module is switched on, and a caller can switch a disposed
    /// module back on through this setter. Guard work that must never outlive the module on
    /// <see cref="IsDisposed"/> instead.
    /// </summary>
    public bool IsActive
    {
        get => isActive;
        set => isActive = value;
    }

    /// <summary>
    /// Whether <see cref="Dispose"/> has run on this module.<br/>
    /// Disposal is terminal, so a module that reads <see langword="true"/> here never returns to service. Guard
    /// anything that would outlive disposal on this: a timer callback, a queued delivery, or a public entry point
    /// that would otherwise build a resource nothing is left to tear down again.<br/>
    /// Reads <see langword="true"/> as soon as disposal is claimed, so a guard placed on it also turns away work
    /// racing a teardown that is still running.
    /// </summary>
    protected internal bool IsDisposed => Volatile.Read(ref disposeState) != 0;

    /// <summary>
    /// The module ID, can be null.
    /// </summary>
    public string? ModuleId { get; set; } = null;

    /// <summary>
    /// The instance counter for this specific module instance.<br/>
    /// Used internally to differentiate between multiple instances of the same module type and ID.
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
    /// Gets the next instance counter for this specific module type and ID combination.<br/>
    /// This ensures that multiple instances of the same module with the same ID get unique counters.<br/>
    /// Prevents duplicate modules to cause crashes or unexpected behavior if the said modules have their own windows.
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
    /// Gets a unique identifier string combining the ModuleId and InstanceCounter.<br/>
    /// Used for Window IDs and other unique identification needs.
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
    /// Sets the active state of the module.<br/>
    /// Runs <see cref="OnActivated"/> or <see cref="OnDeactivated"/> when the state actually changes, and does
    /// nothing at all when it already holds the requested value.<br/>
    /// Activating a disposed module is refused, since disposal is terminal; create a new instance instead.
    /// Deactivating one is not, so that a module can deactivate itself from its own teardown.<br/>
    /// Drive this from one thread. Reading the current state and performing the transition that follows are not
    /// one atomic step, so two threads changing the state at the same time can both conclude they own the
    /// transition and run the hooks concurrently or in the wrong order.
    /// </summary>
    /// <param name="active">Whether to activate the module.</param>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule SetActive(bool active)
    {
        if (IsActive == active)
            return (TModule)this;

        // Everything OnActivated would wire back up was released by the teardown, so allowing this would attach a
        // disposed module to the framework and run it against resources that are gone. Only activation is refused:
        // disposal is claimed before a module's teardown runs, and a teardown that deactivates the module itself
        // still has to reach OnDeactivated.
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
    /// Internal method that disposes the module resources.<br/>
    /// Not to be confused with <see cref="Dispose"/>, which is the public method.<br/>
    /// Do not call <see cref="Dispose"/> in this method to avoid infinite recursion.
    /// </summary>
    protected abstract void DisposeInternal();

    /// <summary>
    /// Runs the module's teardown. Called by <see cref="Dispose"/> exactly once, after disposal has been claimed
    /// and before <see cref="IsActive"/> is cleared.<br/>
    /// Overridden by the bases that own resources of their own, so that a module keeps the guarantees
    /// <see cref="Dispose"/> makes no matter which base it derives from.
    /// </summary>
    private protected virtual void DisposeCore() => DisposeInternal();

    /// <summary>
    /// Disposes the module completely.<br/>
    /// This is here because modules may have windows. This way, windows can be disposed automatically.<br/>
    /// Tears the module down once. A module is reachable for disposal both from the consumer that owns it and
    /// from the library disposing its modules, so a second call returns having done nothing.<br/>
    /// Once this returns, <see cref="IsDisposed"/> reads <see langword="true"/> and <see cref="IsActive"/> reads
    /// <see langword="false"/>.<br/>
    /// Do not call manually unless you are managing module lifecycles yourself (i.e. Without using <see cref="NoireLibMain.AddModule{T}(T)"/>).
    /// </summary>
    public virtual void Dispose()
    {
        // Claimed before any teardown runs, so that a second call cannot re-enter a teardown, including one racing
        // this call and one arriving after a teardown that threw partway through and left the module half torn down.
        if (Interlocked.Exchange(ref disposeState, 1) != 0)
            return;

        try
        {
            DisposeCore();
        }
        finally
        {
            // Cleared after the teardown, not before it: a module whose teardown deactivates itself needs the
            // module to still read as active for its own SetActive(false) to reach OnDeactivated at all. Assigned
            // directly rather than routed through SetActive, so that disposal never runs a deactivation hook a
            // module did not already ask for; teardown belongs in DisposeInternal, and firing OnDeactivated here
            // would run it twice for every module that tears down in both places.
            // The finally is what stops a teardown that throws partway from leaving a disposed module reporting
            // itself as active.
            isActive = false;
        }
    }
}

/// <summary>
/// Base class for modules within the NoireLib library.<br/>
/// Allows for multiple instances of the same module type with unique identifiers and instance counters.<br/>
/// Will initialize the configuration of type <typeparamref name="TConfiguration"/> on static constructor to make sure it's loaded on initialization of the module.
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
