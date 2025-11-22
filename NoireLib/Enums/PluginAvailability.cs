using NoireLib.Helpers;

namespace NoireLib.Enums;

/// <summary>
/// Represents the availability status of a plugin for use in <see cref="InteropHelper"/>.
/// </summary>
public enum PluginAvailability
{
    /// <summary>
    /// The plugin is available and meets the required version.
    /// </summary>
    Available,
    /// <summary>
    /// The plugin has not been found.
    /// </summary>
    NotInstalled,
    /// <summary>
    /// The plugin is installed but is currently disabled.
    /// </summary>
    Disabled,
    /// <summary>
    /// The plugin is installed but does not meet the required version.
    /// </summary>
    UnsupportedVersion
}
