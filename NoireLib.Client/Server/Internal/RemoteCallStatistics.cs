using System;
using System.Collections.Generic;

namespace NoireLib.Remote.Internal;

// Keeps one minute of finished calls.
internal sealed class RemoteCallStatistics
{
    private const int Capacity = 4096;

    private readonly object gate = new();
    private readonly Queue<Entry> window = new();

    private readonly record struct Entry(DateTime AtUtc, double Milliseconds, bool Ok);

    public void Record(TimeSpan elapsed, bool ok)
    {
        lock (gate)
        {
            window.Enqueue(new Entry(DateTime.UtcNow, elapsed.TotalMilliseconds, ok));
            Trim();
        }
    }

    public double CallsPerMinute()
    {
        lock (gate)
        {
            Trim();
            return window.Count;
        }
    }

    public double FailuresPerMinute()
    {
        lock (gate)
        {
            Trim();

            var failed = 0;

            foreach (var entry in window)
            {
                if (!entry.Ok)
                    failed++;
            }

            return failed;
        }
    }

    public double P95Milliseconds()
    {
        List<double> durations;

        lock (gate)
        {
            Trim();

            if (window.Count == 0)
                return 0;

            durations = new List<double>(window.Count);

            foreach (var entry in window)
                durations.Add(entry.Milliseconds);
        }

        durations.Sort();

        var index = (int)Math.Ceiling(durations.Count * 0.95) - 1;

        return durations[Math.Clamp(index, 0, durations.Count - 1)];
    }

    private void Trim()
    {
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(1);

        while (window.Count > 0 && (window.Peek().AtUtc < cutoff || window.Count > Capacity))
            window.Dequeue();
    }
}
