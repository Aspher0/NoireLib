using Dalamud.Plugin;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Helpers;

/// <summary>
/// Drives Dalamud's plugin installation: installed plugins, repository listings, install, remove, enable, disable and update, third-party repositories and dev plugin directories.<br/>
/// This drives Dalamud internals, not its API. Every operation returns a <see cref="PluginOperationResult"/> and never throws.
/// </summary>
public static class DalamudPluginsHelper
{
    private const string LoggerPrefix = "DalamudPluginsHelper";

    /// <summary>Whether Dalamud's plugin manager could be reached. Every operation fails with a message when it cannot.</summary>
    public static bool Available => DalamudInternals.PluginManager() != null;

    #region Reading

    /// <summary>Every plugin Dalamud has installed, loaded or not.</summary>
    /// <param name="source">Which copies to list. Every one by default.</param>
    /// <returns>The plugins, or empty when the plugin manager could not be reached.</returns>
    public static IReadOnlyList<ReflectedPlugin> Installed(PluginSource source = PluginSource.Any)
        => PluginReflectionHelper.All(source);

    /// <summary>Every plugin the configured repositories offer, installed or not.</summary>
    /// <returns>The plugins, or empty when the plugin manager could not be reached.</returns>
    public static IReadOnlyList<AvailablePluginInfo> Catalog()
    {
        var manager = DalamudInternals.PluginManager();
        if (manager == null)
            return [];

        var list = new List<AvailablePluginInfo>();
        foreach (var manifest in DalamudInternals.ReadSequence(manager, "AvailablePlugins"))
            list.Add(ReadManifest(new ReflectedObject(manifest)));

        return list;
    }

    /// <summary>The installed plugins a repository offers a newer version of.</summary>
    /// <returns>Their internal names, or empty when the plugin manager could not be reached.</returns>
    public static IReadOnlyList<string> Updatable()
    {
        var names = new List<string>();
        foreach (var update in Updates())
        {
            var manifest = update.Into("UpdateManifest");
            var name = manifest?.Get<string>("InternalName");
            if (!string.IsNullOrEmpty(name))
                names.Add(name);
        }

        return names;
    }

    /// <summary>The third-party repositories Dalamud is configured with, and the official one.</summary>
    /// <returns>The repositories, or empty when the configuration could not be reached.</returns>
    public static IReadOnlyList<PluginRepositoryInfo> Repositories()
    {
        var manager = DalamudInternals.PluginManager();
        if (manager == null)
            return [];

        var list = new List<PluginRepositoryInfo>();
        foreach (var repo in DalamudInternals.ReadSequence(manager, "Repos"))
        {
            var reflected = new ReflectedObject(repo);
            list.Add(new PluginRepositoryInfo(
                reflected.Get<string>("PluginMasterUrl") ?? string.Empty,
                reflected.Get<bool>("IsEnabled"),
                reflected.Get<bool>("IsThirdParty")));
        }

        return list;
    }

    /// <summary>The directories Dalamud scans for dev plugins.</summary>
    /// <returns>Each directory's path, or empty when the configuration could not be reached.</returns>
    public static IReadOnlyList<string> DevPluginDirectories()
    {
        var list = new List<string>();
        foreach (var location in DalamudInternals.ReadSequence(DalamudInternals.Configuration(), "DevPluginLoadLocations"))
        {
            var path = new ReflectedObject(location).Get<string>("Path");
            if (!string.IsNullOrEmpty(path))
                list.Add(path);
        }

        return list;
    }

    #endregion

    #region Installing and removing

    /// <summary>Installs a plugin a configured repository offers.</summary>
    /// <param name="internalName">The plugin's internal name.</param>
    /// <param name="useTesting">True to install the testing version.</param>
    /// <returns>What happened.</returns>
    public static async Task<PluginOperationResult> InstallAsync(string internalName, bool useTesting = false)
    {
        var manager = DalamudInternals.PluginManager();
        if (manager == null)
            return PluginOperationResult.Failed(internalName, "Dalamud's plugin manager could not be reached.");

        object? manifest = null;
        foreach (var candidate in DalamudInternals.ReadSequence(manager, "AvailablePlugins"))
        {
            if (string.Equals(
                    new ReflectedObject(candidate).Get<string>("InternalName"),
                    internalName,
                    StringComparison.OrdinalIgnoreCase))
            {
                manifest = candidate;
                break;
            }
        }

        if (manifest == null)
            return PluginOperationResult.Failed(internalName, "No configured repository offers that plugin.");

        return await RunAsync(
            internalName,
            $"Installed '{internalName}'.",
            () => new ReflectedObject(manager).Call(
                "InstallPluginAsync", manifest, useTesting, PluginLoadReason.Installer));
    }

    /// <summary>Removes an installed plugin. A file still held open is deleted when Dalamud next starts.</summary>
    /// <param name="name">The plugin's internal name or display name.</param>
    /// <param name="source">Which copy to remove. The repository one by default when both are installed.</param>
    /// <returns>What happened.</returns>
    public static async Task<PluginOperationResult> UninstallAsync(
        string name,
        PluginSource source = PluginSource.Any)
    {
        var manager = DalamudInternals.PluginManager();
        var plugin = PluginReflectionHelper.Find(name, source);
        if (manager == null || plugin == null)
            return PluginOperationResult.Failed(name, Missing(name, source));

        var unloaded = await SetLoadedAsync(name, loaded: false, source);
        if (!unloaded.Succeeded && plugin.IsLoaded)
            return unloaded;

        return SafeExecutor.ExecuteSafely(
            () =>
            {
                plugin.Local.Call("ScheduleDeletion", true);
                new ReflectedObject(manager).Call("RemovePlugin", plugin.Local.Target);
                return PluginOperationResult.Ok(name, $"Removed '{plugin.InternalName}'.");
            },
            PluginOperationResult.Failed(name, "Dalamud refused the removal."));
    }

    #endregion

    #region Enabling and updating

    /// <summary>Enables a plugin through the default profile, like Dalamud's own installer.</summary>
    /// <param name="name">The plugin's internal name or display name.</param>
    /// <param name="source">Which copy to enable. The repository one by default when both are installed.</param>
    /// <returns>What happened.</returns>
    public static Task<PluginOperationResult> EnableAsync(string name, PluginSource source = PluginSource.Any)
        => SetWantedAsync(name, source, wanted: true);

    /// <summary>Disables a plugin the way Dalamud's own installer does, leaving it installed.</summary>
    /// <param name="name">The plugin's internal name or display name.</param>
    /// <param name="source">Which copy to disable. The repository one by default when both are installed.</param>
    /// <returns>What happened.</returns>
    public static Task<PluginOperationResult> DisableAsync(string name, PluginSource source = PluginSource.Any)
        => SetWantedAsync(name, source, wanted: false);

    /// <summary>
    /// Loads or unloads a plugin's assembly without touching whether a profile wants it. This is the lower level:
    /// Dalamud's installer keeps showing the plugin as it was, and the next time profiles are applied the plugin
    /// goes back to what the profile says. <see cref="EnableAsync"/> is what a caller usually wants.
    /// </summary>
    /// <param name="name">The plugin's internal name or display name.</param>
    /// <param name="loaded">True to load the assembly, false to unload it.</param>
    /// <param name="source">Which copy to act on.</param>
    /// <returns>What happened.</returns>
    public static async Task<PluginOperationResult> SetLoadedAsync(
        string name,
        bool loaded,
        PluginSource source = PluginSource.Any)
    {
        var plugin = PluginReflectionHelper.Find(name, source);
        if (plugin == null)
            return PluginOperationResult.Failed(name, Missing(name, source));

        if (plugin.IsLoaded == loaded)
            return PluginOperationResult.Ok(name, $"'{plugin.InternalName}' was already {(loaded ? "loaded" : "unloaded")}.");

        if (loaded)
        {
            return await RunAsync(
                name,
                $"Loaded '{plugin.InternalName}'.",
                () => plugin.Local.Call("LoadAsync", PluginLoadReason.Installer, false, CancellationToken.None));
        }

        var mode = DisposalMode("WaitBeforeDispose");
        return await RunAsync(
            name,
            $"Unloaded '{plugin.InternalName}'.",
            () => mode == null
                ? plugin.Local.Call("UnloadAsync")
                : plugin.Local.Call("UnloadAsync", mode));
    }

    // Loading the assembly behind the profiles leaves the installer showing the plugin as disabled.
    private static async Task<PluginOperationResult> SetWantedAsync(string name, PluginSource source, bool wanted)
    {
        var plugin = PluginReflectionHelper.Find(name, source);
        if (plugin == null)
            return PluginOperationResult.Failed(name, Missing(name, source));

        var profile = DalamudInternals.Read(DalamudInternals.Read(DalamudInternals.PluginManager(), "profileManager"), "DefaultProfile");
        if (profile == null)
        {
            // Without the profile manager the assembly still loads, but the installer does not follow.
            var fallback = await SetLoadedAsync(name, wanted, source);
            return fallback.Succeeded
                ? PluginOperationResult.Failed(name, $"{fallback.Message} Dalamud's profiles could not be reached. Its installer still shows the old state.")
                : fallback;
        }

        return await RunAsync(
            name,
            $"{(wanted ? "Enabled" : "Disabled")} '{plugin.InternalName}'.",
            () => new ReflectedObject(profile).Call("AddOrUpdateAsync", plugin.Id, plugin.InternalName, wanted, true));
    }

    /// <summary>Unloads and loads a plugin again. A dev plugin picks up a rebuilt assembly this way.</summary>
    /// <param name="name">The plugin's internal name or display name.</param>
    /// <param name="source">Which copy to reload. The repository one by default when both are installed.</param>
    /// <returns>What happened.</returns>
    public static async Task<PluginOperationResult> ReloadAsync(string name, PluginSource source = PluginSource.Any)
    {
        var plugin = PluginReflectionHelper.Find(name, source);
        if (plugin == null)
            return PluginOperationResult.Failed(name, Missing(name, source));

        return await RunAsync(name, $"Reloaded '{plugin.InternalName}'.", () => plugin.Local.Call("ReloadAsync"));
    }

    /// <summary>
    /// Updates one installed plugin, when a repository offers a newer version of it. Only a repository copy can be
    /// updated: a dev plugin is whatever was last built into its directory.
    /// </summary>
    /// <param name="name">The plugin's internal name or display name.</param>
    /// <returns>What happened.</returns>
    public static async Task<PluginOperationResult> UpdateAsync(string name)
    {
        var manager = DalamudInternals.PluginManager();
        if (manager == null)
            return PluginOperationResult.Failed(name, "Dalamud's plugin manager could not be reached.");

        foreach (var update in Updates())
        {
            var internalName = update.Into("UpdateManifest")?.Get<string>("InternalName");
            if (!string.Equals(internalName, name, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(update.Into("InstalledPlugin")?.Get<string>("Name"), name, StringComparison.OrdinalIgnoreCase))
                continue;

            return await RunAsync(
                name,
                $"Updated '{internalName}'.",
                () => new ReflectedObject(manager).Call("UpdateSinglePluginAsync", update.Target, true, false));
        }

        return PluginOperationResult.Failed(name, "No update is available for that plugin.");
    }

    /// <summary>Updates every installed plugin a repository offers a newer version of.</summary>
    /// <returns>What happened, one result per plugin, or a single failure when the operation could not run.</returns>
    public static async Task<IReadOnlyList<PluginOperationResult>> UpdateAllAsync()
    {
        var results = new List<PluginOperationResult>();
        foreach (var internalName in Updatable())
            results.Add(await UpdateAsync(internalName));

        return results;
    }

    #endregion

    #region Repositories and dev directories

    /// <summary>
    /// Adds a third-party repository and reloads the repository list. What it offers becomes installable. A URL
    /// already configured is left as it is.
    /// </summary>
    /// <param name="pluginMasterUrl">The repository's plugin master URL.</param>
    /// <param name="enabled">Whether Dalamud reads it straight away.</param>
    /// <returns>What happened.</returns>
    public static async Task<PluginOperationResult> AddRepositoryAsync(string pluginMasterUrl, bool enabled = true)
    {
        if (string.IsNullOrWhiteSpace(pluginMasterUrl))
            return PluginOperationResult.Failed(pluginMasterUrl ?? string.Empty, "A repository needs a URL.");

        var configuration = DalamudInternals.Configuration();
        var repoType = DalamudInternals.Resolve(DalamudInternals.ThirdPartyRepoType);
        if (DalamudInternals.Read(configuration, "ThirdRepoList") is not IList list || repoType == null)
            return PluginOperationResult.Failed(pluginMasterUrl, "Dalamud's repository list could not be reached.");

        foreach (var existing in list)
        {
            if (string.Equals(new ReflectedObject(existing!).Get<string>("Url"), pluginMasterUrl, StringComparison.OrdinalIgnoreCase))
                return PluginOperationResult.Ok(pluginMasterUrl, "That repository was already configured.");
        }

        var added = SafeExecutor.ExecuteSafely(
            () =>
            {
                var settings = Activator.CreateInstance(repoType)!;
                var reflected = new ReflectedObject(settings);
                reflected.Set("Url", pluginMasterUrl);
                reflected.Set("IsEnabled", enabled);
                list.Add(settings);
                return true;
            },
            false);

        if (!added)
            return PluginOperationResult.Failed(pluginMasterUrl, "Dalamud refused the repository entry.");

        return await ApplyRepositoriesAsync(pluginMasterUrl, $"Added '{pluginMasterUrl}'.");
    }

    /// <summary>Removes a third-party repository and reloads the repository list.</summary>
    /// <param name="pluginMasterUrl">The repository's plugin master URL.</param>
    /// <returns>What happened.</returns>
    public static async Task<PluginOperationResult> RemoveRepositoryAsync(string pluginMasterUrl)
    {
        if (DalamudInternals.Read(DalamudInternals.Configuration(), "ThirdRepoList") is not IList list)
            return PluginOperationResult.Failed(pluginMasterUrl ?? string.Empty, "Dalamud's repository list could not be reached.");

        for (var index = 0; index < list.Count; index++)
        {
            if (!string.Equals(new ReflectedObject(list[index]!).Get<string>("Url"), pluginMasterUrl, StringComparison.OrdinalIgnoreCase))
                continue;

            list.RemoveAt(index);
            return await ApplyRepositoriesAsync(pluginMasterUrl, $"Removed '{pluginMasterUrl}'.");
        }

        return PluginOperationResult.Failed(pluginMasterUrl, "That repository is not configured.");
    }

    /// <summary>Turns a configured third-party repository on or off and reloads the repository list.</summary>
    /// <param name="pluginMasterUrl">The repository's plugin master URL.</param>
    /// <param name="enabled">Whether Dalamud reads it.</param>
    /// <returns>What happened.</returns>
    public static async Task<PluginOperationResult> SetRepositoryEnabledAsync(string pluginMasterUrl, bool enabled)
    {
        if (DalamudInternals.Read(DalamudInternals.Configuration(), "ThirdRepoList") is not IList list)
            return PluginOperationResult.Failed(pluginMasterUrl ?? string.Empty, "Dalamud's repository list could not be reached.");

        foreach (var existing in list)
        {
            var reflected = new ReflectedObject(existing!);
            if (!string.Equals(reflected.Get<string>("Url"), pluginMasterUrl, StringComparison.OrdinalIgnoreCase))
                continue;

            reflected.Set("IsEnabled", enabled);
            return await ApplyRepositoriesAsync(pluginMasterUrl, $"{(enabled ? "Enabled" : "Disabled")} '{pluginMasterUrl}'.");
        }

        return PluginOperationResult.Failed(pluginMasterUrl, "That repository is not configured.");
    }

    /// <summary>Fetches every configured repository again. Newly published versions are seen.</summary>
    /// <returns>What happened.</returns>
    public static async Task<PluginOperationResult> ReloadRepositoriesAsync()
    {
        var manager = DalamudInternals.PluginManager();
        if (manager == null)
            return PluginOperationResult.Failed(string.Empty, "Dalamud's plugin manager could not be reached.");

        return await RunAsync(
            string.Empty,
            "Reloaded every repository.",
            () => new ReflectedObject(manager).Call("ReloadAllReposAsync"));
    }

    /// <summary>Adds a directory Dalamud scans for dev plugins, then scans it.</summary>
    /// <param name="path">The directory holding the built plugin.</param>
    /// <param name="nickname">A name for the entry in Dalamud's settings, or null to leave it unnamed.</param>
    /// <returns>What happened.</returns>
    public static async Task<PluginOperationResult> AddDevPluginDirectoryAsync(string path, string? nickname = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return PluginOperationResult.Failed(path ?? string.Empty, "A dev location needs a path.");

        var configuration = DalamudInternals.Configuration();
        var locationType = DalamudInternals.Resolve("Dalamud.Configuration.DevPluginLocationSettings");
        if (DalamudInternals.Read(configuration, "DevPluginLoadLocations") is not IList list || locationType == null)
            return PluginOperationResult.Failed(path, "Dalamud's dev location list could not be reached.");

        foreach (var existing in list)
        {
            if (string.Equals(new ReflectedObject(existing!).Get<string>("Path"), path, StringComparison.OrdinalIgnoreCase))
                return PluginOperationResult.Ok(path, "That directory was already configured.");
        }

        var added = SafeExecutor.ExecuteSafely(
            () =>
            {
                var settings = Activator.CreateInstance(locationType)!;
                var reflected = new ReflectedObject(settings);
                reflected.Set("Path", path);
                reflected.Set("IsEnabled", true);
                if (!string.IsNullOrEmpty(nickname))
                    reflected.Set("Nickname", nickname);

                list.Add(settings);
                return true;
            },
            false);

        if (!added)
            return PluginOperationResult.Failed(path, "Dalamud refused the dev location entry.");

        Save();
        return await ScanDevPluginsAsync();
    }

    /// <summary>Removes a directory from the ones Dalamud scans for dev plugins.</summary>
    /// <param name="path">The directory to stop scanning.</param>
    /// <returns>What happened.</returns>
    public static PluginOperationResult RemoveDevPluginDirectory(string path)
    {
        if (DalamudInternals.Read(DalamudInternals.Configuration(), "DevPluginLoadLocations") is not IList list)
            return PluginOperationResult.Failed(path ?? string.Empty, "Dalamud's dev location list could not be reached.");

        for (var index = 0; index < list.Count; index++)
        {
            if (!string.Equals(new ReflectedObject(list[index]!).Get<string>("Path"), path, StringComparison.OrdinalIgnoreCase))
                continue;

            list.RemoveAt(index);
            Save();
            return PluginOperationResult.Ok(path, $"Removed '{path}'. The plugins it holds stay loaded until Dalamud restarts.");
        }

        return PluginOperationResult.Failed(path, "That directory is not configured.");
    }

    /// <summary>Turns a configured dev directory on or off without removing it.</summary>
    /// <param name="path">The directory.</param>
    /// <param name="enabled">Whether Dalamud scans it.</param>
    /// <returns>What happened.</returns>
    public static PluginOperationResult SetDevPluginDirectoryEnabled(string path, bool enabled)
    {
        if (DalamudInternals.Read(DalamudInternals.Configuration(), "DevPluginLoadLocations") is not IList list)
            return PluginOperationResult.Failed(path ?? string.Empty, "Dalamud's dev location list could not be reached.");

        foreach (var existing in list)
        {
            var reflected = new ReflectedObject(existing!);
            if (!string.Equals(reflected.Get<string>("Path"), path, StringComparison.OrdinalIgnoreCase))
                continue;

            reflected.Set("IsEnabled", enabled);
            Save();
            return PluginOperationResult.Ok(path, $"{(enabled ? "Enabled" : "Disabled")} '{path}'.");
        }

        return PluginOperationResult.Failed(path, "That directory is not configured.");
    }

    /// <summary>Scans the configured dev directories, which loads a plugin newly built into one of them.</summary>
    /// <returns>What happened.</returns>
    public static async Task<PluginOperationResult> ScanDevPluginsAsync()
    {
        var manager = DalamudInternals.PluginManager();
        if (manager == null)
            return PluginOperationResult.Failed(string.Empty, "Dalamud's plugin manager could not be reached.");

        return await RunAsync(
            string.Empty,
            "Scanned the dev directories.",
            () => new ReflectedObject(manager).Call("ScanDevPluginsAsync"));
    }

    #endregion

    private static string Missing(string name, PluginSource source) => source switch
    {
        PluginSource.Repository => $"No repository copy of '{name}' is installed.",
        PluginSource.Dev => $"No dev copy of '{name}' is installed.",
        _ => $"'{name}' is not installed.",
    };

    private static IEnumerable<ReflectedObject> Updates()
    {
        var manager = DalamudInternals.PluginManager();
        if (manager == null)
            yield break;

        foreach (var update in DalamudInternals.ReadSequence(manager, "UpdatablePlugins"))
            yield return new ReflectedObject(update);
    }

    private static AvailablePluginInfo ReadManifest(ReflectedObject manifest)
    {
        var repo = manifest.Into("SourceRepo");
        return new AvailablePluginInfo(
            manifest.Get<string>("InternalName") ?? string.Empty,
            manifest.Get<string>("Name") ?? string.Empty,
            manifest.Get<Version>("AssemblyVersion"),
            manifest.Get<Version>("TestingAssemblyVersion"),
            repo?.Get<string>("PluginMasterUrl") ?? string.Empty,
            manifest.Get<string>("Punchline") ?? string.Empty);
    }

    private static object? DisposalMode(string name)
    {
        var type = DalamudInternals.Resolve("Dalamud.Plugin.Internal.Types.PluginLoaderDisposalMode");
        return type == null ? null : SafeExecutor.ExecuteSafely<object?>(() => Enum.Parse(type, name), null);
    }

    private static void Save()
    {
        var configuration = DalamudInternals.Configuration();
        if (configuration != null)
            new ReflectedObject(configuration).Call("QueueSave");
    }

    private static async Task<PluginOperationResult> ApplyRepositoriesAsync(string subject, string message)
    {
        Save();

        var manager = DalamudInternals.PluginManager();
        if (manager == null)
            return PluginOperationResult.Failed(subject, "Dalamud's plugin manager could not be reached.");

        return await RunAsync(
            subject,
            message,
            () => new ReflectedObject(manager).Call("SetPluginReposFromConfigAsync", true));
    }

    // The returned Task's exception is reported, never left to a finalizer.
    private static async Task<PluginOperationResult> RunAsync(string subject, string message, Func<object?> call)
    {
        try
        {
            var returned = call();
            if (returned is Task task)
                await task.ConfigureAwait(false);
            else if (returned == null)
                return PluginOperationResult.Failed(subject, "Dalamud no longer exposes that operation.");

            return PluginOperationResult.Ok(subject, message);
        }
        catch (Exception exception)
        {
            NoireLogger.LogWarning($"{message} failed: {exception.Message}", LoggerPrefix);
            return PluginOperationResult.Failed(subject, exception.Message, exception);
        }
    }
}
