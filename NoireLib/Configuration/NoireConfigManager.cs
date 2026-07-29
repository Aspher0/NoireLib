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
    /// Gets or creates a configuration instance of the specified type, loaded from disk if a file exists. The
    /// instance is cached and shared by later callers, except when a load fails against a file that exists but
    /// could not be read or parsed: that instance is returned but not cached, so the next call retries instead of
    /// being handed defaults forever.<br/>
    /// A first run with no file yet is cached normally, since the defaults are the real configuration until
    /// something saves them. A configuration loaded into a degraded state (see
    /// <see cref="NoireConfigBase.IsDegraded"/>) is cached as well, since its load succeeded and its saves are
    /// refused on purpose.
    /// </summary>
    /// <typeparam name="T">The configuration type that inherits from NoireConfigBase.</typeparam>
    /// <returns>The configuration instance, or null if creation/loading failed.</returns>
    /// <seealso cref="ReloadConfig{T}"/>
    public static T? GetConfig<T>() where T : NoireConfigBase, new()
    {
        var type = typeof(T);

        if (ConfigCache.TryGetValue(type, out var cachedConfig))
            return cachedConfig as T;

        T config;
        bool cacheable;

        try
        {
            config = new T();

            // The second operand reaches a virtual member a derived configuration may override. A successful load
            // already cached the instance from inside Load; this only decides what happens to a failure.
            cacheable = config.Load() || config.IsUnwrittenDefault;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to get or create configuration of type: {type.Name}", "[NoireConfigManager] ");
            return null;
        }

        if (cacheable)
        {
            ConfigCache.TryAdd(type, config);
        }
        else
        {
            NoireLogger.LogWarning(
                $"Configuration {type.Name} could not be loaded and is not being cached, so the defaults returned here " +
                $"are for this caller only and the next call will try to load it again.", "[NoireConfigManager] ");
        }

        return config;
    }

    /// <summary>
    /// Gets or creates a configuration instance of the specified type without caching, returning a fresh instance
    /// every time.
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
    /// Saves all cached configurations to disk. Each is saved inside its own boundary, so a configuration that
    /// throws or fails to write does not stop the others.<br/>
    /// A configuration that refuses because it is <see cref="NoireConfigBase.IsDegraded"/> is not reported as a
    /// fault here, since it already explains itself where it is decided, but it still counts against the return
    /// value.
    /// </summary>
    /// <returns>True if every cached configuration is on disk, whether it was written now or was already up to date;
    /// false if any of them is not, whether it failed to write or refused to.</returns>
    /// <seealso cref="NoireConfigBase.IsDegraded"/>
    public static bool SaveAllCached()
    {
        var allSuccess = true;

        foreach (var config in ConfigCache.Values)
        {
            try
            {
                if (config.Save())
                    continue;

                allSuccess = false;

                // A degraded configuration reports false on every save attempt, which for [AutoSave] members can be
                // often; not logged here since it already explains itself where it is decided.
                if (config is NoireConfigBase { IsDegraded: true })
                    continue;

                NoireLogger.LogWarning(
                    $"Cached configuration {config.GetType().Name} reported that it was not saved. Every other cached " +
                    $"configuration is still being saved.", "[NoireConfigManager] ");
            }
            catch (Exception ex)
            {
                allSuccess = false;

                NoireLogger.LogError(ex,
                    $"Failed to save cached configuration of type: {config.GetType().Name}. Every other cached " +
                    $"configuration is still being saved.", "[NoireConfigManager] ");
            }
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
    /// Replaces the cached instance for a type, but only when one is already cached. A configuration with
    /// <see cref="AutoSaveAttribute"/> members is loaded into a raw instance and handed to consumers as an
    /// auto-save proxy; swapping the cache entry for the proxy keeps <see cref="SaveAllCached"/> writing the values
    /// consumers are changing rather than the raw load-time snapshot.<br/>
    /// Only an existing entry is swapped: a failed load is deliberately left uncached by <see cref="GetConfig{T}"/>
    /// so the next call retries, and adding one here would defeat that.
    /// </summary>
    /// <param name="configType">The configuration type whose cached instance is being replaced.</param>
    /// <param name="config">The instance to cache in its place.</param>
    internal static void ReplaceCachedInstance(Type configType, INoireConfig config)
    {
        while (ConfigCache.TryGetValue(configType, out var existing))
        {
            if (ReferenceEquals(existing, config) || ConfigCache.TryUpdate(configType, config, existing))
                return;
        }
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
    /// Registers a migration for a configuration type, for organizing migrations outside the configuration class.
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
