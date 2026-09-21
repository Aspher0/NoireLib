using Dalamud.Plugin;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace NoireLib.Helpers;

// Every name Dalamud does not publish sits here. None of it is API. Each lookup is cached, guarded, and yields null.
internal static class DalamudInternals
{
    internal const string LoggerPrefix = "DalamudInternals";

    internal const string ServiceType = "Dalamud.Service`1";
    internal const string PluginManagerType = "Dalamud.Plugin.Internal.PluginManager";
    internal const string DalamudInterfaceType = "Dalamud.Interface.Internal.DalamudInterface";
    internal const string ConfigurationType = "Dalamud.Configuration.Internal.DalamudConfiguration";
    internal const string ThirdPartyRepoType = "Dalamud.Configuration.ThirdPartyRepoSettings";

    // The sink Dalamud's own log window reads: a static Instance raising LogLine with each line and its Serilog event.
    internal const string LogSinkType = "Dalamud.Logging.Internal.SerilogEventSink";

    // The field holding the IDalamudPlugin a LocalPlugin constructed.
    internal const string PluginInstanceField = "instance";

    internal const BindingFlags Everything = BindingFlags.Public | BindingFlags.NonPublic
        | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;

    private static readonly Dictionary<string, Type?> Types = [];
    private static readonly object Gate = new();

    private static Assembly? dalamud;

    internal static Assembly Assembly => dalamud ??= typeof(IDalamudPlugin).Assembly;

    internal static Type? Resolve(string fullName)
    {
        lock (Gate)
        {
            if (Types.TryGetValue(fullName, out var cached))
                return cached;

            var type = SafeExecutor.ExecuteSafely(() => Assembly.GetType(fullName, throwOnError: false), null);
            Types[fullName] = type;

            if (type == null)
                NoireLogger.LogWarning($"Dalamud no longer carries the internal type '{fullName}'.", LoggerPrefix);

            return type;
        }
    }

    internal static object? Service(string serviceTypeName)
    {
        var serviceType = Resolve(serviceTypeName);
        var locator = Resolve(ServiceType);
        if (serviceType == null || locator == null)
            return null;

        return SafeExecutor.ExecuteSafely<object?>(
            () => locator.MakeGenericType(serviceType)
                .GetMethod("Get", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, null),
            null);
    }

    internal static object? PluginManager() => Service(PluginManagerType);

    internal static object? DalamudInterface() => Service(DalamudInterfaceType);

    internal static object? Configuration() => Service(ConfigurationType);

    // A Type passed as the target reads a static member.
    internal static object? Read(object? target, string name)
    {
        if (target == null)
            return null;

        var type = target as Type ?? target.GetType();
        var instance = target is Type ? null : target;

        return SafeExecutor.ExecuteSafely<object?>(
            () =>
            {
                var property = type.GetProperty(name, Everything);
                if (property != null && property.CanRead)
                    return property.GetValue(instance);

                return type.GetField(name, Everything)?.GetValue(instance);
            },
            null);
    }

    internal static IEnumerable<object> ReadSequence(object? target, string name)
    {
        if (Read(target, name) is not IEnumerable sequence || sequence is string)
            yield break;

        foreach (var item in sequence)
        {
            if (item != null)
                yield return item;
        }
    }
}
