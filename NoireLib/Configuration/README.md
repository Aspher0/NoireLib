# NoireLib Documentation - NoireConfigManager

You are reading the documentation for `NoireConfigManager`.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration Approaches](#configuration-approaches)
  - [1. With Source Generator](#with-source-generator)
  - [2. With Castle DynamicProxy](#with-castle-dynamicproxy)
  - [3. Legacy-Manual](#legacy---manual)
- [AutoSave Attribute](#autosave-attribute)
- [Using NoireConfigManager](#using-noireconfigmanager)
- [Configuration Migrations](#configuration-migrations)
  - [What are Migrations?](#what-are-migrations)
  - [Migration Approaches](#migration-approaches)
    - [1. Nested Class Migration](#nested-class-migration)
    - [2. Attribute-Based Migration](#attribute-based-migration)
    - [3. Runtime Migration Registration](#runtime-migration-registration)
  - [Using MigrationBuilder](#using-migrationbuilder)
  - [Migration Best Practices](#migration-best-practices)
- [Advanced Features](#advanced-features)
- [Comparison Table](#comparison-table)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireConfigManager` is a configuration system that manages JSON-based configuration files for your plugin. It provides:
- **Automatic JSON serialization and management** of configuration files, they are loaded automatically when you access any of the properties
- **Multiple configuration approaches** to suit different needs
- **AutoSave functionality** for automatic configuration saving
- **Configuration migrations** to handle version upgrades seamlessly
- **Centralized cache management**
- **Type-safe configuration**

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### Things to know and quick example

Whatever way you create your configuration, you will need to either inherit from `NoireConfigBase` or `NoireConfigBase<T>`.<br/>
You will also need to override the `GetConfigFileName` method to specify the name of the configuration file in the default dalamud config folder.<br/>
You must also implement the `Version` property to track the configuration schema version.<br/>
In the example below, the configuration file will be named `MyPluginConfig.json` and we use the Source Generator.

```csharp
using System;
using NoireLib.Configuration;

namespace MyPlugin.Configuration;

[Serializable]
[NoireConfig("MyPluginConfig")]
public class MyPluginConfigInstance : NoireConfigBase
{
    public override int Version { get; set; } = 1;
    public override string GetConfigFileName() => "MyPluginConfig"; // Do not include the .json extension

    [AutoSave]
    public bool SomeBooleanProperty { get; set; } = true;
}

// Usage anywhere in your code:
MyPluginConfig.SomeBooleanProperty = false; // Automatically saves
```

That's it! The configuration is automatically loaded and saved.<br/>
You can read about the details and other ways of creating configurations below.

---

## Configuration Approaches

NoireLib offers **three different approaches** for creating configurations, each with its own trade-offs.

### 1. With Source Generator

**Uses a compile-time source generator to create a static class wrapper around your configuration.**<br/>
With this approach, you define your configuration class inheriting from `NoireConfigBase` and decorate it with the `[NoireConfig("ClassName")]` attribute. The source generator will then create a static class named `ClassName` that provides direct access to the configuration instance.

#### Example

To create a configuration using the Source Generator approach, define your configuration class that inherits from `NoireConfigBase` and decorate it with the `[NoireConfig("ClassName")]` attribute:

```csharp
using System;
using NoireLib.Configuration;

namespace MyPlugin.Configuration;

[Serializable]
[NoireConfig("MyConfig")]
public class MyConfigInstance : NoireConfigBase
{
    public override int Version { get; set; } = 1;
    public override string GetConfigFileName() => "MyConfig"; // Do not include the .json extension

    [AutoSave]
    public bool AutoSavedProperty { get; set; } = true; // This property will auto save when you access its setter on the static MyConfig class

    public int RegularProperty { get; set; } = 0;

    [AutoSave]
    public void AutoSaveMethod() => RegularProperty++;

    public void RegularMethod()
    {
        RegularProperty--;
        Save();
    }

    // This method won't save, you need to decorate it with the [AutoSave] attribute
    public void WontAutoSave() => AutoSavedProperty = !AutoSavedProperty;
}
```

The `[NoireConfig("MyConfig")]` attribute will mark the class as a configuration and the source generator will create a static class named `MyConfig` that provides easy access to the configuration instance.<br/>
The `[AutoSave]` attribute on properties and methods will ensure that the configuration is automatically saved when those properties are set or methods are called.<br/>
However, methods not marked with `[AutoSave]` will not trigger an automatic save even if they modify `[AutoSave]` properties.

#### Usage

```csharp
// Access properties and methods through the generated static class
MyConfig.AutoSavedProperty = false; // Automatically saves
MyConfig.AutoSaveMethod(); // Automatically saves after execution

var value = MyConfig.RegularProperty;

// Access the instance directly if needed
MyConfig.Instance.RegularMethod();

// Manually save, reload, or clear cache
MyConfig.Save();
MyConfig.Reload();
MyConfig.ClearCache();
```

#### Pros
- **No instance management needed** - You do not need to instanciate the configuration nor to initialize it, you can access properties and methods directly via static class
- **Clean syntax** - No need to call `.Instance` for every access
- **No virtual keyword required** - Properties and methods don't need to be virtual unlike the [Castle approach](#2-castle-dynamicproxy-approach)

#### Cons
- **"Find All References" limitations** - IDE may not find all usages of the instance class since you use the generated static class
- **Method-level AutoSave limitation** - Methods **not** marked with `[AutoSave]` but modifying `[AutoSave]` properties will not automatically save the config, instead you need to mark the method itself with `[AutoSave]`
- **Class name limitation** - You cannot name the instance class the same as the generated static class (e.g., `MyConfig`), you must use a different name like `MyConfigInstance`

---

### 2. With Castle DynamicProxy

**Uses runtime proxying to intercept property and method calls for automatic saving.**<br/>
With this approach, you define your configuration class inheriting from `NoireConfigBase<T>` and access the singleton instance via the static `Instance` property. Properties and methods that should trigger automatic saving must be marked with the `[AutoSave]` attribute and declared as `virtual`.

#### Example

To create a configuration using the Castle DynamicProxy approach, define your configuration class that inherits from `NoireConfigBase<T>` and mark your `[AutoSave]` properties and methods as `virtual`:

```csharp
using System;
using NoireLib.Configuration;

namespace MyPlugin.Configuration;

[Serializable]
public class MyConfigCastle : NoireConfigBase<MyConfigCastle>
{
    public override int Version { get; set; } = 1;
    public override string GetConfigFileName() => "MyConfigCastle";

    [AutoSave]
    public virtual bool AutoSavedProperty { get; set; } = true;

    public int RegularProperty { get; set; } = 0;

    [AutoSave]
    public virtual void AutoSaveMethod() => RegularProperty++;

    public void RegularMethod()
    {
        AutoSavedProperty = !AutoSavedProperty; // Auto-saves because property has [AutoSave]
    }
}
```

The `NoireConfigBase<T>` base class provides the static `Instance` property that returns the singleton instance of the configuration.<br/>
Methods and properties marked with `[AutoSave]` and declared as `virtual` will automatically save the configuration when accessed. A warning will be logged in /xllog if you forget to mark them as `virtual`.<br/>
Methods that are only used to modify `[AutoSave]` properties do not need to be marked with `[AutoSave]` themselves, as the property setter will handle the saving, but depending on your use case we suggest you still add the attribute to methods so the configuration is saved after the method completely executes:

```csharp
[AutoSave]
public virtual bool Property1 { get; set; } = false;

public bool Property2 { get; set; } = false;

public void Method()
{
    Property1 = true;
    // Implicit auto-save since Property1 has [AutoSave]
    Property2 = true;
    // Does not auto-save since Property2 lacks [AutoSave]

    // The configuration file did not save the Property2 change
    // This is why we suggest adding [AutoSave] to methods too depending on your use case
}
```

#### Usage

```csharp
// Access through the static Instance property
MyConfigCastle.Instance.AutoSavedProperty = false; // Automatically saves
MyConfigCastle.Instance.AutoSaveMethod(); // Automatically saves

var value = MyConfigCastle.Instance.RegularProperty;

// The Instance property handles everything
MyConfigCastle.Reload();
MyConfigCastle.ClearCache();
```

#### Pros
- **"Find All References" works** - IDE can track all usages through the Instance property
- **Smart AutoSave** - Methods automatically save when they modify `[AutoSave]` properties, even if the method itself isn't marked
- **Built-in singleton** - Instance property ensures only one instance exists
- **No source generator needed**

#### Cons
- **Requires `.Instance`** - Must always access through `ClassName.Instance`
- **Virtual keyword required** - Properties and methods with `[AutoSave]` must be virtual
- **Runtime overhead** - Slight performance cost from dynamic proxying

---

### 3. Legacy - Manual

**Traditional approach with manual instance management and saving.**<br/>
With this approach, you need to manually instanciate, store, load and save the configuration yourself.<br>
We do not recommend that you use this method, but it is still included in the event you find use for it.<br/>
The `[AutoSave]` attribute will not work and you must use `NoireConfigManager` to load the configuration.

#### Example

```csharp
using System;
using NoireLib.Configuration;

namespace MyPlugin.Configuration;

[Serializable]
public class MyConfigLegacy : NoireConfigBase
{
    public override int Version { get; set; } = 1;
    public override string GetConfigFileName() => "MyConfigLegacy";

    public bool SomeProperty { get; set; } = false;

    public int Counter { get; set; } = 0;

    public void IncrementCounter()
    {
        Counter++;
        Save(); // Must manually call Save()
    }
}
```

#### Usage

```csharp
// Get or create the configuration
var config = NoireConfigManager.GetConfig<MyConfigLegacy>();

// Modify and save manually
if (config != null)
{
    config.SomeProperty = true;
    config.Save(); // Manual save required!
}

// Or use UpdateConfig helper
NoireConfigManager.UpdateConfig<MyConfigLegacy>(config =>
{
    config.Counter++;
});
```

#### Pros
- **Full control** - You manage when and how configurations are saved
- **Simple and explicit** - No magic, no attributes, no proxying
- **No special requirements** - No virtual keywords or attributes needed
- **Easy to understand**

#### Cons
- **Manual management** - Must manually get, modify, and save configurations
- **No AutoSave** - Must remember to call `Save()` after every change
- **Requires NoireConfigManager** - Must use the manager for caching and access
- **More boilerplate** - More code needed for basic operations

---

## AutoSave Attribute

The `[AutoSave]` attribute automatically saves the configuration when properties are set or methods are called.

### On Properties

```csharp
[AutoSave]
public virtual bool EnableFeature { get; set; } = true;

// Usage:
MyConfig.EnableFeature = false; // Automatically saves!
```

**Requirements:**
- **Source Generator**: No special requirements
- **Castle DynamicProxy**: Property must be `virtual`

### On Methods

```csharp
[AutoSave]
public virtual void UpdateSettings()
{
    // Method logic
    // Configuration saves automatically after execution
}

// Usage:
MyConfig.UpdateSettings(); // Automatically saves after execution!
```

**Requirements:**
- **Source Generator**: No special requirements
- **Castle DynamicProxy**: Method must be `virtual`

### Important Notes

#### Source Generator Approach
- Methods marked with `[AutoSave]` will save the entire configuration after execution
- If a method modifies an `[AutoSave]` property but the method itself is **not** marked with `[AutoSave]`, the configuration will **not** auto-save

```csharp
public void WontAutoSave()
{
    AutoSavedProperty = false; // Won't auto-save because method lacks [AutoSave]
}

[AutoSave]
public void WillAutoSave()
{
    AutoSavedProperty = false; // Will auto-save because method has [AutoSave]
}
```

#### Castle DynamicProxy Approach
- Methods don't need `[AutoSave]` if they only modify `[AutoSave]` properties
- The property setter will trigger the save automatically

```csharp
public void RegularMethod()
{
  AutoSavedProperty = !AutoSavedProperty; // Auto-saves via property!
}
```

---

## Using NoireConfigManager

The `NoireConfigManager` provides centralized configuration management with caching.

### Get Configuration

```csharp
// Get or create configuration (cached)
var config = NoireConfigManager.GetConfig<MyConfig>();

// Get fresh instance (not cached)
var freshConfig = NoireConfigManager.LoadConfigFresh<MyConfig>();
```

### Save Configuration

```csharp
// Save a specific configuration
var config = NoireConfigManager.GetConfig<MyConfig>();
NoireConfigManager.SaveConfig(config);
// Or saving the instance directly
config.Save();

// Save all cached configurations
NoireConfigManager.SaveAllCached();
```

### Update Configuration

```csharp
// Update and automatically save
NoireConfigManager.UpdateConfig<MyConfig>(config =>
{
    config.SomeProperty = true;
    config.Counter++;
});
```

### Reload Configuration

```csharp
// Reload from disk
var config = NoireConfigManager.ReloadConfig<MyConfig>();
```

### Delete Configuration

```csharp
// Delete file and remove from cache
NoireConfigManager.DeleteConfig<MyConfig>();
```

### Check Existence

```csharp
// Check if configuration file exists
bool exists = NoireConfigManager.ConfigExists<MyConfig>();
```

---

## Configuration Migrations

### What are Migrations?

Configuration migrations allow you to automatically upgrade your configuration files when you change the schema (add/remove/rename properties, change types, etc.). When a user upgrades your plugin to a new version with schema changes, migrations ensure their existing configuration is transformed to match the new structure without data loss.

**Key Concepts:**
- **Version Property**: Each configuration has a `Version` property that tracks the schema version
- **Automatic Execution**: Migrations run automatically when loading a configuration with an older version
- **Migration Chain**: Multiple migrations can be chained together (e.g., v1 -> v2 -> v3)
- **Attention**: Migrations only support upgrading, not downgrading

### Migration Approaches

NoireLib provides **three different ways** to define migrations, each suited for different scenarios.

---

#### 1 - Nested Class Migration

**Define migrations as nested classes inside your configuration class.**

This is the most straightforward approach and keeps your migration logic close to your configuration.

##### Example

```csharp
using System;
using System.Text.Json;
using NoireLib.Configuration;
using NoireLib.Configuration.Migrations;

namespace MyPlugin.Configuration;

[Serializable]
[NoireConfig("MyConfig")]
public class MyConfigInstance : NoireConfigBase
{
    public override int Version { get; set; } = 3; // Current version
    public override string GetConfigFileName() => "MyConfig";

    public bool NewPropertyName { get; set; } = true;
    public int SomeCounter { get; set; } = 0;
    public string UserPreference { get; set; } = "default";

    // Migration from version 1 to version 2: Rename property
    private class MigrationV1ToV2 : ConfigMigrationBase
    {
        public override int FromVersion => 1;
        public override int ToVersion => 2;

        public override string Migrate(JsonDocument jsonDocument)
        {
            return MigrationBuilder.Create()
                .RenameProperty("OldPropertyName", "NewPropertyName")
                .Migrate(jsonDocument, ToVersion);
        }
    }

    // Migration from version 2 to version 3: Add new property
    private class MigrationV2ToV3 : ConfigMigrationBase
    {
        public override int FromVersion => 2;
        public override int ToVersion => 3;

        public override string Migrate(JsonDocument jsonDocument)
        {
            return MigrationBuilder.Create()
                .AddProperty("UserPreference", "default")
                .Migrate(jsonDocument, ToVersion);
        }
    }
}
```

##### How It Works

1. The `MigrationExecutor` automatically discovers all nested classes that implement `IConfigMigration`
2. When loading a v1 configuration, both migrations execute in order: v1 -> v2 -> v3
3. The migrated configuration is automatically saved to disk with the new version

##### Pros
- **Organized** - Migrations live with the configuration they modify
- **No registration needed** - Automatically discovered
- **Simple** - Easy to understand and maintain

##### Cons
- **Class clutter** - Can make configuration class large with many migrations
- **Limited reuse** - Hard to share migrations between configurations

---

#### 2 - Attribute-Based Migration

**Define migrations in separate classes and register them with attributes.**

This approach keeps your configuration class clean while organizing migrations in dedicated files.

##### Example

**Migration Class (Migrations/MyConfigMigrations.cs):**

```csharp
using System.Text.Json;
using NoireLib.Configuration.Migrations;

namespace MyPlugin.Configuration.Migrations;

public class MyConfigMigrationV1ToV2 : ConfigMigrationBase
{
    public override int FromVersion => 1;
    public override int ToVersion => 2;

    public override string Migrate(JsonDocument jsonDocument)
    {
        return MigrationBuilder.Create()
            .RenameProperty("OldPropertyName", "NewPropertyName")
            .DeleteProperty("ObsoleteProperty")
            .Migrate(jsonDocument, ToVersion);
    }
}

public class MyConfigMigrationV2ToV3 : ConfigMigrationBase
{
    public override int FromVersion => 2;
    public override int ToVersion => 3;

    public override string Migrate(JsonDocument jsonDocument)
    {
        return MigrationBuilder.Create()
            .AddProperty("UserPreference", "default")
            .ChangePropertyType<int, string>("SomeCounter", count => count.ToString())
            .Migrate(jsonDocument, ToVersion);
    }
}
```

**Configuration Class:**

```csharp
using System;
using NoireLib.Configuration;
using NoireLib.Configuration.Migrations;
using MyPlugin.Configuration.Migrations;

namespace MyPlugin.Configuration;

[Serializable]
[ConfigMigration(typeof(MyConfigMigrationV1ToV2))]
[ConfigMigration(typeof(MyConfigMigrationV2ToV3))]
public class MyConfig : NoireConfigBase<MyConfig>
{
    public override int Version { get; set; } = 3;
    public override string GetConfigFileName() => "MyConfig";

    public bool NewPropertyName { get; set; } = true;
    public string SomeCounter { get; set; } = "0"; // Changed to string in v3
    public string UserPreference { get; set; } = "default";
}
```

##### How It Works

1. Apply `[ConfigMigration(typeof(MigrationClass))]` attributes to your configuration class
2. Each attribute registers one migration class
3. Migrations are discovered and executed in the correct order automatically

##### Pros
- **Clean separation** - Configuration class stays focused on data
- **Better organization** - Group migrations in a dedicated folder/namespace
- **Reusable** - Can share migration classes across configurations if needed
- **Explicit** - Easy to see which migrations are registered

##### Cons
- **Attribute overhead** - Need to add attributes for each migration

---

#### 3 - Runtime Migration Registration

**Register migrations dynamically at runtime using NoireConfigManager.**

This approach is useful for plugin systems, dynamic configuration, or when you need programmatic control over migrations.

##### Example

**Migration Classes (Migrations/DynamicMigrations.cs):**

```csharp
using System.Text.Json;
using NoireLib.Configuration.Migrations;

namespace MyPlugin.Configuration.Migrations;

public class DynamicMigrationV1ToV2 : ConfigMigrationBase
{
    public override int FromVersion => 1;
    public override int ToVersion => 2;

    public override string Migrate(JsonDocument jsonDocument)
    {
        return MigrationBuilder.Create()
            .RenameProperty("LegacyName", "ModernName")
            .Migrate(jsonDocument, ToVersion);
    }
}

public class DynamicMigrationV2ToV3 : ConfigMigrationBase
{
    public override int FromVersion => 2;
    public override int ToVersion => 3;

    public override string Migrate(JsonDocument jsonDocument)
    {
        return MigrationBuilder.Create()
            .AddProperty("NewFeatureEnabled", false)
            .Migrate(jsonDocument, ToVersion);
    }
}
```

**Plugin Initialization:**

```csharp
using NoireLib;
using NoireLib.Configuration;
using MyPlugin.Configuration;
using MyPlugin.Configuration.Migrations;

public class MyPlugin : IDalamudPlugin
{
    public void Initialize()
    {
        // Initialize NoireLib first
        NoireService.Initialize(pluginInterface, logger);

        // Register migrations dynamically
        NoireConfigManager.RegisterMigration<MyConfigInstance>(new DynamicMigrationV1ToV2());
        NoireConfigManager.RegisterMigration<MyConfigInstance>(new DynamicMigrationV2ToV3());

        // Now when config loads, migrations will be applied
        var config = NoireConfigManager.GetConfig<MyConfigInstance>();
    }

    public void Dispose()
    {
        // Optional: Clear migrations on cleanup
        NoireConfigManager.ClearMigrations();
    }
}
```

**Configuration Class:**

```csharp
using System;
using NoireLib.Configuration;

namespace MyPlugin.Configuration;

[Serializable]
[NoireConfig("MyConfig")]
public class MyConfigInstance : NoireConfigBase
{
    public override int Version { get; set; } = 3;
    public override string GetConfigFileName() => "MyConfig";

    public string ModernName { get; set; } = "value";
    public bool NewFeatureEnabled { get; set; } = false;
}
```

##### How It Works

1. Call `NoireConfigManager.RegisterMigration<TConfig>(migrationInstance)` before first config access
2. Registered migrations are added to the runtime migration registry
3. When configurations load, runtime migrations are discovered alongside nested/attribute-based ones
4. Optionally call `NoireConfigManager.ClearMigrations()` to remove runtime registrations

##### Pros
- **Maximum flexibility** - Register migrations conditionally based on runtime logic
- **Dynamic scenarios** - Can add/remove migrations based on configuration, user settings, etc.
- **Testing** - Easy to test migrations in isolation

##### Cons
- **Manual registration** - Must remember to register before first config access
- **Order matters** - Registration must happen during initialization
- **Less discoverable** - Migrations aren't visible by looking at the configuration class

---

### Using MigrationBuilder

The `MigrationBuilder` provides a fluent API for common migration operations. This is the recommended way to write migrations.

#### Common Operations

```csharp
public override string Migrate(JsonDocument jsonDocument)
{
    return MigrationBuilder.Create()
        // Rename a property
        .RenameProperty("OldName", "NewName")
        
        // Delete properties
        .DeleteProperty("ObsoleteProperty")
        .DeleteProperties("Prop1", "Prop2", "Prop3")
        
        // Add new properties with default values
        .AddProperty("NewFeature", true)
        .AddProperty("MaxRetries", 3)
        
        // Change property types with conversion
        .ChangePropertyType<int, string>("Counter", count => count.ToString())
        .ChangePropertyType<string, bool>("Enabled", str => str == "true")
        
        // Transform property values
        .TransformProperty<int>("Level", level => Math.Max(level, 1))
        
        // Add computed properties from existing data
        .AddComputedProperty("FullName", root =>
        {
            var firstName = root.GetProperty("FirstName").GetString();
            var lastName = root.GetProperty("LastName").GetString();
            return $"{firstName} {lastName}";
        })
        
        // Custom operations for complex scenarios
        .WithCustomOperation((root, writer) =>
        {
            // Write custom JSON transformation
            if (root.TryGetProperty("ComplexData", out var data))
            {
                writer.WritePropertyName("TransformedData");
                // ... custom logic
            }
        })
        
        // Build the migrated JSON
        .Migrate(jsonDocument, ToVersion);
}
```

#### Manual Migration (Without MigrationBuilder)

For complex scenarios, you can manually manipulate JSON:

```csharp
public class ComplexMigration : ConfigMigrationBase
{
    public override int FromVersion => 2;
    public override int ToVersion => 3;

    public override string Migrate(JsonDocument jsonDocument)
    {
        using var stream = new MemoryStream();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });

        writer.WriteStartObject();

        var root = jsonDocument.RootElement;
        foreach (var property in root.EnumerateObject())
        {
            if (property.Name == "Version")
                continue;

            // Custom transformation logic
            if (property.Name == "Settings")
            {
                writer.WritePropertyName("Settings");
                TransformSettings(property.Value, writer);
            }
            else
            {
                property.WriteTo(writer);
            }
        }

        writer.WriteNumber("Version", ToVersion);
        writer.WriteEndObject();
        writer.Flush();

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void TransformSettings(JsonElement settings, Utf8JsonWriter writer)
    {
        // Complex nested transformation
        // ...
    }
}
```

---

### Migration Best Practices

#### 1. **Test Migrations with Real Data**

```csharp
public void TestMigrationV1ToV2()
{
    var oldJson = @"{""OldProperty"": true, ""Version"": 1}";
    var document = JsonDocument.Parse(oldJson);
    
    var migration = new MigrationV1ToV2();
    var newJson = migration.Migrate(document);
    
    NoireLogger.LogDebug("Migrated JSON: " + newJson);
}
```

#### 2. **Document Migration Reasons**

```csharp
/// <summary>
/// Migration from V1 to V2.
/// Changes:
/// - Renamed "EnableDebugMode" to "DebugMode" for consistency
/// - Removed "LegacyFeature" (deprecated in 1.5.0)
/// - Added "MaxCacheSize" with default 100
/// </summary>
private class MigrationV1ToV2 : ConfigMigrationBase
{
    // ...
}
```

#### 6. **Never Remove Old Migrations**

Once deployed, migrations should never be removed or modified. Users might be upgrading from any previous version.

---

## Advanced Features

### Cache Management

```csharp
// Clear all cached configurations
NoireConfigManager.ClearCache();

// Unload specific configuration from cache
NoireConfigManager.UnloadConfig<MyConfig>();

// Get number of cached configurations
int count = NoireConfigManager.GetCachedConfigCount();
```

### Configuration Directory

```csharp
// Get the plugin's configuration directory
string? configPath = NoireConfigManager.GetConfigDirectoryPath();
```

### Manual Load/Save

```csharp
// Load configuration from disk
config.Load();

// Save configuration to disk
config.Save();

// Check if configuration file exists
bool exists = config.Exists();

// Delete configuration file
config.Delete();
```

### Instance Access (Source Generator)

If you ever need to access the instance of the configuration when using the Source Generator approach, you can do it like so:
```csharp
// Access the instance directly if needed
var instance = MyConfig.Instance;

// Use instance methods
instance.RegularMethod();

// Copy properties
instance.CopyPropertiesFrom(otherConfig);
```

### Migration Management

```csharp
// Register a migration at runtime
NoireConfigManager.RegisterMigration<MyConfig>(new CustomMigration());

// Clear all runtime-registered migrations (useful for testing)
NoireConfigManager.ClearMigrations();
```

---

## Troubleshooting

### Configuration not saving
- Check that NoireLib is initialized before accessing configurations
- **Source Generator**: Ensure properties/methods are marked with `[AutoSave]`
- **Castle DynamicProxy**: Ensure properties/methods are `virtual` and marked with `[AutoSave]`
- **Legacy**: Ensure you call `Save()` manually after modifications
- Check in the `pluginConfigs` folder in `%APPDATA%/XIVLauncher` for existence of files
- Report this issue if you believe this is a bug

### Configuration file not found
- Configuration files are stored in the plugin's configuration directory
- Use `NoireConfigManager.GetConfigDirectoryPath()` to check the directory
- Files are created automatically on first save

### Source Generator not working
- Verify the `[NoireConfig("ClassName")]` attribute is present
- Check that the class inherits from `NoireConfigBase` and not `NoireConfigBase<T>`
- Clean and rebuild the solution
- Check for compilation errors

### Castle DynamicProxy warnings
- Ensure properties and methods with `[AutoSave]` are marked as `virtual`
- Check `/xllog` for detailed warning messages
- If you see "must be virtual" warnings, add the `virtual` keyword to the indicated properties and methods

### Configuration not loading
- Verify the configuration file exists in the plugin directory
- Check file permissions
- Ensure the JSON format is valid
- Try deleting the file and letting it regenerate
- Check dalamud logs with `/xllog`

### Migration issues

#### Migration not executing
- Verify the `Version` property is incremented in your configuration class
- Check that your migration's `FromVersion` matches the file's version
- Ensure migrations are properly registered (nested class, attribute, or runtime)
- Check `/xllog` for migration discovery and execution logs

#### Wrong migration path
- Verify `FromVersion` and `ToVersion` values create a valid chain
- Example: v1->v2, v2->v3 works; v1->v3 without v2->v3 fails if user has v2
- Use `/xllog` to see the discovered migration path

#### Migration executed but version not updated
- Make sure you updated the Version in your actual configuration class (not just in migrations)
- Check logs in `/xllog` for potential errors

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
