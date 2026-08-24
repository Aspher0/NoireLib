using NoireLib.Configuration.Migrations;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Configuration;

/// <summary>
/// The static manager for <see cref="NoireConfigBase"/> configurations
/// </summary>
public static class NoireConfigManager
{
    private const string LifecycleDisposeKey = "NoireLib.Configuration.Lifecycle";

    private static readonly ConcurrentDictionary<Type, NoireConfigBase> ConfigCache = new();

    /// <summary>
    /// The loads currently running, keyed by configuration type. Deduplicates concurrent first loads.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, Task<NoireConfigBase?>> InFlightLoads = new();

    private static int lifecycleHookRegistered;

    /// <summary>
    /// Gets the cached configuration of a type, joining an in-flight background load or loading inline.
    /// </summary>
    /// <typeparam name="T">The configuration type.</typeparam>
    /// <returns>The instance, or null when creation or loading threw.</returns>
    /// <seealso cref="ReloadConfig{T}"/>
    public static T? GetConfig<T>() where T : NoireConfigBase, new()
        => (T?)GetConfig(typeof(T));

    /// <summary>
    /// Non-generic core of <see cref="GetConfig{T}"/>, shared with the preload.
    /// </summary>
    /// <param name="type">The configuration type.</param>
    /// <returns>The instance, or null when creation or loading threw.</returns>
    internal static NoireConfigBase? GetConfig(Type type)
    {
        if (ConfigCache.TryGetValue(type, out var cached))
        {
            cached.MarkAccessed();
            return cached;
        }

        EnsureLifecycleHook();

        var load = InFlightLoads.GetOrAdd(type, static t => Task.Run(() => LoadNewInstance(t)));

        NoireConfigBase? config;

        try
        {
            // A load never touches the framework thread, so waiting on one cannot deadlock.
            config = load.GetAwaiter().GetResult();
        }
        finally
        {
            // Removing a failed load's entry is what lets the next call retry.
            InFlightLoads.TryRemove(type, out _);
        }

        config?.MarkAccessed();
        return config;
    }

    /// <summary>
    /// Constructs and loads one configuration, caching it when it is fit to share.
    /// </summary>
    /// <param name="type">The configuration type.</param>
    /// <returns>The instance, or null when construction or loading threw.</returns>
    private static NoireConfigBase? LoadNewInstance(Type type)
    {
        try
        {
            if (Activator.CreateInstance(type) is not NoireConfigBase config)
            {
                NoireLogger.LogError($"Failed to construct configuration of type: {type.Name}", "[NoireConfig] ");
                return null;
            }

            var loaded = config.Load();

            if (!loaded)
            {
                if (config.IsUnwrittenDefault)
                {
                    // No file yet: the defaults are the configuration until something saves them.
                    config.CompleteLoadSetup(null);
                    AddConfigToCache(type, config);
                }
                else
                {
                    NoireLogger.LogWarning(
                        $"Configuration {type.Name} could not be loaded and is not being cached, so the defaults returned " +
                        $"here are for this caller only and the next call will try to load it again.", "[NoireConfig] ");
                }
            }

            // A successful load cached itself; the canonical entry keeps a racing direct Load from splitting identity.
            return ConfigCache.TryGetValue(type, out var canonical) ? canonical : config;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to get or create configuration of type: {type.Name}", "[NoireConfig] ");
            return null;
        }
    }

    /// <summary>
    /// Saves a configuration instance to disk and caches it on success.
    /// </summary>
    /// <typeparam name="T">The configuration type.</typeparam>
    /// <param name="config">The configuration instance to save.</param>
    /// <returns>True when the save succeeded.</returns>
    public static bool SaveConfig<T>(T config) where T : NoireConfigBase
    {
        if (config == null)
        {
            NoireLogger.LogWarning("Cannot save null configuration.", "[NoireConfig] ");
            return false;
        }

        var success = config.Save();

        if (success)
            AddConfigToCache(typeof(T), config);

        return success;
    }

    /// <summary>
    /// Drops the cached instance and loads the configuration from disk again.
    /// </summary>
    /// <typeparam name="T">The configuration type.</typeparam>
    /// <returns>The reloaded configuration instance, or null when the reload failed.</returns>
    public static T? ReloadConfig<T>() where T : NoireConfigBase, new()
    {
        ConfigCache.TryRemove(typeof(T), out _);
        return GetConfig<T>();
    }

    /// <summary>
    /// Removes a configuration from the cache without deleting its file.
    /// </summary>
    /// <typeparam name="T">The configuration type to remove from cache.</typeparam>
    /// <returns>True when an entry was removed.</returns>
    public static bool UnloadConfig<T>() where T : NoireConfigBase
    {
        return ConfigCache.TryRemove(typeof(T), out _);
    }

    /// <summary>
    /// Saves every cached configuration, each isolated so one failure does not stop the others.
    /// </summary>
    /// <returns>True when every cached configuration is on disk.</returns>
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

                // A degraded configuration refuses every save and logs its own reason.
                if (config.IsDegraded)
                    continue;

                NoireLogger.LogWarning(
                    $"Cached configuration {config.GetType().Name} reported that it was not saved. Every other cached " +
                    $"configuration is still being saved.", "[NoireConfig] ");
            }
            catch (Exception ex)
            {
                allSuccess = false;

                NoireLogger.LogError(ex,
                    $"Failed to save cached configuration of type: {config.GetType().Name}. Every other cached " +
                    $"configuration is still being saved.", "[NoireConfig] ");
            }
        }

        return allSuccess;
    }

    /// <summary>
    /// Writes every queued payload and waits for any write already running, so the disk matches memory on return.
    /// </summary>
    /// <returns>True when every pending payload reached disk.</returns>
    /// <seealso cref="NoireConfigBase.RequestSave"/>
    public static bool FlushPendingSaves() => NoireConfigBase.FlushAllPendingSaves();

    /// <summary>
    /// Clears all cached configurations without deleting their files.
    /// </summary>
    public static void ClearCache()
    {
        ConfigCache.Clear();
        NoireLogger.LogDebug("Configuration cache cleared.", "[NoireConfig] ");
    }

    /// <summary>
    /// Registers a migration declared outside its configuration class.
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

    /// <summary>
    /// Caches an instance for a type when no entry exists yet.
    /// </summary>
    /// <param name="configType">The type to key on.</param>
    /// <param name="config">The instance to cache.</param>
    /// <returns>True when the entry was added.</returns>
    internal static bool AddConfigToCache(Type configType, NoireConfigBase config)
    {
        EnsureLifecycleHook();
        return ConfigCache.TryAdd(configType, config);
    }

    /// <summary>
    /// A snapshot of every cached configuration.
    /// </summary>
    internal static IReadOnlyList<NoireConfigBase> CachedConfigsSnapshot() => [.. ConfigCache.Values];

    /// <summary>
    /// Loads every marked configuration of the plugin assembly on a background thread, off the game thread.
    /// </summary>
    /// <param name="assembly">The plugin assembly to scan.</param>
    internal static void PreloadMarked(Assembly assembly)
    {
        EnsureLifecycleHook();

        Task.Run(() =>
        {
            try
            {
                var configTypes = assembly.GetTypes()
                    .Where(t => t.IsClass && !t.IsAbstract && typeof(NoireConfigBase).IsAssignableFrom(t));

                foreach (var type in configTypes)
                {
                    try
                    {
                        if (ConfigCache.ContainsKey(type))
                            continue;

                        // First construction of a configuration type costs hundreds of milliseconds; the probe pays it here.
                        if (Activator.CreateInstance(type) is not NoireConfigBase probe || !probe.LoadFromDiskOnInitialization)
                            continue;

                        var load = InFlightLoads.GetOrAdd(type, static t => Task.Run(() => LoadNewInstance(t)));
                        load.GetAwaiter().GetResult();
                        InFlightLoads.TryRemove(type, out _);
                    }
                    catch (Exception ex)
                    {
                        NoireLogger.LogError(ex, $"Failed to preload configuration of type: {type.Name}.", "[NoireConfig] ");
                    }
                }

                // An arm that landed before initialization could not attach the pump.
                NoireConfigWatch.EnsurePumpIfPending();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, "Failed to preload the marked configurations.", "[NoireConfig] ");
            }
        });
    }

    /// <summary>
    /// Registers the teardown callback once per initialization, from whichever configuration path runs first.
    /// </summary>
    private static void EnsureLifecycleHook()
    {
        if (Interlocked.CompareExchange(ref lifecycleHookRegistered, 1, 0) != 0)
            return;

        // A collision can only be a leftover of this same key, which runs the same teardown.
        NoireLibMain.RegisterOnDispose(LifecycleDisposeKey, OnLibraryDispose);
    }

    /// <summary>
    /// Captures outstanding changes, flushes the queued writes and resets the static state.
    /// </summary>
    private static void OnLibraryDispose()
    {
        NoireConfigWatch.RunFinalSweep(CachedConfigsSnapshot());

        ConfigCache.Clear();
        InFlightLoads.Clear();
        NoireLibMain.UnregisterOnDispose(LifecycleDisposeKey);
        Volatile.Write(ref lifecycleHookRegistered, 0);
    }
}
