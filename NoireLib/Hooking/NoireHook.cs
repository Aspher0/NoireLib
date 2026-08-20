using Dalamud.Plugin.Services;
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NoireLib.Hooking;

using HookBackend = IGameInteropProvider.HookBackend;

/// <summary>
/// The entry point for the hooking system: the live registry, the group handles, the defaults every
/// new hook inherits, and the lookups that say what a game address actually is.
/// </summary>
public static class NoireHook
{
    /// <summary>
    /// Gets or sets the options a hook created without its own <see cref="HookOptions"/> inherits.
    /// </summary>
    public static HookOptions DefaultOptions { get; set; } = new();

    /// <summary>
    /// Gets a snapshot of every live hook.
    /// </summary>
    public static IReadOnlyList<INoireHook> All => HookRegistry.Snapshot();

    /// <summary>
    /// Gets the number of live hooks.
    /// </summary>
    public static int Count => HookRegistry.Count;

    /// <summary>
    /// Gets a counter incremented whenever hooks are added, removed, or change state.
    /// </summary>
    public static int Version => HookRegistry.Version;

    /// <summary>
    /// Gets the group names currently in use.
    /// </summary>
    public static IReadOnlyList<string> GroupNames
    {
        get
        {
            var names = new List<string>();

            foreach (var hook in HookRegistry.Snapshot())
            {
                if (!string.IsNullOrEmpty(hook.Group) && !names.Contains(hook.Group))
                    names.Add(hook.Group);
            }

            return names;
        }
    }

    /// <summary>
    /// Creates a hook on the function the delegate describes, resolving its address from XIVClientStructs.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type of the hooked function.</typeparam>
    /// <param name="detour">The detour to install.</param>
    /// <param name="autoEnable">Whether the hook is enabled as soon as it installs.</param>
    /// <param name="name">A friendly name for logs and diagnostics.</param>
    /// <returns>The hook.</returns>
    public static NoireHook<TDelegate> Create<TDelegate>(TDelegate detour, bool autoEnable = false, string? name = null)
        where TDelegate : Delegate
        => new(detour, autoEnable, name);

    /// <summary>
    /// Creates a hook on the function a target points at.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type of the hooked function.</typeparam>
    /// <param name="target">Where the function lives.</param>
    /// <param name="detour">The detour to install.</param>
    /// <param name="options">The options to apply.</param>
    /// <returns>The hook.</returns>
    public static NoireHook<TDelegate> Create<TDelegate>(HookTarget target, TDelegate detour, HookOptions options)
        where TDelegate : Delegate
        => new(target, detour, options);

    /// <summary>
    /// Installs a counting passthrough on the function the delegate describes, its detour generated from the
    /// delegate so that it cannot change what the game does.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type of the function to watch.</typeparam>
    /// <param name="name">A friendly name for logs and diagnostics.</param>
    /// <param name="autoEnable">Whether the observer starts counting immediately.</param>
    /// <param name="options">The options to apply.</param>
    /// <returns>The hook, whose <see cref="INoireHook.Stats"/> carries the counts.</returns>
    public static NoireHook<TDelegate> Observe<TDelegate>(string? name = null, bool autoEnable = true, HookOptions? options = null)
        where TDelegate : Delegate
    {
        var configured = (options ?? DefaultOptions).Clone();
        configured.Name = string.IsNullOrWhiteSpace(name) ? $"{typeof(TDelegate).Name} observer" : name;
        configured.Guard = HookGuardMode.None;
        configured.CollectStats = false;
        configured.FaultLimit = 0;
        configured.AutoEnable = false;

        var context = new HookGuardContext<TDelegate>
        {
            Stats = new HookStats(),
            Name = configured.Name!,
            CollectStats = true,
        };

        var hook = new NoireHook<TDelegate>(DetourGuardFactory.CreatePassthrough(context), configured);

        context.Stats = hook.Stats;

        if (hook.State != HookState.Installed)
            return hook;

        context.Original = hook.Original;

        if (autoEnable)
            hook.Enable();

        return hook;
    }

    /// <summary>
    /// Creates a hook on the function a byte signature resolves to.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type of the hooked function.</typeparam>
    /// <param name="signature">The byte signature to scan for.</param>
    /// <param name="detour">The detour to install.</param>
    /// <param name="name">A friendly name for logs and diagnostics.</param>
    /// <param name="backend">The Dalamud hook backend.</param>
    /// <param name="autoEnable">Whether the hook is enabled as soon as it installs.</param>
    /// <returns>The hook.</returns>
    public static NoireHook<TDelegate> FromSignature<TDelegate>(string signature, TDelegate detour, string? name = null, HookBackend backend = HookBackend.Automatic, bool autoEnable = true)
        where TDelegate : Delegate
        => new(signature, detour, autoEnable, name, backend);

    /// <summary>
    /// Creates a hook on an exported symbol in a loaded module.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type of the hooked function.</typeparam>
    /// <param name="moduleName">The module name.</param>
    /// <param name="exportName">The exported function name.</param>
    /// <param name="detour">The detour to install.</param>
    /// <param name="name">A friendly name for logs and diagnostics.</param>
    /// <param name="backend">The Dalamud hook backend.</param>
    /// <param name="autoEnable">Whether the hook is enabled as soon as it installs.</param>
    /// <returns>The hook.</returns>
    public static NoireHook<TDelegate> FromSymbol<TDelegate>(string moduleName, string exportName, TDelegate detour, string? name = null, HookBackend backend = HookBackend.Automatic, bool autoEnable = true)
        where TDelegate : Delegate
        => new(HookTarget.Symbol(moduleName, exportName), detour, autoEnable, name, backend);

    /// <summary>
    /// Creates a hook at an explicit function address.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type of the hooked function.</typeparam>
    /// <param name="procAddress">The function address.</param>
    /// <param name="detour">The detour to install.</param>
    /// <param name="name">A friendly name for logs and diagnostics.</param>
    /// <param name="backend">The Dalamud hook backend.</param>
    /// <param name="autoEnable">Whether the hook is enabled as soon as it installs.</param>
    /// <returns>The hook.</returns>
    public static NoireHook<TDelegate> FromAddress<TDelegate>(nint procAddress, TDelegate detour, string? name = null, HookBackend backend = HookBackend.Automatic, bool autoEnable = true)
        where TDelegate : Delegate
        => new(procAddress, detour, autoEnable, name, backend);

    /// <inheritdoc cref="FromAddress{TDelegate}(nint, TDelegate, string?, HookBackend, bool)"/>
    public static NoireHook<TDelegate> FromAddress<TDelegate>(nuint procAddress, TDelegate detour, string? name = null, HookBackend backend = HookBackend.Automatic, bool autoEnable = true)
        where TDelegate : Delegate
        => new((nint)procAddress, detour, autoEnable, name, backend);

    /// <inheritdoc cref="FromAddress{TDelegate}(nint, TDelegate, string?, HookBackend, bool)"/>
    public static unsafe NoireHook<TDelegate> FromAddress<TDelegate>(void* procAddress, TDelegate detour, string? name = null, HookBackend backend = HookBackend.Automatic, bool autoEnable = true)
        where TDelegate : Delegate
        => new((nint)procAddress, detour, autoEnable, name, backend);

    /// <summary>
    /// Creates a hook by rewriting a function pointer variable rather than the function itself.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type of the hooked function.</typeparam>
    /// <param name="variableAddress">The address of the function pointer variable.</param>
    /// <param name="detour">The detour to install.</param>
    /// <param name="name">A friendly name for logs and diagnostics.</param>
    /// <param name="autoEnable">Whether the hook is enabled as soon as it installs.</param>
    /// <returns>The hook.</returns>
    public static NoireHook<TDelegate> FromFunctionPointerVariable<TDelegate>(nint variableAddress, TDelegate detour, string? name = null, bool autoEnable = true)
        where TDelegate : Delegate
        => new(HookTarget.FunctionPointerVariable(variableAddress), detour, autoEnable, name);

    /// <summary>
    /// Creates a hook by rewriting an entry in a module import table.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type of the hooked function.</typeparam>
    /// <param name="module">The module whose import table is rewritten, or null for the current process main module.</param>
    /// <param name="moduleName">The imported module name.</param>
    /// <param name="functionName">The imported function name.</param>
    /// <param name="hintOrOrdinal">The hint or ordinal of the imported function.</param>
    /// <param name="detour">The detour to install.</param>
    /// <param name="name">A friendly name for logs and diagnostics.</param>
    /// <param name="autoEnable">Whether the hook is enabled as soon as it installs.</param>
    /// <returns>The hook.</returns>
    public static NoireHook<TDelegate> FromImport<TDelegate>(ProcessModule? module, string moduleName, string functionName, uint hintOrOrdinal, TDelegate detour, string? name = null, bool autoEnable = true)
        where TDelegate : Delegate
        => new(HookTarget.Import(module, moduleName, functionName, hintOrOrdinal), detour, autoEnable, name);

    /// <summary>
    /// Reads the address XIVClientStructs holds for the function a delegate describes, without creating a hook.
    /// </summary>
    /// <typeparam name="TDelegate">A delegate nested in a XIVClientStructs <c>Delegates</c> container.</typeparam>
    /// <returns>The address.</returns>
    public static nint ResolveAddress<TDelegate>()
        where TDelegate : Delegate
        => HookAddressResolver.ResolveClientStructs(typeof(TDelegate));

    /// <summary>
    /// Scans the game module for a byte signature, without creating a hook.
    /// </summary>
    /// <param name="signature">The byte signature.</param>
    /// <returns>The address, or zero when the bytes are not present.</returns>
    public static nint ScanSignature(string signature) => HookAddressResolver.ScanSignature(signature);

    /// <summary>
    /// Finds a live hook by name.
    /// </summary>
    /// <param name="name">The hook name.</param>
    /// <returns>The hook, or null when no live hook carries that name.</returns>
    public static INoireHook? Find(string name)
    {
        foreach (var hook in HookRegistry.Snapshot())
        {
            if (string.Equals(hook.Name, name, StringComparison.Ordinal) && !hook.IsDisposed)
                return hook;
        }

        return null;
    }

    /// <summary>
    /// Finds the live hook installed on an address.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The hook, or null when nothing is hooked there.</returns>
    public static INoireHook? AtAddress(nint address)
    {
        foreach (var hook in HookRegistry.Snapshot())
        {
            if (hook.Address == address && !hook.IsDisposed)
                return hook;
        }

        return null;
    }

    /// <summary>
    /// Returns a handle over every hook sharing a group name.
    /// </summary>
    /// <param name="name">The group name.</param>
    /// <returns>The group handle.</returns>
    public static HookGroup Group(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new HookGroup(name);
    }

    /// <summary>
    /// Enables every live hook.
    /// </summary>
    public static void EnableAll() => SetAllEnabled(true);

    /// <summary>
    /// Disables every live hook.
    /// </summary>
    public static void DisableAll() => SetAllEnabled(false);

    /// <summary>
    /// Disposes every live hook.
    /// </summary>
    public static void DisposeAll()
    {
        foreach (var hook in HookRegistry.Snapshot())
            hook.Dispose();
    }

    /// <summary>
    /// Returns what XIVClientStructs declares at an address, or null when it declares nothing there.
    /// </summary>
    /// <param name="address">The address to look up.</param>
    /// <returns>The identity, or null.</returns>
    public static HookIdentity? Identify(nint address) => ClientStructsIndex.Identify(address);

    /// <summary>
    /// Checks a delegate against the function XIVClientStructs declares at an address, without creating a hook.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate to check.</typeparam>
    /// <param name="address">The address to check against.</param>
    /// <param name="strict">Whether types must match exactly rather than only in calling-convention shape.</param>
    /// <returns>The result.</returns>
    public static HookVerificationResult Verify<TDelegate>(nint address, bool strict = false)
        where TDelegate : Delegate
        => ClientStructsIndex.Verify(typeof(TDelegate), address, strict);

    /// <summary>
    /// Renders an address as <c>ffxiv_dx11.exe+0xB30F70</c>.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The rendered address.</returns>
    public static string DescribeAddress(nint address) => HookSignatureFormatter.FormatAddress(address);

    /// <summary>
    /// Gets a value indicating whether the shared <see cref="NoireHookWindow"/> is open.
    /// </summary>
    public static bool IsWindowOpen => NoireHookWindow.IsSharedOpen;

    /// <summary>
    /// Opens the shared hook window, creating and registering it on first use.
    /// </summary>
    public static void ShowWindow() => NoireHookWindow.SetSharedOpen(true);

    /// <summary>
    /// Closes the shared hook window.
    /// </summary>
    public static void HideWindow() => NoireHookWindow.SetSharedOpen(false);

    /// <summary>
    /// Flips the shared hook window open or closed.
    /// </summary>
    public static void ToggleWindow() => NoireHookWindow.SetSharedOpen(!NoireHookWindow.IsSharedOpen);

    private static void SetAllEnabled(bool enabled)
    {
        foreach (var hook in HookRegistry.Snapshot())
        {
            if (!hook.IsDisposed)
                hook.SetEnabled(enabled);
        }
    }
}
