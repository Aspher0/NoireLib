using Dalamud.Plugin;
using Dalamud.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using NoireLib.Core.Modules;
using NoireLib.Internal;

namespace NoireLib;

/// <summary>
/// The main class of NoireLib, used to initialize the library and manage its modules.
/// </summary>
public class NoireLibMain
{
    /// <summary>
    /// Initializes NoireLib services. Must be called in your plugin's constructor.
    /// </summary>
    /// <param name="dalamudPluginInterface">The Dalamud plugin interface instance from your plugin.</param>
    /// <param name="plugin">The instance of your plugin.</param>
    /// <param name="windowSystem">Optional window system to use for NoireLib windows. If null, a new window system will be created.</param>
    public static void Initialize(IDalamudPluginInterface dalamudPluginInterface, IDalamudPlugin plugin)
    {
        NoireService.Initialize(dalamudPluginInterface, plugin);
        NoireLogger.LogInfo<NoireLibMain>($"NoireLib {typeof(NoireLibMain).Assembly.GetName().Version} has been successfully initialized for {dalamudPluginInterface.InternalName} {plugin.GetType().Assembly.GetName().Version}.");
    }

    /// <summary>
    /// Adds a NoireLib module to be used in your project. Can be retrieved later with <see cref="GetModule{T}(string?, int)"/>.<br/>
    /// Multiple modules of the same type can be added, and they can be differentiated by their <paramref name="moduleId"/> or their zero-based index.<br/>
    /// If no instance is provided, a new instance of the module will be created and activated. Logging will also be enabled. If this is not what you are looking for, specify an instance instead.<br/>
    /// </summary>
    /// <typeparam name="T">The type of the module to add.</typeparam>
    /// <param name="moduleId">Optional module ID for the module instance, in case you want to create multiple instances of the same module and be able to retrieve a specific one later.</param>
    /// <returns>The instance of the module added.</returns>
    public static T? AddModule<T>(string? moduleId = null) where T : class, INoireModule, new()
    {
        var moduleType = typeof(T);

        T instanceToAdd;

        var specialConstructor = moduleType.GetConstructor([typeof(ModuleId), typeof(bool), typeof(bool)]);

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
    /// Adds a NoireLib module to be used in your project. Can be retrieved later with <see cref="GetModule{T}(string?, int)"/>.<br/>
    /// Multiple modules of the same type can be added, and they can be differentiated by their <paramref name="moduleId"/> or their zero-based index.<br/>
    /// </summary>
    /// <typeparam name="T">The type of the module to add.</typeparam>
    /// <param name="instance">The instance of the module to add.</param>
    /// <returns>The instance of the module added.</returns>
    public static T? AddModule<T>(T instance) where T : class, INoireModule
    {
        var moduleType = typeof(T);

        if (!instance.ModuleId.IsNullOrEmpty() && NoireService.ActiveModules.Any(m => m.Type == moduleType && m.Module.ModuleId == instance.ModuleId))
            NoireLogger.LogWarning($"Warning, a module of type {moduleType.Name} with id {instance.ModuleId} has already been added. Adding another instance with the same id may cause issues when trying to retrieve it later.");

        NoireService.ActiveModules.Add((moduleType, instance));

        return instance;
    }

    /// <summary>
    /// Adds multiple NoireLib modules to be used in your project. Can be retrieved later with <see cref="GetModule{T}(string?, int)"/>.<br/>
    /// Multiple modules of the same type can be added, and they can be differentiated by their module ID or their index.<br/>
    /// See <see cref="AddModule"/> to add a module with an optional custom ID.
    /// </summary>
    /// <param name="modules">The instances of the modules to add.</param>
    /// <returns>An array of the instances of the modules added.</returns>
    public static INoireModule[] AddModules(params INoireModule[] modules)
    {
        var addedModules = new List<INoireModule>();

        foreach (var module in modules)
        {
            NoireService.ActiveModules.Add((module.GetType(), module));
            addedModules.Add(module);
        }

        return addedModules.ToArray();
    }

    /// <summary>
    /// Removes a module from your project.
    /// </summary>
    /// <typeparam name="T">The type of the module to remove.</typeparam>
    /// <param name="moduleId">The optional ID of the module to remove.</param>
    /// <returns>True if successfully removed, otherwise false if module not found or if module failed to dispose.</returns>
    public static bool RemoveModule<T>(string? moduleId = null) where T : class, INoireModule
    {
        var moduleToRemove = NoireService.ActiveModules.FirstOrDefault(m => m.Type == typeof(T) && (moduleId.IsNullOrEmpty() || m.Module.ModuleId == moduleId));

        if (moduleToRemove.IsDefault())
        {
            NoireLogger.LogInfo($"No module of type {typeof(T).FullName}" + (moduleId.IsNullOrEmpty() ? string.Empty : " with id " + moduleId) + " found to remove.");
            return false;
        }

        try
        {
            moduleToRemove.Module.Dispose();
        }
        catch (Exception ex)
        {
            NoireLogger.LogError(ex, $"Failed to dispose module of type {typeof(T).FullName}" + (moduleId.IsNullOrEmpty() ? string.Empty : " with id " + moduleId) + ".");
            return false;
        }

        NoireService.ActiveModules.Remove(moduleToRemove);
        return true;
    }

    /// <summary>
    /// Removes a module from your project by its instance.
    /// </summary>
    /// <typeparam name="T">The type of the module to remove.</typeparam>
    /// <param name="instance">The instance of the module to remove.</param>
    /// <returns>True if successfully removed, otherwise false if module not found or if module failed to dispose.</returns>
    public static bool RemoveModule<T>(T instance) where T : class, INoireModule
    {
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
    /// Removes all modules from your project, disposing them first.
    /// </summary>
    /// <returns>True if all modules were successfully disposed, otherwise false if at least one module failed to dispose.</returns>
    public static bool ClearAllModules()
    {
        bool allDisposed = true;
        foreach (var moduleEntry in NoireService.ActiveModules)
        {
            try
            {
                moduleEntry.Module.Dispose();
            }
            catch (Exception ex)
            {
                NoireLogger.LogError(ex, $"Failed to dispose module of type {moduleEntry.Type.FullName}" + (moduleEntry.Module.ModuleId.IsNullOrEmpty() ? string.Empty : " with id " + moduleEntry.Module.ModuleId) + ".");
                allDisposed = false;
            }
        }
        NoireService.ActiveModules.Clear();
        return allDisposed;
    }

    /// <summary>
    /// Checks if a module has been added to your project.<br/>
    /// See <see cref="AddModule"/> or <see cref="AddModules"/>.
    /// </summary>
    /// <typeparam name="T">The type of the module to check.</typeparam>
    /// <param name="moduleId">The optional ID of the module to check.</param>
    /// <returns>True if the module is added, otherwise false.</returns>
    public static bool IsModuleAdded<T>(string? moduleId = null) where T : class, INoireModule
    {
        return !NoireService.ActiveModules.FirstOrDefault(m => m.Type == typeof(T) && (moduleId.IsNullOrEmpty() || m.Module.ModuleId == moduleId)).IsDefault();
    }

    /// <summary>
    /// Checks if a module has been added to your project and currently active.<br/>
    /// See <see cref="INoireModule.IsActive"/>.
    /// </summary>
    /// <typeparam name="T">The type of the module to check.</typeparam>
    /// <param name="moduleId">The optional ID of the module to check.</param>
    /// <returns>True if the module is added and active, otherwise false.</returns>
    public static bool IsModuleActive<T>(string? moduleId = null) where T : class, INoireModule
    {
        var added = NoireService.ActiveModules.FirstOrDefault(m => m.Type == typeof(T) && (moduleId.IsNullOrEmpty() || m.Module.ModuleId == moduleId));
        return added.IsDefault() ? false : added.Module.IsActive;
    }

    /// <summary>
    /// Tries to retrieve an instance of an added module by its type, module ID and/or index.<br/>
    /// </summary>
    /// <typeparam name="T">The type of the module to retrieve.</typeparam>
    /// <param name="moduleId">The optional ID of the module to retrieve.</param>
    /// <param name="index">
    /// The zero-based index of the instance to retrieve in the list.<br/>
    /// If <paramref name="moduleId"/> is provided, the index will be applied only to the instances with the specified ID.
    /// </param>
    /// <returns>
    /// The n-th (<paramref name="index"/>) instance of the module if added and found, otherwise null if the module couldn't be found or if the module wasn't added to your project.<br/>
    /// If the index is out of range, the closest valid instance will be returned (e.g. index 0 if negative, last instance if too high).
    /// </returns>
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
    /// Disposes NoireLib services and all active modules. Should be called in your plugin's Dispose method.
    /// </summary>
    public static void Dispose()
    {
        ClearAllModules();
        NoireService.Dispose();
    }
}
