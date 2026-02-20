using NoireLib.Enums;
using System;
using System.Linq;

namespace NoireLib.Helpers;

/// <summary>
/// Provides static methods for various Dalamud interop functionalities such as checking external plugin availability.
/// </summary>
public static class InteropHelper
{
    /// <summary>
    /// Determines the availability status of a plugin by its internal name and optional minimum version requirement.
    /// </summary>
    /// <param name="pluginInternalName">The internal name of the plugin to check for availability.</param>
    /// <param name="minVersion">
    /// An optional minimum version that the plugin must meet to be considered available.<br/>
    /// If null, any installed version is accepted.
    /// </param>
    /// <returns>
    /// A <see cref="PluginAvailability"/> value indicating whether the plugin is available, disabled, not
    /// installed, or does not meet the minimum version requirement.
    /// </returns>
    public static PluginAvailability IsPluginAvailable(string pluginInternalName, Version? minVersion = null)
    {
        // Get all installed plugins, including dev ones
        var plugins = NoireService.PluginInterface.InstalledPlugins.Where(x => x.InternalName == pluginInternalName);

        if (plugins.Count() == 0)
            return PluginAvailability.NotInstalled;

        if (!plugins.Any(x => x.IsLoaded))
            return PluginAvailability.Disabled;

        var supportedPlugin = plugins.FirstOrDefault(x => x.IsLoaded && x.Version >= minVersion);

        if (supportedPlugin == null)
            return PluginAvailability.UnsupportedVersion;
        else
            return PluginAvailability.Available;
    }

    /// <inheritdoc cref="IsPluginAvailable(string, Version?)"/>
    /// <param name="pluginInternalName">The internal name of the plugin to check for availability.</param>
    /// <param name="minVersion">The string representation of the minimum version that the plugin must meet to be considered available.</param>
    /// <exception cref="FormatException"></exception>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="OverflowException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public static PluginAvailability IsPluginAvailable(string pluginInternalName, string minVersion = "0.0.0.0")
        => IsPluginAvailable(pluginInternalName, Version.Parse(minVersion));
}
