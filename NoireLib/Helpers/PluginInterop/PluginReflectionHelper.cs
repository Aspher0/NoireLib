using System;
using System.Collections.Generic;

namespace NoireLib.Helpers;

/// <summary>
/// Reaches the other plugins Dalamud has installed: what they are, whether they are loaded, and the objects and
/// types inside them. Everything a plugin does not make public is reached by reflection. A member that is
/// renamed or removed reads as absent. Nothing throws.
/// <br/>
/// Dalamud loads each plugin into its own assembly context and publishes no list of them. This walks its
/// internal plugin manager. None of that is API, and a Dalamud release is free to change it. Every lookup here
/// yields an empty result. It never fails.
/// </summary>
public static class PluginReflectionHelper
{
    /// <summary>Whether Dalamud's plugin manager could be reached at all. Everything else returns empty when it cannot.</summary>
    public static bool Available => DalamudInternals.PluginManager() != null;

    /// <summary>Every plugin Dalamud has installed, loaded or not.</summary>
    /// <param name="source">Which copies to list. Every one of them by default.</param>
    /// <returns>The plugins, or empty when the plugin manager could not be reached.</returns>
    public static IReadOnlyList<ReflectedPlugin> All(PluginSource source = PluginSource.Any)
    {
        var manager = DalamudInternals.PluginManager();
        if (manager == null)
            return [];

        var plugins = new List<ReflectedPlugin>();
        foreach (var local in DalamudInternals.ReadSequence(manager, "InstalledPlugins"))
        {
            var plugin = new ReflectedPlugin(new ReflectedObject(local));
            if (Matches(plugin, source))
                plugins.Add(plugin);
        }

        return plugins;
    }

    /// <summary>Whether a plugin is the copy a source names.</summary>
    /// <param name="plugin">The plugin.</param>
    /// <param name="source">The source to test against.</param>
    /// <returns>True when it matches. <see cref="PluginSource.Any"/> matches everything.</returns>
    public static bool Matches(ReflectedPlugin plugin, PluginSource source) => source switch
    {
        PluginSource.Repository => !plugin.IsDev,
        PluginSource.Dev => plugin.IsDev,
        _ => true,
    };

    /// <summary>
    /// Finds an installed plugin by its internal name or its display name, matched without case. The internal name
    /// is the stable one. A display name can change between releases.
    /// <br/>
    /// The same plugin can be installed from a repository and from a dev directory at once. With
    /// <see cref="PluginSource.Any"/> the repository copy wins. A dev copy is only reached by asking for
    /// <see cref="PluginSource.Dev"/>.
    /// </summary>
    /// <param name="name">The internal name or the display name.</param>
    /// <param name="source">Which copy to find.</param>
    /// <returns>The plugin, or null when none matched.</returns>
    public static ReflectedPlugin? Find(string name, PluginSource source = PluginSource.Any)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // Internal name beats display name, then a repository copy beats a dev copy.
        ReflectedPlugin? best = null;
        var bestRank = int.MaxValue;

        foreach (var plugin in All(source))
        {
            var byInternalName = string.Equals(plugin.InternalName, name, StringComparison.OrdinalIgnoreCase);
            if (!byInternalName && !string.Equals(plugin.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            var rank = (byInternalName ? 0 : 2) + (plugin.IsDev ? 1 : 0);
            if (rank >= bestRank)
                continue;

            best = plugin;
            bestRank = rank;
        }

        return best;
    }

    /// <summary>
    /// Finds an installed plugin by the id that is unique to it. Prefer this over a name whenever more than one
    /// installation can carry that name.
    /// </summary>
    /// <param name="id">The plugin's <see cref="ReflectedPlugin.Id"/>.</param>
    /// <returns>The plugin, or null when nothing carries that id.</returns>
    public static ReflectedPlugin? Find(Guid id)
    {
        if (id == Guid.Empty)
            return null;

        foreach (var plugin in All())
        {
            if (plugin.Id == id)
                return plugin;
        }

        return null;
    }

    /// <summary>
    /// Finds the first installed plugin a predicate accepts, for anything a name and a source cannot narrow, such
    /// as two dev builds of one assembly told apart by their directory.
    /// </summary>
    /// <param name="predicate">What the plugin has to satisfy.</param>
    /// <returns>The first match, or null when nothing matched.</returns>
    /// <exception cref="ArgumentNullException">If <paramref name="predicate"/> is null.</exception>
    public static ReflectedPlugin? Find(Func<ReflectedPlugin, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        foreach (var plugin in All())
        {
            if (SafeExecutor.ExecuteSafely(() => predicate(plugin), false))
                return plugin;
        }

        return null;
    }

    /// <summary>Every installed plugin carrying a name.</summary>
    /// <param name="name">The internal name or the display name.</param>
    /// <param name="source">Which copies to include.</param>
    /// <returns>Every match, in the order Dalamud holds them.</returns>
    public static IReadOnlyList<ReflectedPlugin> FindAll(string name, PluginSource source = PluginSource.Any)
    {
        var matches = new List<ReflectedPlugin>();
        if (string.IsNullOrWhiteSpace(name))
            return matches;

        foreach (var plugin in All(source))
        {
            if (string.Equals(plugin.InternalName, name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(plugin.Name, name, StringComparison.OrdinalIgnoreCase))
                matches.Add(plugin);
        }

        return matches;
    }

    /// <summary>Finds an installed plugin, reporting whether it was there.</summary>
    /// <param name="name">The internal name or the display name.</param>
    /// <param name="plugin">The plugin when one matched.</param>
    /// <param name="source">Which copy to find.</param>
    /// <returns>True when a plugin matched.</returns>
    public static bool TryFind(string name, out ReflectedPlugin plugin, PluginSource source = PluginSource.Any)
    {
        var found = Find(name, source);
        plugin = found!;
        return found != null;
    }

    /// <summary>Whether a plugin is installed, whether or not it is loaded.</summary>
    /// <param name="name">The internal name or the display name.</param>
    /// <param name="source">Which copy to look for.</param>
    /// <returns>True when it is installed.</returns>
    public static bool IsInstalled(string name, PluginSource source = PluginSource.Any)
        => Find(name, source) != null;

    /// <summary>Whether a plugin is installed and currently loaded.</summary>
    /// <param name="name">The internal name or the display name.</param>
    /// <param name="source">Which copy to look for.</param>
    /// <returns>True when it is loaded.</returns>
    public static bool IsLoaded(string name, PluginSource source = PluginSource.Any)
        => Find(name, source)?.IsLoaded == true;

    /// <summary>Builds a typed facade over a plugin's object. Each contract member routes to the plugin member of the same name.</summary>
    /// <typeparam name="TContract">The interface describing what the plugin is expected to carry.</typeparam>
    /// <param name="name">The internal name or the display name.</param>
    /// <param name="strict">True to throw when a member of the contract is not on the plugin.</param>
    /// <param name="source">Which copy to bind to.</param>
    /// <returns>The facade, or null when the plugin is not installed or not loaded.</returns>
    /// <exception cref="ArgumentException">If <typeparamref name="TContract"/> is not an interface.</exception>
    public static TContract? Bind<TContract>(string name, bool strict = false, PluginSource source = PluginSource.Any)
        where TContract : class
        => Find(name, source)?.Bind<TContract>(strict);

    /// <summary>Builds a typed facade over one of a plugin's types, for a plugin whose entry points are static.</summary>
    /// <typeparam name="TContract">The interface describing what the type is expected to carry.</typeparam>
    /// <param name="name">The internal name or the display name.</param>
    /// <param name="typeName">The type's full name, namespace included.</param>
    /// <param name="strict">True to throw when a member of the contract is missing.</param>
    /// <param name="source">Which copy to bind to.</param>
    /// <returns>The facade, or null when the plugin or the type could not be reached.</returns>
    /// <exception cref="ArgumentException">If <typeparamref name="TContract"/> is not an interface.</exception>
    public static TContract? BindStatic<TContract>(
        string name,
        string typeName,
        bool strict = false,
        PluginSource source = PluginSource.Any)
        where TContract : class
        => Find(name, source)?.BindStatic<TContract>(typeName, strict);
}
