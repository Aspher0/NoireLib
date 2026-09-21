# Helper Documentation : PluginReflectionHelper

You are reading the documentation for the `PluginReflectionHelper` static helper.

## Table of Contents
- [Overview](#overview)
- [Finding a plugin](#finding-a-plugin)
- [The typed facade](#the-typed-facade)
- [The reflection handle](#the-reflection-handle)
- [Static state](#static-state)
- [What this rests on](#what-this-rests-on)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

`PluginReflectionHelper` is a static helper in the `NoireLib.Helpers` namespace that reaches the other plugins Dalamud has installed, and their objects and types.

- **Every installed plugin**, loaded or not, with what Dalamud knows about each.
- **A typed facade**: describe what you expect as an interface and call it like any other object.
- **A reflection handle** underneath, reading and writing fields and properties and calling methods of any visibility.
- **A missing member never throws.** A read yields the default, a write reports false, a call yields null.

---

## Finding a plugin

```csharp
using NoireLib.Helpers;

foreach (var plugin in PluginReflectionHelper.All())
    NoireLogger.LogInfo($"{plugin.InternalName} {plugin.Version} loaded={plugin.IsLoaded}");

var one = PluginReflectionHelper.Find("SomePlugin");   // internal name, or display name
PluginReflectionHelper.IsLoaded("SomePlugin");
```

The **internal name** keys the plugin's folder and repository entry. A display name can change. `Find` tries the internal name first.

A `ReflectedPlugin` carries `Name`, `InternalName`, `Id`, `Version`, `Author`, `RepositoryUrl`, `Path`,
`Nickname`, `IsEnabled`, `State`, `IsLoaded`, `IsDev`, `IsThirdParty`, `IsTesting`, `IsOutdated`, `IsBanned`,
`Assembly`, and `Manifest` for the fields it does not name.

`IsEnabled` is whether a profile wants the plugin, shown as enabled in the installer. `IsLoaded` is whether its assembly is in memory. A wanted plugin can fail to load.

### Telling two installations apart

Two dev directories can hold builds of one assembly, and two repositories can offer one plugin. **`Id` is the only value unique per installation.** Match on `Path`, `RepositoryUrl`, `Nickname` or `Author`:

```csharp
PluginReflectionHelper.FindAll("SomePlugin");            // every installation carrying that name
PluginReflectionHelper.Find(id);                         // exactly one, by its own id
PluginReflectionHelper.Find(p => p.Path.Contains("Debug", StringComparison.OrdinalIgnoreCase));
PluginReflectionHelper.Find(p => p.RepositoryUrl == url && !p.IsDev);
```

Take the `Id` once from `FindAll` and keep it.

### The same plugin, installed twice

A plugin can be installed from a repository and loaded from a dev directory **at the same time**. `PluginSource` picks which one a call acts on:

```csharp
PluginReflectionHelper.Find("SomePlugin");                          // the repository copy when both are there
PluginReflectionHelper.Find("SomePlugin", PluginSource.Dev);        // the dev copy, and only that
PluginReflectionHelper.All(PluginSource.Repository);                // no dev plugins in the list
```

`PluginSource.Any` prefers the repository copy. An internal-name match beats a display-name one.

---

## The typed facade

Describe what you expect as an interface, and each of its members is routed to the plugin member of the same name:

```csharp
public interface ISomePlugin
{
    bool IsBusy { get; }
    void ExecuteCommand(string command);
}

var plugin = PluginReflectionHelper.Bind<ISomePlugin>("SomePlugin");
if (plugin != null && !plugin.IsBusy)
    plugin.ExecuteCommand("/something");
```

`Bind` returns null when the plugin is not installed or not loaded.

**A property reads and writes the member of its own name. A method calls the method of its own name.**

A missing member reads as the default and a call returns the default. `strict: true` throws `MissingMemberException`, useful while writing the contract:

```csharp
var plugin = PluginReflectionHelper.Bind<ISomePlugin>("SomePlugin", strict: true);
```

---

## The reflection handle

`ReflectedObject` is the handle under the facade, for members only known at runtime.

```csharp
var instance = PluginReflectionHelper.Find("SomePlugin")?.Instance;

instance?.Get<bool>("IsBusy");
instance?.Set("Threshold", 30);
instance?.Call<string>("Describe");
instance?.Call("Overloaded", 5);          // the overload is picked by the argument types

instance?.Has("Threshold");
instance?.MemberNames();                  // everything reachable, for finding out what is there
instance?.Into("Configuration")?.Get<int>("Delay");   // walk into what a member holds
instance?.Each("Jobs");                   // one handle per element of a sequence
```

`Instance` is the plugin object. It is null when the plugin is not loaded and **does not survive a reload**. Look the plugin up again.

Members are resolved on the type and then every base type. A private member is only reachable from its declaring type. `BindingFlags.FlattenHierarchy` flattens statics alone.

---

## Static state

Most plugins keep their state in statics:

```csharp
var plugin = PluginReflectionHelper.Find("SomePlugin");

plugin?.Static("SomePlugin.Service")?.Get<object>("Config");
PluginReflectionHelper.BindStatic<IServiceContract>("SomePlugin", "SomePlugin.Service");
```

---

## What this rests on

Dalamud publishes no plugin list. This walks Dalamud's **internal** plugin manager and reads each plugin's private instance field. None of that is API.

When a name is gone, `All()` returns empty, `Find` returns null and `PluginReflectionHelper.Available` reads false.

---

## Troubleshooting

### Find returns null for a plugin I can see in the installer

It is installed but not loaded, or another plugin's internal name matched first. Check both names in `All()`.

### I get the repository copy when I wanted the dev one

Pass `PluginSource.Dev`.

### A bound member always reads the default

The plugin spells it differently, or is not loaded. Bind with `strict: true`, or call `MemberNames()` on the instance.

### It worked, then stopped after the other plugin updated

A plugin can rename its internals in any release. The lenient mode degrades without throwing.

---

## See Also

- [DalamudPluginsHelper](../DalamudPlugins/README.md) for installing, removing, enabling and updating plugins.
- [DalamudDevBarHelper](../DevBar/README.md) for menus on Dalamud's dev bar.
