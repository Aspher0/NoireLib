# NoireLib Documentation - NoireIPC

You are reading the documentation for `NoireIPC`.

## Table of Contents

- [Overview](#overview)
- [Getting Started](#getting-started)
- [Recommended Approach: Attributes + Consumer Wrappers](#recommended-approach-attributes--consumer-wrappers)
  - [Provider Example](#provider-example)
  - [Consumer Example with Wrappers](#consumer-example-with-wrappers)
  - [Usage](#usage)
  - [Why This Approach is Preferred](#why-this-approach-is-preferred)
- [Consumer Methods and Direct Consumer Events](#consumer-methods-and-direct-consumer-events)
  - [Consumer Method](#consumer-method)
  - [When Metadata is Required](#when-metadata-is-required)
- [Wrapper Reference](#wrapper-reference)
  - [`NoireIpcConsumer<TDelegate>`](#noireipcconsumertdelegate)
  - [`NoireIpcEventConsumer<TDelegate>`](#noireipceventconsumertdelegate)
  - [Strongly Typed Extension Methods](#strongly-typed-extension-methods)
  - [Delegate Availability via `IsIpcAvailable()`](#delegate-availability-via-isipcavailable)
- [Using Attributed IPC with Instances](#using-attributed-ipc-with-instances)
- [Availability and Binding State](#availability-and-binding-state)
- [Attribute Reference](#attribute-reference)
  - [`NoireIpcClassAttribute`](#noireipcclassattribute)
  - [`NoireIpcAttribute`](#noireipcattribute)
- [Enum Reference](#enum-reference)
  - [`NoireIpcMode`](#noireipcmode)
  - [`NoireIpcTargetKind`](#noireipctargetkind)
  - [`NoireIpcRegistrationKind`](#noireipcregistrationkind)
- [Global Configuration](#global-configuration)
- [Handle Types and Lifecycle](#handle-types-and-lifecycle)
- [Advanced APIs](#advanced-apis)
  - [Direct `NoireIPC` Usage](#direct-noireipc-usage)
  - [`NoireIpcScope`](#noireipcscope)
  - [`NoireIpcChannel`](#noireipcchannel)
  - [Name Resolution](#name-resolution)
  - [Raw Dalamud Access](#raw-dalamud-access)
- [Constraints and Limits](#constraints-and-limits)
- [Troubleshooting](#troubleshooting)
- [See Also](#see-also)

---

## Overview

`NoireIPC` is a high-level wrapper over Dalamud IPC that focuses on **simple provider registration**, **simple consumer binding**, and **strong attribute-based ergonomics**.

It provides:

- **Attribute-based IPC** for both provider-side and consumer-side code
- **Consumer wrappers** with built-in availability and error tracking via `NoireIpcConsumer<TDelegate>`
- **Event consumer wrappers** with subscribe/unsubscribe semantics via `NoireIpcEventConsumer<TDelegate>`
- **Strongly typed extension methods** for type-safe invocation on consumer wrappers (up to 8 parameters)
- **Automatic registration of attributed static types** during `NoireLibMain.Initialize(...)`
- **Instance support** through `NoireIPC.Initialize(...)`
- **Global configuration** for default prefixes, name separators, and message result types
- **Scoped and channel-based APIs** for manual control (`NoireIpcScope`, `NoireIpcChannel`)
- **Automatic disposal** of all tracked handles when `NoireLibMain.Dispose()` runs
- **Direct access** to raw Dalamud call gates when needed

The strongest part of `NoireIPC` is the combination of:

- `[NoireIpcClass]`
- `[NoireIpc]`
- `NoireIpcConsumer<TDelegate>`
- `NoireIpcEventConsumer<TDelegate>`

If you want the easiest and cleanest setup, start with the [recommended attributed approach](#recommended-approach-attributes--consumer-wrappers) below.

---

## Getting Started

> **Prerequisite:** NoireLib must already be initialized in your plugin.
> If not, please refer to the [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md).

### IPC Styles

`NoireIPC` supports two broad IPC styles:

- **Call-style IPC** - actions (void) and functions (return value)
- **Event-style IPC** - publish/subscribe message channels

### Attributed IPC at a Glance

- Decorate your class with `[NoireIpcClass]`
- Decorate members with `[NoireIpc]`
- Use **methods** and **events** for provider-side exposure
- Use **consumer wrappers**, **consumer methods**, or **consumer events** on the consumer side

Static attributed types are automatically registered during `NoireLibMain.Initialize(...)`.
Instance attributed types must be initialized manually with `NoireIPC.Initialize(instance)`.

---

## Recommended Approach: Attributes + Consumer Wrappers

**This is the recommended way to use `NoireIPC`.**

You expose providers with attributed methods/events and consume them with attributed wrapper properties. This gives you:

- Clean provider code
- Clean consumer code
- Built-in availability checks
- Error tracking
- Minimal boilerplate

### Provider Example

```csharp
using System;
using NoireLib.IPC;

namespace MyPlugin.IPC;

[NoireIpcClass("MyPlugin.Counter")]
public static class CounterProvider
{
    private static int counter;

    [NoireIpc("GetCounter")] // Registered as "MyPlugin.Counter.GetCounter"
    public static int DoGetCounter() => counter;

    [NoireIpc] // Registered using the method name: "MyPlugin.Counter.Increment"
    public static void Increment(int amount)
    {
        counter += amount;
        OnUpdated?.Invoke($"Counter is now {counter}.");
    }

    [NoireIpc("OnUpdated")] // Registered as "MyPlugin.Counter.OnUpdated"
    public static event Action<string>? OnUpdated;
}
```

### Consumer Example with Wrappers

```csharp
using System;
using NoireLib.IPC;

namespace MyPlugin.IPC;

[NoireIpcClass("MyPlugin.Counter")]
public static class CounterConsumer
{
    [NoireIpc("GetCounter")] // Consumes "MyPlugin.Counter.GetCounter"
    public static NoireIpcConsumer<Func<int>> GetCounter { get; set; }

    [NoireIpc] // Consumes "MyPlugin.Counter.Increment"
    public static NoireIpcConsumer<Action<int>> Increment { get; set; }

    [NoireIpc("OnUpdated")] // Consumes "MyPlugin.Counter.OnUpdated"
    public static NoireIpcEventConsumer<Action<string>> OnUpdated { get; set; }
}
```

### Usage

```csharp
// Strongly typed invocation (via extension methods)
if (CounterConsumer.GetCounter.IsAvailable)
{
    var current = CounterConsumer.GetCounter.Invoke();
}

// Safe invocation that returns false on failure
CounterConsumer.Increment.TryInvoke(5);

// Invocation with fallback
var count = CounterConsumer.GetCounter.InvokeOrDefault(-1);

// Event-style subscription
CounterConsumer.OnUpdated += message =>
{
    // React to the event
};
```

### Why This Approach is Preferred

**Provider side:**

- Methods and events look natural
- No manual factory code is needed
- Static providers are auto-registered during `NoireLibMain.Initialize(...)`

**Consumer side:**

- `NoireIpcConsumer<TDelegate>` is explicit and safe
- `NoireIpcEventConsumer<TDelegate>` allows event-style subscriptions anywhere
- The wrappers expose binding and availability state directly
- Strongly typed extension methods provide `Invoke(...)`, `TryInvoke(...)`, and `InvokeOrDefault(...)` with proper type safety

---

## Consumer Methods and Direct Consumer Events

When wrappers are not what you want, you can consume event-style IPC directly with attributed methods.

### Consumer Method

Use this when you want a plain method callback for an event-style IPC channel.

```csharp
[NoireIpc("OnUpdated", Mode = NoireIpcMode.Consumer, Target = NoireIpcTargetKind.Event)]
public static void HandleUpdated(string message)
{
    // React to the event
}
```

### When Metadata is Required

For wrapper properties (`NoireIpcConsumer<TDelegate>` or `NoireIpcEventConsumer<TDelegate>`), `NoireIPC` can infer the correct behavior automatically.

For **public methods** and **direct consumer events**, `Mode` and `Target` should be specified explicitly so there is no ambiguity:

- `Mode = NoireIpcMode.Consumer` - marks the member as a consumer
- `Target = NoireIpcTargetKind.Event` - marks the member as event-style

In practice:

- Provider methods and wrapper properties usually work with defaults (`Mode = Auto`, `Target = Auto`)
- Event consumer methods should explicitly specify `Mode = Consumer` and `Target = Event`

---

## Wrapper Reference

### `NoireIpcConsumer<TDelegate>`

Use `NoireIpcConsumer<TDelegate>` for call-style action/function IPC.

#### Typical Declarations

```csharp
[NoireIpc("Ping")]
public static NoireIpcConsumer<Func<string, string>> Ping { get; set; }

[NoireIpc("Reset")]
public static NoireIpcConsumer<Action> Reset { get; set; }
```

#### Properties

- `FullName` - the fully qualified IPC channel name
- `BindingError` - the exception captured during binding resolution, if any
- `IsAvailable` - whether the remote IPC provider is currently available
- `Delegate` - the underlying delegate, or `null` if unavailable

#### Methods

- `EnsureAvailable()` - throws if the consumer is not available
- `InvokeRaw(params object?[] args)` - invokes the delegate and returns the raw result
- `InvokeRaw<TResult>(params object?[] args)` - invokes and casts the result to `TResult`
- `TryInvokeRaw(params object?[] args)` - safe invocation that returns `bool`
- `TryInvokeRaw<TResult>(out TResult? result, params object?[] args)` - safe invocation with typed output
- `InvokeRawOrDefault<TResult>(TResult defaultValue, params object?[] args)` - invocation with fallback

#### Implicit Conversion

`NoireIpcConsumer<TDelegate>` can be implicitly converted to the underlying `TDelegate?`:

```csharp
Func<int>? fn = CounterConsumer.GetCounter; // implicit conversion
```

#### Example

```csharp
if (MyConsumer.Ping.IsAvailable)
{
    var result = MyConsumer.Ping.Invoke("Noire");
}

if (!MyConsumer.Reset.TryInvoke())
{
    // IPC unavailable or invocation failed
}
```

---

### `NoireIpcEventConsumer<TDelegate>`

Use `NoireIpcEventConsumer<TDelegate>` for event-style consumer wrappers.

#### Typical Declaration

```csharp
[NoireIpc("Updated")]
public static NoireIpcEventConsumer<Action<string>> Updated { get; set; }
```

#### Properties

- `FullName` - the fully qualified IPC channel name
- `SubscriptionCount` - the number of active subscriptions through this wrapper

#### Methods

- `Subscribe(TDelegate handler)` - subscribes a handler and returns a `NoireIpcSubscription` handle
- `TrySubscribe(TDelegate handler, out NoireIpcSubscription? subscription)` - safe subscription
- `Unsubscribe(TDelegate handler)` - unsubscribes the most recent subscription for the handler
- `TryUnsubscribe(TDelegate handler)` - safe unsubscription
- `UnsubscribeAll()` - disposes every active subscription through this wrapper

#### Operators

- `+=` - subscribes a handler (calls `Subscribe(...)`)
- `-=` - unsubscribes a handler (calls `Unsubscribe(...)`)

#### Example

```csharp
void OnUpdated(string message) { /* ... */ }

// Using operators
MyConsumer.Updated += OnUpdated;
MyConsumer.Updated -= OnUpdated;

// Using methods
var subscription = MyConsumer.Updated.Subscribe(OnUpdated);
MyConsumer.Updated.Unsubscribe(OnUpdated);

// Safe subscription
if (MyConsumer.Updated.TrySubscribe(OnUpdated, out var sub))
{
    // Subscribed successfully
}

// Clear all
MyConsumer.Updated.UnsubscribeAll();
```

---

### Strongly Typed Extension Methods

`NoireIpcConsumerInvokeExtensions` provides strongly typed `Invoke(...)`, `TryInvoke(...)`, and `InvokeOrDefault(...)` extension methods for `NoireIpcConsumer<TDelegate>`. These cover all `Action` and `Func` variants from 0 to 8 parameters.

These extensions avoid the need to use `InvokeRaw(...)` in most cases:

```csharp
// Action with no parameters
consumer.Invoke();
consumer.TryInvoke();

// Func with no parameters
var result = consumer.Invoke();
var fallback = consumer.InvokeOrDefault(defaultValue);

// Action<T1>
consumer.Invoke(arg1);
consumer.TryInvoke(arg1);

// Func<T1, TResult>
var result = consumer.Invoke(arg1);
consumer.TryInvoke(arg1, out var result);
var fallback = consumer.InvokeOrDefault(arg1, defaultValue);

// ... and so on up to 8 parameters
```

---

### Delegate Availability via `IsIpcAvailable()`

Any consumer delegate (including those assigned to plain delegate properties) can be checked for availability using the `IsIpcAvailable()` extension method from `NoireIpcExtensions`:

```csharp
Action<int>? myAction = /* bound by NoireIPC */;

if (myAction.IsIpcAvailable())
{
    myAction(42);
}
```

This works for delegates created internally by `NoireIPC` that are backed by a `NoireIpcConsumerProxy`.

---

## Using Attributed IPC with Instances

Static types are automatically discovered when decorated with `[NoireIpcClass]`.
For instance types, you must initialize them manually.

### Provider Instance

```csharp
using System;
using NoireLib.IPC;

namespace MyPlugin.IPC;

[NoireIpcClass("MyPlugin.Counter.Instance")]
public sealed class CounterProviderInstance
{
    public CounterProviderInstance()
    {
        NoireIPC.Initialize(this); // Uses "MyPlugin.Counter.Instance" from the class attribute

        // If you don't use [NoireIpcClass], you can pass a prefix directly:
        // NoireIPC.Initialize(this, prefix: "MyPlugin.Counter.Instance");
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
```

### Consumer Instance

```csharp
using System;
using NoireLib.IPC;

namespace MyPlugin.IPC;

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

`NoireIPC.Initialize(...)` returns a `NoireIpcGroup` containing all handles created for the instance. The group implements `IDisposable` and can be disposed early if needed, but all handles are automatically disposed when `NoireLibMain.Dispose()` runs.

---

## Availability and Binding State

The wrapper types are designed so you can safely inspect IPC state before invoking or subscribing.

### Call Consumer Availability

```csharp
if (MyConsumer.GetCounter.IsAvailable)
{
    var count = MyConsumer.GetCounter.Invoke();
}
else if (MyConsumer.GetCounter.BindingError != null)
{
    // Binding failed during resolution
}
```

### Guarding with `EnsureAvailable()`

```csharp
try
{
    MyConsumer.GetCounter.EnsureAvailable();
    var count = MyConsumer.GetCounter.Invoke();
}
catch (InvalidOperationException ex)
{
    // Not available or binding error
}
```

### Standalone Availability Checks

You can check availability without a wrapper using the static `NoireIPC` methods:

```csharp
bool funcReady = NoireIPC.IsFuncAvailable<int>("GetCounter", prefix: "MyPlugin");
bool actionReady = NoireIPC.IsActionAvailable("Reset", prefix: "MyPlugin");

// Full signature check
bool available = NoireIPC.IsAvailable(
    "GetCounter",
    parameterTypes: [],
    returnType: typeof(int),
    prefix: "MyPlugin");
```

---

## Attribute Reference

### `NoireIpcClassAttribute`

Applied to a class or struct to define shared IPC metadata for all attributed members inside it.

```csharp
[NoireIpcClass("MyPlugin.Counter")]
public static class CounterProvider { /* ... */ }
```

**Properties:**

- `Prefix` (`string?`) - the prefix applied to annotated members when they do not define their own
- `UseDefaultPrefix` (`bool`, default `true`) - whether default prefix resolution remains enabled
- `MessageResultType` (`Type?`) - the default trailing generic type for message-oriented members

Static types with `[NoireIpcClass]` are automatically registered during `NoireLibMain.Initialize(...)`.

### `NoireIpcAttribute`

Applied to a method, property, or event to mark it for IPC registration or binding.

```csharp
[NoireIpc("GetCounter")]
public static int DoGetCounter() => 42;

[NoireIpc] // Uses the member name
public static NoireIpcConsumer<Action<int>> Increment { get; set; }
```

**Properties:**

- `Name` (`string?`) - the IPC name for the member (defaults to the member name)
- `Prefix` (`string?`) - explicit prefix override for this member
- `UseDefaultPrefix` (`bool`, default `true`) - whether default prefix resolution is enabled
- `Mode` (`NoireIpcMode`, default `Auto`) - how the member should be processed
- `Target` (`NoireIpcTargetKind`, default `Auto`) - the target IPC behavior
- `Kind` (`NoireIpcRegistrationKind`, default `Auto`) - registration kind for provider members
- `MessageResultType` (`Type?`) - trailing generic type for message-oriented members

---

## Enum Reference

### `NoireIpcMode`

Defines how an attributed IPC member should be processed.

```csharp
public enum NoireIpcMode
{
    Auto,     // Infers from the member shape
    Provider, // Processes as a provider registration
    Consumer, // Processes as a consumer binding or subscription
}
```

### `NoireIpcTargetKind`

Defines the target IPC behavior for an attributed member.

```csharp
public enum NoireIpcTargetKind
{
    Auto,  // Infers from the member shape
    Call,  // Call-style action or function IPC
    Event, // Event-style message IPC
}
```

### `NoireIpcRegistrationKind`

Defines how an IPC provider delegate should be registered.

```csharp
public enum NoireIpcRegistrationKind
{
    Auto,     // Chooses automatically from the delegate return type
    Action,   // Registers as an action IPC (void return)
    Function, // Registers as a function IPC (non-void return)
}
```

---

## Global Configuration

`NoireIPC.Configure(...)` lets you set global defaults that affect name resolution and message channels.

```csharp
NoireIPC.Configure(
    defaultPrefix: "MyPlugin",
    usePluginInternalNameAsDefaultPrefix: true,
    nameSeparator: ".",
    defaultMessageResultType: typeof(object));
```

**Parameters:**

- `defaultPrefix` - explicit default prefix used when no prefix is provided to a call
- `usePluginInternalNameAsDefaultPrefix` - when `true` and no explicit prefix is set, the current plugin's internal name is used as the prefix
- `nameSeparator` - the separator inserted between a prefix and a channel name (default `"."`)
- `defaultMessageResultType` - the default trailing generic type for message channels (default `typeof(object)`)

**Static properties (read-only outside `Configure`):**

- `NoireIPC.DefaultPrefix`
- `NoireIPC.UsePluginInternalNameAsDefaultPrefix`
- `NoireIPC.NameSeparator`
- `NoireIPC.DefaultMessageResultType`

Call `NoireIPC.ResetConfiguration()` to restore all defaults.

The configuration is automatically reset when `NoireLibMain.Dispose()` runs.

---

## Handle Types and Lifecycle

Every registration, subscription, or consumer binding creates a tracked handle. All handles inherit from `NoireIpcHandle`.

### `NoireIpcHandle` (base class)

- `FullName` - the fully qualified IPC channel name
- `IsDisposed` - whether the handle has been disposed
- `Dispose()` - unregisters or unsubscribes immediately

### `NoireIpcRegistration`

Returned by `Register(...)`, `RegisterAction(...)`, and `RegisterFunc(...)`.

- `Kind` - the `NoireIpcRegistrationKind` used (`Action` or `Function`)

### `NoireIpcSubscription`

Returned by `Subscribe(...)`.

### `NoireIpcConsumerBinding`

Returned internally when binding consumers to properties or events.

### `NoireIpcGroup`

Returned by `Initialize(...)`, `RegisterType(...)`, `RegisterType<T>(...)`, and `RegisterAttributedTypes(...)`.

- Implements `IReadOnlyList<NoireIpcHandle>` and `IDisposable`
- `Count` - the number of handles in the group
- `Dispose()` - disposes every handle in the group

### Automatic Disposal

All tracked handles are automatically disposed when `NoireLibMain.Dispose()` runs. You do not need to dispose them manually unless you want early cleanup.

---

## Advanced APIs

The sections above should cover most plugins.
The APIs below are for users who want more control over naming, scoping, message result types, manual registration, or direct Dalamud interop.

### Direct `NoireIPC` Usage

You can use `NoireIPC` directly without attributes for full control.

#### Register Providers

```csharp
// Auto-detects Action vs Function from the delegate
NoireIPC.Register("GetCounter", (Func<int>)(() => 42), prefix: "MyPlugin");

// Explicit action registration
NoireIPC.RegisterAction("Reset", (Action)(() => { }), prefix: "MyPlugin");

// Explicit function registration
NoireIPC.RegisterFunc("GetCounter", (Func<int>)(() => 42), prefix: "MyPlugin");
```

#### Invoke Call-Style IPC

```csharp
// Invoke a function
var result = NoireIPC.InvokeFunc<int>("GetCounter", prefix: "MyPlugin");

// Invoke an action
NoireIPC.InvokeAction("Reset", prefix: "MyPlugin");

// With explicit parameter types (useful when arguments can be null)
NoireIPC.InvokeAction("SetName", [typeof(string)], prefix: "MyPlugin", arguments: ["Noire"]);
```

#### Subscribe and Send Event-Style IPC

```csharp
// Subscribe to a message channel
NoireIPC.Subscribe("Updated", (Action<string>)(msg => { }), prefix: "MyPlugin");

// Send a message (inferred types)
NoireIPC.Send("Updated", prefix: "MyPlugin", arguments: ["Hello"]);

// Send with explicit parameter types
NoireIPC.Send("Updated", [typeof(string)], prefix: "MyPlugin", arguments: ["Hello"]);
```

#### Availability Checks

```csharp
bool funcReady = NoireIPC.IsFuncAvailable<int>("GetCounter", prefix: "MyPlugin");
bool actionReady = NoireIPC.IsActionAvailable("Reset", prefix: "MyPlugin");
```

#### Batch Registration with `RegisterType`

You can register all attributed static members of a type without needing `[NoireIpcClass]` for automatic discovery:

```csharp
NoireIpcGroup group = NoireIPC.RegisterType<MyProviderType>(prefix: "MyPlugin");

// Or with a Type object
NoireIpcGroup group = NoireIPC.RegisterType(typeof(MyProviderType), prefix: "MyPlugin");
```

#### Assembly-Wide Registration

`RegisterAttributedTypes(...)` scans an assembly for all static types decorated with `[NoireIpcClass]` and registers them. This is called automatically by `NoireLibMain.Initialize(...)`, but you can also call it manually on other assemblies:

```csharp
NoireIpcGroup group = NoireIPC.RegisterAttributedTypes(typeof(SomeType).Assembly);
```

---

### `NoireIpcScope`

Use `NoireIpcScope` when several IPC channels share the same prefix and message configuration.

```csharp
var scope = NoireIPC.Scope(prefix: "MyPlugin.Counter");

scope.RegisterFunc("GetCounter", (Func<int>)(() => 42));
scope.RegisterAction("Increment", (Action<int>)(value => { }));

var count = scope.InvokeFunc<int>("GetCounter");
scope.InvokeAction("Increment", 5);
```

A scope provides the same registration, invocation, subscription, and send methods as `NoireIPC`, but with a fixed prefix and message result type.

#### Scope API

- `ResolveName(name)` - resolves a local name into its final channel name
- `WithPrefix(prefix, useDefaultPrefix)` - creates a copy of the scope with a different prefix
- `WithMessageResultType(type)` - creates a copy with a different message result type
- `Channel(name)` - creates a `NoireIpcChannel` from the scope
- `Register(name, handler, kind)` / `RegisterAction(name, handler)` / `RegisterFunc(name, handler)`
- `Subscribe(name, handler)`
- `Send(name, args)` / `Send(name, parameterTypes, args)`
- `InvokeAction(name, args)` / `InvokeAction(name, parameterTypes, args)`
- `InvokeFunc<TResult>(name, args)` / `InvokeFunc<TResult>(name, parameterTypes, args)`
- `Initialize(instance, bindingFlags)` - processes attributed members on an instance
- `RegisterType(type, bindingFlags)` - processes attributed static members on a type

#### Unprefixed Scope

Use `NoireIPC.Raw()` to create a scope that does not apply any automatic prefix resolution:

```csharp
var raw = NoireIPC.Raw();
raw.InvokeFunc<int>("SomePlugin.ExactChannelName");
```

---

### `NoireIpcChannel`

Use `NoireIpcChannel` when you want to work with one specific fully resolved channel.

```csharp
var channel = NoireIPC.Channel("Updated", prefix: "MyPlugin.Counter");

channel.Subscribe((Action<string>)(message => { }));
channel.Send("Updated from channel");
```

#### Channel API

- `FullName` - the fully qualified IPC channel name
- `MessageResultType` - the trailing generic type for message operations
- `Register(handler, kind)` / `RegisterAction(handler)` / `RegisterFunc(handler)`
- `Subscribe(handler)`
- `Send(args)` / `Send(parameterTypes, args)`
- `InvokeAction(args)` / `InvokeAction(parameterTypes, args)`
- `InvokeFunc<TResult>(args)` / `InvokeFunc<TResult>(parameterTypes, args)`
- `IsAvailable(parameterTypes, returnType)` / `IsActionAvailable(parameterTypes)` / `IsFuncAvailable<TResult>(parameterTypes)`
- `GetRawProvider(callGateTypes)` / `GetRawSubscriber(callGateTypes)`

---

### Name Resolution

`NoireIPC` resolves IPC names through a prefix + separator + local name pipeline.

**`NoireIPC.BuildName(name, prefix, useDefaultPrefix)`** produces a fully qualified name:

```csharp
// With explicit prefix
NoireIPC.BuildName("GetCounter", prefix: "MyPlugin"); // "MyPlugin.GetCounter"

// With default prefix configured
NoireIPC.Configure(defaultPrefix: "MyPlugin");
NoireIPC.BuildName("GetCounter"); // "MyPlugin.GetCounter"

// Already prefixed names are not double-prefixed
NoireIPC.BuildName("MyPlugin.GetCounter", prefix: "MyPlugin"); // "MyPlugin.GetCounter"
```

**`NoireIPC.ResolvePrefix(prefix, useDefaultPrefix)`** resolves the effective prefix:

1. If an explicit `prefix` is provided, it is used
2. If `useDefaultPrefix` is `true` and `DefaultPrefix` is set, that is used
3. If `useDefaultPrefix` is `true` and `UsePluginInternalNameAsDefaultPrefix` is `true`, the plugin's internal name is used
4. Otherwise, an empty string is returned (no prefix)

---

### Raw Dalamud Access

When you need full control over the underlying call gates:

```csharp
var rawProvider = NoireIPC.GetRawProvider("MyPlugin.Counter.GetCounter", typeof(int));
var rawSubscriber = NoireIPC.GetRawSubscriber("MyPlugin.Counter.GetCounter", typeof(int));
```

You can also retrieve raw provider/subscriber objects from a `NoireIpcChannel`:

```csharp
var channel = NoireIPC.Channel("GetCounter", prefix: "MyPlugin.Counter");
var rawProvider = channel.GetRawProvider(typeof(int));
var rawSubscriber = channel.GetRawSubscriber(typeof(int));
```

This is intended for advanced scenarios only.

---

## Constraints and Limits

- Dalamud IPC supports up to **8 parameters** per delegate
- `ref` and `out` parameters are **not supported**
- The message result type (trailing generic type) **cannot be `void`**
- Open generic methods **cannot be registered** as IPC providers
- Attributed event consumers must use delegates that **return `void`**
- Assembly scanning only auto-registers **static types** with `[NoireIpcClass]`; non-static types are skipped
- Null arguments require **explicit parameter types** to be passed (types cannot be inferred from `null`)

---

## Troubleshooting

### Consumer wrapper is unavailable

- Make sure the provider is initialized first
- Check that the provider and consumer names resolve to the same fully qualified IPC name
- If using attributes, ensure the type is decorated with `[NoireIpcClass]` for static classes
- For instance types, ensure you called `NoireIPC.Initialize(instance)`
- Check `/xllog` for binding or invocation failures
- Check `BindingError` on the wrapper for resolution exceptions

### Attributed static type is not working

- Static attributed types are registered automatically only if the type has `[NoireIpcClass]`
- Automatic registration happens during `NoireLibMain.Initialize(...)`
- The class must be `static` (both `abstract` and `sealed` in IL terms); non-static classes are skipped by `RegisterAttributedTypes`

### Event consumer method is not binding as expected

- For event consumer methods, use explicit metadata:
  - `Mode = NoireIpcMode.Consumer`
  - `Target = NoireIpcTargetKind.Event`
- Wrapper properties (`NoireIpcConsumer`, `NoireIpcEventConsumer`) do not require that extra metadata

### Wrapper property throws on use

- Check `BindingError` for resolution exceptions
- Check `IsAvailable` to verify the remote provider exists
- Call `EnsureAvailable()` before invocation if you want a clear failure path with a descriptive error

### Message channels and call channels are mixed up

- Use `NoireIpcConsumer<TDelegate>` for **call-style** action/function IPC
- Use `NoireIpcEventConsumer<TDelegate>` for **event-style** publish/subscribe IPC
- Use `Target = NoireIpcTargetKind.Event` for explicit event consumer methods

### Null argument causes an error

- When one or more arguments are `null`, their types cannot be inferred
- Use the overload that accepts explicit `Type[] parameterTypes` instead

---

## See Also

- [NoireLib Documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
