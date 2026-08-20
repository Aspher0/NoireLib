using System;
using System.Diagnostics;
using System.Text;

namespace NoireLib.Hooking;

/// <summary>
/// Renders delegates and addresses the way Dalamud's hook verification reports them.
/// </summary>
internal static class HookSignatureFormatter
{
    private static readonly object ModuleLock = new();
    private static ProcessModule[]? modules;

    /// <summary>
    /// Renders a delegate as <c>Boolean (Int32, Vector3*, Int32)</c>.
    /// </summary>
    /// <param name="delegateType">The delegate type.</param>
    /// <returns>The rendered signature.</returns>
    public static string Format(Type? delegateType)
    {
        var invoke = delegateType?.GetMethod("Invoke");
        if (invoke == null)
            return delegateType?.Name ?? "unknown";

        var parameters = invoke.GetParameters();
        var builder = new StringBuilder(invoke.ReturnType.Name).Append(" (");

        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append(parameters[i].ParameterType.Name);
        }

        return builder.Append(')').ToString();
    }

    /// <summary>
    /// Renders an address as <c>ffxiv_dx11.exe+0xB30F70</c>, falling back to the absolute value when no module contains it.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <returns>The rendered address.</returns>
    public static string FormatAddress(nint address)
    {
        if (address == 0)
            return "unresolved";

        var module = FindModule(address);
        return module == null
            ? $"0x{address:X}"
            : $"{module.ModuleName}+0x{address - module.BaseAddress:X}";
    }

    /// <summary>
    /// Gets the address range of the process main module.
    /// </summary>
    /// <param name="baseAddress">The main module base address.</param>
    /// <param name="size">The main module size in bytes.</param>
    /// <returns>True if the main module could be read.</returns>
    public static bool TryGetMainModuleRange(out nint baseAddress, out int size)
    {
        try
        {
            var main = Process.GetCurrentProcess().MainModule;
            if (main != null)
            {
                baseAddress = main.BaseAddress;
                size = main.ModuleMemorySize;
                return true;
            }
        }
        catch (Exception ex)
        {
            NoireLogger.LogDebug($"Could not read the process main module: {ex.Message}", HookLog.Prefix);
        }

        baseAddress = 0;
        size = 0;
        return false;
    }

    private static ProcessModule? FindModule(nint address)
    {
        var snapshot = GetModules();
        if (snapshot == null)
            return null;

        foreach (var module in snapshot)
        {
            var start = module.BaseAddress;
            if (address >= start && address < start + module.ModuleMemorySize)
                return module;
        }

        return null;
    }

    private static ProcessModule[]? GetModules()
    {
        lock (ModuleLock)
        {
            if (modules != null)
                return modules;

            try
            {
                var collection = Process.GetCurrentProcess().Modules;
                var snapshot = new ProcessModule[collection.Count];
                collection.CopyTo(snapshot, 0);
                modules = snapshot;
            }
            catch (Exception ex)
            {
                NoireLogger.LogDebug($"Could not enumerate process modules: {ex.Message}", HookLog.Prefix);
                modules = [];
            }

            return modules;
        }
    }
}
