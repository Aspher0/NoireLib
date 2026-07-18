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
    /// Whether this module writes its own informational, debug and verbose log output.<br/>
    /// Warnings, errors and fatal messages are reported regardless of this flag. Reading it is what lets the module
    /// logging helpers skip building a log message when the module is not logging, so the flag is exposed here rather
    /// than only on the base class.
    /// </summary>
    bool EnableLogging { get; set; }

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
