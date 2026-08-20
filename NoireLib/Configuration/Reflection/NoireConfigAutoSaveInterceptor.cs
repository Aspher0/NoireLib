using Castle.DynamicProxy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace NoireLib.Configuration;

/// <summary>
/// A Castle DynamicProxy interceptor that requests a configuration save after any virtual member marked with
/// <see cref="AutoSaveAttribute"/> runs.
/// </summary>
internal class NoireConfigAutoSaveInterceptor : IInterceptor
{
    private readonly HashSet<string> autoSavePropertySetters;
    private readonly HashSet<string> autoSaveMethods;

    /// <summary>
    /// Collects the interceptable members marked with <see cref="AutoSaveAttribute"/> and warns about the rest.
    /// </summary>
    /// <param name="targetType">The configuration type being proxied.</param>
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
    /// Runs the intercepted call, then queues a save when the member is marked with <see cref="AutoSaveAttribute"/>.
    /// </summary>
    /// <param name="invocation">The intercepted invocation.</param>
    public void Intercept(IInvocation invocation)
    {
        invocation.Proceed();

        var methodName = invocation.Method.Name;

        // Loading a configuration assigns through these same setters with values just read from disk, so writing
        // them back is redundant. The flag is thread-scoped, so a real change on another thread still persists.
        if (NoireConfigBase.IsInternalCopying)
            return;

        // Queued rather than written here: this runs inline inside the assignment, normally on the framework
        // thread, where a synchronous write would block on the disk.
        if ((autoSavePropertySetters.Contains(methodName) || autoSaveMethods.Contains(methodName))
            && invocation.InvocationTarget is NoireConfigBase config)
            config.RequestSave();
    }

    /// <summary>
    /// Logs a warning for every member marked with <see cref="AutoSaveAttribute"/> that is not virtual and so cannot
    /// be intercepted.
    /// </summary>
    /// <param name="targetType">The configuration type being proxied.</param>
    private static void ValidateVirtualMembers(Type targetType)
    {
        var nonVirtualProperties = targetType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetCustomAttribute<AutoSaveAttribute>() != null)
            .Where(p => p.SetMethod != null && !p.SetMethod.IsVirtual);

        foreach (var prop in nonVirtualProperties)
        {
            NoireLogger.LogWarning($"[AutoSave] on non-virtual property '{targetType.Name}.{prop.Name}' will be ignored. " +
                $"Make the property virtual to enable auto-save.");
        }

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
