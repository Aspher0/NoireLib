using System;
using System.Collections.Generic;

namespace NoireLib;

/// <summary>
/// Base class for modules within the NoireLib library.<br/>
/// Allows for multiple instances of the same module type with unique identifiers and instance counters.
/// </summary>
public abstract class NoireModuleBase : INoireModule
{
    private static readonly Dictionary<(Type, string), int> ModuleInstanceCounters = new();
    private static readonly object CounterLock = new();

    public bool IsActive { get; set; } = false;
    public string? ModuleId { get; set; } = null;
    public int InstanceCounter { get; private set; }

    public NoireModuleBase(bool active = false, string? moduleId = null)
    {
        ModuleId = moduleId;
        InstanceCounter = GetNextInstanceCounter();
        InitializeModule();
        SetActive(active);
    }

    /// <summary>
    /// Every derived class (module class) shall implement a constructor like this, calling base(moduleId, active)<br/>
    /// Used in <see cref="NoireLibMain.AddModule"/> to create modules with specific IDs.
    /// </summary>
    /// <param name="moduleId">The module ID.</param>
    /// <param name="active">Whether to activate the module on creation.</param>
    public NoireModuleBase(ModuleId moduleId, bool active = false)
    {
        ModuleId = moduleId?.Id;
        InstanceCounter = GetNextInstanceCounter();
        InitializeModule();
        SetActive(active);
    }

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
    /// Initializes the module. Called in the constructor.
    /// </summary>
    protected abstract void InitializeModule();

    /// <summary>
    /// Called when the module switches from <see cref="IsActive"/> false to true, hence when activated.
    /// </summary>
    protected abstract void OnActivated();

    /// <summary>
    /// Called when the module switches from <see cref="IsActive"/> true to false, hence when deactivated.
    /// </summary>
    protected abstract void OnDeactivated();

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
