using Castle.DynamicProxy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NoireLib.Configuration;

/// <summary>
/// A Castle DynamicProxy interceptor that automatically saves configuration
/// </summary>
internal class NoireConfigAutoSaveInterceptor : IInterceptor
{
    private readonly HashSet<string> autoSavePropertySetters;
    private readonly HashSet<string> autoSaveMethods;

    public NoireConfigAutoSaveInterceptor(Type targetType)
    {
        autoSavePropertySetters = targetType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<AutoSaveAttribute>() != null)
            .Where(p => p.SetMethod != null && p.SetMethod.IsVirtual) // Only virtual properties can be intercepted
            .Select(p => $"set_{p.Name}")
            .ToHashSet();

        autoSaveMethods = targetType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<AutoSaveAttribute>() != null
                     && !m.IsSpecialName  // Excludes property getters/setters
                     && m.IsVirtual)      // Only virtual methods can be intercepted
            .Select(m => m.Name)
            .ToHashSet();

        ValidateVirtualMembers(targetType);
    }

    /// <summary>
    /// Intercepts method/property calls to trigger auto-save if marked with [AutoSave].
    /// </summary>
    /// <param name="invocation"></param>
    public void Intercept(IInvocation invocation)
    {
        // Execute the original method/property
        invocation.Proceed();

        var methodName = invocation.Method.Name;

        if ((autoSavePropertySetters.Contains(methodName) || autoSaveMethods.Contains(methodName))
            && invocation.InvocationTarget is NoireConfigBase config)
            config.Save();
    }

    /// <summary>
    /// Will log warnings for any members marked with [AutoSave] that are not virtual.
    /// </summary>
    /// <param name="targetType"></param>
    private static void ValidateVirtualMembers(Type targetType)
    {
        // Check for non-virtual properties with [AutoSave]
        var nonVirtualProperties = targetType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<AutoSaveAttribute>() != null)
            .Where(p => p.SetMethod != null && !p.SetMethod.IsVirtual);

        foreach (var prop in nonVirtualProperties)
        {
            NoireLogger.LogWarning($"[AutoSave] on non-virtual property '{targetType.Name}.{prop.Name}' will be ignored. " +
                $"Make the property virtual to enable auto-save.");
        }

        // Check for non-virtual methods with [AutoSave]
        var nonVirtualMethods = targetType
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<AutoSaveAttribute>() != null)
            .Where(m => !m.IsSpecialName && !m.IsVirtual);

        foreach (var method in nonVirtualMethods)
        {
            NoireLogger.LogWarning($"[AutoSave] on non-virtual method '{targetType.Name}.{method.Name}' will be ignored. " +
                $"Make the method virtual to enable auto-save.");
        }
    }
}
