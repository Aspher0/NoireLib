using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace NoireLib.Remote.Internal;

// One map per interval for aligned series. Nothing is sampled while nobody watches.
internal sealed class RemoteMetricsSampler : IDisposable
{
    private readonly HttpRouterContext context;
    private readonly CancellationTokenSource lifetime = new();
    private readonly List<NoireRemoteMetricHandle> builtIn = [];
    private int disposed;

    public RemoteMetricsSampler(HttpRouterContext context)
    {
        this.context = context;

        RegisterBuiltIn();
        _ = RunAsync();
    }

    // The built-ins read this listener's call history. In the shared registry two listeners would replace each other's.
    private void RegisterBuiltIn()
    {
        var process = Process.GetCurrentProcess();

        builtIn.Add(NoireRemoteMetrics.Gauge("heapMb", static () => GC.GetTotalMemory(false) / 1048576.0));
        builtIn.Add(NoireRemoteMetrics.Gauge("gen0", static () => GC.CollectionCount(0)));
        builtIn.Add(NoireRemoteMetrics.Gauge("gen1", static () => GC.CollectionCount(1)));
        builtIn.Add(NoireRemoteMetrics.Gauge("gen2", static () => GC.CollectionCount(2)));

        builtIn.Add(NoireRemoteMetrics.Gauge("workingSetMb", () =>
        {
            process.Refresh();
            return process.WorkingSet64 / 1048576.0;
        }));

        builtIn.Add(NoireRemoteMetrics.Gauge("httpCallsPerMinute", () => context.Calls.CallsPerMinute()));
        builtIn.Add(NoireRemoteMetrics.Gauge("httpP95Ms", () => context.Calls.P95Milliseconds()));
        builtIn.Add(NoireRemoteMetrics.Gauge("httpFailuresPerMinute", () => context.Calls.FailuresPerMinute()));
    }

    private async Task RunAsync()
    {
        var budget = TimeSpan.FromMilliseconds(2);

        while (!lifetime.IsCancellationRequested)
        {
            var interval = context.Options.MetricsInterval <= TimeSpan.Zero
                ? TimeSpan.FromSeconds(1)
                : context.Options.MetricsInterval;

            try
            {
                await Task.Delay(interval, lifetime.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (!context.Events.IsWatched(NoireRemoteChannels.Metrics))
                continue;

            NoireRemoteMetrics.Sampled();

            try
            {
                var sample = await ReadAsync(budget).ConfigureAwait(false);

                if (sample.Count > 0)
                    context.Events.Publish(NoireRemoteChannels.Metrics, NoireRemotePaths.MetricsTopic, sample);
            }
            catch (Exception exception)
            {
                context.Host.Log(NoireRemoteLogLevel.Error, "[NoireRemote] a metrics sample failed", exception);
            }
        }
    }

    private async Task<Dictionary<string, double>> ReadAsync(TimeSpan budget)
    {
        var gauges = new List<NoireRemoteMetricHandle>(builtIn);
        gauges.AddRange(NoireRemoteMetrics.Snapshot());

        var sample = new Dictionary<string, double>(gauges.Count);
        List<NoireRemoteMetricHandle>? hosted = null;

        foreach (var gauge in gauges)
        {
            if (gauge.IsDropped)
                continue;

            if (gauge.Thread == NoireRemoteThread.Framework && context.Host.HasHostThread)
            {
                (hosted ??= []).Add(gauge);
                continue;
            }

            if (gauge.Sample(budget, out var value))
                sample[gauge.Name] = value;

            Report(gauge);
        }

        if (hosted == null)
            return sample;

        // One framework-thread hop for every host gauge.
        var readOnHost = await context.Host.RunOnHostThreadAsync(() =>
        {
            var values = new Dictionary<string, double>(hosted.Count);

            foreach (var gauge in hosted)
            {
                if (gauge.Sample(budget, out var value))
                    values[gauge.Name] = value;
            }

            return Task.FromResult(values);
        }).ConfigureAwait(false);

        foreach (var pair in readOnHost)
            sample[pair.Key] = pair.Value;

        foreach (var gauge in hosted)
            Report(gauge);

        return sample;
    }

    private void Report(NoireRemoteMetricHandle gauge)
    {
        if (!gauge.IsDropped || reported.Contains(gauge.Name))
            return;

        reported.Add(gauge.Name);
        context.Host.Log(NoireRemoteLogLevel.Warning, "[NoireRemote] the gauge '" + gauge.Name + "' is no longer sampled, it overran its budget", null);
    }

    private readonly HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;

        try
        {
            lifetime.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        lifetime.Dispose();
        builtIn.Clear();
    }
}
