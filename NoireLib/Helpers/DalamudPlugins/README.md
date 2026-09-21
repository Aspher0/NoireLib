# Helper Documentation : DalamudPluginsHelper

You are reading the documentation for the `DalamudPluginsHelper` static helper.

## Table of Contents
- [Overview](#overview)
- [Reading the installation](#reading-the-installation)
- [Installing and removing](#installing-and-removing)
- [Enabling, disabling and updating](#enabling-disabling-and-updating)
- [Repositories](#repositories)
- [Dev plugins](#dev-plugins)
- [What this rests on](#what-this-rests-on)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

`DalamudPluginsHelper` is a static helper in the `NoireLib.Helpers` namespace that drives Dalamud's plugin installation.

- **Install, remove, enable, disable, update** any plugin, from the official repository or any other.
- **Add and remove third-party repositories**, and turn a configured one on or off.
- **Add dev plugin directories** and rescan them.
- **Every operation reports what happened** as a `PluginOperationResult`. Nothing is swallowed and nothing throws.

**These operations change the user's installation.** Nothing guards them.

---

## Reading the installation

```csharp
using NoireLib.Helpers;

DalamudPluginsHelper.Installed();      // every installed plugin, as ReflectedPlugin
DalamudPluginsHelper.Catalog();        // everything the configured repositories offer
DalamudPluginsHelper.Updatable();      // the internal names with a newer version available
DalamudPluginsHelper.Repositories();   // the official repository and every third-party one
DalamudPluginsHelper.DevPluginDirectories();
```

---

## Installing and removing

```csharp
var installed = await DalamudPluginsHelper.InstallAsync("SomePlugin");
var testing   = await DalamudPluginsHelper.InstallAsync("SomePlugin", useTesting: true);

if (!installed.Succeeded)
    NoireLogger.LogWarning(installed.Message);

await DalamudPluginsHelper.UninstallAsync("SomePlugin");
```

`InstallAsync` takes the **internal name** and installs from whichever configured repository offers it. Add a third-party repository first. `useTesting` fails with a message when the plugin has no testing version.

`UninstallAsync` unloads the plugin, marks its files for deletion, and drops it from Dalamud's list. A file still held open is deleted when Dalamud next starts, like Dalamud's own installer.

---

## Enabling, disabling and updating

```csharp
await DalamudPluginsHelper.DisableAsync("SomePlugin");   // unloads, stays installed
await DalamudPluginsHelper.EnableAsync("SomePlugin");    // loads again
await DalamudPluginsHelper.ReloadAsync("SomePlugin");    // unload then load, for a rebuilt dev plugin

await DalamudPluginsHelper.UpdateAsync("SomePlugin");
var all = await DalamudPluginsHelper.UpdateAllAsync();   // one result per plugin
```

**Enable and disable go through Dalamud's profiles**, like its own installer. Loading the assembly directly leaves the installer showing the plugin disabled, and the next profile apply reverts it. The lower level is still there:

```csharp
await DalamudPluginsHelper.SetLoadedAsync("SomePlugin", loaded: true);   // the assembly alone
```

Enabling an enabled plugin, or disabling a disabled one, succeeds.

**A plugin installed twice needs its copy named.** Every call takes a `PluginSource`, defaulting to the repository copy:

```csharp
await DalamudPluginsHelper.ReloadAsync("SomePlugin", PluginSource.Dev);
await DalamudPluginsHelper.UninstallAsync("SomePlugin", PluginSource.Repository);
DalamudPluginsHelper.Installed(PluginSource.Dev);
```

Only a repository copy can be updated. `UpdateAsync` takes no source.

---

## Repositories

```csharp
await DalamudPluginsHelper.AddRepositoryAsync("https://example.com/pluginmaster.json");
await DalamudPluginsHelper.SetRepositoryEnabledAsync(url, enabled: false);
await DalamudPluginsHelper.RemoveRepositoryAsync(url);
await DalamudPluginsHelper.ReloadRepositoriesAsync();
```

Each saves Dalamud's configuration and applies the repository list. A new repository's plugins are installable at once. A URL already configured is left as is and reported as a success.

---

## Dev plugins

A dev directory takes the same four operations as a repository:

```csharp
await DalamudPluginsHelper.AddDevPluginDirectoryAsync(@"C:\dev\MyPlugin\bin\Debug", "My plugin");
DalamudPluginsHelper.SetDevPluginDirectoryEnabled(path, enabled: false);
DalamudPluginsHelper.RemoveDevPluginDirectory(path);
await DalamudPluginsHelper.ScanDevPluginsAsync();
```

A plugin loaded from a removed directory stays loaded until Dalamud restarts. The two configuration-only calls are synchronous.

---

## What this rests on

Dalamud publishes no API for any of this. The helper drives its **internal** plugin manager and configuration. An operation that cannot reach them returns a failed result naming what is missing. `DalamudPluginsHelper.Available` reads false when the plugin manager cannot be reached.

---

## Troubleshooting

### InstallAsync says no repository offers the plugin

The internal name is wrong, or its repository is not configured. Check `Catalog()` and `Repositories()`.

### A removed plugin's files are still on disk

They are deleted the next time Dalamud starts.

### The installer does not grey the icon after I disable a plugin

`SetLoadedAsync` only touches the assembly. `DisableAsync` writes the profile the installer draws.

### UninstallAsync failed at the unload step

The plugin threw while unloading. The result carries its exception in `Error`. The plugin is still installed.

---

## See Also

- [PluginReflectionHelper](../PluginInterop/README.md) for reaching inside another plugin.
- [DalamudDevBarHelper](../DevBar/README.md) for menus on Dalamud's dev bar.
