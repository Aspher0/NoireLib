using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.UI;

internal static class UiCodeWarmup
{
    private const int MaxWorkers = 4;

    private static int started;

    internal static bool Finished { get; private set; }

    internal static Task Start(IReadOnlyList<Type>? alsoWarm)
    {
        if (Interlocked.Exchange(ref started, 1) != 0)
            return Task.CompletedTask;

        // The array is the caller's and may be reused after this returns.
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
            var types = new List<Type>(DrawingTypes(extra));
            var workers = Math.Clamp(Environment.ProcessorCount / 2, 1, MaxWorkers);
            var next = -1;

            void Compile()
            {
                int index;

                while ((index = Interlocked.Increment(ref next)) < types.Count)
                    Interlocked.Add(ref prepared, Prepare(types[index]));
            }

            if (workers == 1)
            {
                Compile();
            }
            else
            {
                var running = new Task[workers];

                for (var worker = 0; worker < workers; worker++)
                    running[worker] = Task.Run(Compile);

                Task.WaitAll(running);
            }

            Finished = true;

            NoireLogger.LogInformation(
                $"Compiled {prepared} drawing method(s) in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:0} ms "
                + $"over {workers} thread(s). "
                + "This is time a window's first frame would otherwise have spent jitting its own draw path.",
                "[NoireUI] ");
        }
        catch (Exception ex)
        {
            // Background thread. An escaping exception has nothing to catch it.
            NoireLogger.LogError(ex, "Could not finish compiling the drawing methods.", "[NoireUI] ");
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
                // A method that will not compile early still compiles when called.
            }
        }

        return prepared;
    }
}
