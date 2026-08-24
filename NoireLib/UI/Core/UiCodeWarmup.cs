using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.UI;

// Compiles the drawing methods ahead of the frame that would otherwise compile them, on a background thread.
internal static class UiCodeWarmup
{
    private static int started;

    internal static bool Finished { get; private set; }

    internal static Task Start(IReadOnlyList<Type>? alsoWarm)
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
            return Task.CompletedTask;

        // Copied before leaving the calling thread: the array is the caller's and nothing promises it will not be
        // reused after this returns.
        var extra = Copy(alsoWarm);

        return Task.Run(() => Run(extra));
    }

    private static Type[] Copy(IReadOnlyList<Type>? types)
    {
        if (types == null || types.Count == 0)
            return [];

        var copy = new Type[types.Count];

        for (var index = 0; index < types.Count; index++)
            copy[index] = types[index];

        return copy;
    }

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
            // Reported rather than thrown: a failed warmup costs a slow first frame, no more. This runs on a
            // background thread, where an escaping exception has nothing to catch it.
            NoireLogger.LogError(ex, "Could not finish compiling the drawing methods.", nameof(NoireUI));
        }
    }

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

    private const string UiNamespace = "NoireLib.UI";

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
                // A method that will not compile early still compiles when it is called.
            }
        }

        return prepared;
    }
}
