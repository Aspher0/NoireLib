using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace NoireLib.Remote;

// The directory read is cached for a moment. A loop of calls would enumerate the folder every call.
internal sealed class NoireRemoteResolver
{
    private readonly object gate = new();
    private IReadOnlyList<NoireRemoteInstanceRecord> cached = [];
    private DateTime cachedAtUtc = DateTime.MinValue;
    private string cachedDirectory = string.Empty;
    private readonly List<int> removedPids = [];

    public void Invalidate()
    {
        lock (gate)
        {
            cachedAtUtc = DateTime.MinValue;
        }
    }

    public IReadOnlyList<NoireRemoteInstance> Discover(NoireRemoteClientOptions options, string? endpointName)
    {
        var records = ReadRecords(options);
        var instances = new List<NoireRemoteInstance>();

        foreach (var record in records)
        {
            if (endpointName != null && !record.Publishes(endpointName))
                continue;

            if (options.ExcludeInstance is { } excluded && record.Instance == excluded)
                continue;

            instances.Add(new NoireRemoteInstance(record));
        }

        return instances;
    }

    public NoireRemoteInstance Resolve(
        NoireRemoteClientOptions options,
        string endpointName,
        Func<NoireRemoteInstance, bool>? filter,
        bool takeAny)
    {
        var matches = new List<NoireRemoteInstance>();

        foreach (var instance in Discover(options, endpointName))
        {
            if (filter == null || filter(instance))
                matches.Add(instance);
        }

        if (matches.Count == 1)
            return matches[0];

        if (matches.Count == 0)
            throw new NoireRemoteNotFoundException(BuildNotFoundMessage(options, endpointName, filter != null));

        if (takeAny)
            return MostRecent(matches);

        var message = new StringBuilder();
        message.Append(matches.Count.ToString(CultureInfo.InvariantCulture));
        message.Append(" live instances publish the HTTP endpoint '").Append(endpointName).Append("'.");
        message.Append(" Name one with Instance(), or take the most recently started one with Any().");

        foreach (var instance in matches)
            message.Append(Environment.NewLine).Append("  ").Append(instance);

        throw new NoireRemoteAmbiguousEndpointException(message.ToString(), matches);
    }

    private static NoireRemoteInstance MostRecent(List<NoireRemoteInstance> matches)
    {
        var best = matches[0];

        for (var index = 1; index < matches.Count; index++)
        {
            if (matches[index].StartedUtc > best.StartedUtc)
                best = matches[index];
        }

        return best;
    }

    private IReadOnlyList<NoireRemoteInstanceRecord> ReadRecords(NoireRemoteClientOptions options)
    {
        lock (gate)
        {
            var now = DateTime.UtcNow;

            if (cachedDirectory == options.RegistryDirectory && now - cachedAtUtc < options.RefreshInterval)
                return cached;

            removedPids.Clear();

            var live = new List<NoireRemoteInstanceRecord>();

            foreach (var record in NoireRemoteDirectory.ReadAll(options.RegistryDirectory))
            {
                var fresh = NoireRemoteDirectory.HeartbeatIsFresh(record, options.StaleAfter, now);
                var alive = !options.CheckProcessLiveness || NoireRemoteDirectory.ProcessIsAlive(record);

                if (fresh && alive)
                {
                    live.Add(record);
                    continue;
                }

                removedPids.Add(record.Pid);

                if (options.DeleteStaleRecords)
                    NoireRemoteDirectory.Delete(options.RegistryDirectory, record.Instance);
            }

            cached = live;
            cachedAtUtc = now;
            cachedDirectory = options.RegistryDirectory;

            return cached;
        }
    }

    private string BuildNotFoundMessage(NoireRemoteClientOptions options, string endpointName, bool filtered)
    {
        var message = new StringBuilder();
        message.Append("No plugin is publishing the HTTP endpoint '").Append(endpointName).Append("'.");
        message.Append(Environment.NewLine);
        message.Append("Looked in ").Append(options.RegistryDirectory).Append(" and found no live instance.");

        if (filtered)
        {
            message.Append(Environment.NewLine);
            message.Append("An instance filter was applied. A live instance may have been skipped by it.");
        }

        lock (gate)
        {
            if (removedPids.Count > 0)
            {
                message.Append(Environment.NewLine);
                message.Append("Removed ").Append(removedPids.Count.ToString(CultureInfo.InvariantCulture));
                message.Append(removedPids.Count == 1 ? " stale record (process " : " stale records (processes ");

                for (var index = 0; index < removedPids.Count; index++)
                {
                    if (index > 0)
                        message.Append(", ");

                    message.Append(removedPids[index].ToString(CultureInfo.InvariantCulture));
                }

                message.Append(").");
            }
        }

        return message.ToString();
    }
}
