# NoireLib Documentation - NoireConfiguration

You are reading the documentation for the `NoireConfiguration` system.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Reaching Your Configuration](#reaching-your-configuration)
  - [1. Generated Accessor](#1-generated-accessor)
  - [2. Singleton Base](#2-singleton-base)
  - [Which One](#which-one)
- [Automatic Saving](#automatic-saving)
  - [1. What Is Captured](#1-what-is-captured)
  - [2. How It Works](#2-how-it-works)
  - [3. What It Does Not Cover](#3-what-it-does-not-cover)
- [Saving and Loading by Hand](#saving-and-loading-by-hand)
- [The Manager](#the-manager)
- [Migrations](#migrations)
  - [1. Write a Migration](#1-write-a-migration)
  - [2. Register It](#2-register-it)
  - [3. When a Migration Fails](#3-when-a-migration-fails)
- [Settings](#settings)
  - [1. Generate Them](#1-generate-them)
  - [2. Rules](#2-rules)
  - [3. Share Codes](#3-share-codes)
  - [4. Settings Windows](#4-settings-windows)
- [Attributes](#attributes)
- [File Location](#file-location)
- [Rules Your Configuration Must Follow](#rules-your-configuration-must-follow)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireConfiguration` system persists plugin settings as JSON, with:
- **Plain classes**, plain `List<T>`, `Dictionary<K,V>`, arrays and nested types, at any depth
- **Automatic saving** of every change in the object graph, including collection and nested changes
- **Background loading** at initialization: the game thread never waits on a configuration
- **Debounced atomic writes**, flushed at plugin unload
- **Versioned migrations** with an automatic backup of the file they run against
- **A generated static accessor** per configuration

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### Things to know and quick example

A configuration inherits `NoireConfigBase`, overrides `Version` and `GetConfigFileName()`, and exposes public
`{ get; set; }` properties. Mark it `[AutoSave]` and you never call `Save()` again.

`[NoireConfig("Config")]` generates the static class `Config`, whose members forward to the single instance. The name
you pass must differ from the class name, since two types cannot share one name in a namespace.

```csharp
using NoireLib.Configuration;
using System.Collections.Generic;

namespace MyPlugin;

[Serializable]
[NoireConfig("Config")]
[AutoSave]
public class ConfigInstance : NoireConfigBase
{
    public override int Version { get; set; } = 1;
    public override string GetConfigFileName() => "Config"; // .json is appended

    public bool PluginEnabled { get; set; } = true;
    public List<uint> Favorites { get; set; } = [];
    public Dictionary<uint, ZonePrefs> Zones { get; set; } = new();
    public UiPrefs Ui { get; set; } = new();
}

// Usage
Config.PluginEnabled = false;
Config.Favorites.Add(28);
Config.Zones[129] = new ZonePrefs();
Config.Instance.Ui.MainWindow.Locked = true;
```

Every line above persists on its own.

---

## Reaching Your Configuration

### 1. Generated Accessor

`[NoireConfig("Config")]` emits a static class forwarding every public property, every `[AutoSave]` method, plus
`Instance`, `Save()`, `RequestSave()`, `Reload()` and `ClearCache()`. Without an argument the class is named
`<ClassName>Static`.

```csharp
Config.PluginEnabled = false;
Config.Favorites.Add(28);
Config.Save();
```

`Version`, `LoadFromDiskOnInitialization` and `GetConfigFileName()` are not forwarded.

### 2. Singleton Base

Inherit `NoireConfigBase<T>` for a typed `Instance` without a generated class.

```csharp
[AutoSave]
public class ConfigInstance : NoireConfigBase<ConfigInstance>
{
    public override int Version { get; set; } = 1;
    public override string GetConfigFileName() => "Config";

    public bool PluginEnabled { get; set; } = true;
}

ConfigInstance.Instance.PluginEnabled = false;
ConfigInstance.Reload();
```

To shorten the call site, forward it from your service class.

```csharp
public static class Service
{
    public static ConfigInstance Config => ConfigInstance.Instance;
}
```

Both routes return the same object, and you can use both on the same class.

### Which One

The accessor gives you the short call site and saves a property inside the assignment. It costs a source generator in
your build, and a new property only appears on it after a rebuild.

The singleton base is plain C# with nothing generated. Property sets are then captured on the next framework tick like
any other change, roughly 16ms later.

---

## Automatic Saving

`[AutoSave]` on the class, or on any single member, enrolls the configuration. Enrollment is per configuration: once
one member carries it, the whole serialized graph is watched. On a member it also captures inside the assignment,
through the generated accessor.

### 1. What Is Captured

| Change | Written |
|---|---|
| Property set through the accessor, member marked `[AutoSave]` | inside the assignment |
| `[AutoSave]` method called through the accessor | when it returns |
| List, dictionary, set or array mutation | next framework tick |
| Nested object change, at any depth | next framework tick |
| Edit of an element inside a collection | next framework tick |
| Anything still unwritten at plugin unload | during disposal |

### 2. How It Works

Reaching a configuration arms one check on the next framework tick. The check hashes the object graph into a 64-bit
fingerprint, compiled once per type on the background load thread, and compares it to the last persisted state. Only a
difference serializes and queues a write.

A check that captured something arms itself again: a burst of changes is followed until one check comes back clean.
A clean check disarms. The framework handler stays attached once the first check ran, since attaching and detaching it
around each check allocated on every frame a configuration was read; with nothing armed it costs a lock per tick.
Reading a configuration every frame costs one fingerprint per frame, microseconds and no garbage.

Writes go through a 250ms debounce capped at 2s (`SaveDebounceInterval`, `MaxSaveDelay`), then a temp-then-rename:
an interrupted write never truncates the file.

### 3. What It Does Not Cover

- A change made through a stored reference, with no further access to that configuration, waits for the next access or
  for unload. A crash before either loses it.
- A crash inside the debounce window loses that write.
- `[JsonIgnore]` members are invisible to the capture, as they are to the serializer.
- Changes made off the framework thread are caught by the next check.

---

## Saving and Loading by Hand

```csharp
config.RequestSave();      // queue a write, returns immediately
config.Save();             // write now, blocking
config.FlushPendingSave(); // write anything queued for this configuration
config.HasPendingSave;     // whether a write is queued

config.Load();             // read the file, migrate it, populate this instance
config.Exists();           // whether the file is on disk
config.Delete();           // delete the file
```

A save whose text matches the file is skipped. `Load()` returns false when no file exists yet, which is the normal
first run.

Override `LoadFromDiskOnInitialization` to keep a configuration out of the background preload. It then loads on first
access.

```csharp
public override bool LoadFromDiskOnInitialization => false;
```

---

## The Manager

```csharp
var config = NoireConfigManager.GetConfig<ConfigInstance>(); // cached, background-loaded
NoireConfigManager.ReloadConfig<ConfigInstance>();           // drop the cache, read the file again
NoireConfigManager.UnloadConfig<ConfigInstance>();           // drop the cache, keep the file
NoireConfigManager.SaveConfig(config);                       // save and cache
NoireConfigManager.SaveAllCached();                          // blocking save of every cached configuration
NoireConfigManager.FlushPendingSaves();                      // write every queued payload
NoireConfigManager.ClearCache();                             // drop every cached instance
```

Marked configurations load in the background when `NoireLibMain.Initialize` runs, and a caller racing that load waits
only for its own configuration. A configuration whose load failed against an existing file is not cached: the next
call tries again.

---

## Migrations

Bump `Version` and provide a migration from each older version. A load runs the shortest path of them, after copying
the file to a `.v<N>.bak` sibling.

### 1. Write a Migration

```csharp
[Serializable]
[NoireConfig("Config")]
public class ConfigInstance : NoireConfigBase
{
    public override int Version { get; set; } = 2;
    public override string GetConfigFileName() => "Config";

    public string NewName { get; set; } = "default";

    private sealed class V1ToV2 : ConfigMigrationBase
    {
        public override int FromVersion => 1;
        public override int ToVersion => 2;

        public override string Migrate(JObject jsonObject) => MigrationBuilder.Create()
            .RenameProperty("OldName", "NewName")
            .Migrate(jsonObject, ToVersion);
    }
}
```

`MigrationBuilder` covers `RenameProperty`, `DeleteProperty`, `DeleteProperties`, `AddProperty`,
`AddComputedProperty`, `ChangePropertyType`, `TransformProperty` and `WithCustomOperation`.

### 2. Register It

Nested migration classes are found automatically. Declare one elsewhere and register it with the attribute or the
manager.

```csharp
[ConfigMigration(typeof(ConfigInstance))]
public class V1ToV2 : ConfigMigrationBase { /* ... */ }

NoireConfigManager.RegisterMigration<ConfigInstance>(new V1ToV2());
```

The version written to the file is the one the class declares, whatever was assigned over `Version`.

### 3. When a Migration Fails

The file still loads, but the instance latches `IsDegraded` and refuses every save: the partially defaulted values
never reach disk. `DegradedBackupPath` points at the backup taken before the attempt.

```csharp
if (config.IsDegraded)
{
    var backup = config.DegradedBackupPath;
    config.ForceSave();          // write anyway
    config.ClearDegradedState(); // or declare it repaired without writing
}
```

---

## Settings

A setting is one configuration property with its default and its rules, for settings pages, reset buttons and share
codes.

### 1. Generate Them

```csharp
[NoireConfig("Configuration", SettingsClassName = "Cfg")]
public class ConfigurationInstance : NoireConfigBase
{
    [Range(1, 50)]
    public int MaxTargets { get; set; } = 3;
}

Cfg.MaxTargets.Value = 99;          // clamped to 50, saved
Cfg.MaxTargets.Default;             // 3, read from a fresh instance
Cfg.MaxTargets.IsModified;
Cfg.MaxTargets.Reset();
foreach (var setting in Cfg.All)    // INoireSetting, in declaration order
    setting.Reset();
```

One `NoireSetting<T>` per public read/write property of a simple type: `bool`, numbers, `char`, `string`, enums,
`TimeSpan`, `DateTime`, `Vector2`/`3`/`4`, and nullable forms of those. Collections and nested objects get none. A
property named `All` or `NoireDefaults` is skipped.

An enum setting labels each value with the declared text keyed `<Enum>.<Value>` (see
[Declared Texts](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/Localizer/README.md#declared-texts)), or the value's name when none is declared.

### 2. Rules

`[Range]`, `[StringLength]` and `[MaxLength]` are read into `NoireSetting<T>.Rules` and applied on every write through
`Value`, and to the whole configuration on every load: an out-of-range value in the file is clamped, logged, and saved
back.

```csharp
[Range(typeof(TimeSpan), "00:00:00", "01:00:00")]
public TimeSpan Throttle { get; set; } = TimeSpan.FromMinutes(5);

Cfg.Throttle.Rules.Max;   // 01:00:00
```

### 3. Share Codes

```csharp
string code = NoireSettingsShare.Export(Cfg.All);             // modified settings only, by name
ShareCodeResult<int> result = NoireSettingsShare.Import(code, Cfg.All);
// result.Value: how many applied. Unknown names and unreadable values are skipped; rules apply.
```

The code is a [share code](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Helpers/ShareCode/README.md) of kind
`<plugin internal name>.settings`; another plugin's code is refused.

### 4. Settings Windows

`NoireSettingsWindow` (see the `NoireLib.UI` README) draws these settings as rows with search, reset, confirmations and
the export and import buttons.

```csharp
page.Toggle(Cfg.Enabled, L.Enabled, L.EnabledHelp);
page.Number(Cfg.MaxTargets, L.MaxTargets);   // bounded by [Range]
```

---

## Attributes

#### `[NoireConfig]`

Generates the static accessor, named by the argument or `<ClassName>Static`.

```csharp
[NoireConfig("Config")]                            // static class Config
[NoireConfig("Config", SettingsClassName = "Cfg")] // plus static class Cfg, see Settings
```

#### `[AutoSave]`

Valid on a class, a property or a method.

```csharp
[AutoSave] public class ConfigInstance : NoireConfigBase   // enrolls the whole configuration
[AutoSave] public bool PluginEnabled { get; set; }         // enrolls, and saves inside the assignment
[AutoSave] public void Reset() { }                         // saves when the method returns
```

#### `[ConfigMigration]`

Registers a migration declared outside its configuration class.

```csharp
[ConfigMigration(typeof(ConfigInstance))]
public class V1ToV2 : ConfigMigrationBase { }
```

---

## File Location

Files go to the plugin's Dalamud configuration directory, named by `GetConfigFileName()` with `.json` appended when
missing. Override `GetConfigFilePath()` to put one elsewhere.

```csharp
protected override string? GetConfigFilePath() => myOwnPath;
```

---

## Rules Your Configuration Must Follow

- Members are public properties with both accessors. Analyzer `NoireLib_001` flags one missing an accessor.
- The class has a public parameterless constructor.
- Serialization is Newtonsoft.Json with `TypeNameHandling` off. A type it cannot round-trip needs a converter on the
  member.

---

## Troubleshooting

### A change is not saved
- Check that the class or one of its members carries `[AutoSave]`.
- Check that the member is a public property with both accessors and no `[JsonIgnore]`.
- Check that your code reaches the configuration through the accessor, `Instance` or `GetConfig<T>()`, and not only
  through a reference stored long ago.
- Check `IsDegraded`. A configuration whose migration failed refuses every save.

### The accessor does not exist, or is missing a member
- Check that the class carries `[NoireConfig]` and inherits `NoireConfigBase`.
- Rebuild. The accessor is generated at compile time.
- `Version`, `LoadFromDiskOnInitialization` and `GetConfigFileName()` are never forwarded.

### The file is not where you expect
- `GetConfigFileName()` names the file, not the directory. Override `GetConfigFilePath()` to move it.
- A `.v<N>.bak` sibling means a migration ran against it.

If it still does not work, check `/xllog` for lines prefixed `[NoireConfig]`, and report it.

---

## See Also

- [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [NoireDatabase documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Database/README.md)
- [NoireIPC documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/IPC/README.md)
