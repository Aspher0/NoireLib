using Castle.DynamicProxy;
using System;
using System.Linq;

namespace NoireLib.Configuration;

/// <summary>
/// Creates a proxy around configuration instances to automatically save when [AutoSave] properties or methods are used.
/// </summary>
internal static class NoireConfigAutoSaveProxy
{
    private static readonly ProxyGenerator ProxyGenerator = new();
    private static readonly ProxyGenerationOptions ProxyOptions = new();

    public static T Create<T>(T? instance) where T : NoireConfigBase
    {
        if (instance == null)
            return null!;

        // Return instance directly if there are no [AutoSave] members for performances
        if (!HasAutoSaveMembers(typeof(T)))
            return instance;

        try
        {
            var interceptor = new NoireConfigAutoSaveInterceptor(typeof(T));
            var proxy = ProxyGenerator.CreateClassProxy(typeof(T), interceptor);
            return (T)proxy;
        }
        catch (Exception ex)
        {
            NoireLogger.LogError($"Failed to create proxy for {typeof(T).Name}: {ex.Message}");
            NoireLogger.LogWarning($"Falling back to non-proxied instance. [AutoSave] will not work.");
            return instance;
        }
    }

    /// <summary>
    /// Checks whether the given type has any members marked with [AutoSave].
    /// </summary>
    /// <param name="type">The type to check.</param>
    /// <returns>True if any members are marked with [AutoSave]; otherwise, false.</returns>
    private static bool HasAutoSaveMembers(Type type)
    {
        return type
            .GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Any(m => m.GetCustomAttributes(typeof(AutoSaveAttribute), true).Length > 0);
    }
}
