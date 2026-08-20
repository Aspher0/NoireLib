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
    /// Gets or creates a configuration instance of the specified type, loaded from disk when a file exists and
    /// cached for later callers. An instance whose load failed against an existing file is returned uncached, so
    /// the next call retries rather than handing out defaults forever.
    /// </summary>
    /// <typeparam name="T">The configuration type.</typeparam>
    /// <returns>The configuration instance, or null when creation or loading threw.</returns>
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

            // A successful load already cached the instance from inside Load; this only decides what happens to a
            // failure, and the second operand reaches a virtual member a derived configuration may override.
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
    /// <typeparam name="T">The configuration type.</typeparam>
    /// <returns>The configuration instance, or null when creation or loading threw.</returns>
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

    /// <summary>Saves a configuration instance to disk and updates the cache.</summary>
    /// <typeparam name="T">The configuration type.</typeparam>
    /// <param name="config">The configuration instance to save.</param>
    /// <returns>True when the save succeeded.</returns>
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

    /// <summary>Applies an action to a configuration and saves it.</summary>
    /// <typeparam name="T">The configuration type.</typeparam>
    /// <param name="updateAction">The action to perform on the configuration before saving.</param>
    /// <returns>True when the update and save both succeeded.</returns>
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

    /// <summary>Drops the cached instance and loads the configuration from disk again.</summary>
    /// <typeparam name="T">The configuration type.</typeparam>
    /// <returns>The reloaded configuration instance, or null when the reload failed.</returns>
    public static T? ReloadConfig<T>() where T : NoireConfigBase, new()
    {
        ConfigCache.Remove(typeof(T), out _);
        return GetConfig<T>();
    }

    /// <summary>Removes a configuration from the cache without deleting its file.</summary>
    /// <typeparam name="T">The configuration type to remove from cache.</typeparam>
    /// <returns>True when an entry was removed.</returns>
    public static bool UnloadConfig<T>() where T : NoireConfigBase
    {
        return ConfigCache.Remove(typeof(T), out _);
    }

    /// <summary>Deletes a configuration file from disk and removes it from the cache.</summary>
    /// <typeparam name="T">The configuration type to delete.</typeparam>
    /// <returns>True when the deletion succeeded.</returns>
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

    /// <summary>Checks whether a configuration file exists on disk.</summary>
    /// <typeparam name="T">The configuration type to check.</typeparam>
    /// <returns>True when the file exists.</returns>
    public static bool ConfigExists<T>() where T : NoireConfigBase, new()
    {
        var config = new T();
        return config.Exists();
    }

    /// <summary>Clears all cached configurations without deleting their files.</summary>
    public static void ClearCache()
    {
        ConfigCache.Clear();
        NoireLogger.LogDebug("Configuration cache cleared.", "[NoireConfigManager] ");
    }

    /// <summary>Gets the number of configurations currently cached.</summary>
    /// <returns>The number of cached configurations.</returns>
    public static int GetCachedConfigCount()
    {
        return ConfigCache.Count;
    }

    /// <summary>
    /// Saves all cached configurations to disk, each inside its own boundary so one that throws or fails to write
    /// does not stop the others.
    /// </summary>
    /// <returns>True when every cached configuration is on disk, whether written now or already up to date.</returns>
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

                // A degraded configuration reports false on every save attempt and already logs why where that is
                // decided, so logging it again here would flood on [AutoSave] members.
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

    /// <summary>
    /// Writes every configuration holding changes queued by <see cref="NoireConfigBase.RequestSave"/> and waits for
    /// any write already running, so on return the disk matches memory. Called during NoireLib disposal.
    /// </summary>
    /// <returns>True when every pending payload reached disk.</returns>
    /// <seealso cref="NoireConfigBase.RequestSave"/>
    public static bool FlushPendingSaves() => NoireConfigBase.FlushAllPendingSaves();

    /// <summary>Caches a configuration instance for a type when no entry exists yet.</summary>
    /// <param name="configType">The configuration type to key on.</param>
    /// <param name="config">The instance to cache.</param>
    /// <returns>True when the entry was added.</returns>
    internal static bool AddConfigToCache(Type configType, INoireConfig config)
    {
#if DEBUG
        NoireLogger.LogDebug($"Adding configuration of type {configType.Name} to cache.", "[NoireConfigManager] ");
#endif
        return ConfigCache.TryAdd(configType, config);
    }

    /// <summary>
    /// Replaces the cached instance for a type, only when one is already cached, so that
    /// <see cref="SaveAllCached"/> writes the auto-save proxy consumers hold rather than the raw load-time
    /// instance. Adding a missing entry here would defeat the uncached retry <see cref="GetConfig{T}"/> relies on.
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

    /// <summary>Gets the configuration directory path for the current plugin.</summary>
    /// <returns>The full path, or null when NoireLib is not initialized.</returns>
    public static string? GetConfigDirectoryPath()
    {
        return FileHelper.GetPluginConfigDirectory();
    }

    /// <summary>Registers a migration for a configuration type, declared outside the configuration class.</summary>
    /// <typeparam name="T">The configuration type.</typeparam>
    /// <param name="migration">The migration to register.</param>
    public static void RegisterMigration<T>(IConfigMigration migration) where T : NoireConfigBase
    {
        MigrationExecutor.RegisterMigration(typeof(T), migration);
    }

    /// <summary>Clears all runtime-registered migrations.</summary>
    public static void ClearMigrations()
    {
        MigrationExecutor.ClearRuntimeMigrations();
    }

    /// <summary>
    /// Loads every configuration type in the plugin assembly whose
    /// <see cref="NoireConfigBase.LoadFromDiskOnInitialization"/> is true.
    /// </summary>
    internal static void LoadMarkedConfigsFromDisk()
    {
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
