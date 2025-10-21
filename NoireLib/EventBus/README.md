# Module Documentation : NoireEventBus

You are reading the documentation for the `NoireEventBus` module.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Publishing Events](#publishing-events)
- [Subscribing to Events](#subscribing-to-events)
- [Advanced Features](#advanced-features)
- [Examples](#examples)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)

---

## Overview

The `NoireEventBus` is a type-safe pub/sub event bus module for decoupled component communication. It provides:
- **Type-safe event publishing and subscription**
- **Priority-based handler execution** (higher priority handlers execute first)
- **Event filtering** at subscription time
- **Async handler support** with both sync and async publishing
- **Owner-based subscription tracking** for automatic cleanup
- **Exception handling** with configurable modes
- **Statistics and monitoring** for debugging and optimization

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin, and know how to create/register modules.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### 1. Define Your Events

Events should be immutable records for clarity and safety:

```csharp
// Simple event
public record PlayerJobChangedEvent(uint OldJobId, uint NewJobId, string JobName);

// Event with optional parameters
public record ConfigurationChangedEvent(PluginConfig NewConfig, string? ChangedProperty = null);

// Complex event
public record DataLoadCompletedEvent(string DataType, string Identifier, object Data);
```

### 2. Publish Events

Publish events to notify all subscribers:

```csharp
var eventBus = NoireLibMain.GetModule<NoireEventBus>();

// Synchronous publish
eventBus?.Publish(new PlayerJobChangedEvent(
    OldJobId: 19,
    NewJobId: 21,
    JobName: "Warrior"
));

// Asynchronous publish (awaits all async handlers)
await eventBus?.PublishAsync(new ConfigurationChangedEvent(config))!;
```

### 3. Subscribe to Events

Subscribe to events with automatic cleanup:

```csharp
public class MyFeature : IDisposable
{
    private readonly NoireEventBus _eventBus;
    
    public MyFeature(NoireEventBus eventBus)
    {
        _eventBus = eventBus;
        
        // Subscribe with owner tracking for automatic cleanup
        _eventBus.Subscribe<PlayerJobChangedEvent>(OnJobChanged, owner: this);
    }
    
    private void OnJobChanged(PlayerJobChangedEvent evt)
    {
        NoireLogger.LogInfo($"Job changed to {evt.JobName}");
    }
    
    public void Dispose()
    {
        // Unsubscribe all handlers owned by this instance
        _eventBus.UnsubscribeAll(this);
    }
}
```

That's it! You now have a working event bus.

---

## Configuration

### Module Parameters

Configure the event bus with the constructor:

```csharp
var eventBus = new NoireEventBus(
    active: true,                                       // Enable/disable the module
    moduleId: "MyEventBus",                            // Optional identifier for multiple instances
    enableLogging: true,                               // Log event publications and subscriptions
    exceptionHandling: EventExceptionMode.LogAndContinue // How to handle exceptions
);
```

### Exception Handling Modes

Control how exceptions in event handlers are handled:

```csharp
// Log the exception and continue processing other handlers (default)
EventExceptionMode.LogAndContinue

// Log the exception and re-throw it, stopping further handler execution
EventExceptionMode.LogAndThrow

// Suppress the exception silently (not recommended for production)
EventExceptionMode.Suppress
```

### Property Configuration

Configure the module after creation:

```csharp
var eventBus = NoireLibMain.GetModule<NoireEventBus>();

// Enable/disable logging
eventBus.EnableLogging = true;

// Change exception handling mode
eventBus.ExceptionHandling = EventExceptionMode.LogAndThrow;
```

---

## Publishing Events

### Synchronous Publishing

Publish events synchronously (fire-and-forget for async handlers):

```csharp
var eventBus = NoireLibMain.GetModule<NoireEventBus>();

// Simple publish
eventBus?.Publish(new PlayerDiedEvent());

// Publish with data
eventBus?.Publish(new PlayerJobChangedEvent(19, 21, "Warrior"));
```

### Asynchronous Publishing

Publish events asynchronously and await all async handlers:

```csharp
var eventBus = NoireLibMain.GetModule<NoireEventBus>();

// Async publish - waits for all async handlers to complete
await eventBus?.PublishAsync(new DataLoadRequestedEvent("PlayerStats", "12345"))!;
```

**Note:** When using synchronous `Publish()` with async handlers, the async handlers are executed in a fire-and-forget manner. Use `PublishAsync()` if you need to await their completion.

---

## Subscribing to Events

### Basic Subscription

Subscribe to events with automatic type safety:

```csharp
var eventBus = NoireLibMain.GetModule<NoireEventBus>();

// Subscribe with owner for automatic cleanup
var token = eventBus?.Subscribe<PlayerJobChangedEvent>(
    handler: OnJobChanged,
    owner: this
);

private void OnJobChanged(PlayerJobChangedEvent evt)
{
    NoireLogger.LogInfo($"Job: {evt.JobName}");
}
```

### Subscription with Priority

Control the order of handler execution:

```csharp
// High priority - executes first
eventBus?.Subscribe<ConfigurationChangedEvent>(
    handler: SaveConfig,
    priority: 100,
    owner: this
);

// Medium priority
eventBus?.Subscribe<ConfigurationChangedEvent>(
    handler: UpdateUI,
    priority: 50,
    owner: this
);

// Low priority - executes last
eventBus?.Subscribe<ConfigurationChangedEvent>(
    handler: LogChange,
    priority: 0,
    owner: this
);
```

Handlers with higher priority values execute first. Default priority is 0.

### Subscription with Filtering

Filter events at subscription time:

```csharp
// Only receive events where EnableFeatureA changed
eventBus?.Subscribe<ConfigurationChangedEvent>(
    handler: OnConfigChanged,
    filter: evt => evt.ChangedProperty == null || evt.ChangedProperty == "EnableFeatureA",
    owner: this
);

// Only receive events for tank jobs
eventBus?.Subscribe<PlayerJobChangedEvent>(
    handler: OnTankJobChanged,
    filter: evt => evt.NewJobId is 19 or 21 or 32 or 37,
    owner: this
);
```

### Async Subscription

Subscribe with async handlers:

```csharp
eventBus?.SubscribeAsync<DataLoadRequestedEvent>(
    handler: OnDataLoadRequested,
    owner: this
);

private async Task OnDataLoadRequested(DataLoadRequestedEvent evt)
{
    var data = await LoadDataAsync(evt.Identifier);
    // Process data...
}
```

### Unsubscribing

Multiple ways to unsubscribe:

```csharp
// Using subscription token
var token = eventBus?.Subscribe<MyEvent>(OnMyEvent, owner: this);
eventBus?.Unsubscribe(token);

// Unsubscribe all handlers for an owner
eventBus?.UnsubscribeAll(this);

// Unsubscribe all handlers for a specific event type
eventBus?.UnsubscribeAll<MyEvent>();

// Unsubscribe all handlers for a specific event type and owner
eventBus?.UnsubscribeAll<MyEvent>(owner: this);

// Unsubscribe the first handler found
eventBus?.UnsubscribeFirst<MyEvent>(owner: this);
```

---

## Advanced Features

### Multiple Event Bus Instances

Separate concerns with different event buses:

```csharp
public class Plugin : IDalamudPlugin
{
    private readonly NoireEventBus _gameEventBus;
    private readonly NoireEventBus _uiEventBus;
    
    public Plugin()
    {
        NoireLibMain.Initialize(PluginInterface, this);
        
        // Separate bus for game-related events
        _gameEventBus = NoireLibMain.AddModule(new NoireEventBus(
            active: true,
            moduleId: "GameEvents",
            enableLogging: true
        ));
        
        // Separate bus for UI-related events
        _uiEventBus = NoireLibMain.AddModule(new NoireEventBus(
            active: true,
            moduleId: "UIEvents",
            enableLogging: false
        ));
    }
}

// Retrieve buses anywhere in your code
public class SomeOtherClass
{
    public void DoSomething()
    {
        var gameEvents = NoireLibMain.GetModule<NoireEventBus>("GameEvents");
        var uiEvents = NoireLibMain.GetModule<NoireEventBus>("UIEvents");
        
        gameEvents?.Publish(new PlayerDiedEvent());
        uiEvents?.Publish(new WindowOpenedEvent("MainWindow"));
    }
}
```

### Statistics and Monitoring

Monitor event bus activity:

```csharp
var eventBus = NoireLibMain.GetModule<NoireEventBus>();
var stats = eventBus?.GetStatistics();

NoireLogger.LogInfo($"Total Events Published: {stats.TotalEventsPublished}");
NoireLogger.LogInfo($"Total Exceptions Caught: {stats.TotalExceptionsCaught}");
NoireLogger.LogInfo($"Active Subscriptions: {stats.ActiveSubscriptions}");
NoireLogger.LogInfo($"Registered Event Types: {stats.RegisteredEventTypes}");

// Get subscriber count for specific event type
var count = eventBus?.GetSubscriberCount<PlayerJobChangedEvent>();
NoireLogger.LogInfo($"Subscribers for PlayerJobChangedEvent: {count}");
```

### Clearing Subscriptions

Remove all subscriptions:

```csharp
var eventBus = NoireLibMain.GetModule<NoireEventBus>();

// Clear all subscriptions from the event bus
eventBus?.ClearAllSubscriptions();
```

---

## Examples

### Example 1: Configuration Change Notification

Notify all features when configuration changes.

```csharp
// Define the event
public record ConfigurationChangedEvent(PluginConfig NewConfig, string? ChangedProperty = null);

// Configuration manager publishes events
public class ConfigurationManager
{
    private readonly NoireEventBus _eventBus;
    private PluginConfig _config;
    
    public ConfigurationManager(NoireEventBus eventBus, PluginConfig config)
    {
        _eventBus = eventBus;
        _config = config;
    }
    
    public void SaveConfig()
    {
        // Save to disk
        NoireService.PluginInterface.SavePluginConfig(_config);
        
        // Notify all subscribers
        _eventBus.Publish(new ConfigurationChangedEvent(_config));
        
        NoireLogger.LogInfo("Configuration saved and broadcasted");
    }
    
    public void UpdateSetting(string propertyName, object value)
    {
        // Update the config
        var property = typeof(PluginConfig).GetProperty(propertyName);
        property?.SetValue(_config, value);
        
        // Notify with specific property that changed
        _eventBus.Publish(new ConfigurationChangedEvent(_config, propertyName));
    }
}

// Features subscribe to config changes
public class FeatureA : IDisposable
{
    private readonly NoireEventBus _eventBus;
    
    public FeatureA(NoireEventBus eventBus)
    {
        _eventBus = eventBus;
        
        // Subscribe with filter - only care about specific setting
        _eventBus.Subscribe<ConfigurationChangedEvent>(
            handler: OnConfigChanged,
            filter: evt => evt.ChangedProperty == null || evt.ChangedProperty == "EnableFeatureA",
            owner: this
        );
    }
    
    private void OnConfigChanged(ConfigurationChangedEvent evt)
    {
        if (evt.NewConfig.EnableFeatureA)
            Enable();
        else
            Disable();
    }
    
    private void Enable() { /* ... */ }
    private void Disable() { /* ... */ }
    
    public void Dispose()
    {
        _eventBus.UnsubscribeAll(this);
    }
}
```

### Example 2: Async Data Loading

Use async handlers for loading data from external sources.

```csharp
// Define events
public record DataLoadRequestedEvent(string DataType, string Identifier);
public record DataLoadCompletedEvent(string DataType, string Identifier, object Data);
public record DataLoadFailedEvent(string DataType, string Identifier, Exception Error);

// Data loader with async handlers
public class DataLoader : IDisposable
{
    private readonly NoireEventBus _eventBus;
    
    public DataLoader(NoireEventBus eventBus)
    {
        _eventBus = eventBus;
        
        // Subscribe with async handler
        _eventBus.SubscribeAsync<DataLoadRequestedEvent>(
            handler: OnDataLoadRequested,
            owner: this
        );
    }
    
    private async Task OnDataLoadRequested(DataLoadRequestedEvent evt)
    {
        try
        {
            NoireLogger.LogInfo($"Loading {evt.DataType} data for {evt.Identifier}...");
            
            // Simulate async operation
            await Task.Delay(100);
            var data = await LoadDataAsync(evt.DataType, evt.Identifier);
            
            // Publish success event
            _eventBus.Publish(new DataLoadCompletedEvent(
                evt.DataType,
                evt.Identifier,
                data
            ));
        }
        catch (Exception ex)
        {
            // Publish failure event
            _eventBus.Publish(new DataLoadFailedEvent(
                evt.DataType,
                evt.Identifier,
                ex
            ));
        }
    }
    
    private async Task<object> LoadDataAsync(string dataType, string identifier)
    {
        // Your async data loading logic here
        await Task.Delay(50);
        return new { Type = dataType, Id = identifier, Value = "SomeData" };
    }
    
    public void Dispose()
    {
        _eventBus.UnsubscribeAll(this);
    }
}

// Consumer requests and receives data
public class DataConsumer : IDisposable
{
    private readonly NoireEventBus _eventBus;
    
    public DataConsumer(NoireEventBus eventBus)
    {
        _eventBus = eventBus;
        
        _eventBus.Subscribe<DataLoadCompletedEvent>(OnDataLoaded, owner: this);
        _eventBus.Subscribe<DataLoadFailedEvent>(OnDataFailed, owner: this);
    }
    
    public void RequestData(string identifier)
    {
        // Request data asynchronously
        _eventBus.Publish(new DataLoadRequestedEvent("PlayerStats", identifier));
    }
    
    private void OnDataLoaded(DataLoadCompletedEvent evt)
    {
        NoireLogger.LogInfo($"Data loaded: {evt.DataType} - {evt.Identifier}");
        // Use the data
    }
    
    private void OnDataFailed(DataLoadFailedEvent evt)
    {
        NoireLogger.LogError(evt.Error, $"Failed to load {evt.DataType} - {evt.Identifier}");
    }
    
    public void Dispose()
    {
        _eventBus.UnsubscribeAll(this);
    }
}
```

### Example 3: Priority-Based Event Handling

Control the order of handler execution.

```csharp
public record PluginShuttingDownEvent();

public class Plugin : IDalamudPlugin
{
    private readonly NoireEventBus _eventBus;
    
    public Plugin()
    {
        NoireLibMain.Initialize(PluginInterface, this);
        _eventBus = NoireLibMain.AddModule<NoireEventBus>();
        
        // Subscribe with different priorities
        _eventBus.Subscribe<PluginShuttingDownEvent>(
            handler: SaveImportantData,
            priority: 100 // Highest priority - save critical data first
        );
        
        _eventBus.Subscribe<PluginShuttingDownEvent>(
            handler: CloseWindows,
            priority: 50 // Medium priority
        );
        
        _eventBus.Subscribe<PluginShuttingDownEvent>(
            handler: LogShutdown,
            priority: 0 // Lowest priority - log last
        );
    }
    
    private void SaveImportantData(PluginShuttingDownEvent evt)
    {
        NoireLogger.LogInfo("Saving critical data...");
        // Save first
    }
    
    private void CloseWindows(PluginShuttingDownEvent evt)
    {
        NoireLogger.LogInfo("Closing windows...");
        // Close second
    }
    
    private void LogShutdown(PluginShuttingDownEvent evt)
    {
        NoireLogger.LogInfo("Plugin shutdown complete");
        // Log last
    }
    
    public void Dispose()
    {
        // Notify all systems that we're shutting down
        _eventBus.Publish(new PluginShuttingDownEvent());
        
        NoireLibMain.Dispose();
    }
}
```

---

## Best Practices

1. **Always use owner tracking**: Subscribe with `owner: this` to enable automatic cleanup
   ```csharp
   _eventBus.Subscribe<MyEvent>(OnMyEvent, owner: this);
   ```

2. **Dispose properly**: Call `UnsubscribeAll(this)` in your Dispose method
   ```csharp
   public void Dispose()
   {
       _eventBus.UnsubscribeAll(this);
   }
   ```

3. **Use filtering wisely**: Filter at subscription time rather than in the handler
   ```csharp
   // Good - filter at subscription
   _eventBus.Subscribe<MyEvent>(OnMyEvent, filter: evt => evt.Value > 0, owner: this);
   
   // Avoid - filter in handler
   private void OnMyEvent(MyEvent evt)
   {
       if (evt.Value > 0) { /* ... */ }
   }
   ```

4. **Use async appropriately**: Use `PublishAsync()` when you need to await async handlers
    ```csharp
    // Fire-and-forget async handlers
    _eventBus.Publish(myEvent);
    
    // Wait for async handlers to complete
    await _eventBus.PublishAsync(myEvent);
    ```

---

## Troubleshooting

### Events not being received
- Ensure the module is active (`IsActive == true`).
- Verify the subscription was successful (check return value).
- Check that the event type matches exactly.
- Ensure the event was published after subscription.
- Check the dalamud logs with `/xllog`.
- If it still does not work, please report it.

### Handlers not executing in expected order
- Verify priority values (higher values execute first).

### Async handlers not completing
- Use `PublishAsync()` instead of `Publish()` to await completion.
- Check exception handling mode, exceptions may be suppressed.
- Verify async handlers are using `SubscribeAsync()` not `Subscribe()`.

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Changelog Module](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/ChangelogManager/README.md)
