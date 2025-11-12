namespace NoireLib.Core.Modules;

/// <summary>
/// Interface for base modules within the NoireLib library.
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
    /// Gets the unique identifier for this module instance, combining ModuleId and InstanceCounter, or the module type if ModuleId is null.
    /// </summary>
    /// <returns>The unique identifier for this module.</returns>
    string GetUniqueIdentifier();

    /// <summary>
    /// Disposes the module completely, unregistering the window, if any.
    /// </summary>
    void Dispose();
}
