
# NoireLib Documentation - NoireIPC

You are reading the documentation for `NoireIPC`.

## Table of Contents
- [Overview](#overview)
- [Getting Started](#getting-started)
- [Recommended Approach](#recommended-approach)
  - [1. With Attributes + Consumer Wrappers](#1-with-attributes--consumer-wrappers)
  - [2. Public Consumer Methods and Direct Consumer Events](#2-public-consumer-methods-and-direct-consumer-events)
  - [3. Wrapper Reference](#3-wrapper-reference)
- [Using Attributed IPC with Instances](#using-attributed-ipc-with-instances)
- [Availability and Binding State](#availability-and-binding-state)
- [Advanced APIs](#advanced-apis)
  - [1. Direct `NoireIPC` Facade](#1-direct-noireipc-facade)
  - [2. `NoireIpcScope`](#2-noireipcscope)
  - [3. `NoireIpcChannel`](#3-noireipcchannel)
  - [4. Raw Dalamud Access](#4-raw-dalamud-access)
- [Comparison Table](#comparison-table)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

The `NoireIPC` API is a high-level wrapper over Dalamud IPC that focuses on **simple provider registration**, **simple consumer binding**, and **strong attribute-based ergonomics**.

It provides:
- **Attribute based IPC** for both provider-side and consumer-side code
- **Consumer wrappers** with built-in availability and error tracking via `NoireIpcConsumer<TDelegate>`
- **Event consumer wrappers** via `NoireIpcEventConsumer<TDelegate>`
- **Automatic registration of attributed static types** through `NoireLibMain.Initialize(...)`
- **Easy instance support** through `NoireIPC.Initialize(...)`
- **Advanced manual control** through `NoireIPC`, `NoireIpcScope`, `NoireIpcChannel`, and raw Dalamud call gates

The strongest part of `NoireIPC` is the combination of:
- `[NoireIpcClass]`
- `[NoireIpc]`
- `NoireIpcConsumer<TDelegate>`
- `NoireIpcEventConsumer<TDelegate>`

If you want the easiest and cleanest setup, start with the attributed approach below.

---

## Getting Started

***❗ We will assume you have already initialized NoireLib in your plugin.
If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).***

### Things to know

`NoireIPC` supports two broad IPC styles:
- **Call-style IPC**: actions and functions
- **Event-style IPC**: publish/subscribe message channels

For attributed IPC:
- decorate your class with `[NoireIpcClass]`
- decorate members with `[NoireIpc]`
- use **methods/events** for provider-side exposure
- use **consumer wrappers**, **consumer methods**, or **consumer events** on the consumer side

Static attributed types are automatically registered during `NoireLibMain.Initialize(...)`.<br/>
Instance attributed types must be initialized manually with `NoireIPC.Initialize()`.
 

## Recommended Approach

## 1. With Attributes + Consumer Wrappers

**This is the recommended way to use `NoireIPC`.**<br/>
You expose providers with attributed methods/events and consume them with attributed wrapper properties.

This gives you:
- clean provider code
- clean consumer code
- availability checks
- error tracking
- minimal boilerplate

### Provider Example

```csharp
using System;
using NoireLib.IPC;

namespace MyPlugin.IPC;

[NoireIpcClass("MyPlugin.Counter")] // Registers "MyPlugin.Counter" as the default prefix for this class
public static class CounterProvider
{
    private static int counter;

    [NoireIpc("GetCounter")] // Will be registered with the prefix overwrite: "MyPlugin.Counter.GetCounter"
    public static int DoGetCounter() => counter;

    [NoireIpc] // Will be registered with the method name: "MyPlugin.Counter.Increment"
    public static void Increment(int amount)
    {
        counter += amount;
        Updated?.Invoke($"Counter is now {counter}.");
    }

    [NoireIpc("OnUpdated")] // Will be registered with the prefix overwrite: "MyPlugin.Counter.OnUpdated"
    public static event Action<string>? OnUpdated;
}
```

### Consumer Example with Wrappers

```csharp
using System;
using NoireLib.IPC;

namespace MyPlugin.IPC;

[NoireIpcClass("MyPlugin.Counter")] // Registers "MyPlugin.Counter" as the default prefix for this class
public static class CounterConsumer
{
    [NoireIpc] // Will consume "MyPlugin.Counter.GetCounter"
    public static NoireIpcConsumer<Func<int>> GetCounter { get; set; }

    [NoireIpc] // Will consume "MyPlugin.Counter.Increment"
    public static NoireIpcConsumer<Action<int>> Increment { get; set; }

    [NoireIpc] // Will consume "MyPlugin.Counter.OnUpdated"
    public static NoireIpcEventConsumer<Action<string>> OnUpdated { get; set; }
}
```

### Usage

```csharp
if (CounterConsumer.GetCounter.IsAvailable)
    var current = CounterConsumer.GetCounter.Invoke();

CounterConsumer.Increment.TryInvoke(5);
// Or using InvokeOrDefault
CounterConsumer.Increment.InvokeOrDefault(5, 0);

CounterConsumer.OnUpdated += message =>
{
    // Do something
};
```

### Why this approach is preferred

#### Provider Side
- Methods and events look natural
- No manual factory code is needed
- Static providers are auto-registered

#### Consumer Side
- `NoireIpcConsumer<TDelegate>` is explicit and safe
- `NoireIpcEventConsumer<TDelegate>` allow event-style subscriptions anywhere
- The wrappers expose binding and availability state directly

---

## 2. Public Consumer Methods and Direct Consumer Events

When wrappers are not what you want, you can consume event-style IPC directly with attributed methods or attributed events.

### Public Consumer Method

Use this when you want a normal method callback.

```csharp
[NoireIpc("OnUpdated", Mode = NoireIpcMode.Consumer, Target = NoireIpcTargetKind.Event)]
public static void HandleUpdated(string message)
{
    // React to event
}
```


### When metadata is required

For wrapper properties like `NoireIpcConsumer<TDelegate>` or `NoireIpcEventConsumer<TDelegate>`, `NoireIPC` can infer the correct behavior automatically.<br/>
For **public methods** and **direct consumer events**, `Mode` and `Target` should be specified explicitly so there is no ambiguity.

### `NoireIpcMode`

```csharp
public enum NoireIpcMode
{
    Auto,
    Provider,
    Consumer,
}
```

### `NoireIpcTargetKind`

```csharp
public enum NoireIpcTargetKind
{
    Auto,
    Call,
    Event,
}
```

In practice:
- provider methods and wrapper properties usually work with defaults
- Event consumer methods should explicitly specify `Mode = Consumer` and `Target = Event`

---

## 3. Wrapper Reference

### `NoireIpcConsumer<TDelegate>`

Use `NoireIpcConsumer<TDelegate>` for call-style action/function IPC.

#### Typical property declarations

```csharp
[NoireIpc("Ping")]
public static NoireIpcConsumer<Func<string, string>> Ping { get; set; }

[NoireIpc("Reset")]
public static NoireIpcConsumer<Action> Reset { get; set; }
```

#### Main members
- `FullName`
- `BindingError`
- `IsAvailable`
- `Delegate`
- `EnsureAvailable()`
- `InvokeRaw(...)`
- `InvokeRaw<TResult>(...)`
- `TryInvoke(...)`
- `TryInvoke<TResult>(out TResult? result, ...)`
- `InvokeRawOrDefault<TResult>(...)`
- `Unavailable(...)`

#### Example

```csharp
if (MyConsumer.Ping.IsAvailable)
    var result = MyConsumer.Ping.Invoke("Noire");

if (!MyConsumer.Reset.TryInvoke())
    // IPC unavailable or invocation failed
```

### `NoireIpcEventConsumer<TDelegate>`

Use `NoireIpcEventConsumer<TDelegate>` for event-style consumer wrappers.

#### Typical property declaration

```csharp
[NoireIpc("Updated")]
public static NoireIpcEventConsumer<Action<string>> Updated { get; set; }
```

#### Main members
- `FullName`
- `SubscriptionCount`
- `Subscribe(...)`
- `TrySubscribe(...)`
- `Unsubscribe(...)`
- `TryUnsubscribe(...)`
- `UnsubscribeAll()`
- `operator +`
- `operator -`

#### Example

```csharp
void OnUpdated(string message) { /* ... */ }

MyConsumer.Updated += OnUpdated;
// Or Subscribe
MyConsumer.Updated.Subscribe(OnUpdated);
// Or TrySubscribe
MyConsumer.Updated.TrySubscribe(OnUpdated, out var _);

MyConsumer.Updated -= OnUpdated;
```

---

## Using Attributed IPC with Instances

Static types are easy because they are automatically discovered when decorated with `[NoireIpcClass]`.<br/>
For instance types, you must initialize them manually.

### Provider and Consumer Instances

```csharp
using System;
using NoireLib.IPC;

namespace MyPlugin.IPC;

[NoireIpcClass("MyPlugin.Counter.Instance")]
public sealed class CounterProviderInstance
{
    public CounterProviderInstance()
    {
        NoireIPC.Initialize(this); // Will initialize with the "MyPlugin.Counter.Instance" prefix
        
        // Alternatively, if you don't decorate the class with [NoireIpcClass],
        // then you can call NoireIPC.Initialize(this, "MyPlugin.Counter.Instance");
    }

    private int counter;

    [NoireIpc("GetCounter")]
    public int GetCounter() => counter;

    [NoireIpc("Increment")]
    public void Increment(int amount)
    {
        counter += amount;
        Updated?.Invoke($"Counter is now {counter}.");
    }

    [NoireIpc("Updated")]
    public event Action<string>? Updated;
}

public sealed class CounterConsumerInstance
{
    public CounterConsumerInstance()
    {
        NoireIPC.Initialize(this);
    }
    
    [NoireIpc("MyPlugin.Counter.Instance.GetCounter")]
    public NoireIpcConsumer<Func<int>> GetCounter { get; set; }

    [NoireIpc("MyPlugin.Counter.Instance.Increment")]
    public NoireIpcConsumer<Action<int>> Increment { get; set; }

    [NoireIpc("MyPlugin.Counter.Instance.Updated")]
    public NoireIpcEventConsumer<Action<string>> Updated { get; set; }
}
```

---

## Availability and Binding State

The wrapper types are designed so you can safely inspect IPC state before invoking or subscribing.

### Call Consumer Availability

```csharp
if (MyConsumer.GetCounter.IsAvailable)
{
    var count = MyConsumer.GetCounter.InvokeRaw<int>();
}
else if (MyConsumer.GetCounter.BindingError != null)
{
    // Binding failed
}
```

---

## Advanced APIs

The sections above should cover most plugins.<br/>
The APIs below are for users who want more control over naming, scoping, message result types, manual registration, or direct Dalamud interop.

## 1. Direct `NoireIPC` Usage

You can use `NoireIPC` directly without attributes.

### Register Providers

```csharp
NoireIPC.RegisterFunc("GetCounter", (Func<int>)(() => 42), prefix: "MyPlugin");
NoireIPC.RegisterAction("Reset", (Action)(() => NoireLogger.LogInfo("Reset")), prefix: "MyPlugin");
```

### Invoke Call-Style IPC

```csharp
var result = NoireIPC.InvokeFunc<int>("GetCounter", prefix: "MyPlugin");
NoireIPC.InvokeAction("Reset", prefix: "MyPlugin");
```

### Subscribe / Send Event-Style IPC

```csharp
NoireIPC.Subscribe("Updated", (Action<string>)(message => NoireLogger.LogInfo(message)), prefix: "MyPlugin");
NoireIPC.Send("Updated", prefix: "MyPlugin", arguments: ["Hello from IPC"]);
```

### Availability Checks

```csharp
bool funcAvailable = NoireIPC.IsFuncAvailable<int>("GetCounter", prefix: "MyPlugin", useDefaultPrefix: false);
bool actionAvailable = NoireIPC.IsActionAvailable("Reset", prefix: "MyPlugin", useDefaultPrefix: false);
```

---

## 2. `NoireIpcScope`

Use `NoireIpcScope` when several IPC channels share the same prefix and message configuration.

```csharp
var scope = NoireIPC.Scope(prefix: "MyPlugin.Counter");

scope.RegisterFunc("GetCounter", (Func<int>)(() => 42));
scope.RegisterAction("Increment", (Action<int>)(value => NoireLogger.LogInfo(value)));

var count = scope.InvokeFunc<int>("GetCounter");
scope.InvokeAction("Increment", 5);
```

---

## 3. `NoireIpcChannel`

Use `NoireIpcChannel` when you want to work with one specific channel object.

```csharp
var channel = NoireIPC.Channel("Updated", prefix: "MyPlugin.Counter");

channel.Subscribe((Action<string>)(message => NoireLogger.LogInfo(message)));
channel.Send("Updated from channel");
```

`NoireIpcChannel` gives you a focused API for one resolved name:
- `Register(...)`
- `RegisterAction(...)`
- `RegisterFunc(...)`
- `Subscribe(...)`
- `Send(...)`
- `InvokeAction(...)`
- `InvokeFunc<TResult>(...)`
- `IsAvailable(...)`
- `IsActionAvailable(...)`
- `IsFuncAvailable<TResult>(...)`
- `GetRawProvider(...)`
- `GetRawSubscriber(...)`

---

## 4. Raw Dalamud Access

When you need full control over the underlying call gates, use:

```csharp
var rawProvider = NoireIPC.GetRawProvider("MyPlugin.Counter.GetCounter", typeof(int));
var rawSubscriber = NoireIPC.GetRawSubscriber("MyPlugin.Counter.GetCounter", typeof(int));
```

You can also retrieve raw provider/subscriber objects from `NoireIpcChannel`.

This is intended for advanced scenarios only.

---

## Troubleshooting

### Consumer wrapper is unavailable
- Make sure the provider is initialized first
- Check that the provider and consumer names resolve to the same full IPC name
- If using attributes, ensure the type is decorated with `[NoireIpcClass]` for static classes
- For instance types, ensure you called `NoireIPC.Initialize(instance)`
- Check `/xllog` for binding or invocation failures

### Attributed static type is not working
- Static attributed types are registered automatically only if the type has `[NoireIpcClass]`
- Automatic registration happens through `NoireLibMain.Initialize(...)`
- If the type is not static, it will not be auto-registered

### Event consumer method is not binding as expected
- For event consumer methods, prefer explicit metadata:
  - `Mode = NoireIpcMode.Consumer`
  - `Target = NoireIpcTargetKind.Event`
- Wrapper properties do not require that extra metadata

### Wrapper property throws on use
- Check `BindingError`
- Check `IsAvailable`
- Call `EnsureAvailable()` before using it if you want a clear failure path

### Message channels and call channels are mixed up
- Use `NoireIpcConsumer<TDelegate>` for call-style action/function IPC
- Use `NoireIpcEventConsumer<TDelegate>` for event-style publish/subscribe IPC
- Use `Target = NoireIpcTargetKind.Event` for explicit Even consumer methods when needed

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
