using System;
using System.Collections.Concurrent;
using System.Reflection;

namespace NoireLib.Hooking;

/// <summary>
/// Turns a <see cref="HookTarget"/> into a function address. Symbol and import targets have no address until
/// Dalamud resolves them, so they come back as zero and are verified after creation.
/// </summary>
internal static class HookAddressResolver
{
    private const BindingFlags StaticMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly ConcurrentDictionary<Type, FieldInfo> AddressFields = new();

    /// <summary>
    /// Resolves the address a target points at, or zero when it cannot be resolved yet.
    /// </summary>
    /// <param name="target">The target to resolve.</param>
    /// <returns>The address, or zero.</returns>
    public static unsafe nint Resolve(HookTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        switch (target.Kind)
        {
            case HookTargetKind.ClientStructs:
                return ResolveClientStructs(target.DelegateType!);

            case HookTargetKind.Address:
                return target.Pointer;

            case HookTargetKind.Vtable:
                return target.Pointer == 0 ? 0 : ((nint*)target.Pointer)[target.VtableSlot];

            case HookTargetKind.FunctionPointerVariable:
                return target.Pointer == 0 ? 0 : *(nint*)target.Pointer;

            case HookTargetKind.Signature:
                return ScanSignature(target.SignatureText!);

            case HookTargetKind.Deferred:
                return target.Resolver!();

            default:
                return 0;
        }
    }

    /// <summary>
    /// Scans the game module for a signature, following a leading relative call or jump to the function it targets.
    /// </summary>
    /// <param name="signature">The byte signature.</param>
    /// <returns>The address, or zero when the bytes are not present.</returns>
    public static nint ScanSignature(string signature)
    {
        if (!NoireService.IsInitialized())
            return 0;

        try
        {
            // ScanText follows a leading E8 or E9 to the callee: hooking the matched call site instead corrupts the caller.
            return NoireService.SigScanner.ScanText(signature);
        }
        catch (Exception ex)
        {
            NoireLogger.LogDebug($"A signature did not resolve: {ex.Message}", HookLog.Prefix);
            return 0;
        }
    }

    /// <summary>
    /// Reads the address XIVClientStructs holds for the function the delegate describes.
    /// </summary>
    /// <param name="delegateType">A delegate nested in a XIVClientStructs <c>Delegates</c> container.</param>
    /// <returns>The address.</returns>
    /// <exception cref="InvalidOperationException">The delegate is not shaped like a XIVClientStructs delegate, or its address is unresolved.</exception>
    public static nint ResolveClientStructs(Type delegateType)
    {
        ArgumentNullException.ThrowIfNull(delegateType);

        var field = AddressFields.GetOrAdd(delegateType, FindAddressField);
        var value = field.GetValue(null)
            ?? throw new InvalidOperationException($"The XIVClientStructs address field for '{delegateType.FullName}' is null.");

        var address = ExtractPointer(value);
        if (address == 0)
            throw new InvalidOperationException($"XIVClientStructs has not resolved an address for '{delegateType.FullName}'. The function may be missing from this game version.");

        return address;
    }

    /// <summary>
    /// Finds the XIVClientStructs type that declares the function a delegate describes.
    /// </summary>
    /// <param name="delegateType">The delegate type.</param>
    /// <returns>The declaring type, or null when the delegate is not a XIVClientStructs delegate.</returns>
    public static Type? FindOwnerType(Type delegateType)
    {
        var container = delegateType.DeclaringType;
        return container?.Name == "Delegates" ? container.DeclaringType : null;
    }

    private static FieldInfo FindAddressField(Type delegateType)
    {
        var ownerType = FindOwnerType(delegateType)
            ?? throw new InvalidOperationException($"'{delegateType.FullName}' is not nested in a XIVClientStructs 'Delegates' container, so its address cannot be resolved. Pass a HookTarget instead.");

        var addressesType = ownerType.GetNestedType("Addresses", BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"'{ownerType.FullName}' declares no nested 'Addresses' type.");

        return addressesType.GetField(delegateType.Name, StaticMembers)
            ?? throw new InvalidOperationException($"'{addressesType.FullName}' declares no address field named '{delegateType.Name}'.");
    }

    /// <summary>
    /// Reads the pointer out of a XIVClientStructs address entry, whichever shape it takes.
    /// </summary>
    /// <param name="addressValue">The address entry.</param>
    /// <returns>The pointer, or zero when it holds none.</returns>
    public static nint ExtractPointer(object addressValue)
    {
        switch (addressValue)
        {
            case nint pointer:
                return pointer;
            case nuint unsignedPointer:
                return (nint)unsignedPointer;
            case ulong unsignedLong:
                return (nint)unsignedLong;
            case long signedLong:
                return (nint)signedLong;
        }

        var type = addressValue.GetType();

        var field = type.GetField("Value", InstanceMembers);
        if (field != null && TryConvert(field.GetValue(addressValue), out var fromField))
            return fromField;

        var property = type.GetProperty("Value", InstanceMembers);
        if (property != null && TryConvert(property.GetValue(addressValue), out var fromProperty))
            return fromProperty;

        return 0;
    }

    private static bool TryConvert(object? value, out nint pointer)
    {
        switch (value)
        {
            case nint asPointer:
                pointer = asPointer;
                return true;
            case nuint asUnsigned:
                pointer = (nint)asUnsigned;
                return true;
            case ulong asUnsignedLong:
                pointer = (nint)asUnsignedLong;
                return true;
            case long asLong:
                pointer = (nint)asLong;
                return true;
            default:
                pointer = 0;
                return false;
        }
    }
}
