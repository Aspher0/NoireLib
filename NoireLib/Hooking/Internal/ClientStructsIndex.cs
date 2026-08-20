using System;
using System.Collections.Generic;
using System.Reflection;

namespace NoireLib.Hooking;

/// <summary>
/// Maps a game address back to the function XIVClientStructs declares there, so a hook delegate can be
/// checked against the real function before the hook exists.
/// </summary>
internal static class ClientStructsIndex
{
    private const BindingFlags NestedTypes = BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags StaticFields = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const string ClientStructsPrefix = "FFXIVClientStructs.FFXIV.";

    private static readonly object BuildLock = new();

    private static Dictionary<nint, HookIdentity>? index;
    private static bool mainModuleRangeRead;
    private static nint mainModuleBase;
    private static int mainModuleSize;

    /// <summary>
    /// Gets the number of indexed functions, building the index if it has not been built.
    /// </summary>
    public static int Count => GetIndex().Count;

    /// <summary>
    /// Returns what XIVClientStructs declares at an address, or null when it declares nothing there.
    /// </summary>
    /// <param name="address">The address to look up.</param>
    /// <returns>The identity, or null.</returns>
    public static HookIdentity? Identify(nint address)
    {
        if (address == 0 || !IsInsideGameModule(address))
            return null;

        return GetIndex().GetValueOrDefault(address);
    }

    /// <summary>
    /// Builds the identity of a XIVClientStructs delegate directly from its declaring types, without touching the index.
    /// </summary>
    /// <param name="delegateType">The delegate type.</param>
    /// <param name="address">The resolved address.</param>
    /// <returns>The identity, or null when the delegate is not a XIVClientStructs delegate.</returns>
    public static HookIdentity? IdentifyDelegate(Type delegateType, nint address)
    {
        var ownerType = HookAddressResolver.FindOwnerType(delegateType);
        return ownerType == null ? null : CreateIdentity(ownerType, delegateType.Name, delegateType, address);
    }

    /// <summary>
    /// Checks a delegate against the function declared at an address.
    /// </summary>
    /// <param name="delegateType">The delegate to check.</param>
    /// <param name="address">The resolved address.</param>
    /// <param name="strict">Whether parameter types must match exactly rather than only in calling-convention shape.</param>
    /// <returns>The result.</returns>
    public static HookVerificationResult Verify(Type delegateType, nint address, bool strict = false)
    {
        var passed = HookSignatureFormatter.Format(delegateType);
        var identity = Identify(address);

        if (identity?.ExpectedDelegateType == null)
            return HookVerificationResult.Unverifiable(delegateType, passed) with { Identity = identity };

        return Compare(delegateType, identity, passed, strict);
    }

    /// <summary>
    /// Checks a delegate against a known identity.
    /// </summary>
    /// <param name="delegateType">The delegate to check.</param>
    /// <param name="identity">What XIVClientStructs declares.</param>
    /// <param name="passed">The checked delegate rendered as a signature.</param>
    /// <param name="strict">Whether parameter types must match exactly.</param>
    /// <returns>The result.</returns>
    public static HookVerificationResult Compare(Type delegateType, HookIdentity identity, string passed, bool strict)
    {
        var expectedType = identity.ExpectedDelegateType;
        if (expectedType == null)
            return HookVerificationResult.Unverifiable(delegateType, passed) with { Identity = identity };

        var expected = HookSignatureFormatter.Format(expectedType);
        var difference = CompareDelegates(delegateType, expectedType, strict);

        return difference == null
            ? new HookVerificationResult(HookVerificationStatus.Matched, delegateType, identity, passed, expected, null)
            : new HookVerificationResult(HookVerificationStatus.Mismatched, delegateType, identity, passed, expected, difference);
    }

    /// <summary>
    /// Compares two delegate signatures and describes the first difference, or returns null when they agree.
    /// </summary>
    /// <param name="passed">The delegate the consumer wrote.</param>
    /// <param name="expected">The delegate XIVClientStructs declares.</param>
    /// <param name="strict">Whether types must match exactly rather than only in calling-convention shape.</param>
    /// <returns>The first difference, or null.</returns>
    public static string? CompareDelegates(Type passed, Type expected, bool strict)
    {
        var passedInvoke = passed.GetMethod("Invoke");
        var expectedInvoke = expected.GetMethod("Invoke");

        if (passedInvoke == null || expectedInvoke == null)
            return null;

        if (!TypesAgree(passedInvoke.ReturnType, expectedInvoke.ReturnType, strict))
            return $"return type is {passedInvoke.ReturnType.Name}, expected {expectedInvoke.ReturnType.Name}";

        var passedParameters = passedInvoke.GetParameters();
        var expectedParameters = expectedInvoke.GetParameters();

        if (passedParameters.Length != expectedParameters.Length)
            return $"takes {passedParameters.Length} parameters, expected {expectedParameters.Length}";

        for (var i = 0; i < passedParameters.Length; i++)
        {
            if (!TypesAgree(passedParameters[i].ParameterType, expectedParameters[i].ParameterType, strict))
                return $"parameter {i + 1} is {passedParameters[i].ParameterType.Name}, expected {expectedParameters[i].ParameterType.Name}";
        }

        return null;
    }

    /// <summary>
    /// Decides whether two types are interchangeable in a hook signature.
    /// </summary>
    /// <param name="passed">The type the consumer wrote.</param>
    /// <param name="expected">The type XIVClientStructs declares.</param>
    /// <param name="strict">Whether the types must be identical.</param>
    /// <returns>True if the types agree.</returns>
    public static bool TypesAgree(Type passed, Type expected, bool strict)
    {
        if (passed == expected)
            return true;

        if (strict)
            return false;

        var passedClass = Classify(passed);

        // Two different aggregates never agree: identical ones returned above, and the class says nothing
        // about their layout.
        return passedClass != ArgumentClass.Aggregate && passedClass == Classify(expected);
    }

    /// <summary>
    /// Groups a type by what a detour actually reads for it, so a pointer written as <c>nint</c> or <c>ulong</c>
    /// is not reported as a mismatch.
    /// </summary>
    /// <param name="type">The type to classify.</param>
    /// <returns>The group.</returns>
    public static ArgumentClass Classify(Type type)
    {
        if (type == typeof(void))
            return ArgumentClass.Void;

        // A pointer and a 64-bit integer arrive in the same general-purpose register, so an address written as
        // nint or ulong is a different spelling rather than a different argument.
        if (type.IsPointer || type.IsFunctionPointer || type.IsByRef || type == typeof(nint) || type == typeof(nuint))
            return ArgumentClass.Register8;

        var effective = type.IsEnum ? Enum.GetUnderlyingType(type) : type;

        if (effective == typeof(long) || effective == typeof(ulong))
            return ArgumentClass.Register8;

        // Narrower integers are kept apart from each other and from Register8: a detour is the callee, so
        // declaring int where the game wrote a byte reads three bytes nobody set.
        if (effective == typeof(bool) || effective == typeof(byte) || effective == typeof(sbyte))
            return ArgumentClass.Integer1;

        if (effective == typeof(short) || effective == typeof(ushort) || effective == typeof(char))
            return ArgumentClass.Integer2;

        if (effective == typeof(int) || effective == typeof(uint))
            return ArgumentClass.Integer4;

        // Floating point travels in a different register file entirely.
        if (effective == typeof(float))
            return ArgumentClass.Float4;

        if (effective == typeof(double))
            return ArgumentClass.Float8;

        return ArgumentClass.Aggregate;
    }

    private static Dictionary<nint, HookIdentity> GetIndex()
    {
        if (index != null)
            return index;

        lock (BuildLock)
        {
            index ??= Build();
            return index;
        }
    }

    private static Dictionary<nint, HookIdentity> Build()
    {
        var fromResolver = BuildFromResolver(out var withDelegate);

        if (fromResolver.Count > 0 && withDelegate > 0)
        {
            NoireLogger.LogDebug(
                $"Indexed {fromResolver.Count} XIVClientStructs functions from the interop resolver, {withDelegate} of them with a delegate to check against.",
                HookLog.Prefix);

            return fromResolver;
        }

        if (fromResolver.Count > 0)
        {
            // Names came back but none mapped to a declaring type, so nothing could be compared.
            NoireLogger.LogWarning(
                $"The interop resolver listed {fromResolver.Count} addresses but none resolved to a declaring type, so the index is being rebuilt by reflection. Report this: the resolver's name format has changed.",
                HookLog.Prefix);
        }

        return BuildByReflection();
    }

    /// <summary>
    /// Reads the interop generator's own list of every address it resolved, in a single collection walk.
    /// </summary>
    /// <param name="withDelegate">How many entries resolved to a delegate that a hook can be checked against.</param>
    /// <returns>The index, or an empty map when the resolver is unavailable or has not run.</returns>
    private static Dictionary<nint, HookIdentity> BuildFromResolver(out int withDelegate)
    {
        var map = new Dictionary<nint, HookIdentity>();
        withDelegate = 0;

        try
        {
            var resolverType = Type.GetType("InteropGenerator.Runtime.Resolver, InteropGenerator.Runtime");
            var instance = resolverType?.GetProperty("GetInstance", StaticFields)?.GetValue(null);
            var addresses = instance == null
                ? null
                : resolverType!.GetField("Addresses", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance);

            if (addresses is not System.Collections.IEnumerable entries)
                return map;

            FieldInfo? nameField = null;
            FieldInfo? valueField = null;

            foreach (var entry in entries)
            {
                if (entry == null)
                    continue;

                var entryType = entry.GetType();
                nameField ??= entryType.GetField("Name");
                valueField ??= entryType.GetField("Value");

                if (nameField == null || valueField == null)
                    return map;

                var value = valueField.GetValue(entry);
                var address = value == null ? 0 : HookAddressResolver.ExtractPointer(value);

                if (address == 0 || nameField.GetValue(entry) is not string name || name.Length == 0)
                    continue;

                var identity = CreateIdentityFromName(name, address);

                if (map.TryAdd(address, identity) && identity.ExpectedDelegateType != null)
                    withDelegate++;
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogDebug($"Could not read the interop resolver's address list: {ex.Message}", HookLog.Prefix);
            withDelegate = 0;
            return [];
        }

        return map;
    }

    /// <summary>
    /// Turns a resolver entry name into an identity, finding the delegate the function declares when the name can
    /// be resolved back to a type.
    /// </summary>
    /// <param name="name">The name the resolver recorded.</param>
    /// <param name="address">The resolved address.</param>
    /// <returns>The identity.</returns>
    private static HookIdentity CreateIdentityFromName(string name, nint address)
    {
        var separator = name.LastIndexOf('.');
        var ownerType = separator > 0 ? FindOwnerTypeByName(name[..separator]) : null;
        var functionName = separator > 0 ? name[(separator + 1)..] : name;

        var delegateType = ownerType?.GetNestedType("Delegates", NestedTypes)?.GetNestedType(functionName, NestedTypes);

        return new HookIdentity(
            ownerType == null ? name : FormatName(ownerType, functionName),
            ownerType,
            functionName,
            delegateType,
            address)
        {
            ModuleRelativeAddress = HookSignatureFormatter.FormatAddress(address),
        };
    }

    /// <summary>
    /// Resolves the type part of a resolver entry name, which may or may not carry the XIVClientStructs namespace
    /// and may separate namespaces with <c>::</c>.
    /// </summary>
    /// <param name="typeName">The type part of the name.</param>
    /// <returns>The type, or null when nothing matches.</returns>
    private static Type? FindOwnerTypeByName(string typeName)
    {
        var assembly = typeof(FFXIVClientStructs.FFXIV.Client.Game.GameMain).Assembly;
        var dotted = typeName.Replace("::", ".");

        return assembly.GetType(dotted)
            ?? assembly.GetType(ClientStructsPrefix + dotted)
            ?? assembly.GetType("FFXIVClientStructs." + dotted);
    }

    private static Dictionary<nint, HookIdentity> BuildByReflection()
    {
        var map = new Dictionary<nint, HookIdentity>();

        Type[] types;
        try
        {
            types = typeof(FFXIVClientStructs.FFXIV.Client.Game.GameMain).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = Array.FindAll(ex.Types, type => type != null)!;
        }
        catch (Exception ex)
        {
            NoireLogger.LogWarning($"Could not read XIVClientStructs to index its functions, so hook delegates cannot be checked: {ex.Message}", HookLog.Prefix);
            return map;
        }

        foreach (var type in types)
        {
            if (type.IsGenericTypeDefinition)
                continue;

            var addressesType = type.GetNestedType("Addresses", NestedTypes);
            if (addressesType == null)
                continue;

            var delegatesType = type.GetNestedType("Delegates", NestedTypes);

            foreach (var field in addressesType.GetFields(StaticFields))
            {
                try
                {
                    var value = field.GetValue(null);
                    if (value == null)
                        continue;

                    var address = HookAddressResolver.ExtractPointer(value);
                    if (address == 0)
                        continue;

                    var delegateType = delegatesType?.GetNestedType(field.Name, NestedTypes);
                    map.TryAdd(address, CreateIdentity(type, field.Name, delegateType, address));
                }
                catch
                {
                    // One unreadable entry must not cost the whole index.
                }
            }
        }

        NoireLogger.LogDebug($"Indexed {map.Count} XIVClientStructs functions by reflection.", HookLog.Prefix);
        return map;
    }

    private static HookIdentity CreateIdentity(Type ownerType, string functionName, Type? delegateType, nint address)
        => new(FormatName(ownerType, functionName), ownerType, functionName, delegateType, address)
        {
            ModuleRelativeAddress = HookSignatureFormatter.FormatAddress(address),
        };

    private static string FormatName(Type ownerType, string functionName)
    {
        var qualified = ownerType.FullName ?? ownerType.Name;

        if (qualified.StartsWith(ClientStructsPrefix, StringComparison.Ordinal))
            qualified = qualified[ClientStructsPrefix.Length..];

        return $"{qualified.Replace('.', ':').Replace(":", "::").Replace("+", "::")}.{functionName}";
    }

    private static bool IsInsideGameModule(nint address)
    {
        if (!mainModuleRangeRead)
        {
            mainModuleRangeRead = true;
            HookSignatureFormatter.TryGetMainModuleRange(out mainModuleBase, out mainModuleSize);
        }

        if (mainModuleBase == 0)
            return true;

        return address >= mainModuleBase && address < mainModuleBase + mainModuleSize;
    }
}
