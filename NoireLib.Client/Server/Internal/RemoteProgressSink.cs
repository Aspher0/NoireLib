using System;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace NoireLib.Remote.Internal;

// Publishes onto the progress channel, coalesced. A fraction also moves the job's progress field.
internal sealed class RemoteProgressSink<T> : IProgress<T>, IRemoteProgressSink
{
    private static readonly bool IsFraction =
        typeof(T) == typeof(double) || typeof(T) == typeof(float) || typeof(T) == typeof(decimal);

    private readonly HttpProgressContext context;
    private readonly Stopwatch since = Stopwatch.StartNew();
    private readonly object gate = new();

    private T? held;
    private bool holding;
    private long lastPublishedMs = -1;

    public RemoteProgressSink(HttpProgressContext context)
    {
        this.context = context;
    }

    public void Report(T value)
    {
        lock (gate)
        {
            var now = since.ElapsedMilliseconds;

            if (lastPublishedMs >= 0 && now - lastPublishedMs < RemoteProgressSink.Interval.TotalMilliseconds)
            {
                held = value;
                holding = true;
                return;
            }

            lastPublishedMs = now;
            holding = false;
        }

        Publish(value);
    }

    // The last held value is not lost to the interval.
    public void Flush()
    {
        T? value;

        lock (gate)
        {
            if (!holding)
                return;

            value = held;
            holding = false;
        }

        if (value != null || !typeof(T).IsValueType)
            Publish(value!);
    }

    private void Publish(T value)
    {
        double? fraction = IsFraction ? Math.Clamp(Convert.ToDouble(value, System.Globalization.CultureInfo.InvariantCulture), 0, 1) : null;

        if (fraction != null)
            context.SetJobProgress?.Invoke(fraction.Value);

        context.Publish(
            NoireRemotePaths.ProgressTopic(context.JobId ?? context.Id),
            new NoireRemoteProgressReport
            {
                Route = context.Route,
                Id = context.Id,
                Job = context.JobId,
                Value = fraction,
                // A fraction already in value would be read twice by a chart.
                Payload = fraction != null ? null : NoireRemoteJson.ToToken(value!),
            });
    }
}

internal interface IRemoteProgressSink
{
    void Flush();
}

internal static class RemoteProgressSink
{
    private static readonly ConcurrentDictionary<Type, Func<HttpProgressContext, object>> Factories = new();

    public static Type? PayloadTypeOf(Type parameterType)
        => parameterType.IsGenericType && parameterType.GetGenericTypeDefinition() == typeof(IProgress<>)
            ? parameterType.GetGenericArguments()[0]
            : null;

    public static Func<HttpProgressContext, object> FactoryFor(Type payloadType)
        => Factories.GetOrAdd(payloadType, static type =>
        {
            var sink = typeof(RemoteProgressSink<>).MakeGenericType(type);
            var constructor = sink.GetConstructor([typeof(HttpProgressContext)])!;

            return context => constructor.Invoke([context]);
        });

    public static TimeSpan Interval { get; set; } = TimeSpan.FromMilliseconds(250);
}
