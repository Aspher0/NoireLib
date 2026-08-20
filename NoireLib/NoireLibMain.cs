using Dalamud.Plugin;
using Dalamud.Utility;
using NoireLib.Configuration;
using NoireLib.Core.Modules;
using NoireLib.Database.Migrations;
using NoireLib.Helpers.ObjectExtensions;
using NoireLib.IPC;
using System;
using System.Collections.Generic;
using System.Linq;
using NoireDatabase = NoireLib.Database.NoireDatabase;

namespace NoireLib;

/// <summary>
/// Entry point of NoireLib: initializes the library, manages its modules and disposes it.
/// </summary>
public class NoireLibMain
{
    /// <summary>
    /// The callbacks registered to run on disposal, with their key and priority.
    /// </summary>
    private static readonly List<(string Key, Action Callback, int Priority)> OnDisposeCallbacks = new();

    /// <summary>
    /// NoireLib's own version, read from the assembly.
    /// </summary>
    public static string Version { get; } =
        typeof(NoireLibMain).Assembly.GetName().Version?.ToString(3) ?? "unknown";

    /// <summary>
    /// Initializes NoireLib services, to be called from the host plugin's constructor.
    /// </summary>
    /// <param name="dalamudPluginInterface">The host plugin's Dalamud plugin interface.</param>
    /// <param name="plugin">The host plugin instance.</param>
    /// <returns>True when the services came up, false when initialization failed.</returns>
    public static bool Initialize(IDalamudPluginInterface dalamudPluginInterface, IDalamudPlugin plugin)
    {
        var initialized = NoireService.Initialize(dalamudPluginInterface, plugin);

        if (initialized)
        {
            DatabaseMigrationExecutor.RegisterMigrationsFromAssembly(plugin.GetType().Assembly);
            var preloadDatabases = Database.NoireDbModelBase.GetDatabasesToPreload(plugin.GetType().Assembly);
            foreach (var databaseName in preloadDatabases)
                NoireDatabase.RegisterForInitialization(databaseName, true);
            NoireDatabase.InitializeRegisteredDatabases();

            NoireIPC.RegisterAttributedTypes(plugin.GetType().Assembly);
            NoireConfigManager.LoadMarkedConfigsFromDisk();

            NoireLogger.LogInfo<NoireLibMain>($"NoireLib {typeof(NoireLibMain).Assembly.GetName().Version} has been successfully initialized for {dalamudPluginInterface.InternalName} {plugin.GetType().Assembly.GetName().Version}.");
        }

        return initialized;
    }

    /// <summary>
    /// Creates a module instance, active and with logging enabled, and adds it for retrieval through
    /// <see cref="GetModule{T}(string?, int)"/>.
    /// Several modules of the same type can be added, told apart by <paramref name="moduleId"/> or by zero-based index.
    /// </summary>
    /// <typeparam name="T">The type of the module to add.</typeparam>
    /// <param name="moduleId">An optional id identifying this instance among others of the same type.</param>
    /// <returns>The added module instance.</returns>
    public static T AddModule<T>(string? moduleId = null) where T : class, INoireModule, new()
    {
        var moduleType = typeof(T);

        T instanceToAdd;

        var specialConstructor = moduleType.GetConstructor(
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic,
            null,
            [typeof(ModuleId), typeof(bool), typeof(bool)],
            null);

        if (specialConstructor != null)
            instanceToAdd = (T)specialConstructor.Invoke([(moduleId.IsNullOrEmpty() ? null : new ModuleId(moduleId)), true, true]);
        else
        {
            NoireLogger.LogWarning($"Module of type {moduleType.Name} does not have a constructor with (ModuleId, bool, bool) parameters. Using parameterless constructor instead. Please report this to the devs.");
            instanceToAdd = new T();
            instanceToAdd.ModuleId = moduleId;
        }

        NoireService.ActiveModules.Add((moduleType, instanceToAdd));

        return instanceToAdd;
    }

    /// <summary>
    /// Adds an existing module instance for retrieval through <see cref="GetModule{T}(string?, int)"/>, told apart
    /// from others of the same type by its <see cref="NoireModuleBase{TModule}.ModuleId"/> or its zero-based index.
    /// </summary>
    /// <typeparam name="T">The type of the module to add.</typeparam>
    /// <param name="instance">The module instance to add.</param>
    /// <returns>The added module instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown if the module instance is null.</exception>
    public static T AddModule<T>(T instance) where T : class, INoireModule
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance), "Module instance cannot be null.");

        var moduleType = typeof(T);

        if ((instance.ModuleId.IsNullOrEmpty() && NoireService.ActiveModules.Any(m => m.Type == moduleType && m.Module.ModuleId.IsNullOrEmpty())) ||
            (!instance.ModuleId.IsNullOrEmpty() && NoireService.ActiveModules.Any(m => m.Type == moduleType && m.Module.ModuleId == instance.ModuleId)))
            NoireLogger.LogWarning($"A module of type {moduleType.Name} with id '{instance.ModuleId}' has already been added. Adding another instance with the same id may cause issues when trying to retrieve it later. Adding the module anyway.");

        NoireService.ActiveModules.Add((moduleType, instance));

        return instance;
    }

    /// <summary>
    /// Adds several existing module instances, skipping nulls.
    /// </summary>
    /// <param name="modules">The module instances to add.</param>
    /// <returns>The added module instances.</returns>
    public static INoireModule[] AddModules(params INoireModule[] modules)
    {
        var addedModules = new List<INoireModule>();

        foreach (var module in modules)
        {
            if (module == null)
                continue;

            var moduleType = module.GetType();

            if ((module.ModuleId.IsNullOrEmpty() && NoireService.ActiveModules.Any(m => m.Type == moduleType && m.Module.ModuleId.IsNullOrEmpty())) ||
                (!module.ModuleId.IsNullOrEmpty() && NoireService.ActiveModules.Any(m => m.Type == moduleType && m.Module.ModuleId == module.ModuleId)))
                NoireLogger.LogWarning($"A module of type {moduleType.Name} with id '{module.ModuleId}' has already been added. Adding another instance with the same id may cause issues when trying to retrieve it later. Adding the module anyway.");

            NoireService.ActiveModules.Add((module.GetType(), module));
            addedModules.Add(module);
        }

        return addedModules.ToArray();
    }

    /// <summary>
    /// Disposes and removes an added module, found by type and optional id.
    /// </summary>
    /// <typeparam name="T">The type of the module to remove.</typeparam>
    /// <param name="moduleId">The optional id of the module to remove.</param>
    /// <returns>True when removed, false when no module matched or disposal threw.</returns>
    public static bool RemoveModule<T>(string? moduleId = null) where T : class, INoireModule
    {
        var moduleToRemove = NoireService.ActiveModules.FirstOrDefault(m => m.Type == typeof(T) && (moduleId.IsNullOrEmpty() || m.Module.ModuleId == moduleId));

        if (moduleToRemove.IsDefault())
        {
            NoireLogger.LogInfo($"No module of type {typeof(T).FullName} {(moduleId.IsNullOrEmpty() ? "" : $" with id {moduleId}")} found to remove.");
            return false;
        }

        try
        {
            moduleToRemove.Module.Dispose();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to dispose module of type {typeof(T).FullName} {(moduleId.IsNullOrEmpty() ? "" : $" with id {moduleId}")}.");
            return false;
        }

        NoireService.ActiveModules.Remove(moduleToRemove);
        return true;
    }

    /// <summary>
    /// Disposes and removes an added module by its instance.
    /// </summary>
    /// <typeparam name="T">The type of the module to remove.</typeparam>
    /// <param name="instance">The module instance to remove.</param>
    /// <returns>True when removed, false when no module matched or disposal threw.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="instance"/> is null.</exception>
    public static bool RemoveModule<T>(T instance) where T : class, INoireModule
    {
        if (instance == null)
            throw new ArgumentNullException(nameof(instance), "Module instance cannot be null.");

        var moduleToRemove = NoireService.ActiveModules.FirstOrDefault(m => m.Type == typeof(T) && m.Module == instance);
        if (moduleToRemove.IsDefault())
        {
            NoireLogger.LogInfo($"No module of type {typeof(T).FullName} found to remove.");
            return false;
        }

        try
        {
            moduleToRemove.Module.Dispose();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to dispose module of type {typeof(T).FullName}.");
            return false;
        }

        NoireService.ActiveModules.Remove(moduleToRemove);
        return true;
    }

    /// <summary>
    /// Disposes and removes every added module.
    /// </summary>
    /// <returns>True when every module disposed, false when at least one threw.</returns>
    public static bool ClearAllModules()
    {
        bool allDisposed = true;
        for (int i = NoireService.ActiveModules.Count - 1; i >= 0; i--)
        {
            var moduleEntry = NoireService.ActiveModules[i];

            try
            {
                moduleEntry.Module.Dispose();
                NoireService.ActiveModules.RemoveAt(i);
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Failed to dispose module of type {moduleEntry.Type.FullName} {(moduleEntry.Module.ModuleId.IsNullOrEmpty() ? "" : $" with id {moduleEntry.Module.ModuleId}")}.");
                allDisposed = false;
            }
        }
        return allDisposed;
    }

    /// <summary>
    /// Whether a module of the given type, and optional id, has been added.
    /// </summary>
    /// <typeparam name="T">The type of the module to check.</typeparam>
    /// <param name="moduleId">The optional id of the module to check.</param>
    /// <returns>True when a matching module is added.</returns>
    public static bool IsModuleAdded<T>(string? moduleId = null) where T : class, INoireModule
        => !NoireService.ActiveModules.FirstOrDefault(m => m.Type == typeof(T) && (moduleId.IsNullOrEmpty() || m.Module.ModuleId == moduleId)).IsDefault();

    /// <summary>
    /// Whether a module of the given type, and optional id, has been added and is
    /// <see cref="INoireModule.IsActive"/>.
    /// </summary>
    /// <typeparam name="T">The type of the module to check.</typeparam>
    /// <param name="moduleId">The optional id of the module to check.</param>
    /// <returns>True when a matching module is added and active.</returns>
    public static bool IsModuleActive<T>(string? moduleId = null) where T : class, INoireModule
    {
        var added = NoireService.ActiveModules.FirstOrDefault(m => m.Type == typeof(T) && (moduleId.IsNullOrEmpty() || m.Module.ModuleId == moduleId));
        return added.IsDefault() ? false : added.Module.IsActive;
    }

    /// <summary>
    /// Retrieves an added module by its type, optional id and index.
    /// </summary>
    /// <typeparam name="T">The type of the module to retrieve.</typeparam>
    /// <param name="moduleId">The id of the module to retrieve, or <see langword="null"/> to match on type alone.</param>
    /// <param name="index">
    /// The zero-based index among the matching instances, clamped into range rather than rejected.
    /// </param>
    /// <returns>The matching instance, or null when nothing matches.</returns>
    public static T? GetModule<T>(string? moduleId = null, int index = 0) where T : class, INoireModule
    {
        var instances = NoireService.ActiveModules.Where(m => m.Type == typeof(T)).ToArray();

        if (!moduleId.IsNullOrEmpty())
            instances = instances.Where(m => m.Module.ModuleId == moduleId).ToArray();

        if (instances.Length == 0)
            return null;

        if (index < 0)
        {
            NoireLogger.LogWarning($"Tried to get module of type {typeof(T).FullName} with negative index {index}. Returning the first instance instead.");
            index = 0;
        }

        if (index >= instances.Length)
        {
            NoireLogger.LogWarning($"Tried to get module of type {typeof(T).FullName} with out-of-range index {index}. Returning the last instance instead.");
            index = instances.Length - 1;
        }

        var instance = instances[index];
        return instance.IsDefault() ? null : instance.Module as T;
    }

    /// <summary>
    /// Registers a callback to be invoked when NoireLib is disposed.
    /// </summary>
    /// <param name="key">A key unique among the registered callbacks.</param>
    /// <param name="callback">The action to execute during disposal.</param>
    /// <param name="priority">The invocation order, lowest first.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null or blank, or when <paramref name="callback"/> is null.</exception>
    /// <returns>True when registered, false when the key is already taken.</returns>
    public static bool RegisterOnDispose(string key, Action callback, int priority = 0)
    {
        if (key.IsNullOrWhitespace())
            throw new ArgumentNullException(nameof(key), "Key cannot be null or blank.");

        if (callback == null)
            throw new ArgumentNullException(nameof(callback), "Callback cannot be null.");

        if (OnDisposeCallbacks.Any(c => c.Key == key))
        {
            NoireLogger.LogError($"A callback with the key '{key}' is already registered for disposal. Each callback must have a unique key.\nRegistration of the new callback failed.");
            return false;
        }

        OnDisposeCallbacks.Add((key, callback, priority));
        return true;
    }

    /// <summary>
    /// Unregisters a disposal callback by its key.
    /// </summary>
    /// <param name="key">The key of the callback to unregister.</param>
    /// <returns>True when a callback was found and unregistered.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null or blank.</exception>
    public static bool UnregisterOnDispose(string key)
    {
        if (key.IsNullOrWhitespace())
            throw new ArgumentNullException(nameof(key), "Key cannot be null or blank.");

        if (!OnDisposeCallbacks.Any(c => c.Key == key))
            return false;

        OnDisposeCallbacks.RemoveAll(c => c.Key == key);
        return true;
    }

    /// <summary>
    /// Whether a disposal callback is registered under the given key.
    /// </summary>
    /// <param name="key">The key to check.</param>
    /// <returns>True when a callback is registered under that key.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="key"/> is null or blank.</exception>
    public static bool IsRegisteredOnDispose(string key)
    {
        if (key.IsNullOrWhitespace())
            throw new ArgumentNullException(nameof(key), "Key cannot be null or blank.");

        return OnDisposeCallbacks.Any(c => c.Key == key);
    }

    /// <summary>
    /// Disposes NoireLib services and every active module, to be called from the host plugin's own disposal.
    /// </summary>
    public static void Dispose()
    {
        var allModulesDisposed = ClearAllModules();

        if (!allModulesDisposed)
            NoireLogger.LogWarning("Some modules failed to dispose properly during NoireLib disposal. Please report this to the devs.");

        // Each callback is isolated so one that throws cannot strand the callbacks after it or the teardown below.
        var orderedCallbacks = OnDisposeCallbacks.OrderBy(c => c.Priority).ToArray();
        foreach (var (key, callback, _) in orderedCallbacks)
        {
            try
            {
                callback.Invoke();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Disposal callback '{key}' threw during NoireLib disposal. The remaining callbacks are still being invoked.");
            }
        }

        // Ahead of the service teardown: dropping a link needs the chat service that registered it.
        try
        {
            NoireLib.Helpers.ChatLinkHelper.Clear();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Failed to drop the registered chat links during NoireLib disposal.");
        }

        // Last, so a setting changed by a module teardown or a disposal callback is written rather than left
        // sitting in the [AutoSave] debounce window.
        try
        {
            NoireConfigManager.FlushPendingSaves();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, "Failed to flush pending configuration saves during NoireLib disposal.");
        }

        NoireService.Dispose();
    }
}
