using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace NoireLib.Remote;

/// <summary>
/// The gauges sampled onto the metrics channel. A host registers a number worth watching and the console draws it as
/// a chart.
/// </summary>
public static class NoireRemoteMetrics
{
    private static readonly object Gate = new();
    private static readonly List<NoireRemoteMetricHandle> Gauges = [];

    /// <summary>
    /// Registers a gauge read on the thread that samples.
    /// </summary>
    /// <param name="name">The series name a chart reads. Registering one name twice replaces the first.</param>
    /// <param name="read">What is sampled. It runs every interval while something watches the channel.</param>
    /// <returns>A handle that removes the gauge when disposed.</returns>
    /// <exception cref="ArgumentException">If the name is null or blank.</exception>
    /// <exception cref="ArgumentNullException">If the reader is null.</exception>
    public static NoireRemoteMetricHandle Register(string name, Func<double> read)
        => Register(name, read, NoireRemoteThread.Background);

    /// <summary>
    /// Registers a gauge read on a thread of your choosing.
    /// </summary>
    /// <param name="name">The series name a chart reads. Registering one name twice replaces the first.</param>
    /// <param name="read">What is sampled. It runs every interval while something watches the channel.</param>
    /// <param name="thread">The thread the reader runs on. Framework asks the host for its own thread.</param>
    /// <returns>A handle that removes the gauge when disposed.</returns>
    /// <exception cref="ArgumentException">If the name is null or blank.</exception>
    /// <exception cref="ArgumentNullException">If the reader is null.</exception>
    public static NoireRemoteMetricHandle Register(string name, Func<double> read, NoireRemoteThread thread)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A gauge needs a name.", nameof(name));

        if (read == null)
            throw new ArgumentNullException(nameof(read));

        var handle = new NoireRemoteMetricHandle(name, read, thread);

        lock (Gate)
        {
            Gauges.RemoveAll(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));
            Gauges.Add(handle);
        }

        return handle;
    }

    /// <summary>
    /// Lists the gauge names registered, in registration order.
    /// </summary>
    /// <returns>The names.</returns>
    public static IReadOnlyList<string> Names()
    {
        lock (Gate)
        {
            var names = new List<string>(Gauges.Count);

            foreach (var gauge in Gauges)
                names.Add(gauge.Name);

            return names;
        }
    }

    /// <summary>Gets whether a listener sampled in the last five seconds. It samples while something watches its metrics channel.</summary>
    public static bool IsSampling => DateTime.UtcNow - LastSampleUtc < TimeSpan.FromSeconds(5);

    private static DateTime LastSampleUtc;

    internal static void Sampled()
        => LastSampleUtc = DateTime.UtcNow;

    // Private to one listener. Never listed by Names, never replaced by another listener's gauge of the same name.
    internal static NoireRemoteMetricHandle Gauge(string name, Func<double> read)
        => new(name, read, NoireRemoteThread.Background);

    internal static IReadOnlyList<NoireRemoteMetricHandle> Snapshot()
    {
        lock (Gate)
            return [.. Gauges];
    }

    internal static void Remove(NoireRemoteMetricHandle handle)
    {
        lock (Gate)
            Gauges.Remove(handle);
    }
}

/// <summary>
/// One registered gauge. Disposing it stops it being sampled.
/// </summary>
public sealed class NoireRemoteMetricHandle : IDisposable
{
    private readonly Stopwatch clock = new();
    private int overruns;

    internal NoireRemoteMetricHandle(string name, Func<double> read, NoireRemoteThread thread)
    {
        Name = name;
        Read = read;
        Thread = thread;
    }

    /// <summary>
    /// Gets the series name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the thread the reader runs on.
    /// </summary>
    public NoireRemoteThread Thread { get; }

    /// <summary>
    /// Gets whether the gauge has been dropped for overrunning its budget twice.
    /// </summary>
    public bool IsDropped { get; private set; }

    internal Func<double> Read { get; }

    // A slow gauge is eventually dropped. A framework gauge is sampled on the host thread.
    internal bool Sample(TimeSpan budget, out double value)
    {
        value = 0;

        if (IsDropped)
            return false;

        clock.Restart();

        try
        {
            value = Read();
        }
        catch (Exception)
        {
            IsDropped = true;
            return false;
        }
        finally
        {
            clock.Stop();
        }

        if (clock.Elapsed <= budget)
            return true;

        overruns++;
        IsDropped = overruns >= 2;

        return true;
    }

    /// <summary>
    /// Stops the gauge being sampled.
    /// </summary>
    public void Dispose()
        => NoireRemoteMetrics.Remove(this);
}
