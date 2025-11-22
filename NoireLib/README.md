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
As an example, the [Changelog Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/ChangelogManager/README.md) is an all-in-one changelog manager that's either fully automatic or manually managed.<br/>
The examples below show different ways to add modules to your project, and then retrieve them later. For these examples, I will be using the `NoireChangelogManager` module.

The easiest way to add a module is with the `NoireLibMain.AddModule<T>(string? moduleId)` method, where T is the module you want to add.<br/>
This method is useful since it will store the instance of the module for you to retrieve later, without having to manually store it anywhere. It will also make it so that disposing NoireLib will also dispose any module instance automatically for you.<br/>
Additionnaly, you can add multiple modules of the same type if you want multiple instances for debugging or any other needs.

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
You can also just call the module's constructor and store it yourself somewhere, but then ***do not forget to dispose the module***, since it won't be done automatically this way.<br/>

Once your module is added, you can access it and use it as you would do with any other object.<br/>
You can then set the module active or inactive with the `SetActive(bool active)` method, and check if it's active with the `IsActive` property.

```csharp
// Activate the module:
changelogManager.SetActive(true);
changelogManager.Activate(); // Alternative
changelogManager.Deactivate(); // Alternative

// Check if the module is active:
bool isActive = changelogManager.IsActive;

// Or with NoireLibMain:
isActive = NoireLibMain.IsModuleActive<NoireChangelogManager>("ChangelogModule");
```

When active, the module functions normally. When inactive, the module will not do anything until reactivated.

All modules share the same few settings and methods:

```csharp
// The active state of the module:
bool isActive = module.IsActive;

// Activate or deactivate the module:
module.SetActive(!isActive);
module.Activate();
module.Deactivate();

// Enable logging for the module
module.EnableLogging();

// Dispose the module when no longer needed (not needed if added with NoireLibMain.AddModule):
module.Dispose();

/*
 * For modules with an associated window
 */

// Gets or sets the window name:
string windowName = module.DisplayWindowName;
// Or with the method (for chaining):
module.SetWindowName(windowName);

// Get the full window name (including ID):
string fullWindowName = module.GetFullWindowName();

// Add title bar buttons:
module.AddTitleBarButton(titleBarButton);

// Remove title bar buttons:
module.RemoveTitleBarButton(index);

// Replace all title bar buttons:
module.SetTitleBarButtons(titleBarButtons);

// Remove all title bar buttons:
module.ClearTitleBarButtons();

// Show or hide the window:
module.SetShowWindow(bool? show); // If null: toggles the window
module.ShowWindow(); // Forces showing the window
module.HideWindow(); // Forces hiding the window
module.ToggleWindow(); // Toggles the window
```

For a list of modules, see the [Modules Section](#modules)

## Modules

- [Changelog Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/ChangelogManager/README.md)
- [Event Bus Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/EventBus/README.md)
- [Update Tracker Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/UpdateTracker/README.md)
- [Task Queue Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Modules/TaskQueue/README.md)
