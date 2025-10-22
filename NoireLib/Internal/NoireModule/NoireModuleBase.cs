using System;
using System.Collections.Generic;

namespace NoireLib.Core.Modules;

/// <summary>
/// Base class for modules within the NoireLib library.<br/>
/// Allows for multiple instances of the same module type with unique identifiers and instance counters.
/// </summary>
public abstract class NoireModuleBase : INoireModule
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
    /// <param name="active">Whether to activate the module on creation.</param>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="enableLogging">Whether to enable logging for this module.</param>
    /// <param name="args">Arguments for module initialization.</param>
    public NoireModuleBase(bool active = true, string? moduleId = null, bool enableLogging = true, params object?[] args)
    {
        ModuleId = moduleId;
        InstanceCounter = GetNextInstanceCounter();
        EnableLogging = enableLogging;
        InitializeModule(args);
        SetActive(active);
    }

    /// <summary>
    /// Every derived class (module class) shall implement a constructor like this, calling base(moduleId, active)<br/>
    /// Used in <see cref="NoireLibMain.AddModule"/> to create modules with specific IDs.
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
    public string GetUniqueIdentifier()
    {
        if (!string.IsNullOrWhiteSpace(ModuleId))
            return InstanceCounter > 0 ? $"{ModuleId}_{InstanceCounter}" : ModuleId;
        else
            return $"{GetType()}_{InstanceCounter}";
    }

    /// <summary>
    /// Sets whether to log this module's actions.
    /// </summary>
    /// <param name="enableLogging">Whether to enable logging.</param>
    /// <returns>The module instance for chaining.</returns>
    public virtual void SetEnableLogging(bool enableLogging)
    {
        EnableLogging = enableLogging;
    }

    /// <summary>
    /// Sets the active state of the module.
    /// </summary>
    /// <param name="active">Whether to activate the module.</param>
    public virtual void SetActive(bool active)
    {
        if (IsActive == active) return;

        IsActive = active;

        if (IsActive)
            OnActivated();
        else
            OnDeactivated();
    }

    /// <summary>
    /// Activates the module.
    /// </summary>
    public virtual void Activate()
    {
        if (IsActive) return;
        SetActive(true);
        OnActivated();
    }

    /// <summary>
    /// Deactivates the module.
    /// </summary>
    public virtual void Deactivate()
    {
        if (!IsActive) return;
        SetActive(false);
        OnDeactivated();
    }

    /// <summary>
    /// Disposes of the module, releasing any resources.
    /// </summary>
    public abstract void Dispose();
}
