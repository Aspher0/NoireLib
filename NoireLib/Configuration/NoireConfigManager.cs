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
    /// The configuration is automatically loaded from disk if it exists, otherwise a new instance is created.<br/>
    /// The returned instance is cached and shared by every later caller, with one exception: an instance whose load
    /// failed against a configuration that is actually there is returned but not cached, so that the next call tries
    /// again instead of being handed defaults forever. A load fails that way when the file exists but cannot be read
    /// or parsed, and when no path to it can be resolved at all, which is the state of a configuration reached before
    /// NoireLib is initialized. Caching those would mean a configuration touched a moment too early, or during a
    /// transient file error, permanently shadows the user's real settings, and that the first save afterwards writes
    /// the defaults over them.<br/>
    /// A first run, where the file simply does not exist yet, is not a failure of this kind: the defaults are the real
    /// configuration until something saves them, so that instance is cached like any other. A configuration loaded
    /// into a degraded state (see <see cref="NoireConfigBase.IsDegraded"/>) is cached as well, because its load
    /// succeeded and it is the live instance whose saves are being refused on purpose.
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

            // Evaluated inside the boundary because the second operand reaches a virtual member a derived
            // configuration may override. A successful load has already cached the instance from inside Load, so this
            // only ever decides what happens to a failure.
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
    /// Saves all cached configurations to disk.<br/>
    /// Each configuration is saved inside its own boundary, so one that cannot be written costs only its own write and
    /// every other cached configuration is still saved. <see cref="NoireConfigBase.Save"/> is virtual and resolves its
    /// file path through another virtual member, so a configuration can throw out of it rather than report false;
    /// without a boundary per configuration the first one to do so would end the run and leave every configuration
    /// after it silently unwritten, which loses settings that have nothing to do with the one that misbehaved.<br/>
    /// A configuration that refuses to write because it is <see cref="NoireConfigBase.IsDegraded"/> is not reported as
    /// a fault here. The refusal is the protection working as intended, and it already explains itself where it is
    /// decided. It still counts against the return value, because the configuration was not written.
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

                // A degraded configuration reports false every time anything asks it to save, which for a configuration
                // with members marked [AutoSave] is as often as anything assigns to one. Reporting that as a fault here
                // would bury the log under an error per pass for a state the configuration itself has already explained.
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
