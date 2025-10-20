# NoireLib Documentation - A library for Dalamud plugins development

The first thing you need to do to use this library is to initialize it.<br/>
To do so, call `NoireLibMain.Initialize(PluginInterface, this)` in the constructor of your Plugin.<br/>
Do not forget to dispose it with `NoireLibMain.Dispose()` before unloading your plugin:

```csharp
public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;

    public Plugin()
    {
        NoireLibMain.Initialize(PluginInterface, this);
        // ...
    }

    public void Dispose()
    {
        // ...
        NoireLibMain.Dispose();
    }
}
```

Once initialized, you can fully benefit from the library.

NoireLib also comes with Modules you can use.<br/>
Those modules are all-in-one autonomous packages you can easily add and configure.<br/>
Each of the modules are made with one goal in mind: add it and then forget it.<br/>
As an example, the [Changelog Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/ChangelogManager/README.md) is an all-in-one changelog manager that's either fully automatic or manually managed.<br/>
The examples below show different ways to add modules to your project, and then retrieve them later. For these examples, I will be using the `NoireChangelogManager` module.

The easiest way to add a module is with the `NoireLibMain.AddModule<T>(string? moduleId)` method, where T is the module you want to add.<br/>
This method is useful since it will store the instance of the module for you to retrieve later, without having to manually store it anywhere.<br/>
You can also add multiple modules of the same type if you want that.

```csharp
// Add the module with an ID:
NoireLibMain.AddModule<NoireChangelogManager>("ChangelogModule");

// Or, without ID:
NoireLibMain.AddModule<NoireChangelogManager>();

// Or directly storing the instance of the created module:
var changelogManager = NoireLibMain.AddModule<NoireChangelogManager>();
```

But the best way to create a module is by constructing the module beforehand and then pass it as an argument to the `NoireLibMain.AddModule<T>(T instance)` method.<br/>
This way, you are sure that every module option is set before getting initialized, meaning no unintended behavior will happen.

```csharp
// Constructing the module, hence configuring it directly:
var changelogManager = new NoireChangelogManager(
    active: true,
    moduleId: "ChangelogModule",
    shouldAutomaticallyShowChangelog: true,
);
NoireLibMain.AddModule(changelogManager);
```

You can then retrieve that module at any time, anywhere in your project, hence without having to store the created module, with the `NoireLibMain.GetModule<NoireChangelogManager>(string? moduleId);` method:

```csharp
// Retrieve the module later, anywhere:
var changelogManager = NoireLibMain.GetModule<NoireChangelogManager>("ChangelogModule");
```

Now, of course, you do not need to use `NoireLibMain` to add modules.<br/>
You can also just call the module's constructor and store it yourself somewhere.<br/>
For a list of modules, see the [Modules Section](#modules)

## Modules

- [Changelog Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/ChangelogManager/README.md)
