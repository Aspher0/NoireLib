using System;
using System.Collections.Generic;

namespace NoireLib.Core.Modules;

/// <summary>
/// Base class for modules within the NoireLib library.<br/>
/// Allows for multiple instances of the same module type with unique identifiers and instance counters.
/// </summary>
/// <typeparam name="TModule">The type of the module.</typeparam>
public abstract class NoireModuleBase<TModule> : INoireModule where TModule : NoireModuleBase<TModule>, new()
{
    private static readonly Dictionary<(Type, string), int> ModuleInstanceCounters = new();
    private static readonly object CounterLock = new();

    /// <summary>
    /// Defines whether the module is currently active.
    /// </summary>
    public bool IsActive { get; set; } = false;

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
    /// Sets the active state of the module.
    /// </summary>
    /// <param name="active">Whether to activate the module.</param>
    /// <returns>The module instance for chaining.</returns>
    public virtual TModule SetActive(bool active)
    {
        if (IsActive == active)
            return (TModule)this;

        IsActive = active;

        if (IsActive)
            OnActivated();
        else
            OnDeactivated();

        return (TModule)this;
    }

    /// <summary>
    /// Activates the module.
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
    /// Disposes the module completely.<br/>
    /// This is here because modules may have windows. This way, windows can be disposed automatically.<br/>
    /// Do not call manually unless you are managing module lifecycles yourself (i.e. Without using <see cref="NoireLibMain.AddModule{T}(T)"/>).
    /// </summary>
    public virtual void Dispose()
    {
        DisposeInternal();
    }
}
