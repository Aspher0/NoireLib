using NoireLib.Enums;
using System;
using System.Linq;

namespace NoireLib.Helpers;

/// <summary>
/// Dalamud interop helpers, such as checking external plugin availability.
/// </summary>
public static class InteropHelper
{
    /// <summary>Determines a plugin's availability by internal name and optional minimum version.</summary>
    /// <param name="pluginInternalName">The internal name of the plugin to check for availability.</param>
    /// <param name="minVersion">Minimum version required. Null accepts any installed version.</param>
    /// <returns>The plugin's <see cref="PluginAvailability"/>.</returns>
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
    /// <param name="minVersion">String form of the minimum version required.</param>
    /// <exception cref="FormatException"></exception>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="OverflowException"></exception>
    /// <exception cref="ArgumentOutOfRangeException"></exception>
    /// <exception cref="ArgumentException"></exception>
    public static PluginAvailability IsPluginAvailable(string pluginInternalName, string minVersion = "0.0.0.0")
        => IsPluginAvailable(pluginInternalName, Version.Parse(minVersion));
}
