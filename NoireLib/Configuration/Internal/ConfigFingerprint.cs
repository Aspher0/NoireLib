using Newtonsoft.Json;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace NoireLib.Configuration;

internal static class ConfigFingerprint
{
    private const long FnvOffset = unchecked((long)0xCBF29CE484222325);
    private const long FnvPrime = unchecked((long)0x100000001B3);

    private const long NullMarker = -1;
    private const long AbsentMarker = 0;
    private const long PresentMarker = 1;
    private const long ObjectMarker = 2;

    internal delegate long Mixer<T>(T? value, long hash);

    private static readonly ConcurrentDictionary<Type, Func<object, long>> RootCache = new();
    private static readonly ConcurrentDictionary<Type, Func<object, long, long>> BoxedMixers = new();
    private static readonly ConcurrentDictionary<Type, byte> OpaqueWarned = new();

    [ThreadStatic]
    private static int objectDepth;

    private const int MaxObjectDepth = 64;

    public static Func<object, long> ForType(Type type)
        => RootCache.GetOrAdd(type, static t =>
            (Func<object, long>)Close(nameof(BuildRootInvoker), t).Invoke(null, null)!);

    private static Func<object, long> BuildRootInvoker<T>()
        => static value => Mix((T)value, FnvOffset);

    private static long MixValue(long hash, long value) => (hash ^ value) * FnvPrime;

    internal static long Mix<T>(T? value, long hash)
        => (MixerCache<T>.Mixer ??= BuildMixer<T>())(value, hash);

    private static class MixerCache<T>
    {
        public static Mixer<T>? Mixer;
    }

    private static Mixer<T> BuildMixer<T>()
    {
        var type = typeof(T);

        if (type == typeof(string))
        {
            return (Mixer<T>)(object)(Mixer<string>)(static (value, hash) =>
                value == null ? MixValue(hash, NullMarker) : MixValue(MixValue(hash, value.Length), value.GetHashCode()));
        }

        var nullableInner = Nullable.GetUnderlyingType(type);
        if (nullableInner != null)
            return (Mixer<T>)Close(nameof(BuildNullableMixer), nullableInner).Invoke(null, null)!;

        if (type.IsPrimitive || type.IsEnum || type == typeof(decimal) || type == typeof(DateTime)
            || type == typeof(DateTimeOffset) || type == typeof(TimeSpan) || type == typeof(Guid))
        {
            return static (value, hash) => MixValue(hash, value!.GetHashCode());
        }

        if (type.IsSZArray)
            return (Mixer<T>)Close(nameof(BuildArrayMixer), type.GetElementType()!).Invoke(null, null)!;

        if (type.IsGenericType)
        {
            var definition = type.GetGenericTypeDefinition();
            var arguments = type.GetGenericArguments();

            if (definition == typeof(List<>))
                return (Mixer<T>)Close(nameof(BuildListMixer), arguments).Invoke(null, null)!;

            if (definition == typeof(Dictionary<,>))
                return (Mixer<T>)Close(nameof(BuildDictionaryMixer), arguments).Invoke(null, null)!;

            if (definition == typeof(HashSet<>))
                return (Mixer<T>)Close(nameof(BuildHashSetMixer), arguments).Invoke(null, null)!;
        }

        if (!type.IsValueType && (type.IsInterface || type.IsAbstract || type == typeof(object)))
        {
            return static (value, hash) => MixBoxed(value, hash);
        }

        return BuildObjectMixer<T>();
    }

    private static MethodInfo Close(string name, params Type[] arguments)
        => typeof(ConfigFingerprint)
            .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(arguments);

    private static Mixer<TInner?> BuildNullableMixer<TInner>() where TInner : struct
        => static (value, hash) => value.HasValue
            ? Mix(value.Value, MixValue(hash, PresentMarker))
            : MixValue(hash, AbsentMarker);

    private static Mixer<TElement[]> BuildArrayMixer<TElement>()
        => static (value, hash) =>
        {
            if (value == null)
                return MixValue(hash, NullMarker);

            hash = MixValue(hash, value.Length);

            for (var i = 0; i < value.Length; i++)
                hash = Mix(value[i], hash);

            return hash;
        };

    private static Mixer<List<TElement>> BuildListMixer<TElement>()
        => static (value, hash) =>
        {
            if (value == null)
                return MixValue(hash, NullMarker);

            hash = MixValue(hash, value.Count);

            for (var i = 0; i < value.Count; i++)
                hash = Mix(value[i], hash);

            return hash;
        };

    private static Mixer<Dictionary<TKey, TValue>> BuildDictionaryMixer<TKey, TValue>() where TKey : notnull
        => static (value, hash) =>
        {
            if (value == null)
                return MixValue(hash, NullMarker);

            hash = MixValue(hash, value.Count);

            foreach (var pair in value)
            {
                hash = Mix(pair.Key, hash);
                hash = Mix(pair.Value, hash);
            }

            return hash;
        };

    private static Mixer<HashSet<TElement>> BuildHashSetMixer<TElement>()
        => static (value, hash) =>
        {
            if (value == null)
                return MixValue(hash, NullMarker);

            hash = MixValue(hash, value.Count);

            foreach (var element in value)
                hash = Mix(element, hash);

            return hash;
        };

    private static long MixBoxed(object? value, long hash)
    {
        if (value == null)
            return MixValue(hash, NullMarker);

        var runtime = value.GetType();
        var mixer = BoxedMixers.GetOrAdd(runtime, static t =>
            (Func<object, long, long>)Close(nameof(BuildBoxedInvoker), t).Invoke(null, null)!);

        return mixer(value, MixValue(hash, runtime.TypeHandle.Value.ToInt64()));
    }

    private static Func<object, long, long> BuildBoxedInvoker<T>()
        => static (value, hash) => Mix((T)value, hash);

    private static Mixer<T> BuildObjectMixer<T>()
    {
        var steps = BuildMemberSteps<T>();

        if (steps.Length == 0)
        {
            if (!typeof(T).IsValueType && OpaqueWarned.TryAdd(typeof(T), 0))
            {
                NoireLogger.LogDebug(
                    $"Configuration fingerprint treats {typeof(T).Name} as opaque; changes inside it are only detected " +
                    $"if it implements value-based GetHashCode.", "[NoireConfig] ");
            }

            return static (value, hash) => value == null ? MixValue(hash, NullMarker) : MixValue(hash, value.GetHashCode());
        }

        return (value, hash) =>
        {
            if (value == null)
                return MixValue(hash, NullMarker);

            if (objectDepth >= MaxObjectDepth)
                return MixValue(hash, ObjectMarker);

            objectDepth++;

            try
            {
                hash = MixValue(hash, ObjectMarker);

                for (var i = 0; i < steps.Length; i++)
                    hash = steps[i](value, hash);

                return hash;
            }
            finally
            {
                objectDepth--;
            }
        };
    }

    private static Func<T, long, long>[] BuildMemberSteps<T>()
    {
        var steps = new List<Func<T, long, long>>();

        foreach (var property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetMethod is not { IsPublic: true })
                continue;

            if (property.GetIndexParameters().Length != 0)
                continue;

            if (property.GetCustomAttribute<JsonIgnoreAttribute>(inherit: true) != null)
                continue;

            steps.Add((Func<T, long, long>)Close(nameof(BuildPropertyStep), typeof(T), property.PropertyType)
                .Invoke(null, [property])!);
        }

        foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (field.IsNotSerialized || field.GetCustomAttribute<JsonIgnoreAttribute>(inherit: true) != null)
                continue;

            steps.Add((Func<T, long, long>)Close(nameof(BuildFieldStep), typeof(T), field.FieldType)
                .Invoke(null, [field])!);
        }

        return [.. steps];
    }

    private static Func<T, long, long> BuildPropertyStep<T, TMember>(PropertyInfo property)
    {
        var instance = Expression.Parameter(typeof(T), "instance");
        var getter = Expression.Lambda<Func<T, TMember>>(Expression.Property(instance, property), instance).Compile();

        return (value, hash) => Mix(getter(value), hash);
    }

    private static Func<T, long, long> BuildFieldStep<T, TMember>(FieldInfo field)
    {
        var instance = Expression.Parameter(typeof(T), "instance");
        var getter = Expression.Lambda<Func<T, TMember>>(Expression.Field(instance, field), instance).Compile();

        return (value, hash) => Mix(getter(value), hash);
    }
}
