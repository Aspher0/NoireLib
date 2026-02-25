using NoireLib.Configuration.Migrations;
using NoireLib.Helpers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NoireLib.Configuration;

/// <summary>
/// A static manager for handling multiple NoireLib configurations with caching and centralized access.
/// </summary>
public static class NoireConfigManager
{
    private static readonly ConcurrentDictionary<Type, INoireConfig> ConfigCache = new();

    /// <summary>
    /// Gets or creates a configuration instance of the specified type.
    /// The configuration is automatically loaded from disk if it exists, otherwise a new instance is created.
    /// </summary>
    /// <typeparam name="T">The configuration type that inherits from NoireConfigBase.</typeparam>
    /// <returns>The configuration instance, or null if creation/loading failed.</returns>
    public static T? GetConfig<T>() where T : NoireConfigBase, new()
    {
        var type = typeof(T);

        if (ConfigCache.TryGetValue(type, out var cachedConfig))
            return cachedConfig as T;

        T? config = null;
        try
        {
            config = new T();
            config.Load();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to get or create configuration of type: {type.Name}", "[NoireConfigManager] ");
            return null;
        }

        ConfigCache.TryAdd(type, config!);
        return config;
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
            ConfigCache.TryAdd(typeof(T), config);
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
        ConfigCache.Remove(typeof(T), out _);
        return GetConfig<T>();
    }

    /// <summary>
    /// Removes a configuration from the cache without deleting the file.
    /// </summary>
    /// <typeparam name="T">The configuration type to remove from cache.</typeparam>
    /// <returns>True if the configuration was removed from cache; otherwise, false.</returns>
    public static bool UnloadConfig<T>() where T : NoireConfigBase
    {
        return ConfigCache.Remove(typeof(T), out _);
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
            ConfigCache.Remove(typeof(T), out _);
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
        ConfigCache.Clear();
        NoireLogger.LogDebug("Configuration cache cleared.", "[NoireConfigManager] ");
    }

    /// <summary>
    /// Gets the number of configurations currently cached.
    /// </summary>
    /// <returns>The number of cached configurations.</returns>
    public static int GetCachedConfigCount()
    {
        return ConfigCache.Count;
    }

    /// <summary>
    /// Saves all cached configurations to disk.
    /// </summary>
    /// <returns>True if all configurations were saved successfully; otherwise, false.</returns>
    public static bool SaveAllCached()
    {
        var allSuccess = true;

        foreach (var config in ConfigCache.Values)
        {
            if (!config.Save())
                allSuccess = false;
        }

        return allSuccess;
    }

    internal static bool AddConfigToCache(Type configType, INoireConfig config)
    {
#if DEBUG
        NoireLogger.LogDebug($"Adding configuration of type {configType.Name} to cache.", "[NoireConfigManager] ");
#endif
        return ConfigCache.TryAdd(configType, config);
    }

    /// <summary>
    /// Gets the configuration directory path for the current plugin.
    /// </summary>
    /// <returns>The full path to the plugin's configuration directory, or null if NoireLib is not initialized.</returns>
    public static string? GetConfigDirectoryPath()
    {
        return FileHelper.GetPluginConfigDirectory();
    }

    /// <summary>
    /// Registers a migration for a configuration type.
    /// This is useful for organizing migrations outside of the configuration class.
    /// </summary>
    /// <typeparam name="T">The configuration type.</typeparam>
    /// <param name="migration">The migration to register.</param>
    public static void RegisterMigration<T>(IConfigMigration migration) where T : NoireConfigBase
    {
        MigrationExecutor.RegisterMigration(typeof(T), migration);
    }

    /// <summary>
    /// Clears all runtime-registered migrations.
    /// </summary>
    public static void ClearMigrations()
    {
        MigrationExecutor.ClearRuntimeMigrations();
    }

    internal static void LoadMarkedConfigsFromDisk()
    {
        // Get all configurations that have LoadFromDiskOnInitialization set to true
        var configTypes = NoireService.PluginInstance?.GetType().Assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(NoireConfigBase).IsAssignableFrom(t))
            .Where(t =>
            {
                var prop = t.GetProperty(nameof(NoireConfigBase.LoadFromDiskOnInitialization));
                if (prop == null || !prop.CanRead)
                    return false;

                try
                {
                    var instance = Activator.CreateInstance(t) as NoireConfigBase;
                    return instance?.LoadFromDiskOnInitialization == true;
                }
                catch
                {
                    return false;
                }
            });

        if (configTypes == null)
            return;

        foreach (var configType in configTypes)
        {
            if (configType == null)
                continue;

            try
            {
                var baseType = configType.BaseType;
                var isGenericBase = baseType != null &&
                    baseType.IsGenericType &&
                    baseType.GetGenericTypeDefinition() == typeof(NoireConfigBase<>);

                if (isGenericBase)
                {
                    var instanceProp = configType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                    if (instanceProp == null && baseType != null)
                        instanceProp = baseType.GetProperty("Instance", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                    if (instanceProp != null)
                        instanceProp.GetValue(null);
                    else
                        NoireLogger.LogWarning($"Could not find static Instance property for generic config type: {configType.Name}", "[NoireConfigManager] ");
                }
                else
                {
                    var configInstance = Activator.CreateInstance(configType) as NoireConfigBase;
                    if (configInstance != null)
                        configInstance.Load();
                }
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Failed to load configuration of type: {configType.Name} during initialization.", "[NoireConfigManager] ");
            }
        }
    }
}
