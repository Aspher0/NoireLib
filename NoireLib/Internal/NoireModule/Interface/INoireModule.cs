using System;

namespace NoireLib.Core.Modules;

/// <summary>
/// A NoireLib module. Construction runs field initializers, <c>InitializeModule</c>, <c>OnActivated</c> when active, then
/// the derived constructor body: setup <c>OnActivated</c> needs belongs in <c>InitializeModule</c>.
/// </summary>
public interface INoireModule : IDisposable
{
    /// <summary>Indicates whether the module is currently active.</summary>
    bool IsActive { get; set; }

    /// <summary>
    /// Whether this module writes its own informational, debug and verbose log output. Warnings, errors and fatal
    /// messages are reported regardless of this flag.
    /// </summary>
    bool EnableLogging { get; set; }

    /// <summary>The identifier for this module, used to differentiate multiple modules of the same type.</summary>
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

    /// <summary>Disposes the module completely, unregistering the window, if any.</summary>
    new void Dispose();

    // Covers an implementation that implements only INoireModule.Dispose, explicitly.
    void IDisposable.Dispose() => Dispose();
}
