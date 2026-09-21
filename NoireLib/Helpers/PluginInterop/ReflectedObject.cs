using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace NoireLib.Helpers;

/// <summary>
/// A handle on one object, reached by reflection: reads and writes its fields and properties and calls its methods,
/// whatever their visibility. Members are resolved once per type and name and then cached.
/// <br/>
/// Nothing here throws for a member that does not exist. A read yields the default, a write reports false, and a
/// call yields null. A plugin that renamed something degrades. It never takes the caller down with it.
/// </summary>
public sealed class ReflectedObject
{
    private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly ConcurrentDictionary<(Type Type, string Name), MemberInfo?> MemberCache = new();

    private readonly object? instance;

    /// <summary>Wraps an object.</summary>
    /// <param name="target">The object to reflect over.</param>
    /// <exception cref="ArgumentNullException">If <paramref name="target"/> is null.</exception>
    public ReflectedObject(object target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target is Type staticType)
        {
            Type = staticType;
            instance = null;
        }
        else
        {
            Type = target.GetType();
            instance = target;
        }
    }

    /// <summary>The object's own type.</summary>
    public Type Type { get; }

    /// <summary>The object itself, or null when this handle reflects over a type's static members.</summary>
    public object? Target => instance;

    /// <summary>Whether this handle reaches static members, never an object's own.</summary>
    public bool IsStatic => instance == null;

    /// <summary>Whether the type carries a field, property, or method of that name.</summary>
    /// <param name="name">The member's name.</param>
    /// <returns>True when it does.</returns>
    public bool Has(string name) => Member(name) != null || FindMethods(name).Count > 0;

    /// <summary>Resolves a field or property by name, cached.</summary>
    /// <param name="name">The member's name.</param>
    /// <returns>The member, or null when the type carries none of that name.</returns>
    public MemberInfo? Member(string name)
    {
        if (string.IsNullOrEmpty(name))
            return null;

        return MemberCache.GetOrAdd((Type, name), static key =>
        {
            // A private member is only returned by its declaring type. FlattenHierarchy flattens statics alone.
            for (var type = key.Type; type != null; type = type.BaseType)
            {
                var property = type.GetProperty(key.Name, Declared);
                if (property != null)
                    return property;

                var field = type.GetField(key.Name, Declared);
                if (field != null)
                    return field;
            }

            return null;
        });
    }

    /// <summary>Reads a field or property.</summary>
    /// <param name="name">The member's name.</param>
    /// <returns>Its value, or null when there is no such member or the read failed.</returns>
    public object? Get(string name)
    {
        return SafeExecutor.ExecuteSafely<object?>(
            () => Member(name) switch
            {
                PropertyInfo property when property.CanRead => property.GetValue(instance),
                FieldInfo field => field.GetValue(instance),
                _ => null,
            },
            null);
    }

    /// <summary>Reads a field or property and casts it.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="name">The member's name.</param>
    /// <param name="fallback">What to return when the member is missing or holds another type.</param>
    /// <returns>The value, or <paramref name="fallback"/>.</returns>
    public T? Get<T>(string name, T? fallback = default)
        => Get(name) is T value ? value : fallback;

    /// <summary>Reads a field or property, reporting whether it was there.</summary>
    /// <typeparam name="T">The value's type.</typeparam>
    /// <param name="name">The member's name.</param>
    /// <param name="value">The value when the member was read.</param>
    /// <returns>True when the member exists and holds a <typeparamref name="T"/>.</returns>
    public bool TryGet<T>(string name, out T value)
    {
        if (Get(name) is T read)
        {
            value = read;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>Writes a field or property.</summary>
    /// <param name="name">The member's name.</param>
    /// <param name="value">The value to write.</param>
    /// <returns>True when it was written.</returns>
    public bool Set(string name, object? value)
    {
        return SafeExecutor.ExecuteSafely(
            () =>
            {
                switch (Member(name))
                {
                    case PropertyInfo property when property.CanWrite:
                        property.SetValue(instance, value);
                        return true;

                    // A property with no setter is left alone. The caller can name its backing field.
                    case FieldInfo field when !field.IsInitOnly && !field.IsLiteral:
                        field.SetValue(instance, value);
                        return true;

                    default:
                        return false;
                }
            },
            false);
    }

    /// <summary>Calls a method.</summary>
    /// <param name="name">The method's name.</param>
    /// <param name="arguments">The arguments, matched against the overloads by count and type.</param>
    /// <returns>What it returned, or null for a void method or when no overload matched.</returns>
    public object? Call(string name, params object?[] arguments)
    {
        var method = ResolveMethod(name, arguments);
        if (method == null)
            return null;

        return SafeExecutor.ExecuteSafely<object?>(() => method.Invoke(instance, arguments), null);
    }

    /// <summary>Calls a method and casts what it returned.</summary>
    /// <typeparam name="T">The return value's type.</typeparam>
    /// <param name="name">The method's name.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The value, or the default when no overload matched or it returned another type.</returns>
    public T? Call<T>(string name, params object?[] arguments)
        => Call(name, arguments) is T value ? value : default;

    /// <summary>Reads a member and wraps what came back, for walking into an object a member holds.</summary>
    /// <param name="name">The member's name.</param>
    /// <returns>A handle on the value, or null when the member is missing or holds null.</returns>
    public ReflectedObject? Into(string name)
    {
        var value = Get(name);
        return value == null ? null : new ReflectedObject(value);
    }

    /// <summary>Reads a member holding a sequence and wraps each element.</summary>
    /// <param name="name">The member's name.</param>
    /// <returns>A handle per element, or nothing when the member is missing or holds no sequence.</returns>
    public IEnumerable<ReflectedObject> Each(string name)
    {
        if (Get(name) is not IEnumerable sequence || sequence is string)
            yield break;

        foreach (var item in sequence)
        {
            if (item != null)
                yield return new ReflectedObject(item);
        }
    }

    /// <summary>
    /// The names of every field, property, and method reachable on the type and on the types it derives from, for
    /// finding out what is actually there. Constructors and property accessors are left out.
    /// </summary>
    /// <returns>The names, sorted and without duplicates.</returns>
    public IReadOnlyList<string> MemberNames()
    {
        var names = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        for (var type = Type; type != null; type = type.BaseType)
        {
            foreach (var member in type.GetMembers(Declared))
            {
                if (member is MethodInfo { IsSpecialName: true } or ConstructorInfo)
                    continue;

                if (seen.Add(member.Name))
                    names.Add(member.Name);
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    // Matched on argument types when every argument is present, on count alone otherwise.
    private MethodInfo? ResolveMethod(string name, object?[] arguments)
    {
        var candidates = FindMethods(name);
        if (candidates.Count == 0)
            return null;

        if (candidates.Count == 1)
            return candidates[0].GetParameters().Length == arguments.Length ? candidates[0] : null;

        MethodInfo? byCount = null;
        var countMatches = 0;

        foreach (var candidate in candidates)
        {
            var parameters = candidate.GetParameters();
            if (parameters.Length != arguments.Length)
                continue;

            countMatches++;
            byCount ??= candidate;

            var exact = true;
            for (var index = 0; index < parameters.Length && exact; index++)
            {
                if (arguments[index] != null)
                    exact = parameters[index].ParameterType.IsInstanceOfType(arguments[index]);
            }

            if (exact)
                return candidate;
        }

        return countMatches == 1 ? byCount : null;
    }

    // A private method of a base class is invisible from the derived type. An override is kept, the hidden method is not.
    private List<MethodInfo> FindMethods(string name)
    {
        var found = new List<MethodInfo>();
        if (string.IsNullOrEmpty(name))
            return found;

        for (var type = Type; type != null; type = type.BaseType)
        {
            foreach (var method in type.GetMethods(Declared))
            {
                if (method.IsSpecialName || !string.Equals(method.Name, name, StringComparison.Ordinal))
                    continue;

                if (!Hides(found, method))
                    found.Add(method);
            }
        }

        return found;
    }

    private static bool Hides(List<MethodInfo> found, MethodInfo candidate)
    {
        var parameters = candidate.GetParameters();

        foreach (var existing in found)
        {
            var other = existing.GetParameters();
            if (other.Length != parameters.Length)
                continue;

            var same = true;
            for (var index = 0; index < parameters.Length && same; index++)
                same = other[index].ParameterType == parameters[index].ParameterType;

            if (same)
                return true;
        }

        return false;
    }
}
