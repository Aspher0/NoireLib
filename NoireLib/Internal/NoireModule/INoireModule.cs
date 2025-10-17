namespace NoireLib.Core.Modules;

/// <summary>
/// Interface for modules within the NoireLib library.
/// </summary>
public interface INoireModule
{
    /// <summary>
    /// Indicates whether the module is currently active.
    /// </summary>
    bool IsActive { get; set; }

    /// <summary>
    /// The identifier for this module, used to differentiate multiple modules of the same type.
    /// </summary>
    string? ModuleId { get; set; }

    /// <summary>
    /// Gets the unique instance counter for this module, used to differentiate multiple instances with the same ModuleId.
    /// </summary>
    int InstanceCounter { get; }

    /// <summary>
    /// Disposes of the module, releasing any resources.
    /// </summary>
    void Dispose();

    /// <summary>
    /// Sets the active state of the module.
    /// </summary>
    void SetActive(bool active);

    /// <summary>
    /// Activates the module.
    /// </summary>
    void Activate();

    /// <summary>
    /// Deactivates the module.
    /// </summary>
    void Deactivate();
}
