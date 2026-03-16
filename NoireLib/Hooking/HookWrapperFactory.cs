using Dalamud.Hooking;
using Dalamud.Plugin.Services;
using System;
using System.Diagnostics;
using System.Reflection;

namespace NoireLib.Hooking;

using HookBackend = IGameInteropProvider.HookBackend;

/// <summary>
/// Provides helper methods for creating <see cref="HookWrapper{TDelegate}"/> instances.
/// </summary>
public static class HookWrapperFactory
{
    /// <summary>
    /// Creates a hook wrapper from a signature in the game module.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type used by the hook.</typeparam>
    /// <param name="signature">The signature to resolve.</param>
    /// <param name="detour">The detour delegate.</param>
    /// <param name="name">An optional friendly name for the hook.</param>
    /// <param name="backend">The preferred hook backend.</param>
    /// <param name="autoEnable">Whether the hook should be enabled immediately after creation.</param>
    /// <returns>A new <see cref="HookWrapper{TDelegate}"/> instance.</returns>
    public static HookWrapper<TDelegate> FromSignature<TDelegate>(string signature, TDelegate detour, string? name = null, HookBackend backend = HookBackend.Automatic, bool autoEnable = true)
        where TDelegate : Delegate
        => new(NoireService.GameInteropProvider.HookFromSignature(signature, detour, backend), detour, autoEnable, name);

    /// <summary>
    /// Creates a hook wrapper from an exported symbol.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type used by the hook.</typeparam>
    /// <param name="moduleName">The name of the loaded module.</param>
    /// <param name="exportName">The exported function name.</param>
    /// <param name="detour">The detour delegate.</param>
    /// <param name="name">An optional friendly name for the hook.</param>
    /// <param name="backend">The preferred hook backend.</param>
    /// <param name="autoEnable">Whether the hook should be enabled immediately after creation.</param>
    /// <returns>A new <see cref="HookWrapper{TDelegate}"/> instance.</returns>
    public static HookWrapper<TDelegate> FromSymbol<TDelegate>(string moduleName, string exportName, TDelegate detour, string? name = null, HookBackend backend = HookBackend.Automatic, bool autoEnable = true)
        where TDelegate : Delegate
        => new(NoireService.GameInteropProvider.HookFromSymbol(moduleName, exportName, detour, backend), detour, autoEnable, name);

    /// <summary>
    /// Creates a hook wrapper from a function address.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type used by the hook.</typeparam>
    /// <param name="procAddress">The function address to hook.</param>
    /// <param name="detour">The detour delegate.</param>
    /// <param name="name">An optional friendly name for the hook.</param>
    /// <param name="backend">The preferred hook backend.</param>
    /// <param name="autoEnable">Whether the hook should be enabled immediately after creation.</param>
    /// <returns>A new <see cref="HookWrapper{TDelegate}"/> instance.</returns>
    public static HookWrapper<TDelegate> FromAddress<TDelegate>(IntPtr procAddress, TDelegate detour, string? name = null, HookBackend backend = HookBackend.Automatic, bool autoEnable = true)
        where TDelegate : Delegate
        => new(procAddress, detour, autoEnable, name, backend);

    /// <summary>
    /// Creates a hook wrapper from a function address.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type used by the hook.</typeparam>
    /// <param name="procAddress">The function address to hook.</param>
    /// <param name="detour">The detour delegate.</param>
    /// <param name="name">An optional friendly name for the hook.</param>
    /// <param name="backend">The preferred hook backend.</param>
    /// <param name="autoEnable">Whether the hook should be enabled immediately after creation.</param>
    /// <returns>A new <see cref="HookWrapper{TDelegate}"/> instance.</returns>
    public static HookWrapper<TDelegate> FromAddress<TDelegate>(UIntPtr procAddress, TDelegate detour, string? name = null, HookBackend backend = HookBackend.Automatic, bool autoEnable = true)
        where TDelegate : Delegate
        => new((IntPtr)procAddress, detour, autoEnable, name, backend);

    /// <summary>
    /// Creates a hook wrapper from a function address.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type used by the hook.</typeparam>
    /// <param name="procAddress">The function address to hook.</param>
    /// <param name="detour">The detour delegate.</param>
    /// <param name="name">An optional friendly name for the hook.</param>
    /// <param name="backend">The preferred hook backend.</param>
    /// <param name="autoEnable">Whether the hook should be enabled immediately after creation.</param>
    /// <returns>A new <see cref="HookWrapper{TDelegate}"/> instance.</returns>
    public static unsafe HookWrapper<TDelegate> FromAddress<TDelegate>(void* procAddress, TDelegate detour, string? name = null, HookBackend backend = HookBackend.Automatic, bool autoEnable = true)
        where TDelegate : Delegate
        => new((IntPtr)procAddress, detour, autoEnable, name, backend);

    /// <summary>
    /// Creates a hook wrapper from a function pointer variable.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type used by the hook.</typeparam>
    /// <param name="address">The address of the function pointer variable.</param>
    /// <param name="detour">The detour delegate.</param>
    /// <param name="name">An optional friendly name for the hook.</param>
    /// <param name="autoEnable">Whether the hook should be enabled immediately after creation.</param>
    /// <returns>A new <see cref="HookWrapper{TDelegate}"/> instance.</returns>
    public static HookWrapper<TDelegate> FromFunctionPointerVariable<TDelegate>(IntPtr address, TDelegate detour, string? name = null, bool autoEnable = true)
        where TDelegate : Delegate
        => new(NoireService.GameInteropProvider.HookFromFunctionPointerVariable(address, detour), detour, autoEnable, name);

    /// <summary>
    /// Creates a hook wrapper by rewriting an import entry.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type used by the hook.</typeparam>
    /// <param name="module">The module containing the import table entry. If null, the current process main module is used.</param>
    /// <param name="moduleName">The imported module name.</param>
    /// <param name="functionName">The imported function name.</param>
    /// <param name="hintOrOrdinal">The hint or ordinal of the imported function.</param>
    /// <param name="detour">The detour delegate.</param>
    /// <param name="name">An optional friendly name for the hook.</param>
    /// <param name="autoEnable">Whether the hook should be enabled immediately after creation.</param>
    /// <returns>A new <see cref="HookWrapper{TDelegate}"/> instance.</returns>
    public static HookWrapper<TDelegate> FromImport<TDelegate>(ProcessModule? module, string moduleName, string functionName, uint hintOrOrdinal, TDelegate detour, string? name = null, bool autoEnable = true)
        where TDelegate : Delegate
    {
        var resolvedModule = module ?? Process.GetCurrentProcess().MainModule ?? throw new InvalidOperationException("Unable to resolve the current process main module for import hooking.");
        return new(NoireService.GameInteropProvider.HookFromImport(resolvedModule, moduleName, functionName, hintOrOrdinal, detour), detour, autoEnable, name);
    }

    /// <summary>
    /// Resolves the target function address for a supported XIVClientStructs delegate type.
    /// </summary>
    /// <typeparam name="TDelegate">The delegate type whose address should be resolved.</typeparam>
    /// <returns>The resolved function address.</returns>
    public static IntPtr ResolveAddress<TDelegate>()
        where TDelegate : Delegate
        => ResolveAddress(typeof(TDelegate));

    internal static Hook<TDelegate> CreateResolvedHook<TDelegate>(TDelegate detour, HookBackend backend)
        where TDelegate : Delegate
        => NoireService.GameInteropProvider.HookFromAddress(ResolveAddress<TDelegate>(), detour, backend);

    private static IntPtr ResolveAddress(Type delegateType)
    {
        ArgumentNullException.ThrowIfNull(delegateType);

        var delegateContainerType = delegateType.DeclaringType;
        var ownerType = delegateContainerType?.Name == "Delegates"
            ? delegateContainerType.DeclaringType
            : null;

        if (ownerType == null)
            throw new InvalidOperationException($"Unable to resolve the owning XIVClientStructs type for delegate '{delegateType.FullName}'.");

        var addressesType = ownerType.GetNestedType("Addresses", BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Unable to find the nested 'Addresses' type for '{ownerType.FullName}'.");

        var addressField = addressesType.GetField(delegateType.Name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Unable to find an address field named '{delegateType.Name}' on '{addressesType.FullName}'.");

        var addressValue = addressField.GetValue(null)
            ?? throw new InvalidOperationException($"The address field '{addressesType.FullName}.{delegateType.Name}' returned null.");

        var resolvedAddress = ExtractAddressPointer(addressValue);
        if (resolvedAddress == IntPtr.Zero)
            throw new InvalidOperationException($"The resolved address for '{delegateType.FullName}' was zero.");

        return resolvedAddress;
    }

    private static IntPtr ExtractAddressPointer(object addressValue)
    {
        return addressValue switch
        {
            IntPtr intPtrValue => intPtrValue,
            UIntPtr uintPtrValue => (IntPtr)uintPtrValue,
            _ => ExtractAddressPointerFromMembers(addressValue),
        };
    }

    private static IntPtr ExtractAddressPointerFromMembers(object addressValue)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        var addressType = addressValue.GetType();

        var valueField = addressType.GetField("Value", flags);
        if (valueField?.GetValue(addressValue) is IntPtr fieldIntPtrValue)
            return fieldIntPtrValue;

        if (valueField?.GetValue(addressValue) is UIntPtr fieldUIntPtrValue)
            return (IntPtr)fieldUIntPtrValue;

        var valueProperty = addressType.GetProperty("Value", flags);
        if (valueProperty?.GetValue(addressValue) is IntPtr propertyIntPtrValue)
            return propertyIntPtrValue;

        if (valueProperty?.GetValue(addressValue) is UIntPtr propertyUIntPtrValue)
            return (IntPtr)propertyUIntPtrValue;

        throw new InvalidOperationException($"Unable to extract an address value from '{addressType.FullName}'.");
    }
}
