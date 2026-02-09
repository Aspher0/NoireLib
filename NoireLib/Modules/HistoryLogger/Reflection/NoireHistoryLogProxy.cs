using Castle.DynamicProxy;
using System;
using System.Linq;
using System.Reflection;

namespace NoireLib.HistoryLogger;

internal static class NoireHistoryLogProxy
{
    private static readonly ProxyGenerator ProxyGenerator = new();
    private static readonly ProxyGenerationOptions ProxyOptions = new();

    public static T Create<T>(T? instance, NoireHistoryLogger logger, bool logAllMethods, string? defaultCategory) where T : class
    {
        if (instance == null)
            return null!;

        var type = typeof(T);
        if (!logAllMethods && !HasLoggableMembers(type))
            return instance;

        try
        {
            ValidateVirtualMembers(type, logAllMethods);
            var interceptor = new NoireHistoryLogInterceptor(logger, logAllMethods, defaultCategory);
            return ProxyGenerator.CreateClassProxyWithTarget(instance, ProxyOptions, interceptor);
        }
        catch (Exception ex)
        {
            NoireLogger.LogError($"Failed to create history logger proxy for {type.Name}: {ex.Message}");
            NoireLogger.LogWarning("Falling back to non-proxied instance. [NoireLog] will not log methods.");
            return instance;
        }
    }

    public static T Create<T>(NoireHistoryLogger logger, bool logAllMethods, string? defaultCategory) where T : class, new()
    {
        var instance = new T();
        return Create(instance, logger, logAllMethods, defaultCategory);
    }

    private static bool HasLoggableMembers(Type type)
    {
        if (type.GetCustomAttributes(typeof(NoireLogAttribute), true).Length > 0)
            return true;

        return type
            .GetMembers(BindingFlags.Public | BindingFlags.Instance)
            .Any(m => m.GetCustomAttributes(typeof(NoireLogAttribute), true).Length > 0);
    }

    private static void ValidateVirtualMembers(Type targetType, bool logAllMethods)
    {
        var methods = targetType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => !m.IsSpecialName)
            .Where(m => logAllMethods || m.GetCustomAttribute<NoireLogAttribute>() != null)
            .Where(m => !m.IsVirtual);

        foreach (var method in methods)
        {
            if (method.Name != "GetType")
                NoireLogger.LogWarning($"[NoireLog] on non-virtual method '{targetType.Name}.{method.Name}' will be ignored. " +
                    "Make the method virtual to enable logging.");
        }

        var properties = targetType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => logAllMethods || p.GetCustomAttribute<NoireLogAttribute>() != null)
            .SelectMany(p => new[] { p.GetMethod, p.SetMethod })
            .Where(method => method != null && !method.IsVirtual);

        foreach (var method in properties)
        {
            NoireLogger.LogWarning($"[NoireLog] on non-virtual property accessor '{targetType.Name}.{method!.Name}' will be ignored. " +
                "Make the accessor virtual to enable logging.");
        }
    }
}
