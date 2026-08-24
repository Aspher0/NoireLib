using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.UI;

/// <summary>
/// Compiles the drawing methods ahead of the frame that would otherwise compile them.<br/>
/// Runs on a background thread.
/// </summary>
internal static class UiCodeWarmup
{
    private static int started;

    /// <summary>Whether a warmup has run to completion.</summary>
    internal static bool Finished { get; private set; }

    /// <summary>
    /// Starts the warmup, or hands back the one already running.
    /// </summary>
    /// <param name="alsoWarm">Consumer types to compile as well as NoireUI's own.</param>
    /// <returns>The work.</returns>
    internal static Task Start(IReadOnlyList<Type>? alsoWarm)
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
            return Task.CompletedTask;

        // Copied before leaving the calling thread: the array is the caller's and nothing promises it will not be
        // reused after this returns.
        var extra = Copy(alsoWarm);

        return Task.Run(() => Run(extra));
    }

    /// <summary>Takes an independent copy of the caller's list, or an empty one.</summary>
    private static Type[] Copy(IReadOnlyList<Type>? types)
    {
        if (types == null || types.Count == 0)
            return [];

        var copy = new Type[types.Count];

        for (var index = 0; index < types.Count; index++)
            copy[index] = types[index];

        return copy;
    }

    /// <summary>
    /// Compiles every method of NoireUI's own drawing surfaces and of whatever the caller added.
    /// </summary>
    private static void Run(Type[] extra)
    {
        var started = Stopwatch.GetTimestamp();
        var prepared = 0;

        try
        {
            foreach (var type in DrawingTypes(extra))
                prepared += Prepare(type);

            Finished = true;

            NoireLogger.LogInformation(
                $"Compiled {prepared} drawing method(s) in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms. "
                + "This is time a window's first frame would otherwise have spent jitting its own draw path.",
                nameof(NoireUI));
        }
        catch (Exception ex)
        {
            // Reported rather than thrown: a warmup that fails costs a slow first frame, which is what the plugin had
            // before it asked for one, and is never worth taking the plugin down for. This runs on a background
            // thread, where an escaping exception has nothing to catch it.
            NoireLogger.LogError(ex, "Could not finish compiling the drawing methods.", nameof(NoireUI));
        }
    }

    /// <summary>
    /// The types whose methods are worth compiling: everything NoireUI draws with, plus the caller's own.
    /// </summary>
    private static IEnumerable<Type> DrawingTypes(Type[] extra)
    {
        foreach (var type in extra)
        {
            if (type != null)
                yield return type;
        }

        foreach (var type in typeof(UiCodeWarmup).Assembly.GetTypes())
        {
            if (type.Namespace == UiNamespace)
                yield return type;
        }
    }

    /// <summary>The namespace every NoireUI drawing surface lives in.</summary>
    private const string UiNamespace = "NoireLib.UI";

    /// <summary>
    /// Compiles every method of one type that can be compiled ahead of time.
    /// </summary>
    /// <param name="type">The type to compile.</param>
    /// <returns>How many methods were compiled.</returns>
    private static int Prepare(Type type)
    {
        if (type.IsGenericTypeDefinition || type.ContainsGenericParameters)
            return 0;

        var prepared = 0;

        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;

        MethodInfo[] methods;

        try
        {
            methods = type.GetMethods(flags);
        }
        catch (Exception)
        {
            return 0;
        }

        foreach (var method in methods)
        {
            if (method.IsAbstract || method.ContainsGenericParameters || method.MethodHandle == default)
                continue;

            try
            {
                RuntimeHelpers.PrepareMethod(method.MethodHandle);
                prepared++;
            }
            catch (Exception)
            {
                // Skipped deliberately, see the remarks.
            }
        }

        return prepared;
    }
}
