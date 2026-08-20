# Hooking

`NoireHook` resolves the address from the XIVClientStructs delegate it is declared with, checks that
delegate against the function actually sitting at that address before the hook exists, and runs the
detour inside a guard so an exception cannot reach the game.

It is the library's only hooking system. `HookWrapper` and `HookWrapperFactory` do not exist; the
mapping from their surface is at the bottom of this page.

---

## Start here

```csharp
public static NoireHook<ShellCommandModule.Delegates.ExecuteCommandInner> CommandHook;

CommandHook = new(DetourExecuteCommand, true);

private static unsafe void DetourExecuteCommand(ShellCommandModule* module, Utf8String* message, UIModule* uiModule)
{
    CommandHook.Original(module, message, uiModule);
}
```

The detour is the first argument and is typed as the delegate itself. The second argument enables the
hook as soon as it installs. The address is read from XIVClientStructs, and the hook disposes itself
when NoireLib shuts down.

---

## Every way to say where the function is

```csharp
new(Detour, true)                                       // XIVClientStructs, from the delegate
new("E8 ?? ?? ?? ?? 48 8D 8B", Detour, true)            // byte signature
new(procAddress, Detour, true)                          // an already-resolved address
new(HookTarget.Vtable(vtable, 15), Detour, true)        // anything else, through a target
new(existingDalamudHook, Detour, true)                  // adopt a hook that already exists
```

A signature that starts at an `E8` or `E9` resolves to the function the call targets, not to the
call site. Adopting an existing `Hook<T>` puts it in the registry, the groups and the diagnostics,
but its detour was chosen when it was created, so no fault guard can be applied to it.

`HookTarget` covers the rest:

| Target | Resolves through |
|---|---|
| `HookTarget.ClientStructs<TDelegate>()` | the declaring type's `Addresses` entry (the default) |
| `HookTarget.Address(pointer)` | the pointer as given |
| `HookTarget.Vtable(vtable, slot)` | `vtable[slot]` |
| `HookTarget.Symbol(module, export)` | an exported symbol |
| `HookTarget.FunctionPointerVariable(pointer)` | rewriting the variable rather than the function |
| `HookTarget.Import(module, moduleName, functionName, hintOrOrdinal)` | an import table entry |
| `HookTarget.Signature(bytes)` | a byte scan of the game module |
| `HookTarget.Deferred(resolver)` | a callback retried until it returns non-zero |

The hub carries the same set as factories, which **enable the hook by default** where the
constructors do not:

```csharp
NoireHook.FromSignature<T>(bytes, detour, name);
NoireHook.FromSymbol<T>(module, export, detour, name);
NoireHook.FromAddress<T>(address, detour, name);          // nint, nuint and void* overloads
NoireHook.FromFunctionPointerVariable<T>(address, detour, name);
NoireHook.FromImport<T>(module, moduleName, functionName, hintOrOrdinal, detour, name);
NoireHook.ResolveAddress<T>();                            // the address, without a hook
NoireHook.ScanSignature(bytes);                           // the address, without a hook
```

---

## What happens without asking

- **The delegate is checked against the function at the resolved address.** A delegate that does not
  describe that function throws before the hook is created, naming the function and the delegate it
  should have been.
- **The detour runs inside a guard.** An exception is logged and the original is called, instead of
  crashing the client.
- **The hook is registered.** It appears in `NoireHook.All`, and a second hook landing on the same
  address logs a warning naming both.

---

## Options

`new HookOptions()` is usable as it stands. Every knob is a named property.

| Property | Default | Effect |
|---|---|---|
| `Name` | delegate type name | shown in logs and diagnostics |
| `Group` | `null` | the group used for bulk enable and disable |
| `AutoEnable` | `false` | enable as soon as the hook installs |
| `Verification` | `Throw` | what a mismatched delegate does |
| `StrictVerification` | `false` | require exact types instead of a matching calling-convention shape |
| `Guard` | `CallOriginal` | what a detour that throws does |
| `FaultLimit` | `0` | consecutive faults after which the hook disables itself |
| `FaultLogInterval` | 5 seconds | shortest gap between two fault log entries |
| `CollectStats` | `false` | count calls and measure time in the detour |
| `EnableLogging` | `true` | log creation and disposal at debug level |
| `Backend` | `Automatic` | the Dalamud hook backend |
| `ResolveTimeout` | 30 seconds | how long a deferred target keeps retrying |
| `AutoDispose` | `true` | dispose with NoireLib |

`NoireHook.DefaultOptions` is what a hook created without its own options inherits.

---

## Verification

The check compares the return type first, then the parameters, against the function XIVClientStructs
declares at the address. By default types have to agree in how they are passed rather than exactly: a
pointer, `nint` and `ulong` are one class, `int` and `uint` another, and a float is never an integer.
A pointer written as `ulong` is accepted; a four-byte integer in a pointer's place is not, and a
`void` return against a function returning `bool` never is. `StrictVerification` requires exact types.

`HookVerificationPolicy` decides what a mismatch does: `Throw` (the default, and the hook is never
created), `LogError`, `LogWarning`, or `Ignore`.

Both halves are usable without creating a hook:

```csharp
NoireHook.Identify(address);            // Client::Game::GameMain.ExecuteCommand (ffxiv_dx11.exe+0xB30F70)
NoireHook.Verify<MyDelegate>(address);  // status, both signatures, and the first difference
NoireHook.DescribeAddress(address);     // ffxiv_dx11.exe+0xB30F70
```

An address XIVClientStructs does not describe reports as unverifiable, which is not a failure.

The index behind this is built once, on the first check against an address that did not come from a
delegate, and it reads the interop generator's own resolved-address list. If that list is empty it
falls back to reflecting the whole assembly, which takes seconds; the log line says which path ran
and how many functions it indexed.

---

## Fault guards

| `HookGuardMode` | On an exception |
|---|---|
| `CallOriginal` | log and call the original |
| `ReturnDefault` | log and return `default` |
| `Rethrow` | log and let it propagate |
| `None` | no wrapper and no added cost |

A detour that throws after it has already had an effect gets that effect applied twice under
`CallOriginal`. `FaultLimit` disables a hook that keeps throwing.

`IsGuarded` reports whether the guard is actually in place; an exotic signature the wrapper cannot be
generated for installs unguarded and logs a warning.

---

## Events and callbacks

```csharp
hook.OnHookEvent += (h, e) => { };   // Installed, Failed, Enabled, Disabled, Disposed

hook.AddStateCallback("my-key", (h, e) => { });   // replaces any callback under the same key
hook.ContainsStateCallback("my-key");
hook.RemoveStateCallback("my-key");
hook.ClearStateCallbacks();
hook.StateCallbackKeys;
```

A keyed callback can be removed without holding the delegate that registered it.

---

## Groups and scopes

A group is just a name on a hook. Set it fluently on the short constructor, or in the options:

```csharp
hook = new NoireHook<T>(Detour, true).SetGroup("Emotes");
hook = new NoireHook<T>(Detour, new HookOptions { Group = "Emotes", AutoEnable = true });

hook.SetGroup(null);   // out of every group
```

`NoireHook.Group(name)` is a live view, so a hook that joins later is included and one that leaves is
dropped.

```csharp
NoireHook.Group("Draw3D").Disable();

using (NoireHook.Group("Draw3D").DisabledScope())
{
    // every hook in the group is off in here
}
// each hook goes back to what it was doing, not to a blanket state

using (hook.DisabledScope()) { }
```

---

## Deferred hooks

A target that cannot resolve yet leaves the hook `Pending` instead of throwing, and it retries every
framework update until `ResolveTimeout` elapses. The retry pump is only attached to the framework
while at least one hook is waiting.

```csharp
var hook = new NoireHook<OmSetRenderTargetsFn>(
    HookTarget.Deferred(() => TryGetDeviceVtableSlot()),
    OmDetour,
    new HookOptions { Name = "Draw3D.OMSetRenderTargets", AutoEnable = true });

hook.OnHookEvent += (h, e) => { /* Installed, or Failed after the timeout */ };
```

---

## Watching a function without writing a detour

```csharp
var probe = NoireHook.Observe<GameMain.Delegates.ExecuteCommand>("execute command probe");

probe.Stats.CallCount;
probe.Stats.LastCallUtc;
```

The passthrough is generated from the delegate, so it cannot have the wrong signature and cannot
change what the game does.

---

## Diagnostics

```csharp
NoireHook.All;                  // every live hook
NoireHook.Find("name");
NoireHook.AtAddress(address);
NoireHook.GroupNames;
NoireHook.Version;              // moves whenever hooks or their states change
NoireHook.ToggleWindow();       // the live table
```

Each hook carries `State`, `Address`, `Identity`, `Verification`, `Stats`, `IsGuarded` and
`BackendName`.

### Counting calls

Counters are off by default, so a hook shows `not counting` rather than a number. Switch them on at
any time, from the window's per-row **Count** button, its **Count all**, or in code:

```csharp
hook.CollectsStats = true;
hook.Stats.CallCount;
hook.Stats.Reset();
```

Two limits. Timings (`AverageDetourTime`, `PeakDetourTime`) stay empty unless the hook was created
with `CollectStats` already set, since the wrapper only reads a clock it was built to read. And a hook
created with `Guard = None` and no `FaultLimit` has no wrapper at all, so it cannot start counting
later; give it a guard, or create it with `CollectStats` set.

`NoireHookWindow` is constructed and registered with a window system on the first `ShowWindow` or
`ToggleWindow` call and never before; reading `NoireHook.IsWindowOpen` or calling `HideWindow` does
not construct it. Construct one directly to place it in another window system.

---

## Coming from HookWrapper

`HookWrapper<TDelegate>`, `HookWrapperFactory`, `IHookWrapper`, `IHookWrapper<TDelegate>` and
`HookCallbackKind` do not exist. Each maps onto something here:

| Removed | Replacement |
|---|---|
| `new HookWrapper<T>(detour, true, name)` | `new NoireHook<T>(detour, true, name)` |
| `new HookWrapper<T>(address, detour, true)` | `new NoireHook<T>(address, detour, true)` |
| `new HookWrapper<T>(signature, detour, true)` | `new NoireHook<T>(signature, detour, true)` |
| `new HookWrapper<T>(hook, detour, true)` | `new NoireHook<T>(hook, detour, true)` |
| `HookWrapperFactory.From*` | `NoireHook.From*`, same defaults |
| `HookWrapperFactory.ResolveAddress<T>()` | `NoireHook.ResolveAddress<T>()` |
| `AddStateCallback(key, Action<IHookWrapper, HookCallbackKind>)` | `AddStateCallback(key, Action<INoireHook, HookEvent>)` |
| `HookName` / `HookFullName` | `DelegateType.Name` / `DelegateType.FullName` |

`HookEvent` carries `Installed` and `Failed` alongside `Enabled` / `Disabled` / `Disposed`, since a
`NoireHook` can exist before its address does.

Two settings matter for a hook that runs very often. A hook on a graphics device call should carry
`Guard = HookGuardMode.None` and `Verification = HookVerificationPolicy.Ignore` so it costs nothing
extra, as `Draw3D`'s device taps do. A hook on a function XIVClientStructs does not name, such as the
movement input pair, should set `Verification = HookVerificationPolicy.Ignore` too, since the check
can only ever report that it does not know the function.

---

## Troubleshooting

### The hook throws on creation naming a delegate

The delegate does not describe the function at the address it resolved to. The message prints the
function, the address, both signatures and the first difference. Use the XIVClientStructs delegate
for that function. If the delegate is deliberate, set `Verification` to `LogWarning` or `Ignore`.

### The detour never runs

- Check `IsEnabled`, and that something did not call `NoireHook.DisableAll` or a group scope.
- Check `State`: a deferred hook may still be `Pending`, or `Failed` after its timeout.
- Check `/xllog` for the duplicate-address warning. Another hook on the same address changes what
  each detour sees.

### The hook is `Failed`

The address never resolved. A signature found nothing, a deferred resolver returned zero until the
timeout, or a vtable pointer was null when the hook was created.

### The delegate was accepted but the game misbehaves

Set `StrictVerification` on that hook. The default accepts anything that is passed the same way, so
a genuinely different type of the same width is not reported.

---

## See Also

- [NoireLib documentation](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/README.md)
- [Draw3D](https://github.com/Aspher0/NoireLib/blob/main/NoireLib/Draw3D/README.md)
