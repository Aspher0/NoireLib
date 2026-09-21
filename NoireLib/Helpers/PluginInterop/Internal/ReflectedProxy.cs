using System;
using System.Reflection;

namespace NoireLib.Helpers;

// Every call is routed to the reflected object by the member's own name.
internal class ReflectedProxy : DispatchProxy
{
    private ReflectedObject target = null!;
    private bool strict;

    internal static object Create(Type contract, ReflectedObject target, bool strict)
    {
        var proxy = Build(contract);
        var reflected = (ReflectedProxy)proxy;
        reflected.target = target;
        reflected.strict = strict;
        return proxy;
    }

    // DispatchProxy has a generic Create and, on newer runtimes, a non-generic one.
    private static object Build(Type contract)
    {
        foreach (var candidate in typeof(DispatchProxy).GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (!string.Equals(candidate.Name, nameof(DispatchProxy.Create), StringComparison.Ordinal))
                continue;

            var parameters = candidate.GetParameters();

            if (!candidate.IsGenericMethodDefinition && parameters.Length == 2)
                return candidate.Invoke(null, [contract, typeof(ReflectedProxy)])!;

            if (candidate.IsGenericMethodDefinition && parameters.Length == 0
                && candidate.GetGenericArguments().Length == 2)
                return candidate.MakeGenericMethod(contract, typeof(ReflectedProxy)).Invoke(null, null)!;
        }

        throw new MissingMethodException(nameof(DispatchProxy), nameof(DispatchProxy.Create));
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod == null)
            return null;

        // A property arrives as its accessor method.
        if (targetMethod.IsSpecialName)
        {
            if (targetMethod.Name.StartsWith("get_", StringComparison.Ordinal))
                return Read(targetMethod.Name[4..], targetMethod.ReturnType);

            if (targetMethod.Name.StartsWith("set_", StringComparison.Ordinal))
                return Write(targetMethod.Name[4..], args is { Length: > 0 } ? args[0] : null);
        }

        if (!target.Has(targetMethod.Name))
            return Missing(targetMethod.Name, targetMethod.ReturnType);

        return target.Call(targetMethod.Name, args ?? []);
    }

    private object? Read(string name, Type returnType)
    {
        if (!target.Has(name))
            return Missing(name, returnType);

        var value = target.Get(name);
        return value ?? Default(returnType);
    }

    private object? Write(string name, object? value)
    {
        if (!target.Set(name, value) && strict)
            throw new MissingMemberException(target.Type.FullName, name);

        return null;
    }

    private object? Missing(string name, Type returnType)
    {
        if (strict)
            throw new MissingMemberException(target.Type.FullName, name);

        return Default(returnType);
    }

    private static object? Default(Type type)
        => type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);
}
