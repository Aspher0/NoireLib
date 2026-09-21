using FluentAssertions;
using NoireLib.Remote;
using NoireLib.Websocket;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Net;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace NoireLib.Tests;

/// <summary>
/// Locks every options and policy type the socket system is configured with, found by sweeping the namespace. No
/// list is kept beside it. A copy carries every public property, the collections it owns are its own,
/// and the values compared are the ones that were set, never the defaults. A property added to one of these types
/// and left out of its copy fails here, and a type added to the namespace is covered the day it lands.
/// </summary>
public sealed class NoireSocketVocabularyTests(ITestOutputHelper output)
{
    private const string SweptNamespace = "NoireLib.Websocket";

    // Every public configuration type of the socket namespace.
    private static readonly IReadOnlyList<Type> Covered = Discover();

    public static TheoryData<string> CoveredTypes
    {
        get
        {
            var names = new TheoryData<string>();

            foreach (var type in Covered)
                names.Add(type.FullName!);

            return names;
        }
    }

    [Fact]
    public void TheSweep_FindsTheConfigurationTypesOfTheNamespace()
    {
        foreach (var type in Covered)
            output.WriteLine(type.FullName + " copied by " + (DeclaredClone(type) != null ? "Clone()" : "a with-expression"));

        Covered.Should().NotBeEmpty("a sweep that finds nothing passes every case below without testing anything");
        Covered.Should().HaveCountGreaterThanOrEqualTo(10,
            "four transports, the server and one endpoint each carry an options type, and both retry schedules are records");
    }

    [Theory]
    [MemberData(nameof(CoveredTypes))]
    public void Copy_CarriesEveryPublicProperty(string typeName)
    {
        var type = Resolve(typeName);

        var source = Create(type);
        var fresh = Create(type);
        Saturate(source);

        var copy = Copy(type, source);

        copy.Should().NotBeSameAs(source);

        foreach (var property in Properties(type))
        {
            var original = property.GetValue(source);
            var carried = property.GetValue(copy);

            // A setter-less property is an owned collection. The copy must own its own.
            if (property.SetMethod == null)
            {
                carried.Should().NotBeSameAs(original, property.Name + " must not be shared with the copy");
                Entries(carried).Should().Equal(Entries(original), property.Name + " is not carried by the copy");
                continue;
            }

            if (IsDeepCopied(property.PropertyType))
            {
                carried.Should().NotBeSameAs(original, property.Name + " must not be shared with the copy");
                continue;
            }

            carried.Should().Be(original, property.Name + " is not carried by the copy");
            original.Should().NotBe(property.GetValue(fresh),
                property.Name + " must not still be at its default, or carrying it proves nothing");
        }
    }

    [Theory]
    [MemberData(nameof(CoveredTypes))]
    public void Copy_SharesNoOwnedCollectionWithTheOriginal(string typeName)
    {
        var type = Resolve(typeName);

        var source = Create(type);
        Saturate(source);

        var copy = Copy(type, source);

        foreach (var property in Properties(type))
        {
            if (property.SetMethod != null)
                continue;

            var original = (ICollection)property.GetValue(source)!;
            var before = original.Count;

            Grow((ICollection)property.GetValue(copy)!);

            original.Count.Should().Be(before,
                "adding to the copy's " + property.Name + " must not reach the original");
        }
    }

    [Fact]
    public void Copy_CarriesTheNestedHttpOptionsAsItsOwn()
    {
        var checkedTypes = 0;

        foreach (var type in Covered)
        {
            var property = type.GetProperty(nameof(NoireSseOptions.Http), BindingFlags.Public | BindingFlags.Instance);

            if (property == null || property.PropertyType != typeof(NoireSocketHttpOptions))
                continue;

            checkedTypes++;

            var source = Create(type);
            Saturate(source);

            var original = (NoireSocketHttpOptions)property.GetValue(source)!;
            original.Headers["X-Noire-Marker"] = "carried";

            var carried = (NoireSocketHttpOptions)property.GetValue(Copy(type, source))!;

            carried.Should().NotBeSameAs(original, type.Name + " hands its copy the same HTTP settings object");
            carried.Headers["X-Noire-Marker"].Should().Be("carried");
            carried.ConnectionTimeout.Should().Be(original.ConnectionTimeout);

            carried.Headers["X-Noire-Added"] = "1";
            original.Headers.Should().NotContainKey("X-Noire-Added");
        }

        checkedTypes.Should().BeGreaterThanOrEqualTo(4, "every transport carries its own HTTP settings");
    }

    private static IReadOnlyList<Type> Discover()
    {
        var found = new List<Type>();

        foreach (var type in typeof(NoireSocketHttpOptions).Assembly.GetExportedTypes())
        {
            if (type.Namespace != SweptNamespace || !type.IsClass || type.IsAbstract)
                continue;

            // A type without a parameterless constructor fails instead of vanishing.
            if (DeclaredClone(type) != null)
            {
                found.Add(type);
                continue;
            }

            // A positional record represents a message.
            if (RecordClone(type) != null && type.GetConstructor(Type.EmptyTypes) != null)
                found.Add(type);
        }

        found.Sort(static (left, right) => string.CompareOrdinal(left.FullName, right.FullName));

        return found;
    }

    private static MethodInfo? DeclaredClone(Type type)
        => type.GetMethod("Clone", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);

    // The method a with-expression calls.
    private static MethodInfo? RecordClone(Type type)
        => type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);

    private static Type Resolve(string typeName)
    {
        foreach (var type in Covered)
        {
            if (type.FullName == typeName)
                return type;
        }

        throw new InvalidOperationException(typeName + " is not one of the swept types.");
    }

    private static object Create(Type type)
        => Activator.CreateInstance(type)!;

    private static object Copy(Type type, object source)
        => (DeclaredClone(type) ?? RecordClone(type)!).Invoke(source, null)!;

    private static PropertyInfo[] Properties(Type type)
        => type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

    // Every property moves off its default. A dropped one cannot match by accident.
    private static void Saturate(object target)
    {
        foreach (var property in Properties(target.GetType()))
        {
            var current = property.GetValue(target);

            if (property.SetMethod == null)
            {
                current.Should().BeAssignableTo<ICollection>(
                    property.Name + " has no setter and is not a collection; this gate cannot drive it");

                Grow((ICollection)current!);
                continue;
            }

            property.SetValue(target, Distinguishable(property.PropertyType, current));
        }
    }

    private static object Distinguishable(Type type, object? current)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;

        if (underlying == typeof(bool))
            return current is not true;

        if (underlying == typeof(int))
            return 4242;

        if (underlying == typeof(double))
            return 0.375;

        if (underlying == typeof(TimeSpan))
            return TimeSpan.FromMilliseconds(4242);

        if (underlying == typeof(string))
            return "noire-vocabulary";

        if (underlying.IsEnum)
            return OtherThan(underlying, current);

        if (underlying == typeof(NoireSocketHttpOptions))
            return new NoireSocketHttpOptions { ConnectionTimeout = TimeSpan.FromMilliseconds(4242) };

        if (underlying == typeof(NoireRetryPolicy))
            return NoireRetryPolicy.Default with { Jitter = 0.375 };

        if (underlying == typeof(NoireSocketIOReconnect))
            return NoireSocketIOReconnect.Default with { Jitter = 0.375 };

        if (underlying == typeof(NoireSocketCredential))
            return NoireSocketCredential.Bearer("loopback-token");

        if (underlying == typeof(CookieContainer))
            return new CookieContainer();

        if (underlying == typeof(X509CertificateCollection))
            return new X509CertificateCollection();

        if (underlying == typeof(IWebProxy))
            return new WebProxy("http://127.0.0.1:8080");

        if (underlying == typeof(ICredentials))
            return new NetworkCredential("user", "secret");

        if (underlying == typeof(INoireRemoteHost))
            return new NoireRemoteStandaloneHost();

        if (typeof(Delegate).IsAssignableFrom(underlying))
            return EmptyDelegate(underlying);

        if (underlying == typeof(object))
            return new object();

        throw new InvalidOperationException(
            underlying.Name + " has no value this gate knows how to distinguish from its default.");
    }

    // A hook added later needs no change here.
    private static Delegate EmptyDelegate(Type type)
    {
        var signature = type.GetMethod("Invoke")!;
        var parameters = new List<ParameterExpression>();

        foreach (var parameter in signature.GetParameters())
            parameters.Add(Expression.Parameter(parameter.ParameterType, parameter.Name));

        var body = signature.ReturnType == typeof(void)
            ? (Expression)Expression.Empty()
            : Expression.Default(signature.ReturnType);

        return Expression.Lambda(type, body, parameters).Compile();
    }

    private static object OtherThan(Type enumType, object? current)
    {
        foreach (var value in Enum.GetValues(enumType))
        {
            if (!Equals(value, current))
                return value;
        }

        throw new InvalidOperationException(enumType.Name + " carries one value. Nothing distinguishes a copy.");
    }

    private static void Grow(ICollection collection)
    {
        switch (collection)
        {
            case IDictionary<string, string> map:
                map["X-Noire-" + map.Count] = "grown";
                break;

            case IList<string> list:
                list.Add("noire.v" + list.Count);
                break;

            default:
                throw new InvalidOperationException(
                    collection.GetType().Name + " is a collection this gate cannot add an entry to.");
        }
    }

    private static List<string> Entries(object? collection)
    {
        var entries = new List<string>();

        if (collection is IDictionary map)
        {
            foreach (DictionaryEntry entry in map)
                entries.Add(entry.Key + "=" + entry.Value);

            entries.Sort(StringComparer.Ordinal);
            return entries;
        }

        foreach (var entry in (ICollection)collection!)
            entries.Add(entry?.ToString() ?? string.Empty);

        return entries;
    }

    private static bool IsDeepCopied(Type type)
        => type == typeof(NoireSocketHttpOptions);
}
