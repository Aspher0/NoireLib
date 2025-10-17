using System;
using System.Collections.Generic;

namespace NoireLib.Configuration;

/// <summary>
/// A static manager for handling multiple NoireLib configurations with caching and centralized access.
/// </summary>
public static class NoireConfigManager
{
    private static readonly Dictionary<Type, INoireConfig> ConfigCache = new();
    private static readonly object CacheLock = new();

    /// <summary>
    /// Gets or creates a configuration instance of the specified type.
    /// The configuration is automatically loaded from disk if it exists, otherwise a new instance is created.
    /// </summary>
    /// <typeparam name="T">The configuration type that inherits from NoireConfigBase.</typeparam>
    /// <returns>The configuration instance, or null if creation/loading failed.</returns>
    public static T? GetConfig<T>() where T : NoireConfigBase, new()
    {
        var type = typeof(T);

        lock (CacheLock)
        {
            if (ConfigCache.TryGetValue(type, out var cachedConfig))
                return cachedConfig as T;

            try
            {
                var config = new T();
                config.Load();
                ConfigCache[type] = config;
                return config;
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Failed to get or create configuration of type: {type.Name}", "[NoireConfigManager] ");
                return null;
            }
        }
    }

    /// <summary>
    /// Gets or creates a configuration instance of the specified type without caching.
    /// This is useful if you want a fresh instance every time.
    /// </summary>
    /// <typeparam name="T">The configuration type that inherits from NoireConfigBase.</typeparam>
    /// <returns>The configuration instance, or null if creation/loading failed.</returns>
    public static T? LoadConfigFresh<T>() where T : NoireConfigBase, new()
    {
        try
        {
            var config = new T();
            config.Load();
            return config;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to load fresh configuration of type: {typeof(T).Name}", "[NoireConfigManager] ");
            return null;
        }
    }

    /// <summary>
    /// Saves a configuration instance to disk and updates the cache.
    /// </summary>
    /// <typeparam name="T">The configuration type that inherits from NoireConfigBase.</typeparam>
    /// <param name="config">The configuration instance to save.</param>
    /// <returns>True if the save operation was successful; otherwise, false.</returns>
    public static bool SaveConfig<T>(T config) where T : NoireConfigBase
    {
        if (config == null)
        {
            NoireLogger.LogWarning("Cannot save null configuration.", "[NoireConfigManager] ");
            return false;
        }

        var success = config.Save();

        if (success)
        {
            lock (CacheLock)
            {
                ConfigCache[typeof(T)] = config;
            }
        }

        return success;
    }

    /// <summary>
    /// Updates a configuration using an action and saves it automatically.
    /// </summary>
    /// <typeparam name="T">The configuration type that inherits from NoireConfigBase.</typeparam>
    /// <param name="updateAction">The action to perform on the configuration before saving.</param>
    /// <returns>True if the update and save were successful; otherwise, false.</returns>
    public static bool UpdateConfig<T>(Action<T> updateAction) where T : NoireConfigBase, new()
    {
        var config = GetConfig<T>();
        if (config == null)
            return false;

        try
        {
            updateAction(config);
            return SaveConfig(config);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to update configuration of type: {typeof(T).Name}", "[NoireConfigManager] ");
            return false;
        }
    }

    /// <summary>
    /// Reloads a configuration from disk and updates the cache.
    /// </summary>
    /// <typeparam name="T">The configuration type that inherits from NoireConfigBase.</typeparam>
    /// <returns>The reloaded configuration instance, or null if the reload failed.</returns>
    public static T? ReloadConfig<T>() where T : NoireConfigBase, new()
    {
        lock (CacheLock)
        {
            ConfigCache.Remove(typeof(T));
        }

        return GetConfig<T>();
    }

    /// <summary>
    /// Removes a configuration from the cache without deleting the file.
    /// </summary>
    /// <typeparam name="T">The configuration type to remove from cache.</typeparam>
    /// <returns>True if the configuration was removed from cache; otherwise, false.</returns>
    public static bool UnloadConfig<T>() where T : NoireConfigBase
    {
        lock (CacheLock)
        {
            return ConfigCache.Remove(typeof(T));
        }
    }

    /// <summary>
    /// Deletes a configuration file from disk and removes it from cache.
    /// </summary>
    /// <typeparam name="T">The configuration type to delete.</typeparam>
    /// <returns>True if the deletion was successful; otherwise, false.</returns>
    public static bool DeleteConfig<T>() where T : NoireConfigBase, new()
    {
        var config = GetConfig<T>();
        if (config == null)
            return false;

        var success = config.Delete();

        if (success)
        {
            lock (CacheLock)
            {
                ConfigCache.Remove(typeof(T));
            }
        }

        return success;
    }

    /// <summary>
    /// Checks if a configuration file exists on disk.
    /// </summary>
    /// <typeparam name="T">The configuration type to check.</typeparam>
    /// <returns>True if the configuration file exists; otherwise, false.</returns>
    public static bool ConfigExists<T>() where T : NoireConfigBase, new()
    {
        var config = new T();
        return config.Exists();
    }

    /// <summary>
    /// Clears all cached configurations without deleting the files.
    /// </summary>
    public static void ClearCache()
    {
        lock (CacheLock)
        {
            ConfigCache.Clear();
            NoireLogger.LogDebug("Configuration cache cleared.", "[NoireConfigManager] ");
        }
    }

    /// <summary>
    /// Gets the number of configurations currently cached.
    /// </summary>
    /// <returns>The number of cached configurations.</returns>
    public static int GetCachedConfigCount()
    {
        lock (CacheLock)
        {
            return ConfigCache.Count;
        }
    }

    /// <summary>
    /// Saves all cached configurations to disk.
    /// </summary>
    /// <returns>True if all configurations were saved successfully; otherwise, false.</returns>
    public static bool SaveAllCached()
    {
        lock (CacheLock)
        {
            var allSuccess = true;

            foreach (var config in ConfigCache.Values)
            {
                if (!config.Save())
                    allSuccess = false;
            }

            return allSuccess;
        }
    }

    /// <summary>
    /// Gets the configuration directory path for the current plugin.
    /// </summary>
    /// <returns>The full path to the plugin's configuration directory, or null if NoireLib is not initialized.</returns>
    public static string? GetConfigDirectoryPath()
    {
        if (NoireService.PluginInstance == null || NoireService.PluginInterface == null)
        {
            NoireLogger.LogError("Cannot get config directory path: NoireLib is not initialized.", "[NoireConfigManager] ");
            return null;
        }

        var configDirectory = NoireService.PluginInterface.ConfigDirectory;
        return configDirectory.FullName;
    }
}
